using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lucitex.Compression;

internal static class Bc6HImageCodec
{
    private const int k_ParallelBlockThreshold = 256;

    public static int EncodedByteCount(int width, int height)
    {
        ValidateDimensions(width, height);
        return checked(((width + 3) / 4) * ((height + 3) / 4) * 16);
    }

    public static void Decode(ReadOnlySpan<byte> source, int width, int height, bool signed, Span<float> destination)
    {
        var encodedByteCount = EncodedByteCount(width, height);
        var decodedValueCount = checked(width * height * 3);
        if (source.Length < encodedByteCount || destination.Length < decodedValueCount) {
            throw new ArgumentException("BC6H buffers are too small for the image dimensions.");
        }

        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        Span<float> blockPixels = stackalloc float[48];
        for (var blockY = 0; blockY < blocksHigh; blockY++) {
            for (var blockX = 0; blockX < blocksWide; blockX++) {
                var blockIndex = (blockY * blocksWide) + blockX;
                Bc6HCodec.Decode(source.Slice(blockIndex * 16, 16), blockPixels, signed);
                StoreBlock(blockPixels, destination, width, height, blockX * 4, blockY * 4);
            }
        }
    }

    public static void Decode(byte[] source, int width, int height, bool signed, float[] destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var encodedByteCount = EncodedByteCount(width, height);
        var decodedValueCount = checked(width * height * 3);
        if (source.Length < encodedByteCount || destination.Length < decodedValueCount) {
            throw new ArgumentException("BC6H buffers are too small for the image dimensions.");
        }

        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        if (blocksWide * blocksHigh < k_ParallelBlockThreshold || Environment.ProcessorCount == 1) {
            Decode(source.AsSpan(), width, height, signed, destination.AsSpan());
            return;
        }

        Parallel.For(0, blocksHigh, blockY => {
            Span<float> blockPixels = stackalloc float[48];
            for (var blockX = 0; blockX < blocksWide; blockX++) {
                var blockIndex = (blockY * blocksWide) + blockX;
                Bc6HCodec.Decode(source.AsSpan(blockIndex * 16, 16), blockPixels, signed);
                StoreBlock(blockPixels, destination, width, height, blockX * 4, blockY * 4);
            }
        });
    }

    private static void StoreBlock(ReadOnlySpan<float> block, Span<float> destination, int width, int height, int originX, int originY)
    {
        var copyWidth = Math.Min(4, width - originX);
        var copyHeight = Math.Min(4, height - originY);
        var rowValues = copyWidth * 3;
        for (var y = 0; y < copyHeight; y++) {
            var sourceRow = block.Slice(y * 12, rowValues);
            var destinationRow = destination.Slice((((originY + y) * width) + originX) * 3, rowValues);
            CopyRow(sourceRow, destinationRow);
        }
    }

    private static void CopyRow(ReadOnlySpan<float> source, Span<float> destination)
    {
        if (Vector128.IsHardwareAccelerated) {
            ref var sourceReference = ref MemoryMarshal.GetReference(source);
            ref var destinationReference = ref MemoryMarshal.GetReference(destination);
            var offset = 0;
            for (; offset <= source.Length - Vector128<float>.Count; offset += Vector128<float>.Count) {
                Vector128.LoadUnsafe(ref sourceReference, (nuint)offset).StoreUnsafe(ref destinationReference, (nuint)offset);
            }

            source = source[offset..];
            destination = destination[offset..];
        }

        source.CopyTo(destination);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), "BC6H image dimensions must be positive.");
        }
    }
}
