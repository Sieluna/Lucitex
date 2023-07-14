namespace Lucitex.Core.Execution;

public static class ExecutionScheduler
{
    public static void Run(IEnumerable<WorkRegion> regions, Action<WorkRegion> action, int? maxDegreeOfParallelism = null)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount,
        };

        Parallel.ForEach(regions, options, action);
    }
}
