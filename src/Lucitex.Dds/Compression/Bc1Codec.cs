using System.Buffers.Binary;

namespace Lucitex.Dds.Compression;

internal static class Bc1Codec
{
    public const int BlockByteSize = 8;

    public static void Decode(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        var color0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        Span<(byte R, byte G, byte B, byte A)> palette = stackalloc (byte, byte, byte, byte)[4];
        BuildPalette(color0, color1, palette);

        for (var texel = 0; texel < 16; texel++) {
            var index = (int)((indices >> (texel * 2)) & 0b11);
            var (r, g, b, a) = palette[index];
            var offset = texel * 4;
            rgba[offset] = r;
            rgba[offset + 1] = g;
            rgba[offset + 2] = b;
            rgba[offset + 3] = a;
        }
    }

    public static void Encode(ReadOnlySpan<byte> rgba, Span<byte> block) => Encode(rgba, block, forceOpaqueMode: false);

    public static void Encode(ReadOnlySpan<byte> rgba, Span<byte> block, bool forceOpaqueMode)
    {
        FindEndpoints(rgba, out var colorA, out var colorB);

        var packedA = Pack565(colorA.R, colorA.G, colorA.B);
        var packedB = Pack565(colorB.R, colorB.G, colorB.B);

        // BuildPalette treats color0 > color1 as "always opaque, 4-color interpolation" and
        // color0 <= color1 as "punch-through alpha". Ordering by the larger packed value
        // keeps every non-degenerate block in opaque mode without disturbing which two real
        // colors were chosen as endpoints.
        var color0 = Math.Max(packedA, packedB);
        var color1 = Math.Min(packedA, packedB);

        if (forceOpaqueMode && color0 == color1) {
            if (color0 == ushort.MaxValue) {
                color1--;
            }
            else {
                color0++;
            }
        }

        BinaryPrimitives.WriteUInt16LittleEndian(block, color0);
        BinaryPrimitives.WriteUInt16LittleEndian(block[2..], color1);

        Span<(byte R, byte G, byte B, byte A)> palette = stackalloc (byte, byte, byte, byte)[4];
        BuildPalette(color0, color1, palette);

        uint indices = 0;
        for (var texel = 0; texel < 16; texel++) {
            var offset = texel * 4;
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < 4; i++) {
                var dr = rgba[offset] - palette[i].R;
                var dg = rgba[offset + 1] - palette[i].G;
                var db = rgba[offset + 2] - palette[i].B;
                var distance = (dr * dr) + (dg * dg) + (db * db);
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = i;
                }
            }

            indices |= (uint)best << (texel * 2);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], indices);
    }

    private static void BuildPalette(ushort color0, ushort color1, Span<(byte R, byte G, byte B, byte A)> palette)
    {
        var c0 = Unpack565(color0);
        var c1 = Unpack565(color1);

        palette[0] = (c0.R, c0.G, c0.B, 255);
        palette[1] = (c1.R, c1.G, c1.B, 255);

        if (color0 > color1) {
            palette[2] = (Lerp2(c0.R, c1.R), Lerp2(c0.G, c1.G), Lerp2(c0.B, c1.B), 255);
            palette[3] = (Lerp1(c0.R, c1.R), Lerp1(c0.G, c1.G), Lerp1(c0.B, c1.B), 255);
        }
        else {
            palette[2] = (Average(c0.R, c1.R), Average(c0.G, c1.G), Average(c0.B, c1.B), 255);
            palette[3] = (0, 0, 0, 0);
        }
    }

    private static byte Lerp2(byte a, byte b) => (byte)(((2 * a) + b) / 3);

    private static byte Lerp1(byte a, byte b) => (byte)((a + (2 * b)) / 3);

    private static byte Average(byte a, byte b) => (byte)((a + b) / 2);

    private static (byte R, byte G, byte B) Unpack565(ushort value)
    {
        var r5 = (value >> 11) & 0x1F;
        var g6 = (value >> 5) & 0x3F;
        var b5 = value & 0x1F;

        var r = (byte)((r5 << 3) | (r5 >> 2));
        var g = (byte)((g6 << 2) | (g6 >> 4));
        var b = (byte)((b5 << 3) | (b5 >> 2));

        return (r, g, b);
    }

    private static ushort Pack565(byte r, byte g, byte b)
    {
        var r5 = r >> 3;
        var g6 = g >> 2;
        var b5 = b >> 3;

        return (ushort)((r5 << 11) | (g6 << 5) | b5);
    }

    private static void FindEndpoints(ReadOnlySpan<byte> rgba, out (byte R, byte G, byte B) colorA, out (byte R, byte G, byte B) colorB)
    {
        // The two endpoints of a BC1 block must lie on the single line every texel gets
        // interpolated along, so picking them independently per channel (a naive bounding
        // box) can invent a line that passes nowhere near the actual pixels. Picking the
        // two actual texels that are farthest apart keeps the line anchored to real data.
        var bestI = 0;
        var bestJ = 1;
        var bestDistance = -1;

        for (var i = 0; i < 16; i++) {
            var iOffset = i * 4;
            for (var j = i + 1; j < 16; j++) {
                var jOffset = j * 4;
                var dr = rgba[iOffset] - rgba[jOffset];
                var dg = rgba[iOffset + 1] - rgba[jOffset + 1];
                var db = rgba[iOffset + 2] - rgba[jOffset + 2];
                var distance = (dr * dr) + (dg * dg) + (db * db);

                if (distance > bestDistance) {
                    bestDistance = distance;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        colorA = (rgba[bestI * 4], rgba[(bestI * 4) + 1], rgba[(bestI * 4) + 2]);
        colorB = (rgba[bestJ * 4], rgba[(bestJ * 4) + 1], rgba[(bestJ * 4) + 2]);
    }
}
