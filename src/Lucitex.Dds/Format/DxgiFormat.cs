namespace Lucitex.Dds.Format;

public enum DxgiFormat : uint
{
    Unknown = 0,
    R32G32B32A32Float = 2,
    R16G16B16A16Typeless = 9,
    R16G16B16A16Float = 10,
    R16G16B16A16Unorm = 11,
    R32G32Float = 16,
    R10G10B10A2Typeless = 23,
    R10G10B10A2Unorm = 24,
    R11G11B10Float = 26,
    R8G8B8A8Typeless = 27,
    R8G8B8A8Unorm = 28,
    R8G8B8A8UnormSrgb = 29,
    R16G16Typeless = 33,
    R16G16Float = 34,
    R16G16Unorm = 35,
    R32Typeless = 39,
    R32Float = 41,
    R8G8Typeless = 48,
    R8G8Unorm = 49,
    R16Typeless = 53,
    R16Float = 54,
    R16Unorm = 56,
    R8Typeless = 60,
    R8Unorm = 61,
    R9G9B9E5SharedExp = 67,
    Bc1Typeless = 70,
    Bc1Unorm = 71,
    Bc1UnormSrgb = 72,
    Bc2Typeless = 73,
    Bc2Unorm = 74,
    Bc2UnormSrgb = 75,
    Bc3Typeless = 76,
    Bc3Unorm = 77,
    Bc3UnormSrgb = 78,
    Bc4Typeless = 79,
    Bc4Unorm = 80,
    Bc4Snorm = 81,
    Bc5Typeless = 82,
    Bc5Unorm = 83,
    Bc5Snorm = 84,
    B5G6R5Unorm = 85,
    B5G5R5A1Unorm = 86,
    B8G8R8A8Unorm = 87,
    B8G8R8X8Unorm = 88,
    Bc6HTypeless = 94,
    Bc6HUf16 = 95,
    Bc6HSf16 = 96,
    Bc7Typeless = 97,
    Bc7Unorm = 98,
    Bc7UnormSrgb = 99,
}

public enum D3d10ResourceDimension : uint
{
    Unknown = 0,
    Buffer = 1,
    Texture1D = 2,
    Texture2D = 3,
    Texture3D = 4,
}

[Flags]
public enum D3d10ResourceMiscFlag : uint
{
    None = 0,
    TextureCube = 0x4,
}

public enum D3d10AlphaMode : uint
{
    Unknown = 0,
    Straight = 1,
    Premultiplied = 2,
    Opaque = 3,
    Custom = 4,
}
