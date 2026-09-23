using System.Security.Cryptography;

namespace Lucitex.Benchmarks.Validation;

internal sealed record JpegStructure(int Width, int Height, string Sampling, bool Progressive, string HuffmanSha256)
{
    public static JpegStructure Read(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216 || bytes[^2] != 255 || bytes[^1] != 217) {
            throw new InvalidDataException("Missing JPEG boundary markers.");
        }
        using var tables = new MemoryStream();
        var width = 0;
        var height = 0;
        var sampling = "";
        var progressive = false;
        for (var offset = 2; offset + 4 <= bytes.Length;) {
            if (bytes[offset++] != 255) {
                throw new InvalidDataException("Invalid JPEG segment marker.");
            }
            var marker = bytes[offset++];
            var length = (bytes[offset] << 8) | bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length) {
                throw new InvalidDataException("Invalid JPEG segment length.");
            }
            if (marker == 0xC4) {
                tables.Write(bytes.AsSpan(offset + 2, length - 2));
            }
            if (marker is 0xC0 or 0xC2) {
                height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                var components = bytes[offset + 7];
                sampling = string.Join(",", Enumerable.Range(0, components).Select(i => bytes[offset + 9 + i * 3].ToString("X2")));
                progressive = marker == 0xC2;
            }
            if (marker == 0xDA) {
                break;
            }
            offset += length;
        }
        return new JpegStructure(width, height, sampling, progressive, Convert.ToHexString(SHA256.HashData(tables.ToArray())));
    }
}
