using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Webp.Lossless;
using Lucitex.Webp.Lossy;

namespace Lucitex.Webp;

internal sealed class WebpReader : IImageReader
{
    private static readonly Vector128<byte> s_LosslessRgbaShuffle = Vector128.Create(
        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);

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
        var sourceBytes = MemoryMarshal.AsBytes(pixels);
        var targetBytes = destination[..count];
        var i = 0;
        if (BitConverter.IsLittleEndian && Ssse3.IsSupported) {
            for (; i <= sourceBytes.Length - 16; i += 16) {
                Ssse3.Shuffle(Vector128.Create(sourceBytes.Slice(i, 16)), s_LosslessRgbaShuffle)
                    .CopyTo(targetBytes.Slice(i, 16));
            }
        }
        for (var pixel = i / 4; pixel < pixels.Length; pixel++) {
            var packed = pixels[pixel];
            targetBytes[pixel * 4] = (byte)(packed >> 16);
            targetBytes[(pixel * 4) + 1] = (byte)(packed >> 8);
            targetBytes[(pixel * 4) + 2] = (byte)packed;
            targetBytes[(pixel * 4) + 3] = (byte)(packed >> 24);
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
