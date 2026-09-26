using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Select(uint left, uint top, uint topLeft)
    {
        if (Sse2.IsSupported) {
            var corner = Vector128.CreateScalar(topLeft).AsByte();
            var leftError = Sse2.SumAbsoluteDifferences(Vector128.CreateScalar(top).AsByte(), corner).AsUInt64().ToScalar();
            var topError = Sse2.SumAbsoluteDifferences(Vector128.CreateScalar(left).AsByte(), corner).AsUInt64().ToScalar();
            return leftError < topError ? left : top;
        }
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
        if (Sse2.IsSupported) {
            return ClampBytes(a, b, c, half).AsUInt32().ToScalar();
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> ClampBytes(uint a, uint b, uint c, bool half)
    {
        var av = Vector128.WidenLower(Vector128.CreateScalar(a).AsByte()).AsInt16();
        var cv = Vector128.WidenLower(Vector128.CreateScalar(c).AsByte()).AsInt16();
        Vector128<short> value;
        if (half) {
            var difference = av - cv;
            value = av + ((difference + (difference >>> 15)) >> 1);
        }
        else {
            var bv = Vector128.WidenLower(Vector128.CreateScalar(b).AsByte()).AsInt16();
            value = av + bv - cv;
        }
        return Sse2.PackUnsignedSaturate(value, Vector128<short>.Zero);
    }

    private static bool IsLeftIndependent(int mode) => mode is 0 or 2 or 3 or 4 or 8 or 9;

    private static readonly Vector128<uint> s_AverageMask = Vector128.Create(0xfefefefeu);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> AddVec(Vector128<uint> a, Vector128<uint> b)
        => (a.AsByte() + b.AsByte()).AsUInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> AverageVec(Vector128<uint> a, Vector128<uint> b)
        => (a & b) + (((a ^ b) & s_AverageMask) >>> 1);

    public static void InversePredictor(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> modes, int blockBits)
    {
        var modeWidth = Subsample(width, blockBits);
        pixels[0] = Add(pixels[0], 0xff000000);
        InverseLeftChainRow(pixels[..width]);

        for (var y = 1; y < height; y++) {
            var row = y * width;
            pixels[row] = Add(pixels[row], pixels[row - width]);
            var x = 1;
            while (x < width) {
                var blockX = x >> blockBits;
                var mode = (int)((modes[((y >> blockBits) * modeWidth) + blockX] >> 8) & 15);
                var blockEnd = Math.Min(width, (blockX + 1) << blockBits);

                if (mode == 1 && Vector128.IsHardwareAccelerated) {
                    pixels[row + x] = Add(pixels[row + x], pixels[row + x - 1]);
                    InverseLeftChainRow(pixels.Slice(row + x, blockEnd - x));
                    x = blockEnd;
                    continue;
                }

                if (Sse2.IsSupported && mode is >= 11 and <= 13) {
                    var left = pixels[row + x - 1];
                    for (; x < blockEnd; x++) {
                        var index = row + x;
                        var top = pixels[index - width];
                        var corner = pixels[index - width - 1];
                        var prediction = mode == 11
                            ? Vector128.CreateScalar(Select(left, top, corner)).AsByte()
                            : ClampBytes(mode == 13 ? Average(left, top) : left, mode == 13 ? 0 : top, corner, mode == 13);
                        left = (Vector128.CreateScalar(pixels[index]).AsByte() + prediction).AsUInt32().ToScalar();
                        pixels[index] = left;
                    }
                    continue;
                }

                var simdEnd = Math.Min(blockEnd, width - 1);
                if (IsLeftIndependent(mode) && Vector128.IsHardwareAccelerated) {
                    for (; x + 4 <= simdEnd; x += 4) {
                        var index = row + x;
                        var current = Vector128.Create(pixels.Slice(index, 4));
                        var prediction = mode switch {
                            0 => Vector128.Create(0xff000000u),
                            2 => Vector128.Create(pixels.Slice(index - width, 4)),
                            3 => Vector128.Create(pixels.Slice(index - width + 1, 4)),
                            4 => Vector128.Create(pixels.Slice(index - width - 1, 4)),
                            8 => AverageVec(Vector128.Create(pixels.Slice(index - width - 1, 4)), Vector128.Create(pixels.Slice(index - width, 4))),
                            _ => AverageVec(Vector128.Create(pixels.Slice(index - width, 4)), Vector128.Create(pixels.Slice(index - width + 1, 4))),
                        };
                        AddVec(current, prediction).CopyTo(pixels.Slice(index, 4));
                    }
                }
                for (; x < blockEnd; x++) {
                    var index = row + x;
                    var prediction = Predict(mode, pixels[index - 1], pixels[index - width], pixels[index - width - 1], pixels[index - width + 1]);
                    pixels[index] = Add(pixels[index], prediction);
                }
            }
        }
    }

    private static void InverseLeftChainRow(Span<uint> row)
    {
        var bytes = MemoryMarshal.AsBytes(row);
        var i = 0;
        if (Ssse3.IsSupported) {
            var carryMask = Vector128.Create((byte)12, 13, 14, 15, 12, 13, 14, 15, 12, 13, 14, 15, 12, 13, 14, 15);
            var carry = Vector128<byte>.Zero;
            for (; i <= bytes.Length - 16; i += 16) {
                var values = Vector128.Create(bytes.Slice(i, 16));
                values += Sse2.ShiftLeftLogical128BitLane(values, 4);
                values += Sse2.ShiftLeftLogical128BitLane(values, 8);
                values += carry;
                values.CopyTo(bytes.Slice(i, 16));
                carry = Ssse3.Shuffle(values, carryMask);
            }
        }
        for (i = Math.Max(i, 4); i < bytes.Length; i += 4) {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(i, 4),
                Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i, 4)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i - 4, 4))));
        }
    }

    private static readonly Vector128<byte> s_ExtractB = Vector128.Create((byte)0, 4, 8, 12, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_ExtractG = Vector128.Create((byte)1, 5, 9, 13, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_ExtractR = Vector128.Create((byte)2, 6, 10, 14, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_PlaceB = Vector128.Create((byte)0, 128, 128, 128, 1, 128, 128, 128, 2, 128, 128, 128, 3, 128, 128, 128);
    private static readonly Vector128<byte> s_PlaceR = Vector128.Create((byte)128, 128, 0, 128, 128, 128, 1, 128, 128, 128, 2, 128, 128, 128, 3, 128);
    private static readonly Vector128<byte> s_KeepGa = Vector128.Create((byte)0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255);

    public static void InverseColor(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> coefficients, int blockBits)
    {
        var blockWidth = Subsample(width, blockBits);
        var bytes = MemoryMarshal.AsBytes(pixels);
        for (var y = 0; y < height; y++) {
            var blockRow = (y >> blockBits) * blockWidth;
            var rowByteOffset = y * width * 4;
            var x = 0;
            while (x < width) {
                var blockX = x >> blockBits;
                var c = coefficients[blockRow + blockX];
                var blockEnd = Math.Min(width, (blockX + 1) << blockBits);
                InverseColorRun(bytes, rowByteOffset + (x * 4), blockEnd - x, (sbyte)c, (sbyte)(c >> 8), (sbyte)(c >> 16));
                x = blockEnd;
            }
        }
    }

    private static void InverseColorRun(Span<byte> bytes, int byteOffset, int count, short greenToRed, short greenToBlue, short redToBlue)
    {
        var i = 0;
        if (Ssse3.IsSupported) {
            var greenToRedVec = Vector128.Create(greenToRed);
            var greenToBlueVec = Vector128.Create(greenToBlue);
            var redToBlueVec = Vector128.Create(redToBlue);
            for (; i + 4 <= count; i += 4) {
                var chunk = Vector128.Create(bytes.Slice(byteOffset + (i * 4), 16));
                var green = Vector128.WidenLower(Ssse3.Shuffle(chunk, s_ExtractG).AsSByte());
                var blue = Vector128.WidenLower(Ssse3.Shuffle(chunk, s_ExtractB)).AsInt16();
                var red = Vector128.WidenLower(Ssse3.Shuffle(chunk, s_ExtractR)).AsInt16();

                var newRed = (red + ((green * greenToRedVec) >> 5)) & Vector128.Create((short)0xFF);
                var redSigned = (newRed << 8) >> 8;
                var newBlue = (blue + ((green * greenToBlueVec) >> 5) + ((redSigned * redToBlueVec) >> 5)) & Vector128.Create((short)0xFF);

                var redBytes = Sse2.PackUnsignedSaturate(newRed, Vector128<short>.Zero);
                var blueBytes = Sse2.PackUnsignedSaturate(newBlue, Vector128<short>.Zero);
                var result = (chunk & s_KeepGa) | Ssse3.Shuffle(blueBytes, s_PlaceB) | Ssse3.Shuffle(redBytes, s_PlaceR);
                result.CopyTo(bytes.Slice(byteOffset + (i * 4), 16));
            }
        }
        for (; i < count; i++) {
            var pixelOffset = byteOffset + (i * 4);
            var green = (sbyte)bytes[pixelOffset + 1];
            var red = (byte)(bytes[pixelOffset + 2] + ((greenToRed * green) >> 5));
            var blue = (byte)(bytes[pixelOffset] + ((greenToBlue * green) >> 5) + ((redToBlue * (sbyte)red) >> 5));
            bytes[pixelOffset] = blue;
            bytes[pixelOffset + 2] = red;
        }
    }

    private static readonly Vector256<byte> s_GreenBroadcastMask = Vector256.Create(
        (byte)1, 128, 1, 128, 5, 128, 5, 128, 9, 128, 9, 128, 13, 128, 13, 128,
        1, 128, 1, 128, 5, 128, 5, 128, 9, 128, 9, 128, 13, 128, 13, 128);

    public static void AddGreen(Span<uint> pixels)
    {
        var bytes = MemoryMarshal.AsBytes(pixels);
        var i = 0;
        if (Avx2.IsSupported) {
            for (; i <= bytes.Length - 32; i += 32) {
                var chunk = Vector256.Create(bytes.Slice(i, 32));
                var green = Avx2.Shuffle(chunk, s_GreenBroadcastMask);
                (chunk + green).CopyTo(bytes.Slice(i, 32));
            }
        }
        for (; i < bytes.Length; i += 4) {
            var green = bytes[i + 1];
            bytes[i] = (byte)(bytes[i] + green);
            bytes[i + 2] = (byte)(bytes[i + 2] + green);
        }
    }

    public static void SubtractGreen(Span<uint> pixels)
    {
        var bytes = MemoryMarshal.AsBytes(pixels);
        var i = 0;
        if (Avx2.IsSupported) {
            for (; i <= bytes.Length - 32; i += 32) {
                var chunk = Vector256.Create(bytes.Slice(i, 32));
                var green = Avx2.Shuffle(chunk, s_GreenBroadcastMask);
                (chunk - green).CopyTo(bytes.Slice(i, 32));
            }
        }
        for (; i < bytes.Length; i += 4) {
            var green = bytes[i + 1];
            bytes[i] = (byte)(bytes[i] - green);
            bytes[i + 2] = (byte)(bytes[i + 2] - green);
        }
    }

    public static void ExpandPalette(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> palette, int widthBits)
    {
        if (widthBits != 0 && width >= 8 && pixels.Length >= 4096) {
            ExpandPackedPalette(pixels, width, height, palette, widthBits);
            return;
        }
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

    private static void ExpandPackedPalette(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> palette, int widthBits)
    {
        var count = 1 << widthBits;
        var indexBits = 8 >> widthBits;
        var indexMask = (1 << indexBits) - 1;
        Span<uint> table = stackalloc uint[256 * count];
        for (var packed = 0; packed < 256; packed++) {
            for (var i = 0; i < count; i++) {
                var index = (packed >> (i * indexBits)) & indexMask;
                table[packed * count + i] = index < palette.Length ? palette[index] : 0;
            }
        }
        var packedWidth = Subsample(width, widthBits);
        for (var y = height - 1; y >= 0; y--) {
            for (var group = packedWidth - 1; group >= 0; group--) {
                var packed = (int)((pixels[y * packedWidth + group] >> 8) & 255);
                var x = group * count;
                var length = Math.Min(count, width - x);
                table.Slice(packed * count, length).CopyTo(pixels.Slice(y * width + x, length));
            }
        }
    }

    public static int Subsample(int value, int bits) => (value + (1 << bits) - 1) >> bits;
}
