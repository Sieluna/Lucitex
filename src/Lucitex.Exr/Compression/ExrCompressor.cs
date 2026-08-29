using Lucitex.Exr.Format;

namespace Lucitex.Exr.Compression;

internal static class ExrCompressor
{
    public static int NumScanlinesPerChunk(ExrCompressionId compression) => compression switch {
        ExrCompressionId.None => 1,
        ExrCompressionId.Rle => 1,
        ExrCompressionId.Zips => 1,
        ExrCompressionId.Zip => 16,
        ExrCompressionId.Piz => 32,
        ExrCompressionId.Pxr24 => 16,
        ExrCompressionId.B44 => 32,
        ExrCompressionId.B44A => 32,
        ExrCompressionId.Dwaa => 32,
        ExrCompressionId.Dwab => 256,
        _ => throw new ArgumentOutOfRangeException(nameof(compression)),
    };

    public static bool IsSupported(ExrCompressionId compression) => compression is
        ExrCompressionId.None or ExrCompressionId.Rle or ExrCompressionId.Zips or ExrCompressionId.Zip or ExrCompressionId.Piz;

    public static byte[] Compress(ExrCompressionId compression, ReadOnlySpan<byte> uncompressed, ExrBlockLayout layout)
    {
        if (compression == ExrCompressionId.None) {
            return uncompressed.ToArray();
        }

        var compressed = compression switch {
            ExrCompressionId.Rle => ExrRle.Compress(uncompressed),
            ExrCompressionId.Zips => ExrZip.Compress(uncompressed),
            ExrCompressionId.Zip => ExrZip.Compress(uncompressed),
            ExrCompressionId.Piz => ExrPiz.Compress(uncompressed, layout),
            _ => throw new NotSupportedException($"EXR compression '{compression}' is not supported."),
        };

        return compressed.Length < uncompressed.Length ? compressed : uncompressed.ToArray();
    }

    public static void Decompress(ExrCompressionId compression, ReadOnlySpan<byte> compressed, Span<byte> destination, ExrBlockLayout layout)
    {
        if (compressed.Length == destination.Length) {
            compressed.CopyTo(destination);
            return;
        }

        if (compressed.Length > destination.Length) {
            throw new InvalidDataException($"Compressed EXR chunk has {compressed.Length} bytes; expected at most {destination.Length}.");
        }

        switch (compression) {
            case ExrCompressionId.None:
                throw new InvalidDataException($"Uncompressed EXR chunk has {compressed.Length} bytes; expected {destination.Length}.");
            case ExrCompressionId.Rle:
                var written = ExrRle.Decompress(compressed, destination);
                if (written != destination.Length) {
                    throw new InvalidDataException($"RLE EXR chunk expands to {written} bytes; expected {destination.Length}.");
                }

                break;
            case ExrCompressionId.Zips:
            case ExrCompressionId.Zip:
                ExrZip.Decompress(compressed, destination);
                break;
            case ExrCompressionId.Piz:
                ExrPiz.Decompress(compressed, destination, layout);
                break;
            default:
                throw new NotSupportedException($"EXR compression '{compression}' is not supported.");
        }
    }
}
