using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Execution.Io;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Tests.Fixtures;
using Lucitex.Tests.RawCodec;

namespace Lucitex.Tests.Core.Execution.Codecs;

public class RawTestCodecExecutionTests
{
    private static byte[] MakeSourceData(long width, long height, int bytesPerTexel)
    {
        var data = new byte[width * height * bytesPerTexel];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return data;
    }

    [Fact]
    public void FullRoundTrip_WriteInParallelRegions_ThenReadBack_ProducesIdenticalBytesAndDescriptor()
    {
        var asset = DdsFixtures.Rgba8();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        const int bytesPerTexel = 4;

        var source = MakeSourceData(width, height, bytesPerTexel);
        var codec = new RawTestCodec();

        var path = Path.Combine(Path.GetTempPath(), $"lucitex-rawtest-{Guid.NewGuid():N}.rawtest");
        try
        {
            using (var session = new AtomicOutputSession(path))
            {
                var writer = codec.CreateWriter(session.Stream, asset);

                var regions = Enumerable.Range(0, (int)height)
                    .Select(y => new WorkRegion
                    {
                        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
                        Region = ImageBox.FromExclusive(0, y, width, y + 1),
                    })
                    .ToList();

                var sync = new object();
                ExecutionScheduler.Run(regions, region =>
                {
                    var rowBytes = (int)(width * bytesPerTexel);
                    var rowOffset = (int)(region.Region.MinY * rowBytes);

                    lock (sync)
                    {
                        writer.Write(region, source.AsSpan(rowOffset, rowBytes));
                    }
                });

                writer.Finish();
                session.Commit();
            }

            Assert.True(File.Exists(path));

            using var readStream = File.OpenRead(path);
            var reader = codec.OpenReader(readStream);
            var describedAsset = reader.Describe();

            Assert.Single(describedAsset.Parts);
            Assert.Equal(width, describedAsset.Parts[0].Topology.BaseExtent.Width);
            Assert.Equal(height, describedAsset.Parts[0].Topology.BaseExtent.Height);

            var destination = new byte[source.Length];
            var fullRegion = new WorkRegion
            {
                Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
                Region = ImageBox.FromOrigin(width, height),
            };
            var readCount = reader.Read(fullRegion, destination);

            Assert.Equal(source.Length, readCount);
            Assert.Equal(source, destination);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Read_PartialSubRegion_ReturnsExactSlice()
    {
        var asset = DdsFixtures.Rgba8();
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;
        const int bytesPerTexel = 4;

        var source = MakeSourceData(width, height, bytesPerTexel);
        var codec = new RawTestCodec();

        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, asset);
        var fullRegion = new WorkRegion
        {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(width, height),
        };
        writer.Write(fullRegion, source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);

        var subRegion = new WorkRegion
        {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromExclusive(4, 4, 12, 8),
        };

        var subWidth = subRegion.Region.Width;
        var subHeight = subRegion.Region.Height;
        var destination = new byte[subWidth * subHeight * bytesPerTexel];
        reader.Read(subRegion, destination);

        var rowBytes = (int)(subWidth * bytesPerTexel);
        for (var row = 0; row < subHeight; row++)
        {
            var sourceRowStart = (int)(((subRegion.Region.MinY + row) * width) + subRegion.Region.MinX) * bytesPerTexel;
            var expected = source.AsSpan(sourceRowStart, rowBytes);
            var actual = destination.AsSpan(row * rowBytes, rowBytes);

            Assert.True(expected.SequenceEqual(actual));
        }
    }

    [Fact]
    public void Probe_RecognizesMagicBytes()
    {
        var codec = new RawTestCodec();
        var result = codec.Probe("RAW1_____"u8);

        Assert.Equal(ProbeConfidence.Certain, result.Confidence);
        Assert.Equal("raw-test", result.Format);
    }

    [Fact]
    public void Probe_RejectsUnrelatedBytes()
    {
        var codec = new RawTestCodec();
        var result = codec.Probe("XXXX"u8);

        Assert.Equal(ProbeConfidence.None, result.Confidence);
    }

    [Fact]
    public void CodecRegistry_ResolvesByProbedMagicBytes()
    {
        var registry = new CodecRegistry();
        registry.Register(new RawTestCodec());

        var resolved = registry.Resolve("RAW1"u8);

        Assert.Equal("raw-test", resolved.FormatId);
    }

    [Fact]
    public void CodecRegistry_ThrowsWhenNoCodecMatches()
    {
        var registry = new CodecRegistry();
        registry.Register(new RawTestCodec());

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("NOPE"u8));
    }

    [Fact]
    public void CodecRegistry_RejectsDuplicateFormatRegistration()
    {
        var registry = new CodecRegistry();
        registry.Register(new RawTestCodec());

        Assert.Throws<InvalidOperationException>(() => registry.Register(new RawTestCodec()));
    }
}
