using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Jpeg.Decoding;

namespace Lucitex.Jpeg;

internal sealed class JpegReader : IImageReader
{
    private readonly ImageAssetDescriptor _descriptor;
    private readonly JpegDecoder _decoder;
    private readonly DecodeLimits _limits;
    private readonly int _rowStrideBytes;
    private byte[]? _decodedPixels;

    public JpegReader(Stream stream, DecodeLimits limits)
    {
        _limits = limits;
        _decoder = new JpegDecoder(stream);

        try {
            _decoder.ParseHeader();
        }
        catch (Exception exception) when (JpegFormatErrors.IsMalformed(exception)) {
            throw JpegFormatErrors.Wrap(exception, stream);
        }

        _descriptor = JpegDescriptorMapper.ToImageAssetDescriptor(_decoder);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("jpeg", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _rowStrideBytes = _decoder.Frame.Width * _decoder.Frame.Components.Count;
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
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

        _decoder.DecodeScans();
        _decodedPixels = JpegPixelAssembler.Reconstruct(_decoder);
    }
}
