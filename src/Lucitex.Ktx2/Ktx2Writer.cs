using System.IO.Compression;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Ktx2.Format;

namespace Lucitex.Ktx2;

internal sealed class Ktx2Writer : IImageWriter
{
    private static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly Stream _stream;
    private readonly ImagePartDescriptor _part;
    private readonly Ktx2SupercompressionScheme _scheme;
    private readonly Ktx2Header _shape;
    private readonly int _itemCount;
    private readonly byte[][] _itemBuffers;
    private readonly long[] _levelExtentDepth;
    private bool _finished;

    public Ktx2Writer(Stream stream, ImageAssetDescriptor descriptor, Ktx2SupercompressionScheme scheme)
    {
        _stream = stream;
        _part = descriptor.Parts[0];
        _scheme = scheme;
        _shape = Ktx2DescriptorMapper.ToKtx2Header(_part, scheme, []);

        _itemCount = checked(_shape.EffectiveArrayElementCount * (int)_shape.FaceCount);
        var levelCount = _part.Topology.Levels.Count;
        _itemBuffers = new byte[_itemCount * levelCount][];
        _levelExtentDepth = new long[levelCount];

        for (var mip = 0; mip < levelCount; mip++) {
            var extent = _part.Topology.Levels[mip].Extent;
            _levelExtentDepth[mip] = extent.Depth;

            var sliceBytes = Ktx2FormatTable.SliceBytes(_shape.Format, extent.Width, extent.Height);
            var itemBytes = checked(sliceBytes * extent.Depth);

            for (var item = 0; item < _itemCount; item++) {
                _itemBuffers[(item * levelCount) + mip] = new byte[itemBytes];
            }
        }
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
        var mip = subresource.Level.X;
        var levelCount = _part.Topology.Levels.Count;
        if ((uint)mip >= levelCount) {
            throw new ArgumentOutOfRangeException(nameof(region), "KTX2 mip level is out of range.");
        }

        var item = ItemIndex(subresource.ArrayElement, subresource.Face);
        var buffer = _itemBuffers[(item * levelCount) + mip];
        data[..buffer.Length].CopyTo(buffer);
    }

    public void Finish()
    {
        if (_finished) {
            return;
        }

        var levelCount = _part.Topology.Levels.Count;
        var levelBlobs = new byte[levelCount][];

        for (var mip = 0; mip < levelCount; mip++) {
            using var buffer = new MemoryStream();
            for (var item = 0; item < _itemCount; item++) {
                buffer.Write(_itemBuffers[(item * levelCount) + mip]);
            }

            levelBlobs[mip] = buffer.ToArray();
        }

        var packedLevels = levelBlobs.Select(PackLevel).ToArray();

        var dfd = BuildDfd();
        var keyValues = Ktx2DescriptorMapper.ToKeyValueEntries(_part);
        var kvd = Ktx2KeyValueIo.Write(keyValues);

        const int headerSize = 12 + 36 + 32;
        var levelIndexSize = levelCount * 24;
        var dfdOffset = headerSize + levelIndexSize;
        var kvdOffset = dfdOffset + dfd.Length;
        var dataStart = Align8(kvdOffset + kvd.Length);

        var levelEntries = new Ktx2LevelIndexEntry[levelCount];
        var running = dataStart;
        for (var mip = 0; mip < levelCount; mip++) {
            var (packed, uncompressedLength) = packedLevels[mip];
            levelEntries[mip] = new Ktx2LevelIndexEntry((ulong)running, (ulong)packed.Length, (ulong)uncompressedLength);
            running += packed.Length;
        }

        var writer = new Ktx2BinaryWriter(_stream);
        writer.WriteBytes(Identifier);

        writer.WriteUInt32((uint)_shape.Format);
        writer.WriteUInt32(_shape.TypeSize);
        writer.WriteUInt32(_shape.PixelWidth);
        writer.WriteUInt32(_shape.PixelHeight);
        writer.WriteUInt32(_shape.PixelDepth);
        writer.WriteUInt32(_shape.LayerCount);
        writer.WriteUInt32(_shape.FaceCount);
        writer.WriteUInt32((uint)levelCount);
        writer.WriteUInt32((uint)_scheme);

        writer.WriteUInt32((uint)dfdOffset);
        writer.WriteUInt32((uint)dfd.Length);
        writer.WriteUInt32((uint)(kvd.Length > 0 ? kvdOffset : 0));
        writer.WriteUInt32((uint)kvd.Length);
        writer.WriteUInt64(0);
        writer.WriteUInt64(0);

        foreach (var entry in levelEntries) {
            writer.WriteUInt64(entry.ByteOffset);
            writer.WriteUInt64(entry.ByteLength);
            writer.WriteUInt64(entry.UncompressedByteLength);
        }

        writer.WriteBytes(dfd);
        writer.WriteBytes(kvd);

        var padding = dataStart - (kvdOffset + kvd.Length);
        if (padding > 0) {
            writer.WriteBytes(new byte[padding]);
        }

        foreach (var (packed, _) in packedLevels) {
            writer.WriteBytes(packed);
        }

        _finished = true;
    }

    private (byte[] Packed, long UncompressedLength) PackLevel(byte[] raw)
    {
        if (_scheme == Ktx2SupercompressionScheme.None) {
            return (raw, raw.Length);
        }

        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
            zlib.Write(raw);
        }

        return (output.ToArray(), raw.Length);
    }

    private byte[] BuildDfd()
    {
        if (_part.Representation is Core.Representation.EncodedElementRepresentation { Class: Core.Representation.EncodedElementClass.BlockCompressed }) {
            return Ktx2DfdWriter.Write(_shape.Format, null);
        }

        if (_part.Representation is Core.Representation.EncodedElementRepresentation { PackedLayout: { } packed }) {
            var channelsByName = _part.Channels.Channels.ToDictionary(channel => channel.Name.FullName);
            var fields = packed.Fields.Select(field => {
                var hasChannel = channelsByName.TryGetValue(field.Name, out var channel);
                return (
                    Name: field.Name,
                    BitLength: field.Bits,
                    Float: hasChannel && channel!.SampleType.Kind == Core.Sampling.ScalarKind.Float,
                    Signed: hasChannel && channel!.SampleType.Kind == Core.Sampling.ScalarKind.SignedInt);
            }).ToList();
            return Ktx2DfdWriter.Write(_shape.Format, fields);
        }

        var channels = _part.Channels.Channels
            .Select(c => (
                Name: c.Name.FullName,
                BitLength: (int)c.SampleType.Bits,
                Float: c.SampleType.Kind == Core.Sampling.ScalarKind.Float,
                Signed: c.SampleType.Kind is Core.Sampling.ScalarKind.SignedInt or Core.Sampling.ScalarKind.Float))
            .ToList();

        return Ktx2DfdWriter.Write(_shape.Format, channels);
    }

    private static int Align8(int value) => (value + 7) & ~7;

    private int ItemIndex(int arrayElement, int face) => checked((arrayElement * (int)_shape.FaceCount) + face);
}
