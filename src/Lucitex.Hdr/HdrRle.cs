using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucitex.Hdr;

internal static class HdrRle
{
    private const int k_IoBufferSize = 1 << 16;

    public static void Decode(Stream stream, Span<byte> pixels, int width, int height)
    {
        var rowBytes = checked(width * 4);
        var planes = ArrayPool<byte>.Shared.Rent(rowBytes);
        var input = ArrayPool<byte>.Shared.Rent(k_IoBufferSize);

        try {
            var reader = new BufferedReader(stream, input);
            for (var y = 0; y < height; y++) {
                DecodeScanline(ref reader, pixels.Slice(y * rowBytes, rowBytes), planes.AsSpan(0, rowBytes), width);
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(input);
            ArrayPool<byte>.Shared.Return(planes);
        }
    }

    public static void Encode(Stream stream, ReadOnlySpan<byte> pixels, int width, int height)
    {
        var rowBytes = checked(width * 4);
        var planes = ArrayPool<byte>.Shared.Rent(rowBytes);
        var output = ArrayPool<byte>.Shared.Rent(Math.Max(k_IoBufferSize, (rowBytes * 2) + 64));

        try {
            var writer = new BufferedWriter(stream, output);
            for (var y = 0; y < height; y++) {
                EncodeScanline(ref writer, pixels.Slice(y * rowBytes, rowBytes), planes.AsSpan(0, rowBytes), width);
            }

            writer.Flush();
        }
        finally {
            ArrayPool<byte>.Shared.Return(output);
            ArrayPool<byte>.Shared.Return(planes);
        }
    }

    private static void DecodeScanline(ref BufferedReader reader, Span<byte> destination, Span<byte> planes, int width)
    {
        Span<byte> marker = stackalloc byte[4];
        reader.ReadExactly(marker);

        if (width < 8 || width > 32767 || marker[0] != 2 || marker[1] != 2 || (marker[2] & 0x80) != 0) {
            marker.CopyTo(destination);
            reader.ReadExactly(destination[4..]);
            return;
        }

        var declaredWidth = (marker[2] << 8) | marker[3];
        if (declaredWidth != width) {
            throw new InvalidDataException("HDR scanline width does not match the resolution header.");
        }

        for (var channel = 0; channel < 4; channel++) {
            var plane = planes.Slice(channel * width, width);
            var pixel = 0;

            while (pixel < width) {
                var code = reader.ReadByte();
                if (code <= 0) {
                    throw new InvalidDataException("HDR scanline contains an invalid RLE packet.");
                }

                if (code > 128) {
                    var count = code - 128;
                    var value = reader.ReadByte();
                    if (value < 0 || count > width - pixel) {
                        throw new InvalidDataException("HDR scanline run exceeds the row boundary.");
                    }

                    plane.Slice(pixel, count).Fill((byte)value);
                    pixel += count;
                }
                else {
                    if (code > width - pixel) {
                        throw new InvalidDataException("HDR scanline literal exceeds the row boundary.");
                    }

                    reader.ReadExactly(plane.Slice(pixel, code));
                    pixel += code;
                }
            }
        }

        Interleave(planes, destination, width);
    }

    private static void EncodeScanline(ref BufferedWriter writer, ReadOnlySpan<byte> source, Span<byte> planes, int width)
    {
        if (width < 8 || width > 32767) {
            writer.Write(source);
            return;
        }

        writer.Write([2, 2, (byte)(width >> 8), (byte)width]);
        Deinterleave(source, planes, width);

        for (var channel = 0; channel < 4; channel++) {
            EncodeChannel(ref writer, planes.Slice(channel * width, width));
        }
    }

    private static void EncodeChannel(ref BufferedWriter writer, ReadOnlySpan<byte> values)
    {
        var index = 0;
        while (index < values.Length) {
            var runStart = FindRunStart(values, index, Math.Min(index + 128, values.Length));

            if (runStart > index) {
                var literalLength = runStart - index;
                writer.WriteByte((byte)literalLength);
                writer.Write(values.Slice(index, literalLength));
                index = runStart;
                continue;
            }

            var runLength = RunLength(values, index);
            writer.WriteByte((byte)(128 + runLength));
            writer.WriteByte(values[index]);
            index += runLength;
        }
    }

    private static int FindRunStart(ReadOnlySpan<byte> values, int index, int limit)
    {
        var position = index;

        if (Vector.IsHardwareAccelerated) {
            var lanes = Vector<byte>.Count;
            while (position + lanes + 3 <= limit) {
                var first = new Vector<byte>(values.Slice(position, lanes));
                var matches = Vector.Equals(first, new Vector<byte>(values.Slice(position + 1, lanes)))
                    & Vector.Equals(first, new Vector<byte>(values.Slice(position + 2, lanes)))
                    & Vector.Equals(first, new Vector<byte>(values.Slice(position + 3, lanes)));

                if (matches != Vector<byte>.Zero) {
                    break;
                }

                position += lanes;
            }
        }

        while (position < limit && !StartsRun(values, position)) {
            position++;
        }

        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool StartsRun(ReadOnlySpan<byte> values, int index) =>
        index + 3 < values.Length &&
        values[index] == values[index + 1] &&
        values[index] == values[index + 2] &&
        values[index] == values[index + 3];

    private static int RunLength(ReadOnlySpan<byte> values, int start)
    {
        var first = values[start];
        var limit = Math.Min(start + 127, values.Length);
        var index = start + 1;

        if (Vector.IsHardwareAccelerated) {
            var lanes = Vector<byte>.Count;
            var repeated = new Vector<byte>(first);
            while (index + lanes <= limit && new Vector<byte>(values.Slice(index, lanes)) == repeated) {
                index += lanes;
            }
        }

        while (index < limit && values[index] == first) {
            index++;
        }

        return index - start;
    }

    private static void Interleave(ReadOnlySpan<byte> planes, Span<byte> destination, int width)
    {
        var x = 0;

        if (Vector.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            var lanes = Vector<byte>.Count;
            var words = MemoryMarshal.Cast<byte, uint>(destination);

            for (; x + lanes <= width; x += lanes) {
                Vector.Widen(new Vector<byte>(planes.Slice(x, lanes)), out var redLow, out var redHigh);
                Vector.Widen(new Vector<byte>(planes.Slice(width + x, lanes)), out var greenLow, out var greenHigh);
                Vector.Widen(new Vector<byte>(planes.Slice((2 * width) + x, lanes)), out var blueLow, out var blueHigh);
                Vector.Widen(new Vector<byte>(planes.Slice((3 * width) + x, lanes)), out var alphaLow, out var alphaHigh);

                StorePixels(redLow | (greenLow << 8), blueLow | (alphaLow << 8), words, x);
                StorePixels(redHigh | (greenHigh << 8), blueHigh | (alphaHigh << 8), words, x + (lanes / 2));
            }
        }

        for (; x < width; x++) {
            destination[x * 4] = planes[x];
            destination[(x * 4) + 1] = planes[width + x];
            destination[(x * 4) + 2] = planes[(2 * width) + x];
            destination[(x * 4) + 3] = planes[(3 * width) + x];
        }
    }

    private static void StorePixels(Vector<ushort> redGreen, Vector<ushort> blueAlpha, Span<uint> words, int offset)
    {
        var lanes = Vector<uint>.Count;
        Vector.Widen(redGreen, out var frontLow, out var frontHigh);
        Vector.Widen(blueAlpha, out var backLow, out var backHigh);

        (frontLow | (backLow << 16)).CopyTo(words.Slice(offset, lanes));
        (frontHigh | (backHigh << 16)).CopyTo(words.Slice(offset + lanes, lanes));
    }

    private static void Deinterleave(ReadOnlySpan<byte> source, Span<byte> planes, int width)
    {
        var x = 0;

        if (Vector.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
            var lanes = Vector<byte>.Count;
            var words = MemoryMarshal.Cast<byte, uint>(source);
            var byteMask = new Vector<uint>(0xFFu);
            var wordLanes = Vector<uint>.Count;

            for (; x + lanes <= width; x += lanes) {
                var a = new Vector<uint>(words.Slice(x, wordLanes));
                var b = new Vector<uint>(words.Slice(x + wordLanes, wordLanes));
                var c = new Vector<uint>(words.Slice(x + (2 * wordLanes), wordLanes));
                var d = new Vector<uint>(words.Slice(x + (3 * wordLanes), wordLanes));

                StoreChannel(a, b, c, d, byteMask, 0, planes.Slice(x, lanes));
                StoreChannel(a, b, c, d, byteMask, 8, planes.Slice(width + x, lanes));
                StoreChannel(a, b, c, d, byteMask, 16, planes.Slice((2 * width) + x, lanes));
                StoreChannel(a, b, c, d, byteMask, 24, planes.Slice((3 * width) + x, lanes));
            }
        }

        for (; x < width; x++) {
            planes[x] = source[x * 4];
            planes[width + x] = source[(x * 4) + 1];
            planes[(2 * width) + x] = source[(x * 4) + 2];
            planes[(3 * width) + x] = source[(x * 4) + 3];
        }
    }

    private static void StoreChannel(
        Vector<uint> a,
        Vector<uint> b,
        Vector<uint> c,
        Vector<uint> d,
        Vector<uint> byteMask,
        int shift,
        Span<byte> destination)
    {
        var low = Vector.Narrow((a >> shift) & byteMask, (b >> shift) & byteMask);
        var high = Vector.Narrow((c >> shift) & byteMask, (d >> shift) & byteMask);
        Vector.Narrow(low, high).CopyTo(destination);
    }

    private ref struct BufferedReader(Stream stream, byte[] buffer)
    {
        private readonly Stream _stream = stream;
        private readonly byte[] _buffer = buffer;
        private int _position;
        private int _length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadByte()
        {
            if (_position == _length && !Fill()) {
                return -1;
            }

            return _buffer[_position++];
        }

        public void ReadExactly(scoped Span<byte> destination)
        {
            while (!destination.IsEmpty) {
                if (_position == _length && !Fill()) {
                    throw new EndOfStreamException("Unexpected end of HDR scanline data.");
                }

                var available = Math.Min(_length - _position, destination.Length);
                _buffer.AsSpan(_position, available).CopyTo(destination);
                _position += available;
                destination = destination[available..];
            }
        }

        private bool Fill()
        {
            _length = _stream.Read(_buffer, 0, _buffer.Length);
            _position = 0;
            return _length > 0;
        }
    }

    private ref struct BufferedWriter(Stream stream, byte[] buffer)
    {
        private readonly Stream _stream = stream;
        private readonly byte[] _buffer = buffer;
        private int _position;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            if (_position == _buffer.Length) {
                Flush();
            }

            _buffer[_position++] = value;
        }

        public void Write(scoped ReadOnlySpan<byte> source)
        {
            if (source.Length > _buffer.Length - _position) {
                Flush();
            }

            if (source.Length > _buffer.Length) {
                _stream.Write(source);
                return;
            }

            source.CopyTo(_buffer.AsSpan(_position));
            _position += source.Length;
        }

        public void Flush()
        {
            if (_position == 0) {
                return;
            }

            _stream.Write(_buffer, 0, _position);
            _position = 0;
        }
    }
}
