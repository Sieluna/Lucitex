namespace Lucitex.Webp.Lossy;

internal static class Vp8YuvToRgb
{
    private static byte Clamp255(double v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : Math.Round(v));

    private static byte Clamp255(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

    private static byte SampleBilinear(ReadOnlySpan<byte> plane, int stride, int width, int height, double fx, double fy)
    {
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        x0 = Clamp(x0, 0, width - 1);
        x1 = Clamp(x1, 0, width - 1);
        y0 = Clamp(y0, 0, height - 1);
        y1 = Clamp(y1, 0, height - 1);

        var v00 = plane[(y0 * stride) + x0];
        var v10 = plane[(y0 * stride) + x1];
        var v01 = plane[(y1 * stride) + x0];
        var v11 = plane[(y1 * stride) + x1];

        var top = (v00 * (1 - tx)) + (v10 * tx);
        var bottom = (v01 * (1 - tx)) + (v11 * tx);
        return Clamp255((top * (1 - ty)) + (bottom * ty));
    }

    public static void ConvertRowToRgba(Vp8DecodedFrame frame, int row, Span<byte> destination, ReadOnlySpan<byte> alphaRow)
    {
        var chromaWidth = (frame.Width + 1) / 2;
        var chromaHeight = (frame.Height + 1) / 2;

        for (var x = 0; x < frame.Width; x++) {
            var y = frame.Y[(row * frame.YStride) + x];
            var cfx = ((x - 0.5) / 2.0);
            var cfy = ((row - 0.5) / 2.0);
            var u = SampleBilinear(frame.U, frame.UvStride, chromaWidth, chromaHeight, cfx, cfy);
            var v = SampleBilinear(frame.V, frame.UvStride, chromaWidth, chromaHeight, cfx, cfy);

            var cb = u - 128;
            var cr = v - 128;
            var luma = 1.164383562 * (y - 16);
            var r = Clamp255((int)Math.Round(luma + (1.596027 * cr)));
            var g = Clamp255((int)Math.Round(luma - (0.391762 * cb) - (0.812968 * cr)));
            var b = Clamp255((int)Math.Round(luma + (2.017232 * cb)));

            destination[x * 4] = r;
            destination[(x * 4) + 1] = g;
            destination[(x * 4) + 2] = b;
            destination[(x * 4) + 3] = alphaRow.IsEmpty ? (byte)255 : alphaRow[x];
        }
    }
}
