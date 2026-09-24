namespace Lucitex.Webp.Lossy;

internal sealed class Vp8BoolEncoder(WebpMemory memory) : IDisposable
{
    private WebpBuffer<byte> _buffer = memory.Rent<byte>(4096);
    private int _length;
    private ulong _low;
    private uint _range = 255;
    private int _count = -24;

    public ReadOnlySpan<byte> Bytes => _buffer.Span[.._length];

    public void Put(int bit, int probability = 128)
    {
        var split = 1 + (((_range - 1) * (uint)probability) >> 8);
        if (bit == 0) {
            _range = split;
        }
        else {
            _low += split;
            _range -= split;
        }
        while (_range < 128) {
            _range <<= 1;
            _low <<= 1;
            if (++_count != 0) {
                continue;
            }
            if ((_low >> 32) != 0) {
                var previous = _length - 1;
                while (previous >= 0 && _buffer.Span[previous] == 255) {
                    _buffer.Span[previous--] = 0;
                }
                if (previous < 0) {
                    throw new InvalidOperationException("VP8 arithmetic coder carry overflow.");
                }
                _buffer.Span[previous]++;
            }
            if (_length == _buffer.Length) {
                var expanded = memory.Rent<byte>(checked(_buffer.Length * 2));
                _buffer.Span.CopyTo(expanded.Span);
                _buffer.Dispose();
                _buffer = expanded;
            }
            _buffer.Span[_length++] = (byte)(_low >> 24);
            _low &= 0xffffff;
            _count = -8;
        }
    }

    public void Literal(int value, int bits)
    {
        for (var bit = bits - 1; bit >= 0; bit--) {
            Put((value >> bit) & 1);
        }
    }

    public void Finish()
    {
        for (var i = 0; i < 32; i++) {
            Put(0);
        }
    }

    public void Dispose() => _buffer.Dispose();
}
