using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Hdr;

internal sealed class HdrWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly int _width;
    private readonly int _height;
    private readonly LogicalOrientation _orientation;
    private readonly byte[] _pixels;
    private bool _finished;

    public HdrWriter(Stream stream, ImageAssetDescriptor descriptor)
    {
        _stream = stream;
        (_width, _height, _orientation) = HdrDescriptorMapper.ValidateForWriting(descriptor);
        _pixels = new byte[checked(_width * _height * 4)];
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
        if (region.Subresource.Part != 0 || region.Subresource.ArrayElement != 0 || region.Subresource.Face != 0 || region.Subresource.Level != LevelKey.Base) {
            throw new ArgumentOutOfRangeException(nameof(region), "HDR has one base-image subresource.");
        }

        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != _width || region.Region.MinY < 0 || region.Region.MaxYExclusive > _height) {
            throw new NotSupportedException("HDR writes require complete rows within the image bounds.");
        }

        var rowBytes = checked(_width * 4);
        var byteCount = checked((int)region.Region.Height * rowBytes);
        if (data.Length < byteCount) {
            throw new ArgumentException("Source buffer is too small for the requested HDR rows.", nameof(data));
        }

        data[..byteCount].CopyTo(_pixels.AsSpan(checked((int)region.Region.MinY * rowBytes), byteCount));
    }

    public void Finish()
    {
        if (_finished) {
            return;
        }

        HdrHeaderIo.Write(_stream, _width, _height, _orientation);
        HdrRle.Encode(_stream, _pixels, _width, _height);

        _finished = true;
    }
}
