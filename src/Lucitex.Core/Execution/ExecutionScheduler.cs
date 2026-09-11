namespace Lucitex.Core.Execution;

public static class ExecutionScheduler
{
    public static void Run(IEnumerable<WorkRegion> regions, Action<WorkRegion> action, int? maxDegreeOfParallelism = null) =>
        Parallel.ForEach(regions, Options(maxDegreeOfParallelism), action);

    public static void For(int fromInclusive, int toExclusive, Action<int> action, int? maxDegreeOfParallelism = null)
    {
        if (toExclusive - fromInclusive <= 1 || Environment.ProcessorCount == 1) {
            for (var index = fromInclusive; index < toExclusive; index++) {
                action(index);
            }

            return;
        }

        Parallel.For(fromInclusive, toExclusive, Options(maxDegreeOfParallelism), action);
    }

    private static ParallelOptions Options(int? maxDegreeOfParallelism) => new() {
        MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount,
    };
}
