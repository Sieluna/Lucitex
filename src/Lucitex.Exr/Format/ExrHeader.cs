namespace Lucitex.Exr.Format;

public sealed record ExrRawAttribute
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required byte[] Value { get; init; }
}

public sealed record ExrHeader
{
    public required IReadOnlyList<ExrChannelInfo> Channels { get; init; }

    public required ExrCompressionId Compression { get; init; }

    public required ExrBox2i DataWindow { get; init; }

    public required ExrBox2i DisplayWindow { get; init; }

    public ExrLineOrder LineOrder { get; init; } = ExrLineOrder.IncreasingY;

    public float PixelAspectRatio { get; init; } = 1.0f;

    public ExrTileDesc? Tiles { get; init; }

    public string? PartName { get; init; }

    public string? PartType { get; init; }

    public int? ChunkCount { get; init; }

    public IReadOnlyList<ExrRawAttribute> UnknownAttributes { get; init; } = [];

    public bool IsTiled => Tiles.HasValue;
}
