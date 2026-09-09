using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Png.Format;

namespace Lucitex.Benchmarks.Suites;

[MemoryDiagnoser]
public class Crc32Benchmarks
{
    private byte[] _data = [];

    [Params(8192, 4 * 1024 * 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() => _data = BenchmarkImages.Rgba8(Size / 4, 1);

    [Benchmark]
    public uint Compute() => Crc32.Compute(_data);
}
