using System.Buffers.Binary;
using Lucitex.Core.Execution;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;

namespace Lucitex.Tests.Exr;

public class PizExrTests
{
    private static ImageAssetDescriptor Asset(long width, long height, SampleType sampleType, params string[] channelNames)
    {
        var window = ImageBox.FromOrigin(width, height);

        var part = new ImagePartDescriptor {
            Name = "piz",
            Spatial = new SpatialDomain { DataWindow = window, DisplayWindow = window },
            Topology = new ResourceTopology {
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema {
                Channels = channelNames.Select(name => new ChannelDescriptor {
                    Name = name,
                    SampleType = sampleType,
                    Sampling = SampleGrid.Unit,
                }).ToList(),
            },
            Representation = new PlainSampleRepresentation {
                Planes = channelNames.Select(name => new SamplePlaneDescriptor {
                    Channels = [name],
                    Extent = new Extent3L(width, height, 1),
                }).ToList(),
            },
        };

        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static byte[] Gradient(ExrHeader header, Func<int, int, int, float> sample)
    {
        var width = (int)header.DataWindow.Width;
        var height = (int)header.DataWindow.Height;
        var rowStride = header.Channels.Sum(c => width * c.BytesPerSample);
        var data = new byte[rowStride * height];

        for (var y = 0; y < height; y++) {
            var offset = y * rowStride;

            for (var channel = 0; channel < header.Channels.Count; channel++) {
                var info = header.Channels[channel];

                for (var x = 0; x < width; x++) {
                    var value = sample(x, y, channel);
                    var target = data.AsSpan(offset + (x * info.BytesPerSample));

                    switch (info.PixelType) {
                        case ExrPixelType.Half:
                            BinaryPrimitives.WriteHalfLittleEndian(target, (Half)value);
                            break;
                        case ExrPixelType.Float:
                            BinaryPrimitives.WriteSingleLittleEndian(target, value);
                            break;
                        default:
                            BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)value);
                            break;
                    }
                }

                offset += width * info.BytesPerSample;
            }
        }

        return data;
    }

    private static (byte[] Decoded, long CompressedLength, long UncompressedLength) RoundTrip(
        ImageAssetDescriptor asset,
        byte[] source)
    {
        var window = asset.Parts[0].Spatial.DataWindow;
        var region = new WorkRegion { Subresource = new SubresourceId(0, 0, 0, LevelKey.Base), Region = window };

        long Write(ExrCompressionId compression)
        {
            using var target = new MemoryStream();
            var writer = new ExrCodec(compression).CreateWriter(target, asset);
            writer.Write(region, source);
            writer.Finish();
            return target.Length;
        }

        var codec = new ExrCodec(ExrCompressionId.Piz);
        using var stream = new MemoryStream();

        var pizWriter = codec.CreateWriter(stream, asset);
        pizWriter.Write(region, source);
        pizWriter.Finish();

        stream.Position = 0;
        var reader = codec.OpenReader(stream);
        var decoded = new byte[source.Length];
        var read = reader.Read(region, decoded);

        Assert.Equal(source.Length, read);
        return (decoded, stream.Length, Write(ExrCompressionId.None));
    }

    [Theory]
    [InlineData(64, 32)]
    [InlineData(37, 23)]
    public void RoundTrip_HalfGradient_IsLosslessAndSmallerThanUncompressed(int width, int height)
    {
        var asset = Asset(width, height, SampleType.Float16, "R", "G", "B", "A");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (x, y, channel) => ((x * 0.01f) + (y * 0.02f) + channel) * 0.5f);

        var (decoded, compressed, uncompressed) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
        Assert.True(compressed < uncompressed, $"PIZ produced {compressed} bytes; uncompressed is {uncompressed}.");
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(64, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 2)]
    [InlineData(2, 5)]
    public void RoundTrip_DegenerateExtents_IsLossless(int width, int height)
    {
        var asset = Asset(width, height, SampleType.Float16, "R", "G", "B", "A");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (x, y, channel) => ((x * 0.01f) + (y * 0.02f) + channel) * 0.5f);

        var (decoded, _, _) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
    }

    [Fact]
    public void RoundTrip_FloatGradient_IsLossless()
    {
        var asset = Asset(48, 40, SampleType.Float32, "R", "G", "B");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (x, y, channel) => (x + (y * 48f) + (channel * 2048f)) / 4096f);

        var (decoded, compressed, uncompressed) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
        Assert.True(compressed < uncompressed, $"PIZ produced {compressed} bytes; uncompressed is {uncompressed}.");
    }

    [Fact]
    public void RoundTrip_UIntChannel_IsLossless()
    {
        var asset = Asset(33, 17, SampleType.UInt32, "Z");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (x, y, _) => (x * 4) + y);

        var (decoded, _, _) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
    }

    [Fact]
    public void RoundTrip_ConstantImage_UsesRunLengthCodesAndStaysLossless()
    {
        var asset = Asset(128, 96, SampleType.Float16, "Y");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (_, _, _) => 0.25f);

        var (decoded, compressed, uncompressed) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
        Assert.True(compressed * 8 < uncompressed, $"PIZ produced {compressed} bytes for a constant image; uncompressed is {uncompressed}.");
    }

    [Fact]
    public void RoundTrip_SingleValuePerChannel_IsLossless()
    {
        var asset = Asset(5, 4, SampleType.Float16, "R", "G");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (_, _, channel) => channel);

        var (decoded, _, _) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
    }

    private static byte[] RawPatterns(int width, int height, Func<int, int, ushort> pattern)
    {
        var data = new byte[width * height * 2];

        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(((y * width) + x) * 2), pattern(x, y));
            }
        }

        return data;
    }

    [Fact]
    public void RoundTrip_MoreDistinctValuesThanTheNarrowWaveletRange_IsLossless()
    {
        const int width = 200, height = 180;

        var asset = Asset(width, height, SampleType.Float16, "Y");
        var source = RawPatterns(width, height, (x, y) => (ushort)(((y * width) + x) % 65536));

        var (decoded, _, _) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
    }

    [Fact]
    public void RoundTrip_TallImageSpanningManyChunks_IsLossless()
    {
        var asset = Asset(70, 200, SampleType.Float16, "R", "G", "B");
        var header = ExrDescriptorMapper.ToExrHeader(asset, ExrCompressionId.Piz);
        var source = Gradient(header, (x, y, channel) => MathF.Sin((x + (y * 0.5f) + channel) * 0.05f));

        var (decoded, compressed, uncompressed) = RoundTrip(asset, source);

        Assert.Equal(source, decoded);
        Assert.True(compressed < uncompressed, $"PIZ produced {compressed} bytes; uncompressed is {uncompressed}.");
    }
}
