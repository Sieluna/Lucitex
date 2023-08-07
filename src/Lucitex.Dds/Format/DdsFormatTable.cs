namespace Lucitex.Dds.Format;

internal enum DdsFormatKind
{
    Raw,
    Block,
}

internal readonly record struct DdsFormatInfo(DdsFormatKind Kind, int BytesPerElement, int BlockWidth, int BlockHeight);

internal static class DdsFormatTable
{
    public static DdsFormatInfo Get(DxgiFormat format) => format switch
    {
        DxgiFormat.R8Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 1, 1, 1),
        DxgiFormat.R8G8Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 2, 1, 1),
        DxgiFormat.R8G8B8A8Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.B8G8R8A8Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.R16Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 2, 1, 1),
        DxgiFormat.R16G16Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.R16G16B16A16Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 8, 1, 1),
        DxgiFormat.R16Float => new DdsFormatInfo(DdsFormatKind.Raw, 2, 1, 1),
        DxgiFormat.R16G16Float => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.R16G16B16A16Float => new DdsFormatInfo(DdsFormatKind.Raw, 8, 1, 1),
        DxgiFormat.R32Float => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.R32G32Float => new DdsFormatInfo(DdsFormatKind.Raw, 8, 1, 1),
        DxgiFormat.R32G32B32A32Float => new DdsFormatInfo(DdsFormatKind.Raw, 16, 1, 1),
        DxgiFormat.R10G10B10A2Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.R11G11B10Float => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.R9G9B9E5SharedExp => new DdsFormatInfo(DdsFormatKind.Raw, 4, 1, 1),
        DxgiFormat.B5G6R5Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 2, 1, 1),
        DxgiFormat.B5G5R5A1Unorm => new DdsFormatInfo(DdsFormatKind.Raw, 2, 1, 1),
        DxgiFormat.Bc1Unorm => new DdsFormatInfo(DdsFormatKind.Block, 8, 4, 4),
        DxgiFormat.Bc2Unorm => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        DxgiFormat.Bc3Unorm => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        DxgiFormat.Bc4Unorm => new DdsFormatInfo(DdsFormatKind.Block, 8, 4, 4),
        DxgiFormat.Bc4Snorm => new DdsFormatInfo(DdsFormatKind.Block, 8, 4, 4),
        DxgiFormat.Bc5Unorm => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        DxgiFormat.Bc5Snorm => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        DxgiFormat.Bc6HUf16 => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        DxgiFormat.Bc6HSf16 => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        DxgiFormat.Bc7Unorm => new DdsFormatInfo(DdsFormatKind.Block, 16, 4, 4),
        _ => throw new NotSupportedException($"DXGI format {format} has no known layout."),
    };

    public static long RowPitch(DxgiFormat format, long width)
    {
        var info = Get(format);
        if (info.Kind == DdsFormatKind.Raw)
        {
            return checked(width * info.BytesPerElement);
        }

        var blocksWide = (width + info.BlockWidth - 1) / info.BlockWidth;
        return checked(blocksWide * info.BytesPerElement);
    }

    public static long SliceBytes(DxgiFormat format, long width, long height)
    {
        var info = Get(format);
        if (info.Kind == DdsFormatKind.Raw)
        {
            return checked(RowPitch(format, width) * height);
        }

        var blocksHigh = (height + info.BlockHeight - 1) / info.BlockHeight;
        return checked(RowPitch(format, width) * blocksHigh);
    }
}
