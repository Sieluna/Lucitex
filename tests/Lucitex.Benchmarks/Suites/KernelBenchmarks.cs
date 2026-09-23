using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Benchmarks.Suites;

[BenchmarkCategory("PNG.CRC32")]
public class Crc32Benchmarks
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "ramp@128x128";
    public IEnumerable<string> Cases => RunOptions.Current.Cases;
    private byte[] _pixels = [];

    [GlobalSetup]
    public void Setup()
    {
        _pixels = TestImage.Create(Case, "png").Pixels;
        if (Crc32.Compute("123456789"u8) != 0xCBF43926 || Lucitex() != DotNet()) {
            throw new InvalidDataException("CRC32 disagrees with the published check value or .NET implementation.");
        }
    }

    [Benchmark(Baseline = true)]
    public uint Lucitex() => Crc32.Compute(_pixels);
    [Benchmark]
    public uint DotNet() => System.IO.Hashing.Crc32.HashToUInt32(_pixels);
}

[BenchmarkCategory("PNG.Filter.Apply")]
public class PngFilterBenchmarks
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "ramp@128x128";
    [Params(PngFilterType.Sub, PngFilterType.Up, PngFilterType.Average, PngFilterType.Paeth)]
    public PngFilterType Filter { get; set; }
    public IEnumerable<string> Cases => RunOptions.Current.Cases;
    private byte[] _raw = [];
    private byte[] _output = [];
    private int _rowBytes;

    [GlobalSetup]
    public void Setup()
    {
        var image = TestImage.Create(Case, "png");
        _raw = image.Pixels;
        _output = new byte[_raw.Length];
        _rowBytes = image.Width * 4;
        var expected = ScalarReference().ToArray();
        if (!expected.AsSpan().SequenceEqual(Lucitex())) {
            throw new InvalidDataException("PNG filter disagrees with the scalar formula.");
        }
        for (var offset = 0; offset < _raw.Length; offset += _rowBytes) {
            PngFilter.Reconstruct(Filter, _output.AsSpan(offset, _rowBytes),
                offset == 0 ? default : _output.AsSpan(offset - _rowBytes, _rowBytes), 4);
        }
        if (!_raw.AsSpan().SequenceEqual(_output)) {
            throw new InvalidDataException("PNG filter reconstruction does not recover the source.");
        }
    }

    [Benchmark(Baseline = true)]
    public byte[] Lucitex()
    {
        for (var offset = 0; offset < _raw.Length; offset += _rowBytes) {
            PngFilter.Apply(Filter, _output.AsSpan(offset, _rowBytes), _raw.AsSpan(offset, _rowBytes),
                offset == 0 ? default : _raw.AsSpan(offset - _rowBytes, _rowBytes), 4);
        }
        return _output;
    }

    [Benchmark]
    public byte[] ScalarReference()
    {
        for (var offset = 0; offset < _raw.Length; offset += _rowBytes) {
            for (var x = 0; x < _rowBytes; x++) {
                var left = x < 4 ? 0 : _raw[offset + x - 4];
                var above = offset == 0 ? 0 : _raw[offset + x - _rowBytes];
                var upperLeft = offset == 0 || x < 4 ? 0 : _raw[offset + x - _rowBytes - 4];
                var predictor = Filter switch {
                    PngFilterType.Sub => left,
                    PngFilterType.Up => above,
                    PngFilterType.Average => (left + above) / 2,
                    PngFilterType.Paeth => Paeth(left, above, upperLeft),
                    _ => throw new ArgumentOutOfRangeException(),
                };
                _output[offset + x] = unchecked((byte)(_raw[offset + x] - predictor));
            }
        }
        return _output;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var prediction = left + above - upperLeft;
        var a = Math.Abs(prediction - left);
        var b = Math.Abs(prediction - above);
        var c = Math.Abs(prediction - upperLeft);
        return a <= b && a <= c ? left : b <= c ? above : upperLeft;
    }
}
