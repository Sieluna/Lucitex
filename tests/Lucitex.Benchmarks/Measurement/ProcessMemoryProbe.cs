using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lucitex.Benchmarks.Codecs;

namespace Lucitex.Benchmarks.Measurement;

internal sealed record ProcessMemoryResult(long BaselinePrivateBytes, long SampledPeakPrivateBytes,
    long BaselineWorkingSetBytes, long SampledPeakWorkingSetBytes, long ManagedBytesPerOperation,
    int Iterations, int Samples, string Scope);

internal static class ProcessMemoryProbe
{
    public static ProcessMemoryResult Measure(ComparisonCase comparison)
    {
        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--memory-worker");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(comparison))));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Memory worker could not start.");
        var error = process.StandardError.ReadToEndAsync();
        try {
            var ready = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
            if (ready != "READY") {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                }
                throw new InvalidOperationException($"Memory worker failed: {ready}; {error.GetAwaiter().GetResult()}");
            }
            var (baselinePrivate, baselineWorkingSet) = Snapshot(process);
            var peakPrivate = baselinePrivate;
            var peakWorkingSet = baselineWorkingSet;
            var samples = 0;
            process.StandardInput.WriteLine("GO");
            process.StandardInput.Flush();
            var response = process.StandardOutput.ReadLineAsync();
            var timeout = Stopwatch.StartNew();
            while (!response.IsCompleted) {
                if (timeout.Elapsed > TimeSpan.FromMinutes(2)) {
                    throw new TimeoutException("Memory worker timed out.");
                }
                var (privateBytes, workingSet) = Snapshot(process);
                peakPrivate = Math.Max(peakPrivate, privateBytes);
                peakWorkingSet = Math.Max(peakWorkingSet, workingSet);
                samples++;
                Thread.Sleep(1);
            }
            var result = JsonSerializer.Deserialize<WorkerResult>(response.GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Memory worker exited without measurements."))!;
            process.StandardInput.WriteLine("DONE");
            process.StandardInput.Flush();
            if (!process.WaitForExit(10000)) {
                throw new TimeoutException("Memory worker did not exit.");
            }
            if (process.ExitCode != 0) {
                throw new InvalidOperationException("Memory worker failed: " + error.GetAwaiter().GetResult());
            }
            return new ProcessMemoryResult(baselinePrivate, peakPrivate, baselineWorkingSet, peakWorkingSet,
                result.ManagedBytesPerOperation, result.Iterations, samples,
                OperatingSystem.IsLinux()
                    ? "Linux private RSS (smaps_rollup Private_Clean + Private_Dirty) and total RSS; whole warmed process, including pools/runtime/FFI. Sampled lower bounds, not native allocations."
                    : "Windows Private Bytes (commit) and Working Set; whole warmed process, including pools/runtime/FFI. Sampled lower bounds, not native allocations.");
        }
        finally {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    public static void RunWorker(string serialized)
    {
        var comparison = JsonSerializer.Deserialize<ComparisonCase>(Encoding.UTF8.GetString(Convert.FromBase64String(serialized)))!;
        var session = new CodecSession(comparison);
        for (var i = 0; i < 3; i++) {
            GC.KeepAlive(session.Run());
        }
        var iterations = session.Image.Pixels.Length <= 65536 ? 128 : 8;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Console.WriteLine("READY");
        if (Console.ReadLine() != "GO") {
            throw new InvalidDataException("Memory worker protocol mismatch.");
        }
        var allocated = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < iterations; i++) {
            GC.KeepAlive(session.Run());
        }
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        Console.WriteLine(JsonSerializer.Serialize(new WorkerResult(allocated / iterations, iterations)));
        Console.ReadLine();
    }

    private static (long PrivateBytes, long WorkingSet) Snapshot(Process process)
    {
        process.Refresh();
        if (!OperatingSystem.IsLinux()) {
            return (process.PrivateMemorySize64, process.WorkingSet64);
        }
        long privateBytes = 0;
        foreach (var line in File.ReadLines($"/proc/{process.Id}/smaps_rollup")) {
            if (line.StartsWith("Private_Clean:", StringComparison.Ordinal) || line.StartsWith("Private_Dirty:", StringComparison.Ordinal)) {
                privateBytes += long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;
            }
        }
        return (privateBytes, process.WorkingSet64);
    }

    private sealed record WorkerResult(long ManagedBytesPerOperation, int Iterations);
}
