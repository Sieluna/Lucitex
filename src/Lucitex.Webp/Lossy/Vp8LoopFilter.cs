namespace Lucitex.Webp.Lossy;

internal static class Vp8LoopFilter
{
    private static int Clamp127(int v) => v < -128 ? -128 : v > 127 ? 127 : v;

    private static int U2S(byte v) => v - 128;

    private static byte S2U(int v) => (byte)(Clamp127(v) + 128);

    private static int Abs(int v) => v < 0 ? -v : v;

    private static int CommonAdjust(bool useOuterTaps, Span<byte> plane, int p1Index, int p0Index, int q0Index, int q1Index)
    {
        var p1 = U2S(plane[p1Index]);
        var p0 = U2S(plane[p0Index]);
        var q0 = U2S(plane[q0Index]);
        var q1 = U2S(plane[q1Index]);

        var a = Clamp127((useOuterTaps ? Clamp127(p1 - q1) : 0) + (3 * (q0 - p0)));
        var b = Clamp127(a + 3) >> 3;
        a = Clamp127(a + 4) >> 3;

        plane[q0Index] = S2U(q0 - a);
        plane[p0Index] = S2U(p0 + b);
        return a;
    }

    private static void SimpleSegment(int edgeLimit, Span<byte> plane, int p1, int p0, int q0, int q1)
    {
        if ((Abs(plane[p0] - plane[q0]) * 2) + (Abs(plane[p1] - plane[q1]) / 2) <= edgeLimit) {
            CommonAdjust(true, plane, p1, p0, q0, q1);
        }
    }

    private static bool FilterYes(int interiorLimit, int edgeLimit, ReadOnlySpan<byte> pixels, int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3)
    {
        return (Abs(pixels[p0] - pixels[q0]) * 2) + (Abs(pixels[p1] - pixels[q1]) / 2) <= edgeLimit
            && Abs(pixels[p3] - pixels[p2]) <= interiorLimit && Abs(pixels[p2] - pixels[p1]) <= interiorLimit && Abs(pixels[p1] - pixels[p0]) <= interiorLimit
            && Abs(pixels[q3] - pixels[q2]) <= interiorLimit && Abs(pixels[q2] - pixels[q1]) <= interiorLimit && Abs(pixels[q1] - pixels[q0]) <= interiorLimit;
    }

    private static bool Hev(int threshold, ReadOnlySpan<byte> pixels, int p1, int p0, int q0, int q1)
        => Abs(pixels[p1] - pixels[p0]) > threshold || Abs(pixels[q1] - pixels[q0]) > threshold;

    private static void SubblockFilter(int hevThreshold, int interiorLimit, int edgeLimit, Span<byte> plane, int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3)
    {
        if (!FilterYes(interiorLimit, edgeLimit, plane, p3, p2, p1, p0, q0, q1, q2, q3)) {
            return;
        }
        var hev = Hev(hevThreshold, plane, p1, p0, q0, q1);
        var a = (CommonAdjust(hev, plane, p1, p0, q0, q1) + 1) >> 1;
        if (!hev) {
            plane[q1] = S2U(U2S(plane[q1]) - a);
            plane[p1] = S2U(U2S(plane[p1]) + a);
        }
    }

    private static void MbFilter(int hevThreshold, int interiorLimit, int edgeLimit, Span<byte> plane, int p3, int p2, int p1, int p0, int q0, int q1, int q2, int q3)
    {
        if (!FilterYes(interiorLimit, edgeLimit, plane, p3, p2, p1, p0, q0, q1, q2, q3)) {
            return;
        }
        if (Hev(hevThreshold, plane, p1, p0, q0, q1)) {
            CommonAdjust(true, plane, p1, p0, q0, q1);
            return;
        }
        var p2v = U2S(plane[p2]);
        var p1v = U2S(plane[p1]);
        var p0v = U2S(plane[p0]);
        var q0v = U2S(plane[q0]);
        var q1v = U2S(plane[q1]);
        var q2v = U2S(plane[q2]);

        var w = Clamp127(Clamp127(p1v - q1v) + (3 * (q0v - p0v)));

        var a = Clamp127((27 * w) + 63) >> 7;
        plane[q0] = S2U(q0v - a);
        plane[p0] = S2U(p0v + a);

        a = Clamp127((18 * w) + 63) >> 7;
        plane[q1] = S2U(q1v - a);
        plane[p1] = S2U(p1v + a);

        a = Clamp127((9 * w) + 63) >> 7;
        plane[q2] = S2U(q2v - a);
        plane[p2] = S2U(p2v + a);
    }

