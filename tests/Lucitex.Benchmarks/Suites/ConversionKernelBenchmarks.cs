using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Conversion.Kernels;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Sampling;

namespace Lucitex.Benchmarks.Suites;

[MemoryDiagnoser]
public class SampleTypeKernelBenchmarks
{
    private const int k_Samples = 1 << 20;

    private byte[] _unorm8 = [];
    private byte[] _unorm16Little = [];
    private byte[] _unorm16Big = [];
    private byte[] _half = [];
    private byte[] _byteDestination = [];
    private float[] _floats = [];
    private float[] _floatDestination = [];

    [GlobalSetup]
    public void Setup()
    {
        _unorm8 = BenchmarkImages.Rgba8(k_Samples / 4, 1);
        _unorm16Little = BenchmarkImages.UNorm16Bytes(k_Samples, bigEndian: false);
        _unorm16Big = BenchmarkImages.UNorm16Bytes(k_Samples, bigEndian: true);
        _half = BenchmarkImages.HalfBytes(k_Samples);
        _floats = BenchmarkImages.Gradient(k_Samples);
        _floatDestination = new float[k_Samples];
        _byteDestination = new byte[k_Samples * sizeof(float)];
    }

    [Benchmark]
    public void UNorm8ToFloat32() =>
        SampleTypeConversionKernel.ToFloat32(_unorm8, SampleType.UNorm8, SampleByteOrder.LittleEndian, _floatDestination);

    [Benchmark]
    public void Float32ToUNorm8() =>
        SampleTypeConversionKernel.FromFloat32(_floats, SampleType.UNorm8, SampleByteOrder.LittleEndian, _byteDestination);

    [Benchmark]
    public void UNorm16ToFloat32() =>
        SampleTypeConversionKernel.ToFloat32(_unorm16Little, SampleType.UNorm16, SampleByteOrder.LittleEndian, _floatDestination);

    [Benchmark]
    public void UNorm16BigEndianToFloat32() =>
        SampleTypeConversionKernel.ToFloat32(_unorm16Big, SampleType.UNorm16, SampleByteOrder.BigEndian, _floatDestination);

    [Benchmark]
    public void Float32ToUNorm16() =>
        SampleTypeConversionKernel.FromFloat32(_floats, SampleType.UNorm16, SampleByteOrder.LittleEndian, _byteDestination);

    [Benchmark]
    public void Float32ToUNorm16BigEndian() =>
        SampleTypeConversionKernel.FromFloat32(_floats, SampleType.UNorm16, SampleByteOrder.BigEndian, _byteDestination);

    [Benchmark]
    public void Float16ToFloat32() =>
        SampleTypeConversionKernel.ToFloat32(_half, SampleType.Float16, SampleByteOrder.LittleEndian, _floatDestination);

    [Benchmark]
    public void Float32ToFloat16() =>
        SampleTypeConversionKernel.FromFloat32(_floats, SampleType.Float16, SampleByteOrder.LittleEndian, _byteDestination);
}

[MemoryDiagnoser]
public class ColorTransformBenchmarks
{
    private const int k_Samples = 1 << 20;

    private float[] _source = [];
    private float[] _work = [];

    [GlobalSetup]
    public void Setup()
    {
        _source = BenchmarkImages.Gradient(k_Samples);
        _work = new float[k_Samples];
    }

    [IterationSetup]
    public void Reset() => _source.CopyTo(_work, 0);

    [Benchmark]
    public void LinearToSrgb() => ColorTransformKernel.LinearToSrgb(_work);

    [Benchmark]
    public void SrgbToLinear() => ColorTransformKernel.SrgbToLinear(_work);
}

[MemoryDiagnoser]
public class AlphaKernelBenchmarks
{
    private const int k_Samples = 1 << 20;

    private float[] _color = [];
    private float[] _alpha = [];

    [GlobalSetup]
    public void Setup()
    {
        _color = BenchmarkImages.Gradient(k_Samples);
        _alpha = BenchmarkImages.Gradient(k_Samples, 23);
    }

    [Benchmark]
    public void Premultiply() => AlphaKernel.Premultiply(_color, _alpha);

    [Benchmark]
    public void Unpremultiply() => AlphaKernel.Unpremultiply(_color, _alpha);
}

[MemoryDiagnoser]
public class RgbeKernelBenchmarks
{
    private const int k_Pixels = 1 << 20;

    private byte[] _rgbe = [];
    private float[] _red = [];
    private float[] _green = [];
    private float[] _blue = [];

    [GlobalSetup]
    public void Setup()
    {
        _rgbe = BenchmarkImages.Rgbe(k_Pixels);
        _red = BenchmarkImages.HighDynamicRange(k_Pixels, 2);
        _green = BenchmarkImages.HighDynamicRange(k_Pixels, 3);
        _blue = BenchmarkImages.HighDynamicRange(k_Pixels, 4);
    }

    [Benchmark]
    public void Decode() => RgbeConversionKernel.Decode(_rgbe, _red, _green, _blue);

    [Benchmark]
    public void Encode() => RgbeConversionKernel.Encode(_red, _green, _blue, _rgbe);
}
