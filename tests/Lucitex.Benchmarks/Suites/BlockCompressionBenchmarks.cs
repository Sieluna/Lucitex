using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Compression;

namespace Lucitex.Benchmarks.Suites;

[MemoryDiagnoser]
public class BlockDecodeBenchmarks
{
    private const int k_Width = 512;
    private const int k_Height = 512;

    private byte[] _encoded = [];
    private byte[] _decoded = [];
    private BcFormat _format;

    [Params("Bc1", "Bc3", "Bc4", "Bc5", "Bc7")]
    public string FormatName { get; set; } = "Bc1";

    [GlobalSetup]
    public void Setup()
    {
        _format = Enum.Parse<BcFormat>(FormatName);
        _encoded = new byte[BcImageCodec.EncodedByteCount(_format, k_Width, k_Height)];
        _decoded = new byte[k_Width * k_Height * BcImageCodec.ChannelCount(_format)];

        if (_format == BcFormat.Bc7) {
            new Random(31).NextBytes(_encoded);
            return;
        }

        BcImageCodec.Encode(_format, BenchmarkPixels(), k_Width, k_Height, _encoded);
    }

    [Benchmark]
    public void Decode() => BcImageCodec.Decode(_format, _encoded, k_Width, k_Height, _decoded);

    private byte[] BenchmarkPixels() => Pixels(_format);

    internal static byte[] Pixels(BcFormat format)
    {
        var channels = BcImageCodec.ChannelCount(format);
        var rgba = BenchmarkImages.Rgba8(k_Width, k_Height);
        var pixels = new byte[k_Width * k_Height * channels];
        for (var pixel = 0; pixel < k_Width * k_Height; pixel++) {
            for (var channel = 0; channel < channels; channel++) {
                pixels[(pixel * channels) + channel] = rgba[(pixel * 4) + channel];
            }
        }

        return pixels;
    }
}

[MemoryDiagnoser]
public class BlockEncodeBenchmarks
{
    private const int k_Width = 512;
    private const int k_Height = 512;

    private byte[] _pixels = [];
    private byte[] _encoded = [];
    private BcFormat _format;

    [Params("Bc1", "Bc3", "Bc4", "Bc5")]
    public string FormatName { get; set; } = "Bc1";

    [GlobalSetup]
    public void Setup()
    {
        _format = Enum.Parse<BcFormat>(FormatName);
        _pixels = BlockDecodeBenchmarks.Pixels(_format);
        _encoded = new byte[BcImageCodec.EncodedByteCount(_format, k_Width, k_Height)];
    }

    [Benchmark]
    public void Encode() => BcImageCodec.Encode(_format, _pixels, k_Width, k_Height, _encoded);
}

[MemoryDiagnoser]
public class Bc6HBenchmarks
{
    private const int k_Width = 512;
    private const int k_Height = 512;

    private byte[] _encoded = [];
    private float[] _decoded = [];

    [GlobalSetup]
    public void Setup()
    {
        _encoded = new byte[Bc6HImageCodec.EncodedByteCount(k_Width, k_Height)];
        new Random(29).NextBytes(_encoded);
        _decoded = new float[k_Width * k_Height * 3];
    }

    [Benchmark]
    public void Decode() => Bc6HImageCodec.Decode(_encoded, k_Width, k_Height, signed: false, _decoded);
}
