using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Ktx2;
using Lucitex.Ktx2.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Ktx2;

public class Ktx2CodecEndToEndTests
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
        var asset = Ktx2Fixtures.Rgba8();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var source = RandomBytes((int)(width * height * 4), 1);

        var codec = new Ktx2Codec();
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
        var asset = Ktx2Fixtures.MipmappedTexture();
        var part = asset.Parts[0];
        var levels = part.Topology.Levels;

        var codec = new Ktx2Codec();
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
    public void RoundTrip_CubemapArray_KeepsEachFaceAndElementIndependent()
    {
        var asset = Ktx2Fixtures.CubemapArray();
        var part = asset.Parts[0];
        var size = part.Topology.BaseExtent.Width;

        var codec = new Ktx2Codec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        var items = new Dictionary<(int Element, int Face), byte[]>();
        for (var element = 0; element < 4; element++) {
            for (var face = 0; face < 6; face++) {
                var source = RandomBytes((int)(size * size), (element * 6) + face + 500);
                items[(element, face)] = source;

                writer.Write(new WorkRegion {
                    Subresource = new SubresourceId(0, element, face, LevelKey.Base),
                    Region = ImageBox.FromOrigin(size, size),
                }, source);
            }
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();
        Assert.Equal(6, described.Parts[0].Topology.FaceCount);
        Assert.Equal(4, described.Parts[0].Topology.ArrayElementCount);

        foreach (var ((element, face), source) in items) {
            var destination = new byte[source.Length];
            reader.Read(new WorkRegion {
                Subresource = new SubresourceId(0, element, face, LevelKey.Base),
                Region = ImageBox.FromOrigin(size, size),
            }, destination);

            Assert.Equal(source, destination);
        }
    }

    [Fact]
    public void RoundTrip_Bc7Block_PassesRawBlockBytesThroughUnmodified()
    {
        var asset = Ktx2Fixtures.Bc7();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var blockBytes = ((width + 3) / 4) * ((height + 3) / 4) * 16;
        var source = RandomBytes((int)blockBytes, 11);

        var codec = new Ktx2Codec();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_Bc6HBlock_PreservesSignednessAndRawBytes(bool signed)
    {
        var asset = DdsFixtures.Bc6H(signed);
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var source = RandomBytes((int)(((width + 3) / 4) * ((height + 3) / 4) * 16), signed ? 19 : 15);
        var codec = new Ktx2Codec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe().Parts[0];
        var encoded = Assert.IsType<Lucitex.Core.Representation.EncodedElementRepresentation>(described.Representation);
        Assert.Equal(signed ? Lucitex.Core.Representation.EncodedFormatId.Bc6HSigned : Lucitex.Core.Representation.EncodedFormatId.Bc6H, encoded.Format);
        Assert.All(described.Channels.Channels, channel => Assert.Equal(Lucitex.Core.Sampling.SampleType.Float16, channel.SampleType));
        var destination = new byte[source.Length];
        reader.Read(region, destination);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_ZlibSupercompression_TransparentlyDecompressesOnRead()
    {
        var asset = Ktx2Fixtures.ZlibSupercompressed();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        var source = RandomBytes((int)(width * height * 4), 42);

        var codec = new Ktx2Codec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset, Ktx2SupercompressionScheme.Zlib);

        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
        writer.Write(region, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var destination = new byte[source.Length];
        var readCount = reader.Read(region, destination);

        Assert.Equal(source.Length, readCount);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void Probe_RecognizesKtx2Identifier()
    {
        var codec = new Ktx2Codec();
        byte[] identifier = [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];
        var result = codec.Probe(identifier);

        Assert.Equal(Lucitex.Core.Execution.Codecs.ProbeConfidence.Certain, result.Confidence);
        Assert.Equal("ktx2", result.Format);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(79)]
    public void OpenReader_TruncatedInput_ReportsImageFormatException(int length)
    {
        var bytes = new byte[length];
        var codec = new Ktx2Codec();

        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(new MemoryStream(bytes)));

        Assert.Equal("ktx2", exception.Format);
    }

    [Fact]
    public void OpenReader_LevelShorterThanImageDimensions_ReportsImageFormatException()
    {
        var asset = Ktx2Fixtures.Rgba8();
        var codec = new Ktx2Codec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);
        var part = asset.Parts[0];
        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = part.Spatial.DataWindow,
        };
        writer.Write(region, new byte[part.Spatial.DataWindow.Width * part.Spatial.DataWindow.Height * 4]);
        writer.Finish();

        var bytes = stream.ToArray();
        bytes[24] = checked((byte)(part.Spatial.DataWindow.Height + 1));

        var exception = Assert.Throws<ImageFormatException>(() => codec.OpenReader(new MemoryStream(bytes)));

        Assert.Equal("BadLevelIndex", exception.Code);
    }
}
