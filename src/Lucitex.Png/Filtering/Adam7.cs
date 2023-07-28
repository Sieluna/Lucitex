namespace Lucitex.Png.Filtering;

internal static class Adam7
{
    public static readonly (int XStart, int YStart, int XStep, int YStep)[] Passes =
    [
        (0, 0, 8, 8),
        (4, 0, 8, 8),
        (0, 4, 4, 8),
        (2, 0, 4, 4),
        (0, 2, 2, 4),
        (1, 0, 2, 2),
        (0, 1, 1, 2),
    ];

    public static (int Width, int Height) PassDimensions(int imageWidth, int imageHeight, int passIndex)
    {
        var (xStart, yStart, xStep, yStep) = Passes[passIndex];

        var width = imageWidth > xStart ? ((imageWidth - xStart - 1) / xStep) + 1 : 0;
        var height = imageHeight > yStart ? ((imageHeight - yStart - 1) / yStep) + 1 : 0;

        return (width, height);
    }
}
