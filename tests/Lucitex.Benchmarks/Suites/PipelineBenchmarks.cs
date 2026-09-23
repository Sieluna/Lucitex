using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Conversion;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Hdr;
using Lucitex.Png;

namespace Lucitex.Benchmarks.Suites;

[MemoryDiagnoser]
public class PngPipelineBenchmarks
{
    private const long k_Width = 1024;
    private const long k_Height = 1024;

    private ImageAssetDescriptor _rgba8 = null!;
    private byte[] _pixels = [];
    private byte[] _encoded = [];
    private byte[] _decodeBuffer = [];

    [GlobalSetup]
    public void Setup()
    {
        _rgba8 = BenchmarkDescriptors.Png(k_Width, k_Height, SampleType.UNorm8, ["R", "G", "B", "A"]);
        _pixels = BenchmarkImages.Rgba8((int)k_Width, (int)k_Height);
        _decodeBuffer = new byte[_pixels.Length];
        _encoded = Encode();
    }

    [Benchmark]
    public byte[] Encode()
    {
        var codec = new PngCodec();
        using var stream = new MemoryStream(_pixels.Length);
        using var writer = codec.CreateWriter(stream, _rgba8);
        writer.Write(BenchmarkDescriptors.Region.Full(k_Width, k_Height), _pixels);
        writer.Finish();
        return stream.ToArray();
    }

    [Benchmark]
    public int Decode()
    {
        var codec = new PngCodec();
        using var stream = new MemoryStream(_encoded, writable: false);
        using var reader = codec.OpenReader(stream);
        return reader.Read(BenchmarkDescriptors.Region.Full(k_Width, k_Height), _decodeBuffer);
    }
}

[MemoryDiagnoser]
public class ExrPipelineBenchmarks
{
    private const long k_Width = 1024;
    private const long k_Height = 1024;

    private ImageAssetDescriptor _rgbaHalf = null!;
    private byte[] _pixels = [];
    private byte[] _encoded = [];
    private byte[] _decodeBuffer = [];

    [Params(ExrCompressionId.None, ExrCompressionId.Rle, ExrCompressionId.Zip, ExrCompressionId.Piz)]
    public ExrCompressionId Compression { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rgbaHalf = BenchmarkDescriptors.Exr(k_Width, k_Height, SampleType.Float16, ["R", "G", "B", "A"]);
        _pixels = BenchmarkImages.HalfBytes((int)(k_Width * k_Height * 4));
        _decodeBuffer = new byte[_pixels.Length];
        _encoded = Encode();
    }

    [Benchmark]
    public byte[] Encode()
    {
        var codec = new ExrCodec(Compression);
        using var stream = new MemoryStream(_pixels.Length);
        using var writer = codec.CreateWriter(stream, _rgbaHalf);
        writer.Write(BenchmarkDescriptors.Region.Full(k_Width, k_Height), _pixels);
        writer.Finish();
        return stream.ToArray();
    }

    [Benchmark]
    public int Decode()
    {
        var codec = new ExrCodec(Compression);
        using var stream = new MemoryStream(_encoded, writable: false);
        using var reader = codec.OpenReader(stream);
        return reader.Read(BenchmarkDescriptors.Region.Full(k_Width, k_Height), _decodeBuffer);
    }
}

[MemoryDiagnoser]
public class HdrPipelineBenchmarks
{
    private const long k_Width = 1024;
    private const long k_Height = 1024;

    private ImageAssetDescriptor _rgbe = null!;
    private byte[] _pixels = [];
    private byte[] _encoded = [];
    private byte[] _decodeBuffer = [];

    [GlobalSetup]
    public void Setup()
    {
        _rgbe = BenchmarkDescriptors.Hdr(k_Width, k_Height);
        _pixels = BenchmarkImages.Rgbe((int)(k_Width * k_Height));
        _decodeBuffer = new byte[_pixels.Length];
        _encoded = Encode();
    }

    [Benchmark]
    public byte[] Encode()
    {
        var codec = new HdrCodec();
        using var stream = new MemoryStream(_pixels.Length);
        using var writer = codec.CreateWriter(stream, _rgbe);
        writer.Write(BenchmarkDescriptors.Region.Full(k_Width, k_Height), _pixels);
        writer.Finish();
        return stream.ToArray();
    }

    [Benchmark]
    public int Decode()
    {
        var codec = new HdrCodec();
        using var stream = new MemoryStream(_encoded, writable: false);
        using var reader = codec.OpenReader(stream);
        return reader.Read(BenchmarkDescriptors.Region.Full(k_Width, k_Height), _decodeBuffer);
    }
}

[MemoryDiagnoser]
public class ConversionPipelineBenchmarks
{
    private const long k_Width = 1024;
    private const long k_Height = 1024;

    private byte[] _exrSource = [];

    [GlobalSetup]
    public void Setup()
    {
        var descriptor = BenchmarkDescriptors.Exr(k_Width, k_Height, SampleType.Float16, ["R", "G", "B", "A"]);
        var pixels = BenchmarkImages.HalfBytes((int)(k_Width * k_Height * 4));
        var codec = new ExrCodec(ExrCompressionId.Zip);
        using var stream = new MemoryStream();
        using var writer = codec.CreateWriter(stream, descriptor);
        writer.Write(BenchmarkDescriptors.Region.Full(k_Width, k_Height), pixels);
        writer.Finish();
        _exrSource = stream.ToArray();
    }

    [Benchmark]
    public long ExrToPng()
    {
        var sourceCodec = new ExrCodec();
        using var sourceStream = new MemoryStream(_exrSource, writable: false);
        using var reader = sourceCodec.OpenReader(sourceStream);

        var targetCodec = new PngCodec();
        var plan = ConversionPlanner.Plan(reader.Describe(), targetCodec.Capabilities, ConversionPolicy.Preview).Plan!;

        using var targetStream = new MemoryStream();
        using var writer = targetCodec.CreateWriter(targetStream, plan.TargetDescriptor);
        ConversionExecutor.Execute(plan, reader, sourceCodec.Capabilities.SampleByteOrder, writer, targetCodec.Capabilities.SampleByteOrder);
        return targetStream.Length;
    }
}
