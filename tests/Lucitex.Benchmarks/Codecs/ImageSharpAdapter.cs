using Lucitex.Benchmarks.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class ImageSharpAdapter : CodecAdapter
{
    public override byte[] Encode(TestImage source, ComparisonCase comparison)
    {
        using Image image = source.Channels == 3
            ? Image.LoadPixelData<Rgb24>(source.Pixels, source.Width, source.Height)
            : Image.LoadPixelData<Rgba32>(source.Pixels, source.Width, source.Height);
        return Save(image, comparison);
    }

    public override byte[] Convert(byte[] encoded, TestImage layout, ComparisonCase comparison)
    {
        using Image image = layout.Channels == 3 ? Image.Load<Rgb24>(encoded) : Image.Load<Rgba32>(encoded);
        return Save(image, comparison);
    }

    public override void Decode(byte[] encoded, TestImage layout, byte[] destination)
    {
        if (layout.Channels == 3) {
            using var image = Image.Load<Rgb24>(encoded);
            image.CopyPixelDataTo(destination);
        }
        else {
            using var image = Image.Load<Rgba32>(encoded);
            image.CopyPixelDataTo(destination);
        }
    }

    private static byte[] Save(Image image, ComparisonCase comparison)
    {
        IImageEncoder encoder = comparison.Format == "jpeg"
            ? new JpegEncoder {
                Quality = ComparisonCase.Quality,
                ColorType = comparison.Is444 ? JpegEncodingColor.YCbCrRatio444 : JpegEncodingColor.YCbCrRatio420,
            }
            : comparison.Format == "webp" ? new WebpEncoder {
                FileFormat = WebpFileFormatType.Lossless, Quality = 100, Method = WebpEncodingMethod.Level4,
                NearLossless = false, TransparentColorMode = WebpTransparentColorMode.Preserve,
            } : new PngEncoder {
                ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8,
                FilterMethod = comparison.Profile switch { "png-none" => PngFilterMethod.None, "png-paeth" => PngFilterMethod.Paeth, _ => PngFilterMethod.Adaptive },
            };
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
