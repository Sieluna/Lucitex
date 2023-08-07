namespace Lucitex.Dds.Format;

[Flags]
public enum DdsHeaderFlags : uint
{
    Caps = 0x1,
    Height = 0x2,
    Width = 0x4,
    Pitch = 0x8,
    PixelFormat = 0x1000,
    MipMapCount = 0x20000,
    LinearSize = 0x80000,
    Depth = 0x800000,
}

[Flags]
public enum DdsCaps : uint
{
    None = 0,
    Complex = 0x8,
    Mipmap = 0x400000,
    Texture = 0x1000,
}

[Flags]
public enum DdsCaps2 : uint
{
    None = 0,
    CubemapPositiveX = 0x400,
    CubemapNegativeX = 0x800,
    CubemapPositiveY = 0x1000,
    CubemapNegativeY = 0x2000,
    CubemapPositiveZ = 0x4000,
    CubemapNegativeZ = 0x8000,
    Cubemap = 0x200,
    CubemapAllFaces = Cubemap | CubemapPositiveX | CubemapNegativeX | CubemapPositiveY | CubemapNegativeY | CubemapPositiveZ | CubemapNegativeZ,
    Volume = 0x200000,
}
