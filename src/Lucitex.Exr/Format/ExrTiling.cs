namespace Lucitex.Exr.Format;

internal static class ExrTiling
{
    public static int RoundedLevelSize(long fullSize, int level, ExrTileRoundingMode rounding)
    {
        var size = fullSize;

        for (var i = 0; i < level; i++)
        {
            size = rounding == ExrTileRoundingMode.RoundDown ? size / 2 : (size + 1) / 2;
            if (size < 1)
            {
                size = 1;
            }
        }

        return (int)size;
    }

    public static (int TilesX, int TilesY) TileGrid(long levelWidth, long levelHeight, uint tileXSize, uint tileYSize) =>
        ((int)((levelWidth + tileXSize - 1) / tileXSize), (int)((levelHeight + tileYSize - 1) / tileYSize));
}
