using Lucitex.Core.Execution;

namespace Lucitex.Dds.Format;

internal static class DdsHeaderReader
{
    private const uint k_Magic = 0x20534444;
    private const uint k_HeaderSize = 124;

    public static DdsHeader Read(DdsBinaryReader reader)
    {
        var magic = reader.ReadUInt32();
        if (magic != k_Magic) {
            throw new ImageFormatException("dds", "BadMagic", "Stream does not start with the DDS magic number.");
        }

        var dwSize = reader.ReadUInt32();
        if (dwSize != k_HeaderSize) {
            throw new ImageFormatException("dds", "BadHeader", $"DDS header size {dwSize} is invalid; expected {k_HeaderSize}.");
        }

        var flags = (DdsHeaderFlags)reader.ReadUInt32();
        var height = reader.ReadUInt32();
        var width = reader.ReadUInt32();
        reader.ReadUInt32();
        var depth = reader.ReadUInt32();
        var mipMapCount = reader.ReadUInt32();
        reader.ReadBytes(44);

        var pixelFormat = ReadPixelFormat(reader);

        var caps = (DdsCaps)reader.ReadUInt32();
        var caps2 = (DdsCaps2)reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();

        if (width == 0 || height == 0) {
            throw new ImageFormatException("dds", "BadHeader", "DDS width and height must be positive.");
        }

        var hasMips = flags.HasFlag(DdsHeaderFlags.MipMapCount) && mipMapCount > 0;
        var effectiveMipCount = hasMips ? mipMapCount : 1;
        var hasDepth = flags.HasFlag(DdsHeaderFlags.Depth) && depth > 0;
        var effectiveDepth = hasDepth ? depth : 1;

        var isCubemapLegacy = caps2.HasFlag(DdsCaps2.Cubemap);

        DxgiFormat format;
        uint arraySize;
        D3d10ResourceDimension dimension;
        var isCubemap = isCubemapLegacy;
        var alphaMode = D3d10AlphaMode.Unknown;

        if (pixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCC) && pixelFormat.FourCC == DdsLegacyFormats.Dx10) {
            var dx10 = ReadDxt10Header(reader);
            format = dx10.Format;
            dimension = dx10.Dimension;
            arraySize = Math.Max(1, dx10.ArraySize);
            isCubemap = dx10.MiscFlag.HasFlag(D3d10ResourceMiscFlag.TextureCube);
            alphaMode = dx10.AlphaMode;
        }
        else {
            format = ResolveLegacyFormat(pixelFormat);
            arraySize = 1;
            dimension = hasDepth ? D3d10ResourceDimension.Texture3D : D3d10ResourceDimension.Texture2D;
        }

        if (isCubemap && dimension == D3d10ResourceDimension.Unknown) {
            dimension = D3d10ResourceDimension.Texture2D;
        }

        return new DdsHeader {
            Width = width,
            Height = height,
            Depth = effectiveDepth,
            MipMapCount = effectiveMipCount,
            ArraySize = arraySize,
            IsCubemap = isCubemap,
            Dimension = dimension,
            Format = format,
            AlphaMode = alphaMode,
        };
    }

    private static DxgiFormat ResolveLegacyFormat(DdsPixelFormat pixelFormat)
    {
        if (pixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCC)) {
            return DdsLegacyFormats.FromFourCc(pixelFormat.FourCC)
                ?? throw new ImageFormatException("dds", "Unsupported.Dds.LegacyFourCC", $"Unrecognized legacy DDS FourCC 0x{pixelFormat.FourCC:X8}.");
        }

        return DdsLegacyFormats.FromBitMasks(pixelFormat)
            ?? throw new ImageFormatException("dds", "Unsupported.Dds.LegacyPixelFormat", "Unrecognized legacy DDS bitmask pixel format.");
    }

    private static DdsPixelFormat ReadPixelFormat(DdsBinaryReader reader)
    {
        var size = reader.ReadUInt32();
        if (size != DdsPixelFormat.StructSize) {
            throw new ImageFormatException("dds", "BadHeader", $"DDS_PIXELFORMAT size {size} is invalid; expected {DdsPixelFormat.StructSize}.");
        }

        var flags = (DdsPixelFormatFlags)reader.ReadUInt32();
        var fourCc = reader.ReadUInt32();
        var rgbBitCount = reader.ReadUInt32();
        var rMask = reader.ReadUInt32();
        var gMask = reader.ReadUInt32();
        var bMask = reader.ReadUInt32();
        var aMask = reader.ReadUInt32();

        return new DdsPixelFormat(flags, fourCc, rgbBitCount, rMask, gMask, bMask, aMask);
    }

    private static (DxgiFormat Format, D3d10ResourceDimension Dimension, D3d10ResourceMiscFlag MiscFlag, uint ArraySize, D3d10AlphaMode AlphaMode) ReadDxt10Header(DdsBinaryReader reader)
    {
        var format = (DxgiFormat)reader.ReadUInt32();
        var dimension = (D3d10ResourceDimension)reader.ReadUInt32();
        var miscFlag = (D3d10ResourceMiscFlag)reader.ReadUInt32();
        var arraySize = reader.ReadUInt32();
        var miscFlags2 = reader.ReadUInt32();

        return (format, dimension, miscFlag, arraySize, (D3d10AlphaMode)(miscFlags2 & 0x7));
    }
}
