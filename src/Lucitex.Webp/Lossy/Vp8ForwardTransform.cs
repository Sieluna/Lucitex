namespace Lucitex.Webp.Lossy;

internal static class Vp8ForwardTransform
{
    public static void Dct(ReadOnlySpan<byte> source, int sourceStride, ReadOnlySpan<byte> prediction, int stride, Span<short> output)
    {
        Span<int> temp = stackalloc int[16];
        for (var y = 0; y < 4; y++) {
            var s = source[(y * sourceStride)..];
            var p = prediction[(y * stride)..];
            var d0 = s[0] - p[0];
            var d1 = s[1] - p[1];
            var d2 = s[2] - p[2];
            var d3 = s[3] - p[3];
            var a = (d0 + d3) * 8;
            var b = (d1 + d2) * 8;
            var c = (d1 - d2) * 8;
            var d = (d0 - d3) * 8;
            temp[y * 4] = a + b;
            temp[(y * 4) + 2] = a - b;
            temp[(y * 4) + 1] = ((c * 2217) + (d * 5352) + 14500) >> 12;
            temp[(y * 4) + 3] = ((d * 2217) - (c * 5352) + 7500) >> 12;
        }
        for (var x = 0; x < 4; x++) {
            var a = temp[x] + temp[12 + x];
            var b = temp[4 + x] + temp[8 + x];
            var c = temp[4 + x] - temp[8 + x];
            var d = temp[x] - temp[12 + x];
            output[x] = (short)((a + b + 7) >> 4);
            output[8 + x] = (short)((a - b + 7) >> 4);
            output[4 + x] = (short)((((c * 2217) + (d * 5352) + 12000) >> 16) + (d == 0 ? 0 : 1));
            output[12 + x] = (short)(((d * 2217) - (c * 5352) + 51000) >> 16);
        }
    }

    public static void Wht(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<int> temp = stackalloc int[16];
        for (var x = 0; x < 4; x++) {
            var a = input[x] + input[12 + x];
            var b = input[4 + x] + input[8 + x];
            var c = input[4 + x] - input[8 + x];
            var d = input[x] - input[12 + x];
            temp[x] = a + b;
            temp[4 + x] = c + d;
            temp[8 + x] = a - b;
            temp[12 + x] = d - c;
        }
        for (var y = 0; y < 16; y += 4) {
            var a = temp[y] + temp[y + 3];
            var b = temp[y + 1] + temp[y + 2];
            var c = temp[y + 1] - temp[y + 2];
            var d = temp[y] - temp[y + 3];
            output[y] = (short)((a + b) >> 1);
            output[y + 1] = (short)((c + d) >> 1);
            output[y + 2] = (short)((a - b) >> 1);
            output[y + 3] = (short)((d - c) >> 1);
        }
    }
}
