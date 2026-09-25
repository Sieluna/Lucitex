using BenchmarkDotNet.Attributes;
using Lucitex.Benchmarks.Data;
using Lucitex.Compression;
using Lucitex.Conversion;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Dds;

namespace Lucitex.Benchmarks.Suites;

[BenchmarkCategory("Core.Execution")]
public class CoreExecutionBenchmarks
{
    [ParamsSource(nameof(Cases))]
    public string Case { get; set; } = "noise@2048x2048";

    [Params("BC1.Encode", "BC1.Decode", "DDS.Resize")]
    public string Operation { get; set; } = "DDS.Resize";

    public IEnumerable<string> Cases => RunOptions.Current.Cases;

    private TestImage _image = null!;
    private byte[] _bcEncoded = [];
    private byte[] _bcEncodeSyncOutput = [];
    private byte[] _bcEncodeAsyncOutput = [];
    private byte[] _bcDecodeSyncOutput = [];
    private byte[] _bcDecodeAsyncOutput = [];
    private byte[] _ddsEncoded = [];

    [GlobalSetup]
    public async Task Setup()
    {
        _image = TestImage.Create(Case, "png");
        var bcBytes = BcImageCodec.EncodedByteCount(BcFormat.Bc1, _image.Width, _image.Height);
        _bcEncoded = new byte[bcBytes];
        _bcEncodeSyncOutput = new byte[bcBytes];
        _bcEncodeAsyncOutput = new byte[bcBytes];
        _bcDecodeSyncOutput = new byte[_image.Pixels.Length];
        _bcDecodeAsyncOutput = new byte[_image.Pixels.Length];
        BcImageCodec.Encode(BcFormat.Bc1, _image.Pixels, _image.Width, _image.Height, _bcEncoded);

        using (var stream = new MemoryStream()) {
            var codec = new DdsCodec();
            using (var writer = codec.CreateWriter(stream, ImageLayout.Describe(_image))) {
                writer.Write(ImageLayout.Full(_image), _image.Pixels);
                writer.Finish();
            }
            _ddsEncoded = stream.ToArray();
        }

        var encodedSync = BcEncodeSync().ToArray();
        var encodedAsync = await BcEncodeAsync().ConfigureAwait(false);
        EnsureEquivalent(encodedSync, encodedAsync, "BC1 encoding");
        EnsureEquivalent(BcDecodeSync(), await BcDecodeAsync().ConfigureAwait(false), "BC1 decoding");
        EnsureEquivalent(ConvertDdsSync(), await ConvertDdsAsync().ConfigureAwait(false), "DDS conversion");
    }

    [Benchmark(Baseline = true)]
    public byte[] Sync() => Operation switch {
        "BC1.Encode" => BcEncodeSync(),
        "BC1.Decode" => BcDecodeSync(),
        "DDS.Resize" => ConvertDdsSync(),
        _ => throw new ArgumentOutOfRangeException(nameof(Operation)),
    };

    [Benchmark]
    public Task<byte[]> Async() => Operation switch {
        "BC1.Encode" => BcEncodeAsync(),
        "BC1.Decode" => BcDecodeAsync(),
        "DDS.Resize" => ConvertDdsAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(Operation)),
    };

    private byte[] BcEncodeSync()
    {
        BcImageCodec.Encode(BcFormat.Bc1, _image.Pixels, _image.Width, _image.Height, _bcEncodeSyncOutput);
        return _bcEncodeSyncOutput;
    }

    private async Task<byte[]> BcEncodeAsync()
    {
        await BcImageCodec.EncodeAsync(BcFormat.Bc1, _image.Pixels, _image.Width, _image.Height, _bcEncodeAsyncOutput).ConfigureAwait(false);
        return _bcEncodeAsyncOutput;
    }

    private byte[] BcDecodeSync()
    {
        BcImageCodec.Decode(BcFormat.Bc1, _bcEncoded, _image.Width, _image.Height, _bcDecodeSyncOutput);
        return _bcDecodeSyncOutput;
    }

    private async Task<byte[]> BcDecodeAsync()
    {
        await BcImageCodec.DecodeAsync(BcFormat.Bc1, _bcEncoded, _image.Width, _image.Height, _bcDecodeAsyncOutput).ConfigureAwait(false);
        return _bcDecodeAsyncOutput;
    }

    private byte[] ConvertDdsSync()
    {
        var codec = new DdsCodec();
        using var input = new MemoryStream(_ddsEncoded, writable: false);
        using var reader = codec.OpenReader(input);
        var plan = ConversionPlanner.Plan(reader.Describe(), codec.Capabilities, ConversionPolicy.Preview,
            targetExtent: (Math.Max(1, _image.Width / 2), Math.Max(1, _image.Height / 2))).Plan!;
        using var output = new MemoryStream();
        using var writer = codec.CreateWriter(output, plan.TargetDescriptor);
        ConversionExecutor.Execute(plan, reader, codec.Capabilities.SampleByteOrder, writer, codec.Capabilities.SampleByteOrder);
        return output.ToArray();
    }

    private async Task<byte[]> ConvertDdsAsync()
    {
        var codec = new DdsCodec();
        using var input = new MemoryStream(_ddsEncoded, writable: false);
        await using var reader = await codec.OpenReaderAsync(input).ConfigureAwait(false);
        var plan = ConversionPlanner.Plan(reader.Describe(), codec.Capabilities, ConversionPolicy.Preview,
            targetExtent: (Math.Max(1, _image.Width / 2), Math.Max(1, _image.Height / 2))).Plan!;
        using var output = new MemoryStream();
        await using var writer = codec.CreateAsyncWriter(output, plan.TargetDescriptor);
        await ConversionExecutor.ExecuteAsync(plan, reader, codec.Capabilities.SampleByteOrder, writer, codec.Capabilities.SampleByteOrder).ConfigureAwait(false);
        return output.ToArray();
    }

    private static void EnsureEquivalent(byte[] expected, byte[] actual, string operation)
    {
        if (!expected.AsSpan().SequenceEqual(actual)) {
            throw new InvalidDataException($"Sync and async {operation} results differ.");
        }
    }
}
