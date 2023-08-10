using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Dds;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Dds;

public class DdsCodecEndToEndTests
{
    private static byte[] RandomBytes(int length, int seed)
    {
        var buffer = new byte[length];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    [Fact]
    public void RoundTrip_Rgba8_PreservesPixelsAndDescriptor()
    {
        var asset = DdsFixtures.Rgba8();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var source = RandomBytes((int)(width * height * 4), 1);

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        Assert.Equal(width, described.Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(height, described.Parts[0].Topology.BaseExtent.Height);

        var destination = new byte[source.Length];
        var readCount = reader.Read(region, destination);

        Assert.Equal(source.Length, readCount);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_MipmappedTexture_PreservesEveryLevelIndependently()
    {
        var asset = DdsFixtures.MipmappedTexture();
        var part = asset.Parts[0];
        var levels = part.Topology.Levels;

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var sources = new byte[levels.Count][];
        for (var mip = 0; mip < levels.Count; mip++) {
            var extent = levels[mip].Extent;
            var source = RandomBytes((int)(extent.Width * extent.Height), 100 + mip);
            sources[mip] = source;

            writer.Write(new WorkRegion {
                Subresource = new SubresourceId(0, 0, 0, LevelKey.Mip(mip)),
                Region = ImageBox.FromOrigin(extent.Width, extent.Height),
            }, source);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);

        for (var mip = 0; mip < levels.Count; mip++) {
            var extent = levels[mip].Extent;
            var destination = new byte[sources[mip].Length];
            reader.Read(new WorkRegion {
                Subresource = new SubresourceId(0, 0, 0, LevelKey.Mip(mip)),
                Region = ImageBox.FromOrigin(extent.Width, extent.Height),
            }, destination);

            Assert.Equal(sources[mip], destination);
        }
    }

    [Fact]
    public void RoundTrip_Cubemap_KeepsEachFaceIndependent()
    {
        var asset = DdsFixtures.Cubemap();
        var part = asset.Parts[0];
        var size = part.Topology.BaseExtent.Width;

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var faces = new byte[6][];
        for (var face = 0; face < 6; face++) {
            var source = RandomBytes((int)(size * size * 4), 200 + face);
            faces[face] = source;

            writer.Write(new WorkRegion {
                Subresource = new SubresourceId(0, 0, face, LevelKey.Base),
                Region = ImageBox.FromOrigin(size, size),
            }, source);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();
        Assert.Equal(6, described.Parts[0].Topology.FaceCount);

        for (var face = 0; face < 6; face++) {
            var destination = new byte[faces[face].Length];
            reader.Read(new WorkRegion {
                Subresource = new SubresourceId(0, 0, face, LevelKey.Base),
                Region = ImageBox.FromOrigin(size, size),
            }, destination);

            Assert.Equal(faces[face], destination);
        }
    }

    [Fact]
    public void RoundTrip_TextureArray_KeepsEachElementIndependent()
    {
        var asset = DdsFixtures.Array();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var elements = new byte[8][];
        for (var element = 0; element < 8; element++) {
            var source = RandomBytes((int)(width * height), 300 + element);
            elements[element] = source;

            writer.Write(new WorkRegion {
                Subresource = new SubresourceId(0, element, 0, LevelKey.Base),
                Region = ImageBox.FromOrigin(width, height),
            }, source);
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);

        for (var element = 0; element < 8; element++) {
            var destination = new byte[elements[element].Length];
            reader.Read(new WorkRegion {
                Subresource = new SubresourceId(0, element, 0, LevelKey.Base),
                Region = ImageBox.FromOrigin(width, height),
            }, destination);

            Assert.Equal(elements[element], destination);
        }
    }

    [Fact]
    public void RoundTrip_Volume3D_PreservesAllDepthSlices()
    {
        var asset = DdsFixtures.Volume3D();
        var part = asset.Parts[0];
        var extent = part.Topology.BaseExtent;
        var source = RandomBytes((int)(extent.Width * extent.Height * extent.Depth), 5);

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(extent.Width, extent.Height),
        };
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();
        Assert.Equal(3, described.Parts[0].Topology.SpatialDimensions);

        var destination = new byte[source.Length];
        var readCount = reader.Read(region, destination);

        Assert.Equal(source.Length, readCount);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_R10G10B10A2_PreservesPackedBytes()
    {
        var asset = DdsFixtures.R10G10B10A2();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var source = RandomBytes((int)(width * height * 4), 9);

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var destination = new byte[source.Length];
        reader.Read(region, destination);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_Bc7Block_PassesRawBlockBytesThroughUnmodified()
    {
        var asset = DdsFixtures.Bc7();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var blockBytes = ((width + 3) / 4) * ((height + 3) / 4) * 16;
        var source = RandomBytes((int)blockBytes, 11);

        var codec = new DdsCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var destination = new byte[source.Length];
        reader.Read(region, destination);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void Probe_RecognizesDdsMagic()
    {
        var codec = new DdsCodec();
        var result = codec.Probe("DDS "u8);

        Assert.Equal(Lucitex.Core.Execution.Codecs.ProbeConfidence.Certain, result.Confidence);
        Assert.Equal("dds", result.Format);
    }
}
