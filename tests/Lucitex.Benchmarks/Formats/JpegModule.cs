using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Suites;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Formats;

internal sealed class JpegModule : IFormatModule
{
    public string Id => "jpeg";
    public int Channels => 3;
    public bool Lossless => false;
    public string DefaultProfile => "jpeg-quality420";
    public IReadOnlyList<string> Profiles { get; } = ["jpeg-quality420", "jpeg-rate420"];
    public IReadOnlyList<Type> BenchmarkTypes { get; } = [typeof(JpegEncodeBenchmarks), typeof(JpegDecodeBenchmarks)];
    public string ReferenceEncoderName(ComparisonCase comparison) => "ImageSharp Q90";

    public byte[] CreateFixture(TestImage source, ComparisonCase comparison) =>
        CodecAdapter.Create(Library.ImageSharp).Encode(source, comparison with { Library = Library.ImageSharp, EncoderQuality = 90 });

    public object ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference)
    {
        var structure = JpegStructure.Read(encoded);
        if (structure.Sampling != "22,11,11" || structure.Progressive) {
            throw new InvalidDataException($"JPEG policy mismatch: {structure.Sampling}, progressive={structure.Progressive}.");
        }
        return structure;
    }
}
