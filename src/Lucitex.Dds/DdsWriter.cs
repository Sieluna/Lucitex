using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Dds.Format;

namespace Lucitex.Dds;

internal sealed class DdsWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly DdsHeader _header;
    private readonly int _itemCount;
    private readonly long[] _levelByteSizes;
    private readonly byte[][] _levelBuffers;
    private bool _finished;

    public DdsWriter(Stream stream, ImageAssetDescriptor descriptor)
    {
        _stream = stream;
        var part = descriptor.Parts[0];
        _header = DdsDescriptorMapper.ToDdsHeader(part);

        _itemCount = checked((int)_header.ArraySize * (_header.IsCubemap ? 6 : 1));

        _levelByteSizes = new long[_header.MipMapCount];
        var w = (long)_header.Width;
        var h = (long)_header.Height;
        var d = _header.Dimension == D3d10ResourceDimension.Texture3D ? (long)_header.Depth : 1;

        for (var mip = 0; mip < _header.MipMapCount; mip++) {
            var sliceBytes = DdsFormatTable.SliceBytes(_header.Format, w, h);
            _levelByteSizes[mip] = checked(sliceBytes * d);

            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            d = Math.Max(1, d / 2);
        }

        _levelBuffers = new byte[_itemCount * _header.MipMapCount][];
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
        var subresource = region.Subresource;
        var item = ItemIndex(subresource.ArrayElement, subresource.Face);
        var mip = subresource.Level.X;

        if ((uint)mip >= _header.MipMapCount) {
            throw new ArgumentOutOfRangeException(nameof(region), "DDS mip level is out of range.");
        }

        var levelBytes = _levelByteSizes[mip];
        var buffer = new byte[levelBytes];
        data[..(int)levelBytes].CopyTo(buffer);
        _levelBuffers[(item * _header.MipMapCount) + mip] = buffer;
    }

    public void Finish()
    {
        if (_finished) {
            return;
        }

        var binaryWriter = new DdsBinaryWriter(_stream);
        DdsHeaderWriter.Write(binaryWriter, _header);

        for (var item = 0; item < _itemCount; item++) {
            for (var mip = 0; mip < _header.MipMapCount; mip++) {
                var index = (item * _header.MipMapCount) + mip;
                var buffer = _levelBuffers[index] ?? new byte[_levelByteSizes[mip]];
                binaryWriter.WriteBytes(buffer);
            }
        }

        _finished = true;
    }

    private int ItemIndex(int arrayElement, int face) => _header.IsCubemap
        ? checked((arrayElement * 6) + face)
        : arrayElement;
}
