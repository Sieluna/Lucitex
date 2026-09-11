using System.Runtime.ExceptionServices;

namespace Lucitex.Core.Execution;

public static class ExecutionScheduler
{
    public static void Run(IEnumerable<WorkRegion> regions, Action<WorkRegion> action, int? maxDegreeOfParallelism = null) =>
        Invoke(() => Parallel.ForEach(regions, Options(maxDegreeOfParallelism), action));

    public static void For(int fromInclusive, int toExclusive, Action<int> action, int? maxDegreeOfParallelism = null)
    {
        if (toExclusive - fromInclusive <= 1 || Environment.ProcessorCount == 1) {
            for (var index = fromInclusive; index < toExclusive; index++) {
                action(index);
            }

            return;
        }

        Invoke(() => Parallel.For(fromInclusive, toExclusive, Options(maxDegreeOfParallelism), action));
    }

    // Work items are independent, so callers classify failures by the exception a single item threw.
    // Parallel wraps those in an AggregateException, which would hide the type every caller matches on.
    private static void Invoke(Action loop)
    {
        try {
            loop();
        }
        catch (AggregateException aggregate) {
            ExceptionDispatchInfo.Capture(aggregate.Flatten().InnerExceptions[0]).Throw();
        }
    }

    private static ParallelOptions Options(int? maxDegreeOfParallelism) => new() {
        MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount,
    };
}
