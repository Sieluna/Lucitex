using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Jpeg.Decoding;
using System.Buffers;

namespace Lucitex.Jpeg;

internal sealed class JpegReader : IImageReader
{
    private readonly ImageAssetDescriptor _descriptor;
    private readonly JpegDecoder _decoder;
    private readonly DecodeLimits _limits;
    private readonly int _rowStrideBytes;
    private readonly JpegDecoderOptions _options;
    private byte[]? _decodedPixels;
    private bool _disposed;

    public JpegReader(Stream stream, DecodeLimits limits, JpegDecoderOptions? options = null)
    {
        _limits = limits;
        _options = options ?? new JpegDecoderOptions();
        _decoder = new JpegDecoder(stream);

        try {
            _decoder.ParseHeader();
            _descriptor = JpegDescriptorMapper.ToImageAssetDescriptor(_decoder);
            var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
            if (violations.Count > 0) {
                throw new ImageFormatException("jpeg", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
            }
            _rowStrideBytes = _decoder.Frame.Width * _decoder.Frame.Components.Count;
        }
        catch {
            _decoder.ReleaseBuffers();
            throw;
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (region.Region.MinY < 0 || region.Region.MaxYExclusive > _decoder.Frame.Height || region.Region.Height < 0) {
            throw new ArgumentOutOfRangeException(nameof(region));
        }
        if (destination.Length < region.Region.Height * _rowStrideBytes) {
            throw new ArgumentException("Destination is too short for the requested region.", nameof(destination));
        }
        try {
            EnsureDecoded();
        }
        catch (Exception exception) when (JpegFormatErrors.IsMalformed(exception)) {
            throw JpegFormatErrors.Wrap(exception, Stream.Null);
        }

        var width = _decoder.Frame.Width;
        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != width) {
            throw new NotSupportedException("Partial-row JPEG reads are not supported yet.");
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

        var totalBytes = checked((long)_rowStrideBytes * _decoder.Frame.Height);
        if (totalBytes > _limits.MaxDecodedBytes) {
            throw new ImageFormatException("jpeg", "LimitExceeded", $"Decoded size {totalBytes} exceeds MaxDecodedBytes limit of {_limits.MaxDecodedBytes}.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(checked((int)totalBytes));
        try {
            _decoder.DecodeScans();
            JpegPixelAssembler.Reconstruct(_decoder, buffer, _options.InterpolateChroma);
            _decodedPixels = buffer;
        }
        catch {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
        finally {
            _decoder.ReleaseBuffers();
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _decoder.ReleaseBuffers();
        if (_decodedPixels is { } buffer) {
            _decodedPixels = null;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
