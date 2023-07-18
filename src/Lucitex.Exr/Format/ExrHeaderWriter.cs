using System.Text;

namespace Lucitex.Exr.Format;

internal static class ExrHeaderWriter
{
    private const int MagicNumber = 20000630;

    public static void WriteFileVersion(ExrBinaryWriter writer, ExrVersionFlags flags, int versionNumber = 2)
    {
        writer.WriteInt32(MagicNumber);
        writer.WriteInt32(versionNumber | (int)flags);
    }

    public static void WriteHeader(ExrBinaryWriter writer, ExrHeader header)
    {
        WriteChannelsAttribute(writer, header.Channels);
        WriteByteAttribute(writer, "compression", "compression", (byte)header.Compression);
        WriteBox2iAttribute(writer, "dataWindow", header.DataWindow);
        WriteBox2iAttribute(writer, "displayWindow", header.DisplayWindow);
        WriteByteAttribute(writer, "lineOrder", "lineOrder", (byte)header.LineOrder);
        WriteFloatAttribute(writer, "pixelAspectRatio", header.PixelAspectRatio);

        if (header.Tiles is { } tiles)
        {
            WriteTileDescAttribute(writer, tiles);
        }

        if (header.PartName is { } partName)
        {
            WriteStringAttribute(writer, "name", partName);
        }

        if (header.PartType is { } partType)
        {
            WriteStringAttribute(writer, "type", partType);
        }

        if (header.ChunkCount is { } chunkCount)
        {
            WriteIntAttribute(writer, "chunkCount", chunkCount);
        }

        foreach (var attribute in header.UnknownAttributes)
        {
            writer.WriteCString(attribute.Name);
            writer.WriteCString(attribute.Type);
            writer.WriteInt32(attribute.Value.Length);
            writer.WriteBytes(attribute.Value);
        }

        writer.WriteByte(0);
    }

    private static void WriteChannelsAttribute(ExrBinaryWriter writer, IReadOnlyList<ExrChannelInfo> channels)
    {
        var size = 1;
        foreach (var channel in channels)
        {
            size += Encoding.UTF8.GetByteCount(channel.Name) + 1 + 4 + 1 + 3 + 4 + 4;
        }

        writer.WriteCString("channels");
        writer.WriteCString("chlist");
        writer.WriteInt32(size);

        Span<byte> reserved = stackalloc byte[3];
        foreach (var channel in channels)
        {
            writer.WriteCString(channel.Name);
            writer.WriteInt32((int)channel.PixelType);
            writer.WriteByte((byte)(channel.PLinear ? 1 : 0));
            writer.WriteBytes(reserved);
            writer.WriteInt32(channel.XSampling);
            writer.WriteInt32(channel.YSampling);
        }

        writer.WriteByte(0);
    }

    private static void WriteByteAttribute(ExrBinaryWriter writer, string name, string type, byte value)
    {
        writer.WriteCString(name);
        writer.WriteCString(type);
        writer.WriteInt32(1);
        writer.WriteByte(value);
    }

    private static void WriteFloatAttribute(ExrBinaryWriter writer, string name, float value)
    {
        writer.WriteCString(name);
        writer.WriteCString("float");
        writer.WriteInt32(4);
        writer.WriteFloat(value);
    }

    private static void WriteIntAttribute(ExrBinaryWriter writer, string name, int value)
    {
        writer.WriteCString(name);
        writer.WriteCString("int");
        writer.WriteInt32(4);
        writer.WriteInt32(value);
    }

    private static void WriteStringAttribute(ExrBinaryWriter writer, string name, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.WriteCString(name);
        writer.WriteCString("string");
        writer.WriteInt32(bytes.Length);
        writer.WriteBytes(bytes);
    }

    private static void WriteBox2iAttribute(ExrBinaryWriter writer, string name, ExrBox2i box)
    {
        writer.WriteCString(name);
        writer.WriteCString("box2i");
        writer.WriteInt32(16);
        writer.WriteInt32(box.XMin);
        writer.WriteInt32(box.YMin);
        writer.WriteInt32(box.XMax);
        writer.WriteInt32(box.YMax);
    }

    private static void WriteTileDescAttribute(ExrBinaryWriter writer, ExrTileDesc tiles)
    {
        writer.WriteCString("tiles");
        writer.WriteCString("tiledesc");
        writer.WriteInt32(9);
        writer.WriteUInt32(tiles.XSize);
        writer.WriteUInt32(tiles.YSize);
        writer.WriteByte((byte)((byte)tiles.LevelMode | ((byte)tiles.RoundingMode << 4)));
    }
}
