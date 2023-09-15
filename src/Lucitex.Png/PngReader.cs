using System.IO.Compression;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Png;

internal sealed class PngReader : IImageReader
{
    private readonly ImageAssetDescriptor _descriptor;
    private readonly PngDocument _document;
    private readonly byte[] _compressedIdat;
    private readonly DecodeLimits _limits;
    private readonly int _rowStrideBytes;
    private byte[]? _decodedPixels;

    public PngReader(Stream stream, DecodeLimits limits)
    {
        _limits = limits;
        (_document, _compressedIdat) = PngDocumentReader.Read(stream, limits);
        _descriptor = PngDescriptorMapper.ToImageAssetDescriptor(_document);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("png", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _rowStrideBytes = _document.Ihdr.RowByteLength(_document.Ihdr.Width);
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        try {
            EnsureDecoded();
        }
        catch (Exception exception) when (PngFormatErrors.IsMalformed(exception)) {
            throw PngFormatErrors.Wrap(exception, Stream.Null);
        }

        var width = _document.Ihdr.Width;

        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != width) {
            throw new NotSupportedException("Partial-row PNG reads are not supported yet.");
        }

        var startRow = (int)region.Region.MinY;
        var rowCount = (int)region.Region.Height;
        var byteCount = rowCount * _rowStrideBytes;

        _decodedPixels!.AsSpan(startRow * _rowStrideBytes, byteCount).CopyTo(destination);
        return byteCount;
    }

    private void EnsureDecoded()
    {
        if (_decodedPixels is not null) {
            return;
        }

        var ihdr = _document.Ihdr;
        var totalBytes = checked((long)_rowStrideBytes * ihdr.Height);
        if (totalBytes > _limits.MaxDecodedBytes) {
            throw new ImageFormatException("png", "LimitExceeded", $"Decoded size {totalBytes} exceeds MaxDecodedBytes limit of {_limits.MaxDecodedBytes}.");
        }

        var buffer = new byte[totalBytes];

        using var input = new MemoryStream(_compressedIdat, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        if (ihdr.Interlace == PngInterlaceMethod.None) {
            DecodeNonInterlaced(ihdr, zlib, buffer);
        }
        else {
            DecodeAdam7(ihdr, zlib, buffer);
        }

        if (zlib.ReadByte() != -1) {
            throw new ImageFormatException("png", "BadImageData", "Inflated PNG image data is longer than expected.");
        }

        _decodedPixels = buffer;
    }

    private static void DecodeNonInterlaced(PngIhdr ihdr, Stream inflated, byte[] buffer)
    {
        var rowBytes = ihdr.RowByteLength(ihdr.Width);
        var bpp = ihdr.BytesPerPixel;

        for (var y = 0; y < ihdr.Height; y++) {
            var filterType = (PngFilterType)inflated.ReadByte();
            var row = buffer.AsSpan(y * rowBytes, rowBytes);
            inflated.ReadExactly(row);

            var previous = y > 0 ? buffer.AsSpan((y - 1) * rowBytes, rowBytes) : ReadOnlySpan<byte>.Empty;
            PngFilter.Reconstruct(filterType, row, previous, bpp);
        }
    }

    private static void DecodeAdam7(PngIhdr ihdr, Stream inflated, byte[] buffer)
    {
        var samplesPerPixel = ihdr.SamplesPerPixel;
        var bitDepth = ihdr.BitDepth;
        var bpp = ihdr.BytesPerPixel;
        var finalRowBytes = ihdr.RowByteLength(ihdr.Width);

        for (var passIndex = 0; passIndex < 7; passIndex++) {
            var (passWidth, passHeight) = Adam7.PassDimensions(ihdr.Width, ihdr.Height, passIndex);
            if (passWidth == 0 || passHeight == 0) {
                continue;
            }

            var (xStart, yStart, xStep, yStep) = Adam7.Passes[passIndex];
            var passRowBytes = ihdr.RowByteLength(passWidth);
            var previousPassRow = new byte[passRowBytes];
            var currentRow = new byte[passRowBytes];
            var hasPreviousRow = false;

            for (var py = 0; py < passHeight; py++) {
                var filterType = (PngFilterType)inflated.ReadByte();
                inflated.ReadExactly(currentRow);

                PngFilter.Reconstruct(filterType, currentRow, hasPreviousRow ? previousPassRow : ReadOnlySpan<byte>.Empty, bpp);

                var y = yStart + (py * yStep);
                var finalRow = buffer.AsSpan(y * finalRowBytes, finalRowBytes);

                for (var px = 0; px < passWidth; px++) {
                    var x = xStart + (px * xStep);
                    for (var s = 0; s < samplesPerPixel; s++) {
                        var value = PngBitPacking.ReadSample(currentRow, (px * samplesPerPixel) + s, bitDepth);
                        PngBitPacking.WriteSample(finalRow, (x * samplesPerPixel) + s, bitDepth, value);
                    }
                }

                (previousPassRow, currentRow) = (currentRow, previousPassRow);
                hasPreviousRow = true;
            }
        }
    }
}
