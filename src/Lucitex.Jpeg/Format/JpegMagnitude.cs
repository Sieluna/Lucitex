using System.Numerics;

namespace Lucitex.Jpeg.Format;

internal static class JpegMagnitude
{
    public static int GetSize(int value) => 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(value));

    public static int Encode(int value, int size) => value >= 0 ? value : value - 1 + (1 << size);

    public static int Decode(int bits, int size)
    {
        if (size == 0) {
            return 0;
        }
        return bits < (1 << (size - 1)) ? bits - (1 << size) + 1 : bits;
    }
}
