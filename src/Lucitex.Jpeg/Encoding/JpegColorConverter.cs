using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegColorConverter
{
    private static readonly Vector128<byte> s_RedLower = Vector128.Create((byte)0, 3, 6, 9, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_RedUpper = Vector128.Create((byte)4, 7, 10, 13, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_GreenLower = Vector128.Create((byte)1, 4, 7, 10, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_GreenUpper = Vector128.Create((byte)5, 8, 11, 14, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_BlueLower = Vector128.Create((byte)2, 5, 8, 11, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
    private static readonly Vector128<byte> s_BlueUpper = Vector128.Create((byte)6, 9, 12, 15, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);

    public static void ConvertRow(ReadOnlySpan<byte> pixels, Span<float> luma, Span<float> cb, Span<float> cr)
    {
        var lower = Vector128.Create(pixels[..16]);
        var upper = Vector128.Create(pixels.Slice(8, 16));
        var r = Expand(lower, upper, s_RedLower, s_RedUpper);
        var g = Expand(lower, upper, s_GreenLower, s_GreenUpper);
        var b = Expand(lower, upper, s_BlueLower, s_BlueUpper);
        var y = r * Vector256.Create(0.299f) + g * Vector256.Create(0.587f) + b * Vector256.Create(0.114f) - Vector256.Create(128f);
        var blue = -r * Vector256.Create(0.168736f) - g * Vector256.Create(0.331264f) + b * Vector256.Create(0.5f);
        var red = r * Vector256.Create(0.5f) - g * Vector256.Create(0.418688f) - b * Vector256.Create(0.081312f);
        y.CopyTo(luma);
        Accumulate(blue, cb);
        Accumulate(red, cr);
    }

    private static Vector256<float> Expand(Vector128<byte> lower, Vector128<byte> upper, Vector128<byte> lowerMask, Vector128<byte> upperMask)
    {
        var bytes = Sse2.UnpackLow(Ssse3.Shuffle(lower, lowerMask).AsUInt32(), Ssse3.Shuffle(upper, upperMask).AsUInt32()).AsByte();
        return Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(bytes));
    }

    private static void Accumulate(Vector256<float> values, Span<float> destination)
    {
        if (destination.Length == 8) {
            (Vector256.Create(destination) + values).CopyTo(destination);
        }
        else {
            var even = Sse.Shuffle(values.GetLower(), values.GetUpper(), 0x88);
            var odd = Sse.Shuffle(values.GetLower(), values.GetUpper(), 0xDD);
            (Vector128.Create(destination) + even + odd).CopyTo(destination);
        }
    }
}
