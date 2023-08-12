namespace Lucitex.Ktx2.Format;

internal enum Ktx2FormatKind
{
    Raw,
    Block,
}

internal readonly record struct Ktx2FormatInfo(Ktx2FormatKind Kind, int BytesPerElement, int BlockWidth, int BlockHeight);

internal static class Ktx2FormatTable
{
    public static Ktx2FormatInfo Get(VkFormat format) => format switch {
        VkFormat.R8Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 1, 1, 1),
        VkFormat.R8G8Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 2, 1, 1),
        VkFormat.R8G8B8A8Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.B8G8R8A8Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.A2B10G10R10Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.R16Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 2, 1, 1),
        VkFormat.R16G16Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.R16G16B16A16Unorm => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 8, 1, 1),
        VkFormat.R16Sfloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 2, 1, 1),
        VkFormat.R16G16Sfloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.R16G16B16A16Sfloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 8, 1, 1),
        VkFormat.R32Sfloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.R32G32Sfloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 8, 1, 1),
        VkFormat.R32G32B32A32Sfloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 16, 1, 1),
        VkFormat.B10G11R11Ufloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.E5B9G9R9Ufloat => new Ktx2FormatInfo(Ktx2FormatKind.Raw, 4, 1, 1),
        VkFormat.Bc1RgbUnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 8, 4, 4),
        VkFormat.Bc1RgbaUnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 8, 4, 4),
        VkFormat.Bc2UnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        VkFormat.Bc3UnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        VkFormat.Bc4UnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 8, 4, 4),
        VkFormat.Bc4SnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 8, 4, 4),
        VkFormat.Bc5UnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        VkFormat.Bc5SnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        VkFormat.Bc6HUfloatBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        VkFormat.Bc6HSfloatBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        VkFormat.Bc7UnormBlock => new Ktx2FormatInfo(Ktx2FormatKind.Block, 16, 4, 4),
        _ => throw new NotSupportedException($"VkFormat {format} has no known layout."),
    };

    public static long RowPitch(VkFormat format, long width)
    {
        var info = Get(format);
        if (info.Kind == Ktx2FormatKind.Raw) {
            return checked(width * info.BytesPerElement);
        }

        var blocksWide = (width + info.BlockWidth - 1) / info.BlockWidth;
        return checked(blocksWide * info.BytesPerElement);
    }

    public static long SliceBytes(VkFormat format, long width, long height)
    {
        var info = Get(format);
        if (info.Kind == Ktx2FormatKind.Raw) {
            return checked(RowPitch(format, width) * height);
        }

        var blocksHigh = (height + info.BlockHeight - 1) / info.BlockHeight;
        return checked(RowPitch(format, width) * blocksHigh);
    }
}
