using System.Buffers.Binary;

namespace Lucitex.TextureCompression;

internal static class Bc2Codec
{
    public const int BlockByteSize = 16;

    public static void Decode(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        var alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block);
        Bc1Codec.Decode(block[8..], rgba);

        for (var texel = 0; texel < 16; texel++) {
            rgba[(texel * 4) + 3] = (byte)(((alphaBits >> (texel * 4)) & 0xF) * 17);
        }
    }

    public static void Encode(ReadOnlySpan<byte> rgba, Span<byte> block)
    {
        ulong alphaBits = 0;
        for (var texel = 0; texel < 16; texel++) {
            var alpha = rgba[(texel * 4) + 3];
            var quantized = Math.Min(15, (alpha + 8) / 17);
            alphaBits |= (ulong)quantized << (texel * 4);
        }

        BinaryPrimitives.WriteUInt64LittleEndian(block, alphaBits);
        Bc1Codec.Encode(rgba, block[8..], forceOpaqueMode: true);
    }
}
