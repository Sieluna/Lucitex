namespace Lucitex.Core.Topology;

public readonly record struct LevelKey(int X, int Y, int Z)
{
    public static LevelKey Base => new(0, 0, 0);

    public static LevelKey Mip(int level) => new(level, level, level);
}
