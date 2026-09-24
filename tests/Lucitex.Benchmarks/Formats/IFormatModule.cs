using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Formats;

internal interface IFormatModule
{
    public string Id { get; }
    public int Channels { get; }
    public bool Lossless { get; }
    public string DefaultProfile { get; }
    public IReadOnlyList<string> Profiles { get; }
    public IReadOnlyList<Type> BenchmarkTypes { get; }
    public TestImage CreateImage(string id) => TestImage.Create(id, Id);
    public QualityMetrics CompareQuality(TestImage layout, byte[] expected, byte[] actual) => QualityMetrics.Compare(expected, actual);
    public double ReferenceMseTolerance => Lossless ? 0 : 4;
    public Library? ReferenceLibrary => Library.ImageSharp;
    public IReadOnlyList<Library> IndependentDecoders => [Library.ImageSharp, Library.NetVips, Library.MagickNet];
    public string ReferenceName => ReferenceLibrary?.ToString() ?? "Independent format reference";
    public void DecodeReference(TestImage layout, byte[] encoded, byte[] destination) =>
        CodecAdapter.Create(ReferenceLibrary!.Value).Decode(encoded, layout, destination);
    public void ValidateDimensions(TestImage layout, byte[] encoded)
    {
        var info = SixLabors.ImageSharp.Image.Identify(encoded);
        if (info is null || info.Width != layout.Width || info.Height != layout.Height) {
            throw new InvalidDataException("Encoded dimensions differ from source.");
        }
    }
    public byte[] CreateFixture(TestImage source, ComparisonCase comparison);
    public object? ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference);
}

internal sealed record ConversionRoute(string Source, string Destination, Type BenchmarkType);
