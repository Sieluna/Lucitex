using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Lucitex.Core.Execution;

namespace Lucitex.Png.Format;

internal static class PngDocumentReader
{
    public static (PngDocument Document, byte[] CompressedIdat) Read(Stream stream)
    {
        PngChunkIo.ReadSignature(stream);

        PngIhdr? ihdr = null;
        IReadOnlyList<PngPaletteEntry> palette = [];
        byte[]? transparency = null;
        float? gamma = null;
        byte? srgbIntent = null;
        PngChromaticities? chromaticities = null;
        byte[]? iccProfile = null;
        string? iccProfileName = null;
        var textEntries = new List<PngTextEntry>();
        var unknown = new List<PngRawChunk>();
        using var idatBuffer = new MemoryStream();

        while (true)
        {
            var chunk = PngChunkIo.ReadChunk(stream);

            switch (chunk.Type)
            {
                case "IHDR":
                    ihdr = ParseIhdr(chunk.Data);
                    break;
                case "PLTE":
                    palette = ParsePalette(chunk.Data);
                    break;
                case "tRNS":
                    transparency = chunk.Data;
                    break;
                case "gAMA":
                    gamma = BinaryPrimitives.ReadUInt32BigEndian(chunk.Data) / 100000.0f;
                    break;
                case "sRGB":
                    srgbIntent = chunk.Data[0];
                    break;
                case "cHRM":
                    chromaticities = ParseChromaticities(chunk.Data);
                    break;
                case "iCCP":
                    (iccProfileName, iccProfile) = ParseIccp(chunk.Data);
                    break;
                case "tEXt":
                    textEntries.Add(ParseTExt(chunk.Data));
                    break;
                case "zTXt":
                    textEntries.Add(ParseZTxt(chunk.Data));
                    break;
                case "iTXt":
                    textEntries.Add(ParseITxt(chunk.Data));
                    break;
                case "IDAT":
                    idatBuffer.Write(chunk.Data);
                    break;
                case "IEND":
                    goto done;
                default:
                    unknown.Add(new PngRawChunk { Type = chunk.Type, Data = chunk.Data });
                    break;
            }
        }

        done:
        if (ihdr is null)
        {
            throw new ImageFormatException("png", "MissingChunk", "The file is missing the required IHDR chunk.");
        }

        if (ihdr.Value.ColorType == PngColorType.Indexed && palette.Count == 0)
        {
            throw new ImageFormatException("png", "MissingChunk", "Indexed-color images require a PLTE chunk.");
        }

        var document = new PngDocument
        {
            Ihdr = ihdr.Value,
            Palette = palette,
            TransparencyData = transparency,
            Gamma = gamma,
            SrgbRenderingIntent = srgbIntent,
            Chromaticities = chromaticities,
            IccProfile = iccProfile,
            IccProfileName = iccProfileName,
            TextEntries = textEntries,
            UnknownChunks = unknown,
        };

        return (document, idatBuffer.ToArray());
    }

    private static PngIhdr ParseIhdr(byte[] data)
    {
        if (data.Length != 13)
        {
            throw new ImageFormatException("png", "BadChunk", "IHDR chunk must be 13 bytes.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4));
        var bitDepth = data[8];
        var colorType = (PngColorType)data[9];
        var compressionMethod = data[10];
        var filterMethod = data[11];
        var interlace = (PngInterlaceMethod)data[12];

        if (compressionMethod != 0)
        {
            throw new ImageFormatException("png", "Unsupported.Png.CompressionMethod", $"Unknown IHDR compression method {compressionMethod}.");
        }

        if (filterMethod != 0)
        {
            throw new ImageFormatException("png", "Unsupported.Png.FilterMethod", $"Unknown IHDR filter method {filterMethod}.");
        }

        return new PngIhdr(width, height, bitDepth, colorType, interlace);
    }

    private static IReadOnlyList<PngPaletteEntry> ParsePalette(byte[] data)
    {
        var entries = new List<PngPaletteEntry>(data.Length / 3);
        for (var i = 0; i + 2 < data.Length; i += 3)
        {
            entries.Add(new PngPaletteEntry(data[i], data[i + 1], data[i + 2]));
        }

        return entries;
    }

    private static PngChromaticities ParseChromaticities(byte[] data)
    {
        PngChromaticity Read(int offset) => new(
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)) / 100000.0,
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4, 4)) / 100000.0);

        return new PngChromaticities
        {
            White = Read(0),
            Red = Read(8),
            Green = Read(16),
            Blue = Read(24),
        };
    }

    private static (string Name, byte[] Profile) ParseIccp(byte[] data)
    {
        var nameEnd = Array.IndexOf(data, (byte)0);
        var name = Encoding.Latin1.GetString(data, 0, nameEnd);
        var compressionMethod = data[nameEnd + 1];

        if (compressionMethod != 0)
        {
            throw new ImageFormatException("png", "Unsupported.Png.IccCompression", $"Unknown iCCP compression method {compressionMethod}.");
        }

        var compressed = data.AsSpan(nameEnd + 2).ToArray();
        return (name, Inflate(compressed));
    }

    private static PngTextEntry ParseTExt(byte[] data)
    {
        var keywordEnd = Array.IndexOf(data, (byte)0);
        var keyword = Encoding.Latin1.GetString(data, 0, keywordEnd);
        var text = Encoding.Latin1.GetString(data, keywordEnd + 1, data.Length - keywordEnd - 1);
        return new PngTextEntry { Keyword = keyword, Text = text };
    }

    private static PngTextEntry ParseZTxt(byte[] data)
    {
        var keywordEnd = Array.IndexOf(data, (byte)0);
        var keyword = Encoding.Latin1.GetString(data, 0, keywordEnd);
        var compressed = data.AsSpan(keywordEnd + 2).ToArray();
        var text = Encoding.Latin1.GetString(Inflate(compressed));
        return new PngTextEntry { Keyword = keyword, Text = text };
    }

    private static PngTextEntry ParseITxt(byte[] data)
    {
        var offset = 0;
        var keywordEnd = Array.IndexOf(data, (byte)0, offset);
        var keyword = Encoding.UTF8.GetString(data, offset, keywordEnd - offset);
        offset = keywordEnd + 1;

        var compressionFlag = data[offset++];
        var compressionMethod = data[offset++];

        var languageEnd = Array.IndexOf(data, (byte)0, offset);
        offset = languageEnd + 1;

        var translatedKeywordEnd = Array.IndexOf(data, (byte)0, offset);
        offset = translatedKeywordEnd + 1;

        var remaining = data.AsSpan(offset).ToArray();
        var text = compressionFlag != 0
            ? Encoding.UTF8.GetString(Inflate(remaining, compressionMethod))
            : Encoding.UTF8.GetString(remaining);

        return new PngTextEntry { Keyword = keyword, Text = text };
    }

    private static byte[] Inflate(byte[] compressed, byte compressionMethod = 0)
    {
        if (compressionMethod != 0)
        {
            throw new ImageFormatException("png", "Unsupported.Png.TextCompression", $"Unknown text compression method {compressionMethod}.");
        }

        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
