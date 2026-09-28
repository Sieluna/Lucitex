using BenchmarkDotNet.Running;
using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Data;

namespace Lucitex.Benchmarks.Codecs;

internal enum Library { Lucitex, ImageSharp, SkiaSharp, NetVips, MagickNet }
internal enum Operation { Encode, Decode, Convert }

internal sealed record ComparisonCase(string Image, string Profile, Library Library, Operation Operation, string? SourceFormat = null, int EncoderQuality = 90, string Candidate = "default")
{
    public string Format => FormatCatalog.ForProfile(Profile).Id;
    public string InputFormat => Operation == Operation.Convert ? SourceFormat ?? throw new InvalidOperationException("A conversion requires an explicit source format.") : Format;
    public string Key => $"{Profile}/{Image}/{Operation}/{Library}" + (Operation == Operation.Convert ? $"/from-{InputFormat}" : "")
        + (Candidate == "default" ? "" : $"/candidate-{Candidate}");
    public EncoderCandidate Settings => EncoderCandidates.Resolve(this);
    public string GroupKey => $"{Profile}/{Image}/{Operation}/from-{InputFormat}";
    public bool RequiresPairing => Format == "jpeg" && Operation != Operation.Decode;
    public bool RateMatched => Profile == "jpeg-rate420";
    public Lucitex.Webp.WebpCompressionEffort WebpEffort => Settings.WebpEffort ?? (Profile switch {
        "webp-lossless-fast" => Lucitex.Webp.WebpCompressionEffort.Fast,
        "webp-lossless-best" => Lucitex.Webp.WebpCompressionEffort.Best,
        _ => Lucitex.Webp.WebpCompressionEffort.Balanced,
    });

    public string EncoderSettings(int quality) => Format switch {
        "jpeg" => $"Q{quality}; baseline 4:2:0; " + (Library == Library.Lucitex ? $"optimize Huffman={Settings.OptimizeHuffman}" : "library-specific entropy coding"),
        "png" => $"{Profile}; " + (Library == Library.Lucitex
            ? $"{Settings.PngCompression ?? System.IO.Compression.CompressionLevel.SmallestSize}; adaptive SmallestSize may evaluate multiple complete encodings"
            : Settings.PngLevel is { } level ? $"compression {level}" : "library-default compression effort"),
        "webp" => Library switch {
            Library.Lucitex => $"Lossless; {WebpEffort}",
            Library.SkiaSharp => $"Lossless; quality/effort {Settings.WebpQuality}; native method 0 (SkiaSharp 3.119.1)",
            _ => $"Lossless; method {Settings.WebpMethod}; quality {Settings.WebpQuality}; exact transparent RGB",
        },
        _ => Profile,
    };

    public string? UnsupportedReason()
    {
        if (!FormatCatalog.Profiles.Contains(Profile)) {
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
        if (Library == Library.SkiaSharp && Format == "webp" && InputFormat != "jpeg" && HasHiddenColor()) {
            return "SkiaSharp's WebP encoder does not expose exact transparent RGB preservation; this input contains nonzero RGB under zero alpha. Decoding remains comparable.";
        }
        return Library switch {
            Library.SkiaSharp when Profile is not ("jpeg-quality420" or "jpeg-rate420" or "png-default" or "webp-lossless" or "webp-lossless-fast" or "webp-lossless-best") => "SkiaSharp does not expose this encoding policy.",
            Library.MagickNet when Profile is "png-none" or "png-paeth" => "Magick.NET 14.17.1's PNG path does not honor the strict per-row fixed-filter contract in verification; use png-default.",
            Library.NetVips when Profile == "png-paeth" && TestImage.DeclaredDimensions(Image) is { } size && (size.Width == 1 || size.Height == 1)
                => "libpng disables Paeth for single-row/column inputs; this is outside the strict fixed-filter contract.",
            _ => null,
        };
    }

    private bool HasHiddenColor()
    {
        if (!Image.StartsWith("external:", StringComparison.Ordinal)) {
            return Image.StartsWith("alpha@", StringComparison.Ordinal);
        }
        var pixels = TestImage.Create(Image, "webp").Pixels;
        for (var i = 0; i < pixels.Length; i += 4) {
            if (pixels[i + 3] == 0 && (pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0) {
                return true;
            }
        }
        return false;
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
            route?.Source, Candidate: benchmark.Parameters.Items.Any(p => p.Name == "Candidate") ? (string)benchmark.Parameters["Candidate"] : "default");
    }
}
