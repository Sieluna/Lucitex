namespace Lucitex.Core.Execution.Codecs;

public enum ProbeConfidence
{
    None,
    Low,
    Medium,
    High,
    Certain,
}

public sealed record FormatProbeResult
{
    public required string Format { get; init; }

    public required ProbeConfidence Confidence { get; init; }

    public required int RequiredBytes { get; init; }

    public string? Variant { get; init; }

    public static FormatProbeResult NoMatch(int requiredBytes) => new()
    {
        Format = string.Empty,
        Confidence = ProbeConfidence.None,
        RequiredBytes = requiredBytes,
    };
}
