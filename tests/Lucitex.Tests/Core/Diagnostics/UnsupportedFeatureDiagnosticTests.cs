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
    }
}
