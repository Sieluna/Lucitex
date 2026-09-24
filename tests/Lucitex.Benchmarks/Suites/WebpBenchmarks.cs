using BenchmarkDotNet.Attributes;

namespace Lucitex.Benchmarks.Suites;

[BenchmarkCategory("WebP.Encode")]
public class WebpEncodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "webp";
    protected override string OperationName => "Encode";
}

[BenchmarkCategory("WebP.Decode")]
public class WebpDecodeBenchmarks : CodecBenchmarks
{
    protected override string Format => "webp";
    protected override string OperationName => "Decode";
}

[BenchmarkCategory("Convert.PNG-to-WebP")]
public class PngToWebpBenchmarks : CodecBenchmarks
{
    protected override string Format => "webp";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.WebP-to-PNG")]
public class WebpToPngBenchmarks : CodecBenchmarks
{
    protected override string Format => "png";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.JPEG-to-WebP")]
public class JpegToWebpBenchmarks : CodecBenchmarks
{
    protected override string Format => "webp";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.WebP-to-JPEG")]
public class WebpToJpegBenchmarks : CodecBenchmarks
{
    protected override string Format => "jpeg";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.EXR-to-WebP")]
public class ExrToWebpBenchmarks : CodecBenchmarks
{
    protected override string Format => "webp";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.WebP-to-EXR")]
public class WebpToExrBenchmarks : CodecBenchmarks
{
    protected override string Format => "exr";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.KTX2-to-WebP")]
public class Ktx2ToWebpBenchmarks : CodecBenchmarks
{
    protected override string Format => "webp";
    protected override string OperationName => "Convert";
}

[BenchmarkCategory("Convert.WebP-to-KTX2")]
public class WebpToKtx2Benchmarks : CodecBenchmarks
{
    protected override string Format => "ktx2";
    protected override string OperationName => "Convert";
}
