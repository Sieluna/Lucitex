using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Lucitex.Core.Execution;

namespace Lucitex.Png.Format;

internal static class PngDocumentReader
{
    public static (PngDocument Document, List<ReadOnlyMemory<byte>> CompressedChunks) Read(Stream stream, DecodeLimits? decodeLimits = null, PngIdatBuffers? buffers = null)
    {
        var limits = decodeLimits ?? DecodeLimits.Default;
        var maxChunkLength = checked((int)Math.Min(limits.MaxWorkingSet, int.MaxValue));
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
        var idatChunks = new List<ReadOnlyMemory<byte>>();
        long idatBytes = 0;
        long metadataBytes = 0;
        var sawIhdr = false;
        var sawIdat = false;
        var endedIdat = false;
        var sawPalette = false;
        var sawIend = false;

        while (true) {
            var chunk = PngChunkIo.ReadChunk(stream, maxChunkLength, buffers);

            if (!sawIhdr && chunk.Type != "IHDR") {
                throw new ImageFormatException("png", "BadChunkOrder", "IHDR must be the first PNG chunk.");
            }

            if (sawIdat && chunk.Type != "IDAT") {
                endedIdat = true;
            }

            switch (chunk.Type) {
                case "IHDR":
                    if (sawIhdr) {
                        throw new ImageFormatException("png", "DuplicateChunk", "PNG contains more than one IHDR chunk.");
                    }

                    ihdr = ParseIhdr(chunk.Data);
                    sawIhdr = true;
                    break;
                case "PLTE":
                    if (sawPalette) {
                        throw new ImageFormatException("png", "DuplicateChunk", "PNG contains more than one PLTE chunk.");
                    }
                    if (sawIdat) {
                        throw new ImageFormatException("png", "BadChunkOrder", "PLTE must precede IDAT.");
                    }
                    if (ihdr!.Value.ColorType is PngColorType.Grayscale or PngColorType.GrayscaleAlpha) {
                        throw new ImageFormatException("png", "BadChunk", "Grayscale PNG images cannot contain PLTE.");
                    }
                    palette = ParsePalette(chunk.Data);
                    if (ihdr.Value.ColorType == PngColorType.Indexed && palette.Count > 1 << ihdr.Value.BitDepth) {
                        throw new ImageFormatException("png", "BadChunk", "PLTE has more entries than the indexed bit depth allows.");
                    }
                    sawPalette = true;
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
                    if (endedIdat) {
                        throw new ImageFormatException("png", "BadChunkOrder", "IDAT chunks must be consecutive.");
                    }
                    idatBytes = checked(idatBytes + chunk.Length);
                    if (idatBytes > limits.MaxWorkingSet) {
                        throw new ImageFormatException("png", "LimitExceeded", "Compressed PNG image data exceeds MaxWorkingSet.");
                    }

                    idatChunks.Add(chunk.Data.AsMemory(0, chunk.Length));
                    sawIdat = true;
                    break;
                case "IEND":
                    if (chunk.Data.Length != 0) {
                        throw new ImageFormatException("png", "BadChunk", "IEND chunk must be empty.");
                    }

                    sawIend = true;
                    goto done;
                default:
                    if ((chunk.Type[0] & 0x20) == 0) {
                        throw new ImageFormatException("png", "Unsupported.Png.CriticalChunk", $"Unknown critical PNG chunk {chunk.Type}.");
                    }
                    unknown.Add(new PngRawChunk { Type = chunk.Type, Data = chunk.Data });
                    break;
            }

            if (chunk.Type != "IDAT") {
                metadataBytes = checked(metadataBytes + chunk.Data.Length);
                if (metadataBytes > limits.MaxMetadataBytes) {
                    throw new ImageFormatException("png", "LimitExceeded", "PNG metadata exceeds MaxMetadataBytes.");
                }
            }
        }

    done:
        if (ihdr is null) {
            throw new ImageFormatException("png", "MissingChunk", "The file is missing the required IHDR chunk.");
        }

        if (!sawIdat || !sawIend) {
            throw new ImageFormatException("png", "MissingChunk", "The file is missing required IDAT or IEND data.");
        }

        if (ihdr.Value.ColorType == PngColorType.Indexed && palette.Count == 0) {
            throw new ImageFormatException("png", "MissingChunk", "Indexed-color images require a PLTE chunk.");
        }

        var document = new PngDocument {
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

        return (document, idatChunks);
    }

    private static PngIhdr ParseIhdr(byte[] data)
    {
        if (data.Length != 13) {
            throw new ImageFormatException("png", "BadChunk", "IHDR chunk must be 13 bytes.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4));
        var bitDepth = data[8];
        var colorType = (PngColorType)data[9];
        var compressionMethod = data[10];
        var filterMethod = data[11];
        var interlace = (PngInterlaceMethod)data[12];

        if (width <= 0 || height <= 0) {
            throw new ImageFormatException("png", "BadIhdr", "PNG width and height must be positive.");
        }

        var validBitDepth = colorType switch {
            PngColorType.Grayscale => bitDepth is 1 or 2 or 4 or 8 or 16,
            PngColorType.Truecolor => bitDepth is 8 or 16,
            PngColorType.Indexed => bitDepth is 1 or 2 or 4 or 8,
            PngColorType.GrayscaleAlpha => bitDepth is 8 or 16,
            PngColorType.TruecolorAlpha => bitDepth is 8 or 16,
            _ => false,
        };
        if (!validBitDepth) {
            throw new ImageFormatException("png", "BadIhdr", $"Bit depth {bitDepth} is invalid for PNG color type {(byte)colorType}.");
        }

        if (compressionMethod != 0) {
            throw new ImageFormatException("png", "Unsupported.Png.CompressionMethod", $"Unknown IHDR compression method {compressionMethod}.");
        }

        if (filterMethod != 0) {
            throw new ImageFormatException("png", "Unsupported.Png.FilterMethod", $"Unknown IHDR filter method {filterMethod}.");
        }

        if (interlace is not PngInterlaceMethod.None and not PngInterlaceMethod.Adam7) {
            throw new ImageFormatException("png", "BadIhdr", $"Unknown PNG interlace method {(byte)interlace}.");
        }

        return new PngIhdr(width, height, bitDepth, colorType, interlace);
    }

    private static IReadOnlyList<PngPaletteEntry> ParsePalette(byte[] data)
    {
        if (data.Length is 0 or > 768 || data.Length % 3 != 0) {
            throw new ImageFormatException("png", "BadChunk", "PLTE length must be a non-zero multiple of three with at most 256 entries.");
        }

        var entries = new List<PngPaletteEntry>(data.Length / 3);
        for (var i = 0; i + 2 < data.Length; i += 3) {
            entries.Add(new PngPaletteEntry(data[i], data[i + 1], data[i + 2]));
        }

        return entries;
    }

    private static PngChromaticities ParseChromaticities(byte[] data)
    {
        if (data.Length != 32) {
            throw new ImageFormatException("png", "BadChunk", "cHRM chunk must be 32 bytes.");
        }

        PngChromaticity Read(int offset) => new(
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)) / 100000.0,
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4, 4)) / 100000.0);

