using System.Numerics;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegCoefficientEncoder
{
    public static void Compute(byte[] pixels, JpegEncodingFrame frame)
    {
        if (frame.ComponentCount == 3) {
            ComputeColorCoefficients(pixels, frame);
        }
        else {
            ComputeGrayscaleCoefficients(pixels, frame);
        }
    }

    private static void ComputeGrayscaleCoefficients(byte[] pixels, JpegEncodingFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var quantTable = frame.QuantizationTables[0];
        var coefficients = frame.Coefficients[0];
        var blockColumns = frame.PaddedBlockColumns[0];
        var inverseQuantization = new float[64];
        for (var i = 0; i < 64; i++) {
            inverseQuantization[i] = 1f / quantTable[i];
        }

        Parallel.For(0, frame.PaddedBlockRows[0], blockRow => {
            Span<float> samples = stackalloc float[64];
            Span<float> dctCoefficients = stackalloc float[64];

            for (var blockCol = 0; blockCol < blockColumns; blockCol++) {
                ExtractGrayscaleBlock(pixels, width, height, blockRow, blockCol, samples);
                DctTransform.Forward(samples, dctCoefficients);
                var blockOffset = frame.BlockOffset(0, blockRow, blockCol);
                Quantize(dctCoefficients, inverseQuantization, coefficients.AsSpan(blockOffset, 64));
            }
        });
    }

    private static void ComputeColorCoefficients(byte[] pixels, JpegEncodingFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var hMax = frame.MaxHorizontalSampling;
        var vMax = frame.MaxVerticalSampling;
        var blocksPerLine = frame.PaddedBlockColumns;
        var blocksPerColumn = frame.PaddedBlockRows;
        var quantTables = frame.QuantizationTables;
        var coefficients = frame.Coefficients;
        var inverseLuma = quantTables[0].Select(v => 1f / v).ToArray();
        var inverseChroma = quantTables[1].Select(v => 1f / v).ToArray();
        var mcusPerLine = blocksPerLine[1];
        var scale = 1f / (hMax * vMax);
        Parallel.For(0, blocksPerColumn[1], row => {
            Span<float> luma = stackalloc float[64];
            Span<float> cb = stackalloc float[64];
            Span<float> cr = stackalloc float[64];
            Span<float> transformed = stackalloc float[64];
            for (var col = 0; col < mcusPerLine; col++) {
                cb.Clear();
                cr.Clear();
                for (var v = 0; v < vMax; v++) {
                    for (var h = 0; h < hMax; h++) {
                        for (var y = 0; y < 8; y++) {
                            var sourceY = Math.Min((row * vMax + v) * 8 + y, height - 1);
                            var chromaRow = ((v * 8 + y) >> (vMax - 1)) * 8;
                            for (var x = 0; x < 8; x++) {
                                var sourceX = Math.Min((col * hMax + h) * 8 + x, width - 1);
                                var offset = (sourceY * width + sourceX) * 3;
                                var r = pixels[offset];
                                var g = pixels[offset + 1];
                                var b = pixels[offset + 2];
                                luma[y * 8 + x] = r * 0.299f + g * 0.587f + b * 0.114f - 128f;
                                var chromaIndex = chromaRow + ((h * 8 + x) >> (hMax - 1));
                                cb[chromaIndex] += -r * 0.168736f - g * 0.331264f + b * 0.5f;
                                cr[chromaIndex] += r * 0.5f - g * 0.418688f - b * 0.081312f;
                            }
                        }
                        DctTransform.Forward(luma, transformed);
                        var block = ((row * vMax + v) * blocksPerLine[0] + col * hMax + h) * 64;
                        Quantize(transformed, inverseLuma, coefficients[0].AsSpan(block, 64));
                    }
                }
                for (var i = 0; i < 64; i++) {
                    cb[i] *= scale;
                    cr[i] *= scale;
                }
                var chromaBlock = (row * mcusPerLine + col) * 64;
                DctTransform.Forward(cb, transformed);
                Quantize(transformed, inverseChroma, coefficients[1].AsSpan(chromaBlock, 64));
                DctTransform.Forward(cr, transformed);
                Quantize(transformed, inverseChroma, coefficients[2].AsSpan(chromaBlock, 64));
            }
        });
    }

    private static void ExtractGrayscaleBlock(byte[] pixels, int width, int height, int blockRow, int blockCol, Span<float> samples)
    {
        for (var y = 0; y < 8; y++) {
            var sourceY = Math.Min(blockRow * 8 + y, height - 1);
            for (var x = 0; x < 8; x++) {
                var sourceX = Math.Min(blockCol * 8 + x, width - 1);
                samples[y * 8 + x] = pixels[sourceY * width + sourceX] - 128f;
            }
        }
    }

    private static void Quantize(ReadOnlySpan<float> dctCoefficients, float[] inverseQuantization, Span<short> quantized)
    {
        var i = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            var min = new Vector<float>(short.MinValue);
            var max = new Vector<float>(short.MaxValue);

            for (; i + (2 * lanes) <= 64; i += 2 * lanes) {
                var a = QuantizeLane(dctCoefficients.Slice(i, lanes), inverseQuantization.AsSpan(i, lanes), min, max);
                var b = QuantizeLane(dctCoefficients.Slice(i + lanes, lanes), inverseQuantization.AsSpan(i + lanes, lanes), min, max);
                Vector.Narrow(a, b).CopyTo(quantized.Slice(i, 2 * lanes));
            }
        }

        for (; i < 64; i++) {
            quantized[i] = (short)Math.Clamp(MathF.Round(dctCoefficients[i] * inverseQuantization[i]), short.MinValue, short.MaxValue);
        }
    }

    private static Vector<int> QuantizeLane(ReadOnlySpan<float> dctCoefficients, ReadOnlySpan<float> inverseQuantization, Vector<float> min, Vector<float> max)
    {
        var rounded = Vector.Round(new Vector<float>(dctCoefficients) * new Vector<float>(inverseQuantization));
        return Vector.ConvertToInt32(Vector.Min(Vector.Max(rounded, min), max));
    }
}
