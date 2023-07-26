namespace Lucitex.Png.Format;

public enum PngColorType : byte
{
    Grayscale = 0,
    Truecolor = 2,
    Indexed = 3,
    GrayscaleAlpha = 4,
    TruecolorAlpha = 6,
}

public enum PngInterlaceMethod : byte
{
    None = 0,
    Adam7 = 1,
}

public enum PngFilterType : byte
{
    None = 0,
    Sub = 1,
    Up = 2,
    Average = 3,
    Paeth = 4,
}
