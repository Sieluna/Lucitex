namespace Lucitex.Webp.Lossless;

internal readonly record struct Vp8LToken(int Position, int Length, int Distance);

internal sealed class Vp8LEntropyEncoder : IDisposable
{
    private readonly Vp8LCodebook[] _books;
    private readonly WebpBuffer<Vp8LToken> _tokens;
    private readonly int _tokenCount;
    private readonly int _width;

    public long DataBits { get; }

    public Vp8LEntropyEncoder(ReadOnlySpan<uint> pixels, int width, Span<int> positions, int candidates, WebpMemory memory)
    {
        _width = width;
        var frequencies = new[] { new int[280], new int[256], new int[256], new int[256], new int[40] };
        var finder = new Vp8LMatchFinder(pixels, positions, width, candidates);
        _tokens = memory.Rent<Vp8LToken>(Math.Max(1, pixels.Length));
        var tokens = _tokens.Span;
        long extraBits = 0;
        while (finder.Next(out var position, out var length, out var distance)) {
            tokens[_tokenCount++] = new Vp8LToken(position, length, distance);
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

    public void WritePixels(Vp8LBitWriter writer, ReadOnlySpan<uint> pixels)
    {
        foreach (var token in _tokens.Span[.._tokenCount]) {
            if (token.Distance == 0) {
                var pixel = pixels[token.Position];
                _books[0].WriteSymbol(writer, (int)((pixel >> 8) & 255));
                _books[1].WriteSymbol(writer, (int)((pixel >> 16) & 255));
                _books[2].WriteSymbol(writer, (int)(pixel & 255));
                _books[3].WriteSymbol(writer, (int)(pixel >> 24));
            }
            else {
                var lengthPrefix = Vp8LPrefix.FromValue(token.Length);
                var distancePrefix = Vp8LPrefix.FromValue(DistanceCode(token.Distance, _width));
                _books[0].WriteSymbol(writer, 256 + lengthPrefix.Symbol);
                writer.Write(lengthPrefix.ExtraValue, lengthPrefix.ExtraBits);
                _books[4].WriteSymbol(writer, distancePrefix.Symbol);
                writer.Write(distancePrefix.ExtraValue, distancePrefix.ExtraBits);
            }
        }
    }

    public void Dispose() => _tokens.Dispose();

    private static int DistanceCode(int distance, int width)
        => distance == width ? 1 : distance == 1 ? 2 : distance + 120;
}
