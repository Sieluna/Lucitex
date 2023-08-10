using Lucitex.Core.Semantic;

namespace Lucitex.Tests.Fixtures;

public static class FixtureCatalog
{
    public static IReadOnlyDictionary<string, Func<ImageAssetDescriptor>> All { get; } =
        new Dictionary<string, Func<ImageAssetDescriptor>> {
            ["exr.simpleRgba"] = ExrFixtures.SimpleRgba,
            ["exr.mixedHalfFloat"] = ExrFixtures.MixedHalfFloat,
            ["exr.subsampledChannels"] = ExrFixtures.SubsampledChannels,
            ["exr.multipart"] = ExrFixtures.Multipart,
            ["exr.ripmap"] = ExrFixtures.Ripmap,
            ["exr.negativeDataWindow"] = ExrFixtures.NegativeDataWindow,
            ["exr.deepDescriptor"] = ExrFixtures.DeepDescriptor,

            ["png.rgba8"] = PngFixtures.Rgba8,
            ["png.rgb16"] = PngFixtures.Rgb16,
            ["png.grayscale1Bit"] = PngFixtures.Grayscale1Bit,
            ["png.palette4Bit"] = PngFixtures.Palette4Bit,

            ["dds.rgba8"] = DdsFixtures.Rgba8,
            ["dds.r10g10b10a2"] = DdsFixtures.R10G10B10A2,
            ["dds.bc7"] = DdsFixtures.Bc7,
            ["dds.cubemap"] = DdsFixtures.Cubemap,
            ["dds.array"] = DdsFixtures.Array,
            ["dds.volume3D"] = DdsFixtures.Volume3D,
            ["dds.mipmappedTexture"] = DdsFixtures.MipmappedTexture,

            ["ktx2.rgba8"] = Ktx2Fixtures.Rgba8,
            ["ktx2.bc7"] = Ktx2Fixtures.Bc7,
            ["ktx2.cubemapArray"] = Ktx2Fixtures.CubemapArray,
            ["ktx2.oriented"] = Ktx2Fixtures.Oriented,
            ["ktx2.zlibSupercompressed"] = Ktx2Fixtures.ZlibSupercompressed,

            ["hdr.rgbe"] = HdrFixtures.Rgbe,
        };

    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var key in All.Keys) {
            data.Add(key);
        }

        return data;
    }
}
