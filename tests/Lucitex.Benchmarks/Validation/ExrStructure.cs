using System.Text;

namespace Lucitex.Benchmarks.Validation;

internal sealed record ExrStructure(int Width, int Height, int Compression, string[] Channels)
{
    public static ExrStructure Read(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded, false);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (reader.ReadUInt32() != 20000630 || reader.ReadUInt32() != 2) {
            throw new InvalidDataException("EXR profile requires a single scanline part, version 2.");
        }
        var attributes = new Dictionary<string, (string Type, byte[] Value)>();
        for (var name = Text(reader); name.Length != 0; name = Text(reader)) {
            var type = Text(reader);
            var count = reader.ReadInt32();
            if (count < 0 || count > stream.Length - stream.Position) {
                throw new InvalidDataException("Truncated EXR attribute.");
            }
            attributes.Add(name, (type, reader.ReadBytes(count)));
        }
        byte[] Attribute(string name, string type) => attributes.TryGetValue(name, out var value) && value.Type == type
            ? value.Value : throw new InvalidDataException($"Missing EXR {name}/{type} attribute.");
        var box = Attribute("dataWindow", "box2i");
        if (box.Length != 16 || BitConverter.ToInt32(box, 0) != 0 || BitConverter.ToInt32(box, 4) != 0) {
            throw new InvalidDataException("EXR fixture requires an origin-based data window.");
        }
        var width = checked(BitConverter.ToInt32(box, 8) + 1);
        var height = checked(BitConverter.ToInt32(box, 12) + 1);
        var compression = Attribute("compression", "compression");
        if (width <= 0 || height <= 0 || compression.Length != 1) {
            throw new InvalidDataException("Invalid EXR shape or compression attribute.");
        }
        using var channelStream = new MemoryStream(Attribute("channels", "chlist"), false);
        using var channels = new BinaryReader(channelStream, Encoding.ASCII);
        var names = new List<string>();
        for (var name = Text(channels); name.Length != 0; name = Text(channels)) {
            names.Add(name);
            var sampleType = channels.ReadInt32();
            channels.ReadUInt32();
            if (sampleType != 1 || channels.ReadInt32() != 1 || channels.ReadInt32() != 1) {
                throw new InvalidDataException("EXR profile requires HALF channels with unit sampling.");
            }
        }
        if (!names.SequenceEqual(new[] { "A", "B", "G", "R" }) || channelStream.Position != channelStream.Length) {
            throw new InvalidDataException("EXR profile requires exactly A/B/G/R channels.");
        }
        return new ExrStructure(width, height, compression[0], names.ToArray());
    }

    private static string Text(BinaryReader reader)
    {
        var text = new StringBuilder();
        for (var value = reader.ReadByte(); value != 0; value = reader.ReadByte()) {
            if (text.Length >= 255) {
                throw new InvalidDataException("EXR attribute name is too long.");
            }
            text.Append((char)value);
        }
        return text.ToString();
    }
}
