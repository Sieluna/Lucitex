using System.Numerics;
using System.Runtime.InteropServices;

namespace Lucitex.Conversion.Kernels;

internal static class RgbeConversionKernel
{
    private const uint k_MaxExponentField = 254u;
    private const float k_DecodeScaleNormal = 1f / 512f;
    private const float k_DecodeScaleSaturated = 1f / 256f;
    private const float k_EncodeMinimum = 1e-32f;

    public static void Decode(ReadOnlySpan<byte> source, Span<float> red, Span<float> green, Span<float> blue)
    {
        var pixelCount = source.Length / 4;
        if (source.Length % 4 != 0 || red.Length < pixelCount || green.Length < pixelCount || blue.Length < pixelCount) {
            throw new ArgumentException("RGBE buffers have incompatible lengths.");
        }

        var pixel = 0;
        var lanes = Vector<uint>.Count;

        if (Vector.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            var packedSource = MemoryMarshal.Cast<byte, uint>(source);
            var byteMask = new Vector<uint>(0xFFu);
            var saturated = new Vector<uint>(255u);
            var maxField = new Vector<uint>(k_MaxExponentField);
            var normalScale = new Vector<float>(k_DecodeScaleNormal);
            var saturatedScale = new Vector<float>(k_DecodeScaleSaturated);

            for (; pixel + lanes <= pixelCount; pixel += lanes) {
                var packed = new Vector<uint>(packedSource.Slice(pixel, lanes));
                var exponent = packed >> 24;
                var field = Vector.Min(exponent, maxField);
                var isSaturated = Vector.Equals(exponent, saturated);
                var multiplier = Vector.ConditionalSelect(Vector.As<uint, int>(isSaturated), saturatedScale, normalScale);
                var scale = Vector.As<uint, float>(field << 23) * multiplier;

                (Vector.ConvertToSingle(packed & byteMask) * scale).CopyTo(red.Slice(pixel, lanes));
                (Vector.ConvertToSingle((packed >> 8) & byteMask) * scale).CopyTo(green.Slice(pixel, lanes));
                (Vector.ConvertToSingle((packed >> 16) & byteMask) * scale).CopyTo(blue.Slice(pixel, lanes));
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

        if (Vector.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            var packedDestination = MemoryMarshal.Cast<byte, uint>(destination);
            var zero = Vector<float>.Zero;
            var saturated = new Vector<uint>(255u);
            var exponentField = new Vector<uint>(0x7F800000u);
            var minimum = new Vector<float>(k_EncodeMinimum);
            var lowestExponent = new Vector<int>(-128);
            var highestExponent = new Vector<int>(127);

            for (; pixel + lanes <= pixelCount; pixel += lanes) {
                var r = new Vector<float>(red.Slice(pixel, lanes));
                var g = new Vector<float>(green.Slice(pixel, lanes));
                var b = new Vector<float>(blue.Slice(pixel, lanes));

                var ordered = Vector.Equals(r, r) & Vector.Equals(g, g) & Vector.Equals(b, b);

                r = Vector.Max(zero, r);
                g = Vector.Max(zero, g);
                b = Vector.Max(zero, b);
                var maximum = Vector.Max(r, Vector.Max(g, b));

                var maximumBits = Vector.As<float, uint>(maximum);
                var field = (maximumBits >> 23) & new Vector<uint>(0xFFu);
                var exponent = Vector.AsVectorInt32(field) - new Vector<int>(126);
                var clamped = Vector.Min(Vector.Max(exponent, lowestExponent), highestExponent);
                var scale = Vector.As<uint, float>(Vector.As<int, uint>(new Vector<int>(135) - clamped) << 23);

                var finite = Vector.LessThan(maximumBits & exponentField, exponentField);
                var representable = Vector.GreaterThanOrEqual(maximum, minimum);
                var keep = Vector.As<int, uint>(ordered) & finite & Vector.As<int, uint>(representable);

                var packed = Quantize(r, scale, saturated)
                    | (Quantize(g, scale, saturated) << 8)
                    | (Quantize(b, scale, saturated) << 16)
                    | (Vector.As<int, uint>(clamped + new Vector<int>(128)) << 24);

                (keep & packed).CopyTo(packedDestination.Slice(pixel, lanes));
            }
        }

        for (; pixel < pixelCount; pixel++) {
            var r = MathF.Max(0, red[pixel]);
            var g = MathF.Max(0, green[pixel]);
            var b = MathF.Max(0, blue[pixel]);
            EncodePixel(r, g, b, MathF.Max(r, MathF.Max(g, b)), destination[(pixel * 4)..]);
        }
    }

    private static Vector<uint> Quantize(Vector<float> value, Vector<float> scale, Vector<uint> saturated) =>
        Vector.Min(Vector.ConvertToUInt32(value * scale), saturated);

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
        if (!float.IsFinite(maximum) || maximum < k_EncodeMinimum) {
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
