namespace Lucitex.Webp.Lossy;

internal static class Vp8Predict
{
    private static byte Clamp255(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private static byte Sample(ReadOnlySpan<byte> plane, int stride, int row, int col)
    {
        if (row < 0) {
            return 127;
        }
        if (col < 0) {
            return 129;
        }
        return plane[(row * stride) + col];
    }

    public static void PredictBlock(Span<byte> plane, int stride, int originRow, int originCol, int size, int mode)
    {
        Span<byte> above = stackalloc byte[16];
        Span<byte> left = stackalloc byte[16];
        for (var i = 0; i < size; i++) {
            above[i] = Sample(plane, stride, originRow - 1, originCol + i);
            left[i] = Sample(plane, stride, originRow + i, originCol - 1);
        }
        var corner = Sample(plane, stride, originRow - 1, originCol - 1);

        switch (mode) {
            case Vp8Tables.VPred:
                for (var r = 0; r < size; r++) {
                    above[..size].CopyTo(plane.Slice(((originRow + r) * stride) + originCol, size));
                }
                break;
            case Vp8Tables.HPred:
                for (var r = 0; r < size; r++) {
                    plane.Slice(((originRow + r) * stride) + originCol, size).Fill(left[r]);
                }
                break;
            case Vp8Tables.TmPred:
                for (var r = 0; r < size; r++) {
                    var row = plane.Slice(((originRow + r) * stride) + originCol, size);
                    for (var c = 0; c < size; c++) {
                        row[c] = Clamp255(left[r] + above[c] - corner);
                    }
                }
                break;
            default:
                var value = DcValue(plane, stride, originRow, originCol, size);
                for (var r = 0; r < size; r++) {
                    plane.Slice(((originRow + r) * stride) + originCol, size).Fill(value);
                }
                break;
        }
    }

    private static byte DcValue(ReadOnlySpan<byte> plane, int stride, int originRow, int originCol, int size)
    {
        var hasAbove = originRow > 0;
        var hasLeft = originCol > 0;
        if (!hasAbove && !hasLeft) {
            return 128;
        }
        var sum = 0;
        var count = 0;
        if (hasAbove) {
            for (var i = 0; i < size; i++) {
                sum += plane[((originRow - 1) * stride) + originCol + i];
            }
            count += size;
        }
        if (hasLeft) {
            for (var i = 0; i < size; i++) {
                sum += plane[((originRow + i) * stride) + originCol - 1];
            }
            count += size;
        }
        var shift = int.Log2(count);
        return (byte)((sum + (1 << (shift - 1))) >> shift);
    }

    public static void PredictSubblock(Span<byte> plane, int stride, int planeWidthInMacroblocks, int mbX, int mbY, int bx, int by, int mode)
    {
        var originRow = (mbY * 16) + (by * 4);
        var originCol = (mbX * 16) + (bx * 4);

        Span<byte> a = stackalloc byte[9];
        var aboveRow = originRow - 1;
        for (var i = 0; i < 4; i++) {
            a[1 + i] = Sample(plane, stride, aboveRow, originCol + i);
        }
        if (bx == 3) {
            var macroblockAboveRow = (mbY * 16) - 1;
            var isRightmostMacroblock = mbX == planeWidthInMacroblocks - 1;
            for (var i = 0; i < 4; i++) {
                var col = isRightmostMacroblock ? (mbX * 16) + 15 : (mbX * 16) + 16 + i;
                a[5 + i] = Sample(plane, stride, macroblockAboveRow, col);
            }
        }
        else {
            for (var i = 0; i < 4; i++) {
                a[5 + i] = Sample(plane, stride, aboveRow, originCol + 4 + i);
            }
        }
        a[0] = Sample(plane, stride, aboveRow, originCol - 1);

        Span<byte> l = stackalloc byte[4];
        for (var i = 0; i < 4; i++) {
            l[i] = Sample(plane, stride, originRow + i, originCol - 1);
        }

        Span<byte> e = stackalloc byte[9];
        e[0] = l[3];
        e[1] = l[2];
        e[2] = l[1];
        e[3] = l[0];
        e[4] = a[0];
        e[5] = a[1];
        e[6] = a[2];
        e[7] = a[3];
        e[8] = a[4];

        var b = new byte[16];
        void Set(int r, int c, byte v) => b[(r * 4) + c] = v;
        byte Avg3(byte x, byte y, byte z) => (byte)((x + y + y + z + 2) >> 2);
        byte Avg3P(ReadOnlySpan<byte> p, int i) => Avg3(p[i - 1], p[i], p[i + 1]);
        byte Avg2(byte x, byte y) => (byte)((x + y + 1) >> 1);
        byte Avg2P(ReadOnlySpan<byte> p, int i) => Avg2(p[i], p[i + 1]);

        Span<byte> aFull = stackalloc byte[9];
        a.CopyTo(aFull);

        switch (mode) {
            case Vp8Tables.BDcPred: {
                var v = 4;
                for (var i = 0; i < 4; i++) {
                    v += a[1 + i] + l[i];
                }
                v >>= 3;
                Array.Fill(b, (byte)v);
                break;
            }
            case Vp8Tables.BTmPred:
                for (var r = 0; r < 4; r++) {
                    for (var c = 0; c < 4; c++) {
                        Set(r, c, Clamp255(l[r] + a[1 + c] - a[0]));
                    }
                }
                break;
            case Vp8Tables.BVePred:
                for (var c = 0; c < 4; c++) {
                    var v = Avg3P(aFull, 1 + c);
                    Set(0, c, v);
                    Set(1, c, v);
                    Set(2, c, v);
                    Set(3, c, v);
                }
                break;
            case Vp8Tables.BHePred: {
                var v = Avg3(l[2], l[3], l[3]);
                Set(3, 0, v); Set(3, 1, v); Set(3, 2, v); Set(3, 3, v);
                Span<byte> lFull = stackalloc byte[5];
                lFull[0] = a[0];
                l.CopyTo(lFull[1..]);
                for (var r = 2; r >= 0; r--) {
                    v = Avg3P(lFull, 1 + r);
                    Set(r, 0, v); Set(r, 1, v); Set(r, 2, v); Set(r, 3, v);
                }
                break;
            }
            case Vp8Tables.BLdPred:
                Set(0, 0, Avg3P(aFull, 2));
                { var v = Avg3P(aFull, 3); Set(0, 1, v); Set(1, 0, v); }
                { var v = Avg3P(aFull, 4); Set(0, 2, v); Set(1, 1, v); Set(2, 0, v); }
                { var v = Avg3P(aFull, 5); Set(0, 3, v); Set(1, 2, v); Set(2, 1, v); Set(3, 0, v); }
                { var v = Avg3P(aFull, 6); Set(1, 3, v); Set(2, 2, v); Set(3, 1, v); }
                { var v = Avg3P(aFull, 7); Set(2, 3, v); Set(3, 2, v); }
                Set(3, 3, Avg3(aFull[7], aFull[8], aFull[8]));
                break;
            case Vp8Tables.BRdPred:
                Set(3, 0, Avg3P(e, 1));
                { var v = Avg3P(e, 2); Set(3, 1, v); Set(2, 0, v); }
                { var v = Avg3P(e, 3); Set(3, 2, v); Set(2, 1, v); Set(1, 0, v); }
                { var v = Avg3P(e, 4); Set(3, 3, v); Set(2, 2, v); Set(1, 1, v); Set(0, 0, v); }
                { var v = Avg3P(e, 5); Set(2, 3, v); Set(1, 2, v); Set(0, 1, v); }
                { var v = Avg3P(e, 6); Set(1, 3, v); Set(0, 2, v); }
                Set(0, 3, Avg3P(e, 7));
                break;
            case Vp8Tables.BVrPred:
                Set(3, 0, Avg3P(e, 2));
                Set(2, 0, Avg3P(e, 3));
                { var v = Avg3P(e, 4); Set(3, 1, v); Set(1, 0, v); }
                { var v = Avg2P(e, 4); Set(2, 1, v); Set(0, 0, v); }
                { var v = Avg3P(e, 5); Set(3, 2, v); Set(1, 1, v); }
                { var v = Avg2P(e, 5); Set(2, 2, v); Set(0, 1, v); }
                { var v = Avg3P(e, 6); Set(3, 3, v); Set(1, 2, v); }
                { var v = Avg2P(e, 6); Set(2, 3, v); Set(0, 2, v); }
                Set(1, 3, Avg3P(e, 7));
                Set(0, 3, Avg2P(e, 7));
                break;
            case Vp8Tables.BVlPred:
                Set(0, 0, Avg2P(aFull, 1));
                Set(1, 0, Avg3P(aFull, 2));
                { var v = Avg2P(aFull, 2); Set(2, 0, v); Set(0, 1, v); }
                { var v = Avg3P(aFull, 3); Set(1, 1, v); Set(3, 0, v); }
                { var v = Avg2P(aFull, 3); Set(2, 1, v); Set(0, 2, v); }
                { var v = Avg3P(aFull, 4); Set(3, 1, v); Set(1, 2, v); }
                { var v = Avg2P(aFull, 4); Set(2, 2, v); Set(0, 3, v); }
                { var v = Avg3P(aFull, 5); Set(3, 2, v); Set(1, 3, v); }
                Set(2, 3, Avg3P(aFull, 6));
                Set(3, 3, Avg3P(aFull, 7));
                break;
            case Vp8Tables.BHdPred:
                Set(3, 0, Avg2P(e, 0));
                Set(3, 1, Avg3P(e, 1));
                { var v = Avg2P(e, 1); Set(2, 0, v); Set(3, 2, v); }
                { var v = Avg3P(e, 2); Set(2, 1, v); Set(3, 3, v); }
                { var v = Avg2P(e, 2); Set(2, 2, v); Set(1, 0, v); }
                { var v = Avg3P(e, 3); Set(2, 3, v); Set(1, 1, v); }
                { var v = Avg2P(e, 3); Set(1, 2, v); Set(0, 0, v); }
                { var v = Avg3P(e, 4); Set(1, 3, v); Set(0, 1, v); }
                Set(0, 2, Avg3P(e, 5));
                Set(0, 3, Avg3P(e, 6));
                break;
            case Vp8Tables.BHuPred: {
                Span<byte> lFull = stackalloc byte[4];
                l.CopyTo(lFull);
                Set(0, 0, Avg2P(lFull, 0));
                Set(0, 1, Avg3P(lFull, 1));
                { var v = Avg2P(lFull, 1); Set(0, 2, v); Set(1, 0, v); }
                { var v = Avg3P(lFull, 2); Set(0, 3, v); Set(1, 1, v); }
                { var v = Avg2P(lFull, 2); Set(1, 2, v); Set(2, 0, v); }
                { var v = Avg3(lFull[2], lFull[3], lFull[3]); Set(1, 3, v); Set(2, 1, v); }
                var last = lFull[3];
                Set(2, 2, last); Set(2, 3, last);
                Set(3, 0, last); Set(3, 1, last); Set(3, 2, last); Set(3, 3, last);
                break;
            }
        }

        for (var r = 0; r < 4; r++) {
            b.AsSpan(r * 4, 4).CopyTo(plane.Slice(((originRow + r) * stride) + originCol, 4));
        }
    }
}
