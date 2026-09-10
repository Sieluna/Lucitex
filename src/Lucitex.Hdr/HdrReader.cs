using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Topology;

namespace Lucitex.Hdr;

internal sealed class HdrReader : IImageReader
{
    private readonly Stream _stream;
    private readonly HdrHeader _header;
    private readonly ImageAssetDescriptor _descriptor;
    private byte[]? _pixels;

    public HdrReader(Stream stream, DecodeLimits limits)
    {
        _stream = stream;
        _header = HdrHeaderIo.Read(stream, limits);
        _descriptor = HdrDescriptorMapper.ToDescriptor(_header);
        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("hdr", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        var decodedBytes = checked((long)_header.Width * _header.Height * 4);
        if (decodedBytes > limits.MaxDecodedBytes || decodedBytes > limits.MaxWorkingSet) {
            throw new ImageFormatException("hdr", "LimitExceeded", "Decoded HDR data exceeds the configured memory limit.");
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        try {
            EnsureDecoded();
        }
        catch (Exception exception) when (HdrFormatErrors.IsMalformed(exception)) {
            throw HdrFormatErrors.Wrap(exception, _stream);
        }

        if (region.Subresource.Part != 0 || region.Subresource.ArrayElement != 0 || region.Subresource.Face != 0 || region.Subresource.Level != LevelKey.Base) {
            throw new ArgumentOutOfRangeException(nameof(region), "HDR has one base-image subresource.");
        }

        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != _header.Width || region.Region.MinY < 0 || region.Region.MaxYExclusive > _header.Height) {
            throw new NotSupportedException("HDR reads require complete rows within the image bounds.");
        }

        var rowBytes = checked(_header.Width * 4);
        var byteCount = checked((int)region.Region.Height * rowBytes);
        if (destination.Length < byteCount) {
            throw new ArgumentException("Destination buffer is too small for the requested HDR rows.", nameof(destination));
        }

        _pixels!.AsSpan(checked((int)region.Region.MinY * rowBytes), byteCount).CopyTo(destination);
        return byteCount;
    }

    private void EnsureDecoded()
    {
        if (_pixels is not null) {
            return;
        }

        var rowBytes = checked(_header.Width * 4);
        var pixels = new byte[checked(rowBytes * _header.Height)];
        HdrRle.Decode(_stream, pixels, _header.Width, _header.Height);

        _pixels = pixels;
    }
}
