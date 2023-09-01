using System.Buffers.Binary;
using Lucitex.Conversion;
using Lucitex.Core.Execution;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Png;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Conversion;

public class ConversionEndToEndTests
{
    [Fact]
    public void Plan_ExrFloat16Rgba_ToPng_ProducesConvertSampleTypeSteps()
    {
        var descriptor = ExrFixtures.SimpleRgba();
        var result = ConversionPlanner.Plan(descriptor, new PngCodec().Capabilities, ConversionPolicy.Preview);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Plan!.Diagnostics);
        Assert.Contains(result.Plan.Diagnostics, d => d.Category == LossCategory.DynamicRange);

        var steps = result.Plan.Parts[0].Steps;
        Assert.Equal(4, steps.OfType<ConvertSampleTypeStep>().Count());
        Assert.All(steps.OfType<ConvertSampleTypeStep>(), s => Assert.Equal(SampleType.UNorm16, s.To));
    }

    [Fact]
    public void Plan_ExrFloat16Rgba_ToPng_Strict_Fails()
    {
        var descriptor = ExrFixtures.SimpleRgba();
        var result = ConversionPlanner.Plan(descriptor, new PngCodec().Capabilities, ConversionPolicy.Strict);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Execute_ExrFloat16Rgba_ToPng_ProducesQuantizedPixels()
    {
        var descriptor = ExrFixtures.SimpleRgba();
        var part = descriptor.Parts[0];
        var width = (int)part.Topology.BaseExtent.Width;
        var height = (int)part.Topology.BaseExtent.Height;
        var pixelCount = width * height;

        // EXR always stores pixel data row-major with channels concatenated per row in the header's
        // (alphabetically sorted) channel order - ExrWriter.Write/ExrReader.Read both interpret their
        // byte buffers that way regardless of the descriptor's own PlaneLayout tag, so the test buffer
        // has to be built the same way rather than assuming R,G,B,A per-pixel interleaving.
        var random = new Random(42);
        var valuesByChannel = new Dictionary<string, Half[]>();
        foreach (var channel in part.Channels.Channels) {
            var values = new Half[pixelCount];
            for (var p = 0; p < pixelCount; p++) {
                values[p] = (Half)random.NextDouble();
            }

            valuesByChannel[channel.Name.FullName] = values;
        }

        var header = ExrDescriptorMapper.ToExrHeader(descriptor, ExrCompressionId.None);
        var rowStride = header.Channels.Sum(c => width * c.BytesPerSample);
        var exrBytes = new byte[rowStride * height];

        for (var y = 0; y < height; y++) {
            var rowOffset = y * rowStride;
            var channelOffset = 0;
            foreach (var channel in header.Channels) {
                var values = valuesByChannel[channel.Name];
                for (var x = 0; x < width; x++) {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        exrBytes.AsSpan(rowOffset + channelOffset + (x * channel.BytesPerSample), channel.BytesPerSample),
                        BitConverter.HalfToUInt16Bits(values[(y * width) + x]));
                }

                channelOffset += width * channel.BytesPerSample;
            }
        }

        using var exrStream = new MemoryStream();
        var exrCodec = new ExrCodec();
        var exrWriter = exrCodec.CreateWriter(exrStream, descriptor);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        exrWriter.Write(region, exrBytes);
        exrWriter.Finish();

        exrStream.Position = 0;
        var exrReader = exrCodec.OpenReader(exrStream);
        var sourceDescriptor = exrReader.Describe();

        var pngCodec = new PngCodec();
        var planResult = ConversionPlanner.Plan(sourceDescriptor, pngCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);

        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, planResult.Plan!.TargetDescriptor);

        ConversionExecutor.Execute(planResult.Plan, exrReader, exrCodec.Capabilities.SampleByteOrder, pngWriter, pngCodec.Capabilities.SampleByteOrder);

        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);
        var pngDescriptor = pngReader.Describe();
        Assert.Equal(width, (int)pngDescriptor.Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(height, (int)pngDescriptor.Parts[0].Topology.BaseExtent.Height);

        // PNG stores R,G,B,A interleaved per pixel, 16-bit samples big-endian.
        var pngChannelOrder = planResult.Plan.TargetDescriptor.Parts[0].Channels.Channels.Select(c => c.Name.FullName).ToList();
        Assert.Equal(["R", "G", "B", "A"], pngChannelOrder);

        var pngBytes = new byte[pixelCount * 4 * 2];
        pngReader.Read(region, pngBytes);

        for (var p = 0; p < pixelCount; p++) {
            for (var c = 0; c < pngChannelOrder.Count; c++) {
                var expected = (ushort)Math.Clamp(MathF.Round((float)valuesByChannel[pngChannelOrder[c]][p] * 65535f), 0f, 65535f);
                var actual = BinaryPrimitives.ReadUInt16BigEndian(pngBytes.AsSpan(((p * 4) + c) * 2, 2));
                Assert.Equal(expected, actual);
            }
        }
    }
}
