using System.Runtime.CompilerServices;

namespace Lucitex.Webp.Lossless;

internal static class Vp8LTransforms
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Add(uint a, uint b)
        => (((a & 0x00ff00ff) + (b & 0x00ff00ff)) & 0x00ff00ff) |
           (((a & 0xff00ff00) + (b & 0xff00ff00)) & 0xff00ff00);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Subtract(uint a, uint b)
        => (((a | 0xff00ff00) - (b & 0x00ff00ff)) & 0x00ff00ff) |
           (((((a >> 8) | 0xff00ff00) - ((b >> 8) & 0x00ff00ff)) & 0x00ff00ff) << 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Average(uint a, uint b) => (a & b) + (((a ^ b) & 0xfefefefe) >> 1);

    public static uint Predict(int mode, uint left, uint top, uint topLeft, uint topRight)
        => mode switch {
            0 => 0xff000000,
            1 => left,
            2 => top,
            3 => topRight,
            4 => topLeft,
            5 => Average(Average(left, topRight), top),
            6 => Average(left, topLeft),
            7 => Average(left, top),
            8 => Average(topLeft, top),
            9 => Average(top, topRight),
            10 => Average(Average(left, topLeft), Average(top, topRight)),
            11 => Select(left, top, topLeft),
            12 => Clamp(left, top, topLeft, false),
            13 => Clamp(Average(left, top), 0, topLeft, true),
            _ => throw new InvalidDataException("Invalid VP8L predictor mode."),
        };

    private static uint Select(uint left, uint top, uint topLeft)
    {
        var leftDistance = 0;
        var topDistance = 0;
        for (var shift = 0; shift < 32; shift += 8) {
            var corner = (int)((topLeft >> shift) & 255);
            leftDistance += Math.Abs((int)((top >> shift) & 255) - corner);
            topDistance += Math.Abs((int)((left >> shift) & 255) - corner);
        }
        return leftDistance < topDistance ? left : top;
    }

    private static uint Clamp(uint a, uint b, uint c, bool half)
    {
        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8) {
            var av = (int)((a >> shift) & 255);
            var bv = (int)((b >> shift) & 255);
            var cv = (int)((c >> shift) & 255);
            var value = half ? av + ((av - cv) / 2) : av + bv - cv;
            result |= (uint)Math.Clamp(value, 0, 255) << shift;
        }
        return result;
    }

    public static void InversePredictor(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> modes, int blockBits)
    {
        var modeWidth = Subsample(width, blockBits);
        pixels[0] = Add(pixels[0], 0xff000000);
        for (var x = 1; x < width; x++) {
            pixels[x] = Add(pixels[x], pixels[x - 1]);
        }
        for (var y = 1; y < height; y++) {
            var row = y * width;
            pixels[row] = Add(pixels[row], pixels[row - width]);
            for (var x = 1; x < width; x++) {
                var index = row + x;
                var mode = (int)((modes[((y >> blockBits) * modeWidth) + (x >> blockBits)] >> 8) & 15);
                var prediction = Predict(mode, pixels[index - 1], pixels[index - width], pixels[index - width - 1], pixels[index - width + 1]);
                pixels[index] = Add(pixels[index], prediction);
            }
        }
    }

    public static void InverseColor(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> coefficients, int blockBits)
    {
        var blockWidth = Subsample(width, blockBits);
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var index = (y * width) + x;
                var pixel = pixels[index];
                var c = coefficients[((y >> blockBits) * blockWidth) + (x >> blockBits)];
                var green = (sbyte)(pixel >> 8);
                var red = (byte)((pixel >> 16) + (((sbyte)c * green) >> 5));
                var blue = (byte)(pixel + (((sbyte)(c >> 8) * green) >> 5) + (((sbyte)(c >> 16) * (sbyte)red) >> 5));
                pixels[index] = (pixel & 0xff00ff00) | ((uint)red << 16) | blue;
            }
        }
    }

    public static void AddGreen(Span<uint> pixels)
    {
        for (var i = 0; i < pixels.Length; i++) {
            var green = (pixels[i] >> 8) & 255;
            pixels[i] = Add(pixels[i], green | (green << 16));
        }
    }

    public static void SubtractGreen(Span<uint> pixels)
    {
        for (var i = 0; i < pixels.Length; i++) {
            var green = (pixels[i] >> 8) & 255;
            pixels[i] = Subtract(pixels[i], green | (green << 16));
        }
    }

    public static void ExpandPalette(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> palette, int widthBits)
    {
        var packedWidth = Subsample(width, widthBits);
        var indexBits = 8 >> widthBits;
        var indexMask = (1 << indexBits) - 1;
        var pixelMask = (1 << widthBits) - 1;
        for (var y = height - 1; y >= 0; y--) {
            for (var x = width - 1; x >= 0; x--) {
                var packed = pixels[(y * packedWidth) + (x >> widthBits)] >> 8;
                var index = (int)(packed >> ((x & pixelMask) * indexBits)) & indexMask;
                pixels[(y * width) + x] = index < palette.Length ? palette[index] : 0;
            }
        }
    }

    public static int Subsample(int value, int bits) => (value + (1 << bits) - 1) >> bits;
}