    public static void FilterVerticalEdgeNormal(Span<byte> plane, int stride, int col, int row, int count, int hevThreshold, int interiorLimit, int edgeLimit, bool isMbEdge)
    {
        for (var i = 0; i < count; i++) {
            var baseIndex = ((row + i) * stride) + col;
            if (isMbEdge) {
                MbFilter(hevThreshold, interiorLimit, edgeLimit, plane, baseIndex - 4, baseIndex - 3, baseIndex - 2, baseIndex - 1, baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);
            }
            else {
                SubblockFilter(hevThreshold, interiorLimit, edgeLimit, plane, baseIndex - 4, baseIndex - 3, baseIndex - 2, baseIndex - 1, baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);
            }
        }
    }

    public static void FilterHorizontalEdgeNormal(Span<byte> plane, int stride, int col, int row, int count, int hevThreshold, int interiorLimit, int edgeLimit, bool isMbEdge)
    {
        for (var i = 0; i < count; i++) {
            var baseIndex = (row * stride) + col + i;
            if (isMbEdge) {
                MbFilter(hevThreshold, interiorLimit, edgeLimit, plane, baseIndex - (4 * stride), baseIndex - (3 * stride), baseIndex - (2 * stride), baseIndex - stride, baseIndex, baseIndex + stride, baseIndex + (2 * stride), baseIndex + (3 * stride));
            }
            else {
                SubblockFilter(hevThreshold, interiorLimit, edgeLimit, plane, baseIndex - (4 * stride), baseIndex - (3 * stride), baseIndex - (2 * stride), baseIndex - stride, baseIndex, baseIndex + stride, baseIndex + (2 * stride), baseIndex + (3 * stride));
            }
        }
    }

    public static void FilterVerticalEdgeSimple(Span<byte> plane, int stride, int col, int row, int count, int edgeLimit)
    {
        for (var i = 0; i < count; i++) {
            var baseIndex = ((row + i) * stride) + col;
            SimpleSegment(edgeLimit, plane, baseIndex - 2, baseIndex - 1, baseIndex, baseIndex + 1);
        }
    }

    public static void FilterHorizontalEdgeSimple(Span<byte> plane, int stride, int col, int row, int count, int edgeLimit)
    {
        for (var i = 0; i < count; i++) {
            var baseIndex = (row * stride) + col + i;
            SimpleSegment(edgeLimit, plane, baseIndex - (2 * stride), baseIndex - stride, baseIndex, baseIndex + stride);
        }
    }

    public static (int InteriorLimit, int HevThreshold) DeriveThresholds(int filterLevel, int sharpnessLevel, bool isKeyFrame)
    {
        var interiorLimit = filterLevel;
        if (sharpnessLevel != 0) {
            interiorLimit >>= sharpnessLevel > 4 ? 2 : 1;
            if (interiorLimit > 9 - sharpnessLevel) {
                interiorLimit = 9 - sharpnessLevel;
            }
        }
        if (interiorLimit == 0) {
            interiorLimit = 1;
        }

        var hevThreshold = 0;
        if (isKeyFrame) {
            if (filterLevel >= 40) {
                hevThreshold = 2;
            }
            else if (filterLevel >= 15) {
                hevThreshold = 1;
            }
        }
        else {
            if (filterLevel >= 40) {
                hevThreshold = 3;
            }
            else if (filterLevel >= 20) {
                hevThreshold = 2;
            }
            else if (filterLevel >= 15) {
                hevThreshold = 1;
            }
        }

        return (interiorLimit, hevThreshold);
    }
}
