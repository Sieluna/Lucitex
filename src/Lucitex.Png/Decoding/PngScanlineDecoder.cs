using System.Buffers;
using System.IO.Compression;
using Lucitex.Core.Execution;
using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Png.Decoding;

internal static class PngScanlineDecoder
{
    private const int k_MinCompressedBytesForOverlap = 64 * 1024;
    private const int k_MinDecodedBytesForOverlap = 256 * 1024;
    private const int k_MinRowsForOverlap = 128;
    private const int k_OverlappedBatchBytes = 128 * 1024;
    private const int k_SynchronousBatchBytes = 32 * 1024;
    private const int k_MaxOverlappedRows = 64;
    private const int k_MaxSynchronousRows = 32;

    private static bool ShouldOverlapInflation(PngIhdr ihdr, long compressedBytes) =>
        Environment.ProcessorCount > 1 && compressedBytes >= k_MinCompressedBytesForOverlap &&
        (long)ihdr.RowByteLength(ihdr.Width) * ihdr.Height >= k_MinDecodedBytesForOverlap && ihdr.Height >= k_MinRowsForOverlap;

    public static void Decode(PngIhdr ihdr, IReadOnlyList<ReadOnlyMemory<byte>> compressedChunks, byte[] buffer)
    {
        using var input = new PngCompressedStream(compressedChunks);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        if (ihdr.Interlace == PngInterlaceMethod.None) {
            DecodeNonInterlaced(ihdr, zlib, buffer, ShouldOverlapInflation(ihdr, compressedChunks.Sum(chunk => (long)chunk.Length)));
        }
        else {
            DecodeAdam7(ihdr, zlib, buffer);
        }

        if (zlib.ReadByte() != -1) {
            throw new ImageFormatException("png", "BadImageData", "Inflated PNG image data is longer than expected.");
        }
    }

    private static void DecodeNonInterlaced(PngIhdr ihdr, Stream inflated, byte[] buffer, bool overlap)
    {
        var rowBytes = ihdr.RowByteLength(ihdr.Width);
        var bytesPerPixel = ihdr.BytesPerPixel;
        var filteredRowBytes = checked(rowBytes + 1);
        var batchByteBudget = overlap ? k_OverlappedBatchBytes : k_SynchronousBatchBytes;
        var maxBatchRows = overlap ? k_MaxOverlappedRows : k_MaxSynchronousRows;
        var rowsPerBatch = Math.Clamp(batchByteBudget / filteredRowBytes, 1, maxBatchRows);
        var currentBatch = ArrayPool<byte>.Shared.Rent(checked(filteredRowBytes * rowsPerBatch));
        var nextBatch = overlap ? ArrayPool<byte>.Shared.Rent(checked(filteredRowBytes * rowsPerBatch)) : [];
        Task? pendingInflation = null;
        try {
            inflated.ReadExactly(currentBatch.AsSpan(0, Math.Min(rowsPerBatch, ihdr.Height) * filteredRowBytes));
            for (var start = 0; start < ihdr.Height; start += rowsPerBatch) {
                var rows = Math.Min(rowsPerBatch, ihdr.Height - start);
                var nextRows = Math.Min(rowsPerBatch, ihdr.Height - start - rows);
                if (overlap && nextRows > 0) {
                    pendingInflation = Task.Run(() => inflated.ReadExactly(nextBatch.AsSpan(0, nextRows * filteredRowBytes)));
                }
                for (var i = 0; i < rows; i++) {
                    var y = start + i;
                    var row = buffer.AsSpan(y * rowBytes, rowBytes);
                    currentBatch.AsSpan(i * filteredRowBytes + 1, rowBytes).CopyTo(row);
                    var previous = y > 0 ? buffer.AsSpan((y - 1) * rowBytes, rowBytes) : ReadOnlySpan<byte>.Empty;
                    PngFilter.Reconstruct((PngFilterType)currentBatch[i * filteredRowBytes], row, previous, bytesPerPixel);
                }
                pendingInflation?.GetAwaiter().GetResult();
                pendingInflation = null;
                if (overlap) {
                    (currentBatch, nextBatch) = (nextBatch, currentBatch);
                }
                else if (nextRows > 0) {
                    inflated.ReadExactly(currentBatch.AsSpan(0, nextRows * filteredRowBytes));
                }
            }
        }
        finally {
            try {
                pendingInflation?.GetAwaiter().GetResult();
            }
            finally {
                ArrayPool<byte>.Shared.Return(currentBatch);
                if (overlap) {
                    ArrayPool<byte>.Shared.Return(nextBatch);
                }
            }
        }
    }

    private static void DecodeAdam7(PngIhdr ihdr, Stream inflated, byte[] buffer)
    {
        var samplesPerPixel = ihdr.SamplesPerPixel;
        var bitDepth = ihdr.BitDepth;
        var bytesPerPixel = ihdr.BytesPerPixel;
        var finalRowBytes = ihdr.RowByteLength(ihdr.Width);

        for (var passIndex = 0; passIndex < 7; passIndex++) {
            var (passWidth, passHeight) = Adam7.PassDimensions(ihdr.Width, ihdr.Height, passIndex);
            if (passWidth == 0 || passHeight == 0) {
                continue;
            }

            var (xStart, yStart, xStep, yStep) = Adam7.Passes[passIndex];
            var passRowBytes = ihdr.RowByteLength(passWidth);
            var previousPassRow = new byte[passRowBytes];
            var currentRow = new byte[passRowBytes];
            var hasPreviousRow = false;

            for (var py = 0; py < passHeight; py++) {
                var filterType = (PngFilterType)inflated.ReadByte();
                inflated.ReadExactly(currentRow);

                PngFilter.Reconstruct(filterType, currentRow, hasPreviousRow ? previousPassRow : ReadOnlySpan<byte>.Empty, bytesPerPixel);

                var y = yStart + (py * yStep);
                var finalRow = buffer.AsSpan(y * finalRowBytes, finalRowBytes);

                for (var px = 0; px < passWidth; px++) {
                    var x = xStart + (px * xStep);
                    for (var s = 0; s < samplesPerPixel; s++) {
                        var value = PngBitPacking.ReadSample(currentRow, (px * samplesPerPixel) + s, bitDepth);
                        PngBitPacking.WriteSample(finalRow, (x * samplesPerPixel) + s, bitDepth, value);
                    }
                }

                (previousPassRow, currentRow) = (currentRow, previousPassRow);
                hasPreviousRow = true;
            }
        }
    }
}
