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
        var sgdByteOffset = reader.ReadUInt64();
        var sgdByteLength = reader.ReadUInt64();

        var levels = new List<Ktx2LevelIndexEntry>((int)levelCount);
        for (var i = 0; i < levelCount; i++) {
            var byteOffset = reader.ReadUInt64();
            var byteLength = reader.ReadUInt64();
            var uncompressedByteLength = reader.ReadUInt64();
            levels.Add(new Ktx2LevelIndexEntry(byteOffset, byteLength, uncompressedByteLength));
        }

        if ((kvdByteLength == 0) != (kvdByteOffset == 0)) {
            throw new ImageFormatException("ktx2", "BadIndex", "KTX2 key/value offset and length must either both be zero or both be nonzero.");
        }

        if (sgdByteLength == 0 ? sgdByteOffset != 0 : sgdByteOffset == 0) {
            throw new ImageFormatException("ktx2", "BadIndex", "KTX2 supercompression global data offset and length are inconsistent.");
        }

        if (supercompressionScheme is Ktx2SupercompressionScheme.None or Ktx2SupercompressionScheme.Zlib && sgdByteLength != 0) {
            throw new ImageFormatException("ktx2", "BadIndex", "KTX2 supercompression global data is not valid for this scheme.");
        }

        if (faceCount is not 1 and not 6) {
            throw new ImageFormatException("ktx2", "BadHeader", "KTX2 face count must be 1 or 6.");
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

        ValidateDataFormatDescriptor(stream, reader, format, dfdByteOffset, dfdByteLength, levelCount);
        var keyValues = ReadKeyValueData(stream, reader, kvdByteOffset, kvdByteLength);
        _descriptor = Ktx2DescriptorMapper.ToImageAssetDescriptor(_header, keyValues);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("ktx2", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _itemCount = checked(_header.EffectiveArrayElementCount * (int)_header.FaceCount);
        _decodedLevels = new byte[]?[levelCount];

        for (var mip = 0; mip < levels.Count; mip++) {
            var level = levels[mip];
            if (level.ByteOffset > (ulong)stream.Length || level.ByteLength > (ulong)stream.Length - level.ByteOffset) {
                throw new ImageFormatException("ktx2", "BadLevelOffset", "KTX2 level data is outside the stream bounds.");
            }

            if (level.UncompressedByteLength > (ulong)limits.MaxDecodedBytes) {
                throw new ImageFormatException("ktx2", "LimitExceeded", "KTX2 level exceeds MaxDecodedBytes.");
            }

            if (supercompressionScheme == Ktx2SupercompressionScheme.None && level.ByteLength != level.UncompressedByteLength) {
                throw new ImageFormatException("ktx2", "BadLevelIndex", "An uncompressed KTX2 level has different packed and unpacked lengths.");
            }

            var extent = _descriptor.Parts[0].Topology.Levels[mip].Extent;
            var minimumLevelBytes = checked((ulong)(Ktx2FormatTable.SliceBytes(format, extent.Width, extent.Height) * extent.Depth * _itemCount));
            if (level.UncompressedByteLength < minimumLevelBytes) {
                throw new ImageFormatException("ktx2", "BadLevelIndex", "KTX2 level data is shorter than its declared image dimensions require.");
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

        try {
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
        catch (Exception exception) when (Ktx2FormatErrors.IsMalformed(exception)) {
            throw Ktx2FormatErrors.Wrap(exception, _stream);
        }
    }

    private int ItemIndex(int arrayElement, int face) => checked((arrayElement * (int)_header.FaceCount) + face);

    private static void ValidateDataFormatDescriptor(
        Stream stream,
        Ktx2BinaryReader reader,
        VkFormat format,
        uint dfdByteOffset,
        uint dfdByteLength,
        uint levelCount)
    {
        var minimumOffset = checked(80UL + (24UL * levelCount));
        if (dfdByteLength > int.MaxValue || dfdByteOffset < minimumOffset ||
            dfdByteOffset > (ulong)stream.Length || dfdByteLength > (ulong)stream.Length - dfdByteOffset) {
            throw new ImageFormatException("ktx2", "BadIndex", "KTX2 data format descriptor is outside the valid file range.");
        }

        stream.Position = dfdByteOffset;
        Ktx2DfdValidator.Validate(reader.ReadBytes((int)dfdByteLength), format);
    }

    private static List<Ktx2KeyValueEntry> ReadKeyValueData(
        Stream stream,
        Ktx2BinaryReader reader,
        uint kvdByteOffset,
        uint kvdByteLength)
    {
        if (kvdByteLength == 0) {
            return [];
        }

        if (kvdByteLength > int.MaxValue || kvdByteOffset > (ulong)stream.Length ||
            kvdByteLength > (ulong)stream.Length - kvdByteOffset) {
            throw new ImageFormatException("ktx2", "BadIndex", "KTX2 key/value data is outside the valid file range.");
        }

        stream.Position = kvdByteOffset;
        var data = reader.ReadBytes((int)kvdByteLength);
        return Ktx2KeyValueIo.Read(data);
    }
}
