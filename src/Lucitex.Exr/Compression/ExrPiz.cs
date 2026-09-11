using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lucitex.Exr.Format;

namespace Lucitex.Exr.Compression;

internal static class ExrPiz
{
    private const int k_ValueRange = 1 << 16;
    private const int k_BitmapBytes = k_ValueRange >> 3;

    private readonly record struct ChannelPlane(int Start, int SampleCount, int RowCount, int WordsPerSample)
    {
        public int WordsPerRow => SampleCount * WordsPerSample;

        public int WordCount => WordsPerRow * RowCount;
    }

    public static byte[] Compress(ReadOnlySpan<byte> uncompressed, ExrBlockLayout layout)
    {
        var planes = BuildPlanes(layout, out var totalWords);
        if (totalWords == 0) {
            return [];
        }

        var words = ArrayPool<ushort>.Shared.Rent(totalWords);
        var lut = ArrayPool<ushort>.Shared.Rent(k_ValueRange);

        try {
            var samples = words.AsSpan(0, totalWords);
            Gather(uncompressed, layout, planes, samples);

            var bitmap = new byte[k_BitmapBytes];
            BuildBitmap(samples, bitmap, out var minNonZero, out var maxNonZero);

            var maxValue = BuildForwardLut(bitmap, lut);
            ApplyLut(lut, samples);

            for (var i = 0; i < planes.Length; i++) {
                var plane = planes[i];
                for (var component = 0; component < plane.WordsPerSample; component++) {
                    ExrWavelet.Encode(
                        samples,
                        plane.Start + component,
                        plane.SampleCount,
                        plane.WordsPerSample,
                        plane.RowCount,
                        plane.WordsPerRow,
                        maxValue);
                }
            }

            var payload = ExrHuffman.Compress(samples);
            var bitmapBytes = minNonZero <= maxNonZero ? maxNonZero - minNonZero + 1 : 0;

            var result = new byte[4 + bitmapBytes + 4 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0), (ushort)minNonZero);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)maxNonZero);
            bitmap.AsSpan(minNonZero, bitmapBytes).CopyTo(result.AsSpan(4));
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4 + bitmapBytes), payload.Length);
            payload.CopyTo(result.AsSpan(4 + bitmapBytes + 4));

            return result;
        }
        finally {
            ArrayPool<ushort>.Shared.Return(lut);
            ArrayPool<ushort>.Shared.Return(words);
        }
    }

    public static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> destination, ExrBlockLayout layout)
    {
        var planes = BuildPlanes(layout, out var totalWords);
        if (totalWords == 0) {
            return;
        }

        if (compressed.Length < 4) {
            throw new InvalidDataException("PIZ-compressed EXR data is truncated before its bitmap range.");
        }

        var minNonZero = BinaryPrimitives.ReadUInt16LittleEndian(compressed);
        var maxNonZero = BinaryPrimitives.ReadUInt16LittleEndian(compressed[2..]);

        if (maxNonZero >= k_BitmapBytes) {
            throw new InvalidDataException($"PIZ bitmap range ends at {maxNonZero}, past the {k_BitmapBytes}-byte bitmap.");
        }

        var bitmap = new byte[k_BitmapBytes];
        var bitmapBytes = minNonZero <= maxNonZero ? maxNonZero - minNonZero + 1 : 0;

        if (compressed.Length < 4 + bitmapBytes + 4) {
            throw new InvalidDataException("PIZ-compressed EXR data is truncated inside its bitmap.");
        }

        compressed.Slice(4, bitmapBytes).CopyTo(bitmap.AsSpan(minNonZero));

        var lut = ArrayPool<ushort>.Shared.Rent(k_ValueRange);
        var words = ArrayPool<ushort>.Shared.Rent(totalWords);

        try {
            var maxValue = BuildReverseLut(bitmap, lut);

            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(compressed[(4 + bitmapBytes)..]);
            var payloadStart = 4 + bitmapBytes + 4;

            if (payloadLength < 0 || payloadStart + payloadLength > compressed.Length) {
                throw new InvalidDataException($"PIZ payload declares {payloadLength} bytes, which is outside the chunk.");
            }

            var samples = words.AsSpan(0, totalWords);
            ExrHuffman.Uncompress(compressed.Slice(payloadStart, payloadLength), samples);

            for (var i = 0; i < planes.Length; i++) {
                var plane = planes[i];
                for (var component = 0; component < plane.WordsPerSample; component++) {
                    ExrWavelet.Decode(
                        samples,
                        plane.Start + component,
                        plane.SampleCount,
                        plane.WordsPerSample,
                        plane.RowCount,
                        plane.WordsPerRow,
                        maxValue);
                }
            }

            ApplyLut(lut, samples);
            Scatter(samples, layout, planes, destination);
        }
        finally {
            ArrayPool<ushort>.Shared.Return(words);
            ArrayPool<ushort>.Shared.Return(lut);
        }
    }

    private static ChannelPlane[] BuildPlanes(ExrBlockLayout layout, out int totalWords)
    {
        var planes = new ChannelPlane[layout.ChannelCount];
        var start = 0;

        for (var i = 0; i < layout.ChannelCount; i++) {
            var bytesPerSample = layout.BytesPerSample(i);
            if (bytesPerSample % 2 != 0) {
                throw new InvalidDataException($"PIZ compression requires 16-bit aligned samples; channel {i} uses {bytesPerSample} bytes.");
            }

            planes[i] = new ChannelPlane(start, layout.SampleCount(i), layout.SampledRowCount(i), bytesPerSample / 2);
            start = checked(start + planes[i].WordCount);
        }

        totalWords = start;
        return planes;
    }

    private static void Gather(ReadOnlySpan<byte> source, ExrBlockLayout layout, ChannelPlane[] planes, Span<ushort> words)
    {
        var written = new int[planes.Length];

        for (var row = 0; row < layout.RowCount; row++) {
            var rowOffset = checked((int)layout.RowOffset(row));

            for (var channel = 0; channel < planes.Length; channel++) {
                var channelOffset = layout.ChannelOffsetInRow(row, channel);
                if (channelOffset < 0) {
                    continue;
                }

                var plane = planes[channel];
                var destination = plane.Start + written[channel];

                ReadWords(source.Slice(rowOffset + channelOffset, plane.WordsPerRow * 2), words.Slice(destination, plane.WordsPerRow));
                written[channel] += plane.WordsPerRow;
            }
        }
    }

    private static void Scatter(ReadOnlySpan<ushort> words, ExrBlockLayout layout, ChannelPlane[] planes, Span<byte> destination)
    {
        var read = new int[planes.Length];

        for (var row = 0; row < layout.RowCount; row++) {
            var rowOffset = checked((int)layout.RowOffset(row));

            for (var channel = 0; channel < planes.Length; channel++) {
                var channelOffset = layout.ChannelOffsetInRow(row, channel);
                if (channelOffset < 0) {
                    continue;
                }

                var plane = planes[channel];
                var source = plane.Start + read[channel];

                WriteWords(words.Slice(source, plane.WordsPerRow), destination.Slice(rowOffset + channelOffset, plane.WordsPerRow * 2));
                read[channel] += plane.WordsPerRow;
            }
        }
    }

    private static void ReadWords(ReadOnlySpan<byte> source, Span<ushort> destination)
    {
        if (BitConverter.IsLittleEndian) {
            MemoryMarshal.Cast<byte, ushort>(source).CopyTo(destination);
            return;
        }

        for (var i = 0; i < destination.Length; i++) {
            destination[i] = BinaryPrimitives.ReadUInt16LittleEndian(source[(i * 2)..]);
        }
    }

    private static void WriteWords(ReadOnlySpan<ushort> source, Span<byte> destination)
    {
        if (BitConverter.IsLittleEndian) {
            source.CopyTo(MemoryMarshal.Cast<byte, ushort>(destination));
            return;
        }

        for (var i = 0; i < source.Length; i++) {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(i * 2)..], source[i]);
        }
    }

    private static void BuildBitmap(ReadOnlySpan<ushort> words, Span<byte> bitmap, out int minNonZero, out int maxNonZero)
    {
        foreach (var word in words) {
            bitmap[word >> 3] |= (byte)(1 << (word & 7));
        }

        bitmap[0] &= 0xFE;

        minNonZero = k_BitmapBytes - 1;
        maxNonZero = 0;

        for (var i = 0; i < k_BitmapBytes; i++) {
            if (bitmap[i] == 0) {
                continue;
            }

            minNonZero = Math.Min(minNonZero, i);
            maxNonZero = Math.Max(maxNonZero, i);
        }
    }

    private static ushort BuildForwardLut(ReadOnlySpan<byte> bitmap, Span<ushort> lut)
    {
        var next = 0;

        for (var i = 0; i < k_ValueRange; i++) {
            lut[i] = i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0 ? (ushort)next++ : (ushort)0;
        }

        return (ushort)(next - 1);
    }

    private static ushort BuildReverseLut(ReadOnlySpan<byte> bitmap, Span<ushort> lut)
    {
        var next = 0;

        for (var i = 0; i < k_ValueRange; i++) {
            if (i == 0 || (bitmap[i >> 3] & (1 << (i & 7))) != 0) {
                lut[next++] = (ushort)i;
            }
        }

        var maxValue = next - 1;
        while (next < k_ValueRange) {
            lut[next++] = 0;
        }

        return (ushort)maxValue;
    }

    private static void ApplyLut(ReadOnlySpan<ushort> lut, Span<ushort> words)
    {
        for (var i = 0; i < words.Length; i++) {
            words[i] = lut[words[i]];
        }
    }
}
