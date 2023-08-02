using Lucitex.Core.Representation;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Core.Representation;

public class RepresentationCoverageTests
{
    [Fact]
    public void Fixtures_CoverPlainSampleRepresentation()
    {
        Assert.Contains(FixtureCatalog.All.Values, build => build().Parts[0].Representation is PlainSampleRepresentation);
    }

    [Fact]
    public void Fixtures_CoverPackedEncodedRepresentation()
    {
        Assert.Contains(
            FixtureCatalog.All.Values,
            build => build().Parts[0].Representation is EncodedElementRepresentation { Class: EncodedElementClass.Packed });
    }

    [Fact]
    public void Fixtures_CoverBlockCompressedRepresentation()
    {
        Assert.Contains(
            FixtureCatalog.All.Values,
            build => build().Parts[0].Representation is EncodedElementRepresentation { Class: EncodedElementClass.BlockCompressed });
    }

    [Fact]
    public void Fixtures_CoverSharedExponentRepresentation()
    {
        Assert.Contains(
            FixtureCatalog.All.Values,
            build => build().Parts[0].Representation is EncodedElementRepresentation { Class: EncodedElementClass.SharedExponent });
    }

    [Fact]
    public void Fixtures_CoverIndexedRepresentation()
    {
        Assert.Contains(FixtureCatalog.All.Values, build => build().Parts[0].Representation is IndexedRepresentation);
    }

    [Fact]
    public void Fixtures_CoverDeepRepresentation()
    {
        Assert.Contains(FixtureCatalog.All.Values, build => build().Parts[0].Representation is DeepRepresentation);
    }

    [Fact]
    public void Fixtures_CoverSubsampledChannels()
    {
        var asset = ExrFixtures.SubsampledChannels();
        var channels = asset.Parts[0].Channels.Channels;

        Assert.Contains(channels, c => c.Sampling.Step is { X: 2, Y: 2 });
        Assert.Contains(channels, c => c.Sampling.Step is { X: 1, Y: 1 });
    }

    [Fact]
    public void Fixtures_CoverMultipleParts()
    {
        var asset = ExrFixtures.Multipart();

        Assert.True(asset.Parts.Count > 1);
    }
}
