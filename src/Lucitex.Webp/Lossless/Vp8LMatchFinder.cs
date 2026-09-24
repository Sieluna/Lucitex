using System.Numerics;

namespace Lucitex.Webp.Lossless;

internal ref struct Vp8LMatchFinder
{
    private readonly ReadOnlySpan<uint> _pixels;
    private readonly Span<int> _positions;
    private readonly int _width;
    private readonly int _candidates;
    private readonly int _mask;
    private int _position;

    public Vp8LMatchFinder(ReadOnlySpan<uint> pixels, Span<int> positions, int width, int candidates)
    {
        _pixels = pixels;
        _positions = positions;
        _width = width;
        _candidates = candidates;
        _mask = (positions.Length / candidates) - 1;
        positions.Fill(-1);
    }

    public static int TableSize(int pixels, int candidates)
        => (int)Math.Min(16384u, BitOperations.RoundUpToPowerOf2((uint)Math.Max(256, pixels))) * candidates;

    public bool Next(out int position, out int length, out int distance)
    {
        position = _position;
        length = 1;
        distance = 0;
        if (position >= _pixels.Length) {
            return false;
        }
        if (_pixels.Length - position >= 3) {
            var limit = Math.Min(4096, _pixels.Length - position);
            Consider(position - 1, limit, ref length, ref distance);
            if (length < limit) {
                Consider(position - _width, limit, ref length, ref distance);
            }
            var bucket = Hash(position) * _candidates;
            for (var i = 0; i < _candidates && length < limit; i++) {
                Consider(_positions[bucket + i], limit, ref length, ref distance);
            }
            if (length < 3) {
                length = 1;
                distance = 0;
            }
        }
        var end = position + length;
        for (var i = position; i < end && i + 2 < _pixels.Length; i++) {
            var bucket = Hash(i) * _candidates;
            for (var slot = _candidates - 1; slot > 0; slot--) {
                _positions[bucket + slot] = _positions[bucket + slot - 1];
            }
            _positions[bucket] = i;
        }
        _position = end;
        return true;
    }

    private readonly int Hash(int position)
    {
        var hash = unchecked((_pixels[position] * 0x1e35a7bd) ^ (_pixels[position + 1] * 0x9e3779b1) ^ (_pixels[position + 2] * 0x85ebca77));
        return (int)(hash ^ (hash >> 16)) & _mask;
    }

    private readonly void Consider(int candidate, int limit, ref int length, ref int distance)
    {
        if (candidate < 0 || candidate >= _position || _position - candidate > 1048456 ||
            _pixels[candidate] != _pixels[_position] || _pixels[candidate + length] != _pixels[_position + length]) {
            return;
        }
        var matched = 1;
        if (Vector.IsHardwareAccelerated) {
            var lanes = Vector<uint>.Count;
            while (matched <= limit - lanes && Vector.EqualsAll(new Vector<uint>(_pixels.Slice(candidate + matched, lanes)), new Vector<uint>(_pixels.Slice(_position + matched, lanes)))) {
                matched += lanes;
            }
        }
        while (matched < limit && _pixels[candidate + matched] == _pixels[_position + matched]) {
            matched++;
        }
        if (matched > length) {
            length = matched;
            distance = _position - candidate;
        }
    }
}
