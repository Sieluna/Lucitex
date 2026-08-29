namespace Lucitex.Exr.Format;

internal readonly record struct ExrTileLevel(int LevelX, int LevelY, int Width, int Height, int TilesX, int TilesY)
{
    public int TileCount => TilesX * TilesY;
}

internal static class ExrTiling
{
    public static int RoundedLevelSize(long fullSize, int level, ExrTileRoundingMode rounding)
    {
        if (level >= 63) {
            return 1;
        }

        var divisor = 1L << level;
        var size = rounding == ExrTileRoundingMode.RoundDown
            ? fullSize / divisor
            : (fullSize + divisor - 1) / divisor;

        return (int)Math.Max(size, 1);
    }

    public static (int TilesX, int TilesY) TileGrid(long levelWidth, long levelHeight, uint tileXSize, uint tileYSize) =>
        ((int)((levelWidth + tileXSize - 1) / tileXSize), (int)((levelHeight + tileYSize - 1) / tileYSize));

    public static int LevelCount(long size, ExrTileRoundingMode rounding) =>
        (rounding == ExrTileRoundingMode.RoundDown ? FloorLog2(size) : CeilLog2(size)) + 1;

    public static IReadOnlyList<ExrTileLevel> Levels(ExrTileDesc tiles, long width, long height)
    {
        var levels = new List<ExrTileLevel>();

        switch (tiles.LevelMode) {
            case ExrTileLevelMode.OneLevel:
                levels.Add(BuildLevel(tiles, width, height, 0, 0));
                break;

            case ExrTileLevelMode.MipmapLevels:
                var mipCount = LevelCount(Math.Max(width, height), tiles.RoundingMode);
                for (var level = 0; level < mipCount; level++) {
                    levels.Add(BuildLevel(tiles, width, height, level, level));
                }

                break;

            case ExrTileLevelMode.RipmapLevels:
                var xCount = LevelCount(width, tiles.RoundingMode);
                var yCount = LevelCount(height, tiles.RoundingMode);
                for (var levelY = 0; levelY < yCount; levelY++) {
                    for (var levelX = 0; levelX < xCount; levelX++) {
                        levels.Add(BuildLevel(tiles, width, height, levelX, levelY));
                    }
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tiles));
        }

        return levels;
    }

    private static ExrTileLevel BuildLevel(ExrTileDesc tiles, long width, long height, int levelX, int levelY)
    {
        var levelWidth = RoundedLevelSize(width, levelX, tiles.RoundingMode);
        var levelHeight = RoundedLevelSize(height, levelY, tiles.RoundingMode);
        var (tilesX, tilesY) = TileGrid(levelWidth, levelHeight, tiles.XSize, tiles.YSize);

        return new ExrTileLevel(levelX, levelY, levelWidth, levelHeight, tilesX, tilesY);
    }

    private static int FloorLog2(long value)
    {
        var result = 0;
        while (value > 1) {
            result++;
            value >>= 1;
        }

        return result;
    }

    private static int CeilLog2(long value)
    {
        var result = 0;
        var remainder = 0;
        while (value > 1) {
            if ((value & 1) != 0) {
                remainder = 1;
            }

            result++;
            value >>= 1;
        }

        return result + remainder;
    }
}
