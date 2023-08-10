using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Dds.Format;

namespace Lucitex.Dds;

internal sealed class DdsReader : IImageReader
{
    private readonly Stream _stream;
    private readonly DdsHeader _header;
    private readonly ImageAssetDescriptor _descriptor;
    private readonly long _dataStart;
    private readonly int _itemCount;
    private readonly long[] _levelByteSizes;
    private readonly long[] _levelOffsetsWithinItem;
    private readonly long _itemByteSize;

    public DdsReader(Stream stream, DecodeLimits limits)
    {
        if (!stream.CanSeek) {
            throw new ArgumentException("DDS reader requires a seekable stream.", nameof(stream));
        }

        _stream = stream;
        var binaryReader = new DdsBinaryReader(stream, checked((int)Math.Min(limits.MaxWorkingSet, int.MaxValue)));
        _header = DdsHeaderReader.Read(binaryReader);
        _descriptor = DdsDescriptorMapper.ToImageAssetDescriptor(_header);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("dds", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _itemCount = checked((int)_header.ArraySize * (_header.IsCubemap ? 6 : 1));

        _levelByteSizes = new long[_header.MipMapCount];
        _levelOffsetsWithinItem = new long[_header.MipMapCount];
        long running = 0;
        var w = (long)_header.Width;
        var h = (long)_header.Height;
        var d = _header.Dimension == D3d10ResourceDimension.Texture3D ? (long)_header.Depth : 1;

        for (var mip = 0; mip < _header.MipMapCount; mip++) {
            var sliceBytes = DdsFormatTable.SliceBytes(_header.Format, w, h);
            var levelBytes = checked(sliceBytes * d);
            _levelByteSizes[mip] = levelBytes;
            _levelOffsetsWithinItem[mip] = running;
            running = checked(running + levelBytes);

            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            d = Math.Max(1, d / 2);
        }

        _itemByteSize = running;

        var totalBytes = checked(_itemByteSize * _itemCount);
        if (totalBytes > limits.MaxDecodedBytes) {
            throw new ImageFormatException("dds", "LimitExceeded", $"DDS pixel data is {totalBytes} bytes, exceeding MaxDecodedBytes.");
        }

        _dataStart = stream.Position;
        if (stream.Length - _dataStart < totalBytes) {
            throw new ImageFormatException("dds", "TruncatedData", "DDS stream is shorter than the pixel data declared by its header.");
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        var subresource = region.Subresource;
        var item = ItemIndex(subresource.ArrayElement, subresource.Face);
        var mip = subresource.Level.X;

        if ((uint)mip >= _header.MipMapCount) {
            throw new ArgumentOutOfRangeException(nameof(region), "DDS mip level is out of range.");
        }

        var level = _descriptor.Parts[0].Topology.Levels[mip];
        var expectedRegion = Core.Spatial.ImageBox.FromOrigin(level.Extent.Width, level.Extent.Height);
        if (region.Region.MinX != expectedRegion.MinX || region.Region.MinY != expectedRegion.MinY ||
            region.Region.MaxXExclusive != expectedRegion.MaxXExclusive || region.Region.MaxYExclusive != expectedRegion.MaxYExclusive) {
            throw new NotSupportedException("Partial DDS subresource reads are not supported yet.");
        }

        var levelBytes = _levelByteSizes[mip];
        if (destination.Length < levelBytes) {
            throw new ArgumentException("Destination buffer is too small for this DDS subresource.", nameof(destination));
        }

        var offset = _dataStart + (item * _itemByteSize) + _levelOffsetsWithinItem[mip];
        _stream.Position = offset;
        _stream.ReadExactly(destination[..(int)levelBytes]);

        return (int)levelBytes;
    }

    private int ItemIndex(int arrayElement, int face) => _header.IsCubemap
        ? checked((arrayElement * 6) + face)
        : arrayElement;
}
