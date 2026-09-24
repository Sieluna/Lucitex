using System.IO.Compression;
using Lucitex.Benchmarks.Data;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Jpeg;
using Lucitex.Png;
using Lucitex.Webp;
using Lucitex.Core.Execution;
using Lucitex.Png.Format;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Ktx2;
using Lucitex.Ktx2.Format;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class LucitexAdapter : CodecAdapter
{
    public override byte[] Encode(TestImage source, ComparisonCase comparison)
    {
        var descriptor = ImageLayout.Describe(source, comparison.Format == "exr");
        using var stream = new MemoryStream();
        using (var writer = comparison.Format switch {
            "jpeg" => new JpegCodec().CreateWriter(stream, descriptor, new JpegEncoderOptions {
                Quality = ComparisonCase.Quality, OptimizeHuffmanTables = comparison.OptimizeHuffman,
                Progressive = comparison.Progressive,
                ChromaSubsampling = comparison.Is444 ? JpegChromaSubsampling.Ratio444 : JpegChromaSubsampling.Ratio420,
            }),
            "png" => new PngCodec().CreateWriter(stream, descriptor, new PngEncoderOptions {
                CompressionLevel = CompressionLevel.Optimal,
                Filter = comparison.Profile switch { "png-none" => PngFilterType.None, "png-paeth" => PngFilterType.Paeth, _ => null },
            }),
            "exr" => new ExrCodec(comparison.Profile switch {
                "exr-half-none" => ExrCompressionId.None,
                "exr-half-piz" => ExrCompressionId.Piz,
                _ => ExrCompressionId.Zip,
            }).CreateWriter(stream, descriptor),
            "ktx2" => new Ktx2Codec().CreateWriter(stream, descriptor,
                comparison.Profile == "ktx2-rgba8-zlib" ? Ktx2SupercompressionScheme.Zlib : Ktx2SupercompressionScheme.None),
            "webp" => new WebpCodec().CreateWriter(stream, descriptor),
            _ => throw new NotSupportedException(comparison.Format),
        }) {
            writer.Write(ImageLayout.Full(source), comparison.Format == "exr" ? ExrPixels.Pack(source) : source.Pixels);
            writer.Finish();
        }
        return stream.ToArray();
    }

    public override void Decode(byte[] encoded, TestImage layout, byte[] destination)
    {
        var jpeg = encoded[0] == 255 && encoded[1] == 216;
        var exr = encoded[0] == 0x76;
        var ktx2 = encoded[0] == 0xAB;
        var webp = encoded.AsSpan(0, 4).SequenceEqual("RIFF"u8);
        using var stream = new MemoryStream(encoded, false);
        using var reader = jpeg
            ? new JpegCodec().OpenReader(stream, new JpegDecoderOptions { InterpolateChroma = layout.Width > 4 })
            : exr ? new ExrCodec().OpenReader(stream)
            : ktx2 ? new Ktx2Codec().OpenReader(stream)
            : webp ? new WebpCodec().OpenReader(stream, DecodeLimits.Default with { MaxCompressionRatio = double.PositiveInfinity })
            : new PngCodec().OpenReader(stream);
        var extent = reader.Describe().Parts[0].Topology.BaseExtent;
        if (extent.Width != layout.Width || extent.Height != layout.Height) {
            throw new InvalidDataException("Lucitex dimensions differ from source.");
        }
        if (exr) {
            var half = new byte[checked(layout.Width * layout.Height * 8)];
            if (reader.Read(ImageLayout.Full(layout), half) != half.Length) {
                throw new InvalidDataException("Lucitex returned incomplete EXR samples.");
            }
            ExrPixels.Unpack(layout, half, destination);
            return;
        }
        var sourceChannels = jpeg ? 3 : 4;
        var raw = sourceChannels == layout.Channels ? destination : new byte[checked(layout.Width * layout.Height * sourceChannels)];
        if (reader.Read(ImageLayout.Full(layout), raw) != raw.Length) {
            throw new InvalidDataException("Lucitex returned an incomplete pixel buffer.");
        }
        if (sourceChannels != layout.Channels) {
            var converted = layout.Channels == 3 ? PixelLayout.ToRgb(raw) : PixelLayout.ToRgba(raw);
            converted.CopyTo(destination, 0);
        }
    }
}
