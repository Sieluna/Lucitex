using System.Buffers;
using System.IO.Compression;
using System.Numerics;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Png.Format;
using Lucitex.Png.Filtering;

namespace Lucitex.Png;

internal sealed class PngWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly PngDocument _document;
    private byte[]? _pixelBuffer;
    private readonly int _rowStrideBytes;
    private readonly PngEncoderOptions _options;
    private bool _finished;

    public PngWriter(Stream stream, ImageAssetDescriptor descriptor, PngEncoderOptions? options = null)
    {
        _stream = stream;
        _options = options ?? new PngEncoderOptions();
        if (!Enum.IsDefined(_options.CompressionLevel) || (_options.Filter is { } filter && !Enum.IsDefined(filter))) {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        _document = PngDescriptorMapper.ToPngDocument(descriptor.Parts[0]);
        _rowStrideBytes = _document.Ihdr.RowByteLength(_document.Ihdr.Width);
        var pixelCount = checked(_rowStrideBytes * _document.Ihdr.Height);
        _pixelBuffer = ArrayPool<byte>.Shared.Rent(pixelCount);
        _pixelBuffer.AsSpan(0, pixelCount).Clear();
    }

    public WriterExecutionContract Contract { get; } = new() {
        RequiresDescriptorUpfront = true,
        RequiresDimensionsUpfront = true,
        WriteGranularity = new Extent3I(1, 1, 1),
        WriteOrder = WriteOrder.Arbitrary,
        RandomAccess = false,
        RequiresSeekableOutput = false,
        SupportsIncompleteLevels = false,
        SupportsSparseRegions = false,
    };

    public void Write(WorkRegion region, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_pixelBuffer is null, this);
        var width = _document.Ihdr.Width;

        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != width) {
            throw new NotSupportedException("Partial-row PNG writes are not supported yet.");
        }

        var startRow = (int)region.Region.MinY;
        var rowCount = (int)region.Region.Height;
        var byteCount = rowCount * _rowStrideBytes;

        data[..byteCount].CopyTo(_pixelBuffer.AsSpan(startRow * _rowStrideBytes, byteCount));
    }

    public void Finish()
    {
        if (_finished) {
            return;
        }

        ObjectDisposedException.ThrowIf(_pixelBuffer is null, this);
        try {
            PngDocumentWriter.WriteHeader(_stream, _document);
            var compressionLevel = _options.CompressionLevel;
            if (compressionLevel == CompressionLevel.Fastest
                && _document.Ihdr.BitDepth >= 8
                && _document.Ihdr.ColorType != PngColorType.Indexed
                && IsHighEntropy(_pixelBuffer, _rowStrideBytes, _document.Ihdr.Width, _document.Ihdr.Height, _document.Ihdr.BytesPerPixel)) {
                compressionLevel = CompressionLevel.NoCompression;
            }
            if (compressionLevel == CompressionLevel.NoCompression && _stream is MemoryStream memoryStream) {
                var rawBytes = ((long)_rowStrideBytes + 1) * _document.Ihdr.Height;
                var zlibBytes = rawBytes + (((rawBytes + 16382) / 16383) * 5) + 6;
                var capacity = memoryStream.Position + zlibBytes + (((zlibBytes + 65535) / 65536) * 12) + 12;
                if (capacity <= int.MaxValue && memoryStream.Capacity < capacity) {
                    try {
                        memoryStream.Capacity = (int)capacity;
                    }
                    catch (NotSupportedException) {
                        // A fixed-capacity stream may still fit the actual, smaller output.
                    }
                }
            }
            using (var chunks = new PngIdatStream(_stream))
            using (var zlib = new ZLibStream(chunks, compressionLevel, leaveOpen: true)) {
                var bestOwner = ArrayPool<byte>.Shared.Rent(_rowStrideBytes + 1);
                var candidateOwner = ArrayPool<byte>.Shared.Rent(_rowStrideBytes + 1);
                var best = bestOwner.AsMemory(0, _rowStrideBytes + 1);
                var candidate = candidateOwner.AsMemory(0, _rowStrideBytes + 1);
                try {
                    for (var y = 0; y < _document.Ihdr.Height; y++) {
                        var row = _pixelBuffer.AsSpan(y * _rowStrideBytes, _rowStrideBytes);
                        var previous = y == 0 ? ReadOnlySpan<byte>.Empty : _pixelBuffer.AsSpan((y - 1) * _rowStrideBytes, _rowStrideBytes);
                        var filter = _options.Filter ?? PngFilterType.None;
                        PngFilter.Apply(filter, best.Span[1..], row, previous, _document.Ihdr.BytesPerPixel);
                        if (compressionLevel != CompressionLevel.NoCompression && _options.Filter is null && _document.Ihdr.BitDepth >= 8 && _document.Ihdr.ColorType != PngColorType.Indexed) {
                            var bestScore = Score(best.Span[1..]);
                            for (var choice = PngFilterType.Sub; choice <= PngFilterType.Paeth && bestScore > 0; choice++) {
                                PngFilter.Apply(choice, candidate.Span[1..], row, previous, _document.Ihdr.BytesPerPixel);
                                var score = Score(candidate.Span[1..]);
                                if (score < bestScore) {
                                    bestScore = score;
                                    filter = choice;
                                    (best, candidate) = (candidate, best);
                                }
                            }
                        }
                        best.Span[0] = (byte)filter;
                        zlib.Write(best.Span);
                    }
                }
                finally {
                    ArrayPool<byte>.Shared.Return(bestOwner);
                    ArrayPool<byte>.Shared.Return(candidateOwner);
                }
            }
            PngDocumentWriter.WriteEnd(_stream);
            _finished = true;
        }
        finally {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_pixelBuffer is { } pixels) {
            _pixelBuffer = null;
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    private static long Score(ReadOnlySpan<byte> data)
    {
        long score = 0;
        var i = 0;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= data.Length - Vector<byte>.Count; i += Vector<byte>.Count) {
                var value = new Vector<byte>(data.Slice(i, Vector<byte>.Count));
                var magnitude = Vector.Min(value, Vector<byte>.Zero - value);
                Vector.Widen(magnitude, out var low, out var high);
                score += Vector.Sum(low) + Vector.Sum(high);
            }
        }
        for (; i < data.Length; i++) {
            score += Math.Min(data[i], 256 - data[i]);
        }
        return score;
    }

    private static bool IsHighEntropy(byte[] pixels, int rowStride, int width, int height, int bytesPerPixel)
    {
        if ((long)width * height < 65_536 || bytesPerPixel <= 0) {
            return false;
        }

        const int filterCount = 5;
        const int sampleRows = 16;
        const int sampleColumns = 128;
        Span<int> histogram = stackalloc int[filterCount * 256];
        var rowStep = Math.Max(1, (height + sampleRows - 1) / sampleRows);
        var columnStep = Math.Max(1, (width + sampleColumns - 1) / sampleColumns);
        var entropyTotal = 0d;
        var rowsMeasured = 0;

        for (var y = 0; y < height; y += rowStep) {
            histogram.Clear();
            var rowStart = y * rowStride;
            var previousStart = rowStart - rowStride;
            var rowSampleCount = 0;

            for (var x = 0; x < width; x += columnStep) {
                var pixelStart = rowStart + x * bytesPerPixel;
                for (var channel = 0; channel < bytesPerPixel; channel++) {
                    var index = pixelStart + channel;
                    var raw = pixels[index];
                    var left = x > 0 ? pixels[index - bytesPerPixel] : 0;
                    var up = y > 0 ? pixels[previousStart + x * bytesPerPixel + channel] : 0;
                    var upperLeft = x > 0 && y > 0 ? pixels[previousStart + x * bytesPerPixel + channel - bytesPerPixel] : 0;
                    histogram[raw]++;
                    histogram[256 + unchecked((byte)(raw - left))]++;
                    histogram[512 + unchecked((byte)(raw - up))]++;
                    histogram[768 + unchecked((byte)(raw - ((left + up) >> 1)))]++;
                    histogram[1024 + unchecked((byte)(raw - Paeth(left, up, upperLeft)))]++;
                    rowSampleCount++;
                }
            }

            var bestEntropy = double.MaxValue;
            for (var filter = 0; filter < filterCount; filter++) {
                var entropy = 0d;
                var offset = filter * 256;
                for (var value = 0; value < 256; value++) {
                    var count = histogram[offset + value];
                    if (count == 0) {
                        continue;
                    }
                    var probability = (double)count / rowSampleCount;
                    entropy -= probability * Math.Log2(probability);
                }
                bestEntropy = Math.Min(bestEntropy, entropy);
            }
            entropyTotal += bestEntropy;
            rowsMeasured++;
        }

        return rowsMeasured > 0 && entropyTotal / rowsMeasured >= 5.0;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= upDistance && leftDistance <= upperLeftDistance
            ? left
            : upDistance <= upperLeftDistance ? up : upperLeft;
    }
}
