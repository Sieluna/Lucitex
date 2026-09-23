namespace Lucitex.Jpeg.Format;

internal static class JpegDocumentWriter
{
    public static void WriteSoi(Stream stream) => WriteMarker(stream, JpegMarkers.Soi);

    public static void WriteEoi(Stream stream) => WriteMarker(stream, JpegMarkers.Eoi);

    public static void WriteJfifHeader(Stream stream)
    {
        ReadOnlySpan<byte> payload = [(byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0];
        WriteSegment(stream, JpegMarkers.App0, payload);
    }

    public static void WriteQuantizationTable(Stream stream, int id, ushort[] naturalOrderValues)
    {
        Span<byte> payload = stackalloc byte[65];
        payload[0] = (byte)id;
        for (var i = 0; i < 64; i++) {
            payload[1 + i] = (byte)naturalOrderValues[JpegZigZag.Order[i]];
        }

        WriteSegment(stream, JpegMarkers.Dqt, payload);
    }

    public static void WriteFrameHeader(Stream stream, byte marker, int width, int height, byte[] componentIds, byte[] hSampling, byte[] vSampling, byte[] quantTableIds)
    {
        var componentCount = componentIds.Length;
        Span<byte> payload = stackalloc byte[6 + (componentCount * 3)];
        payload[0] = 8;
        payload[1] = (byte)(height >> 8);
        payload[2] = (byte)(height & 0xFF);
        payload[3] = (byte)(width >> 8);
        payload[4] = (byte)(width & 0xFF);
        payload[5] = (byte)componentCount;

        for (var c = 0; c < componentCount; c++) {
            var offset = 6 + (c * 3);
            payload[offset] = componentIds[c];
            payload[offset + 1] = (byte)((hSampling[c] << 4) | vSampling[c]);
            payload[offset + 2] = quantTableIds[c];
        }

        WriteSegment(stream, marker, payload);
    }

    public static void WriteHuffmanTable(Stream stream, JpegHuffmanSpec spec)
    {
        Span<byte> payload = stackalloc byte[1 + 16 + spec.Values.Length];
        payload[0] = (byte)(((spec.IsAc ? 1 : 0) << 4) | spec.Id);
        for (var i = 0; i < 16; i++) {
            payload[1 + i] = spec.Bits[i];
        }

        for (var i = 0; i < spec.Values.Length; i++) {
            payload[17 + i] = spec.Values[i];
        }

        WriteSegment(stream, JpegMarkers.Dht, payload);
    }

    public static void WriteDri(Stream stream, int restartInterval)
    {
        ReadOnlySpan<byte> payload = [(byte)(restartInterval >> 8), (byte)(restartInterval & 0xFF)];
        WriteSegment(stream, JpegMarkers.Dri, payload);
    }

    public static void WriteRestartMarker(Stream stream, byte marker) => WriteMarker(stream, marker);

    public static void WriteScanHeader(Stream stream, byte[] componentIds, byte[] dcTableIds, byte[] acTableIds, byte spectralStart, byte spectralEnd, byte successiveApproximation)
    {
        var count = componentIds.Length;
        Span<byte> payload = stackalloc byte[4 + (count * 2)];
        payload[0] = (byte)count;

        for (var c = 0; c < count; c++) {
            payload[1 + (c * 2)] = componentIds[c];
            payload[2 + (c * 2)] = (byte)((dcTableIds[c] << 4) | acTableIds[c]);
        }

        payload[1 + (count * 2)] = spectralStart;
        payload[2 + (count * 2)] = spectralEnd;
        payload[3 + (count * 2)] = successiveApproximation;

        WriteSegment(stream, JpegMarkers.Sos, payload);
    }

    private static void WriteMarker(Stream stream, byte marker)
    {
        stream.WriteByte(JpegMarkers.Prefix);
        stream.WriteByte(marker);
    }

    private static void WriteSegment(Stream stream, byte marker, ReadOnlySpan<byte> payload)
    {
        WriteMarker(stream, marker);
        var length = payload.Length + 2;
        stream.WriteByte((byte)(length >> 8));
        stream.WriteByte((byte)(length & 0xFF));
        stream.Write(payload);
    }
}
