namespace Lucitex.Jpeg.Encoding;

internal sealed class JpegBitWriter
{
    private byte[] _buffer = new byte[4096];
    private int _length;
    private uint _bitBuffer;
    private int _bitCount;

    public void WriteBits(int value, int length)
    {
        if (length == 0) {
            return;
        }

        _bitBuffer = (_bitBuffer << length) | (uint)(value & ((1 << length) - 1));
        _bitCount += length;

        while (_bitCount >= 8) {
            _bitCount -= 8;
            WriteByteStuffed((byte)((_bitBuffer >> _bitCount) & 0xFF));
        }
    }

    public void PadAndFlush()
    {
        if (_bitCount > 0) {
            var padding = 8 - _bitCount;
            _bitBuffer = (_bitBuffer << padding) | (uint)((1 << padding) - 1);
            WriteByteStuffed((byte)(_bitBuffer & 0xFF));
            _bitCount = 0;
        }
    }

    public void CopyTo(Stream stream) => stream.Write(_buffer, 0, _length);

    private void WriteByteStuffed(byte value)
    {
        EnsureCapacity(_length + 2);
        _buffer[_length++] = value;
        if (value == 0xFF) {
            _buffer[_length++] = 0x00;
        }
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length) {
            return;
        }

        var capacity = _buffer.Length * 2;
        while (capacity < required) {
            capacity *= 2;
        }

        Array.Resize(ref _buffer, capacity);
    }
}
