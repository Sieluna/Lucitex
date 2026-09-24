using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Webp.Lossless;

namespace Lucitex.Webp;

internal sealed class WebpWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly WebpEncoderOptions _options;
    private readonly WebpMemory _memory;
    private readonly WebpBuffer<uint> _pixels;
    private readonly WebpBuffer<byte> _writtenRows;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly WebpMetadata _metadata;
    private bool _finished;
    private bool _faulted;
    private bool _disposed;

    public WebpWriter(Stream stream, ImageAssetDescriptor descriptor, WebpEncoderOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);
        if (!stream.CanWrite) {
            throw new ArgumentException("Output stream must be writable.", nameof(stream));
        }
        if (!Enum.IsDefined(options.Effort) || options.MaxWorkingSet <= 0) {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        (_width, _height, _channels, _metadata) = WebpDescriptorMapper.Validate(descriptor);
        _stream = stream;
        _options = options;
        _memory = new WebpMemory(options.MaxWorkingSet);
        _pixels = _memory.Rent<uint>(checked(_width * _height));
        try {
            _writtenRows = _memory.Rent<byte>(_height);
            _writtenRows.Span.Clear();
        }
        catch {
            _pixels.Dispose();
            throw;
        }
        Contract = new WriterExecutionContract {
            WriteGranularity = new Extent3I(_width, 1, 1),
            WriteOrder = WriteOrder.Arbitrary,
        };
    }

    public WriterExecutionContract Contract { get; }

    public void Write(WorkRegion region, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished || _faulted) {
            throw new InvalidOperationException("The WebP writer is already finished or faulted.");
        }
        var count = WebpDescriptorMapper.ValidateRows(region, _width, _height, _channels);
        if (data.Length < count) {
            throw new ArgumentException("Source is too short for the requested region.", nameof(data));
        }
        var start = (int)region.Region.MinY;
        var rows = (int)region.Region.Height;
        var pixels = _pixels.Span.Slice(start * _width, count / _channels);
        for (var i = 0; i < pixels.Length; i++) {
            var offset = i * _channels;
            var alpha = _channels == 4 ? data[offset + 3] : 255u;
            pixels[i] = (alpha << 24) | ((uint)data[offset] << 16) | ((uint)data[offset + 1] << 8) | data[offset + 2];
        }
        _writtenRows.Span.Slice(start, rows).Fill(1);
    }

    public void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finished) {
            return;
        }
        if (_faulted) {
            throw new InvalidOperationException("The WebP writer is faulted.");
        }
        if (_writtenRows.Span.Contains((byte)0)) {
            throw new InvalidOperationException("Every WebP image row must be written before finishing.");
        }
        try {
            Vp8LEncoder.Encode(_stream, _pixels.Span, _width, _height, _options, _memory, _metadata);
            _finished = true;
        }
        catch {
            _faulted = true;
            throw;
        }
        finally {
            _pixels.Dispose();
            _writtenRows.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _pixels.Dispose();
        _writtenRows.Dispose();
    }
}
