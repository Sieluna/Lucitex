using System.Runtime.ExceptionServices;

namespace Lucitex.Core.Execution;

public static class ExecutionScheduler
{
    public static Task RunAsync(IEnumerable<WorkRegion> regions, Func<WorkRegion, CancellationToken, ValueTask> action,
        int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(action);
        return Parallel.ForEachAsync(regions, AsyncOptions(maxDegreeOfParallelism, cancellationToken), action);
    }

    public static Task ForAsync(int fromInclusive, int toExclusive, Func<int, CancellationToken, ValueTask> action,
        int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(toExclusive, fromInclusive);
        return Parallel.ForAsync(fromInclusive, toExclusive, AsyncOptions(maxDegreeOfParallelism, cancellationToken), action);
    }

    public static Task ForAsync(int fromInclusive, int toExclusive, Action<int> action,
        int? maxDegreeOfParallelism = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ForAsync(fromInclusive, toExclusive, (index, token) => {
            token.ThrowIfCancellationRequested();
            action(index);
            return ValueTask.CompletedTask;
        }, maxDegreeOfParallelism, cancellationToken);
    }

    private static ParallelOptions AsyncOptions(int? maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        var options = Options(maxDegreeOfParallelism, cancellationToken);
        options.TaskScheduler = TaskScheduler.Default;
        return options;
    }

    public static void Run(IEnumerable<WorkRegion> regions, Action<WorkRegion> action, int? maxDegreeOfParallelism = null) =>
        Run(regions, action, maxDegreeOfParallelism, default);

    public static void Run(IEnumerable<WorkRegion> regions, Action<WorkRegion> action, int? maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(action);
        var options = Options(maxDegreeOfParallelism, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (options.MaxDegreeOfParallelism == 1 || Environment.ProcessorCount == 1) {
            foreach (var region in regions) {
                cancellationToken.ThrowIfCancellationRequested();
                action(region);
            }
        }
        else {
            Invoke(() => Parallel.ForEach(regions, options, action));
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static void For(int fromInclusive, int toExclusive, Action<int> action, int? maxDegreeOfParallelism = null) =>
        For(fromInclusive, toExclusive, action, maxDegreeOfParallelism, default);

    public static void For(int fromInclusive, int toExclusive, Action<int> action, int? maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(toExclusive, fromInclusive);
        var options = Options(maxDegreeOfParallelism, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if ((long)toExclusive - fromInclusive <= 1 || options.MaxDegreeOfParallelism == 1 || Environment.ProcessorCount == 1) {
            for (var index = fromInclusive; index < toExclusive; index++) {
                cancellationToken.ThrowIfCancellationRequested();
                action(index);
            }
        }
        else {
            Invoke(() => Parallel.For(fromInclusive, toExclusive, options, action));
        }
        cancellationToken.ThrowIfCancellationRequested();
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

    private static ParallelOptions Options(int? maxDegreeOfParallelism, CancellationToken cancellationToken) => new() {
        MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount,
        CancellationToken = cancellationToken,
    };
}
