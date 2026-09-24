namespace Lucitex.Webp.Lossless;

internal sealed class Vp8LEntropyEncoder
{
    private readonly Vp8LCodebook[] _books;

    public long DataBits { get; }

    public Vp8LEntropyEncoder(ReadOnlySpan<uint> pixels, int width, Span<int> positions, int candidates)
    {
        var frequencies = new[] { new int[280], new int[256], new int[256], new int[256], new int[40] };
        var finder = new Vp8LMatchFinder(pixels, positions, width, candidates);
        long extraBits = 0;
        while (finder.Next(out var position, out var length, out var distance)) {
            if (distance == 0) {
                var pixel = pixels[position];
                frequencies[0][(pixel >> 8) & 255]++;
                frequencies[1][(pixel >> 16) & 255]++;
                frequencies[2][pixel & 255]++;
                frequencies[3][pixel >> 24]++;
            }
            else {
                var lengthPrefix = Vp8LPrefix.FromValue(length);
                var distancePrefix = Vp8LPrefix.FromValue(DistanceCode(distance, width));
                frequencies[0][256 + lengthPrefix.Symbol]++;
                frequencies[4][distancePrefix.Symbol]++;
                extraBits += lengthPrefix.ExtraBits + distancePrefix.ExtraBits;
            }
        }
        _books = new Vp8LCodebook[5];
        DataBits = extraBits;
        for (var i = 0; i < 5; i++) {
            _books[i] = new Vp8LCodebook(frequencies[i]);
            DataBits += _books[i].Measure(frequencies[i]);
        }
    }

    public void WriteHeader(Vp8LBitWriter writer, bool mainImage)
    {
        writer.Write(0, 1);
        if (mainImage) {
            writer.Write(0, 1);
        }
        foreach (var book in _books) {
            book.WriteHeader(writer);
        }
    }

    public void WritePixels(Vp8LBitWriter writer, ReadOnlySpan<uint> pixels, int width, Span<int> positions, int candidates)
    {
        var finder = new Vp8LMatchFinder(pixels, positions, width, candidates);
        while (finder.Next(out var position, out var length, out var distance)) {
            if (distance == 0) {
                var pixel = pixels[position];
                _books[0].WriteSymbol(writer, (int)((pixel >> 8) & 255));
                _books[1].WriteSymbol(writer, (int)((pixel >> 16) & 255));
                _books[2].WriteSymbol(writer, (int)(pixel & 255));
                _books[3].WriteSymbol(writer, (int)(pixel >> 24));
            }
            else {
                var lengthPrefix = Vp8LPrefix.FromValue(length);
                var distancePrefix = Vp8LPrefix.FromValue(DistanceCode(distance, width));
                _books[0].WriteSymbol(writer, 256 + lengthPrefix.Symbol);
                writer.Write(lengthPrefix.ExtraValue, lengthPrefix.ExtraBits);
                _books[4].WriteSymbol(writer, distancePrefix.Symbol);
                writer.Write(distancePrefix.ExtraValue, distancePrefix.ExtraBits);
            }
        }
    }

    private static int DistanceCode(int distance, int width)
        => distance == width ? 1 : distance == 1 ? 2 : distance + 120;
}
