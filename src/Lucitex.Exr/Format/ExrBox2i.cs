namespace Lucitex.Exr.Format;

public readonly record struct ExrBox2i(int XMin, int YMin, int XMax, int YMax)
{
    public long Width => (long)XMax - XMin + 1;

    public long Height => (long)YMax - YMin + 1;
}

public readonly record struct ExrTileDesc(
    uint XSize,
    uint YSize,
    ExrTileLevelMode LevelMode,
    ExrTileRoundingMode RoundingMode);
