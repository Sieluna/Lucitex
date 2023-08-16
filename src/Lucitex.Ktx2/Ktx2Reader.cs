using System.IO.Compression;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Ktx2.Format;

namespace Lucitex.Ktx2;

internal sealed class Ktx2Reader : IImageReader
{
    private static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly Stream _stream;
    private readonly Ktx2Header _header;
    private readonly ImageAssetDescriptor _descriptor;
    private readonly DecodeLimits _limits;
    private readonly int _itemCount;
    private readonly byte[]?[] _decodedLevels;

    public Ktx2Reader(Stream stream, DecodeLimits limits)
    {
        if (!stream.CanSeek) {
            throw new ArgumentException("KTX2 reader requires a seekable stream.", nameof(stream));
        }

        _stream = stream;
        _limits = limits;

        var reader = new Ktx2BinaryReader(stream, checked((int)Math.Min(limits.MaxWorkingSet, int.MaxValue)));

        Span<byte> identifier = stackalloc byte[12];
        stream.ReadExactly(identifier);
        if (!identifier.SequenceEqual(Identifier)) {
            throw new ImageFormatException("ktx2", "BadMagic", "Stream does not start with the KTX2 identifier.");
        }

        var format = (VkFormat)reader.ReadUInt32();
        var typeSize = reader.ReadUInt32();
        var pixelWidth = reader.ReadUInt32();
        var pixelHeight = reader.ReadUInt32();
        var pixelDepth = reader.ReadUInt32();
        var layerCount = reader.ReadUInt32();
        var faceCount = reader.ReadUInt32();
        var levelCount = reader.ReadUInt32();
        var supercompressionScheme = (Ktx2SupercompressionScheme)reader.ReadUInt32();

        if (pixelWidth == 0 || pixelHeight == 0) {
            throw new ImageFormatException("ktx2", "BadHeader", "KTX2 pixel width and height must be positive.");
        }

        if (levelCount == 0 || levelCount > limits.MaxLevels) {
            throw new ImageFormatException("ktx2", "LimitExceeded", $"KTX2 level count {levelCount} is outside the supported limits.");
        }

        var dfdByteOffset = reader.ReadUInt32();
        var dfdByteLength = reader.ReadUInt32();
        var kvdByteOffset = reader.ReadUInt32();
        var kvdByteLength = reader.ReadUInt32();
        reader.ReadUInt64();
        reader.ReadUInt64();

        var levels = new List<Ktx2LevelIndexEntry>((int)levelCount);
        for (var i = 0; i < levelCount; i++) {
            var byteOffset = reader.ReadUInt64();
            var byteLength = reader.ReadUInt64();
            var uncompressedByteLength = reader.ReadUInt64();
            levels.Add(new Ktx2LevelIndexEntry(byteOffset, byteLength, uncompressedByteLength));
        }

        _header = new Ktx2Header {
            Format = format,
            TypeSize = typeSize,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            PixelDepth = pixelDepth,
            LayerCount = layerCount,
            FaceCount = Math.Max(1, faceCount),
            Levels = levels,
            SupercompressionScheme = supercompressionScheme,
        };

        var keyValues = ReadKeyValueData(stream, reader, dfdByteOffset, dfdByteLength, kvdByteOffset, kvdByteLength);
        _descriptor = Ktx2DescriptorMapper.ToImageAssetDescriptor(_header, keyValues);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("ktx2", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _itemCount = checked(_header.EffectiveArrayElementCount * (int)_header.FaceCount);
        _decodedLevels = new byte[]?[levelCount];

        foreach (var level in levels) {
            if (level.ByteOffset > (ulong)stream.Length || level.ByteLength > (ulong)stream.Length - level.ByteOffset) {
                throw new ImageFormatException("ktx2", "BadLevelOffset", "KTX2 level data is outside the stream bounds.");
            }

            if (level.UncompressedByteLength > (ulong)limits.MaxDecodedBytes) {
                throw new ImageFormatException("ktx2", "LimitExceeded", "KTX2 level exceeds MaxDecodedBytes.");
            }
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        var subresource = region.Subresource;
        var mip = subresource.Level.X;
        if ((uint)mip >= _header.Levels.Count) {
            throw new ArgumentOutOfRangeException(nameof(region), "KTX2 mip level is out of range.");
        }

        var level = _descriptor.Parts[0].Topology.Levels[mip];
        var expectedRegion = Core.Spatial.ImageBox.FromOrigin(level.Extent.Width, level.Extent.Height);
        if (region.Region.MinX != expectedRegion.MinX || region.Region.MinY != expectedRegion.MinY ||
            region.Region.MaxXExclusive != expectedRegion.MaxXExclusive || region.Region.MaxYExclusive != expectedRegion.MaxYExclusive) {
            throw new NotSupportedException("Partial KTX2 subresource reads are not supported yet.");
        }

        var decoded = DecodeLevel(mip);
        var sliceBytes = Ktx2FormatTable.SliceBytes(_header.Format, level.Extent.Width, level.Extent.Height);
        var itemBytes = checked(sliceBytes * level.Extent.Depth);
        var item = ItemIndex(subresource.ArrayElement, subresource.Face);
        var offset = checked(item * itemBytes);

        if (destination.Length < itemBytes) {
            throw new ArgumentException("Destination buffer is too small for this KTX2 subresource.", nameof(destination));
        }

        decoded.AsSpan((int)offset, (int)itemBytes).CopyTo(destination);
        return (int)itemBytes;
    }

    private byte[] DecodeLevel(int mip)
    {
        if (_decodedLevels[mip] is { } cached) {
            return cached;
        }

        var entry = _header.Levels[mip];
        _stream.Position = (long)entry.ByteOffset;
        var packed = new byte[entry.ByteLength];
        _stream.ReadExactly(packed);

        byte[] unpacked;
        switch (_header.SupercompressionScheme) {
            case Ktx2SupercompressionScheme.None:
                unpacked = packed;
                break;
            case Ktx2SupercompressionScheme.Zlib:
                unpacked = new byte[entry.UncompressedByteLength];
                using (var input = new MemoryStream(packed))
                using (var zlib = new ZLibStream(input, CompressionMode.Decompress)) {
                    zlib.ReadExactly(unpacked);
                }

                break;
            default:
                throw new ImageFormatException(
                    "ktx2",
                    $"Unsupported.Ktx2.Supercompression.{_header.SupercompressionScheme}",
                    $"KTX2 supercompression scheme '{_header.SupercompressionScheme}' is not implemented.");
        }

        _decodedLevels[mip] = unpacked;
        return unpacked;
    }

    private int ItemIndex(int arrayElement, int face) => checked((arrayElement * (int)_header.FaceCount) + face);

    private static List<Ktx2KeyValueEntry> ReadKeyValueData(
        Stream stream,
        Ktx2BinaryReader reader,
        uint dfdByteOffset,
        uint dfdByteLength,
        uint kvdByteOffset,
        uint kvdByteLength)
    {
        _ = dfdByteOffset;
        _ = dfdByteLength;

        if (kvdByteLength == 0) {
            return [];
        }

        stream.Position = kvdByteOffset;
        var data = reader.ReadBytes((int)kvdByteLength);
        return Ktx2KeyValueIo.Read(data);
    }
}
