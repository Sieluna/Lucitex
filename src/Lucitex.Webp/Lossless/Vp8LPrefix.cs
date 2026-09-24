using System.Numerics;

namespace Lucitex.Webp.Lossless;

internal readonly record struct Vp8LPrefix(int Symbol, int ExtraBits, uint ExtraValue)
{
    public static Vp8LPrefix FromValue(int value)
    {
        if (value <= 4) {
            return new Vp8LPrefix(value - 1, 0, 0);
        }
        var reduced = (uint)(value - 1);
        var extraBits = BitOperations.Log2(reduced) - 1;
        var symbol = (extraBits * 2) + (int)(reduced >> extraBits);
        return new Vp8LPrefix(symbol, extraBits, reduced & ((1u << extraBits) - 1));
    }
}
