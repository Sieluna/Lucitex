using Lucitex.Core.Execution;

namespace Lucitex.Exr.Format;

internal static class ExrHeaderReader
{
    private const int k_MagicNumber = 20000630;

    public static ExrVersionFlags ReadFileVersion(ExrBinaryReader reader, out int versionNumber)
    {
        var magic = reader.ReadInt32();
        if (magic != k_MagicNumber) {
            throw new ImageFormatException("exr", "BadMagic", "Stream does not start with the OpenEXR magic number.");
        }

        var versionField = reader.ReadInt32();
        versionNumber = versionField & 0xFF;
        return (ExrVersionFlags)(versionField & ~0xFF);
    }

    public static ExrHeader ReadHeader(ExrBinaryReader reader) =>
        TryReadHeader(reader) ?? throw new ImageFormatException("exr", "MissingAttribute", "Expected a header but found an empty part-list terminator.");

    public static List<ExrHeader> ReadHeaderList(ExrBinaryReader reader, bool isMultiPart)
    {
        if (!isMultiPart) {
            return [ReadHeader(reader)];
        }

        var headers = new List<ExrHeader>();
        while (TryReadHeader(reader) is { } header) {
            headers.Add(header);
        }

        return headers;
    }

    public static ExrHeader? TryReadHeader(ExrBinaryReader reader)
    {
        List<ExrChannelInfo>? channels = null;
        ExrCompressionId? compression = null;
        ExrBox2i? dataWindow = null;
        ExrBox2i? displayWindow = null;
        var lineOrder = ExrLineOrder.IncreasingY;
        var pixelAspectRatio = 1.0f;
        ExrTileDesc? tiles = null;
        string? partName = null;
        string? partType = null;
        int? chunkCount = null;
        var unknown = new List<ExrRawAttribute>();
        var isFirstAttribute = true;

        while (true) {
            var name = reader.ReadCString();
            if (name.Length == 0) {
                if (isFirstAttribute) {
                    return null;
                }

                break;
            }

            isFirstAttribute = false;

            var type = reader.ReadCString();
            var size = reader.ReadInt32();

            switch (name) {
                case "channels" when type == "chlist":
                    channels = ReadChannelList(reader);
                    break;
                case "compression" when type == "compression":
                    compression = (ExrCompressionId)reader.ReadByte();
                    break;
                case "dataWindow" when type == "box2i":
                    dataWindow = ReadBox2i(reader);
                    break;
                case "displayWindow" when type == "box2i":
                    displayWindow = ReadBox2i(reader);
                    break;
                case "lineOrder" when type == "lineOrder":
                    lineOrder = (ExrLineOrder)reader.ReadByte();
                    break;
                case "pixelAspectRatio" when type == "float":
                    pixelAspectRatio = reader.ReadFloat();
                    break;
                case "tiles" when type == "tiledesc":
                    tiles = ReadTileDesc(reader);
                    break;
                case "name" when type == "string":
                    partName = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(size));
                    break;
                case "type" when type == "string":
                    partType = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(size));
                    break;
                case "chunkCount" when type == "int":
                    chunkCount = reader.ReadInt32();
                    break;
                default:
                    unknown.Add(new ExrRawAttribute { Name = name, Type = type, Value = reader.ReadBytes(size) });
                    break;
            }
        }

        if (channels is null) {
            throw new ImageFormatException("exr", "MissingAttribute", "Header is missing the required 'channels' attribute.");
        }

        if (compression is null) {
            throw new ImageFormatException("exr", "MissingAttribute", "Header is missing the required 'compression' attribute.");
        }

        if (dataWindow is null) {
            throw new ImageFormatException("exr", "MissingAttribute", "Header is missing the required 'dataWindow' attribute.");
        }

        if (displayWindow is null) {
            throw new ImageFormatException("exr", "MissingAttribute", "Header is missing the required 'displayWindow' attribute.");
        }

        return new ExrHeader {
            Channels = channels,
            Compression = compression.Value,
            DataWindow = dataWindow.Value,
            DisplayWindow = displayWindow.Value,
            LineOrder = lineOrder,
            PixelAspectRatio = pixelAspectRatio,
            Tiles = tiles,
            PartName = partName,
            PartType = partType,
            ChunkCount = chunkCount,
            UnknownAttributes = unknown,
        };
    }

    private static List<ExrChannelInfo> ReadChannelList(ExrBinaryReader reader)
    {
        var channels = new List<ExrChannelInfo>();

        while (true) {
            var name = reader.ReadCString();
            if (name.Length == 0) {
                break;
            }

            var pixelType = (ExrPixelType)reader.ReadInt32();
            var pLinear = reader.ReadByte() != 0;
            reader.ReadBytes(3);
            var xSampling = reader.ReadInt32();
            var ySampling = reader.ReadInt32();

            channels.Add(new ExrChannelInfo {
                Name = name,
                PixelType = pixelType,
                PLinear = pLinear,
                XSampling = xSampling,
                YSampling = ySampling,
            });
        }

        return channels;
    }

    private static ExrBox2i ReadBox2i(ExrBinaryReader reader)
    {
        var xMin = reader.ReadInt32();
        var yMin = reader.ReadInt32();
        var xMax = reader.ReadInt32();
        var yMax = reader.ReadInt32();
        return new ExrBox2i(xMin, yMin, xMax, yMax);
    }

    private static ExrTileDesc ReadTileDesc(ExrBinaryReader reader)
    {
        var xSize = reader.ReadUInt32();
        var ySize = reader.ReadUInt32();
        var mode = reader.ReadByte();

        return new ExrTileDesc(
            xSize,
            ySize,
            (ExrTileLevelMode)(mode & 0x0F),
            (ExrTileRoundingMode)((mode >> 4) & 0x0F));
    }
}
