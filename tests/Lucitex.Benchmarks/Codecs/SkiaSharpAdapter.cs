using System.Runtime.InteropServices;
using Lucitex.Benchmarks.Data;
using SkiaSharp;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class SkiaSharpAdapter : CodecAdapter
{
    public override byte[] Encode(TestImage source, ComparisonCase comparison)
    {
        using var bitmap = new SKBitmap(Layout(source));
        var rgba = source.Channels == 4 ? source.Pixels : PixelLayout.ToRgba(source.Pixels);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        return Save(bitmap, comparison);
    }

    public override byte[] Convert(byte[] encoded, TestImage layout, ComparisonCase comparison)
    {
        using var bitmap = SKBitmap.Decode(encoded, Layout(layout)) ?? throw new InvalidDataException("SkiaSharp failed to decode.");
        return Save(bitmap, comparison);
    }

    public override void Decode(byte[] encoded, TestImage layout, byte[] destination)
    {
        using var bitmap = SKBitmap.Decode(encoded, Layout(layout)) ?? throw new InvalidDataException("SkiaSharp failed to decode.");
        if (layout.Channels == 4) {
            Marshal.Copy(bitmap.GetPixels(), destination, 0, destination.Length);
        }
        else {
            var rgba = new byte[checked(layout.Width * layout.Height * 4)];
            Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
            for (var i = 0; i < layout.Width * layout.Height; i++) {
                rgba.AsSpan(i * 4, 3).CopyTo(destination.AsSpan(i * 3));
            }
        }
    }

    private static SKImageInfo Layout(TestImage source) => new(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    private static byte[] Save(SKBitmap bitmap, ComparisonCase comparison)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(comparison.Format == "jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, ComparisonCase.Quality);
        return data.ToArray();
    }
}
