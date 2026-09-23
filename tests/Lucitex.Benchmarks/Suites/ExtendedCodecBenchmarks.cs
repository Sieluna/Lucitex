using BenchmarkDotNet.Attributes;

namespace Lucitex.Benchmarks.Suites;

[BenchmarkCategory("EXR.Encode")]
public class ExrEncodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "exr";
    protected override string OperationName => "Encode";
}

[BenchmarkCategory("EXR.Decode")]
public class ExrDecodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "exr";
    protected override string OperationName => "Decode";
}

[BenchmarkCategory("KTX2.Encode")]
public class Ktx2EncodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "ktx2";
    protected override string OperationName => "Encode";
}

[BenchmarkCategory("KTX2.Decode")]
public class Ktx2DecodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "ktx2";
    protected override string OperationName => "Decode";
}

[BenchmarkCategory("Convert.PNG-to-EXR")]
public class PngToExrBenchmarks : CodecBenchmarks
{
    protected override string Format => "exr";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.PNG-to-KTX2")]
public class PngToKtx2Benchmarks : CodecBenchmarks
{
    protected override string Format => "ktx2";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.JPEG-to-EXR")]
public class JpegToExrBenchmarks : CodecBenchmarks
{
    protected override string Format => "exr";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.JPEG-to-KTX2")]
public class JpegToKtx2Benchmarks : CodecBenchmarks
{
    protected override string Format => "ktx2";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.EXR-to-PNG")]
public class ExrToPngBenchmarks : CodecBenchmarks
{
    protected override string Format => "png";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.EXR-to-JPEG")]
public class ExrToJpegBenchmarks : CodecBenchmarks
{
    protected override string Format => "jpeg";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.EXR-to-KTX2")]
public class ExrToKtx2Benchmarks : CodecBenchmarks
{
    protected override string Format => "ktx2";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.KTX2-to-PNG")]
public class Ktx2ToPngBenchmarks : CodecBenchmarks
{
    protected override string Format => "png";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.KTX2-to-JPEG")]
public class Ktx2ToJpegBenchmarks : CodecBenchmarks
{
    protected override string Format => "jpeg";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.KTX2-to-EXR")]
public class Ktx2ToExrBenchmarks : CodecBenchmarks
{
    protected override string Format => "exr";
    protected override string OperationName => "Convert";
}
