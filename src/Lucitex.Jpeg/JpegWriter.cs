using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Jpeg.Encoding;

namespace Lucitex.Jpeg;

internal sealed class JpegWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly int _componentCount;
    private readonly JpegEncoderOptions _options;
    private readonly byte[] _pixelBuffer;
    private readonly int _rowStrideBytes;
    private bool _finished;

    public JpegWriter(Stream stream, ImageAssetDescriptor descriptor, JpegEncoderOptions? options = null)
    {
        _stream = stream;
        (_componentCount, _width, _height) = JpegDescriptorMapper.ToJpegEncodeParams(descriptor.Parts[0]);
        _options = options ?? new JpegEncoderOptions();
        if (_options.Quality is < 1 or > 100 || !Enum.IsDefined(_options.ChromaSubsampling)) {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        _rowStrideBytes = _width * _componentCount;
        _pixelBuffer = new byte[checked((long)_rowStrideBytes * _height)];
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
        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != _width) {
            throw new NotSupportedException("Partial-row JPEG writes are not supported yet.");
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

        JpegEncoder.Encode(_stream, _pixelBuffer, _width, _height, _componentCount, _options);
        _finished = true;
    }
}
