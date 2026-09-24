using Lucitex.Benchmarks.Data;
using NetVips;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class NetVipsAdapter : CodecAdapter
{
    public NetVipsAdapter()
    {
        Cache.Max = 0;
    }

    public override byte[] Encode(TestImage source, ComparisonCase comparison)
    {
        using var image = Image.NewFromMemory(source.Pixels, source.Width, source.Height, source.Channels, Enums.BandFormat.Uchar);
        using var tagged = image.Copy(interpretation: Enums.Interpretation.Srgb);
        return Save(tagged, comparison);
    }

    public override byte[] Convert(byte[] encoded, TestImage layout, ComparisonCase comparison)
    {
        using var image = Image.NewFromBuffer(encoded, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
        using var normalized = Channels(image, layout.Channels);
        return Save(normalized, comparison);
    }

    public override void Decode(byte[] encoded, TestImage layout, byte[] destination)
    {
        using var image = Image.NewFromBuffer(encoded, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
        if (image.Width != layout.Width || image.Height != layout.Height) {
            throw new InvalidDataException("NetVips dimensions differ.");
        }
        using var normalized = Channels(image, layout.Channels);
        var pixels = normalized.WriteToMemory<byte>();
        if (pixels.Length != destination.Length) {
            throw new InvalidDataException("NetVips returned an unexpected pixel layout.");
        }
        pixels.CopyTo(destination, 0);
    }

    private static Image Channels(Image image, int channels)
    {
        using var color = image.Bands < 3 ? image.Colourspace(Enums.Interpretation.Srgb) : image.Copy();
        return color.Bands == channels ? color.Copy()
            : channels == 4 && color.Bands == 3 ? color.Bandjoin(255) : color.ExtractBand(0, n: channels);
    }

    private static byte[] Save(Image image, ComparisonCase comparison) => comparison.Format == "jpeg"
        ? image.JpegsaveBuffer(q: ComparisonCase.Quality, optimizeCoding: comparison.Profile == "jpeg-default420" ? null : comparison.OptimizeHuffman,
            interlace: comparison.Progressive, subsampleMode: comparison.Is444 ? Enums.ForeignSubsample.Off : Enums.ForeignSubsample.On,
            keep: Enums.ForeignKeep.None)
        : comparison.Format == "webp" ? image.WebpsaveBuffer(lossless: true, nearLossless: false, exact: true, q: 100, effort: 4, keep: Enums.ForeignKeep.None)
        : image.PngsaveBuffer(bitdepth: 8, palette: false,
            filter: comparison.Profile switch { "png-none" => Enums.ForeignPngFilter.None, "png-paeth" => Enums.ForeignPngFilter.Paeth, _ => null },
            keep: Enums.ForeignKeep.None);
}
