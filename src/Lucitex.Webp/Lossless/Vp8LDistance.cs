namespace Lucitex.Webp.Lossless;

internal static class Vp8LDistance
{
    private static ReadOnlySpan<byte> PlaneCodes => [
        96, 73, 55, 39, 23, 13, 5, 1, 255, 255, 255, 255, 255, 255, 255, 255,
        101, 78, 58, 42, 26, 16, 8, 2, 0, 3, 9, 17, 27, 43, 59, 79,
        102, 86, 62, 46, 32, 20, 10, 6, 4, 7, 11, 21, 33, 47, 63, 87,
        105, 90, 70, 52, 37, 28, 18, 14, 12, 15, 19, 29, 38, 53, 71, 91,
        110, 99, 82, 66, 48, 35, 30, 24, 22, 25, 31, 36, 49, 67, 83, 100,
        115, 108, 94, 76, 64, 50, 44, 40, 34, 41, 45, 51, 65, 77, 95, 109,
        118, 113, 103, 92, 80, 68, 60, 56, 54, 57, 61, 69, 81, 93, 104, 114,
        119, 116, 111, 106, 97, 88, 84, 74, 72, 75, 85, 89, 98, 107, 112, 117,
    ];

    public static int Encode(int distance, int width)
    {
        var y = distance / width;
        var x = distance - y * width;
        if (x <= 8 && y < 8) {
            return PlaneCodes[y * 16 + 8 - x] + 1;
        }
        if (x > width - 8 && y < 7) {
            return PlaneCodes[(y + 1) * 16 + 8 + width - x] + 1;
        }
        return distance + 120;
    }
}
