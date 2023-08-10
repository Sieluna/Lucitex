namespace Lucitex.Dds.Compression;

internal static class Bc3Codec
{
    public const int BlockByteSize = 16;

    public static void Decode(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        Span<byte> alpha = stackalloc byte[16];
        Bc4Codec.Decode(block[..8], alpha);

        Bc1Codec.Decode(block[8..], rgba);

        for (var texel = 0; texel < 16; texel++) {
            rgba[(texel * 4) + 3] = alpha[texel];
        }
    }

    public static void Encode(ReadOnlySpan<byte> rgba, Span<byte> block)
    {
        Span<byte> alpha = stackalloc byte[16];
        for (var texel = 0; texel < 16; texel++) {
            alpha[texel] = rgba[(texel * 4) + 3];
        }

        Bc4Codec.Encode(alpha, block[..8]);
        Bc1Codec.Encode(rgba, block[8..], forceOpaqueMode: true);
    }
}
