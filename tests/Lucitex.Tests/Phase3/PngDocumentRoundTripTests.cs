using Lucitex.Png.Format;

namespace Lucitex.Tests.Phase3;

public class PngDocumentRoundTripTests
{
    private static PngDocument BuildDocument() => new()
    {
        Ihdr = new PngIhdr(16, 8, 8, PngColorType.Indexed, PngInterlaceMethod.None),
        Palette =
        [
            new PngPaletteEntry(0, 0, 0),
            new PngPaletteEntry(255, 255, 255),
            new PngPaletteEntry(255, 0, 0),
        ],
        TransparencyData = [255, 128, 0],
        Gamma = 0.45455f,
        SrgbRenderingIntent = 0,
        Chromaticities = new PngChromaticities
        {
            White = new PngChromaticity(0.3127, 0.3290),
            Red = new PngChromaticity(0.6400, 0.3300),
            Green = new PngChromaticity(0.3000, 0.6000),
            Blue = new PngChromaticity(0.1500, 0.0600),
        },
        IccProfile = [1, 2, 3, 4, 5, 6, 7, 8],
        IccProfileName = "sRGB IEC61966-2.1",
        TextEntries =
        [
            new PngTextEntry { Keyword = "Author", Text = "Lucitex" },
        ],
    };

    [Fact]
    public void WriteThenRead_PreservesIhdrPaletteAndTransparency()
    {
        var document = BuildDocument();
        using var stream = new MemoryStream();

        PngDocumentWriter.Write(stream, document, "compressed-idat-placeholder"u8);
        stream.Position = 0;

        var (readDocument, idat) = PngDocumentReader.Read(stream);

        Assert.Equal(document.Ihdr, readDocument.Ihdr);
        Assert.Equal(document.Palette, readDocument.Palette);
        Assert.Equal(document.TransparencyData, readDocument.TransparencyData);
        Assert.Equal("compressed-idat-placeholder"u8.ToArray(), idat);
    }

    [Fact]
    public void WriteThenRead_PreservesGammaSrgbAndChromaticities()
    {
        var document = BuildDocument();
        using var stream = new MemoryStream();

        PngDocumentWriter.Write(stream, document, []);
        stream.Position = 0;

        var (readDocument, _) = PngDocumentReader.Read(stream);

        Assert.Equal(document.Gamma!.Value, readDocument.Gamma!.Value, 4);
        Assert.Equal(document.SrgbRenderingIntent, readDocument.SrgbRenderingIntent);
        Assert.NotNull(readDocument.Chromaticities);
        Assert.Equal(document.Chromaticities!.White.X, readDocument.Chromaticities!.White.X, 4);
        Assert.Equal(document.Chromaticities!.Red.Y, readDocument.Chromaticities!.Red.Y, 4);
    }

    [Fact]
    public void WriteThenRead_PreservesIccProfileBytes()
    {
        var document = BuildDocument();
        using var stream = new MemoryStream();

        PngDocumentWriter.Write(stream, document, []);
        stream.Position = 0;

        var (readDocument, _) = PngDocumentReader.Read(stream);

        Assert.Equal(document.IccProfile, readDocument.IccProfile);
    }

    [Fact]
    public void WriteThenRead_PreservesTextEntries()
    {
        var document = BuildDocument();
        using var stream = new MemoryStream();

        PngDocumentWriter.Write(stream, document, []);
        stream.Position = 0;

        var (readDocument, _) = PngDocumentReader.Read(stream);

        var author = Assert.Single(readDocument.TextEntries);
        Assert.Equal("Author", author.Keyword);
        Assert.Equal("Lucitex", author.Text);
    }

    [Fact]
    public void Read_ThrowsWhenIndexedImageIsMissingPalette()
    {
        var document = BuildDocument() with { Palette = [] };
        using var stream = new MemoryStream();

        PngDocumentWriter.Write(stream, document, []);
        stream.Position = 0;

        Assert.Throws<Lucitex.Core.Execution.ImageFormatException>(() => PngDocumentReader.Read(stream));
    }
}