        return new PngChromaticities {
            White = Read(0),
            Red = Read(8),
            Green = Read(16),
            Blue = Read(24),
        };
    }

    private static (string Name, byte[] Profile) ParseIccp(byte[] data)
    {
        var nameEnd = Array.IndexOf(data, (byte)0);
        if (nameEnd is < 1 or > 79 || nameEnd + 2 > data.Length) {
            throw new ImageFormatException("png", "BadChunk", "iCCP contains an invalid profile name or payload.");
        }
        var name = Encoding.Latin1.GetString(data, 0, nameEnd);
        var compressionMethod = data[nameEnd + 1];

        if (compressionMethod != 0) {
            throw new ImageFormatException("png", "Unsupported.Png.IccCompression", $"Unknown iCCP compression method {compressionMethod}.");
        }

        var compressed = data.AsSpan(nameEnd + 2).ToArray();
        return (name, Inflate(compressed));
    }

    private static PngTextEntry ParseTExt(byte[] data)
    {
        var keywordEnd = Array.IndexOf(data, (byte)0);
        ValidateKeywordEnd(keywordEnd, data.Length, "tEXt");
        var keyword = Encoding.Latin1.GetString(data, 0, keywordEnd);
        var text = Encoding.Latin1.GetString(data, keywordEnd + 1, data.Length - keywordEnd - 1);
        return new PngTextEntry { Keyword = keyword, Text = text };
    }

    private static PngTextEntry ParseZTxt(byte[] data)
    {
        var keywordEnd = Array.IndexOf(data, (byte)0);
        ValidateKeywordEnd(keywordEnd, data.Length - 1, "zTXt");
        if (data[keywordEnd + 1] != 0) {
            throw new ImageFormatException("png", "Unsupported.Png.TextCompression", $"Unknown text compression method {data[keywordEnd + 1]}.");
        }
        var keyword = Encoding.Latin1.GetString(data, 0, keywordEnd);
        var compressed = data.AsSpan(keywordEnd + 2).ToArray();
        var text = Encoding.Latin1.GetString(Inflate(compressed));
        return new PngTextEntry { Keyword = keyword, Text = text };
    }

    private static PngTextEntry ParseITxt(byte[] data)
    {
        var offset = 0;
        var keywordEnd = Array.IndexOf(data, (byte)0, offset);
        ValidateKeywordEnd(keywordEnd, data.Length - 4, "iTXt");
        var keyword = Encoding.UTF8.GetString(data, offset, keywordEnd - offset);
        offset = keywordEnd + 1;

        var compressionFlag = data[offset++];
        var compressionMethod = data[offset++];

        var languageEnd = Array.IndexOf(data, (byte)0, offset);
        if (languageEnd < offset) {
            throw new ImageFormatException("png", "BadChunk", "iTXt is missing the language terminator.");
        }
        offset = languageEnd + 1;

        var translatedKeywordEnd = Array.IndexOf(data, (byte)0, offset);
        if (translatedKeywordEnd < offset) {
            throw new ImageFormatException("png", "BadChunk", "iTXt is missing the translated-keyword terminator.");
        }
        offset = translatedKeywordEnd + 1;

        var remaining = data.AsSpan(offset).ToArray();
        var text = compressionFlag != 0
            ? Encoding.UTF8.GetString(Inflate(remaining, compressionMethod))
            : Encoding.UTF8.GetString(remaining);

        return new PngTextEntry { Keyword = keyword, Text = text };
    }

    private static void ValidateKeywordEnd(int keywordEnd, int maximum, string chunkType)
    {
        if (keywordEnd is < 1 or > 79 || keywordEnd > maximum) {
            throw new ImageFormatException("png", "BadChunk", $"{chunkType} contains an invalid keyword.");
        }
    }

    private static byte[] Inflate(byte[] compressed, byte compressionMethod = 0)
    {
        if (compressionMethod != 0) {
            throw new ImageFormatException("png", "Unsupported.Png.TextCompression", $"Unknown text compression method {compressionMethod}.");
        }

        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
