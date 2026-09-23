using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegByteCursor(Stream stream)
{
    private readonly Stack<byte> _pushback = new();
    private readonly byte[] _buffer = new byte[4096];
    private int _position;
    private int _length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadEntropyBytes(int count, out ulong value)
    {
        if (_pushback.Count == 0 && _length - _position >= sizeof(ulong)) {
            var word = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.AsSpan(_position, sizeof(ulong)));
            var inverted = ~word;
            if (((inverted - 0x0101010101010101UL) & ~inverted & 0x8080808080808080UL) == 0) {
                value = BinaryPrimitives.ReverseEndianness(word) >> (64 - count * 8);
                _position += count;
                return true;
            }
        }
        value = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (_pushback.Count > 0) {
            return _pushback.Pop();
        }

        if (_position == _length) {
            Fill();
        }
        return _buffer[_position++];
    }

    private void Fill()
    {
        _length = stream.Read(_buffer);
        _position = 0;
        if (_length == 0) {
            throw new EndOfStreamException("Unexpected end of JPEG stream.");
        }
    }

    public void PushBackMarker(byte markerCode)
    {
        _pushback.Push(markerCode);
        _pushback.Push(JpegMarkers.Prefix);
    }

    public void PushBackBytes(ReadOnlySpan<byte> bytes)
    {
        for (var i = bytes.Length - 1; i >= 0; i--) {
            _pushback.Push(bytes[i]);
        }
    }

    public int ReadBulk(Span<byte> destination)
    {
        var written = 0;
        while (written < destination.Length && _pushback.Count > 0) {
            destination[written++] = _pushback.Pop();
        }

        if (written < destination.Length) {
            var count = Math.Min(destination.Length - written, _length - _position);
            _buffer.AsSpan(_position, count).CopyTo(destination[written..]);
            _position += count;
            written += count;
        }

        if (written < destination.Length) {
            written += stream.Read(destination[written..]);
        }

        return written;
    }

    public byte ReadMarker()
    {
        var first = ReadByte();
        if (first != JpegMarkers.Prefix) {
            throw new ImageFormatException("jpeg", "BadMarker", "Expected a JPEG marker.");
        }

        byte marker;
        do {
            marker = ReadByte();
        } while (marker == JpegMarkers.Prefix);

        return marker;
    }

    public byte[] ReadSegment()
    {
        var lengthHigh = ReadByte();
        var lengthLow = ReadByte();
        var length = (lengthHigh << 8) | lengthLow;
        if (length < 2) {
            throw new ImageFormatException("jpeg", "BadSegment", "JPEG segment length must be at least 2 bytes.");
        }

        var payload = new byte[length - 2];
        for (var i = 0; i < payload.Length; i++) {
            payload[i] = ReadByte();
        }

        return payload;
    }
}
