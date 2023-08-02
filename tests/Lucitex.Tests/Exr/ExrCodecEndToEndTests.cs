using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Exr;

public class ExrCodecEndToEndTests
{
    private static WorkRegion FullRegion(long width, long height) => new()
    {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    [Theory]
    [InlineData(ExrCompressionId.None)]
    [InlineData(ExrCompressionId.Rle)]
    [InlineData(ExrCompressionId.Zips)]
    [InlineData(ExrCompressionId.Zip)]
    public void RoundTrip_SimpleRgba_PreservesPixelsAndDescriptor(ExrCompressionId compression)
    {
        var asset = ExrFixtures.SimpleRgba();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var header = ExrDescriptorMapper.ToExrHeader(asset, compression);
        var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);
        var source = new byte[rowStride * height];
        new Random(42).NextBytes(source);

        var codec = new ExrCodec(compression);
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(FullRegion(width, height), source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        Assert.Single(described.Parts);
        Assert.Equal(width, described.Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(height, described.Parts[0].Topology.BaseExtent.Height);
        Assert.Equal(part.Channels.Channels.Count, described.Parts[0].Channels.Channels.Count);

        var destination = new byte[source.Length];
        var readCount = reader.Read(FullRegion(width, height), destination);

        Assert.Equal(source.Length, readCount);
        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_MixedHalfFloat_PreservesChannelSampleTypes()
    {
        var asset = ExrFixtures.MixedHalfFloat();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Zip);
        var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);
        var source = new byte[rowStride * height];
        new Random(7).NextBytes(source);

        var codec = new ExrCodec(ExrCompressionId.Zip);
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(FullRegion(width, height), source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        var describedChannels = described.Parts[0].Channels.Channels
            .OrderBy(c => c.Name.FullName, StringComparer.Ordinal)
            .Select(c => c.SampleType)
            .ToList();
        var sourceChannels = part.Channels.Channels
            .OrderBy(c => c.Name.FullName, StringComparer.Ordinal)
            .Select(c => c.SampleType)
            .ToList();

        Assert.Equal(sourceChannels, describedChannels);

        var destination = new byte[source.Length];
        reader.Read(FullRegion(width, height), destination);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_WithMisalignedRowWrites_StillProducesCorrectBytes()
    {
        var asset = ExrFixtures.SimpleRgba();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Zip);
        var rowStride = header.Channels.Sum(c => (int)width * c.BytesPerSample);
        var source = new byte[rowStride * height];
        new Random(99).NextBytes(source);

        var codec = new ExrCodec(ExrCompressionId.Zip);
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);

        const int groupSize = 5;
        var y = 0L;
        while (y < height)
        {
            var rows = Math.Min(groupSize, height - y);
            var region = new WorkRegion
            {
                Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
                Region = ImageBox.FromExclusive(0, y, width, y + rows),
            };

            var offset = (int)(y * rowStride);
            var length = (int)(rows * rowStride);
            writer.Write(region, source.AsSpan(offset, length));

            y += rows;
        }

        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var destination = new byte[source.Length];
        reader.Read(FullRegion(width, height), destination);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void Probe_RecognizesExrMagicBytes()
    {
        var codec = new ExrCodec();
        var result = codec.Probe([0x76, 0x2f, 0x31, 0x01]);

        Assert.Equal(ProbeConfidence.Certain, result.Confidence);
        Assert.Equal("exr", result.Format);
    }

    [Fact]
    public void CodecRegistry_ResolvesExrByMagicBytes()
    {
        var registry = new CodecRegistry();
        registry.Register(new ExrCodec());

        var resolved = registry.Resolve([0x76, 0x2f, 0x31, 0x01, 2, 0, 0, 0]);

        Assert.Equal("exr", resolved.FormatId);
    }
}
