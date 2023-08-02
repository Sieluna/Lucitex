using Lucitex.Core.Serialization;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Core.Serialization;

public class DescriptorJsonRoundTripTests
{
    [Theory]
    [MemberData(nameof(FixtureCatalog.Keys), MemberType = typeof(FixtureCatalog))]
    public void Serialize_Deserialize_Serialize_ProducesIdenticalJson(string fixtureKey)
    {
        var asset = FixtureCatalog.All[fixtureKey]();

        var firstJson = DescriptorJsonSerializer.Serialize(asset);
        var roundTripped = DescriptorJsonSerializer.Deserialize(firstJson);
        var secondJson = DescriptorJsonSerializer.Serialize(roundTripped);

        Assert.Equal(firstJson, secondJson);
    }

    [Theory]
    [MemberData(nameof(FixtureCatalog.Keys), MemberType = typeof(FixtureCatalog))]
    public void Serialize_EmbedsCurrentSchemaVersion(string fixtureKey)
    {
        var asset = FixtureCatalog.All[fixtureKey]();

        var json = DescriptorJsonSerializer.Serialize(asset);

        Assert.Contains(DescriptorDocument.CurrentSchemaVersion, json);
    }

    [Fact]
    public void Deserialize_RejectsUnknownSchemaVersion()
    {
        const string json = """
        {
          "schemaVersion": "graphics.imageio.descriptor/999",
          "asset": { "parts": [] }
        }
        """;

        Assert.Throws<System.Text.Json.JsonException>(() => DescriptorJsonSerializer.Deserialize(json));
    }
}
