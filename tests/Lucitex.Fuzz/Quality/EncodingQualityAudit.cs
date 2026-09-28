using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Jpeg;
using Lucitex.Png;
using Lucitex.Quality;
using Lucitex.Webp;

namespace Lucitex.Fuzz;

internal sealed record EncodingCase(string Format, string Pattern, int Width, int Height, int Quality, int PixelSeed,
    WebpCompressionEffort Effort = WebpCompressionEffort.Balanced)
{
    public int Channels => Format == "jpeg" ? 3 : 4;
}

internal sealed record EncodingAuditResult(EncodingCase Case, string SourceSha256, CompressionQuality? Compression,
    string? Error, string? Artifact);

internal static class EncodingQualityAudit
{
    private static readonly JsonSerializerOptions s_Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static readonly string[] s_Patterns = ["flat", "ramp", "checker", "alpha", "palette", "stripes", "noise"];

    public static int Run(FuzzOptions options)
    {
        using var oracle = new FfiOracle(options.Oracle ?? throw new ArgumentException("Quality checks require --oracle LIBRARY."));
        oracle.EnsureAvailable();
        var results = new List<EncodingAuditResult>();
        var random = new Random(options.RandomSeed);
        // Guaranteed compressible coverage; random noise alone cannot detect a missing compressor.
        foreach (var pattern in s_Patterns) {
            foreach (var format in new[] { "png", "jpeg", "webp" }) {
                Check(new(format, pattern, 256, 256, format == "jpeg" ? 90 : 75, random.Next()));
            }
        }
        for (var i = 0; i < options.QualityIterations; i++) {
            var width = i % 8 == 0 ? 1 : random.Next(2, 258);
            var height = i % 8 == 1 ? 1 : random.Next(2, 258);
            var pattern = s_Patterns[random.Next(s_Patterns.Length)];
            var pixelSeed = random.Next();
            foreach (var format in new[] { "png", "jpeg", "webp" }) {
                Check(new(format, pattern, width, height, random.Next(format == "jpeg" ? 1 : 0, 101), pixelSeed));
            }
        }
        var directory = Path.Combine(options.Artifacts, "quality");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new {
            SchemaVersion = 1, Options = options, MaxSizeRatio = CompressionQuality.SizeLimit,
            Reference = "libpng simplified RGBA8 writer; libjpeg RGB8 4:2:0 optimized Huffman/ISLOW same Q, nearest chroma decode; libwebp lossless method4 Q100 exact",
            NativeAbi = NativeAbi.Version, NativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.Oracle))),
            Contract = "PNG RGBA8 lossless; JPEG RGB8 4:2:0 same Q (not identical fidelity); WebP RGBA8 lossless including hidden RGB. Native reference encoders and pixel decoders are required. Lucitex size <=1.2x reference; WebP Fast size advisory, fidelity required at every effort. Lossless Quality is ignored; higher effort cannot increase size. EXR/HDR/KTX2 remain decoder-fuzz coverage; benchmark verifies EXR/KTX2 compression separately.",
            Results = results,
        }, s_Json));
        var failures = results.Count(r => r.Error is not null);
        Console.WriteLine($"encoding_quality cases={results.Count} failures={failures} max_size_ratio={CompressionQuality.SizeLimit} report={directory}");
        return failures == 0 ? 0 : 1;

        void Check(EncodingCase sample)
        {
            if (sample.Format == "webp") {
                foreach (var effort in Enum.GetValues<WebpCompressionEffort>()) CheckOne(sample with { Effort = effort });
            }
            else CheckOne(sample);
        }

        void CheckOne(EncodingCase sample)
        {
            var result = CheckCase(oracle, sample, Pixels(sample), directory: Path.Combine(options.Artifacts, "quality"));
            results.Add(result);
            if (result.Error is not null) {
                Console.Error.WriteLine($"QUALITY FAIL {sample}: {result.Error.Split('\n')[0].TrimEnd()} ({result.Artifact})");
            }
        }
    }

    public static int Replay(string path, string oraclePath)
    {
        using var oracle = new FfiOracle(oraclePath);
        oracle.EnsureAvailable();
        var saved = JsonSerializer.Deserialize<EncodingAuditResult>(File.ReadAllText(path), s_Json)
            ?? throw new InvalidDataException("Invalid quality report.");
        if (saved.Case is not { } sample || sample.Format is not ("png" or "jpeg" or "webp")
            || sample.Width <= 0 || sample.Height <= 0 || sample.Quality is < 0 or > 100
            || sample.Format == "jpeg" && sample.Quality == 0 || !Enum.IsDefined(sample.Effort))
            throw new InvalidDataException("Invalid quality replay configuration.");
        var pixels = File.ReadAllBytes(Path.ChangeExtension(path, ".pixels"));
        if (Convert.ToHexString(SHA256.HashData(pixels)) != saved.SourceSha256) {
            throw new InvalidDataException("Quality replay source hash mismatch.");
        }
        var result = CheckCase(oracle, saved.Case, pixels, Path.GetDirectoryName(Path.GetFullPath(path))!);
        Console.WriteLine(JsonSerializer.Serialize(result, s_Json));
        return result.Error is null ? 0 : 1;
    }

    private static EncodingAuditResult CheckCase(FfiOracle oracle, EncodingCase sample, byte[] pixels, string directory)
    {
        byte[]? encoded = null, reference = null;
        CompressionQuality? metrics = null;
        var hash = Convert.ToHexString(SHA256.HashData(pixels));
        var originalPixels = pixels.ToArray();
        try {
            if (pixels.Length != checked(sample.Width * sample.Height * sample.Channels)) {
                throw new InvalidDataException("Source pixel length mismatch.");
            }
            encoded = Encode(sample, pixels);
            if (Convert.ToHexString(SHA256.HashData(pixels)) != hash)
                throw new InvalidDataException("Encoder modified the source pixels.");
            reference = ReadPayload(oracle.EncodeReference(Format(sample), pixels, (uint)sample.Width, (uint)sample.Height, sample.Quality), sample);
            var decoded = Decode(oracle, sample, encoded);
            var referencePixels = Decode(oracle, sample, reference);
            metrics = new(encoded.Length, reference.Length, pixels.Length, sample.Format switch { "png" => "libpng", "jpeg" => "libjpeg optimized 4:2:0", _ => "libwebp method4 Q100 exact" },
                CompressionQuality.SizeLimit, Mse(pixels, decoded), Mse(pixels, referencePixels), sample.Format != "jpeg",
                sample.Format != "webp" || sample.Effort != WebpCompressionEffort.Fast);
            metrics.EnsurePassed();
            if (sample.Format == "webp") {
                if (sample.Effort != WebpCompressionEffort.Best) {
                    var next = sample.Effort == WebpCompressionEffort.Fast ? WebpCompressionEffort.Balanced : WebpCompressionEffort.Best;
                    var high = Encode(sample with { Effort = next }, pixels);
                    if (high.Length > encoded.Length || Mse(pixels, Decode(oracle, sample, high)) != 0)
                        throw new InvalidDataException($"Lossless WebP {next} regressed in size or fidelity against {sample.Effort}.");
                }
                if (!encoded.AsSpan().SequenceEqual(Encode(sample with { Quality = (sample.Quality + 1) % 101 }, pixels)))
                    throw new InvalidDataException("Lossless WebP output changed with the ignored Quality parameter.");
            }
            if (Convert.ToHexString(SHA256.HashData(pixels)) != hash)
                throw new InvalidDataException("Quality verification modified the source pixels.");
            return new(sample, hash, metrics, null, null);
        }
        catch (Exception exception) {
            Directory.CreateDirectory(directory);
            var stem = Path.Combine(directory, $"{sample.Format}-{sample.Width}x{sample.Height}-q{sample.Quality}-{sample.Effort}-{hash}");
            var path = stem + ".json";
            File.WriteAllBytes(stem + ".pixels", originalPixels);
            if (encoded is not null) File.WriteAllBytes(stem + ".actual." + sample.Format, encoded);
            if (reference is not null) File.WriteAllBytes(stem + ".reference." + sample.Format, reference);
            var result = new EncodingAuditResult(sample, hash, metrics, exception.ToString(), path);
            File.WriteAllText(path, JsonSerializer.Serialize(result, s_Json));
            return result;
        }
    }

    private static byte[] Encode(EncodingCase sample, byte[] pixels)
    {
        var names = sample.Channels == 3 ? new[] { "R", "G", "B" } : ["R", "G", "B", "A"];
        var descriptor = SeedCorpus.PngDescriptor(sample.Width, sample.Height, names, SampleType.UNorm8);
        descriptor = descriptor with { Parts = [descriptor.Parts[0] with { Color = new ColorEncoding { Transfer = TransferFunction.Srgb } }] };
        using var stream = new MemoryStream();
        using var writer = sample.Format switch {
            "png" => new PngCodec().CreateWriter(stream, descriptor),
            "jpeg" => new JpegCodec().CreateWriter(stream, descriptor, new JpegEncoderOptions {
                Quality = sample.Quality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, OptimizeHuffmanTables = true }),
            "webp" => new WebpCodec().CreateWriter(stream, descriptor, new WebpEncoderOptions { Quality = sample.Quality, Effort = sample.Effort }),
            _ => throw new ArgumentException("Unknown quality format."),
        };
        writer.Write(new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(sample.Width, sample.Height) }, pixels);
        writer.Finish();
        return stream.ToArray();
    }

    private static ImageFormat Format(EncodingCase sample) => sample.Format switch {
        "png" => ImageFormat.Png, "jpeg" => ImageFormat.Jpeg, "webp" => ImageFormat.Webp,
        _ => throw new ArgumentException("Unknown quality format."),
    };

    private static byte[] ReadPayload(NativePayload payload, EncodingCase sample)
    {
        if (!payload.Outcome.Accepted || payload.Warnings != 0)
            throw new InvalidDataException($"Native quality oracle: {payload.Outcome.Status}: {payload.Outcome.Detail}; warnings={payload.Warnings}.");
        if (payload.Width != sample.Width || payload.Height != sample.Height || payload.Bytes.Length == 0)
            throw new InvalidDataException("Native quality oracle returned an invalid extent or empty output.");
        return payload.Bytes;
    }

    private static byte[] Decode(FfiOracle oracle, EncodingCase sample, byte[] encoded)
    {
        var pixels = ReadPayload(oracle.DecodePixels(Format(sample), encoded), sample);
        if (pixels.Length != checked(sample.Width * sample.Height * sample.Channels))
            throw new InvalidDataException("Native decoder returned an unexpected pixel layout.");
        return pixels;
    }

    private static double Mse(byte[] expected, byte[] actual)
    {
        if (expected.Length == 0 || expected.Length != actual.Length) throw new InvalidDataException("Pixel lengths differ.");
        double squared = 0;
        for (var i = 0; i < expected.Length; i++) {
            var difference = expected[i] - actual[i];
            squared += difference * difference;
        }
        return squared / expected.Length;
    }

    private static byte[] Pixels(EncodingCase sample)
    {
        var random = new Random(sample.PixelSeed);
        var palette = new byte[16 * 3];
        random.NextBytes(palette);
        var pixels = new byte[checked(sample.Width * sample.Height * sample.Channels)];
        for (var y = 0; y < sample.Height; y++) {
            for (var x = 0; x < sample.Width; x++) {
                var p = pixels.AsSpan((y * sample.Width + x) * sample.Channels, sample.Channels);
                var h = (byte)(255L * x / Math.Max(1, sample.Width - 1));
                var v = (byte)(255L * y / Math.Max(1, sample.Height - 1));
                p[0] = h; p[1] = v; p[2] = (byte)((h + v) / 2);
                if (sample.Channels == 4) p[3] = sample.Pattern == "alpha" ? (byte)(x + y) : (byte)255;
                switch (sample.Pattern) {
                    case "flat": p[..3].Fill(127); break;
                    case "checker": p[..3].Fill((byte)(((x ^ y) & 1) * 255)); break;
                    case "noise": random.NextBytes(p); break;
                    case "palette": palette.AsSpan(((x / 8 + y / 8) % 16) * 3, 3).CopyTo(p); break;
                    case "stripes": palette.AsSpan(((x + y) % 16) * 3, 3).CopyTo(p); break;
                }
            }
        }
        return pixels;
    }
}
