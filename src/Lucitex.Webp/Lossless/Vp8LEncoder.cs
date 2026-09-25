using System.Buffers.Binary;

namespace Lucitex.Webp.Lossless;

internal static class Vp8LEncoder
{
    private const int k_PredictorBits = 4;

    public static void Encode(Stream stream, Span<uint> pixels, int width, int height, WebpEncoderOptions options, WebpMemory memory, WebpMetadata metadata)
    {
        var alpha = false;
        foreach (var pixel in pixels) {
            alpha |= (pixel >> 24) != 255;
        }
        memory.Reserve(128 * 1024);
        try {
            var subtractGreen = UseSubtractGreen(pixels);
            if (subtractGreen) {
                Vp8LTransforms.SubtractGreen(pixels);
            }
            using var modes = ChoosePredictors(pixels, width, height, memory, fast: options.Effort == WebpCompressionEffort.Fast);
            if (modes is not null) {
                ApplyPredictors(pixels, width, height, modes.Span);
            }
            var candidates = options.Effort == WebpCompressionEffort.Fast ? 1 : 4;
            using var positions = memory.Rent<int>(Vp8LMatchFinder.TableSize(pixels.Length, candidates));
            using var image = new Vp8LEntropyEncoder(pixels, width, positions.Span, candidates, memory);
            var modeWidth = Vp8LTransforms.Subsample(width, k_PredictorBits);
            using var predictorImage = modes is null ? null : new Vp8LEntropyEncoder(modes.Span, modeWidth, positions.Span, candidates, memory);
            using var counter = new Vp8LBitWriter(Stream.Null, memory);
            WriteHeader(counter, subtractGreen, modes, predictorImage, image);
            var payloadSize = checked(5 + (int)((counter.TotalBits + image.DataBits + 7) / 8));
            WebpContainer.WriteHeader(stream, payloadSize, width, height, alpha, metadata, true);
            Span<byte> header = stackalloc byte[5];
            header[0] = 0x2f;
            BinaryPrimitives.WriteUInt32LittleEndian(header[1..], (uint)(width - 1) | ((uint)(height - 1) << 14) | (alpha ? 1u << 28 : 0));
            stream.Write(header);
            using var writer = new Vp8LBitWriter(stream, memory);
            WriteHeader(writer, subtractGreen, modes, predictorImage, image);
            image.WritePixels(writer, pixels);
            writer.Finish();
            if ((payloadSize & 1) != 0) {
                stream.WriteByte(0);
            }
            WebpContainer.WriteTrailingMetadata(stream, metadata);
        }
        finally {
            memory.Release(128 * 1024);
        }
    }

    private static void WriteHeader(Vp8LBitWriter writer, bool subtractGreen, WebpBuffer<uint>? modes, Vp8LEntropyEncoder? predictorImage, Vp8LEntropyEncoder image)
    {
        if (subtractGreen) {
            writer.Write(1, 1);
            writer.Write(2, 2);
        }
        if (modes is not null) {
            writer.Write(1, 1);
            writer.Write(0, 2);
            writer.Write(k_PredictorBits - 2, 3);
            predictorImage!.WriteHeader(writer, false);
            predictorImage.WritePixels(writer, modes.Span);
        }
        writer.Write(0, 1);
        image.WriteHeader(writer, true);
    }

    private static bool UseSubtractGreen(ReadOnlySpan<uint> pixels)
    {
        Span<int> histogram = stackalloc int[1024];
        histogram.Clear();
        var step = Math.Max(1, pixels.Length / 4096);
        for (var i = 0; i < pixels.Length; i += step) {
            var pixel = pixels[i];
            var red = (byte)(pixel >> 16);
            var green = (byte)(pixel >> 8);
            var blue = (byte)pixel;
            histogram[red]++;
            histogram[256 + blue]++;
            histogram[512 + (byte)(red - green)]++;
            histogram[768 + (byte)(blue - green)]++;
        }
        double original = 0;
        double transformed = 0;
        for (var i = 0; i < 512; i++) {
            var a = histogram[i];
            var b = histogram[512 + i];
            original += a > 0 ? a * Math.Log2(a) : 0;
            transformed += b > 0 ? b * Math.Log2(b) : 0;
        }
        return transformed > original + 8;
    }

    private static WebpBuffer<uint>? ChoosePredictors(ReadOnlySpan<uint> pixels, int width, int height, WebpMemory memory, bool fast)
    {
        var modeWidth = Vp8LTransforms.Subsample(width, k_PredictorBits);
        var modeHeight = Vp8LTransforms.Subsample(height, k_PredictorBits);
        var modes = memory.Rent<uint>(modeWidth * modeHeight);
        long originalScore = 0;
        long predictedScore = 0;
        ReadOnlySpan<int> choices = fast ? [2, 7] : [1, 2, 7, 12];
        var sampleStep = fast ? 4 : 2;
        Span<long> scores = stackalloc long[choices.Length];
        for (var by = 0; by < modeHeight; by++) {
            for (var bx = 0; bx < modeWidth; bx++) {
                scores.Clear();
                var endY = Math.Min(height, (by + 1) << k_PredictorBits);
                var endX = Math.Min(width, (bx + 1) << k_PredictorBits);
                for (var y = by << k_PredictorBits; y < endY; y += sampleStep) {
                    for (var x = bx << k_PredictorBits; x < endX; x += sampleStep) {
                        var index = (y * width) + x;
                        originalScore += Score(pixels[index]);
                        for (var c = 0; c < choices.Length; c++) {
                            var prediction = Prediction(pixels, index, x, y, width, choices[c]);
                            scores[c] += Score(Vp8LTransforms.Subtract(pixels[index], prediction));
                        }
                    }
                }
                var best = 0;
                for (var c = 1; c < choices.Length; c++) {
                    if (scores[c] < scores[best]) {
                        best = c;
                    }
                }
                predictedScore += scores[best];
                modes.Span[(by * modeWidth) + bx] = 0xff000000 | ((uint)choices[best] << 8);
            }
        }
        if (predictedScore + (modes.Length * 8L) >= originalScore) {
            modes.Dispose();
            return null;
        }
        return modes;
    }

    private static void ApplyPredictors(Span<uint> pixels, int width, int height, ReadOnlySpan<uint> modes)
    {
        var modeWidth = Vp8LTransforms.Subsample(width, k_PredictorBits);
        for (var y = height - 1; y >= 0; y--) {
            for (var x = width - 1; x >= 0; x--) {
                var index = (y * width) + x;
                var mode = (int)((modes[((y >> k_PredictorBits) * modeWidth) + (x >> k_PredictorBits)] >> 8) & 15);
                pixels[index] = Vp8LTransforms.Subtract(pixels[index], Prediction(pixels, index, x, y, width, mode));
            }
        }
    }

    private static uint Prediction(ReadOnlySpan<uint> pixels, int index, int x, int y, int width, int mode)
        => y == 0 ? (x == 0 ? 0xff000000 : pixels[index - 1]) : x == 0 ? pixels[index - width] :
            Vp8LTransforms.Predict(mode, pixels[index - 1], pixels[index - width], pixels[index - width - 1], pixels[index - width + 1]);

    private static int Score(uint pixel)
    {
        var result = 0;
        for (var shift = 0; shift < 32; shift += 8) {
            var channel = (int)((pixel >> shift) & 255);
            result += Math.Min(channel, 256 - channel);
        }
        return result;
    }
}
