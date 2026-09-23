using BenchmarkDotNet.Running;
using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Data;

namespace Lucitex.Benchmarks.Codecs;

internal enum Library { Lucitex, ImageSharp, SkiaSharp, NetVips, MagickNet }
internal enum Operation { Encode, Decode, Convert }

internal sealed record ComparisonCase(string Image, string Profile, Library Library, Operation Operation, string? SourceFormat = null)
{
    public string Format => FormatCatalog.ForProfile(Profile).Id;
    public string InputFormat => Operation == Operation.Convert ? SourceFormat ?? throw new InvalidOperationException("A conversion requires an explicit source format.") : Format;
    public string Key => $"{Profile}/{Image}/{Operation}/{Library}" + (Operation == Operation.Convert ? $"/from-{InputFormat}" : "");
    public bool Is444 => Profile == "jpeg-standard444";
    public bool Progressive => Profile == "jpeg-progressive420";
    public bool OptimizeHuffman => Profile is "jpeg-optimized420" or "jpeg-progressive420"
        || (Profile == "jpeg-default420" && Library == Library.Lucitex);
    public const int Quality = 90;
    public static IEnumerable<string> Profiles => FormatCatalog.Profiles;

    public string? UnsupportedReason()
    {
        if (!Profiles.Contains(Profile)) {
            return $"Unknown profile {Profile}.";
        }
        if (!CodecCapabilities.Supports(Library, Format) || !CodecCapabilities.Supports(Library, InputFormat)) {
            if (Library == Library.NetVips && (Format == "exr" || InputFormat == "exr") && Format != "ktx2" && InputFormat != "ktx2") {
                return "NetVips exposes filename-based EXR loading, no EXR buffer loader/saver; excluded from this in-memory conversion contract.";
            }
            return $"{Library} adapter does not implement the requested format route.";
        }
        if (Operation == Operation.Decode) {
            return null;
        }
        return Library switch {
            Library.ImageSharp when Progressive || Profile == "jpeg-optimized420" => "ImageSharp 3.1.11 does not expose this encoding policy.",
            Library.SkiaSharp when Profile is not ("jpeg-default420" or "png-default") => "SkiaSharp does not expose this encoding policy.",
            Library.MagickNet when Profile is "png-none" or "png-paeth" => "Magick.NET 14.17.1's PNG path does not honor the strict per-row fixed-filter contract in verification; use png-default.",
            Library.NetVips when Profile == "png-paeth" && TestImage.DeclaredDimensions(Image) is { } size && (size.Width == 1 || size.Height == 1)
                => "libpng disables Paeth for single-row/column inputs; this is outside the strict fixed-filter contract.",
            _ => null,
        };
    }

    public static ComparisonCase? From(BenchmarkCase benchmark)
    {
        if (!benchmark.Parameters.Items.Any(p => p.Name == "Profile")
            || !Enum.TryParse<Library>(benchmark.Descriptor.WorkloadMethod.Name, out var library)) {
            return null;
        }
        var route = FormatCatalog.Conversions.SingleOrDefault(r => r.BenchmarkType == benchmark.Descriptor.Type);
        return new ComparisonCase((string)benchmark.Parameters["Case"], (string)benchmark.Parameters["Profile"], library,
            route is not null ? Operation.Convert
                : benchmark.Descriptor.Type.Name.Contains("Encode", StringComparison.Ordinal) ? Operation.Encode : Operation.Decode,
            route?.Source);
    }
}
