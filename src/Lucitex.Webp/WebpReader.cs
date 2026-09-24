using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Webp.Lossless;

namespace Lucitex.Webp;

internal sealed class WebpReader : IImageReader
{
    private readonly WebpDocument _document;
    private readonly ImageAssetDescriptor _descriptor;
    private readonly WebpMemory _memory;
    private WebpBuffer<uint>? _pixels;
    private bool _disposed;

    public WebpReader(Stream stream, DecodeLimits limits)
    {
        _memory = new WebpMemory(limits.MaxWorkingSet);
        _document = WebpContainer.Read(stream, limits, _memory);
        try {
            _descriptor = WebpDescriptorMapper.Describe(_document);
            var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
            if (violations.Count != 0) {
                throw new ImageFormatException("webp", "LimitExceeded", string.Join("; ", violations.Select(violation => violation.Message)));
            }
        }
        catch {
            _document.Payload.Dispose();
            throw;
        }
    }

    public ImageAssetDescriptor Describe()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _descriptor;
    }

    public int Read(WorkRegion region, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var count = WebpDescriptorMapper.ValidateRows(region, _document.Width, _document.Height, 4);
        if (destination.Length < count) {
            throw new ArgumentException("Destination is too short for the requested region.", nameof(destination));
        }
        if (count == 0) {
            return 0;
        }
        if (_pixels is null) {
            try {
                _pixels = Vp8LDecoder.Decode(_document.Payload.Span, _document.Width, _document.Height, _memory);
                _document.Payload.Dispose();
            }
            catch (Exception exception) when (WebpFormatErrors.IsMalformed(exception)) {
                throw WebpFormatErrors.Wrap(exception);
            }
        }
        var pixels = _pixels.Span.Slice((int)region.Region.MinY * _document.Width, count / 4);
        for (var i = 0; i < pixels.Length; i++) {
            var pixel = pixels[i];
            destination[i * 4] = (byte)(pixel >> 16);
            destination[(i * 4) + 1] = (byte)(pixel >> 8);
            destination[(i * 4) + 2] = (byte)pixel;
            destination[(i * 4) + 3] = (byte)(pixel >> 24);
        }
        return count;
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _document.Payload.Dispose();
        _pixels?.Dispose();
        _pixels = null;
    }
}
