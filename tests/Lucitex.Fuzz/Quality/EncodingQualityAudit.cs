using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
    WebpCompressionEffort Effort = WebpCompressionEffort.Balanced, string Corpus = "unspecified")
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
                Check(new(format, pattern, 256, 256, format == "jpeg" ? 90 : 75, random.Next(), Corpus: "fixed"));
            }
        }
        for (var i = 0; i < options.QualityIterations; i++) {
            var width = i % 8 == 0 ? 1 : random.Next(2, 258);
            var height = i % 8 == 1 ? 1 : random.Next(2, 258);
            var pattern = s_Patterns[random.Next(s_Patterns.Length)];
            var pixelSeed = random.Next();
            foreach (var format in new[] { "png", "jpeg", "webp" }) {
                Check(new(format, pattern, width, height, random.Next(format == "jpeg" ? 1 : 0, 101), pixelSeed, Corpus: "random"));
            }
        }
        var directory = Path.Combine(options.Artifacts, "quality");
        Directory.CreateDirectory(directory);
        WriteReport(options, results, directory);
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

    private static void WriteReport(FuzzOptions options, List<EncodingAuditResult> results, string directory)
    {
        const string reference = "libpng simplified RGBA8 writer; libjpeg RGB8 4:2:0 optimized Huffman/ISLOW same Q, nearest chroma decode; libwebp lossless method4 Q100 exact";
        const string contract = "PNG RGBA8 and WebP RGBA8 must be lossless, including hidden RGB. JPEG uses the same Q and 4:2:0, not matched fidelity: Lucitex MSE must be <= reference MSE * 1.25 + 2. Lucitex size must be <= 1.2x reference, except WebP Fast (size advisory only). Every effort requires fidelity, unchanged input, ignored lossless Quality and no size regression at higher effort.";
        const string methodology = "Procedural fixed and seeded random inputs, not a representative image benchmark or a speed measurement. Each pair uses the same source pixels. Compression = raw pixel bytes / complete encoded file bytes (higher is better); gap = (Lucitex / reference - 1) * 100% (lower is better). Aggregate ratios use byte sums, including failed cases with metrics. Missing metrics are counted separately, never treated as passes. Median and nearest-rank P95 use individual size ratios. WebP efforts reuse sources and are reported separately; no pooled cross-format score. EXR/HDR/KTX2 retain decoder-fuzz coverage only in this report.";
        var nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.Oracle!)));
        var groups = results.GroupBy(r => (r.Case.Format, Effort: r.Case.Format == "webp" ? r.Case.Effort.ToString() : "default", r.Case.Corpus))
            .Select(group => {
                var measured = group.Where(r => r.Compression is not null).Select(r => r.Compression!).ToArray();
                var ratios = measured.Select(m => m.SizeRatio).Order().ToArray();
                var raw = measured.Sum(m => m.RawBytes);
                var actual = measured.Sum(m => m.EncodedBytes);
                var baseline = measured.Sum(m => m.ReferenceBytes);
                return new {
                    group.Key.Format, group.Key.Effort, group.Key.Corpus, Cases = group.Count(), Measured = measured.Length,
                    Passed = group.Count(r => r.Error is null), Failed = group.Count(r => r.Error is not null),
                    MissingMetrics = group.Count(r => r.Compression is null),
                    RawBytes = raw, EncodedBytes = actual, ReferenceBytes = baseline,
                    LucitexCompression = Divide(raw, actual), ReferenceCompression = Divide(raw, baseline),
                    SizeRatio = Divide(actual, baseline), GapPercent = (Divide(actual, baseline) - 1) * 100,
                    MedianSizeRatio = ratios.Length == 0 ? (double?)null : (ratios[(ratios.Length - 1) / 2] + ratios[ratios.Length / 2]) / 2,
                    P95SizeRatio = ratios.Length == 0 ? (double?)null : ratios[(int)Math.Ceiling(ratios.Length * .95) - 1],
                    WorstSizeRatio = ratios.Length == 0 ? (double?)null : ratios[^1],
                    AtOrBelowReference = measured.Count(m => m.SizeRatio <= 1),
                    SizeFailures = measured.Count(m => m.EnforceSize && !m.SizePassed),
                    SizeAdvisories = measured.Count(m => !m.EnforceSize && !m.SizePassed),
                    FidelityFailures = measured.Count(m => !m.FidelityPassed),
                };
            }).ToArray();
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new {
            SchemaVersion = 2, Options = options, MaxSizeRatio = CompressionQuality.SizeLimit,
            Reference = reference, NativeAbi = NativeAbi.Version, NativeSha256 = nativeSha256,
            Contract = contract, Methodology = methodology, Groups = groups, Results = results,
        }, s_Json));

        var failures = results.Count(r => r.Error is not null);
        var missing = results.Count(r => r.Compression is null);
        var status = $"{results.Count} checks · {results.Count - failures} passed · {failures} failed · {missing} missing metrics · seed {options.RandomSeed}";
        var markdown = new StringBuilder();
        markdown.AppendLine("| Format · corpus | Measured | Compression L / ref ↑ | Size gap ↓ | Worst L/ref ↓ | > 1.2× | Result |");
        markdown.AppendLine("|:---|---:|---:|---:|---:|---:|:---|");
        var html = new StringBuilder("""
            <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Fuzz compression audit</title><style>
            body{font:15px system-ui,sans-serif;margin:32px;color:#172536;background:#f6f8fb}h1{margin-bottom:8px}
            p{max-width:1100px;line-height:1.6}table{border-collapse:collapse;background:white;width:100%;font-size:13px}
            th,td{padding:10px;border:1px solid #dae1ea;text-align:right;vertical-align:top}th{background:#e9eff6}
            th:first-child,td:first-child{text-align:left}.scroll{overflow:auto;margin:20px 0}small{color:#526274}
            .fail{background:#fff0ee}.advisory{background:#fff9e6}a{color:#1657a0}code{overflow-wrap:anywhere}
            details{max-width:540px;text-align:left}pre{white-space:pre-wrap;overflow-wrap:anywhere}summary{cursor:pointer}
            </style><h1>Fuzz compression audit</h1>
            """);
        html.Append($"<p><strong>{H(status)}</strong></p><p><a href='summary.json'>JSON + provenance</a> · <a href='cases.csv'>All cases (CSV)</a></p>");
        html.Append($"<p>{H(methodology)}</p><p>{H(contract)}</p><h2>Compression by format, effort and corpus</h2>");
        html.Append("<p>L = Lucitex. Oversize counts use the per-case 1.2× limit; passing an aggregate does not override a failing case. Empty values mean unavailable.</p>");
        html.Append("<div class='scroll'><table><tr><th>Format / effort / corpus</th><th>Measured / total</th><th>Failed</th><th>Raw B</th><th>L B / ref B</th><th>Compression L / ref ↑</th><th>Gap % ↓</th><th>Median / P95 / worst L/ref ↓</th><th>≤ reference</th><th>Size fail / advisory</th><th>Fidelity fail</th></tr>");
        foreach (var group in groups) {
            var name = $"{group.Format} / {group.Effort} / {group.Corpus}";
            var label = group.Format == "webp" ? $"WebP {group.Effort}" : group.Format.ToUpperInvariant();
            var oversize = group.SizeAdvisories > 0 ? $"{group.SizeAdvisories} advisory" : group.SizeFailures.ToString(CultureInfo.InvariantCulture);
            var outcome = group.Failed > 0 ? $"**FAIL ({group.Failed})**" : "PASS";
            markdown.AppendLine($"| {label} · {group.Corpus} | {group.Measured}/{group.Cases} | {N(group.LucitexCompression, "F2")}× / {N(group.ReferenceCompression, "F2")}× | {N(group.GapPercent, "+0.00;-0.00;0.00")}% | {N(group.WorstSizeRatio, "F3")}× | {oversize} | {outcome} |");
            html.Append($"<tr class='{(group.Failed > 0 ? "fail" : group.SizeAdvisories > 0 ? "advisory" : "")}'><td>{H(name)}</td><td>{group.Measured}/{group.Cases}</td><td>{group.Failed}</td><td>{group.RawBytes}</td><td>{group.EncodedBytes} / {group.ReferenceBytes}</td><td>{N(group.LucitexCompression)}× / {N(group.ReferenceCompression)}×</td><td>{N(group.GapPercent)}%</td><td>{N(group.MedianSizeRatio)} / {N(group.P95SizeRatio)} / {N(group.WorstSizeRatio)}</td><td>{group.AtOrBelowReference}</td><td>{group.SizeFailures} / {group.SizeAdvisories}</td><td>{group.FidelityFailures}</td></tr>");
        }
        html.Append("</table></div><h2>Every case</h2><p>Failures first, then largest size ratio. MSE is averaged over 8-bit RGB (JPEG) or RGBA (lossless) samples. PSNR uses peak 255; exact means MSE = 0. JPEG size and distortion must be read together. Source hash, pixel seed and replay artifacts are included in each row.</p>");
        html.Append("<div class='scroll'><table><tr><th>Case / source</th><th>Result</th><th>Raw B</th><th>L B / ref B</th><th>Compression L / ref ↑</th><th>Gap % ↓</th><th>L/ref ↓</th><th>Size</th><th>Fidelity</th><th>MSE L / ref ↓</th><th>PSNR dB L / ref ↑</th></tr>");
        var csv = new StringBuilder("Format,Effort,Corpus,Pattern,Width,Height,Quality,PixelSeed,SourceSha256,ReferenceEncoder,RawBytes,LucitexBytes,ReferenceBytes,LucitexCompression,ReferenceCompression,GapPercent,SizeRatio,MaxSizeRatio,EnforceSize,SizePassed,FidelityPassed,LucitexMse,ReferenceMse,LucitexPsnrDb,ReferencePsnrDb,Result,Error,Artifact\n");
        foreach (var result in results.OrderByDescending(r => r.Error is not null).ThenByDescending(r => r.Compression?.SizeRatio)) {
            var sample = result.Case;
            var metrics = result.Compression;
            var effort = sample.Format == "webp" ? sample.Effort.ToString() : "default";
            var quality = sample.Format == "jpeg" ? sample.Quality.ToString(CultureInfo.InvariantCulture) : "ignored (lossless)";
            var name = $"{sample.Format} / {effort} / {sample.Corpus} / {sample.Pattern} / {sample.Width}×{sample.Height} / Q {quality}";
            var compression = metrics?.RawToEncodedRatio;
            var refCompression = metrics is null ? null : Divide(metrics.RawBytes, metrics.ReferenceBytes);
            var gap = (metrics?.SizeRatio - 1) * 100;
            var size = metrics is null ? "unavailable" : metrics.SizePassed ? "pass" : metrics.EnforceSize ? "FAIL" : "advisory";
            var fidelity = metrics is null ? "unavailable" : metrics.FidelityPassed ? "pass" : "FAIL";
            var outcome = result.Error is null ? "PASS" : "FAIL";
            var artifact = result.Artifact is null ? null : Path.GetFileName(result.Artifact);
            html.Append($"<tr class='{(result.Error is not null ? "fail" : size == "advisory" ? "advisory" : "")}'><td>{H(name)}<details><summary>Source / diagnostics</summary><p>Pixel seed: {sample.PixelSeed}<br>SHA256: <code>{H(result.SourceSha256)}</code></p>");
            if (artifact is not null) html.Append($"<a href='{H(Uri.EscapeDataString(artifact))}'>Replay JSON</a> · <a href='{H(Uri.EscapeDataString(Path.ChangeExtension(artifact, ".pixels")))}'>Source pixels</a>");
            if (result.Error is not null) html.Append($"<pre>{H(result.Error)}</pre>");
            html.Append($"</details></td><td>{outcome}</td><td>{metrics?.RawBytes}</td><td>{metrics?.EncodedBytes} / {metrics?.ReferenceBytes}</td><td>{N(compression)}× / {N(refCompression)}×</td><td>{N(gap)}%</td><td>{N(metrics?.SizeRatio)}</td><td>{size}</td><td>{fidelity}</td><td>{N(metrics?.SourceMse)} / {N(metrics?.ReferenceMse)}</td><td>{Psnr(metrics?.SourceMse)} / {Psnr(metrics?.ReferenceMse)}</td></tr>");
            object?[] cells = [sample.Format, effort, sample.Corpus, sample.Pattern, sample.Width, sample.Height, quality,
                sample.PixelSeed, result.SourceSha256, metrics?.ReferenceEncoder, metrics?.RawBytes, metrics?.EncodedBytes,
                metrics?.ReferenceBytes, compression, refCompression, gap, metrics?.SizeRatio, metrics?.MaxSizeRatio,
                metrics?.EnforceSize, metrics?.SizePassed, metrics?.FidelityPassed, metrics?.SourceMse, metrics?.ReferenceMse,
                Psnr(metrics?.SourceMse), Psnr(metrics?.ReferenceMse), outcome, result.Error, artifact];
            csv.AppendLine(string.Join(",", cells.Select(c => "\"" + (Convert.ToString(c, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"")));
        }
        markdown.AppendLine();
        markdown.AppendLine("| Audit | Details |");
        markdown.AppendLine("|:---|:---|");
        markdown.AppendLine($"| Checks | {results.Count - failures}/{results.Count} passed · {failures} failed · {missing} missing metrics |");
        markdown.AppendLine("| Ratios | L = Lucitex; ref = native encoder. Compression = Σ raw / Σ encoded; gap = Σ L / Σ ref − 1. Failed cases included. |");
        markdown.AppendLine("| Size gate | Per case: L/ref ≤ 1.2×; WebP Fast advisory. |");
        markdown.AppendLine("| Fidelity gate | PNG/WebP: exact. JPEG: same Q, L MSE ≤ ref MSE × 1.25 + 2. |");
        markdown.AppendLine("| Scope / artifacts | Synthetic corpus; no speed measurement. Full data, references and failures: `quality/index.html`, `cases.csv`, `summary.json` in uploaded artifacts. |");
        html.Append($"</table></div><h2>Reference provenance</h2><p>{H(reference)}</p><p>Native ABI {NativeAbi.Version}; SHA256 <code>{nativeSha256}</code>. Seed {options.RandomSeed}; random rounds {options.QualityIterations}. Full options and raw measurements are in <a href='summary.json'>summary.json</a>.</p></html>");
        File.WriteAllText(Path.Combine(directory, "index.html"), html.ToString());
        File.WriteAllText(Path.Combine(directory, "cases.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(directory, "summary.md"), markdown.ToString());

        static double? Divide(long numerator, long denominator) => denominator > 0 ? (double)numerator / denominator : null;
        static string N(double? value, string format = "F4") => value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";
        static string H(string value) => WebUtility.HtmlEncode(value);
        static string Psnr(double? mse) => mse is null ? "" : mse == 0 ? "exact" : N(10 * Math.Log10(255 * 255 / mse.Value));
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
