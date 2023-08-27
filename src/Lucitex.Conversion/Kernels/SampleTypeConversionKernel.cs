using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Sampling;

namespace Lucitex.Conversion.Kernels;

// Converts between a codec's on-wire sample bytes and a canonical float32 pipeline. float32 is the
// intermediate for every cross-format numeric conversion so the kernel set only needs one "ToFloat32"
// and one "FromFloat32" per supported SampleType, instead of one function per source/target pair.
//
// Every conversion here has a scalar reference loop (the correctness ground truth) and a
// System.Numerics.Vector<T>-batched loop that processes Vector<T>.Count lanes at a time with a scalar
// remainder for the tail - this is a structural rule for every kernel in this namespace, not a
// per-kernel judgment call. The vectorized path is validated against the scalar one in
// SampleTypeConversionKernelTests.
//
// Scope: UNorm8, UNorm16, Float16 and Float32 are implemented (the pair actually exercised by the
// EXR<->PNG conversion this phase targets). UInt8/16/32 and SNorm8/16 are not wired up yet.
internal static class SampleTypeConversionKernel
{
    public static void ToFloat32(ReadOnlySpan<byte> source, SampleType from, SampleByteOrder byteOrder, Span<float> destination)
    {
        switch (from.Kind, from.Bits, from.Encoding) {
            case (ScalarKind.UnsignedInt, 8, NumericEncoding.UNorm):
                UNorm8ToFloat32(source, destination);
                return;
            case (ScalarKind.UnsignedInt, 16, NumericEncoding.UNorm):
                UNorm16ToFloat32(ToHostOrderUInt16(source, byteOrder), destination);
                return;
            case (ScalarKind.Float, 16, NumericEncoding.Raw):
                Half16ToFloat32(ToHostOrderUInt16(source, byteOrder), destination);
                return;
            case (ScalarKind.Float, 32, NumericEncoding.Raw):
                Float32Passthrough(source, byteOrder, destination);
                return;
            default:
                throw new NotSupportedException($"SampleTypeConversionKernel.ToFloat32 does not support {from}.");
        }
    }

    public static void FromFloat32(ReadOnlySpan<float> source, SampleType to, SampleByteOrder byteOrder, Span<byte> destination)
    {
        switch (to.Kind, to.Bits, to.Encoding) {
            case (ScalarKind.UnsignedInt, 8, NumericEncoding.UNorm):
                Float32ToUNorm8(source, destination);
                return;
            case (ScalarKind.UnsignedInt, 16, NumericEncoding.UNorm):
                FromFloat32ToUInt16Then(source, destination, byteOrder, Float32ToUNorm16);
                return;
            case (ScalarKind.Float, 16, NumericEncoding.Raw):
                FromFloat32ToUInt16Then(source, destination, byteOrder, Float32ToHalfBits);
                return;
            case (ScalarKind.Float, 32, NumericEncoding.Raw):
                Float32FromPassthrough(source, byteOrder, destination);
                return;
            default:
                throw new NotSupportedException($"SampleTypeConversionKernel.FromFloat32 does not support {to}.");
        }
    }

    private static void UNorm8ToFloat32(ReadOnlySpan<byte> source, Span<float> destination)
    {
        const float inv255 = 1f / 255f;
        var i = 0;
        var byteLanes = Vector<byte>.Count;

        if (Vector.IsHardwareAccelerated) {
            var floatLanes = Vector<float>.Count;
            for (; i + byteLanes <= source.Length; i += byteLanes) {
                var bytes = new Vector<byte>(source.Slice(i, byteLanes));
                Vector.Widen(bytes, out var ushortsLo, out var ushortsHi);
                Vector.Widen(ushortsLo, out var uintsA, out var uintsB);
                Vector.Widen(ushortsHi, out var uintsC, out var uintsD);

                (Vector.ConvertToSingle(uintsA) * inv255).CopyTo(destination.Slice(i, floatLanes));
                (Vector.ConvertToSingle(uintsB) * inv255).CopyTo(destination.Slice(i + floatLanes, floatLanes));
                (Vector.ConvertToSingle(uintsC) * inv255).CopyTo(destination.Slice(i + (2 * floatLanes), floatLanes));
                (Vector.ConvertToSingle(uintsD) * inv255).CopyTo(destination.Slice(i + (3 * floatLanes), floatLanes));
            }
        }

        for (; i < source.Length; i++) {
            destination[i] = source[i] * inv255;
        }
    }

