using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lucitex.Jpeg;

internal static class DctTransform
{
    public static void Inverse(ReadOnlySpan<int> source, Span<float> destination)
    {
        Span<Vector256<float>> rows = stackalloc Vector256<float>[8];
        for (var i = 0; i < 8; i++) {
            rows[i] = Vector256.ConvertToSingle(Vector256.Create(source.Slice(i * 8, 8)));
        }
        InversePass(rows);
        Transpose(rows);
        InversePass(rows);
        Transpose(rows);
        Store(rows, destination);
    }

    public static void Forward(ReadOnlySpan<float> source, Span<float> destination)
    {
        Span<Vector256<float>> rows = stackalloc Vector256<float>[8];
        for (var i = 0; i < 8; i++) {
            rows[i] = Vector256.Create(source.Slice(i * 8, 8));
        }
        ForwardPass(rows);
        Transpose(rows);
        ForwardPass(rows);
        Transpose(rows);
        Store(rows, destination);
    }

    private static void Store(ReadOnlySpan<Vector256<float>> rows, Span<float> destination)
    {
        for (var i = 0; i < 8; i++) {
            rows[i].CopyTo(destination.Slice(i * 8, 8));
        }
    }

    private static void InversePass(Span<Vector256<float>> rows)
    {
        var x0 = rows[0];
        var x1 = rows[1];
        var x2 = rows[2];
        var x3 = rows[3];
        var x4 = rows[4];
        var x5 = rows[5];
        var x6 = rows[6];
        var x7 = rows[7];
        var a = (x0 + x4) * 0.353553391f;
        var b = (x0 - x4) * 0.353553391f;
        var c = x2 * 0.461939766f + x6 * 0.191341716f;
        var d = x2 * 0.191341716f - x6 * 0.461939766f;
        var e0 = a + c;
        var e1 = b + d;
        var e2 = b - d;
        var e3 = a - c;
        var o0 = x1 * 0.490392640f + x3 * 0.415734806f + x5 * 0.277785117f + x7 * 0.097545161f;
        var o1 = x1 * 0.415734806f - x3 * 0.097545161f - x5 * 0.490392640f - x7 * 0.277785117f;
        var o2 = x1 * 0.277785117f - x3 * 0.490392640f + x5 * 0.097545161f + x7 * 0.415734806f;
        var o3 = x1 * 0.097545161f - x3 * 0.277785117f + x5 * 0.415734806f - x7 * 0.490392640f;
        rows[0] = e0 + o0;
        rows[1] = e1 + o1;
        rows[2] = e2 + o2;
        rows[3] = e3 + o3;
        rows[4] = e3 - o3;
        rows[5] = e2 - o2;
        rows[6] = e1 - o1;
        rows[7] = e0 - o0;
    }

    private static void ForwardPass(Span<Vector256<float>> rows)
    {
        var s0 = rows[0] + rows[7];
        var s1 = rows[1] + rows[6];
        var s2 = rows[2] + rows[5];
        var s3 = rows[3] + rows[4];
        var d0 = rows[0] - rows[7];
        var d1 = rows[1] - rows[6];
        var d2 = rows[2] - rows[5];
        var d3 = rows[3] - rows[4];
        rows[0] = (s0 + s1 + s2 + s3) * 0.353553391f;
        rows[4] = (s0 - s1 - s2 + s3) * 0.353553391f;
        rows[2] = (s0 - s3) * 0.461939766f + (s1 - s2) * 0.191341716f;
        rows[6] = (s0 - s3) * 0.191341716f - (s1 - s2) * 0.461939766f;
        rows[1] = d0 * 0.490392640f + d1 * 0.415734806f + d2 * 0.277785117f + d3 * 0.097545161f;
        rows[3] = d0 * 0.415734806f - d1 * 0.097545161f - d2 * 0.490392640f - d3 * 0.277785117f;
        rows[5] = d0 * 0.277785117f - d1 * 0.490392640f + d2 * 0.097545161f + d3 * 0.415734806f;
        rows[7] = d0 * 0.097545161f - d1 * 0.277785117f + d2 * 0.415734806f - d3 * 0.490392640f;
    }

    private static void Transpose(Span<Vector256<float>> rows)
    {
        if (!Avx.IsSupported) {
            Span<Vector256<float>> transposed = stackalloc Vector256<float>[8];
            for (var i = 0; i < 8; i++) {
                transposed[i] = Vector256.Create(
                    rows[0].GetElement(i), rows[1].GetElement(i), rows[2].GetElement(i), rows[3].GetElement(i),
                    rows[4].GetElement(i), rows[5].GetElement(i), rows[6].GetElement(i), rows[7].GetElement(i));
            }
            transposed.CopyTo(rows);
            return;
        }
        var t0 = Avx.UnpackLow(rows[0], rows[1]);
        var t1 = Avx.UnpackHigh(rows[0], rows[1]);
        var t2 = Avx.UnpackLow(rows[2], rows[3]);
        var t3 = Avx.UnpackHigh(rows[2], rows[3]);
        var t4 = Avx.UnpackLow(rows[4], rows[5]);
        var t5 = Avx.UnpackHigh(rows[4], rows[5]);
        var t6 = Avx.UnpackLow(rows[6], rows[7]);
        var t7 = Avx.UnpackHigh(rows[6], rows[7]);
        var s0 = Avx.Shuffle(t0, t2, 0x44);
        var s1 = Avx.Shuffle(t0, t2, 0xEE);
        var s2 = Avx.Shuffle(t1, t3, 0x44);
        var s3 = Avx.Shuffle(t1, t3, 0xEE);
        var s4 = Avx.Shuffle(t4, t6, 0x44);
        var s5 = Avx.Shuffle(t4, t6, 0xEE);
        var s6 = Avx.Shuffle(t5, t7, 0x44);
        var s7 = Avx.Shuffle(t5, t7, 0xEE);
        rows[0] = Avx.Permute2x128(s0, s4, 0x20);
        rows[1] = Avx.Permute2x128(s1, s5, 0x20);
        rows[2] = Avx.Permute2x128(s2, s6, 0x20);
        rows[3] = Avx.Permute2x128(s3, s7, 0x20);
        rows[4] = Avx.Permute2x128(s0, s4, 0x31);
        rows[5] = Avx.Permute2x128(s1, s5, 0x31);
        rows[6] = Avx.Permute2x128(s2, s6, 0x31);
        rows[7] = Avx.Permute2x128(s3, s7, 0x31);
    }
}
