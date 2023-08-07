namespace Lucitex.Dds.Format;

internal static class DdsLegacyFormats
{
    private static uint FourCc(char a, char b, char c, char d) =>
        (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

    public static readonly uint Dx10 = FourCc('D', 'X', '1', '0');

    public static DxgiFormat? FromFourCc(uint fourCc)
    {
        if (fourCc == FourCc('D', 'X', 'T', '1'))
        {
            return DxgiFormat.Bc1Unorm;
        }

        if (fourCc == FourCc('D', 'X', 'T', '2') || fourCc == FourCc('D', 'X', 'T', '3'))
        {
            return DxgiFormat.Bc2Unorm;
        }

        if (fourCc == FourCc('D', 'X', 'T', '4') || fourCc == FourCc('D', 'X', 'T', '5'))
        {
            return DxgiFormat.Bc3Unorm;
        }

        if (fourCc == FourCc('A', 'T', 'I', '1') || fourCc == FourCc('B', 'C', '4', 'U'))
        {
            return DxgiFormat.Bc4Unorm;
        }

        if (fourCc == FourCc('B', 'C', '4', 'S'))
        {
            return DxgiFormat.Bc4Snorm;
        }

        if (fourCc == FourCc('A', 'T', 'I', '2') || fourCc == FourCc('B', 'C', '5', 'U'))
        {
            return DxgiFormat.Bc5Unorm;
        }

        if (fourCc == FourCc('B', 'C', '5', 'S'))
        {
            return DxgiFormat.Bc5Snorm;
        }

        return fourCc switch
        {
            36 => DxgiFormat.R16G16B16A16Unorm,
            111 => DxgiFormat.R16Float,
            112 => DxgiFormat.R16G16Float,
            113 => DxgiFormat.R16G16B16A16Float,
            114 => DxgiFormat.R32Float,
            115 => DxgiFormat.R32G32Float,
            116 => DxgiFormat.R32G32B32A32Float,
            _ => null,
        };
    }

    public static DxgiFormat? FromBitMasks(DdsPixelFormat pf)
    {
        if (!pf.Flags.HasFlag(DdsPixelFormatFlags.Rgb))
        {
            return null;
        }

        var hasAlpha = pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels);

        return (pf.RgbBitCount, pf.RBitMask, pf.GBitMask, pf.BBitMask, hasAlpha ? pf.ABitMask : 0u) switch
        {
            (32, 0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000) => DxgiFormat.R8G8B8A8Unorm,
            (32, 0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000) => DxgiFormat.B8G8R8A8Unorm,
            (32, 0x00ff0000, 0x0000ff00, 0x000000ff, 0) => DxgiFormat.B8G8R8X8Unorm,
            (32, 0x000003ff, 0x000ffc00, 0x3ff00000, 0xc0000000) => DxgiFormat.R10G10B10A2Unorm,
            (16, 0x0000f800, 0x000007e0, 0x0000001f, 0) => DxgiFormat.B5G6R5Unorm,
            (16, 0x00007c00, 0x000003e0, 0x0000001f, 0x00008000) => DxgiFormat.B5G5R5A1Unorm,
            (8, 0x000000ff, 0, 0, 0) => DxgiFormat.R8Unorm,
            (16, 0x000000ff, 0, 0, 0x0000ff00) => DxgiFormat.R8G8Unorm,
            _ => null,
        };
    }
}
