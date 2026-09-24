namespace Lucitex.Fuzz;

internal enum ComparisonKind
{
    NotCompared,
    Agreement,
    AcceptanceMismatch,
    Inconclusive,
    Failure,
}

internal static class DifferentialComparison
{
    public static ComparisonKind Compare(DecodeOutcome managed, DecodeOutcome? native)
    {
        if (managed.IsFailure || native?.IsFailure == true) {
            return ComparisonKind.Failure;
        }
        if (native is null) {
            return ComparisonKind.NotCompared;
        }
        if (managed.Status is DecodeStatus.Unsupported or DecodeStatus.ResourceLimit ||
            native.Status is DecodeStatus.Unsupported or DecodeStatus.ResourceLimit) {
            return ComparisonKind.Inconclusive;
        }
        return managed.Status == native.Status ? ComparisonKind.Agreement : ComparisonKind.AcceptanceMismatch;
    }
}
