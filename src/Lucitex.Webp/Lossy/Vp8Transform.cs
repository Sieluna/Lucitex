namespace Lucitex.Webp.Lossy;

internal static class Vp8Transform
{
    private const int k_CosPi8Sqrt2Minus1 = 20091;
    private const int k_SinPi8Sqrt2 = 35468;

    public static void InverseWht(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<short> temp = stackalloc short[16];
        for (var i = 0; i < 4; i++) {
            var a1 = input[i] + input[12 + i];
            var b1 = input[4 + i] + input[8 + i];
            var c1 = input[4 + i] - input[8 + i];
            var d1 = input[i] - input[12 + i];
            temp[i] = (short)(a1 + b1);
            temp[4 + i] = (short)(c1 + d1);
            temp[8 + i] = (short)(a1 - b1);
            temp[12 + i] = (short)(d1 - c1);
        }
        for (var i = 0; i < 4; i++) {
            var row = i * 4;
            var a1 = temp[row] + temp[row + 3];
            var b1 = temp[row + 1] + temp[row + 2];
            var c1 = temp[row + 1] - temp[row + 2];
            var d1 = temp[row] - temp[row + 3];
            var a2 = a1 + b1;
            var b2 = c1 + d1;
            var c2 = a1 - b1;
            var d2 = d1 - c1;
            output[row] = (short)((a2 + 3) >> 3);
            output[row + 1] = (short)((b2 + 3) >> 3);
            output[row + 2] = (short)((c2 + 3) >> 3);
            output[row + 3] = (short)((d2 + 3) >> 3);
        }
    }

    public static void InverseWhtDcOnly(short input0, Span<short> output)
    {
        var value = (short)((input0 + 3) >> 3);
        for (var i = 0; i < 16; i++) {
            output[i] = value;
        }
    }

    public static void InverseDct(ReadOnlySpan<short> input, Span<short> output)
    {
        Span<short> temp = stackalloc short[16];
        for (var i = 0; i < 4; i++) {
            var a1 = input[i] + input[8 + i];
            var b1 = input[i] - input[8 + i];
            var temp1 = (input[4 + i] * k_SinPi8Sqrt2) >> 16;
            var temp2 = input[12 + i] + ((input[12 + i] * k_CosPi8Sqrt2Minus1) >> 16);
            var c1 = temp1 - temp2;
            temp1 = input[4 + i] + ((input[4 + i] * k_CosPi8Sqrt2Minus1) >> 16);
            temp2 = (input[12 + i] * k_SinPi8Sqrt2) >> 16;
            var d1 = temp1 + temp2;
            temp[i] = (short)(a1 + d1);
            temp[12 + i] = (short)(a1 - d1);
            temp[4 + i] = (short)(b1 + c1);
            temp[8 + i] = (short)(b1 - c1);
        }
        for (var i = 0; i < 4; i++) {
            var row = i * 4;
            var a1 = temp[row] + temp[row + 2];
            var b1 = temp[row] - temp[row + 2];
            var temp1 = (temp[row + 1] * k_SinPi8Sqrt2) >> 16;
            var temp2 = temp[row + 3] + ((temp[row + 3] * k_CosPi8Sqrt2Minus1) >> 16);
            var c1 = temp1 - temp2;
            temp1 = temp[row + 1] + ((temp[row + 1] * k_CosPi8Sqrt2Minus1) >> 16);
            temp2 = (temp[row + 3] * k_SinPi8Sqrt2) >> 16;
            var d1 = temp1 + temp2;
            output[row] = (short)((a1 + d1 + 4) >> 3);
            output[row + 3] = (short)((a1 - d1 + 4) >> 3);
            output[row + 1] = (short)((b1 + c1 + 4) >> 3);
            output[row + 2] = (short)((b1 - c1 + 4) >> 3);
        }
    }
}
