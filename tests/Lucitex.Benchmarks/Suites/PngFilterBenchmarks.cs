using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Benchmarks.Suites;

[MemoryDiagnoser]
public class PngFilterBenchmarks
{
    private const int k_Rows = 512;

    private byte[] _raw = [];
    private byte[] _work = [];
    private byte[] _output = [];
    private int _rowBytes;

    [Params(3, 4)]
    public int Bpp { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rowBytes = BenchmarkImages.Width * Bpp;
        _raw = Bpp == 3
            ? BenchmarkImages.Rgb8(BenchmarkImages.Width, k_Rows)
            : BenchmarkImages.Rgba8(BenchmarkImages.Width, k_Rows);
        _work = new byte[_raw.Length];
        _output = new byte[_raw.Length];
    }

    [IterationSetup(Targets = [nameof(ReconstructSub), nameof(ReconstructUp), nameof(ReconstructAverage), nameof(ReconstructPaeth)])]
    public void ResetWork() => _raw.CopyTo(_work, 0);

    [Benchmark]
    public void ReconstructSub() => Reconstruct(PngFilterType.Sub);

    [Benchmark]
    public void ReconstructUp() => Reconstruct(PngFilterType.Up);

    [Benchmark]
    public void ReconstructAverage() => Reconstruct(PngFilterType.Average);

    [Benchmark]
    public void ReconstructPaeth() => Reconstruct(PngFilterType.Paeth);

    [Benchmark]
    public void ApplySub() => Apply(PngFilterType.Sub);

    [Benchmark]
    public void ApplyUp() => Apply(PngFilterType.Up);

    [Benchmark]
    public void ApplyAverage() => Apply(PngFilterType.Average);

    [Benchmark]
    public void ApplyPaeth() => Apply(PngFilterType.Paeth);

    private void Reconstruct(PngFilterType filterType)
    {
        var work = _work.AsSpan();
        for (var row = 0; row < k_Rows; row++) {
            var current = work.Slice(row * _rowBytes, _rowBytes);
            var previous = row == 0 ? default : work.Slice((row - 1) * _rowBytes, _rowBytes);
            PngFilter.Reconstruct(filterType, current, previous, Bpp);
        }
    }

    private void Apply(PngFilterType filterType)
    {
        var raw = _raw.AsSpan();
        var output = _output.AsSpan();
        for (var row = 0; row < k_Rows; row++) {
            var current = raw.Slice(row * _rowBytes, _rowBytes);
            var previous = row == 0 ? default : raw.Slice((row - 1) * _rowBytes, _rowBytes);
            PngFilter.Apply(filterType, output.Slice(row * _rowBytes, _rowBytes), current, previous, Bpp);
        }
    }
}
