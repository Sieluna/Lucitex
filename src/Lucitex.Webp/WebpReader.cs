using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Webp.Lossless;
using Lucitex.Webp.Lossy;

namespace Lucitex.Webp;

internal sealed class WebpReader : IImageReader
{
    private readonly WebpDocument _document;
    private readonly ImageAssetDescriptor _descriptor;
    private readonly WebpMemory _memory;
    private WebpBuffer<uint>? _pixels;
    private Vp8DecodedFrame? _lossyFrame;
    private byte[]? _alpha;
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
            _document.AlphaPayload?.Dispose();
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
        EnsureDecoded();

        var startRow = (int)region.Region.MinY;
        var rows = count / 4 / _document.Width;
        if (_lossyFrame is { } frame) {
            for (var r = 0; r < rows; r++) {
                var row = startRow + r;
                var alphaRow = _alpha is null ? ReadOnlySpan<byte>.Empty : _alpha.AsSpan(row * _document.Width, _document.Width);
                Vp8YuvToRgb.ConvertRowToRgba(frame, row, destination.Slice(r * _document.Width * 4, _document.Width * 4), alphaRow);
            }
            return count;
        }

        var pixels = _pixels!.Span.Slice(startRow * _document.Width, count / 4);
        for (var i = 0; i < pixels.Length; i++) {
            var pixel = pixels[i];
            destination[i * 4] = (byte)(pixel >> 16);
            destination[(i * 4) + 1] = (byte)(pixel >> 8);
            destination[(i * 4) + 2] = (byte)pixel;
            destination[(i * 4) + 3] = (byte)(pixel >> 24);
        }
        return count;
    }

    private void EnsureDecoded()
    {
        if (_pixels is not null || _lossyFrame is not null) {
            return;
        }
        try {
            if (_document.IsLossy) {
                _lossyFrame = Vp8Decoder.Decode(_document.Payload.Span);
                if (_document.AlphaPayload is { } alphaChunk) {
                    _alpha = WebpAlpha.Decode(alphaChunk.Span, _document.Width, _document.Height, _memory);
                }
            }
            else {
                _pixels = Vp8LDecoder.Decode(_document.Payload.Span, _document.Width, _document.Height, _memory);
            }
        }
        catch (Exception exception) when (WebpFormatErrors.IsMalformed(exception)) {
            throw WebpFormatErrors.Wrap(exception);
        }
        finally {
            _document.Payload.Dispose();
            _document.AlphaPayload?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _document.Payload.Dispose();
        _document.AlphaPayload?.Dispose();
        _pixels?.Dispose();
        _pixels = null;
    }
}
