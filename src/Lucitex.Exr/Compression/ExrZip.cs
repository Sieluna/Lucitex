using System.Buffers;
using System.IO.Compression;

namespace Lucitex.Exr.Compression;

internal static class ExrZip
{
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        var rented = ArrayPool<byte>.Shared.Rent(input.Length);
        byte[]? compressed = null;
        try {
            var reordered = rented.AsSpan(0, input.Length);
            ByteReorder.Split(input, reordered);
            BytePredictor.Apply(reordered);

            compressed = ArrayPool<byte>.Shared.Rent(checked(input.Length + (input.Length / 8) + 64));
            using var output = new MemoryStream(compressed, 0, compressed.Length, writable: true, publiclyVisible: true);
            output.SetLength(0);
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) {
                zlib.Write(reordered);
            }

            return output.ToArray();
        }
        finally {
            ArrayPool<byte>.Shared.Return(rented);
            if (compressed is not null) {
                ArrayPool<byte>.Shared.Return(compressed);
            }
        }
    }

    public static void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var rented = ArrayPool<byte>.Shared.Rent(output.Length);
        var source = ArrayPool<byte>.Shared.Rent(input.Length);
        try {
            input.CopyTo(source);
            var reordered = rented.AsSpan(0, output.Length);

            using (var inputStream = new MemoryStream(source, 0, input.Length))
            using (var zlib = new ZLibStream(inputStream, CompressionMode.Decompress)) {
                zlib.ReadExactly(reordered);
                if (zlib.ReadByte() != -1) {
                    throw new InvalidDataException("EXR ZIP chunk expands beyond its expected size.");
                }
            }

            BytePredictor.Remove(reordered);
            ByteReorder.Interleave(reordered, output);
        }
        finally {
            ArrayPool<byte>.Shared.Return(source);
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
