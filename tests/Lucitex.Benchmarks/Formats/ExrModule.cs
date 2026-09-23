using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Suites;
using Lucitex.Benchmarks.Validation;

namespace Lucitex.Benchmarks.Formats;

internal sealed class ExrModule : IFormatModule
{
    public string Id => "exr";
    public int Channels => 4;
    public bool Lossless => true;
    public string DefaultProfile => "exr-half-zip";
    public IReadOnlyList<string> Profiles { get; } = ["exr-half-none", "exr-half-zip", "exr-half-piz"];
    public IReadOnlyList<Type> BenchmarkTypes { get; } = [typeof(ExrEncodeBenchmarks), typeof(ExrDecodeBenchmarks)];
    public Library? ReferenceLibrary => Library.MagickNet;
    public IReadOnlyList<Library> IndependentDecoders { get; } = [Library.Lucitex, Library.MagickNet];
    public byte[] CreateFixture(TestImage source, ComparisonCase comparison) => new MagickNetAdapter().Encode(source, comparison);
    public void DecodeReference(TestImage layout, byte[] encoded, byte[] destination)
    {
        try {
            new MagickNetAdapter().Decode(encoded, layout, destination);
        }
        catch (ImageMagick.MagickException exception) {
            throw new InvalidDataException("Magick.NET EXR reference decoder failed; this case has no validated timing result.", exception);
        }
    }
    public void ValidateDimensions(TestImage layout, byte[] encoded)
    {
        var header = ExrStructure.Read(encoded);
        if (header.Width != layout.Width || header.Height != layout.Height) {
            throw new InvalidDataException("EXR dimensions differ from source.");
        }
    }

    public object ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference)
    {
        var header = ExrStructure.Read(encoded);
        var compression = comparison.Profile switch { "exr-half-none" => 0, "exr-half-zip" => 3, "exr-half-piz" => 4, _ => -1 };
        if (header.Compression != compression) {
            throw new InvalidDataException($"EXR compression {header.Compression} differs from requested {compression}.");
        }
        return header;
    }
}
