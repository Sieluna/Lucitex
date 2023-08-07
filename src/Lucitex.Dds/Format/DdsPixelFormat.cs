namespace Lucitex.Dds.Format;

[Flags]
public enum DdsPixelFormatFlags : uint
{
    None = 0,
    AlphaPixels = 0x1,
    Alpha = 0x2,
    FourCC = 0x4,
    Rgb = 0x40,
    Yuv = 0x200,
    Luminance = 0x20000,
}

public readonly record struct DdsPixelFormat(
    DdsPixelFormatFlags Flags,
    uint FourCC,
    uint RgbBitCount,
    uint RBitMask,
    uint GBitMask,
    uint BBitMask,
    uint ABitMask)
{
    public const uint StructSize = 32;
}
