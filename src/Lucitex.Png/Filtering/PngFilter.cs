using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Lucitex.Core.Execution;
using Lucitex.Png.Format;

namespace Lucitex.Png.Filtering;

internal static class PngFilter
{
    private const int k_MaxBytesPerPixel = 8;

    private static readonly Vector128<short> s_ByteMask = Vector128.Create((short)0xFF);

    public static void Reconstruct(PngFilterType filterType, Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        switch (filterType) {
            case PngFilterType.None:
                break;
            case PngFilterType.Sub:
                ReconstructSub(current, bpp);

                break;
            case PngFilterType.Up:
                ReconstructUp(current, previous);

                break;
            case PngFilterType.Average:
                ReconstructAverage(current, previous, bpp);

                break;
            case PngFilterType.Paeth:
                ReconstructPaeth(current, previous, bpp);

                break;
            default:
                throw new ImageFormatException("png", "BadFilterType", $"Unknown PNG filter type {(byte)filterType}.");
        }
    }

    public static void Apply(PngFilterType filterType, Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previousRaw, int bpp)
    {
        switch (filterType) {
            case PngFilterType.None:
                raw.CopyTo(output);
                break;
            case PngFilterType.Sub:
                ApplySub(output, raw, bpp);

                break;
            case PngFilterType.Up:
                ApplyUp(output, raw, previousRaw);

                break;
            case PngFilterType.Average:
                ApplyAverage(output, raw, previousRaw, bpp);

                break;
            case PngFilterType.Paeth:
                ApplyPaeth(output, raw, previousRaw, bpp);

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(filterType));
        }
    }

    private static void ReconstructUp(Span<byte> current, ReadOnlySpan<byte> previous)
    {
        if (previous.IsEmpty) {
            return;
        }

        var i = 0;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= current.Length - lanes; i += lanes) {
                var value = new Vector<byte>(current.Slice(i, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                (value + up).CopyTo(current.Slice(i, lanes));
            }
        }

        for (; i < current.Length; i++) {
            current[i] = unchecked((byte)(current[i] + previous[i]));
        }
    }

    private static void ReconstructSub(Span<byte> current, int bpp)
    {
        var i = bpp;

        if (Vector128.IsHardwareAccelerated && bpp <= k_MaxBytesPerPixel && current.Length > k_MaxBytesPerPixel) {
            ref var row = ref MemoryMarshal.GetReference(current);
            var limit = current.Length - k_MaxBytesPerPixel;
            var left = LoadBytes(ref row, 0);

            for (; i <= limit; i += bpp) {
                var value = LoadBytes(ref row, i) + left;
                StoreBytes(ref row, i, value, bpp);
                left = value;
            }
        }

        for (; i < current.Length; i++) {
            current[i] = unchecked((byte)(current[i] + current[i - bpp]));
        }
    }

    private static void ReconstructAverage(Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        var i = 0;

        if (Vector128.IsHardwareAccelerated && bpp <= k_MaxBytesPerPixel && !previous.IsEmpty &&
            current.Length > k_MaxBytesPerPixel) {
            ref var row = ref MemoryMarshal.GetReference(current);
            ref var above = ref MemoryMarshal.GetReference(previous);
            var limit = current.Length - k_MaxBytesPerPixel;
            var left = Vector128<byte>.Zero;

            for (; i <= limit; i += bpp) {
                var up = LoadBytes(ref above, i);
                var average = (left & up) + ((left ^ up) >>> 1);
                var value = LoadBytes(ref row, i) + average;
                StoreBytes(ref row, i, value, bpp);
                left = value;
            }
        }

        for (; i < current.Length; i++) {
            var left = i >= bpp ? current[i - bpp] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            current[i] = unchecked((byte)(current[i] + ((left + up) / 2)));
        }
    }

    private static void ReconstructPaeth(Span<byte> current, ReadOnlySpan<byte> previous, int bpp)
    {
        if (previous.IsEmpty) {
            ReconstructSub(current, bpp);
            return;
        }

        var i = 0;

        if (Vector128.IsHardwareAccelerated && bpp <= k_MaxBytesPerPixel && current.Length > k_MaxBytesPerPixel) {
            ref var row = ref MemoryMarshal.GetReference(current);
            ref var above = ref MemoryMarshal.GetReference(previous);
            var limit = current.Length - k_MaxBytesPerPixel;
            var left = Vector128<short>.Zero;
            var upperLeft = Vector128<short>.Zero;

            for (; i <= limit; i += bpp) {
                var up = LoadPixel(ref above, i);
                var value = (LoadPixel(ref row, i) + PaethPredictor(left, up, upperLeft)) & s_ByteMask;
                StorePixel(ref row, i, value, bpp);
                left = value;
                upperLeft = up;
            }
        }

        for (; i < current.Length; i++) {
            var left = i >= bpp ? current[i - bpp] : 0;
            var up = previous[i];
            var upLeft = i >= bpp ? previous[i - bpp] : 0;
            current[i] = unchecked((byte)(current[i] + Paeth(left, up, upLeft)));
        }
    }

    private static void ApplySub(Span<byte> output, ReadOnlySpan<byte> raw, int bpp)
    {
        raw[..Math.Min(bpp, raw.Length)].CopyTo(output);
        var i = bpp;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var left = new Vector<byte>(raw.Slice(i - bpp, lanes));
                (value - left).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            output[i] = unchecked((byte)(raw[i] - raw[i - bpp]));
        }
    }

    private static void ApplyUp(Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previous)
    {
        if (previous.IsEmpty) {
            raw.CopyTo(output);
            return;
        }

        var i = 0;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated) {
            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                (value - up).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            output[i] = unchecked((byte)(raw[i] - previous[i]));
        }
    }

    private static void ApplyAverage(Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previous, int bpp)
    {
        var i = 0;
        var lanes = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated && !previous.IsEmpty) {
            for (; i < Math.Min(bpp, raw.Length); i++) {
                output[i] = unchecked((byte)(raw[i] - (previous[i] / 2)));
            }

            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var left = new Vector<byte>(raw.Slice(i - bpp, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                var average = (left & up) + ((left ^ up) >> 1);
                (value - average).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            var left = i >= bpp ? raw[i - bpp] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            output[i] = unchecked((byte)(raw[i] - ((left + up) / 2)));
        }
    }

    private static void ApplyPaeth(Span<byte> output, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> previous, int bpp)
    {
        if (previous.IsEmpty) {
            ApplySub(output, raw, bpp);
            return;
        }

        var i = 0;
        var lanes = Vector<byte>.Count;

        if (Vector.IsHardwareAccelerated) {
            for (; i < Math.Min(bpp, raw.Length); i++) {
                output[i] = unchecked((byte)(raw[i] - previous[i]));
            }

            for (; i <= raw.Length - lanes; i += lanes) {
                var value = new Vector<byte>(raw.Slice(i, lanes));
                var left = new Vector<byte>(raw.Slice(i - bpp, lanes));
                var up = new Vector<byte>(previous.Slice(i, lanes));
                var upperLeft = new Vector<byte>(previous.Slice(i - bpp, lanes));
                (value - PaethPredictor(left, up, upperLeft)).CopyTo(output.Slice(i, lanes));
            }
        }

        for (; i < raw.Length; i++) {
            var left = i >= bpp ? raw[i - bpp] : 0;
            var up = previous[i];
            var upLeft = i >= bpp ? previous[i - bpp] : 0;
            output[i] = unchecked((byte)(raw[i] - Paeth(left, up, upLeft)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> LoadBytes(ref byte source, int offset) =>
        Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, offset))).AsByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> LoadPixel(ref byte source, int offset) =>
        Vector128.WidenLower(LoadBytes(ref source, offset)).AsInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePixel(ref byte destination, int offset, Vector128<short> value, int bpp) =>
        StoreBytes(ref destination, offset, Vector128.Narrow(value.AsUInt16(), Vector128<ushort>.Zero), bpp);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreBytes(ref byte destination, int offset, Vector128<byte> value, int bpp)
    {
        var packed = value.AsUInt64().ToScalar();
        ref var target = ref Unsafe.Add(ref destination, offset);

        switch (bpp) {
            case 8:
                Unsafe.WriteUnaligned(ref target, packed);
                break;
            case 6:
                Unsafe.WriteUnaligned(ref target, (uint)packed);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, 4), (ushort)(packed >> 32));
                break;
            case 4:
                Unsafe.WriteUnaligned(ref target, (uint)packed);
                break;
            case 3:
                Unsafe.WriteUnaligned(ref target, (ushort)packed);
                Unsafe.Add(ref target, 2) = (byte)(packed >> 16);
                break;
            case 2:
                Unsafe.WriteUnaligned(ref target, (ushort)packed);
                break;
            default:
                target = (byte)packed;
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> PaethPredictor(Vector128<short> a, Vector128<short> b, Vector128<short> c)
    {
        var pa = Vector128.Abs(b - c);
        var pb = Vector128.Abs(a - c);
        var pc = Vector128.Abs(a + b - c - c);

        var notA = Vector128.GreaterThan(pa, pb) | Vector128.GreaterThan(pa, pc);
        var notB = Vector128.GreaterThan(pb, pc);

        return Vector128.ConditionalSelect(notA, Vector128.ConditionalSelect(notB, c, b), a);
    }

    private static Vector<byte> PaethPredictor(Vector<byte> a, Vector<byte> b, Vector<byte> c)
    {
        Vector.Widen(a, out var aLow, out var aHigh);
        Vector.Widen(b, out var bLow, out var bHigh);
        Vector.Widen(c, out var cLow, out var cHigh);

        var low = PaethPredictor(Vector.As<ushort, short>(aLow), Vector.As<ushort, short>(bLow), Vector.As<ushort, short>(cLow));
        var high = PaethPredictor(Vector.As<ushort, short>(aHigh), Vector.As<ushort, short>(bHigh), Vector.As<ushort, short>(cHigh));

        return Vector.Narrow(Vector.As<short, ushort>(low), Vector.As<short, ushort>(high));
    }

    private static Vector<short> PaethPredictor(Vector<short> a, Vector<short> b, Vector<short> c)
    {
        var pa = Vector.Abs(b - c);
        var pb = Vector.Abs(a - c);
        var pc = Vector.Abs(a + b - c - c);

        var notA = Vector.GreaterThan(pa, pb) | Vector.GreaterThan(pa, pc);
        var notB = Vector.GreaterThan(pb, pc);

        return Vector.ConditionalSelect(notA, Vector.ConditionalSelect(notB, c, b), a);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) {
            return a;
        }

        return pb <= pc ? b : c;
    }
}
