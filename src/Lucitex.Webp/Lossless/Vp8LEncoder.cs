using System.Buffers.Binary;
using System.Runtime.CompilerServices;

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
        Vp8LBitWriter? bestBits = null;
        WebpBuffer<uint>? previousModes = null;
        try {
            var subtractGreen = UseSubtractGreen(pixels);
            if (subtractGreen) {
                Vp8LTransforms.SubtractGreen(pixels);
            }
            var settings = WebpEffortSettings.For(options.Effort);
            var maxTier = settings.PredictorTier;
            var uniform = !pixels.ContainsAnyExcept(pixels[0]);
            if (uniform) {
                maxTier = 0;
            }
            using var work = memory.Rent<uint>(pixels.Length);
            using var tokens = memory.Rent<Vp8LToken>(pixels.Length);
            using var positions = memory.Rent<int>(Vp8LMatchFinder.TableSize(pixels.Length, uniform ? 1 : settings.MatchCandidates));
            var modeWidth = Vp8LTransforms.Subsample(width, k_PredictorBits);
            var bestPayloadSize = 0;
            long bestPaddedSize = long.MaxValue;
            ReadOnlySpan<int> fixedModes = [-1, 1, 2, 7, 12];
            var fixedPasses = options.Effort == WebpCompressionEffort.Fast ? 1 : 2;
            var plans = uniform ? 1 : options.Effort != WebpCompressionEffort.Fast
                ? maxTier + 1 + fixedModes.Length * fixedPasses : 0;
            bestBits = CreatePaletteCandidate(pixels, width, height, subtractGreen, memory, work.Span, positions.Span, tokens, settings, out var compactPalette);
            if (bestBits is not null) {
                bestPayloadSize = checked(5 + bestBits.WrittenSpan.Length);
                bestPaddedSize = (long)bestPayloadSize + (bestPayloadSize & 1);
            }
            var paletteOnly = !uniform && options.Effort != WebpCompressionEffort.Best && compactPalette;
            var sampled = uniform || paletteOnly ? (First: -1, Second: -1, LimitSearch: false) : SelectPredictors(pixels, width);
            var quickPlans = uniform || paletteOnly ? 0 : options.Effort == WebpCompressionEffort.Fast ? 1 : sampled.LimitSearch ? 2 : 5;
            var residualReady = false;
            var previousCandidates = 0;
            for (var plan = -quickPlans; plan < plans; plan++) {
                if (plan == 0 && !uniform && options.Effort == WebpCompressionEffort.Balanced
                    && (!sampled.LimitSearch || bestPaddedSize * 4 >= pixels.Length * 9L)) {
                    break;
                }
                var tier = plan <= maxTier ? plan : 0;
                WebpBuffer<uint>? modes;
                var quick = plan + quickPlans;
                if (plan < 0) {
                    if (quickPlans > 1 && quick == quickPlans - 1) {
                        modes = ChoosePredictors(pixels, width, height, memory, 0);
                    }
                    else if (quick == 3) {
                        modes = ChoosePredictors(pixels, width, height, memory, 2);
                    }
                    else {
                        var mode = quick <= 1 ? sampled.First : sampled.Second;
                        modes = mode < 0 ? null : memory.Rent<uint>(modeWidth * Vp8LTransforms.Subsample(height, k_PredictorBits));
                        modes?.Span.Fill(0xff000000 | ((uint)mode << 8));
                    }
                }
                else if (plan <= maxTier) {
                    modes = uniform ? null : ChoosePredictors(pixels, width, height, memory, tier);
                }
                else {
                    var mode = fixedModes[(plan - maxTier - 1) % fixedModes.Length];
                    modes = mode < 0 ? null : memory.Rent<uint>(modeWidth * Vp8LTransforms.Subsample(height, k_PredictorBits));
                    modes?.Span.Fill(0xff000000 | ((uint)mode << 8));
                }
                var samePredictors = modes is null ? previousModes is null :
                    previousModes is not null && modes.Span.SequenceEqual(previousModes.Span);
                previousModes?.Dispose();
                previousModes = modes;
                if (!residualReady || !samePredictors) {
                    pixels.CopyTo(work.Span);
                    if (modes is not null) {
                        ApplyPredictors(work.Span, width, height, modes.Span);
                    }
                    residualReady = true;
                }
                var candidates = plan < 0 ? quick == quickPlans - 1 ? 1 : quick == 3 ? 4 : quick == 0 ? 1 : 16 : plan <= maxTier ? 1 << tier
                    : (plan - maxTier - 1) / fixedModes.Length == 0 ? 1 : settings.MatchCandidates;
                if (samePredictors && candidates == previousCandidates) {
                    continue;
                }
                previousCandidates = candidates;
                var table = positions.Span[..Vp8LMatchFinder.TableSize(pixels.Length, candidates)];
                using var image = new Vp8LEntropyEncoder(work.Span, width, table, candidates, memory, tokens);
                using var predictorImage = modes is null ? null : new Vp8LEntropyEncoder(modes.Span, modeWidth, table, candidates, memory);
                using var counter = new Vp8LBitWriter(Stream.Null, memory);
                WriteHeader(counter, subtractGreen, modes, predictorImage, image);
                var payloadSize = checked(5 + (int)((counter.TotalBits + image.DataBits + 7) / 8));
                var paddedSize = (long)payloadSize + (payloadSize & 1);
                var changed = image.UsePlaneCodes();
                changed |= predictorImage?.UsePlaneCodes() ?? false;
                if (changed) {
                    counter.Reset();
                    WriteHeader(counter, subtractGreen, modes, predictorImage, image);
                    var planePayloadSize = checked(5 + (int)((counter.TotalBits + image.DataBits + 7) / 8));
                    var planePaddedSize = (long)planePayloadSize + (planePayloadSize & 1);
                    if (planePaddedSize < paddedSize) {
                        payloadSize = planePayloadSize;
                        paddedSize = planePaddedSize;
                    }
                    else {
                        image.UseLegacyCodes();
                        predictorImage?.UseLegacyCodes();
                    }
                }
                if (paddedSize >= bestPaddedSize) {
                    continue;
                }
                bestPaddedSize = paddedSize;
                bestPayloadSize = payloadSize;
                bestBits ??= new Vp8LBitWriter(null, memory, checked(payloadSize - 5 + 3));
                bestBits.Reset();
                WriteHeader(bestBits, subtractGreen, modes, predictorImage, image);
                image.WritePixels(bestBits, work.Span);
                bestBits.Finish();
            }
            WebpContainer.WriteHeader(stream, bestPayloadSize, width, height, alpha, metadata, true);
            Span<byte> header = stackalloc byte[5];
            header[0] = 0x2f;
            BinaryPrimitives.WriteUInt32LittleEndian(header[1..], (uint)(width - 1) | ((uint)(height - 1) << 14) | (alpha ? 1u << 28 : 0));
            stream.Write(header);
            stream.Write(bestBits!.WrittenSpan);
            if ((bestPayloadSize & 1) != 0) {
                stream.WriteByte(0);
            }
            WebpContainer.WriteTrailingMetadata(stream, metadata);
        }
        finally {
            bestBits?.Dispose();
            previousModes?.Dispose();
            memory.Release(128 * 1024);
        }
    }

    private static (int First, int Second, bool LimitSearch) SelectPredictors(ReadOnlySpan<uint> pixels, int width)
    {
        ReadOnlySpan<int> choices = [-1, 1, 2, 7, 12];
        Span<double> costs = stackalloc double[5];
        Span<int> counts = stackalloc int[1024];
        var samples = Math.Min(512, pixels.Length);
        for (var choice = 0; choice < choices.Length; choice++) {
            counts.Clear();
            for (var i = 0; i < samples; i++) {
                var index = (int)((long)i * pixels.Length / samples + (long)(i * 127 % 251) * (pixels.Length / samples) / 251);
                var value = pixels[index];
                if (choices[choice] >= 0) {
                    value = Vp8LTransforms.Subtract(value, Prediction(pixels, index, index % width, index / width, width, choices[choice]));
                }
                counts[(byte)value]++;
                counts[256 + (byte)(value >> 8)]++;
                counts[512 + (byte)(value >> 16)]++;
                counts[768 + (byte)(value >> 24)]++;
            }
            double bits = 0;
            foreach (var count in counts) {
                if (count > 0) bits += count * Math.Log2((double)samples / count);
            }
            costs[choice] = bits / samples;
        }
        var first = 0;
        for (var i = 1; i < choices.Length; i++) if (costs[i] < costs[first]) first = i;
        if (costs[0] > 16 && costs[0] - costs[first] < 0.25) return (-1, -1, true);
        var second = first == 0 ? 1 : 0;
        for (var i = 0; i < choices.Length; i++) if (i != first && costs[i] < costs[second]) second = i;
        return (choices[first], choices[second], costs[first] > 14);
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

    private static WebpBuffer<uint>? ChoosePredictors(ReadOnlySpan<uint> pixels, int width, int height, WebpMemory memory, int tier)
    {
        var modeWidth = Vp8LTransforms.Subsample(width, k_PredictorBits);
        var modeHeight = Vp8LTransforms.Subsample(height, k_PredictorBits);
        var modes = memory.Rent<uint>(modeWidth * modeHeight);
        long originalScore = 0;
        long predictedScore = 0;
        ReadOnlySpan<int> choices = [1, 2, 7, 12, 0, 3, 4, 5, 6, 8, 9, 10, 11, 13];
        var choiceCount = tier switch { 0 => 1, 1 => 2, 2 => 4, 3 => 8, _ => 14 };
        choices = choices[..choiceCount];
        var sampleStep = tier switch { 0 => 8, <= 2 => 4, 3 => 2, _ => 1 };
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
                        if (x == 0 || y == 0) {
                            var score = Score(Vp8LTransforms.Subtract(pixels[index], Prediction(pixels, index, x, y, width, 0)));
                            for (var c = 0; c < scores.Length; c++) {
                                scores[c] += score;
                            }
                        }
                        else {
                            ScorePredictions(scores, pixels[index], pixels[index - 1], pixels[index - width], pixels[index - width - 1], pixels[index - width + 1]);
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

    private static void ScorePredictions(Span<long> scores, uint pixel, uint left, uint top, uint topLeft, uint topRight)
    {
        scores[0] += Score(Vp8LTransforms.Subtract(pixel, left));
        if (scores.Length == 1) return;
        scores[1] += Score(Vp8LTransforms.Subtract(pixel, top));
        if (scores.Length == 2) return;
        scores[2] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(7, left, top, topLeft, topRight)));
        scores[3] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(12, left, top, topLeft, topRight)));
        if (scores.Length == 4) return;
        scores[4] += Score(Vp8LTransforms.Subtract(pixel, 0xff000000));
        scores[5] += Score(Vp8LTransforms.Subtract(pixel, topRight));
        scores[6] += Score(Vp8LTransforms.Subtract(pixel, topLeft));
        scores[7] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(5, left, top, topLeft, topRight)));
        if (scores.Length == 8) return;
        scores[8] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(6, left, top, topLeft, topRight)));
        scores[9] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(8, left, top, topLeft, topRight)));
        scores[10] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(9, left, top, topLeft, topRight)));
        scores[11] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(10, left, top, topLeft, topRight)));
        scores[12] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(11, left, top, topLeft, topRight)));
        scores[13] += Score(Vp8LTransforms.Subtract(pixel, Vp8LTransforms.Predict(13, left, top, topLeft, topRight)));
    }

    private static Vp8LBitWriter? CreatePaletteCandidate(ReadOnlySpan<uint> pixels, int width, int height,
        bool subtractGreen, WebpMemory memory, Span<uint> work, Span<int> positions, WebpBuffer<Vp8LToken> tokens, WebpEffortSettings settings,
        out bool compactAtFastEffort)
    {
        compactAtFastEffort = false;
        var indices = new Dictionary<uint, int>(16);
        Span<uint> colors = stackalloc uint[256];
        foreach (var pixel in pixels) {
            if (!indices.ContainsKey(pixel)) {
                if (indices.Count == colors.Length) return null;
                colors[indices.Count] = pixel;
                indices.Add(pixel, indices.Count);
            }
        }
        var count = indices.Count;
        var packing = count <= 2 ? 3 : count <= 4 ? 2 : count <= 16 ? 1 : 0;
        var bits = 8 >> packing;
        var codedWidth = Vp8LTransforms.Subsample(width, packing);
        var packed = work[..checked(codedWidth * height)];
        packed.Fill(0xff000000);
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                packed[y * codedWidth + (x >> packing)] |=
                    (uint)indices[pixels[y * width + x]] << (8 + (x & ((1 << packing) - 1)) * bits);
            }
        }
        for (var i = count - 1; i > 0; i--) colors[i] = Vp8LTransforms.Subtract(colors[i], colors[i - 1]);
        var tableSize = Vp8LMatchFinder.TableSize(packed.Length, settings.MatchCandidates);
        using var extraPositions = positions.Length < tableSize ? memory.Rent<int>(tableSize) : null;
        var table = extraPositions is null ? positions[..tableSize] : extraPositions.Span;
        using var residual = memory.Rent<uint>(packed.Length);
        Vp8LBitWriter? writer = null;
        try {
            var passes = settings.MatchCandidates == 1 ? 1 : 2;
            for (var pass = 0; pass < passes; pass++) {
                var candidates = pass == 0 ? 1 : settings.MatchCandidates;
                var maxTier = pass == 0 ? 0 : settings.PaletteTier;
                var passTable = table[..Vp8LMatchFinder.TableSize(packed.Length, candidates)];
                using var palette = new Vp8LEntropyEncoder(colors[..count], count, passTable, candidates, memory);
                for (var tier = -1; tier <= maxTier; tier++) {
                    using var modes = tier < 0 ? null : ChoosePredictors(packed, codedWidth, height, memory, tier);
                    if (tier >= 0 && modes is null) continue;
                    packed.CopyTo(residual.Span);
                    if (modes is not null) ApplyPredictors(residual.Span, codedWidth, height, modes.Span);
                    using var image = new Vp8LEntropyEncoder(residual.Span, codedWidth, passTable, candidates, memory, tokens);
                    using var predictor = modes is null ? null : new Vp8LEntropyEncoder(modes.Span,
                        Vp8LTransforms.Subsample(codedWidth, k_PredictorBits), passTable, candidates, memory);
                    image.UsePlaneCodes();
                    predictor?.UsePlaneCodes();
                    using var counter = new Vp8LBitWriter(Stream.Null, memory);
                    WritePaletteHeader(counter, subtractGreen, colors[..count], palette, modes, predictor, image);
                    var size = checked((int)((counter.TotalBits + image.DataBits + 7) / 8));
                    if (writer is not null && size >= writer.WrittenSpan.Length) continue;
                    writer?.Dispose();
                    writer = new Vp8LBitWriter(null, memory, size + 3);
                    WritePaletteHeader(writer, subtractGreen, colors[..count], palette, modes, predictor, image);
                    image.WritePixels(writer, residual.Span);
                    writer.Finish();
                }
                if (pass == 0) {
                    compactAtFastEffort = (writer!.WrittenSpan.Length + 5L) * 8 < pixels.Length;
                    if (compactAtFastEffort && packing > 0 && settings.PredictorTier < 4) break;
                }
            }
            return writer;
        }
        catch {
            writer?.Dispose();
            throw;
        }
    }

    private static void WritePaletteHeader(Vp8LBitWriter writer, bool subtractGreen, ReadOnlySpan<uint> colors,
        Vp8LEntropyEncoder palette, WebpBuffer<uint>? modes, Vp8LEntropyEncoder? predictor, Vp8LEntropyEncoder image)
    {
        if (subtractGreen) {
            writer.Write(1, 1);
            writer.Write(2, 2);
        }
        writer.Write(1, 1);
        writer.Write(3, 2);
        writer.Write((uint)(colors.Length - 1), 8);
        palette.WriteHeader(writer, false);
        palette.WritePixels(writer, colors);
        WriteHeader(writer, false, modes, predictor, image);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Score(uint pixel)
    {
        var signs = (pixel & 0x80808080u) >> 7;
        var magnitudes = (pixel ^ (signs * 255)) + signs;
        var pairs = (magnitudes & 0x00ff00ffu) + ((magnitudes >> 8) & 0x00ff00ffu);
        return (int)((pairs & 0xffff) + (pairs >> 16));
    }
}