    private static void Float32ToUNorm8(ReadOnlySpan<float> source, Span<byte> destination)
    {
        var i = 0;
        var floatLanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            var zero = new Vector<float>(0f);
            var one = new Vector<float>(255f);
            for (; i + (4 * floatLanes) <= source.Length; i += 4 * floatLanes) {
                var a = ClampToBytes(new Vector<float>(source.Slice(i, floatLanes)), zero, one);
                var b = ClampToBytes(new Vector<float>(source.Slice(i + floatLanes, floatLanes)), zero, one);
                var c = ClampToBytes(new Vector<float>(source.Slice(i + (2 * floatLanes), floatLanes)), zero, one);
                var d = ClampToBytes(new Vector<float>(source.Slice(i + (3 * floatLanes), floatLanes)), zero, one);

                var uintsA = Vector.ConvertToUInt32(a);
                var uintsB = Vector.ConvertToUInt32(b);
                var uintsC = Vector.ConvertToUInt32(c);
                var uintsD = Vector.ConvertToUInt32(d);

                var ushortsLo = Vector.Narrow(uintsA, uintsB);
                var ushortsHi = Vector.Narrow(uintsC, uintsD);
                var bytes = Vector.Narrow(ushortsLo, ushortsHi);

                bytes.CopyTo(destination.Slice(i, 4 * floatLanes));
            }
        }

        for (; i < source.Length; i++) {
            destination[i] = (byte)Math.Clamp(MathF.Round(source[i] * 255f), 0f, 255f);
        }

