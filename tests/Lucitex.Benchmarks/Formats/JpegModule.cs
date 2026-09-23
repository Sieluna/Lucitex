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
    public string DefaultProfile => "jpeg-default420";
    public IReadOnlyList<string> Profiles { get; } = ["jpeg-default420", "jpeg-standard420", "jpeg-standard444", "jpeg-optimized420", "jpeg-progressive420"];
    public IReadOnlyList<Type> BenchmarkTypes { get; } = [typeof(JpegEncodeBenchmarks), typeof(JpegDecodeBenchmarks)];

    public byte[] CreateFixture(TestImage source, ComparisonCase comparison)
    {
        var library = comparison.Profile is "jpeg-progressive420" or "jpeg-optimized420" ? Library.MagickNet : Library.ImageSharp;
        return CodecAdapter.Create(library).Encode(source, comparison with { Library = library });
    }

    public object ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference)
    {
        var structure = JpegStructure.Read(encoded);
        var expectedSampling = comparison.Is444 ? "11,11,11" : "22,11,11";
        if (structure.Sampling != expectedSampling || structure.Progressive != comparison.Progressive) {
            throw new InvalidDataException($"JPEG policy mismatch: {structure.Sampling}, progressive={structure.Progressive}.");
        }
        if (comparison.Operation != Operation.Decode && (comparison.Profile is "jpeg-standard420" or "jpeg-standard444")
            && structure.HuffmanSha256 != JpegStructure.Read(reference).HuffmanSha256) {
            throw new InvalidDataException("Fixed-Huffman profile emitted different Huffman tables.");
        }
        return structure;
    }
}
