namespace Lucitex.Dds.Compression;

internal static class Bc4Codec
{
    public const int BlockByteSize = 8;

    public static void Decode(ReadOnlySpan<byte> block, Span<byte> channel16)
    {
        var v0 = block[0];
        var v1 = block[1];

        Span<byte> palette = stackalloc byte[8];
        BuildPalette(v0, v1, palette);

        var bits = ReadIndexBits(block[2..]);

        for (var texel = 0; texel < 16; texel++) {
            var index = (int)((bits >> (texel * 3)) & 0b111);
            channel16[texel] = palette[index];
        }
    }

    public static void Encode(ReadOnlySpan<byte> channel16, Span<byte> block)
    {
        byte min = 255, max = 0;
        foreach (var value in channel16) {
            if (value < min) {
                min = value;
            }

            if (value > max) {
                max = value;
            }
        }

        block[0] = max;
        block[1] = min;

        Span<byte> palette = stackalloc byte[8];
        BuildPalette(max, min, palette);

        ulong bits = 0;
        for (var texel = 0; texel < 16; texel++) {
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < 8; i++) {
                var distance = Math.Abs(channel16[texel] - palette[i]);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = i;
                }
            }

            bits |= (ulong)best << (texel * 3);
        }

        WriteIndexBits(bits, block[2..]);
    }

    private static void BuildPalette(byte v0, byte v1, Span<byte> palette)
    {
        palette[0] = v0;
        palette[1] = v1;

        if (v0 > v1) {
            for (var i = 1; i <= 6; i++) {
                palette[1 + i] = (byte)((((7 - i) * v0) + (i * v1)) / 7);
            }
        }
        else {
            for (var i = 1; i <= 4; i++) {
                palette[1 + i] = (byte)((((5 - i) * v0) + (i * v1)) / 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }
    }

    private static ulong ReadIndexBits(ReadOnlySpan<byte> sixBytes)
    {
        ulong value = 0;
        for (var i = 0; i < 6; i++) {
            value |= (ulong)sixBytes[i] << (i * 8);
        }

        return value;
    }

    private static void WriteIndexBits(ulong value, Span<byte> sixBytes)
    {
        for (var i = 0; i < 6; i++) {
            sixBytes[i] = (byte)(value >> (i * 8));
        }
    }
}
