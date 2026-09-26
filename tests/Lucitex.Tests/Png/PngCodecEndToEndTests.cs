using System.Buffers.Binary;
using Lucitex.Core.Execution;
using Lucitex.Core.Metadata;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Png;
using Lucitex.Tests.Fixtures;

namespace Lucitex.Tests.Png;

public class PngCodecEndToEndTests
{
    private static WorkRegion FullRegion(long width, long height) => new() {
        Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
        Region = ImageBox.FromOrigin(width, height),
    };

    private static byte[] RandomRowBytes(Lucitex.Core.Semantic.ImagePartDescriptor part, int seed)
    {
        var document = PngDescriptorMapper.ToPngDocument(part);
        var rowBytes = document.Ihdr.RowByteLength(document.Ihdr.Width);
        var buffer = new byte[rowBytes * document.Ihdr.Height];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    private static byte[] RoundTrip(Lucitex.Core.Semantic.ImageAssetDescriptor asset, byte[] source)
    {
        var part = asset.Parts[0];
        var width = part.Topology.BaseExtent.Width;
        var height = part.Topology.BaseExtent.Height;

        var codec = new PngCodec();
        using var stream = new MemoryStream();

        var writer = codec.CreateWriter(stream, asset);
        writer.Write(FullRegion(width, height), source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var destination = new byte[source.Length];
        var readCount = reader.Read(FullRegion(width, height), destination);

        Assert.Equal(source.Length, readCount);
        return destination;
    }

    private static byte[] Encode(Lucitex.Core.Semantic.ImageAssetDescriptor asset, byte[] source)
    {
        var part = asset.Parts[0];
        using var stream = new MemoryStream();
        using var writer = new PngCodec().CreateWriter(stream, asset);
        writer.Write(FullRegion(part.Topology.BaseExtent.Width, part.Topology.BaseExtent.Height), source);
        writer.Finish();
        return stream.ToArray();
    }

    private static int FirstIdatDeflateBlockType(ReadOnlySpan<byte> png)
    {
        var offset = 8;
        while (offset <= png.Length - 12) {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]));
            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8)) {
                return (png[offset + 10] >> 1) & 0b11;
            }
            offset = checked(offset + length + 12);
        }
        throw new InvalidDataException("PNG has no IDAT chunk.");
    }

    [Fact]
    public void RoundTrip_Rgba8_PreservesPixelsAndDescriptor()
    {
        var asset = PngFixtures.Rgba8();
        var source = RandomRowBytes(asset.Parts[0], 1);

        var destination = RoundTrip(asset, source);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void DefaultCompression_UsesStoredDeflateForHighEntropyAndCompressesFlatImages()
    {
        var asset = PngFixtures.Rgba8(256, 256);
        var noise = RandomRowBytes(asset.Parts[0], 21);
        var flat = new byte[noise.Length];
        for (var offset = 0; offset < flat.Length; offset += 4) {
            flat[offset] = 127;
            flat[offset + 1] = 127;
            flat[offset + 2] = 127;
            flat[offset + 3] = 255;
        }

        var noisePng = Encode(asset, noise);
        var flatPng = Encode(asset, flat);

        Assert.Equal(0, FirstIdatDeflateBlockType(noisePng));
        Assert.NotEqual(0, FirstIdatDeflateBlockType(flatPng));
        Assert.Equal(noise, RoundTrip(asset, noise));
        Assert.Equal(flat, RoundTrip(asset, flat));
    }

    [Fact]
    public void StoredDeflate_WritesToExactlySizedFixedCapacityStream()
    {
        var asset = PngFixtures.Rgba8(256, 256);
        var source = RandomRowBytes(asset.Parts[0], 29);
        var expected = Encode(asset, source);
        var destination = new byte[expected.Length];
        using var stream = new MemoryStream(destination, writable: true);
        using var writer = new PngCodec().CreateWriter(stream, asset);
        writer.Write(FullRegion(256, 256), source);
        writer.Finish();
        Assert.Equal(expected.Length, stream.Position);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void RoundTrip_Rgb16_PreservesPixels()
    {
        var asset = PngFixtures.Rgb16();
        var source = RandomRowBytes(asset.Parts[0], 2);

        var destination = RoundTrip(asset, source);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_Grayscale1Bit_PreservesPackedBits()
    {
        var asset = PngFixtures.Grayscale1Bit();
        var source = RandomRowBytes(asset.Parts[0], 3);

        var destination = RoundTrip(asset, source);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void RoundTrip_Palette4Bit_PreservesIndicesAndPalette()
    {
        var asset = PngFixtures.Palette4Bit();
        var paletteBytes = new byte[16 * 3];
        for (var i = 0; i < 16; i++) {
            paletteBytes[(i * 3) + 0] = (byte)(i * 16);
            paletteBytes[(i * 3) + 1] = (byte)(255 - (i * 16));
            paletteBytes[(i * 3) + 2] = (byte)i;
        }

        var partWithPalette = asset.Parts[0] with {
            Metadata = new MetadataCollection {
                Entries = [new MetadataEntry { Namespace = "png", Name = "PLTE", RawRepresentation = paletteBytes }],
            },
        };
        var withPalette = asset with { Parts = [partWithPalette] };

        var source = RandomRowBytes(partWithPalette, 4);

        var destination = RoundTrip(withPalette, source);

        Assert.Equal(source, destination);

        var codec = new PngCodec();
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, withPalette);
        var width = partWithPalette.Topology.BaseExtent.Width;
        var height = partWithPalette.Topology.BaseExtent.Height;
        writer.Write(FullRegion(width, height), source);
        writer.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        var plte = described.Parts[0].Metadata.Entries.First(e => e.Namespace == "png" && e.Name == "PLTE");
        Assert.Equal(paletteBytes, plte.RawRepresentation);
    }

    [Fact]
    public void Probe_RecognizesPngSignature()
    {
        var codec = new PngCodec();
        var result = codec.Probe([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.Equal(Lucitex.Core.Execution.Codecs.ProbeConfidence.Certain, result.Confidence);
        Assert.Equal("png", result.Format);
    }

    [Fact]
    public void CodecRegistry_ResolvesPngBySignature()
    {
        var registry = new Lucitex.Core.Execution.Codecs.CodecRegistry();
        registry.Register(new PngCodec());

        var resolved = registry.Resolve([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.Equal("png", resolved.FormatId);
    }
}
