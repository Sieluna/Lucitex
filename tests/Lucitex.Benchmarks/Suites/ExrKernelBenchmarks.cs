using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Exr.Compression;

namespace Lucitex.Benchmarks.Suites;

[MemoryDiagnoser]
public class ExrKernelBenchmarks
{
    private const int k_Bytes = 1 << 20;

    private byte[] _source = [];
    private byte[] _work = [];
    private byte[] _scratch = [];
    private ushort[] _halfSource = [];
    private ushort[] _halfWork = [];

    private byte[] _rleCompressed = [];
    private byte[] _zipCompressed = [];
    private byte[] _huffmanCompressed = [];

    [GlobalSetup]
    public void Setup()
    {
        _source = BenchmarkImages.HalfBytes(k_Bytes / sizeof(ushort));
        _work = new byte[k_Bytes];
        _scratch = new byte[k_Bytes];
        _halfSource = BenchmarkImages.HalfBits(k_Bytes / sizeof(ushort));
        _halfWork = new ushort[_halfSource.Length];
        _rleCompressed = ExrRle.Compress(_source);
        _zipCompressed = ExrZip.Compress(_source);
        _huffmanCompressed = ExrHuffman.Compress(_halfSource);
    }

    [IterationSetup(Targets = [nameof(PredictorApply), nameof(PredictorRemove)])]
    public void ResetBytes() => _source.CopyTo(_work, 0);

    [IterationSetup(Targets = [nameof(WaveletEncode), nameof(WaveletDecode)])]
    public void ResetHalves() => _halfSource.CopyTo(_halfWork, 0);

    [Benchmark]
    public void ReorderSplit() => ByteReorder.Split(_source, _work);

    [Benchmark]
    public void ReorderInterleave() => ByteReorder.Interleave(_source, _work);

    [Benchmark]
    public void PredictorApply() => BytePredictor.Apply(_work);

    [Benchmark]
    public void PredictorRemove() => BytePredictor.Remove(_work);

    [Benchmark]
    public void WaveletEncode() => ExrWavelet.Encode(_halfWork, 0, 1024, 1, 512, 1024, ushort.MaxValue);

    [Benchmark]
    public void WaveletDecode() => ExrWavelet.Decode(_halfWork, 0, 1024, 1, 512, 1024, ushort.MaxValue);

    [Benchmark]
    public byte[] HuffmanCompress() => ExrHuffman.Compress(_halfSource);

    [Benchmark]
    public byte[] RleCompress() => ExrRle.Compress(_source);

    [Benchmark]
    public int RleDecompress() => ExrRle.Decompress(_rleCompressed, _scratch);

    [Benchmark]
    public byte[] ZipCompress() => ExrZip.Compress(_source);

    [Benchmark]
    public void ZipDecompress() => ExrZip.Decompress(_zipCompressed, _scratch);

    [Benchmark]
    public void HuffmanUncompress() => ExrHuffman.Uncompress(_huffmanCompressed, _halfWork);
}
