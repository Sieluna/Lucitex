namespace Lucitex.Hdr;

internal static class HdrRle
{
    public static void DecodeScanline(Stream stream, Span<byte> destination, int width)
    {
        Span<byte> marker = stackalloc byte[4];
        stream.ReadExactly(marker);
        if (width < 8 || width > 32767 || marker[0] != 2 || marker[1] != 2 || (marker[2] & 0x80) != 0) {
            marker.CopyTo(destination);
            stream.ReadExactly(destination[4..]);
            return;
        }

        var declaredWidth = (marker[2] << 8) | marker[3];
        if (declaredWidth != width) {
            throw new InvalidDataException("HDR scanline width does not match the resolution header.");
        }

        for (var channel = 0; channel < 4; channel++) {
            var pixel = 0;
            while (pixel < width) {
                var code = stream.ReadByte();
                if (code <= 0) {
                    throw new InvalidDataException("HDR scanline contains an invalid RLE packet.");
                }

                if (code > 128) {
                    var count = code - 128;
                    var value = stream.ReadByte();
                    if (value < 0 || count > width - pixel) {
                        throw new InvalidDataException("HDR scanline run exceeds the row boundary.");
                    }

                    for (var i = 0; i < count; i++) {
                        destination[((pixel + i) * 4) + channel] = (byte)value;
                    }

                    pixel += count;
                }
                else {
                    var count = code;
                    if (count > width - pixel) {
                        throw new InvalidDataException("HDR scanline literal exceeds the row boundary.");
                    }

                    for (var i = 0; i < count; i++) {
                        var value = stream.ReadByte();
                        if (value < 0) {
                            throw new EndOfStreamException("Unexpected end of HDR scanline literal.");
                        }

                        destination[((pixel + i) * 4) + channel] = (byte)value;
                    }

                    pixel += count;
                }
            }
        }
    }

    public static void EncodeScanline(Stream stream, ReadOnlySpan<byte> source, int width, Span<byte> channelBuffer)
    {
        if (width < 8 || width > 32767) {
            stream.Write(source);
            return;
        }

        Span<byte> marker = stackalloc byte[4] { 2, 2, (byte)(width >> 8), (byte)width };
        stream.Write(marker);
        for (var channel = 0; channel < 4; channel++) {
            for (var pixel = 0; pixel < width; pixel++) {
                channelBuffer[pixel] = source[(pixel * 4) + channel];
            }

            EncodeChannel(stream, channelBuffer[..width]);
        }
    }

    private static void EncodeChannel(Stream stream, ReadOnlySpan<byte> values)
    {
        var index = 0;
        while (index < values.Length) {
            var runStart = index;
            while (runStart < values.Length && runStart - index < 128 && RunLength(values, runStart) < 4) {
                runStart++;
            }

            if (runStart > index) {
                var literalLength = runStart - index;
                stream.WriteByte((byte)literalLength);
                stream.Write(values.Slice(index, literalLength));
                index = runStart;
                continue;
            }

            var runLength = Math.Min(127, RunLength(values, index));
            stream.WriteByte((byte)(128 + runLength));
            stream.WriteByte(values[index]);
            index += runLength;
        }
    }

    private static int RunLength(ReadOnlySpan<byte> values, int start)
    {
        var length = 1;
        while (length < 127 && start + length < values.Length && values[start + length] == values[start]) {
            length++;
        }

        return length;
    }
}
