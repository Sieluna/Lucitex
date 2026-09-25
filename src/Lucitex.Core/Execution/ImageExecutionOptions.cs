namespace Lucitex.Core.Execution;

public sealed record ImageExecutionOptions
{
    private int _maxDegreeOfParallelism = Environment.ProcessorCount;

    public int MaxDegreeOfParallelism {
        get => _maxDegreeOfParallelism;
        init {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxDegreeOfParallelism = value;
        }
    }

    public static ImageExecutionOptions Default { get; } = new();
}
