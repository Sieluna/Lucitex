using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal sealed class JpegByteCursor(Stream stream)
{
    private readonly Stack<byte> _pushback = new();

    public byte ReadByte()
    {
        if (_pushback.Count > 0) {
            return _pushback.Pop();
        }

        var value = stream.ReadByte();
        if (value < 0) {
            throw new EndOfStreamException("Unexpected end of JPEG stream.");
        }

        return (byte)value;
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
