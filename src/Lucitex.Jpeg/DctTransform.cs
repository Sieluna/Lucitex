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
        var c = Vector256.FusedMultiplyAdd(x6, Vector256.Create(0.191341716f), x2 * 0.461939766f);
        var d = Vector256.FusedMultiplyAdd(x6, Vector256.Create(-0.461939766f), x2 * 0.191341716f);
        var e0 = a + c;
        var e1 = b + d;
        var e2 = b - d;
        var e3 = a - c;
        var o0 = Vector256.FusedMultiplyAdd(x7, Vector256.Create(0.097545161f),
            Vector256.FusedMultiplyAdd(x5, Vector256.Create(0.277785117f),
            Vector256.FusedMultiplyAdd(x3, Vector256.Create(0.415734806f), x1 * 0.490392640f)));
        var o1 = Vector256.FusedMultiplyAdd(x7, Vector256.Create(-0.277785117f),
            Vector256.FusedMultiplyAdd(x5, Vector256.Create(-0.490392640f),
            Vector256.FusedMultiplyAdd(x3, Vector256.Create(-0.097545161f), x1 * 0.415734806f)));
        var o2 = Vector256.FusedMultiplyAdd(x7, Vector256.Create(0.415734806f),
            Vector256.FusedMultiplyAdd(x5, Vector256.Create(0.097545161f),
            Vector256.FusedMultiplyAdd(x3, Vector256.Create(-0.490392640f), x1 * 0.277785117f)));
        var o3 = Vector256.FusedMultiplyAdd(x7, Vector256.Create(-0.490392640f),
            Vector256.FusedMultiplyAdd(x5, Vector256.Create(0.415734806f),
            Vector256.FusedMultiplyAdd(x3, Vector256.Create(-0.277785117f), x1 * 0.097545161f)));
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
        rows[2] = Vector256.FusedMultiplyAdd(s1 - s2, Vector256.Create(0.191341716f), (s0 - s3) * 0.461939766f);
        rows[6] = Vector256.FusedMultiplyAdd(s1 - s2, Vector256.Create(-0.461939766f), (s0 - s3) * 0.191341716f);
        rows[1] = Vector256.FusedMultiplyAdd(d3, Vector256.Create(0.097545161f),
            Vector256.FusedMultiplyAdd(d2, Vector256.Create(0.277785117f),
            Vector256.FusedMultiplyAdd(d1, Vector256.Create(0.415734806f), d0 * 0.490392640f)));
        rows[3] = Vector256.FusedMultiplyAdd(d3, Vector256.Create(-0.277785117f),
            Vector256.FusedMultiplyAdd(d2, Vector256.Create(-0.490392640f),
            Vector256.FusedMultiplyAdd(d1, Vector256.Create(-0.097545161f), d0 * 0.415734806f)));
        rows[5] = Vector256.FusedMultiplyAdd(d3, Vector256.Create(0.415734806f),
            Vector256.FusedMultiplyAdd(d2, Vector256.Create(0.097545161f),
            Vector256.FusedMultiplyAdd(d1, Vector256.Create(-0.490392640f), d0 * 0.277785117f)));
        rows[7] = Vector256.FusedMultiplyAdd(d3, Vector256.Create(-0.490392640f),
            Vector256.FusedMultiplyAdd(d2, Vector256.Create(0.415734806f),
            Vector256.FusedMultiplyAdd(d1, Vector256.Create(-0.277785117f), d0 * 0.097545161f)));
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
