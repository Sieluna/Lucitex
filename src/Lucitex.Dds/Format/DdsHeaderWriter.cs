namespace Lucitex.Dds.Format;

internal static class DdsHeaderWriter
{
    private const uint Magic = 0x20534444;
    private const uint HeaderSize = 124;

    public static void Write(DdsBinaryWriter writer, DdsHeader header)
    {
        var flags = DdsHeaderFlags.Caps | DdsHeaderFlags.Height | DdsHeaderFlags.Width | DdsHeaderFlags.PixelFormat;
        if (header.MipMapCount > 1)
        {
            flags |= DdsHeaderFlags.MipMapCount;
        }

        if (header.Dimension == D3d10ResourceDimension.Texture3D)
        {
            flags |= DdsHeaderFlags.Depth;
        }

        var caps = DdsCaps.Texture;
        if (header.MipMapCount > 1 || header.IsCubemap || header.ArraySize > 1)
        {
            caps |= DdsCaps.Complex;
        }

        if (header.MipMapCount > 1)
        {
            caps |= DdsCaps.Mipmap;
        }

        var caps2 = DdsCaps2.None;
        if (header.IsCubemap)
        {
            caps2 |= DdsCaps2.CubemapAllFaces;
        }

        if (header.Dimension == D3d10ResourceDimension.Texture3D)
        {
            caps2 |= DdsCaps2.Volume;
        }

        writer.WriteUInt32(Magic);
        writer.WriteUInt32(HeaderSize);
        writer.WriteUInt32((uint)flags);
        writer.WriteUInt32(header.Height);
        writer.WriteUInt32(header.Width);
        writer.WriteUInt32(0);
        writer.WriteUInt32(header.Dimension == D3d10ResourceDimension.Texture3D ? header.Depth : 0);
        writer.WriteUInt32(header.MipMapCount);
        writer.WriteBytes(new byte[44]);

        WritePixelFormat(writer);

        writer.WriteUInt32((uint)caps);
        writer.WriteUInt32((uint)caps2);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);

        writer.WriteUInt32((uint)header.Format);
        writer.WriteUInt32((uint)header.Dimension);
        writer.WriteUInt32((uint)(header.IsCubemap ? D3d10ResourceMiscFlag.TextureCube : D3d10ResourceMiscFlag.None));
        writer.WriteUInt32(header.ArraySize);
        writer.WriteUInt32((uint)header.AlphaMode);
    }

    private static void WritePixelFormat(DdsBinaryWriter writer)
    {
        writer.WriteUInt32(DdsPixelFormat.StructSize);
        writer.WriteUInt32((uint)DdsPixelFormatFlags.FourCC);
        writer.WriteUInt32(DdsLegacyFormats.Dx10);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
    }
}
