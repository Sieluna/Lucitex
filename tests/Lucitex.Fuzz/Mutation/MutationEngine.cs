using System.Buffers.Binary;

namespace Lucitex.Fuzz;

internal enum MutationMode
{
    Mixed,
    Raw,
    Structured,
}

internal readonly record struct MutationInput(byte[] Bytes, string Strategy);

internal static class MutationEngine
{
    public static MutationInput Mutate(SeedInput seed, Random random, MutationMode mode)
    {
        if (mode == MutationMode.Mixed && random.Next(16) == 0) {
            return new(seed.Bytes.ToArray(), "identity");
        }
        if (mode != MutationMode.Raw && (mode == MutationMode.Structured || random.Next(2) == 0)) {
            var structured = seed.Format switch {
                ImageFormat.Png => MutateChunks(seed.Bytes, random, true),
                ImageFormat.Webp => MutateChunks(seed.Bytes, random, false),
                _ => null,
            };
            if (structured is not null) {
                return new(structured, "chunk-payload");
            }
        }
        var bytes = ByteMutator.Mutate(seed.Bytes, random);
        if (bytes.Length > FuzzLimits.MaxInputBytes) {
            Array.Resize(ref bytes, FuzzLimits.MaxInputBytes);
        }
        return new(bytes, "raw");
    }

    private static byte[]? MutateChunks(byte[] source, Random random, bool png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png ? source.Length < 8 || !source.AsSpan(0, 8).SequenceEqual(signature) :
            source.Length < 12 || !source.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !source.AsSpan(8, 4).SequenceEqual("WEBP"u8)) {
            return null;
        }
        var chunks = new List<(int Offset, int Length)>();
        var offset = png ? 8 : 12;
        while (offset <= source.Length - (png ? 12 : 8)) {
            var length = png ? BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset)) :
                BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 4));
            var next = (long)offset + 8 + length + (png ? 4 : length & 1);
            if (next > source.Length) {
                return null;
            }
            if (length > 0) {
                chunks.Add((offset, (int)length));
            }
            offset = (int)next;
        }
        if (offset != source.Length || chunks.Count == 0) {
            return null;
        }
        var result = source.ToArray();
        var chunk = chunks[random.Next(chunks.Count)];
        result[chunk.Offset + 8 + random.Next(chunk.Length)] ^= (byte)(1 << random.Next(8));
        if (png) {
            var crc = Crc32(result.AsSpan(chunk.Offset + 4, chunk.Length + 4));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(chunk.Offset + 8 + chunk.Length), crc);
        }
        else {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        }
        return result;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data) {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
        }
        return ~crc;
    }
}
