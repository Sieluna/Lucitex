namespace Lucitex.Png.Format;

public readonly record struct PngIhdr(
    int Width,
    int Height,
    byte BitDepth,
    PngColorType ColorType,
    PngInterlaceMethod Interlace)
{
    public int SamplesPerPixel => ColorType switch
    {
        PngColorType.Grayscale => 1,
        PngColorType.Truecolor => 3,
        PngColorType.Indexed => 1,
        PngColorType.GrayscaleAlpha => 2,
        PngColorType.TruecolorAlpha => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(ColorType)),
    };

    public int BitsPerPixel => SamplesPerPixel * BitDepth;

    public int BytesPerPixel => Math.Max(1, (BitsPerPixel + 7) / 8);

    public int RowByteLength(int width) => checked((int)((checked((long)width * BitsPerPixel) + 7) / 8));
}
