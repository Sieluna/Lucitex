namespace Lucitex.Webp.Lossless;

internal readonly record struct Vp8LToken(int Length, int Distance);

internal sealed class Vp8LEntropyEncoder : IDisposable
{
    private readonly Vp8LCodebook[] _books;
    private readonly WebpBuffer<Vp8LToken> _tokens;
    private readonly int _tokenCount;
    private readonly int _width;
    private readonly bool _ownsTokens;
    private long _distanceBits;
    private bool _usePlaneCodes;
    private Vp8LCodebook? _legacyDistanceBook;
    private long _legacyDistanceBits;

    public long DataBits { get; private set; }

    public Vp8LEntropyEncoder(ReadOnlySpan<uint> pixels, int width, Span<int> positions, int candidates, WebpMemory memory, WebpBuffer<Vp8LToken>? tokens = null)
    {
        _width = width;
        var frequencies = new[] { new int[280], new int[256], new int[256], new int[256], new int[40] };
        var uniform = !pixels.IsEmpty && !pixels.ContainsAnyExcept(pixels[0]);
        using var heads = candidates == 1 || uniform ? null : memory.Rent<byte>(positions.Length / candidates);
        var headPositions = heads is null ? Span<byte>.Empty : heads.Span;
        var finder = uniform ? default : new Vp8LMatchFinder(pixels, positions, headPositions, width, candidates);
        _ownsTokens = tokens is null;
        _tokens = tokens ?? memory.Rent<Vp8LToken>(Math.Max(1, pixels.Length));
        var tokenSpan = _tokens.Span;
        long extraBits = 0;
        if (uniform) {
            var pixel = pixels[0];
            tokenSpan[_tokenCount++] = new Vp8LToken(pixels.Length, 0);
            frequencies[0][(pixel >> 8) & 255] = 1;
            frequencies[1][(pixel >> 16) & 255] = 1;
            frequencies[2][pixel & 255] = 1;
            frequencies[3][pixel >> 24] = 1;
        }
        while (!uniform && finder.Next(out var position, out var length, out var distance)) {
            tokenSpan[_tokenCount++] = new Vp8LToken(length, distance);
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
                _distanceBits += distancePrefix.ExtraBits;
            }
        }
        _books = new Vp8LCodebook[5];
        DataBits = extraBits;
        for (var i = 0; i < 5; i++) {
            _books[i] = new Vp8LCodebook(frequencies[i]);
            DataBits += _books[i].Measure(frequencies[i]);
        }
        _distanceBits += _books[4].Measure(frequencies[4]);
    }

    public bool UsePlaneCodes()
    {
        if (_usePlaneCodes) {
            return false;
        }
        Span<int> frequencies = stackalloc int[40];
        frequencies.Clear();
        long extraBits = 0;
        var changed = false;
        foreach (var token in _tokens.Span[.._tokenCount]) {
            if (token.Distance == 0) {
                continue;
            }
            var code = Vp8LDistance.Encode(token.Distance, _width);
            changed |= code != DistanceCode(token.Distance, _width);
            var prefix = Vp8LPrefix.FromValue(code);
            frequencies[prefix.Symbol]++;
            extraBits += prefix.ExtraBits;
        }
        if (!changed) {
            return false;
        }
        _legacyDistanceBook = _books[4];
        _legacyDistanceBits = _distanceBits;
        _books[4] = new Vp8LCodebook(frequencies);
        var distanceBits = extraBits + _books[4].Measure(frequencies);
        DataBits += distanceBits - _distanceBits;
        _distanceBits = distanceBits;
        _usePlaneCodes = true;
        return true;
    }

    public void UseLegacyCodes()
    {
        if (!_usePlaneCodes) {
            return;
        }
        _books[4] = _legacyDistanceBook!;
        DataBits += _legacyDistanceBits - _distanceBits;
        _distanceBits = _legacyDistanceBits;
        _usePlaneCodes = false;
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
        var position = 0;
        foreach (var token in _tokens.Span[.._tokenCount]) {
            if (token.Distance == 0) {
                var pixel = pixels[position];
                _books[0].WriteSymbol(writer, (int)((pixel >> 8) & 255));
                _books[1].WriteSymbol(writer, (int)((pixel >> 16) & 255));
                _books[2].WriteSymbol(writer, (int)(pixel & 255));
                _books[3].WriteSymbol(writer, (int)(pixel >> 24));
            }
            else {
                var lengthPrefix = Vp8LPrefix.FromValue(token.Length);
                var distancePrefix = Vp8LPrefix.FromValue(_usePlaneCodes ? Vp8LDistance.Encode(token.Distance, _width) : DistanceCode(token.Distance, _width));
                _books[0].WriteSymbol(writer, 256 + lengthPrefix.Symbol);
                writer.Write(lengthPrefix.ExtraValue, lengthPrefix.ExtraBits);
                _books[4].WriteSymbol(writer, distancePrefix.Symbol);
                writer.Write(distancePrefix.ExtraValue, distancePrefix.ExtraBits);
            }
            position += token.Length;
        }
    }

    public void Dispose()
    {
        if (_ownsTokens) {
            _tokens.Dispose();
        }
    }

    private static int DistanceCode(int distance, int width)
        => distance == width ? 1 : distance == 1 ? 2 : distance + 120;
}
