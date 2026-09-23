using Lucitex.Jpeg.Format;
using System.Runtime.CompilerServices;

namespace Lucitex.Jpeg.Encoding;

internal ref struct JpegAcSymbols(ReadOnlySpan<short> coefficients)
{
    private readonly ReadOnlySpan<short> _coefficients = coefficients;
    private int _position = 1;
    private int _run;

    public (byte Symbol, int Value) Current { get; private set; }

    public readonly JpegAcSymbols GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        while (_position < 64) {
            var value = _coefficients[JpegZigZag.Order[_position]];
            if (value == 0) {
                _run++;
                _position++;
                continue;
            }
            if (_run >= 16) {
                _run -= 16;
                Current = (0xF0, 0);
                return true;
            }
            Current = ((byte)((_run << 4) | JpegMagnitude.GetSize(value)), value);
            _position++;
            _run = 0;
            return true;
        }
        if (_run == 0) {
            return false;
        }
        _run = 0;
        Current = (0, 0);
        return true;
    }
}
