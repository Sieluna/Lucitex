using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks.Data;

internal sealed record TestImage(int Width, int Height, int Channels, byte[] Pixels, string Source)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Pixels));

    public static (int Width, int Height)? DeclaredDimensions(string id)
    {
        if (id.StartsWith("external:", StringComparison.Ordinal)) {
            var manifest = RunOptions.Current.DatasetManifest;
            if (manifest is null || !File.Exists(manifest)) {
                return null;
            }
            var entries = JsonSerializer.Deserialize<ExternalImage[]>(File.ReadAllText(manifest));
            var entry = entries?.SingleOrDefault(e => e.Id == id[9..]);
            return entry is null ? null : (entry.Width, entry.Height);
        }
        var parts = id.Split('@');
        var dimensions = parts.Length == 2 ? parts[1].Split('x') : [];
        if (dimensions.Length is < 1 or > 2 || !int.TryParse(dimensions[0], out var width)) {
            return null;
        }
        var height = width;
        return dimensions.Length == 2 && !int.TryParse(dimensions[1], out height) ? null : (width, height);
    }

    public static TestImage Create(string id, string format)
    {
        var channels = FormatCatalog.Get(format).Channels;
        if (id.StartsWith("external:", StringComparison.Ordinal)) {
            return LoadExternal(id[9..], channels);
        }
        var parts = id.Split('@');
        if (parts.Length != 2) {
            throw new ArgumentException("Case IDs use pattern@widthxheight, or external:manifest-id.");
        }
        var size = parts[1].Split('x');
        if (size.Length is < 1 or > 2) {
            throw new ArgumentException("Case dimensions use widthxheight.");
        }
        var width = int.Parse(size[0]);
        var height = size.Length == 1 ? width : int.Parse(size[1]);
        if (width < 1 || height < 1 || width > 8192 || height > 8192) {
            throw new ArgumentOutOfRangeException(nameof(id), "Generated dimensions must be 1..8192.");
        }
        var pixels = new byte[checked(width * height * channels)];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var offset = (y * width + x) * channels;
                var horizontal = (byte)(255L * x / Math.Max(1, width - 1));
                var vertical = (byte)(255L * y / Math.Max(1, height - 1));
                var gray = (byte)(((x ^ y) & 1) * 255);
                var rgb = parts[0] switch {
                    "ramp" => (horizontal, vertical, (byte)((horizontal + vertical) / 2)),
                    "checker" => (gray, gray, gray),
                    "impulse" => x == width / 2 && y == height / 2 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0),
                    "stripes" => ((byte)((x & 1) * 255), (byte)((y & 1) * 255), (byte)(((x + y) & 1) * 255)),
                    "chroma" => (x / 8 & 1) == 0 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)128, (byte)255),
                    "flat" => ((byte)127, (byte)127, (byte)127),
                    "alpha" => (horizontal, vertical, (byte)(255 - horizontal)),
                    "noise" => (Noise(x, y, 0), Noise(x, y, 1), Noise(x, y, 2)),
                    _ => throw new ArgumentException($"Unknown pattern {parts[0]}."),
                };
                pixels[offset] = rgb.Item1;
                pixels[offset + 1] = rgb.Item2;
                pixels[offset + 2] = rgb.Item3;
                if (channels == 4) {
                    pixels[offset + 3] = parts[0] == "alpha" ? horizontal : (byte)255;
                }
            }
        }
        return new TestImage(width, height, channels, pixels, "Lucitex analytic patterns v1; integer formulas; counter-hash noise seed 0x9E3779B9");
    }

    private static byte Noise(int x, int y, int channel)
    {
        var value = unchecked((uint)x * 0x85EBCA6Bu ^ (uint)y * 0xC2B2AE35u ^ (uint)channel * 0x27D4EB2Fu ^ 0x9E3779B9u);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        return (byte)(value >> 24);
    }

    private static TestImage LoadExternal(string id, int channels)
    {
        var manifestPath = RunOptions.Current.DatasetManifest ?? throw new ArgumentException("--dataset-manifest is required.");
        var entries = JsonSerializer.Deserialize<ExternalImage[]>(File.ReadAllText(manifestPath))!;
        var entry = entries.Single(e => e.Id == id);
        if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.License)) {
            throw new InvalidDataException("External data requires source and license metadata.");
        }
        var path = Path.GetFullPath(entry.Path, Path.GetDirectoryName(manifestPath)!);
        var encoded = File.ReadAllBytes(path);
        if (!Convert.ToHexString(SHA256.HashData(encoded)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException($"Dataset SHA-256 mismatch: {id}.");
        }
        if (encoded.Length < 33 || !encoded.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !encoded.AsSpan(12, 4).SequenceEqual("IHDR"u8) || encoded[24] != 8 || encoded[25] is not (2 or 6)) {
            throw new InvalidDataException("External images must be RGB8/RGBA8 PNG; no implicit bit-depth or palette conversion is allowed.");
        }
        using var decoded = Image.Load<Rgba32>(encoded);
        if (decoded.Width != entry.Width || decoded.Height != entry.Height || entry.BitDepth != 8) {
            throw new InvalidDataException("Only declared RGB/RGBA8 inputs without resizing are accepted; dimensions must match.");
        }
        var rgba = new byte[checked(decoded.Width * decoded.Height * 4)];
        decoded.CopyPixelDataTo(rgba);
        var pixels = channels == 4 ? rgba : PixelLayout.ToRgb(rgba);
        return new TestImage(decoded.Width, decoded.Height, channels, pixels, $"{entry.Source}; {entry.License}; file sha256={entry.Sha256}");
    }

    private sealed record ExternalImage(string Id, string Path, string Sha256, int Width, int Height, int BitDepth, string Source, string License);
}
