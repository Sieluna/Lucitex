using System.Numerics;

namespace Lucitex.Conversion.Kernels;

internal static class RgbeConversionKernel
{
    public static void Decode(ReadOnlySpan<byte> source, Span<float> red, Span<float> green, Span<float> blue)
    {
        var pixelCount = source.Length / 4;
        if (source.Length % 4 != 0 || red.Length < pixelCount || green.Length < pixelCount || blue.Length < pixelCount) {
            throw new ArgumentException("RGBE buffers have incompatible lengths.");
        }

        var pixel = 0;
        var lanes = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated) {
            Span<float> r = stackalloc float[lanes];
            Span<float> g = stackalloc float[lanes];
            Span<float> b = stackalloc float[lanes];
            Span<float> scale = stackalloc float[lanes];
            for (; pixel <= pixelCount - lanes; pixel += lanes) {
                for (var lane = 0; lane < lanes; lane++) {
                    var offset = (pixel + lane) * 4;
                    r[lane] = source[offset];
                    g[lane] = source[offset + 1];
                    b[lane] = source[offset + 2];
                    var exponent = source[offset + 3];
                    scale[lane] = exponent == 0 ? 0 : MathF.ScaleB(1, exponent - 136);
                }

                var multiplier = new Vector<float>(scale);
                (new Vector<float>(r) * multiplier).CopyTo(red[pixel..]);
                (new Vector<float>(g) * multiplier).CopyTo(green[pixel..]);
                (new Vector<float>(b) * multiplier).CopyTo(blue[pixel..]);
            }
        }

        for (; pixel < pixelCount; pixel++) {
            DecodePixel(source[(pixel * 4)..], out red[pixel], out green[pixel], out blue[pixel]);
        }
    }

    public static void Encode(ReadOnlySpan<float> red, ReadOnlySpan<float> green, ReadOnlySpan<float> blue, Span<byte> destination)
    {
        var pixelCount = red.Length;
        if (green.Length != pixelCount || blue.Length != pixelCount || destination.Length < pixelCount * 4) {
            throw new ArgumentException("RGBE buffers have incompatible lengths.");
        }

        var pixel = 0;
        var lanes = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated) {
            Span<float> r = stackalloc float[lanes];
            Span<float> g = stackalloc float[lanes];
            Span<float> b = stackalloc float[lanes];
            Span<float> maximum = stackalloc float[lanes];
            for (; pixel <= pixelCount - lanes; pixel += lanes) {
                var vr = Vector.Max(Vector<float>.Zero, new Vector<float>(red.Slice(pixel, lanes)));
                var vg = Vector.Max(Vector<float>.Zero, new Vector<float>(green.Slice(pixel, lanes)));
                var vb = Vector.Max(Vector<float>.Zero, new Vector<float>(blue.Slice(pixel, lanes)));
                vr.CopyTo(r);
                vg.CopyTo(g);
                vb.CopyTo(b);
                Vector.Max(vr, Vector.Max(vg, vb)).CopyTo(maximum);
                for (var lane = 0; lane < lanes; lane++) {
                    EncodePixel(r[lane], g[lane], b[lane], maximum[lane], destination[((pixel + lane) * 4)..]);
                }
            }
        }

        for (; pixel < pixelCount; pixel++) {
            var r = MathF.Max(0, red[pixel]);
            var g = MathF.Max(0, green[pixel]);
            var b = MathF.Max(0, blue[pixel]);
            EncodePixel(r, g, b, MathF.Max(r, MathF.Max(g, b)), destination[(pixel * 4)..]);
        }
    }

    private static void DecodePixel(ReadOnlySpan<byte> source, out float red, out float green, out float blue)
    {
        if (source[3] == 0) {
            red = 0;
            green = 0;
            blue = 0;
            return;
        }

        var scale = MathF.ScaleB(1, source[3] - 136);
        red = source[0] * scale;
        green = source[1] * scale;
        blue = source[2] * scale;
    }

    private static void EncodePixel(float red, float green, float blue, float maximum, Span<byte> destination)
    {
        if (!float.IsFinite(maximum) || maximum < 1e-32f) {
            destination[..4].Clear();
            return;
        }

        var exponent = Math.Clamp(MathF.ILogB(maximum) + 1, -128, 127);
        var scale = MathF.ScaleB(1, 8 - exponent);
        destination[0] = (byte)Math.Clamp((int)(red * scale), 0, 255);
        destination[1] = (byte)Math.Clamp((int)(green * scale), 0, 255);
        destination[2] = (byte)Math.Clamp((int)(blue * scale), 0, 255);
        destination[3] = (byte)(exponent + 128);
    }
}
