namespace Lucitex.Core.Color;

public enum TransferFunction
{
    Unknown,
    Linear,
    Srgb,
}

public readonly record struct Chromaticity(double X, double Y);

public sealed record Chromaticities
{
    public required Chromaticity Red { get; init; }

    public required Chromaticity Green { get; init; }

    public required Chromaticity Blue { get; init; }

    public required Chromaticity White { get; init; }
}

public sealed record ColorEncoding
{
    public TransferFunction Transfer { get; init; } = TransferFunction.Unknown;

    public Chromaticities? Primaries { get; init; }

    public byte[]? IccProfile { get; init; }
}

public enum MissingColorInformationPolicy
{
    PreserveUnknown,
    AssumeSRGB,
    AssumeLinear,
    Reject,
}
