using Lucitex.Exr.Format;

namespace Lucitex.Exr.Compression;

internal static class ExrCompressor
{
    public static int NumScanlinesPerChunk(ExrCompressionId compression) => compression switch
    {
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
        ExrCompressionId.None or ExrCompressionId.Rle or ExrCompressionId.Zips or ExrCompressionId.Zip;

    public static byte[] Compress(ExrCompressionId compression, ReadOnlySpan<byte> uncompressed) => compression switch
    {
        ExrCompressionId.None => uncompressed.ToArray(),
        ExrCompressionId.Rle => ExrRle.Compress(uncompressed),
        ExrCompressionId.Zips => ExrZip.Compress(uncompressed),
        ExrCompressionId.Zip => ExrZip.Compress(uncompressed),
        _ => throw new NotSupportedException($"EXR compression '{compression}' is not supported."),
    };

    public static void Decompress(ExrCompressionId compression, ReadOnlySpan<byte> compressed, Span<byte> destination)
    {
        switch (compression)
        {
            case ExrCompressionId.None:
                compressed.CopyTo(destination);
                break;
            case ExrCompressionId.Rle:
                ExrRle.Decompress(compressed, destination);
                break;
            case ExrCompressionId.Zips:
            case ExrCompressionId.Zip:
                ExrZip.Decompress(compressed, destination);
                break;
            default:
                throw new NotSupportedException($"EXR compression '{compression}' is not supported.");
        }
    }
}
