using System.Buffers.Binary;
using Lucitex.Conversion;
using Lucitex.Conversion.Kernels;
using Lucitex.Core.Execution;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Dds;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Hdr;
using Lucitex.Ktx2;
using Lucitex.Png;
using Lucitex.Tests.Fixtures;
using Lucitex.Compression;

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

    [Fact]
    public void Execute_HdrRgbe_ToPng_ProducesLinearRgb16()
    {
        const int width = 8;
        const int height = 2;
        var descriptor = Resize(HdrFixtures.Rgbe(), width, height);
        var rgbe = Enumerable.Repeat(new byte[] { 128, 64, 32, 129 }, width * height).SelectMany(pixel => pixel).ToArray();
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var hdrCodec = new HdrCodec();
        using var hdrStream = new MemoryStream();
        var hdrWriter = hdrCodec.CreateWriter(hdrStream, descriptor);
        hdrWriter.Write(region, rgbe);
        hdrWriter.Finish();
        hdrStream.Position = 0;
        var hdrReader = hdrCodec.OpenReader(hdrStream);

        var pngCodec = new PngCodec();
        var planResult = ConversionPlanner.Plan(hdrReader.Describe(), pngCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, step => step is DecodeEncodedElementsStep { Format.Name: nameof(Lucitex.Core.Representation.EncodedFormatId.Rgbe) });
        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, hdrReader, hdrCodec.Capabilities.SampleByteOrder, pngWriter, pngCodec.Capabilities.SampleByteOrder);

        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);
        var pixels = new byte[width * height * 6];
        pngReader.Read(region, pixels);
        for (var pixel = 0; pixel < width * height; pixel++) {
            Assert.Equal(ushort.MaxValue, BinaryPrimitives.ReadUInt16BigEndian(pixels.AsSpan((pixel * 6), 2)));
            Assert.Equal(32768, BinaryPrimitives.ReadUInt16BigEndian(pixels.AsSpan((pixel * 6) + 2, 2)));
            Assert.Equal(16384, BinaryPrimitives.ReadUInt16BigEndian(pixels.AsSpan((pixel * 6) + 4, 2)));
        }
    }

    [Fact]
    public void Execute_PngRgb8_ToHdr_ProducesRgbeElements()
    {
        const int width = 8;
        const int height = 2;
        var descriptor = Resize(PngFixtures.Rgba8(), width, height);
        var rgbDescriptor = descriptor with {
            Parts =
            [
                descriptor.Parts[0] with {
                    Channels = new Lucitex.Core.Sampling.ChannelSchema { Channels = descriptor.Parts[0].Channels.Channels.Take(3).ToList() },
                    Representation = new Lucitex.Core.Representation.PlainSampleRepresentation {
                        Planes = [new Lucitex.Core.Representation.SamplePlaneDescriptor { Channels = ["R", "G", "B"], Extent = new Extent3L(width, height, 1), Layout = Lucitex.Core.Representation.PlaneLayout.Interleaved }],
                    },
                },
            ],
        };
        var pixels = Enumerable.Repeat(new byte[] { 255, 128, 64 }, width * height).SelectMany(pixel => pixel).ToArray();
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var pngCodec = new PngCodec();
        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, rgbDescriptor);
        pngWriter.Write(region, pixels);
        pngWriter.Finish();
        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);

        var hdrCodec = new HdrCodec();
        var planResult = ConversionPlanner.Plan(pngReader.Describe(), hdrCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, step => step is EncodeEncodedElementsStep { Format.Name: nameof(Lucitex.Core.Representation.EncodedFormatId.Rgbe) });
        using var hdrStream = new MemoryStream();
        var hdrWriter = hdrCodec.CreateWriter(hdrStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, pngReader, pngCodec.Capabilities.SampleByteOrder, hdrWriter, hdrCodec.Capabilities.SampleByteOrder);

        hdrStream.Position = 0;
        var hdrReader = hdrCodec.OpenReader(hdrStream);
        var rgbe = new byte[width * height * 4];
        hdrReader.Read(region, rgbe);
        for (var pixel = 0; pixel < width * height; pixel++) {
            Assert.True(rgbe.AsSpan(pixel * 4, 4).SequenceEqual(new byte[] { 128, 64, 32, 129 }));
        }
    }

    [Fact]
    public void Execute_DdsBc7_ToKtx2_RepackagesEncodedBytesWithoutDecode()
    {
        var descriptor = DdsFixtures.Bc7();
        var part = descriptor.Parts[0];
        var width = (int)part.Topology.BaseExtent.Width;
        var height = (int)part.Topology.BaseExtent.Height;
        var encoded = new byte[((width + 3) / 4) * ((height + 3) / 4) * 16];
        new Random(91).NextBytes(encoded);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = part.Spatial.DataWindow };
        var ddsCodec = new DdsCodec();
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, descriptor);
        ddsWriter.Write(region, encoded);
        ddsWriter.Finish();
        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);

        var ktx2Codec = new Ktx2Codec();
        var planResult = ConversionPlanner.Plan(ddsReader.Describe(), ktx2Codec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        var step = Assert.Single(planResult.Plan!.Parts[0].Steps.OfType<TranscodeEncodedElementsStep>());
        Assert.Equal(step.SourceFormat, step.TargetFormat);
        Assert.DoesNotContain(planResult.Plan.Parts[0].Steps, candidate => candidate is DecodeEncodedElementsStep or EncodeEncodedElementsStep);
        using var ktx2Stream = new MemoryStream();
        var ktx2Writer = ktx2Codec.CreateWriter(ktx2Stream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, ddsReader, ddsCodec.Capabilities.SampleByteOrder, ktx2Writer, ktx2Codec.Capabilities.SampleByteOrder);

        ktx2Stream.Position = 0;
        var ktx2Reader = ktx2Codec.OpenReader(ktx2Stream);
        var actual = new byte[encoded.Length];
        ktx2Reader.Read(region, actual);
        Assert.Equal(encoded, actual);
    }

    [Fact]
    public void Execute_DdsBc1_ToPng_DecodesEveryBlockAndCropsEdges()
    {
        const int width = 7;
        const int height = 5;
        var resized = Resize(DdsFixtures.Bc7(), width, height);
        var part = resized.Parts[0];
        var descriptor = resized with {
            Parts =
            [
                part with {
                    Representation = ((Lucitex.Core.Representation.EncodedElementRepresentation)part.Representation) with {
                        Format = Lucitex.Core.Representation.EncodedFormatId.Bc1,
                        BitsPerElement = 64,
                    },
                },
            ],
        };
        var rgba = new byte[width * height * 4];
        new Random(17).NextBytes(rgba);
        for (var i = 3; i < rgba.Length; i += 4) {
            rgba[i] = 255;
        }

        var encoded = new byte[BcImageCodec.EncodedByteCount(BcFormat.Bc1, width, height)];
        BcImageCodec.Encode(BcFormat.Bc1, rgba, width, height, encoded);
        var expected = new byte[rgba.Length];
        BcImageCodec.Decode(BcFormat.Bc1, encoded, width, height, expected);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var ddsCodec = new DdsCodec();
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, descriptor);
        ddsWriter.Write(region, encoded);
        ddsWriter.Finish();
        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);

        var pngCodec = new PngCodec();
        var planResult = ConversionPlanner.Plan(ddsReader.Describe(), pngCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, candidate => candidate is DecodeEncodedElementsStep { Format.Name: nameof(Lucitex.Core.Representation.EncodedFormatId.Bc1) });
        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, ddsReader, ddsCodec.Capabilities.SampleByteOrder, pngWriter, pngCodec.Capabilities.SampleByteOrder);

        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);
        var actual = new byte[expected.Length];
        pngReader.Read(region, actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Execute_DdsBc7_ToPng_DecodesEveryBlockAndCropsEdges()
    {
        const int width = 7;
        const int height = 5;
        var descriptor = Resize(DdsFixtures.Bc7(), width, height);
        using var vectors = typeof(ConversionEndToEndTests).Assembly.GetManifestResourceStream("Lucitex.Tests.Bc7Conformance.bin");
        Assert.NotNull(vectors);
        using var vectorReader = new BinaryReader(vectors);
        Assert.True(vectorReader.ReadInt32() > 0);
        vectorReader.ReadByte();
        var block = vectorReader.ReadBytes(16);
        var encoded = new byte[BcImageCodec.EncodedByteCount(BcFormat.Bc7, width, height)];
        for (var offset = 0; offset < encoded.Length; offset += block.Length) {
            block.CopyTo(encoded, offset);
        }

        var expected = new byte[width * height * 4];
        BcImageCodec.Decode(BcFormat.Bc7, encoded, width, height, expected);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var ddsCodec = new DdsCodec();
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, descriptor);
        ddsWriter.Write(region, encoded);
        ddsWriter.Finish();
        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);

        var pngCodec = new PngCodec();
        var planResult = ConversionPlanner.Plan(ddsReader.Describe(), pngCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, candidate => candidate is DecodeEncodedElementsStep { Format.Name: nameof(Lucitex.Core.Representation.EncodedFormatId.Bc7) });
        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, ddsReader, ddsCodec.Capabilities.SampleByteOrder, pngWriter, pngCodec.Capabilities.SampleByteOrder);

        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);
        var actual = new byte[expected.Length];
        pngReader.Read(region, actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Execute_DdsBc6H_ToHdr_PreservesReferenceHdrValuesThroughRgbe()
    {
        const int width = 8;
        const int height = 4;
        var descriptor = Resize(DdsFixtures.Bc6H(), width, height);
        using var vectors = typeof(ConversionEndToEndTests).Assembly.GetManifestResourceStream("Lucitex.Tests.Bc6HUnsignedConformance.bin");
        Assert.NotNull(vectors);
        using var vectorReader = new BinaryReader(vectors);
        Assert.True(vectorReader.ReadInt32() > 0);
        vectorReader.ReadByte();
        var block = vectorReader.ReadBytes(16);
        var referenceBlock = Enumerable.Range(0, 48).Select(_ => vectorReader.ReadSingle()).ToArray();
        var encoded = new byte[32];
        block.CopyTo(encoded, 0);
        block.CopyTo(encoded, 16);
        var red = new float[width * height];
        var green = new float[width * height];
        var blue = new float[width * height];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var sourcePixel = (y * 4) + (x & 3);
                var targetPixel = (y * width) + x;
                red[targetPixel] = referenceBlock[(sourcePixel * 3) + 0];
                green[targetPixel] = referenceBlock[(sourcePixel * 3) + 1];
                blue[targetPixel] = referenceBlock[(sourcePixel * 3) + 2];
            }
        }

        var expected = new byte[width * height * 4];
        RgbeConversionKernel.Encode(red, green, blue, expected);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var ddsCodec = new DdsCodec();
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, descriptor);
        ddsWriter.Write(region, encoded);
        ddsWriter.Finish();
        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);

        var hdrCodec = new HdrCodec();
        var planResult = ConversionPlanner.Plan(ddsReader.Describe(), hdrCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, candidate => candidate is DecodeEncodedElementsStep { Format.Name: nameof(Lucitex.Core.Representation.EncodedFormatId.Bc6H) });
        using var hdrStream = new MemoryStream();
        var hdrWriter = hdrCodec.CreateWriter(hdrStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, ddsReader, ddsCodec.Capabilities.SampleByteOrder, hdrWriter, hdrCodec.Capabilities.SampleByteOrder);

        hdrStream.Position = 0;
        var hdrReader = hdrCodec.OpenReader(hdrStream);
        var actual = new byte[expected.Length];
        hdrReader.Read(region, actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Execute_PngRgba8_ToDdsBc1_UsesRequestedBlockEncoder()
    {
        const int width = 7;
        const int height = 5;
        var descriptor = Resize(PngFixtures.Rgba8(), width, height);
        var rgba = new byte[width * height * 4];
        new Random(73).NextBytes(rgba);
        for (var i = 3; i < rgba.Length; i += 4) {
            rgba[i] = 255;
        }

        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var pngCodec = new PngCodec();
        using var pngStream = new MemoryStream();
        var pngWriter = pngCodec.CreateWriter(pngStream, descriptor);
        pngWriter.Write(region, rgba);
        pngWriter.Finish();
        pngStream.Position = 0;
        var pngReader = pngCodec.OpenReader(pngStream);

        var ddsCodec = new DdsCodec();
        var planResult = ConversionPlanner.Plan(
            pngReader.Describe(),
            ddsCodec.Capabilities,
            ConversionPolicy.Preview,
            Lucitex.Core.Representation.EncodedFormatId.Bc1);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, candidate => candidate is EncodeEncodedElementsStep { Format.Name: nameof(Lucitex.Core.Representation.EncodedFormatId.Bc1) });
        Assert.Contains(planResult.Plan.Diagnostics, diagnostic => diagnostic.Category == LossCategory.Compression);
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, pngReader, pngCodec.Capabilities.SampleByteOrder, ddsWriter, ddsCodec.Capabilities.SampleByteOrder);

        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);
        var actual = new byte[BcImageCodec.EncodedByteCount(BcFormat.Bc1, width, height)];
        ddsReader.Read(region, actual);
        var expected = new byte[actual.Length];
        BcImageCodec.Encode(BcFormat.Bc1, rgba, width, height, expected);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Bc2", 4)]
    [InlineData("Bc3", 4)]
    [InlineData("Bc4", 1)]
    [InlineData("Bc5", 2)]
    public void Execute_Ktx2Raw_ToDdsBc_UsesRequestedBlockEncoder(string formatName, int channelCount)
    {
        const int width = 9;
        const int height = 6;
        var descriptor = PlainKtxDescriptor(width, height, channelCount);
        var source = new byte[width * height * channelCount];
        new Random(101 + channelCount).NextBytes(source);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var ktx2Codec = new Ktx2Codec();
        using var ktx2Stream = new MemoryStream();
        var ktx2Writer = ktx2Codec.CreateWriter(ktx2Stream, descriptor);
        ktx2Writer.Write(region, source);
        ktx2Writer.Finish();
        ktx2Stream.Position = 0;
        var ktx2Reader = ktx2Codec.OpenReader(ktx2Stream);

        var encodedFormat = formatName switch {
            "Bc2" => Lucitex.Core.Representation.EncodedFormatId.Bc2,
            "Bc3" => Lucitex.Core.Representation.EncodedFormatId.Bc3,
            "Bc4" => Lucitex.Core.Representation.EncodedFormatId.Bc4,
            "Bc5" => Lucitex.Core.Representation.EncodedFormatId.Bc5,
            _ => throw new ArgumentOutOfRangeException(nameof(formatName)),
        };
        var bcFormat = (BcFormat)(int.Parse(formatName[2..]) - 1);
        var ddsCodec = new DdsCodec();
        var planResult = ConversionPlanner.Plan(ktx2Reader.Describe(), ddsCodec.Capabilities, ConversionPolicy.Preview, encodedFormat);
        Assert.True(planResult.Success);
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, planResult.Plan!.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, ktx2Reader, ktx2Codec.Capabilities.SampleByteOrder, ddsWriter, ddsCodec.Capabilities.SampleByteOrder);

        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);
        var actual = new byte[BcImageCodec.EncodedByteCount(bcFormat, width, height)];
        ddsReader.Read(region, actual);
        var expected = new byte[actual.Length];
        BcImageCodec.Encode(bcFormat, source, width, height, expected);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("R10G10B10A2", 4)]
    [InlineData("R11G11B10Float", 4)]
    [InlineData("Rgb9E5", 4)]
    public void Execute_Ktx2Raw_ToPacked_EncodesAndDecodesThroughPlanner(string formatName, int channelCount)
    {
        const int width = 17;
        const int height = 9;
        var descriptor = PlainKtxDescriptor(width, height, channelCount);
        var source = new byte[width * height * channelCount];
        new Random(401 + channelCount).NextBytes(source);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var ktx2Codec = new Ktx2Codec();
        using var sourceStream = new MemoryStream();
        var sourceWriter = ktx2Codec.CreateWriter(sourceStream, descriptor);
        sourceWriter.Write(region, source);
        sourceWriter.Finish();
        sourceStream.Position = 0;
        var sourceReader = ktx2Codec.OpenReader(sourceStream);
        var format = formatName switch {
            "R10G10B10A2" => Lucitex.Core.Representation.EncodedFormatId.R10G10B10A2,
            "R11G11B10Float" => Lucitex.Core.Representation.EncodedFormatId.R11G11B10Float,
            "Rgb9E5" => Lucitex.Core.Representation.EncodedFormatId.Rgb9E5,
            _ => throw new ArgumentOutOfRangeException(nameof(formatName)),
        };

        var planResult = ConversionPlanner.Plan(sourceReader.Describe(), ktx2Codec.Capabilities, ConversionPolicy.Preview, format);
        Assert.True(planResult.Success);
        Assert.Contains(planResult.Plan!.Parts[0].Steps, step => step is EncodeEncodedElementsStep encode && encode.Format == format);
        using var packedStream = new MemoryStream();
        var packedWriter = ktx2Codec.CreateWriter(packedStream, planResult.Plan.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, sourceReader, ktx2Codec.Capabilities.SampleByteOrder, packedWriter, ktx2Codec.Capabilities.SampleByteOrder);

        var red = new float[width * height];
        var green = new float[width * height];
        var blue = new float[width * height];
        var alpha = format == Lucitex.Core.Representation.EncodedFormatId.R10G10B10A2 ? new float[width * height] : [];
        for (var pixel = 0; pixel < red.Length; pixel++) {
            red[pixel] = source[pixel * channelCount] / 255f;
            green[pixel] = source[(pixel * channelCount) + 1] / 255f;
            blue[pixel] = source[(pixel * channelCount) + 2] / 255f;
            if (alpha.Length > 0) {
                alpha[pixel] = source[(pixel * channelCount) + 3] / 255f;
            }
        }

        var expectedPacked = new byte[width * height * 4];
        PackedPixelConversionKernel.Encode(format, red, green, blue, alpha, expectedPacked);
        packedStream.Position = 0;
        var packedReader = ktx2Codec.OpenReader(packedStream);
        var actualPacked = new byte[expectedPacked.Length];
        packedReader.Read(region, actualPacked);
        Assert.Equal(expectedPacked, actualPacked);

        var hdrCodec = new HdrCodec();
        var decodePlan = ConversionPlanner.Plan(packedReader.Describe(), hdrCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(decodePlan.Success);
        Assert.Contains(decodePlan.Plan!.Parts[0].Steps, step => step is DecodeEncodedElementsStep decode && decode.Format == format);
        using var hdrStream = new MemoryStream();
        var hdrWriter = hdrCodec.CreateWriter(hdrStream, decodePlan.Plan.TargetDescriptor);
        ConversionExecutor.Execute(decodePlan.Plan, packedReader, ktx2Codec.Capabilities.SampleByteOrder, hdrWriter, hdrCodec.Capabilities.SampleByteOrder);
        hdrStream.Position = 0;
        var hdrReader = hdrCodec.OpenReader(hdrStream);
        var actualRgbe = new byte[width * height * 4];
        hdrReader.Read(region, actualRgbe);
        var decodedRed = new float[red.Length];
        var decodedGreen = new float[red.Length];
        var decodedBlue = new float[red.Length];
        PackedPixelConversionKernel.Decode(format, expectedPacked, decodedRed, decodedGreen, decodedBlue, alpha);
        var expectedRgbe = new byte[actualRgbe.Length];
        RgbeConversionKernel.Encode(decodedRed, decodedGreen, decodedBlue, expectedRgbe);
        Assert.Equal(expectedRgbe, actualRgbe);
    }

    [Theory]
    [InlineData("Bc6H")]
    [InlineData("Bc6HSigned")]
    public void Plan_ExplicitBc6HEncode_FailsWithSpecificDiagnostic(string formatName)
    {
        var format = formatName == "Bc6H"
            ? Lucitex.Core.Representation.EncodedFormatId.Bc6H
            : Lucitex.Core.Representation.EncodedFormatId.Bc6HSigned;
        var result = ConversionPlanner.Plan(PlainKtxDescriptor(8, 8, 3), new DdsCodec().Capabilities, ConversionPolicy.Preview, format);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message == $"No encoder is available for encoded format '{format}'.");
    }

    [Theory]
    [InlineData("B5G6R5")]
    [InlineData("B5G5R5A1")]
    public void Execute_Ktx2Raw_ToDdsBgr16_UsesPackedEncoder(string formatName)
    {
        const int width = 19;
        const int height = 11;
        const int channelCount = 4;
        var descriptor = PlainKtxDescriptor(width, height, channelCount);
        var source = new byte[width * height * channelCount];
        new Random(431).NextBytes(source);
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = ImageBox.FromOrigin(width, height) };
        var ktx2Codec = new Ktx2Codec();
        using var sourceStream = new MemoryStream();
        var sourceWriter = ktx2Codec.CreateWriter(sourceStream, descriptor);
        sourceWriter.Write(region, source);
        sourceWriter.Finish();
        sourceStream.Position = 0;
        var sourceReader = ktx2Codec.OpenReader(sourceStream);
        var format = formatName == "B5G6R5"
            ? Lucitex.Core.Representation.EncodedFormatId.B5G6R5
            : Lucitex.Core.Representation.EncodedFormatId.B5G5R5A1;

        var ddsCodec = new DdsCodec();
        var planResult = ConversionPlanner.Plan(sourceReader.Describe(), ddsCodec.Capabilities, ConversionPolicy.Preview, format);
        Assert.True(planResult.Success);
        using var ddsStream = new MemoryStream();
        var ddsWriter = ddsCodec.CreateWriter(ddsStream, planResult.Plan!.TargetDescriptor);
        ConversionExecutor.Execute(planResult.Plan, sourceReader, ktx2Codec.Capabilities.SampleByteOrder, ddsWriter, ddsCodec.Capabilities.SampleByteOrder);

        var red = new float[width * height];
        var green = new float[red.Length];
        var blue = new float[red.Length];
        var alpha = format == Lucitex.Core.Representation.EncodedFormatId.B5G5R5A1 ? new float[red.Length] : [];
        for (var pixel = 0; pixel < red.Length; pixel++) {
            red[pixel] = source[pixel * channelCount] / 255f;
            green[pixel] = source[(pixel * channelCount) + 1] / 255f;
            blue[pixel] = source[(pixel * channelCount) + 2] / 255f;
            if (alpha.Length > 0) {
                alpha[pixel] = source[(pixel * channelCount) + 3] / 255f;
            }
        }

        var expected = new byte[width * height * 2];
        PackedPixelConversionKernel.Encode(format, red, green, blue, alpha, expected);
        ddsStream.Position = 0;
        var ddsReader = ddsCodec.OpenReader(ddsStream);
        var actual = new byte[expected.Length];
        ddsReader.Read(region, actual);
        Assert.Equal(expected, actual);
        var representation = Assert.IsType<Lucitex.Core.Representation.EncodedElementRepresentation>(ddsReader.Describe().Parts[0].Representation);
        Assert.Equal(format, representation.Format);

        var hdrCodec = new HdrCodec();
        var decodePlan = ConversionPlanner.Plan(ddsReader.Describe(), hdrCodec.Capabilities, ConversionPolicy.Preview);
        Assert.True(decodePlan.Success);
        using var hdrStream = new MemoryStream();
        var hdrWriter = hdrCodec.CreateWriter(hdrStream, decodePlan.Plan!.TargetDescriptor);
        ConversionExecutor.Execute(decodePlan.Plan, ddsReader, ddsCodec.Capabilities.SampleByteOrder, hdrWriter, hdrCodec.Capabilities.SampleByteOrder);
        var decodedRed = new float[red.Length];
        var decodedGreen = new float[red.Length];
        var decodedBlue = new float[red.Length];
        PackedPixelConversionKernel.Decode(format, expected, decodedRed, decodedGreen, decodedBlue, alpha);
        var expectedRgbe = new byte[red.Length * 4];
        RgbeConversionKernel.Encode(decodedRed, decodedGreen, decodedBlue, expectedRgbe);
        hdrStream.Position = 0;
        var hdrReader = hdrCodec.OpenReader(hdrStream);
        var actualRgbe = new byte[expectedRgbe.Length];
        hdrReader.Read(region, actualRgbe);
        Assert.Equal(expectedRgbe, actualRgbe);
    }

    [Fact]
    public void Plan_Rgb_ToBc1_SynthesizesOpaqueAlpha()
    {
        var descriptor = PlainKtxDescriptor(8, 8, 3);
        var result = ConversionPlanner.Plan(
            descriptor,
            new DdsCodec().Capabilities,
            ConversionPolicy.Preview,
            Lucitex.Core.Representation.EncodedFormatId.Bc1);

        Assert.True(result.Success);
        Assert.Contains(result.Plan!.Parts[0].Steps, step => step is SynthesizeChannelStep { Channel.FullName: "A", ConstantValue: 1 });
        Assert.Equal(["R", "G", "B", "A"], result.Plan.TargetDescriptor.Parts[0].Channels.Channels.Select(channel => channel.Name.FullName));
    }

    private static Lucitex.Core.Semantic.ImageAssetDescriptor Resize(Lucitex.Core.Semantic.ImageAssetDescriptor descriptor, int width, int height)
    {
        var part = descriptor.Parts[0];
        var extent = new Extent3L(width, height, 1);
        var window = ImageBox.FromOrigin(width, height);
        return descriptor with {
            Parts =
            [
                part with {
                    Spatial = part.Spatial with { DataWindow = window, DisplayWindow = window },
                    Topology = part.Topology with { BaseExtent = extent, Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = extent }] },
                    Representation = part.Representation is Lucitex.Core.Representation.PlainSampleRepresentation plain
                        ? plain with { Planes = plain.Planes.Select(plane => plane with { Extent = extent }).ToList() }
                        : part.Representation,
                },
            ],
        };
    }

    private static Lucitex.Core.Semantic.ImageAssetDescriptor PlainKtxDescriptor(int width, int height, int channelCount)
    {
        var resized = Resize(Ktx2Fixtures.Rgba8(), width, height);
        var part = resized.Parts[0];
        var channels = part.Channels.Channels.Take(channelCount).ToList();
        return resized with {
            Parts =
            [
                part with {
                    Channels = new Lucitex.Core.Sampling.ChannelSchema { Channels = channels },
                    Representation = new Lucitex.Core.Representation.PlainSampleRepresentation {
                        Planes =
                        [
                            new Lucitex.Core.Representation.SamplePlaneDescriptor {
                                Channels = channels.Select(channel => channel.Name).ToList(),
                                Extent = new Extent3L(width, height, 1),
                                Layout = Lucitex.Core.Representation.PlaneLayout.Interleaved,
                            },
                        ],
                    },
                },
            ],
        };
    }
}
