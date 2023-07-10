using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Phase0;

public class SubresourceTopologyTests
{
    [Fact]
    public void ExrRipmap_LevelsVaryXAndYIndependently()
    {
        var asset = ExrFixtures.Ripmap();
        var levels = asset.Parts[0].Topology.Levels;

        Assert.Contains(levels, l => l.Key is { X: 1, Y: 0 });
        Assert.Contains(levels, l => l.Key is { X: 0, Y: 1 });
        Assert.Contains(levels, l => l.Key is { X: 1, Y: 1 });
        Assert.Contains(levels, l => l.Key is { X: 0, Y: 0 });
    }

    [Fact]
    public void DdsMipChain_HalvesDownToOneByOne()
    {
        var asset = DdsFixtures.MipmappedTexture();
        var levels = asset.Parts[0].Topology.Levels;

        Assert.Equal(7, levels.Count);
        Assert.Equal(new Extent3L(64, 64, 1), levels[0].Extent);
        Assert.Equal(new Extent3L(1, 1, 1), levels[^1].Extent);
    }

    [Fact]
    public void DdsCubemap_UsesFaceCountNotArrayElement()
    {
        var asset = DdsFixtures.Cubemap();
        var topology = asset.Parts[0].Topology;

        Assert.Equal(6, topology.FaceCount);
        Assert.Equal(1, topology.ArrayElementCount);
    }

    [Fact]
    public void DdsArray_UsesArrayElementCountNotFace()
    {
        var asset = DdsFixtures.Array();
        var topology = asset.Parts[0].Topology;

        Assert.Equal(8, topology.ArrayElementCount);
        Assert.Equal(1, topology.FaceCount);
    }

    [Fact]
    public void Ktx2CubemapArray_CombinesFaceAndArrayElementIndependently()
    {
        var asset = Ktx2Fixtures.CubemapArray();
        var topology = asset.Parts[0].Topology;

        Assert.Equal(6, topology.FaceCount);
        Assert.Equal(4, topology.ArrayElementCount);
    }

    [Fact]
    public void DdsVolume3D_UsesThreeSpatialDimensionsWithDepthGreaterThanOne()
    {
        var asset = DdsFixtures.Volume3D();
        var topology = asset.Parts[0].Topology;

        Assert.Equal(3, topology.SpatialDimensions);
        Assert.True(topology.BaseExtent.Depth > 1);
    }

    [Fact]
    public void SubresourceId_KeepsPartArrayElementFaceAndLevelDistinct()
    {
        var id = new SubresourceId(Part: 2, ArrayElement: 3, Face: 4, Level: new LevelKey(1, 0, 0));

        Assert.Equal(2, id.Part);
        Assert.Equal(3, id.ArrayElement);
        Assert.Equal(4, id.Face);
        Assert.Equal(new LevelKey(1, 0, 0), id.Level);
    }
}
