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
    private readonly byte[] _pixelBuffer;
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
        _pixelBuffer = new byte[checked((long)_rowStrideBytes * _document.Ihdr.Height)];
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

        PngDocumentWriter.WriteHeader(_stream, _document);
        using (var chunks = new PngIdatStream(_stream))
        using (var zlib = new ZLibStream(chunks, _options.CompressionLevel, leaveOpen: true)) {
            var best = new byte[_rowStrideBytes + 1];
            var candidate = new byte[_rowStrideBytes + 1];
            for (var y = 0; y < _document.Ihdr.Height; y++) {
                var row = _pixelBuffer.AsSpan(y * _rowStrideBytes, _rowStrideBytes);
                var previous = y == 0 ? ReadOnlySpan<byte>.Empty : _pixelBuffer.AsSpan((y - 1) * _rowStrideBytes, _rowStrideBytes);
                var filter = _options.Filter ?? PngFilterType.None;
                PngFilter.Apply(filter, best.AsSpan(1), row, previous, _document.Ihdr.BytesPerPixel);
                if (_options.Filter is null && _document.Ihdr.BitDepth >= 8 && _document.Ihdr.ColorType != PngColorType.Indexed) {
                    var bestScore = Score(best.AsSpan(1));
                    for (var choice = PngFilterType.Sub; choice <= PngFilterType.Paeth && bestScore > 0; choice++) {
                        PngFilter.Apply(choice, candidate.AsSpan(1), row, previous, _document.Ihdr.BytesPerPixel);
                        var score = Score(candidate.AsSpan(1));
                        if (score < bestScore) {
                            bestScore = score;
                            filter = choice;
                            (best, candidate) = (candidate, best);
                        }
                    }
                }
                best[0] = (byte)filter;
                zlib.Write(best);
            }
        }
        PngDocumentWriter.WriteEnd(_stream);
        _finished = true;
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
}
