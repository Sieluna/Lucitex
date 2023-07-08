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
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ImageAssetDescriptor asset)
    {
        var document = new DescriptorDocument { Asset = asset };
        return JsonSerializer.Serialize(document, Options);
    }

    public static ImageAssetDescriptor Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<DescriptorDocument>(json, Options)
            ?? throw new JsonException("Descriptor document was null.");

        if (document.SchemaVersion != DescriptorDocument.CurrentSchemaVersion)
        {
            throw new JsonException($"Unsupported descriptor schema version '{document.SchemaVersion}'.");
        }

        return document.Asset;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}
