using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lucitex.Example.Shared;

namespace Lucitex.Example.Browser;

public static partial class ImageConversion
{
    private static readonly string s_Formats = JsonSerializer.Serialize(Formats.All, BrowserJsonContext.Default.IReadOnlyListFormat);
    private static readonly HashSet<string> s_RenderableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    [JSExport]
    public static string GetFormats() => s_Formats;

    [JSExport]
    public static byte[] GetPreview(byte[] source, string sourceExtension)
    {
        if (s_RenderableExtensions.Contains(sourceExtension)) {
            return source;
        }

        using var sourceStream = new MemoryStream(source, writable: false);
        using var targetStream = new MemoryStream();
        Formats.Png.From(Formats.Resolve(sourceExtension), sourceStream).WriteTo(targetStream);
        return targetStream.ToArray();
    }

    [JSExport]
    public static byte[] Convert(byte[] source, string sourceExtension, string targetExtension, string optionsJson,
        int cropX, int cropY, int cropWidth, int cropHeight, int resizeWidth, int resizeHeight, int targetSizeBytes)
    {
        using var document = JsonDocument.Parse(optionsJson);
        var target = Formats.Resolve(targetExtension).Parse(ReadParameters(document.RootElement));
        var sourceFormat = Formats.Resolve(sourceExtension);
        (int Width, int Height)? resize = resizeWidth > 0 && resizeHeight > 0 ? (resizeWidth, resizeHeight) : null;
        (int X, int Y, int Width, int Height)? crop = cropWidth > 0 && cropHeight > 0 ? (cropX, cropY, cropWidth, cropHeight) : null;

        if (targetSizeBytes > 0) {
            if (!TargetSizeCompressor.SupportsTargetSize(target)) {
                throw new NotSupportedException($"'{target.Id}' has no adjustable quality, so a target size has nothing to search over.");
            }

            return TargetSizeCompressor.Compress(sourceFormat, source, target, targetSizeBytes, resize: resize, crop: crop).Bytes;
        }

        using var sourceStream = new MemoryStream(source, writable: false);
        using var targetStream = new MemoryStream();
        var conversion = target.From(sourceFormat, sourceStream);
        if (crop is { } region) {
            conversion = conversion.Crop(region.X, region.Y, region.Width, region.Height);
        }
        if (resize is { } size) {
            conversion = conversion.Resize(size.Width, size.Height);
        }

        conversion.WriteTo(targetStream);
        return targetStream.ToArray();
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadParameters(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) {
            throw new ArgumentException("Encoder parameters must be a JSON object.");
        }
        foreach (var property in element.EnumerateObject()) {
            if (property.Value.ValueKind != JsonValueKind.String) {
                throw new ArgumentException($"Parameter '{property.Name}' must be a string.");
            }
            yield return KeyValuePair.Create(property.Name, property.Value.GetString()!);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(IReadOnlyList<Format>))]
internal partial class BrowserJsonContext : JsonSerializerContext;
