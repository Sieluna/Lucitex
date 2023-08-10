using Lucitex.Core.Diagnostics;
using Lucitex.Core.Representation;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Core.Diagnostics;

public class UnsupportedFeatureDiagnosticTests
{
    [Fact]
    public void DeepFixture_IsFullyDescribable_WhileNoCodecClaimsToDecodeIt()
    {
        var asset = ExrFixtures.DeepDescriptor();

        var representation = Assert.IsType<DeepRepresentation>(asset.Parts[0].Representation);
        Assert.NotEmpty(representation.SampleChannels.Channels);

        var diagnostic = new UnsupportedFeatureDiagnostic {
            Feature = "Exr.DeepData",
            Reason = "Deep sample decoding is not implemented in Phase 0.",
        };

        Assert.Equal("Exr.DeepData", diagnostic.Feature);
        Assert.NotNull(diagnostic.Reason);
    }
}
