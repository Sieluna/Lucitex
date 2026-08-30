using System.Buffers.Binary;
using Lucitex.Conversion;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Png;

namespace Lucitex.Tests.Conversion;

public class SubsampledConversionTests
{
    private const int k_Width = 8;
    private const int k_Height = 4;
    private const int k_ChromaStep = 2;

    private static ImageAssetDescriptor SubsampledChroma()
    {
        var window = ImageBox.FromOrigin(k_Width, k_Height);
        var chroma = new SampleGrid { Origin = Long3.Zero, Step = new Int3(k_ChromaStep, k_ChromaStep, 1) };

        ChannelDescriptor Channel(string name, SampleGrid sampling) => new() {
            Name = name,
            SampleType = SampleType.Float16,
            Sampling = sampling,
        };

        var part = new ImagePartDescriptor {
            Name = "chroma",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
            Topology = new ResourceTopology {
                BaseExtent = new Extent3L(k_Width, k_Height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(k_Width, k_Height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels = [Channel("R", chroma), Channel("G", SampleGrid.Unit), Channel("B", chroma)],
            },
            Representation = new PlainSampleRepresentation {
                Planes = [
                    new SamplePlaneDescriptor {
                        Channels = ["R"],
                        Extent = new Extent3L(k_Width / k_ChromaStep, k_Height / k_ChromaStep, 1),
                    },
                    new SamplePlaneDescriptor { Channels = ["G"], Extent = new Extent3L(k_Width, k_Height, 1) },
                    new SamplePlaneDescriptor {
                        Channels = ["B"],
                        Extent = new Extent3L(k_Width / k_ChromaStep, k_Height / k_ChromaStep, 1),
                    },
                ],
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static float Luma(int x, int y) => ((y * k_Width) + x + 1) / 64f;

    private static float Chroma(string channel, int blockX, int blockY) =>
        ((channel == "R" ? 0.05f : 0.55f) + (((blockY * (k_Width / k_ChromaStep)) + blockX) * 0.02f)) % 1f;

    private static byte[] BuildExrBytes()
    {
        var chromaColumns = k_Width / k_ChromaStep;
        var bytes = new List<byte>();

        void Write(float value)
        {
            var buffer = new byte[2];
            BinaryPrimitives.WriteHalfLittleEndian(buffer, (Half)value);
            bytes.AddRange(buffer);
        }

        for (var y = 0; y < k_Height; y++) {
            var chromaRow = y % k_ChromaStep == 0;

            if (chromaRow) {
                for (var cx = 0; cx < chromaColumns; cx++) {
                    Write(Chroma("B", cx, y / k_ChromaStep));
                }
            }

            for (var x = 0; x < k_Width; x++) {
                Write(Luma(x, y));
            }

            if (chromaRow) {
                for (var cx = 0; cx < chromaColumns; cx++) {
                    Write(Chroma("R", cx, y / k_ChromaStep));
                }
            }
        }

        return bytes.ToArray();
    }

    private static IImageReader OpenSubsampledExr(out ImageAssetDescriptor described, out MemoryStream stream)
    {
        var asset = SubsampledChroma();
        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(k_Width, k_Height),
        };

        stream = new MemoryStream();
        var codec = new ExrCodec(ExrCompressionId.Zip);
        var writer = codec.CreateWriter(stream, asset);
        writer.Write(region, BuildExrBytes());
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        described = reader.Describe();
        return reader;
    }

    [Fact]
    public void Plan_SubsampledSource_ResamplesOnlyTheSubsampledChannels()
    {
        OpenSubsampledExr(out var source, out var stream);

        var result = ConversionPlanner.Plan(source, new PngCodec().Capabilities, ConversionPolicy.Preview);

        Assert.True(result.Success);

        var resamples = result.Plan!.Parts[0].Steps.OfType<ChangeSampleGridStep>().ToList();
        Assert.Equal(2, resamples.Count);
        Assert.Equal(["B", "R"], resamples.Select(step => step.Channel.FullName).Order().ToList());
        Assert.All(resamples, step => Assert.Equal(new Int3(2, 2, 1), step.From.Step));
        Assert.All(resamples, step => Assert.True(step.To.IsUnit));

        var samplingDiagnostics = result.Plan.Diagnostics.Where(d => d.Category == LossCategory.Sampling).ToList();
        Assert.Equal(2, samplingDiagnostics.Count);
        Assert.All(samplingDiagnostics, d => Assert.False(d.IsSemanticLoss));

        Assert.All(
            result.Plan.TargetDescriptor.Parts[0].Channels.Channels,
            channel => Assert.True(channel.Sampling.IsUnit));

        stream.Dispose();
    }

    [Fact]
    public void Execute_SubsampledExr_ToPng_ReplicatesChromaAcrossEachBlock()
    {
        var exrReader = OpenSubsampledExr(out var source, out var exrStream);

        var pngCodec = new PngCodec();
        var planResult = ConversionPlanner.Plan(source, pngCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);

        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, planResult.Plan!.TargetDescriptor);
        ConversionExecutor.Execute(
            planResult.Plan, exrReader, new ExrCodec().Capabilities.SampleByteOrder, pngWriter, pngCodec.Capabilities.SampleByteOrder);

        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);
        var channelOrder = planResult.Plan.TargetDescriptor.Parts[0].Channels.Channels
            .Select(c => c.Name.FullName)
            .ToList();

        var region = new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(k_Width, k_Height),
        };

        var pixels = new byte[k_Width * k_Height * channelOrder.Count * 2];
        pngReader.Read(region, pixels);

        ushort Sample(int x, int y, string channel)
        {
            var index = channelOrder.IndexOf(channel);
            var offset = ((((y * k_Width) + x) * channelOrder.Count) + index) * 2;
            return BinaryPrimitives.ReadUInt16BigEndian(pixels.AsSpan(offset, 2));
        }

        ushort Quantize(float value) => (ushort)Math.Clamp(MathF.Round((float)(Half)value * 65535f), 0f, 65535f);

        for (var y = 0; y < k_Height; y++) {
            for (var x = 0; x < k_Width; x++) {
                Assert.Equal(Quantize(Luma(x, y)), Sample(x, y, "G"));

                foreach (var channel in new[] { "R", "B" }) {
                    var expected = Quantize(Chroma(channel, x / k_ChromaStep, y / k_ChromaStep));
                    Assert.Equal(expected, Sample(x, y, channel));
                }
            }
        }

        var distinctChroma = new HashSet<ushort>();
        for (var blockY = 0; blockY < k_Height / k_ChromaStep; blockY++) {
            for (var blockX = 0; blockX < k_Width / k_ChromaStep; blockX++) {
                distinctChroma.Add(Sample(blockX * k_ChromaStep, blockY * k_ChromaStep, "R"));
            }
        }

        Assert.Equal((k_Width / k_ChromaStep) * (k_Height / k_ChromaStep), distinctChroma.Count);

        exrStream.Dispose();
    }
}
