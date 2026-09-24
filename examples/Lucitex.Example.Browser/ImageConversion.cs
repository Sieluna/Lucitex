using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lucitex.Example.Shared;

namespace Lucitex.Example.Browser;

public static partial class ImageConversion
{
    private static readonly string s_Formats = JsonSerializer.Serialize(Formats.All, BrowserJsonContext.Default.IReadOnlyListFormat);

    [JSExport]
    public static string GetFormats() => s_Formats;

    [JSExport]
    public static byte[] Convert(byte[] source, string sourceExtension, string targetExtension, string optionsJson)
    {
        using var document = JsonDocument.Parse(optionsJson);
        var target = Formats.Resolve(targetExtension).Parse(ReadParameters(document.RootElement));
        using var sourceStream = new MemoryStream(source, writable: false);
        using var targetStream = new MemoryStream();
        target.From(Formats.Resolve(sourceExtension), sourceStream).WriteTo(targetStream);
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
