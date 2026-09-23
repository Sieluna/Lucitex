using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Validation;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks.Suites;

public abstract class CodecBenchmarks
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "ramp@128x128";
    [ParamsSource(nameof(Profiles))]
    public string Profile { get; set; } = "";
    public IEnumerable<string> Cases => RunOptions.Current.Cases;
    public IEnumerable<string> Profiles => RunOptions.Current.Profiles.Intersect(FormatCatalog.Get(Format).Profiles);
    protected abstract string Format { get; }
    protected abstract string OperationName { get; }
    private CodecSession _session = null!;

    [GlobalSetup(Target = nameof(Lucitex))]
    public void SetupLucitex() => Setup(Library.Lucitex);
    [GlobalSetup(Target = nameof(ImageSharp))]
    public void SetupImageSharp() => Setup(Library.ImageSharp);
    [GlobalSetup(Target = nameof(SkiaSharp))]
    public void SetupSkiaSharp() => Setup(Library.SkiaSharp);
    [GlobalSetup(Target = nameof(NetVips))]
    public void SetupNetVips() => Setup(Library.NetVips);
    [GlobalSetup(Target = nameof(MagickNet))]
    public void SetupMagickNet() => Setup(Library.MagickNet);

    private void Setup(Library library)
    {
        _session = new CodecSession(new ComparisonCase(Case, Profile, library, Enum.Parse<Operation>(OperationName),
            FormatCatalog.Conversions.SingleOrDefault(r => r.BenchmarkType.IsAssignableFrom(GetType()))?.Source));
        ValidationStore.CheckWorkerResult(AccuracyCheck.Run(_session));
    }

    [Benchmark(Baseline = true)]
    public byte[] Lucitex() => _session.Run();
    [Benchmark]
    public byte[] ImageSharp() => _session.Run();
    [Benchmark]
    public byte[] SkiaSharp() => _session.Run();
    [Benchmark]
    public byte[] NetVips() => _session.Run();
    [Benchmark]
    public byte[] MagickNet() => _session.Run();
}

[BenchmarkCategory("JPEG.Encode")]
public class JpegEncodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "jpeg";
    protected override string OperationName => "Encode";
}

[BenchmarkCategory("JPEG.Decode")]
public class JpegDecodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "jpeg";
    protected override string OperationName => "Decode";
}

[BenchmarkCategory("PNG.Encode")]
public class PngEncodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "png";
    protected override string OperationName => "Encode";
}

[BenchmarkCategory("PNG.Decode")]
public class PngDecodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "png";
    protected override string OperationName => "Decode";
}

[BenchmarkCategory("Convert.PNG-to-JPEG")]
public class PngToJpegBenchmarks : CodecBenchmarks
{
    protected override string Format => "jpeg";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.JPEG-to-PNG")]
public class JpegToPngBenchmarks : CodecBenchmarks
{
    protected override string Format => "png";
    protected override string OperationName => "Convert";
}
