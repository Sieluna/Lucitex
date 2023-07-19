using System.IO.Compression;

namespace Lucitex.Exr.Compression;

internal static class ExrZip
{
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        var reordered = new byte[input.Length];
        ByteReorder.Split(input, reordered);
        BytePredictor.Apply(reordered);

        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(reordered);
        }

        return output.ToArray();
    }

    public static void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var reordered = new byte[output.Length];

        using (var inputStream = new MemoryStream(input.ToArray()))
        using (var zlib = new ZLibStream(inputStream, CompressionMode.Decompress))
        {
            zlib.ReadExactly(reordered);
        }

        BytePredictor.Remove(reordered);
        ByteReorder.Interleave(reordered, output);
    }
}
