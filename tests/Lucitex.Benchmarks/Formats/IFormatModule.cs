using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Formats;

internal interface IFormatModule
{
    string Id { get; }
    int Channels { get; }
    bool Lossless { get; }
    string DefaultProfile { get; }
    IReadOnlyList<string> Profiles { get; }
    IReadOnlyList<Type> BenchmarkTypes { get; }
    TestImage CreateImage(string id) => TestImage.Create(id, Id);
    QualityMetrics CompareQuality(TestImage layout, byte[] expected, byte[] actual) => QualityMetrics.Compare(expected, actual);
    double ReferenceMseTolerance => Lossless ? 0 : 4;
    Library? ReferenceLibrary => Library.ImageSharp;
    IReadOnlyList<Library> IndependentDecoders => [Library.ImageSharp, Library.NetVips, Library.MagickNet];
    string ReferenceName => ReferenceLibrary?.ToString() ?? "Independent format reference";
    void DecodeReference(TestImage layout, byte[] encoded, byte[] destination) =>
        CodecAdapter.Create(ReferenceLibrary!.Value).Decode(encoded, layout, destination);
    void ValidateDimensions(TestImage layout, byte[] encoded)
    {
        var info = SixLabors.ImageSharp.Image.Identify(encoded);
        if (info is null || info.Width != layout.Width || info.Height != layout.Height) {
            throw new InvalidDataException("Encoded dimensions differ from source.");
        }
    }
    byte[] CreateFixture(TestImage source, ComparisonCase comparison);
    object? ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference);
}

internal sealed record ConversionRoute(string Source, string Destination, Type BenchmarkType);
