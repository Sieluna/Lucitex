namespace Lucitex.Dds.Compression;

internal static class Bc5Codec
{
    public const int BlockByteSize = 16;

    public static void Decode(ReadOnlySpan<byte> block, Span<byte> rg)
    {
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];

        Bc4Codec.Decode(block[..8], red);
        Bc4Codec.Decode(block[8..], green);

        for (var texel = 0; texel < 16; texel++) {
            rg[texel * 2] = red[texel];
            rg[(texel * 2) + 1] = green[texel];
        }
    }

    public static void Encode(ReadOnlySpan<byte> rg, Span<byte> block)
    {
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];

        for (var texel = 0; texel < 16; texel++) {
            red[texel] = rg[texel * 2];
            green[texel] = rg[(texel * 2) + 1];
        }

        Bc4Codec.Encode(red, block[..8]);
        Bc4Codec.Encode(green, block[8..]);
    }
}
