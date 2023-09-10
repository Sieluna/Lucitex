using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lucitex.TextureCompression;

internal static class BcImageCodec
{
    public static int ChannelCount(BcFormat format) => format switch {
        BcFormat.Bc1 or BcFormat.Bc2 or BcFormat.Bc3 or BcFormat.Bc7 => 4,
        BcFormat.Bc4 => 1,
        BcFormat.Bc5 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static int BlockByteSize(BcFormat format) => format switch {
        BcFormat.Bc1 or BcFormat.Bc4 => 8,
        BcFormat.Bc2 or BcFormat.Bc3 or BcFormat.Bc5 or BcFormat.Bc7 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static int EncodedByteCount(BcFormat format, int width, int height) =>
        checked(((width + 3) / 4) * ((height + 3) / 4) * BlockByteSize(format));

    public static void Decode(BcFormat format, ReadOnlySpan<byte> source, int width, int height, Span<byte> destination)
    {
        ValidateDimensions(width, height);
        var channels = ChannelCount(format);
        var encodedBytes = EncodedByteCount(format, width, height);
        var decodedBytes = checked(width * height * channels);
        if (source.Length < encodedBytes || destination.Length < decodedBytes) {
            throw new ArgumentException("BC buffers are too small for the image dimensions.");
        }

        var blockBytes = BlockByteSize(format);
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        Span<byte> blockPixels = stackalloc byte[64];
        for (var blockY = 0; blockY < blocksHigh; blockY++) {
            for (var blockX = 0; blockX < blocksWide; blockX++) {
                var blockIndex = (blockY * blocksWide) + blockX;
                DecodeBlock(format, source.Slice(blockIndex * blockBytes, blockBytes), blockPixels);
                StoreBlock(blockPixels, destination, width, height, channels, blockX * 4, blockY * 4);
            }
        }
    }

    public static void Encode(BcFormat format, ReadOnlySpan<byte> source, int width, int height, Span<byte> destination)
    {
        ValidateDimensions(width, height);
        var channels = ChannelCount(format);
        var decodedBytes = checked(width * height * channels);
        var encodedBytes = EncodedByteCount(format, width, height);
        if (source.Length < decodedBytes || destination.Length < encodedBytes) {
            throw new ArgumentException("BC buffers are too small for the image dimensions.");
        }

        var blockBytes = BlockByteSize(format);
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        Span<byte> blockPixels = stackalloc byte[64];
        for (var blockY = 0; blockY < blocksHigh; blockY++) {
            for (var blockX = 0; blockX < blocksWide; blockX++) {
                LoadBlock(source, blockPixels, width, height, channels, blockX * 4, blockY * 4);
                var blockIndex = (blockY * blocksWide) + blockX;
                EncodeBlock(format, blockPixels, destination.Slice(blockIndex * blockBytes, blockBytes));
            }
        }
    }

    private static void DecodeBlock(BcFormat format, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        switch (format) {
            case BcFormat.Bc1:
                Bc1Codec.Decode(source, destination);
                break;
            case BcFormat.Bc2:
                Bc2Codec.Decode(source, destination);
                break;
            case BcFormat.Bc3:
                Bc3Codec.Decode(source, destination);
                break;
            case BcFormat.Bc4:
                Bc4Codec.Decode(source, destination);
                break;
            case BcFormat.Bc5:
                Bc5Codec.Decode(source, destination);
                break;
            case BcFormat.Bc7:
                Bc7Codec.Decode(source, destination);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void EncodeBlock(BcFormat format, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        switch (format) {
            case BcFormat.Bc1:
                Bc1Codec.Encode(source, destination);
                break;
            case BcFormat.Bc2:
                Bc2Codec.Encode(source, destination);
                break;
            case BcFormat.Bc3:
                Bc3Codec.Encode(source, destination);
                break;
            case BcFormat.Bc4:
                Bc4Codec.Encode(source, destination);
                break;
            case BcFormat.Bc5:
                Bc5Codec.Encode(source, destination);
                break;
            case BcFormat.Bc7:
                throw new NotSupportedException("BC7 encoding is not available.");
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void StoreBlock(ReadOnlySpan<byte> block, Span<byte> destination, int width, int height, int channels, int originX, int originY)
    {
        var copyWidth = Math.Min(4, width - originX);
        var copyHeight = Math.Min(4, height - originY);
        var rowBytes = copyWidth * channels;
        for (var y = 0; y < copyHeight; y++) {
            var sourceRow = block.Slice(y * 4 * channels, rowBytes);
            var destinationRow = destination.Slice((((originY + y) * width) + originX) * channels, rowBytes);
            CopyRow(sourceRow, destinationRow);
        }
    }

    private static void LoadBlock(ReadOnlySpan<byte> source, Span<byte> block, int width, int height, int channels, int originX, int originY)
    {
        for (var y = 0; y < 4; y++) {
            var sourceY = Math.Min(originY + y, height - 1);
            for (var x = 0; x < 4; x++) {
                var sourceX = Math.Min(originX + x, width - 1);
                source.Slice(((sourceY * width) + sourceX) * channels, channels)
                    .CopyTo(block.Slice(((y * 4) + x) * channels, channels));
            }
        }
    }

    private static void CopyRow(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (Vector128.IsHardwareAccelerated && source.Length >= Vector128<byte>.Count) {
            ref var sourceReference = ref MemoryMarshal.GetReference(source);
            ref var destinationReference = ref MemoryMarshal.GetReference(destination);
            Vector128.LoadUnsafe(ref sourceReference).StoreUnsafe(ref destinationReference);
            source = source[Vector128<byte>.Count..];
            destination = destination[Vector128<byte>.Count..];
        }

        source.CopyTo(destination);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), "BC image dimensions must be positive.");
        }
    }
}
