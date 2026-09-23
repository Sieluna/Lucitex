using ImageMagick;
using ImageMagick.Formats;
using Lucitex.Benchmarks.Data;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class MagickNetAdapter(bool fancyUpsampling = true) : CodecAdapter
{
    public override byte[] Encode(TestImage source, ComparisonCase comparison)
    {
        using var image = new MagickImage();
        var settings = new PixelReadSettings((uint)source.Width, (uint)source.Height,
            StorageType.Char, source.Channels == 3 ? PixelMapping.RGB : PixelMapping.RGBA);
        settings.ReadSettings.ColorSpace = comparison.Format == "exr" ? ColorSpace.RGB : ColorSpace.sRGB;
        image.ReadPixels(source.Pixels, settings);
        return Save(image, comparison);
    }

    public override byte[] Convert(byte[] encoded, TestImage layout, ComparisonCase comparison)
    {
        if (comparison.Format == "exr" || comparison.InputFormat == "exr") {
            return base.Convert(encoded, layout, comparison);
        }
        using var image = Load(encoded);
        return Save(image, comparison);
    }

    public override void Decode(byte[] encoded, TestImage layout, byte[] destination)
    {
        using var image = Load(encoded);
        if (image.Width != layout.Width || image.Height != layout.Height) {
            throw new InvalidDataException("Magick.NET dimensions differ.");
        }
        using var pixels = image.GetPixels();
        var bytes = pixels.ToByteArray(layout.Channels == 3 ? PixelMapping.RGB : PixelMapping.RGBA)
            ?? throw new InvalidDataException("Magick.NET did not return pixels.");
        if (bytes.Length != destination.Length) {
            throw new InvalidDataException("Magick.NET returned an unexpected pixel layout.");
        }
        bytes.CopyTo(destination, 0);
    }

    private MagickImage Load(byte[] encoded) => new(encoded, new MagickReadSettings {
        Defines = new JpegReadDefines { FancyUpsampling = fancyUpsampling, DctMethod = JpegDctMethod.Slow },
    });

    private static byte[] Save(MagickImage image, ComparisonCase comparison)
    {
        image.Strip();
        image.Depth = 8;
        if (comparison.Format == "exr") {
            image.Settings.SetDefine(MagickFormat.Exr, "color-type", "RGBA");
            image.Settings.Compression = comparison.Profile switch {
                "exr-half-none" => CompressionMethod.NoCompression,
                "exr-half-piz" => CompressionMethod.Piz,
                _ => CompressionMethod.Zip,
            };
            return image.ToByteArray(MagickFormat.Exr);
        }
        if (comparison.Format == "jpeg") {
            image.Quality = ComparisonCase.Quality;
            image.ColorType = ColorType.TrueColor;
            image.Settings.ColorType = ColorType.TrueColor;
            image.Settings.Interlace = comparison.Progressive ? Interlace.Plane : Interlace.NoInterlace;
            image.Settings.SetDefines(new JpegWriteDefines {
                SamplingFactor = comparison.Is444 ? JpegSamplingFactor.Ratio444 : JpegSamplingFactor.Ratio420,
                OptimizeCoding = comparison.Profile == "jpeg-default420" ? null : comparison.OptimizeHuffman, DctMethod = JpegDctMethod.Slow,
            });
            return image.ToByteArray(MagickFormat.Jpeg);
        }
        image.Settings.ColorType = ColorType.TrueColorAlpha;
        image.Settings.SetDefines(new PngWriteDefines {
            BitDepth = 8, ColorType = ColorType.TrueColorAlpha,
            CompressionFilter = comparison.Profile switch { "png-none" => PngCompressionFilter.None, "png-paeth" => PngCompressionFilter.Paeth, _ => null },
            ExcludeChunks = PngChunkFlags.All,
        });
        return image.ToByteArray(MagickFormat.Png32);
    }
}
