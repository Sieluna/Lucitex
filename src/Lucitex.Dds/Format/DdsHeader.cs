namespace Lucitex.Dds.Format;

public sealed record DdsHeader
{
    public required uint Width { get; init; }

    public required uint Height { get; init; }

    public required uint Depth { get; init; }

    public required uint MipMapCount { get; init; }

    public required uint ArraySize { get; init; }

    public required bool IsCubemap { get; init; }

    public required D3d10ResourceDimension Dimension { get; init; }

    public required DxgiFormat Format { get; init; }

    public D3d10AlphaMode AlphaMode { get; init; } = D3d10AlphaMode.Unknown;
}