        static Vector<float> ClampToBytes(Vector<float> value, Vector<float> zero, Vector<float> max) =>
            Vector.Min(Vector.Max((value * 255f) + new Vector<float>(0.5f), zero), max);
    }

    private static void UNorm16ToFloat32(ReadOnlySpan<ushort> source, Span<float> destination)
    {
        const float inv65535 = 1f / 65535f;
        var i = 0;
        var ushortLanes = Vector<ushort>.Count;

        if (Vector.IsHardwareAccelerated) {
            var floatLanes = Vector<float>.Count;
            for (; i + ushortLanes <= source.Length; i += ushortLanes) {
                var values = new Vector<ushort>(source.Slice(i, ushortLanes));
                Vector.Widen(values, out var uintsA, out var uintsB);

                (Vector.ConvertToSingle(uintsA) * inv65535).CopyTo(destination.Slice(i, floatLanes));
                (Vector.ConvertToSingle(uintsB) * inv65535).CopyTo(destination.Slice(i + floatLanes, floatLanes));
            }
        }

        for (; i < source.Length; i++) {
            destination[i] = source[i] * inv65535;
        }
    }

    private static void Float32ToUNorm16(ReadOnlySpan<float> source, Span<ushort> destination)
    {
        var i = 0;
        var floatLanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            var zero = new Vector<float>(0f);
            var max = new Vector<float>(65535f);
            for (; i + (2 * floatLanes) <= source.Length; i += 2 * floatLanes) {
                var a = Vector.Min(Vector.Max((new Vector<float>(source.Slice(i, floatLanes)) * 65535f) + new Vector<float>(0.5f), zero), max);
                var b = Vector.Min(Vector.Max((new Vector<float>(source.Slice(i + floatLanes, floatLanes)) * 65535f) + new Vector<float>(0.5f), zero), max);

                var uintsA = Vector.ConvertToUInt32(a);
                var uintsB = Vector.ConvertToUInt32(b);
                Vector.Narrow(uintsA, uintsB).CopyTo(destination.Slice(i, 2 * floatLanes));
            }
        }

        for (; i < source.Length; i++) {
            destination[i] = (ushort)Math.Clamp(MathF.Round(source[i] * 65535f), 0f, 65535f);
        }
    }

    // Branchless half->float widening (Fabian Giesen's "half_to_float_fast3"): shifting the half's
    // exponent+mantissa bits into a float32's exponent+mantissa position and multiplying by a
    // constant power-of-two lets the FPU's own normalization handle subnormals correctly, with a
    // single comparison left to patch up Inf/NaN. This is what makes it vectorizable without any
    // per-lane branching or loops.
    private static readonly Vector<float> s_HalfMagic = new(BitConverter.Int32BitsToSingle((254 - 15) << 23));
    private static readonly Vector<float> s_HalfWasInfNan = new(BitConverter.Int32BitsToSingle((127 + 16) << 23));
    private const uint k_InfNanExponentBits = 255u << 23;

    private static void Half16ToFloat32(ReadOnlySpan<ushort> source, Span<float> destination)
    {
        var i = 0;
        var ushortLanes = Vector<ushort>.Count;

        if (Vector.IsHardwareAccelerated) {
            var floatLanes = Vector<float>.Count;
            var infNanMask = new Vector<uint>(k_InfNanExponentBits);
            for (; i + ushortLanes <= source.Length; i += ushortLanes) {
                var bits = new Vector<ushort>(source.Slice(i, ushortLanes));
                Vector.Widen(bits, out var lo, out var hi);

                (HalfBitsToFloat(lo, infNanMask)).CopyTo(destination.Slice(i, floatLanes));
                (HalfBitsToFloat(hi, infNanMask)).CopyTo(destination.Slice(i + floatLanes, floatLanes));
            }
        }

        for (; i < source.Length; i++) {
            destination[i] = (float)BitConverter.UInt16BitsToHalf(source[i]);
        }

        static Vector<float> HalfBitsToFloat(Vector<uint> halfBits, Vector<uint> infNanMask)
        {
            var mantissaExponent = (halfBits & new Vector<uint>(0x7FFFu)) << 13;
            var asFloat = Vector.As<uint, float>(mantissaExponent) * s_HalfMagic;

            var isInfNan = Vector.As<int, uint>(Vector.GreaterThanOrEqual(asFloat, s_HalfWasInfNan));
            var patched = Vector.As<float, uint>(asFloat) | (isInfNan & infNanMask);

            var sign = (halfBits & new Vector<uint>(0x8000u)) << 16;
            return Vector.As<uint, float>(patched | sign);
        }
    }

    private static void Float32ToHalfBits(ReadOnlySpan<float> source, Span<ushort> destination)
    {
        for (var i = 0; i < source.Length; i++) {
            destination[i] = BitConverter.HalfToUInt16Bits((Half)source[i]);
        }
    }

    private static void Float32Passthrough(ReadOnlySpan<byte> source, SampleByteOrder byteOrder, Span<float> destination)
    {
        if (byteOrder == SampleByteOrder.LittleEndian) {
            MemoryMarshal.Cast<byte, float>(source).CopyTo(destination);
            return;
        }

        for (var i = 0; i < destination.Length; i++) {
            destination[i] = BinaryPrimitives.ReadSingleBigEndian(source.Slice(i * sizeof(float), sizeof(float)));
        }
    }

    private static void Float32FromPassthrough(ReadOnlySpan<float> source, SampleByteOrder byteOrder, Span<byte> destination)
    {
        if (byteOrder == SampleByteOrder.LittleEndian) {
            MemoryMarshal.AsBytes(source).CopyTo(destination);
            return;
        }

        for (var i = 0; i < source.Length; i++) {
            BinaryPrimitives.WriteSingleBigEndian(destination.Slice(i * sizeof(float), sizeof(float)), source[i]);
        }
    }

    private static ReadOnlySpan<ushort> ToHostOrderUInt16(ReadOnlySpan<byte> source, SampleByteOrder byteOrder)
    {
        var count = source.Length / sizeof(ushort);
        if (byteOrder == SampleByteOrder.LittleEndian) {
            return MemoryMarshal.Cast<byte, ushort>(source);
        }

        var swapped = new ushort[count];
        for (var i = 0; i < count; i++) {
            swapped[i] = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(i * sizeof(ushort), sizeof(ushort)));
        }

        return swapped;
    }

    private static void FromFloat32ToUInt16Then(ReadOnlySpan<float> source, Span<byte> destination, SampleByteOrder byteOrder, Action<ReadOnlySpan<float>, Span<ushort>> convert)
    {
        if (byteOrder == SampleByteOrder.LittleEndian) {
            convert(source, MemoryMarshal.Cast<byte, ushort>(destination));
            return;
        }

        var hostOrder = new ushort[source.Length];
        convert(source, hostOrder);
        for (var i = 0; i < hostOrder.Length; i++) {
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(i * sizeof(ushort), sizeof(ushort)), hostOrder[i]);
        }
    }
}
