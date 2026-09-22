using System.Numerics;

namespace Lucitex.Jpeg.Decoding;

internal static class JpegPixelAssembler
{
    private const int k_ScaleBits = 16;
    private const int k_Half = 1 << (k_ScaleBits - 1);

    private static readonly int[] s_CrToR = BuildDirectTable(1.40200);
    private static readonly int[] s_CbToB = BuildDirectTable(1.77200);
    private static readonly int[] s_CrToG = BuildScaledTable(-0.71414);
    private static readonly int[] s_CbToG = BuildScaledTable(-0.34414);

    public static byte[] Reconstruct(JpegDecoder decoder)
    {
        var frame = decoder.Frame;
        var componentCount = frame.Components.Count;
        var planes = new byte[componentCount][];
        for (var c = 0; c < componentCount; c++) {
            var component = decoder.Components[c];
            var quantTable = decoder.GetQuantTable(component.Component.QuantTableId);
            planes[c] = ReconstructComponentPlane(component, quantTable);
        }

        var width = frame.Width;
        var height = frame.Height;
        var output = new byte[width * height * componentCount];
        var hMax = frame.HMax;
        var vMax = frame.VMax;
        var useColorTransform = componentCount == 3 && !decoder.AdobeTransformIsRaw;

        var xMaps = new int[componentCount][];
        for (var c = 0; c < componentCount; c++) {
            xMaps[c] = BuildAxisMap(width, decoder.Components[c].Component.HSampling, hMax, decoder.Components[c].SamplesPerLine);
        }

        if (componentCount == 1) {
            var plane = planes[0];
            var component = decoder.Components[0];
            var xMap = xMaps[0];
            Parallel.For(0, height, y => {
                var sampleY = Math.Min((y * component.Component.VSampling) / vMax, component.SamplesPerColumn - 1);
                var rowBase = sampleY * component.SamplesPerLine;
                var outRowBase = y * width;
                for (var x = 0; x < width; x++) {
                    output[outRowBase + x] = plane[rowBase + xMap[x]];
                }
            });

            return output;
        }

        var plane0 = planes[0];
        var plane1 = planes[1];
        var plane2 = planes[2];
        var component0 = decoder.Components[0];
        var component1 = decoder.Components[1];
        var component2 = decoder.Components[2];
        var xMap0 = xMaps[0];
        var xMap1 = xMaps[1];
        var xMap2 = xMaps[2];

        Parallel.For(0, height, y => {
            var rowBase0 = Math.Min((y * component0.Component.VSampling) / vMax, component0.SamplesPerColumn - 1) * component0.SamplesPerLine;
            var rowBase1 = Math.Min((y * component1.Component.VSampling) / vMax, component1.SamplesPerColumn - 1) * component1.SamplesPerLine;
            var rowBase2 = Math.Min((y * component2.Component.VSampling) / vMax, component2.SamplesPerColumn - 1) * component2.SamplesPerLine;
            var outRowBase = y * width * 3;

            for (var x = 0; x < width; x++) {
                var sample0 = plane0[rowBase0 + xMap0[x]];
                var sample1 = plane1[rowBase1 + xMap1[x]];
                var sample2 = plane2[rowBase2 + xMap2[x]];
                var outIndex = outRowBase + (x * 3);

                if (useColorTransform) {
                    output[outIndex] = ClampByte(sample0 + s_CrToR[sample2]);
                    output[outIndex + 1] = ClampByte(sample0 + ((s_CrToG[sample2] + s_CbToG[sample1] + k_Half) >> k_ScaleBits));
                    output[outIndex + 2] = ClampByte(sample0 + s_CbToB[sample1]);
                }
                else {
                    output[outIndex] = sample0;
                    output[outIndex + 1] = sample1;
                    output[outIndex + 2] = sample2;
                }
            }
        });

        return output;
    }

    private static int[] BuildAxisMap(int outputExtent, int componentSampling, int maxSampling, int samplesPerAxis)
    {
        var map = new int[outputExtent];
        for (var i = 0; i < outputExtent; i++) {
            map[i] = Math.Min((i * componentSampling) / maxSampling, samplesPerAxis - 1);
        }

        return map;
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static int[] BuildDirectTable(double scale)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++) {
            table[i] = (int)Math.Round(scale * (i - 128));
        }

        return table;
    }

    private static int[] BuildScaledTable(double scale)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++) {
            table[i] = (int)Math.Round(scale * (i - 128) * (1 << k_ScaleBits));
        }

        return table;
    }

    private static byte[] ReconstructComponentPlane(JpegComponentState component, Format.JpegQuantizationTable quantTable)
    {
        var plane = new byte[component.SamplesPerLine * component.SamplesPerColumn];
        var coefficients = component.Coefficients;
        var quantValues = quantTable.Values;
        var blocksPerLine = component.BlocksPerLine;
        var samplesPerLine = component.SamplesPerLine;
        var samplesPerColumn = component.SamplesPerColumn;

        Parallel.For(0, component.BlocksPerColumn, blockRow => {
            Span<int> dequantized = stackalloc int[64];
            Span<float> spatial = stackalloc float[64];

            for (var blockCol = 0; blockCol < blocksPerLine; blockCol++) {
                var blockOffset = component.BlockOffset(blockRow, blockCol);
                Dequantize(coefficients.AsSpan(blockOffset, 64), quantValues, dequantized);

                InverseDct.Transform(dequantized, spatial);

                var originY = blockRow * 8;
                var originX = blockCol * 8;
                for (var y = 0; y < 8; y++) {
                    var sampleY = originY + y;
                    if (sampleY >= samplesPerColumn) {
                        continue;
                    }

                    for (var x = 0; x < 8; x++) {
                        var sampleX = originX + x;
                        if (sampleX >= samplesPerLine) {
                            continue;
                        }

                        var value = spatial[(y * 8) + x] + 128.5f;
                        plane[(sampleY * samplesPerLine) + sampleX] = (byte)Math.Clamp((int)value, 0, 255);
                    }
                }
            }
        });

        return plane;
    }

    private static void Dequantize(ReadOnlySpan<short> coefficients, ushort[] quantValues, Span<int> dequantized)
    {
        var i = 0;
        var lanes = Vector<short>.Count;

        if (Vector.IsHardwareAccelerated) {
            for (; i + lanes <= 64; i += lanes) {
                var c = new Vector<short>(coefficients.Slice(i, lanes));
                var q = new Vector<ushort>(quantValues.AsSpan(i, lanes));

                Vector.Widen(c, out var cLo, out var cHi);
                Vector.Widen(q, out var qLo, out var qHi);

                (cLo * Vector.AsVectorInt32(qLo)).CopyTo(dequantized.Slice(i, lanes / 2));
                (cHi * Vector.AsVectorInt32(qHi)).CopyTo(dequantized.Slice(i + (lanes / 2), lanes / 2));
            }
        }

        for (; i < 64; i++) {
            dequantized[i] = coefficients[i] * quantValues[i];
        }
    }
}
