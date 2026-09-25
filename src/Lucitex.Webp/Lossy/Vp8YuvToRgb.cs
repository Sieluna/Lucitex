using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lucitex.Webp.Lossy;

internal static class Vp8YuvToRgb
{
    private const int FixedBits = 16;
    private const int Half = 1 << (FixedBits - 1);
    private static readonly int YScale = (int)Math.Round(1.164383562 * (1 << FixedBits));
    private static readonly int CrToR = (int)Math.Round(1.596027 * (1 << FixedBits));
    private static readonly int CbToG = (int)Math.Round(-0.391762 * (1 << FixedBits));
    private static readonly int CrToG = (int)Math.Round(-0.812968 * (1 << FixedBits));
    private static readonly int CbToB = (int)Math.Round(2.017232 * (1 << FixedBits));

    [ThreadStatic]
    private static byte[]? t_uRow;
    [ThreadStatic]
    private static byte[]? t_vRow;

    private static byte Clamp255(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    public static void ConvertRowToRgba(Vp8DecodedFrame frame, int row, Span<byte> destination, ReadOnlySpan<byte> alphaRow)
    {
        var width = frame.Width;
        var chromaWidth = (width + 1) / 2;
        var chromaHeight = (frame.Height + 1) / 2;

        var uRow = t_uRow is { Length: var ul } buffer && ul >= width ? buffer : t_uRow = new byte[width];
        var vRow = t_vRow is { Length: var vl } buffer2 && vl >= width ? buffer2 : t_vRow = new byte[width];

        UpsampleChromaRow(frame.U, frame.UvStride, chromaWidth, chromaHeight, width, row, uRow);
        UpsampleChromaRow(frame.V, frame.UvStride, chromaWidth, chromaHeight, width, row, vRow);

        var luma = frame.Y.AsSpan(row * frame.YStride, width);
        ConvertRow(luma, uRow.AsSpan(0, width), vRow.AsSpan(0, width), alphaRow, destination);
    }

    private static void UpsampleChromaRow(byte[] plane, int stride, int chromaWidth, int chromaHeight, int outputWidth, int row, Span<byte> output)
    {
        var currentY = row / 2;
        var otherY = Math.Clamp(currentY + ((row & 1) == 0 ? -1 : 1), 0, chromaHeight - 1);
        var current = plane.AsSpan(currentY * stride, chromaWidth);
        var other = plane.AsSpan(otherY * stride, chromaWidth);
        var center = (current[0] * 3) + other[0];
        var left = center;
        var x = 0;
        if (Ssse3.IsSupported) {
            var interleave = Vector128.Create((byte)0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7, 15);
            for (; x + 8 < chromaWidth; x += 8) {
                var values = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(current), x))).AsByte()).AsInt16();
                var neighbors = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(other), x))).AsByte()).AsInt16();
                var centers = values + values + values + neighbors;
                var lefts = Sse2.ShiftLeftLogical128BitLane(centers.AsByte(), 2).AsInt16() | Vector128.CreateScalar((short)left);
                var next = (current[x + 8] * 3) + other[x + 8];
                var rights = Sse2.ShiftRightLogical128BitLane(centers.AsByte(), 2).AsInt16().WithElement(7, (short)next);
                var triple = centers + centers + centers;
                var even = (triple + lefts + Vector128.Create((short)8)) >> 4;
                var odd = (triple + rights + Vector128.Create((short)8)) >> 4;
                var packed = Sse2.PackUnsignedSaturate(even, odd);
                Ssse3.Shuffle(packed, interleave).CopyTo(output.Slice(x * 2, 16));
                left = centers.GetElement(7);
            }
            center = (current[x] * 3) + other[x];
        }
        for (; x < chromaWidth; x++) {
            var right = x + 1 < chromaWidth ? (current[x + 1] * 3) + other[x + 1] : center;
            var target = x * 2;
            output[target] = (byte)((center * 3 + left + 8) >> 4);
            if (target + 1 < outputWidth) {
                output[target + 1] = (byte)((center * 3 + right + 8) >> 4);
            }
            left = center;
            center = right;
        }
    }

    private static void ConvertRow(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> u, ReadOnlySpan<byte> v, ReadOnlySpan<byte> alphaRow, Span<byte> destination)
    {
        var width = luma.Length;
        var x = 0;
        if (Avx2.IsSupported) {
            var lowR = Vector128.Create((byte)0, 128, 128, 128, 1, 128, 128, 128, 2, 128, 128, 128, 3, 128, 128, 128);
            var lowG = Vector128.Create((byte)128, 0, 128, 128, 128, 1, 128, 128, 128, 2, 128, 128, 128, 3, 128, 128);
            var lowB = Vector128.Create((byte)128, 128, 0, 128, 128, 128, 1, 128, 128, 128, 2, 128, 128, 128, 3, 128);
            var lowA = Vector128.Create((byte)128, 128, 128, 0, 128, 128, 128, 1, 128, 128, 128, 2, 128, 128, 128, 3);
            var highR = Vector128.Create((byte)4, 128, 128, 128, 5, 128, 128, 128, 6, 128, 128, 128, 7, 128, 128, 128);
            var highG = Vector128.Create((byte)128, 4, 128, 128, 128, 5, 128, 128, 128, 6, 128, 128, 128, 7, 128, 128);
            var highB = Vector128.Create((byte)128, 128, 4, 128, 128, 128, 5, 128, 128, 128, 6, 128, 128, 128, 7, 128);
            var highA = Vector128.Create((byte)128, 128, 128, 4, 128, 128, 128, 5, 128, 128, 128, 6, 128, 128, 128, 7);
            var yScaleVec = Vector256.Create(YScale);
            var crToRVec = Vector256.Create(CrToR);
            var cbToGVec = Vector256.Create(CbToG);
            var crToGVec = Vector256.Create(CrToG);
            var cbToBVec = Vector256.Create(CbToB);
            var halfVec = Vector256.Create(Half);
            var sixteenVec = Vector256.Create(16);
            var oneTwentyEightVec = Vector256.Create(128);
            for (; x <= width - 8; x += 8) {
                var yv = (LoadEight(luma, x) - sixteenVec) * yScaleVec;
                var cb = LoadEight(u, x) - oneTwentyEightVec;
                var cr = LoadEight(v, x) - oneTwentyEightVec;
                var r = Pack((yv + (cr * crToRVec) + halfVec) >> FixedBits);
                var g = Pack((yv + (cb * cbToGVec) + (cr * crToGVec) + halfVec) >> FixedBits);
                var b = Pack((yv + (cb * cbToBVec) + halfVec) >> FixedBits);
                var a = alphaRow.IsEmpty ? Vector128.Create((byte)255) : Pack(LoadEight(alphaRow, x));
                var low = Ssse3.Shuffle(r, lowR) | Ssse3.Shuffle(g, lowG) | Ssse3.Shuffle(b, lowB) | Ssse3.Shuffle(a, lowA);
                var high = Ssse3.Shuffle(r, highR) | Ssse3.Shuffle(g, highG) | Ssse3.Shuffle(b, highB) | Ssse3.Shuffle(a, highA);
                low.CopyTo(destination.Slice(x * 4, 16));
                high.CopyTo(destination.Slice((x * 4) + 16, 16));
            }
        }
        for (; x < width; x++) {
            var yv = (luma[x] - 16) * YScale;
            var cb = u[x] - 128;
            var cr = v[x] - 128;
            destination[x * 4] = Clamp255((yv + (cr * CrToR) + Half) >> FixedBits);
            destination[(x * 4) + 1] = Clamp255((yv + (cb * CbToG) + (cr * CrToG) + Half) >> FixedBits);
            destination[(x * 4) + 2] = Clamp255((yv + (cb * CbToB) + Half) >> FixedBits);
            destination[(x * 4) + 3] = alphaRow.IsEmpty ? (byte)255 : alphaRow[x];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> LoadEight(ReadOnlySpan<byte> source, int offset)
    {
        var bytes = Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(source), offset))).AsByte();
        return Avx2.ConvertToVector256Int32(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Pack(Vector256<int> values)
    {
        values = Vector256.Clamp(values, Vector256<int>.Zero, Vector256.Create(255));
        return Vector128.Narrow(Vector256.Narrow(values, Vector256<int>.Zero).GetLower(), Vector128<short>.Zero).AsByte();
    }
}
