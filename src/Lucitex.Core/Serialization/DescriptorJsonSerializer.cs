using System.Text.Json;
using System.Text.Json.Serialization;
using Lucitex.Core.Semantic;

namespace Lucitex.Core.Serialization;

public sealed record DescriptorDocument
{
    public const string CurrentSchemaVersion = "graphics.imageio.descriptor/1";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required ImageAssetDescriptor Asset { get; init; }
}

public static class DescriptorJsonSerializer
{
    public static JsonSerializerOptions Options => DescriptorJsonContext.Default.Options;

    public static string Serialize(ImageAssetDescriptor asset)
    {
        var document = new DescriptorDocument { Asset = asset };
        return JsonSerializer.Serialize(document, DescriptorJsonContext.Default.DescriptorDocument);
    }

    public static ImageAssetDescriptor Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize(json, DescriptorJsonContext.Default.DescriptorDocument)
            ?? throw new JsonException("Descriptor document was null.");

        if (document.SchemaVersion != DescriptorDocument.CurrentSchemaVersion) {
            throw new JsonException($"Unsupported descriptor schema version '{document.SchemaVersion}'.");
        }

        return document.Asset;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DescriptorDocument))]
internal sealed partial class DescriptorJsonContext : JsonSerializerContext;
