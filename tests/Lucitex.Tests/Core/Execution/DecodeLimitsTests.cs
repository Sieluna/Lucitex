using Lucitex.Core.Execution;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Core.Execution;

public class DecodeLimitsTests
{
    [Fact]
    public void Validate_AcceptsFixturesWithinDefaultLimits()
    {
        foreach (var build in FixtureCatalog.All.Values)
        {
            var asset = build();
            var violations = DecodeLimitsValidator.Validate(asset, DecodeLimits.Default);

            Assert.Empty(violations);
        }
    }

    [Fact]
    public void Validate_FlagsExcessiveDimensions()
    {
        var asset = DdsFixtures.Rgba8();
        var oversized = asset with
        {
            Parts =
            [
                asset.Parts[0] with
                {
                    Topology = asset.Parts[0].Topology with
                    {
                        BaseExtent = new Lucitex.Core.Spatial.Extent3L(1L << 20, 1, 1),
                    },
                },
            ],
        };

        var violations = DecodeLimitsValidator.Validate(oversized, DecodeLimits.Default);

        Assert.Contains(violations, v => v.Limit == nameof(DecodeLimits.MaxDimensions));
    }

    [Fact]
    public void Validate_FlagsTooManyParts()
    {
        var asset = ExrFixtures.Multipart();
        var limits = DecodeLimits.Default with { MaxParts = 1 };

        var violations = DecodeLimitsValidator.Validate(asset, limits);

        Assert.Contains(violations, v => v.Limit == nameof(DecodeLimits.MaxParts));
    }

    [Fact]
    public void Validate_FlagsTooManyLevels()
    {
        var asset = DdsFixtures.MipmappedTexture();
        var limits = DecodeLimits.Default with { MaxLevels = 2 };

        var violations = DecodeLimitsValidator.Validate(asset, limits);

        Assert.Contains(violations, v => v.Limit == nameof(DecodeLimits.MaxLevels));
    }
}
