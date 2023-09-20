using System.Numerics;
using System.Runtime.InteropServices;
using Lucitex.Core.Representation;

namespace Lucitex.Conversion.Kernels;

internal static class PackedPixelConversionKernel
{
    public static void Decode(
        EncodedFormatId format,
        ReadOnlySpan<byte> source,
        Span<float> red,
        Span<float> green,
        Span<float> blue,
        Span<float> alpha = default)
    {
        var bytesPerPixel = BytesPerPixel(format);
        var pixelCount = source.Length / bytesPerPixel;
        var hasAlpha = format.Name is nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G5R5A1);
        if (source.Length % bytesPerPixel != 0 || red.Length < pixelCount || green.Length < pixelCount || blue.Length < pixelCount ||
            (hasAlpha && alpha.Length < pixelCount)) {
            throw new ArgumentException("Packed pixel buffers have incompatible lengths.");
        }

        if (format.Name is nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1)) {
            DecodeBgr16(format, MemoryMarshal.Cast<byte, ushort>(source), red, green, blue, alpha);
            return;
        }

        var words = MemoryMarshal.Cast<byte, uint>(source);
        if (format == EncodedFormatId.R10G10B10A2) {
            DecodeR10G10B10A2(words, red, green, blue, alpha);
            return;
        }

        if (format == EncodedFormatId.R11G11B10Float) {
            DecodeR11G11B10(words, red, green, blue);
            return;
        }

        if (format == EncodedFormatId.Rgb9E5) {
            DecodeRgb9E5(words, red, green, blue);
            return;
        }

        throw new NotSupportedException($"Packed format '{format}' is not supported.");
    }

    public static void Encode(
        EncodedFormatId format,
        ReadOnlySpan<float> red,
        ReadOnlySpan<float> green,
        ReadOnlySpan<float> blue,
        ReadOnlySpan<float> alpha,
        Span<byte> destination)
    {
        var bytesPerPixel = BytesPerPixel(format);
        var pixelCount = red.Length;
        var hasAlpha = format.Name is nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G5R5A1);
        if (green.Length != pixelCount || blue.Length != pixelCount || destination.Length < pixelCount * bytesPerPixel ||
            (hasAlpha && alpha.Length != pixelCount)) {
            throw new ArgumentException("Packed pixel buffers have incompatible lengths.");
        }

        if (format.Name is nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1)) {
            EncodeBgr16(format, red, green, blue, alpha, MemoryMarshal.Cast<byte, ushort>(destination[..(pixelCount * bytesPerPixel)]));
            return;
        }

        var words = MemoryMarshal.Cast<byte, uint>(destination[..(pixelCount * sizeof(uint))]);
        if (format == EncodedFormatId.R10G10B10A2) {
            EncodeR10G10B10A2(red, green, blue, alpha, words);
            return;
        }

        if (format == EncodedFormatId.R11G11B10Float) {
            EncodeR11G11B10(red, green, blue, words);
            return;
        }

        if (format == EncodedFormatId.Rgb9E5) {
            EncodeRgb9E5(red, green, blue, words);
            return;
        }

        throw new NotSupportedException($"Packed format '{format}' is not supported.");
    }

    private static void DecodeBgr16(
        EncodedFormatId format,
        ReadOnlySpan<ushort> source,
        Span<float> red,
        Span<float> green,
        Span<float> blue,
        Span<float> alpha)
    {
        var i = 0;
        var ushortLanes = Vector<ushort>.Count;
        if (Vector.IsHardwareAccelerated) {
            var floatLanes = Vector<float>.Count;
            for (; i <= source.Length - ushortLanes; i += ushortLanes) {
                var packed = new Vector<ushort>(source.Slice(i, ushortLanes));
                Vector.Widen(packed, out var lo, out var hi);
                DecodeBgr16Vector(format, lo, red.Slice(i, floatLanes), green.Slice(i, floatLanes), blue.Slice(i, floatLanes), alpha.Length == 0 ? [] : alpha.Slice(i, floatLanes));
                DecodeBgr16Vector(format, hi, red.Slice(i + floatLanes, floatLanes), green.Slice(i + floatLanes, floatLanes), blue.Slice(i + floatLanes, floatLanes), alpha.Length == 0 ? [] : alpha.Slice(i + floatLanes, floatLanes));
            }
        }

        for (; i < source.Length; i++) {
            var packed = source[i];
            blue[i] = (packed & 0x1fu) * (1f / 31f);
            if (format == EncodedFormatId.B5G6R5) {
                green[i] = ((packed >> 5) & 0x3fu) * (1f / 63f);
                red[i] = ((packed >> 11) & 0x1fu) * (1f / 31f);
            }
            else {
                green[i] = ((packed >> 5) & 0x1fu) * (1f / 31f);
                red[i] = ((packed >> 10) & 0x1fu) * (1f / 31f);
                alpha[i] = packed >> 15;
            }
        }
    }

    private static void DecodeBgr16Vector(
        EncodedFormatId format,
        Vector<uint> packed,
        Span<float> red,
        Span<float> green,
        Span<float> blue,
        Span<float> alpha)
    {
        var mask5 = new Vector<uint>(0x1fu);
        (Vector.ConvertToSingle(packed & mask5) * (1f / 31f)).CopyTo(blue);
        if (format == EncodedFormatId.B5G6R5) {
            (Vector.ConvertToSingle((packed >> 5) & new Vector<uint>(0x3fu)) * (1f / 63f)).CopyTo(green);
            (Vector.ConvertToSingle((packed >> 11) & mask5) * (1f / 31f)).CopyTo(red);
        }
        else {
            (Vector.ConvertToSingle((packed >> 5) & mask5) * (1f / 31f)).CopyTo(green);
            (Vector.ConvertToSingle((packed >> 10) & mask5) * (1f / 31f)).CopyTo(red);
            Vector.ConvertToSingle(packed >> 15).CopyTo(alpha);
        }
    }

    private static void EncodeBgr16(
        EncodedFormatId format,
        ReadOnlySpan<float> red,
        ReadOnlySpan<float> green,
        ReadOnlySpan<float> blue,
        ReadOnlySpan<float> alpha,
        Span<ushort> destination)
    {
        var i = 0;
        var lanes = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= red.Length - (2 * lanes); i += 2 * lanes) {
                var lo = EncodeBgr16Vector(format, red.Slice(i, lanes), green.Slice(i, lanes), blue.Slice(i, lanes), alpha.Length == 0 ? [] : alpha.Slice(i, lanes));
                var hi = EncodeBgr16Vector(format, red.Slice(i + lanes, lanes), green.Slice(i + lanes, lanes), blue.Slice(i + lanes, lanes), alpha.Length == 0 ? [] : alpha.Slice(i + lanes, lanes));
                Vector.Narrow(lo, hi).CopyTo(destination[i..]);
            }
        }

        for (; i < red.Length; i++) {
            var r = QuantizeUNorm(red[i], 31);
            var b = QuantizeUNorm(blue[i], 31);
            if (format == EncodedFormatId.B5G6R5) {
                destination[i] = (ushort)(b | (QuantizeUNorm(green[i], 63) << 5) | (r << 11));
            }
            else {
                destination[i] = (ushort)(b | (QuantizeUNorm(green[i], 31) << 5) | (r << 10) | (QuantizeUNorm(alpha[i], 1) << 15));
            }
        }
    }

    private static Vector<uint> EncodeBgr16Vector(
        EncodedFormatId format,
        ReadOnlySpan<float> red,
        ReadOnlySpan<float> green,
        ReadOnlySpan<float> blue,
        ReadOnlySpan<float> alpha)
    {
        var r = QuantizeUNorm(new Vector<float>(red), 31);
        var b = QuantizeUNorm(new Vector<float>(blue), 31);
        if (format == EncodedFormatId.B5G6R5) {
            return b | (QuantizeUNorm(new Vector<float>(green), 63) << 5) | (r << 11);
        }

        return b | (QuantizeUNorm(new Vector<float>(green), 31) << 5) | (r << 10) | (QuantizeUNorm(new Vector<float>(alpha), 1) << 15);
    }

    private static void DecodeR10G10B10A2(
        ReadOnlySpan<uint> source,
        Span<float> red,
        Span<float> green,
        Span<float> blue,
        Span<float> alpha)
    {
        var i = 0;
        var lanes = Vector<uint>.Count;
        if (Vector.IsHardwareAccelerated) {
            var rgbScale = new Vector<float>(1f / 1023f);
            var alphaScale = new Vector<float>(1f / 3f);
            var rgbMask = new Vector<uint>(0x3ffu);
            for (; i <= source.Length - lanes; i += lanes) {
                var packed = new Vector<uint>(source.Slice(i, lanes));
                (Vector.ConvertToSingle(packed & rgbMask) * rgbScale).CopyTo(red[i..]);
                (Vector.ConvertToSingle((packed >> 10) & rgbMask) * rgbScale).CopyTo(green[i..]);
                (Vector.ConvertToSingle((packed >> 20) & rgbMask) * rgbScale).CopyTo(blue[i..]);
                (Vector.ConvertToSingle(packed >> 30) * alphaScale).CopyTo(alpha[i..]);
            }
        }

        for (; i < source.Length; i++) {
            var packed = source[i];
            red[i] = (packed & 0x3ffu) * (1f / 1023f);
            green[i] = ((packed >> 10) & 0x3ffu) * (1f / 1023f);
            blue[i] = ((packed >> 20) & 0x3ffu) * (1f / 1023f);
            alpha[i] = (packed >> 30) * (1f / 3f);
        }
    }

    private static void EncodeR10G10B10A2(
        ReadOnlySpan<float> red,
        ReadOnlySpan<float> green,
        ReadOnlySpan<float> blue,
        ReadOnlySpan<float> alpha,
        Span<uint> destination)
    {
        var i = 0;
        var lanes = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated) {
            var zero = Vector<float>.Zero;
            var one = Vector<float>.One;
            var half = new Vector<float>(0.5f);
            for (; i <= red.Length - lanes; i += lanes) {
                var r = Vector.ConvertToUInt32((Vector.Min(Vector.Max(new Vector<float>(red.Slice(i, lanes)), zero), one) * 1023f) + half);
                var g = Vector.ConvertToUInt32((Vector.Min(Vector.Max(new Vector<float>(green.Slice(i, lanes)), zero), one) * 1023f) + half);
                var b = Vector.ConvertToUInt32((Vector.Min(Vector.Max(new Vector<float>(blue.Slice(i, lanes)), zero), one) * 1023f) + half);
                var a = Vector.ConvertToUInt32((Vector.Min(Vector.Max(new Vector<float>(alpha.Slice(i, lanes)), zero), one) * 3f) + half);
                (r | (g << 10) | (b << 20) | (a << 30)).CopyTo(destination[i..]);
            }
        }

        for (; i < red.Length; i++) {
            var r = QuantizeUNorm(red[i], 1023);
            var g = QuantizeUNorm(green[i], 1023);
            var b = QuantizeUNorm(blue[i], 1023);
            var a = QuantizeUNorm(alpha[i], 3);
            destination[i] = r | (g << 10) | (b << 20) | (a << 30);
        }
    }

    private static void DecodeR11G11B10(ReadOnlySpan<uint> source, Span<float> red, Span<float> green, Span<float> blue)
    {
        var i = 0;
        var lanes = Vector<uint>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= source.Length - lanes; i += lanes) {
                var packed = new Vector<uint>(source.Slice(i, lanes));
                DecodeUnsignedFloat(packed & new Vector<uint>(0x7ffu), 6).CopyTo(red[i..]);
                DecodeUnsignedFloat((packed >> 11) & new Vector<uint>(0x7ffu), 6).CopyTo(green[i..]);
                DecodeUnsignedFloat(packed >> 22, 5).CopyTo(blue[i..]);
            }
        }

        for (; i < source.Length; i++) {
            var packed = source[i];
            red[i] = DecodeUnsignedFloat(packed & 0x7ffu, 6);
            green[i] = DecodeUnsignedFloat((packed >> 11) & 0x7ffu, 6);
            blue[i] = DecodeUnsignedFloat(packed >> 22, 5);
        }
    }

    private static void EncodeR11G11B10(
        ReadOnlySpan<float> red,
        ReadOnlySpan<float> green,
        ReadOnlySpan<float> blue,
        Span<uint> destination)
    {
        var i = 0;
        var lanes = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated) {
            Span<float> r = stackalloc float[lanes];
            Span<float> g = stackalloc float[lanes];
            Span<float> b = stackalloc float[lanes];
            for (; i <= red.Length - lanes; i += lanes) {
                Vector.Max(Vector<float>.Zero, new Vector<float>(red.Slice(i, lanes))).CopyTo(r);
                Vector.Max(Vector<float>.Zero, new Vector<float>(green.Slice(i, lanes))).CopyTo(g);
                Vector.Max(Vector<float>.Zero, new Vector<float>(blue.Slice(i, lanes))).CopyTo(b);
                for (var lane = 0; lane < lanes; lane++) {
                    destination[i + lane] = EncodeUnsignedFloat(r[lane], 6) |
                        (EncodeUnsignedFloat(g[lane], 6) << 11) |
                        (EncodeUnsignedFloat(b[lane], 5) << 22);
                }
            }
        }

        for (; i < red.Length; i++) {
            destination[i] = EncodeUnsignedFloat(red[i], 6) |
                (EncodeUnsignedFloat(green[i], 6) << 11) |
                (EncodeUnsignedFloat(blue[i], 5) << 22);
        }
    }

    private static void DecodeRgb9E5(ReadOnlySpan<uint> source, Span<float> red, Span<float> green, Span<float> blue)
    {
        var i = 0;
        var lanes = Vector<uint>.Count;
        if (Vector.IsHardwareAccelerated) {
            var mask = new Vector<uint>(0x1ffu);
            for (; i <= source.Length - lanes; i += lanes) {
                var packed = new Vector<uint>(source.Slice(i, lanes));
                var scaleBits = ((packed >> 27) + new Vector<uint>(103u)) << 23;
                var scale = Vector.As<uint, float>(scaleBits);
                (Vector.ConvertToSingle(packed & mask) * scale).CopyTo(red[i..]);
                (Vector.ConvertToSingle((packed >> 9) & mask) * scale).CopyTo(green[i..]);
                (Vector.ConvertToSingle((packed >> 18) & mask) * scale).CopyTo(blue[i..]);
            }
        }

        for (; i < source.Length; i++) {
            var packed = source[i];
            var scale = MathF.ScaleB(1f, (int)(packed >> 27) - 24);
            red[i] = (packed & 0x1ffu) * scale;
            green[i] = ((packed >> 9) & 0x1ffu) * scale;
            blue[i] = ((packed >> 18) & 0x1ffu) * scale;
        }
    }

    private static void EncodeRgb9E5(
        ReadOnlySpan<float> red,
        ReadOnlySpan<float> green,
        ReadOnlySpan<float> blue,
        Span<uint> destination)
    {
        var i = 0;
        var lanes = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated) {
            Span<float> r = stackalloc float[lanes];
            Span<float> g = stackalloc float[lanes];
            Span<float> b = stackalloc float[lanes];
            for (; i <= red.Length - lanes; i += lanes) {
                ClampSharedExponent(new Vector<float>(red.Slice(i, lanes))).CopyTo(r);
                ClampSharedExponent(new Vector<float>(green.Slice(i, lanes))).CopyTo(g);
                ClampSharedExponent(new Vector<float>(blue.Slice(i, lanes))).CopyTo(b);
                for (var lane = 0; lane < lanes; lane++) {
                    destination[i + lane] = EncodeRgb9E5Pixel(r[lane], g[lane], b[lane]);
                }
            }
        }

        for (; i < red.Length; i++) {
            destination[i] = EncodeRgb9E5Pixel(red[i], green[i], blue[i]);
        }
    }

    private static Vector<float> DecodeUnsignedFloat(Vector<uint> value, int mantissaBits)
    {
        var mantissaMask = new Vector<uint>((1u << mantissaBits) - 1u);
        var mantissa = value & mantissaMask;
        var exponent = value >> mantissaBits;
        var normalBits = ((exponent + new Vector<uint>(112u)) << 23) | (mantissa << (23 - mantissaBits));
        var specialBits = new Vector<uint>(0x7f800000u) | (mantissa << (23 - mantissaBits));
        var subnormal = Vector.ConvertToSingle(mantissa) * new Vector<float>(MathF.ScaleB(1f, -14 - mantissaBits));
        var normal = Vector.As<uint, float>(normalBits);
        var special = Vector.As<uint, float>(specialBits);
        var zeroMask = Vector.Equals(exponent, Vector<uint>.Zero);
        var specialMask = Vector.Equals(exponent, new Vector<uint>(31u));
        return Vector.ConditionalSelect(
            Vector.As<uint, int>(specialMask),
            special,
            Vector.ConditionalSelect(Vector.As<uint, int>(zeroMask), subnormal, normal));
    }

    private static float DecodeUnsignedFloat(uint value, int mantissaBits)
    {
        var halfBits = (ushort)(value << (10 - mantissaBits));
        return (float)BitConverter.UInt16BitsToHalf(halfBits);
    }

    private static uint EncodeUnsignedFloat(float value, int mantissaBits)
    {
        if (float.IsNaN(value)) {
            return (31u << mantissaBits) | (1u << (mantissaBits - 1));
        }

        if (value <= 0) {
            return 0;
        }

        if (float.IsPositiveInfinity(value)) {
            return 31u << mantissaBits;
        }

        var minNormal = MathF.ScaleB(1f, -14);
        if (value < minNormal) {
            var subnormal = (uint)MathF.Round(MathF.ScaleB(value, 14 + mantissaBits), MidpointRounding.ToEven);
            return Math.Min(subnormal, 1u << mantissaBits);
        }

        var exponent = MathF.ILogB(value);
        var biasedExponent = exponent + 15;
        var scaled = MathF.ScaleB(value, -exponent);
        var mantissa = (uint)MathF.Round((scaled - 1f) * (1 << mantissaBits), MidpointRounding.ToEven);
        if (mantissa == 1u << mantissaBits) {
            mantissa = 0;
            biasedExponent++;
        }

        if (biasedExponent >= 31) {
            return 31u << mantissaBits;
        }

        return ((uint)biasedExponent << mantissaBits) | mantissa;
    }

    private static uint EncodeRgb9E5Pixel(float red, float green, float blue)
    {
        red = ClampSharedExponent(red);
        green = ClampSharedExponent(green);
        blue = ClampSharedExponent(blue);
        var maximum = MathF.Max(red, MathF.Max(green, blue));
        var provisionalExponent = Math.Max(-16, MathF.ILogB(maximum)) + 16;
        var provisionalScale = MathF.ScaleB(1f, provisionalExponent - 24);
        var maximumMantissa = (uint)MathF.Floor((maximum / provisionalScale) + 0.5f);
        var exponent = maximumMantissa == 512 ? provisionalExponent + 1 : provisionalExponent;
        exponent = Math.Clamp(exponent, 0, 31);
        var scale = MathF.ScaleB(1f, exponent - 24);
        var r = (uint)Math.Clamp(MathF.Floor((red / scale) + 0.5f), 0f, 511f);
        var g = (uint)Math.Clamp(MathF.Floor((green / scale) + 0.5f), 0f, 511f);
        var b = (uint)Math.Clamp(MathF.Floor((blue / scale) + 0.5f), 0f, 511f);
        return r | (g << 9) | (b << 18) | ((uint)exponent << 27);
    }

    private static uint QuantizeUNorm(float value, uint maximum) =>
        (uint)Math.Clamp(MathF.Floor((Math.Clamp(value, 0f, 1f) * maximum) + 0.5f), 0f, maximum);

    private static Vector<uint> QuantizeUNorm(Vector<float> value, uint maximum)
    {
        var finite = Vector.ConditionalSelect(Vector.Equals(value, value), value, Vector<float>.Zero);
        var scaled = (Vector.Min(Vector.Max(finite, Vector<float>.Zero), Vector<float>.One) * maximum) + new Vector<float>(0.5f);
        return Vector.ConvertToUInt32(scaled);
    }

    private static Vector<float> ClampSharedExponent(Vector<float> value)
    {
        var finite = Vector.Min(Vector.Max(Vector<float>.Zero, value), new Vector<float>(65408f));
        return Vector.ConditionalSelect(Vector.Equals(value, value), finite, Vector<float>.Zero);
    }

    private static float ClampSharedExponent(float value) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 65408f);

    private static int BytesPerPixel(EncodedFormatId format) => format.Name switch {
        nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1) => sizeof(ushort),
        nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5) => sizeof(uint),
        _ => throw new NotSupportedException($"Packed format '{format}' is not supported."),
    };
}
