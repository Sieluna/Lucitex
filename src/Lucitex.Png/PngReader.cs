using System.Buffers;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Png.Decoding;
using Lucitex.Png.Format;

namespace Lucitex.Png;

internal sealed class PngReader : IImageReader
{
    private readonly ImageAssetDescriptor _descriptor;
    private readonly PngDocument _document;
    private IReadOnlyList<ReadOnlyMemory<byte>> _compressedChunks;
    private readonly PngIdatBuffers _compressedBuffers = new();
    private readonly DecodeLimits _limits;
    private readonly int _rowStrideBytes;
    private byte[]? _decodedPixels;
    private bool _disposed;

    public PngReader(Stream stream, DecodeLimits limits)
    {
        _limits = limits;
        try {
            (_document, _compressedChunks) = PngDocumentReader.Read(stream, limits, _compressedBuffers);
            _descriptor = PngDescriptorMapper.ToImageAssetDescriptor(_document);

            var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
            if (violations.Count > 0) {
                throw new ImageFormatException("png", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
            }
            _rowStrideBytes = _document.Ihdr.RowByteLength(_document.Ihdr.Width);
        }
        catch {
            _compressedBuffers.Dispose();
            throw;
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (region.Region.MinY < 0 || region.Region.MaxYExclusive > _document.Ihdr.Height || region.Region.Height < 0) {
            throw new ArgumentOutOfRangeException(nameof(region));
        }
        if (destination.Length < region.Region.Height * _rowStrideBytes) {
            throw new ArgumentException("Destination is too short for the requested region.", nameof(destination));
        }
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

        var length = checked((int)totalBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try {
            if (ihdr.Interlace != PngInterlaceMethod.None) {
                buffer.AsSpan(0, length).Clear();
            }
            PngScanlineDecoder.Decode(ihdr, _compressedChunks, buffer);
            _decodedPixels = buffer;
            _compressedChunks = [];
            _compressedBuffers.Dispose();
        }
        catch {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _compressedChunks = [];
        _compressedBuffers.Dispose();
        if (_decodedPixels is { } buffer) {
            _decodedPixels = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
