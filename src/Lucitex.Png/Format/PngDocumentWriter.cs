using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lucitex.Png.Format;

internal static class PngDocumentWriter
{
    public static void WriteEnd(Stream stream) => PngChunkIo.WriteChunk(stream, "IEND", []);

    public static void WriteHeader(Stream stream, PngDocument document)
    {
        PngChunkIo.WriteSignature(stream);
        WriteIhdr(stream, document.Ihdr);

        if (document.Chromaticities is { } chromaticities) {
            WriteChromaticities(stream, chromaticities);
        }

        if (document.Gamma is { } gamma) {
            WriteGamma(stream, gamma);
        }

        if (document.SrgbRenderingIntent is { } intent) {
            PngChunkIo.WriteChunk(stream, "sRGB", [intent]);
        }

        if (document.IccProfile is { } iccProfile) {
            WriteIccp(stream, document.IccProfileName ?? "icc", iccProfile);
        }

        if (document.Palette.Count > 0) {
            WritePalette(stream, document.Palette);
        }

        if (document.TransparencyData is { } transparency) {
            PngChunkIo.WriteChunk(stream, "tRNS", transparency);
        }

        foreach (var text in document.TextEntries) {
            WriteText(stream, text);
        }

    }

    private static void WriteIhdr(Stream stream, PngIhdr ihdr)
    {
        Span<byte> data = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(data[..4], ihdr.Width);
        BinaryPrimitives.WriteInt32BigEndian(data[4..8], ihdr.Height);
        data[8] = ihdr.BitDepth;
        data[9] = (byte)ihdr.ColorType;
        data[10] = 0;
        data[11] = 0;
        data[12] = (byte)ihdr.Interlace;

        PngChunkIo.WriteChunk(stream, "IHDR", data);
    }

    private static void WritePalette(Stream stream, IReadOnlyList<PngPaletteEntry> palette)
    {
        var data = new byte[palette.Count * 3];
        for (var i = 0; i < palette.Count; i++) {
            data[(i * 3) + 0] = palette[i].R;
            data[(i * 3) + 1] = palette[i].G;
            data[(i * 3) + 2] = palette[i].B;
        }

        PngChunkIo.WriteChunk(stream, "PLTE", data);
    }

    private static void WriteGamma(Stream stream, float gamma)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)Math.Round(gamma * 100000.0));
        PngChunkIo.WriteChunk(stream, "gAMA", data);
    }

    private static void WriteChromaticities(Stream stream, PngChromaticities chromaticities)
    {
        Span<byte> data = stackalloc byte[32];

        static void WritePoint(Span<byte> data, int offset, PngChromaticity point)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), (uint)Math.Round(point.X * 100000.0));
            BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset + 4, 4), (uint)Math.Round(point.Y * 100000.0));
        }

        WritePoint(data, 0, chromaticities.White);
        WritePoint(data, 8, chromaticities.Red);
        WritePoint(data, 16, chromaticities.Green);
        WritePoint(data, 24, chromaticities.Blue);

        PngChunkIo.WriteChunk(stream, "cHRM", data);
    }

    private static void WriteIccp(Stream stream, string name, byte[] profile)
    {
        using var buffer = new MemoryStream();
        buffer.Write(Encoding.Latin1.GetBytes(name));
        buffer.WriteByte(0);
        buffer.WriteByte(0);

        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true)) {
            zlib.Write(profile);
        }

        PngChunkIo.WriteChunk(stream, "iCCP", buffer.ToArray());
    }

    private static void WriteText(Stream stream, PngTextEntry text)
    {
        using var buffer = new MemoryStream();
        buffer.Write(Encoding.Latin1.GetBytes(text.Keyword));
        buffer.WriteByte(0);
        buffer.Write(Encoding.Latin1.GetBytes(text.Text));

        PngChunkIo.WriteChunk(stream, "tEXt", buffer.ToArray());
    }
}
