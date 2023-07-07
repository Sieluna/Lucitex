namespace Lucitex.Core.Diagnostics;

public sealed record UnsupportedFeatureDiagnostic
{
    public required string Feature { get; init; }

    public string? Reason { get; init; }
}
