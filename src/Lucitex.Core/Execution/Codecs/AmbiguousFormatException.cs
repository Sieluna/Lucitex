namespace Lucitex.Core.Execution.Codecs;

public sealed class AmbiguousFormatException : Exception
{
    public IReadOnlyList<FormatProbeResult> Candidates { get; }

    public AmbiguousFormatException(IReadOnlyList<FormatProbeResult> candidates)
        : base($"Ambiguous format: could not choose between {string.Join(", ", candidates.Select(c => c.Format))}.")
    {
        Candidates = candidates;
    }
}
