using Lucitex.Benchmarks.Data;
using SkiaSharp;

namespace Lucitex.Benchmarks.Codecs;

internal sealed unsafe class SkiaSharpAdapter : CodecAdapter
{
    public override byte[] Encode(TestImage source, ComparisonCase comparison)
    {
        var rgba = source.Channels == 4 ? source.Pixels : PixelLayout.ToRgba(source.Pixels);
        fixed (byte* pixels = rgba) {
            using var pixmap = new SKPixmap(Layout(source), (nint)pixels);
            return Save(pixmap, comparison);
        }
    }

    public override byte[] Convert(byte[] encoded, TestImage layout, ComparisonCase comparison)
    {
        fixed (byte* input = encoded) {
            using var data = SKData.Create((nint)input, encoded.Length);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("SkiaSharp failed to open input.");
            ValidateDimensions(codec, layout);
            using var bitmap = new SKBitmap(Layout(layout));
            EnsureDecoded(codec.GetPixels(bitmap.Info, bitmap.GetPixels()));
            using var pixels = bitmap.PeekPixels();
            return Save(pixels, comparison);
        }
    }

    public override void Decode(byte[] encoded, TestImage layout, byte[] destination)
    {
        if (destination.Length != checked(layout.Width * layout.Height * layout.Channels))
            throw new ArgumentException("Destination does not match the requested layout.", nameof(destination));
        fixed (byte* input = encoded) {
            using var data = SKData.Create((nint)input, encoded.Length);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("SkiaSharp failed to open input.");
            ValidateDimensions(codec, layout);
            var rgba = layout.Channels == 4 ? destination : new byte[checked(layout.Width * layout.Height * 4)];
            EnsureDecoded(codec.GetPixels(Layout(layout), rgba));
            if (layout.Channels != 4) {
                for (var i = 0; i < layout.Width * layout.Height; i++) {
                    rgba.AsSpan(i * 4, 3).CopyTo(destination.AsSpan(i * 3));
                }
            }
        }
    }

    private static void ValidateDimensions(SKCodec codec, TestImage layout)
    {
        var info = codec.Info;
        if (info.Width != layout.Width || info.Height != layout.Height)
            throw new InvalidDataException("SkiaSharp dimensions differ from source.");
    }

    private static void EnsureDecoded(SKCodecResult result)
    {
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"SkiaSharp failed to decode: {result}.");
    }

    private static SKImageInfo Layout(TestImage source) => new(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    private static byte[] Save(SKPixmap pixels, ComparisonCase comparison)
    {
        if (comparison.Format == "webp") {
            using var lossless = pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, comparison.Settings.WebpQuality))
                ?? throw new InvalidDataException("SkiaSharp failed to encode lossless WebP.");
            return lossless.ToArray();
        }
        if (comparison.Format == "png" && comparison.Settings.PngLevel is { } level) {
            using var png = pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, level))
                ?? throw new InvalidDataException("SkiaSharp failed to encode PNG.");
            return png.ToArray();
        }
        using var data = pixels.Encode(comparison.Format == "jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, comparison.EncoderQuality)
            ?? throw new InvalidDataException("SkiaSharp failed to encode.");
        return data.ToArray();
    }
}
