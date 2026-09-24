namespace Lucitex.Fuzz;

internal enum DecodeStatus
{
    Accepted,
    Rejected,
    Unsupported,
    ResourceLimit,
    Crash,
    Timeout,
    InfrastructureFailure,
}

internal sealed record DecodeOutcome(DecodeStatus Status, string? Detail = null, int Subresources = 0, long DecodedBytes = 0)
{
    public bool Accepted => Status == DecodeStatus.Accepted;

    public bool IsFailure => Status is DecodeStatus.Crash or DecodeStatus.Timeout or DecodeStatus.InfrastructureFailure;
}
