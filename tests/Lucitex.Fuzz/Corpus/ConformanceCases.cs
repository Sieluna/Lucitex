using System.Buffers.Binary;

namespace Lucitex.Fuzz;

internal static class ConformanceCases
{
    public static IEnumerable<SeedInput> Create(IReadOnlyList<SeedInput> seeds)
    {
        yield return Mutate("rgba8.png", "iend-crc", bytes => bytes[^1] ^= 1);
        var png = seeds.Single(seed => seed.Name == "rgba8.png");
        yield return png with { Name = "truncated-iend.png", Bytes = png.Bytes[..^1] };
        yield return Mutate("rgba8.ktx2", "dfd-offset", bytes => Put32(bytes, 48, (uint)bytes.Length + 1));
        yield return Mutate("rgba8.ktx2", "level-extent", bytes => Put32(bytes, 20, 213));
        yield return Mutate("rgba8.ktx2", "dfd-sample", bytes => bytes[(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48)) + 30] = 80);
        yield return Mutate("rgba8.ktx2", "dfd-transfer", bytes => bytes[(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48)) + 14] = 190);
        yield return Mutate("rgba8.ktx2", "dfd-exponent", bytes => bytes[(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48)) + 31] |= 32);
        yield return Mutate("rgb8.jpg", "scan-parameters", bytes => {
            var offset = bytes.AsSpan().IndexOf(new byte[] { 255, 218 });
            bytes[offset + 5 + 2 * bytes[offset + 4]] = 1;
        });
        var jpeg = seeds.Single(seed => seed.Name == "gray8.jpg");
        var end = jpeg.Bytes.AsSpan().LastIndexOf(new byte[] { 255, 217 });
        yield return jpeg with { Name = "extra-entropy.jpg", Bytes = [..jpeg.Bytes.AsSpan(0, end), 0, ..jpeg.Bytes.AsSpan(end)] };
        yield return Mutate("lossy-alpha.webp", "alpha-header", bytes => bytes[Chunk(bytes, "ALPH"u8)] = 0x21);
        yield return Mutate("lossy-alpha.webp", "alpha-flag", bytes => bytes[Chunk(bytes, "VP8X"u8)] = 0);
        yield return Mutate("lossy-alpha.webp", "frame-profile", bytes => bytes[Chunk(bytes, "VP8 "u8)] |= 8);
        yield return Mutate("rgba-none.exr", "attribute-type", bytes => {
            var offset = bytes.AsSpan().IndexOf("name\0string\0"u8);
            bytes[offset + 5] = (byte)'_';
        });
        yield return Mutate("rgba-none.exr", "pixel-aspect", bytes => {
            var offset = bytes.AsSpan().IndexOf("pixelAspectRatio\0float\0"u8) + "pixelAspectRatio\0float\0"u8.Length + 4;
            Put32(bytes, offset, 0xbf800000);
        });
        yield return Mutate("rgba-none.exr", "linear-flag", bytes => {
            var offset = bytes.AsSpan().IndexOf("channels\0chlist\0"u8) + "channels\0chlist\0"u8.Length + 4;
            bytes[offset + 6] = 176;
        });
        yield return Mutate("rgba-none.exr", "chunk-size", bytes => {
            var offset = 8;
            while (bytes[offset] != 0) {
                offset += bytes.AsSpan(offset).IndexOf((byte)0) + 1;
                offset += bytes.AsSpan(offset).IndexOf((byte)0) + 1;
                offset += 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            }
            var chunk = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset + 1)));
            Put32(bytes, chunk + 4, 1);
        });
        yield return new SeedInput("invalid-arithmetic-code.webp", ImageFormat.Webp, Convert.FromHexString(
            "524946466c00000057454250565038205f0000003001009d012a110013000000a600000f255800ffa27b77d84be16feb9cc10ae7136d674c6449ffaeb5936ab75de673cdbcd8d1e6b24fbfc986feb5d5875258b035c1137b8c394d093bc2475541a1df5f079ee85cf0ac44fe5d802224da300000"));

        SeedInput Mutate(string name, string reason, Action<byte[]> mutate)
        {
            var seed = seeds.Single(seed => seed.Name == name);
            var bytes = seed.Bytes.ToArray();
            mutate(bytes);
            return new SeedInput($"{reason}-{name}", seed.Format, bytes);
        }
    }

    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static int Chunk(byte[] bytes, ReadOnlySpan<byte> type)
    {
        for (var offset = 12; offset <= bytes.Length - 8;) {
            if (bytes.AsSpan(offset, 4).SequenceEqual(type)) return offset + 8;
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4)));
            offset += 8 + size + (size & 1);
        }
        throw new InvalidOperationException("Missing conformance seed chunk.");
    }
}
