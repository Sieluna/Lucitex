namespace Lucitex.Exr.Format;

public enum ExrPixelType
{
    UInt = 0,
    Half = 1,
    Float = 2,
}

public enum ExrCompressionId
{
    None = 0,
    Rle = 1,
    Zips = 2,
    Zip = 3,
    Piz = 4,
    Pxr24 = 5,
    B44 = 6,
    B44A = 7,
    Dwaa = 8,
    Dwab = 9,
}

public enum ExrLineOrder
{
    IncreasingY = 0,
    DecreasingY = 1,
    RandomY = 2,
}

public enum ExrTileLevelMode
{
    OneLevel = 0,
    MipmapLevels = 1,
    RipmapLevels = 2,
}

public enum ExrTileRoundingMode
{
    RoundDown = 0,
    RoundUp = 1,
}

[Flags]
public enum ExrVersionFlags
{
    None = 0,
    Tiled = 0x200,
    LongNames = 0x400,
    NonImage = 0x800,
    MultiPart = 0x1000,
}
