using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Suites;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Formats;

internal sealed class Ktx2Module : IFormatModule
{
    public string Id => "ktx2";
    public int Channels => 4;
    public bool Lossless => true;
    public string DefaultProfile => "ktx2-rgba8-none";
    public IReadOnlyList<string> Profiles { get; } = ["ktx2-rgba8-none", "ktx2-rgba8-zlib"];
    public IReadOnlyList<Type> BenchmarkTypes { get; } = [typeof(Ktx2EncodeBenchmarks), typeof(Ktx2DecodeBenchmarks)];
    public Library? ReferenceLibrary => null;
    public string ReferenceName => "Independent KTX2 RGBA8 parser";
    public byte[] CreateFixture(TestImage source, ComparisonCase comparison) => Ktx2Reference.Encode(source, comparison.Profile.EndsWith("zlib", StringComparison.Ordinal));
    public void DecodeReference(TestImage layout, byte[] encoded, byte[] destination) => Ktx2Reference.Decode(layout, encoded, destination);
    public void ValidateDimensions(TestImage layout, byte[] encoded)
    {
        var info = Ktx2Reference.Inspect(encoded);
        if (info.Width != layout.Width || info.Height != layout.Height) {
            throw new InvalidDataException("KTX2 dimensions differ from source.");
        }
    }

    public object ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference)
    {
        var info = Ktx2Reference.Inspect(encoded);
        if (info.Supercompression != (comparison.Profile.EndsWith("zlib", StringComparison.Ordinal) ? 3 : 0)) {
            throw new InvalidDataException("KTX2 supercompression differs from the requested policy.");
        }
        return info;
    }
}
