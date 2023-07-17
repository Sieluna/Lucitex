namespace Lucitex.Exr.Format;

public sealed record ExrChannelInfo
{
    public required string Name { get; init; }

    public required ExrPixelType PixelType { get; init; }

    public bool PLinear { get; init; }

    public int XSampling { get; init; } = 1;

    public int YSampling { get; init; } = 1;

    public int BytesPerSample => PixelType switch
    {
        ExrPixelType.UInt => 4,
        ExrPixelType.Half => 2,
        ExrPixelType.Float => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(PixelType)),
    };
}
