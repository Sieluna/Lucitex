namespace Lucitex.Exr.Compression;

internal static class ExrWavelet
{
    private const int k_Bits = 16;
    private const int k_Offset = 1 << (k_Bits - 1);
    private const int k_ModMask = (1 << k_Bits) - 1;

    public static void Encode(Span<ushort> data, int start, int nx, int ox, int ny, int oy, ushort maxValue)
    {
        var narrow = maxValue < 1 << 14;
        var n = Math.Min(nx, ny);
        var p = 1;
        var p2 = 2;

        while (p2 <= n) {
            var oy1 = oy * p;
            var oy2 = oy * p2;
            var ox1 = ox * p;
            var ox2 = ox * p2;

            var py = start;
            var ey = start + (oy * (ny - p2));

            for (; py <= ey; py += oy2) {
                var px = py;
                var ex = py + (ox * (nx - p2));

                for (; px <= ex; px += ox2) {
                    var p01 = px + ox1;
                    var p10 = px + oy1;
                    var p11 = p10 + ox1;

                    Encode(narrow, data[px], data[p01], out var i00, out var i01);
                    Encode(narrow, data[p10], data[p11], out var i10, out var i11);
                    Encode(narrow, i00, i10, out var o00, out var o10);
                    Encode(narrow, i01, i11, out var o01, out var o11);

                    data[px] = o00;
                    data[p01] = o01;
                    data[p10] = o10;
                    data[p11] = o11;
                }

                if ((nx & p) != 0) {
                    var p10 = px + oy1;
                    Encode(narrow, data[px], data[p10], out var low, out var high);
                    data[p10] = high;
                    data[px] = low;
                }
            }

            if ((ny & p) != 0) {
                var px = py;
                var ex = py + (ox * (nx - p2));

                for (; px <= ex; px += ox2) {
                    var p01 = px + ox1;
                    Encode(narrow, data[px], data[p01], out var low, out var high);
                    data[p01] = high;
                    data[px] = low;
                }
            }

            p = p2;
            p2 <<= 1;
        }
    }

    public static void Decode(Span<ushort> data, int start, int nx, int ox, int ny, int oy, ushort maxValue)
    {
        var narrow = maxValue < 1 << 14;
        var n = Math.Min(nx, ny);
        var p = 1;

        while (p <= n) {
            p <<= 1;
        }

        p >>= 1;
        var p2 = p;
        p >>= 1;

        while (p >= 1) {
            var oy1 = oy * p;
            var oy2 = oy * p2;
            var ox1 = ox * p;
            var ox2 = ox * p2;

            var py = start;
            var ey = start + (oy * (ny - p2));

            for (; py <= ey; py += oy2) {
                var px = py;
                var ex = py + (ox * (nx - p2));

                for (; px <= ex; px += ox2) {
                    var p01 = px + ox1;
                    var p10 = px + oy1;
                    var p11 = p10 + ox1;

                    Decode(narrow, data[px], data[p10], out var i00, out var i10);
                    Decode(narrow, data[p01], data[p11], out var i01, out var i11);
                    Decode(narrow, i00, i01, out var o00, out var o01);
                    Decode(narrow, i10, i11, out var o10, out var o11);

                    data[px] = o00;
                    data[p01] = o01;
                    data[p10] = o10;
                    data[p11] = o11;
                }

                if ((nx & p) != 0) {
                    var p10 = px + oy1;
                    Decode(narrow, data[px], data[p10], out var a, out var b);
                    data[p10] = b;
                    data[px] = a;
                }
            }

            if ((ny & p) != 0) {
                var px = py;
                var ex = py + (ox * (nx - p2));

                for (; px <= ex; px += ox2) {
                    var p01 = px + ox1;
                    Decode(narrow, data[px], data[p01], out var a, out var b);
                    data[p01] = b;
                    data[px] = a;
                }
            }

            p2 = p;
            p >>= 1;
        }
    }

    private static void Encode(bool narrow, ushort a, ushort b, out ushort low, out ushort high)
    {
        if (narrow) {
            EncodeNarrow(a, b, out low, out high);
        }
        else {
            EncodeWide(a, b, out low, out high);
        }
    }

    private static void Decode(bool narrow, ushort low, ushort high, out ushort a, out ushort b)
    {
        if (narrow) {
            DecodeNarrow(low, high, out a, out b);
        }
        else {
            DecodeWide(low, high, out a, out b);
        }
    }

    private static void EncodeNarrow(ushort a, ushort b, out ushort low, out ushort high)
    {
        var left = (short)a;
        var right = (short)b;

        low = unchecked((ushort)(short)((left + right) >> 1));
        high = unchecked((ushort)(short)(left - right));
    }

    private static void DecodeNarrow(ushort low, ushort high, out ushort a, out ushort b)
    {
        var mean = (short)low;
        var difference = (short)high;

        var value = mean + (difference & 1) + (difference >> 1);

        a = unchecked((ushort)(short)value);
        b = unchecked((ushort)(short)(value - difference));
    }

    private static void EncodeWide(ushort a, ushort b, out ushort low, out ushort high)
    {
        var shifted = (a + k_Offset) & k_ModMask;
        var mean = (shifted + b) >> 1;
        var difference = shifted - b;
        var negative = difference >> 31;

        low = (ushort)((mean + (k_Offset & negative)) & k_ModMask);
        high = (ushort)(difference & k_ModMask);
    }

    private static void DecodeWide(ushort low, ushort high, out ushort a, out ushort b)
    {
        var right = (low - (high >> 1)) & k_ModMask;

        b = (ushort)right;
        a = (ushort)((high + right - k_Offset) & k_ModMask);
    }
}
