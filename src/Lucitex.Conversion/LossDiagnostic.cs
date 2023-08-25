namespace Lucitex.Conversion;

public enum LossCategory
{
    Numeric,
    Channel,
    Colorimetric,
    DynamicRange,
    Spatial,
    Orientation,
    Sampling,
    Topology,
    Metadata,
    Compression,
    Transcode,
}

public sealed record LossDiagnostic
{
    public required LossCategory Category { get; init; }

    public required string Message { get; init; }

    public string? ChannelName { get; init; }

    public bool IsSemanticLoss { get; init; } = true;
}
