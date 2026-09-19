using System.Numerics;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegEncoder
{
    public static void Encode(Stream stream, byte[] pixels, int width, int height, int componentCount, int quality, bool progressive)
    {
        var lumaQuant = JpegStandardTables.ScaleQuantizationTable(JpegStandardTables.LuminanceQuantization, quality);
        var chromaQuant = JpegStandardTables.ScaleQuantizationTable(JpegStandardTables.ChrominanceQuantization, quality);

        var dcLumaSpec = new JpegHuffmanSpec { Id = 0, IsAc = false, Bits = JpegStandardTables.DcLuminanceBits, Values = JpegStandardTables.DcLuminanceValues };
        var acLumaSpec = new JpegHuffmanSpec { Id = 0, IsAc = true, Bits = JpegStandardTables.AcLuminanceBits, Values = JpegStandardTables.AcLuminanceValues };
        var dcChromaSpec = new JpegHuffmanSpec { Id = 1, IsAc = false, Bits = JpegStandardTables.DcChrominanceBits, Values = JpegStandardTables.DcChrominanceValues };
        var acChromaSpec = new JpegHuffmanSpec { Id = 1, IsAc = true, Bits = JpegStandardTables.AcChrominanceBits, Values = JpegStandardTables.AcChrominanceValues };

        var dcLumaTable = new JpegHuffmanEncodeTable(dcLumaSpec);
        var acLumaTable = new JpegHuffmanEncodeTable(acLumaSpec);
        var dcChromaTable = componentCount == 3 ? new JpegHuffmanEncodeTable(dcChromaSpec) : null;
        var acChromaTable = componentCount == 3 ? new JpegHuffmanEncodeTable(acChromaSpec) : null;

        byte[] hSampling = componentCount == 3 ? [2, 1, 1] : [1];
        byte[] vSampling = componentCount == 3 ? [2, 1, 1] : [1];
        byte[] quantTableIds = componentCount == 3 ? [0, 1, 1] : [0];
        byte[] componentIds = componentCount == 3 ? [1, 2, 3] : [1];

        var hMax = hSampling.Max();
        var vMax = vSampling.Max();
        var mcusPerLine = CeilDiv(width, 8 * hMax);
        var mcusPerColumn = CeilDiv(height, 8 * vMax);

        var componentPlanes = BuildComponentPlanes(pixels, width, height, componentCount, hMax, vMax, hSampling, vSampling, mcusPerLine, mcusPerColumn);

        JpegDocumentWriter.WriteSoi(stream);
        JpegDocumentWriter.WriteJfifHeader(stream);
        JpegDocumentWriter.WriteQuantizationTable(stream, 0, lumaQuant);
        if (componentCount == 3) {
            JpegDocumentWriter.WriteQuantizationTable(stream, 1, chromaQuant);
        }

        JpegDocumentWriter.WriteFrameHeader(stream, progressive ? JpegMarkers.Sof2 : JpegMarkers.Sof0, width, height, componentIds, hSampling, vSampling, quantTableIds);

        JpegDocumentWriter.WriteHuffmanTable(stream, dcLumaSpec);
        JpegDocumentWriter.WriteHuffmanTable(stream, acLumaSpec);
        if (componentCount == 3) {
            JpegDocumentWriter.WriteHuffmanTable(stream, dcChromaSpec);
            JpegDocumentWriter.WriteHuffmanTable(stream, acChromaSpec);
        }

        var quantTables = componentCount == 3 ? new[] { lumaQuant, chromaQuant, chromaQuant } : [lumaQuant];
        var dcTables = componentCount == 3 ? new[] { dcLumaTable, dcChromaTable!, dcChromaTable! } : [dcLumaTable];
        var acTables = componentCount == 3 ? new[] { acLumaTable, acChromaTable!, acChromaTable! } : [acLumaTable];

        if (progressive) {
            EncodeProgressive(stream, componentPlanes, componentCount, componentIds, hSampling, vSampling, hMax, vMax, mcusPerLine, mcusPerColumn, width, height, quantTables, dcTables, acTables);
        }
        else {
            EncodeBaseline(stream, componentPlanes, componentCount, componentIds, hSampling, vSampling, mcusPerLine, mcusPerColumn, quantTables, dcTables, acTables);
        }

        JpegDocumentWriter.WriteEoi(stream);
    }

    private static void EncodeBaseline(
        Stream stream,
        float[][] componentPlanes,
        int componentCount,
        byte[] componentIds,
        byte[] hSampling,
        byte[] vSampling,
        int mcusPerLine,
        int mcusPerColumn,
        ushort[][] quantTables,
        JpegHuffmanEncodeTable[] dcTables,
        JpegHuffmanEncodeTable[] acTables)
    {
        byte[] dcTableIds = componentCount == 3 ? [0, 1, 1] : [0];
        byte[] acTableIds = componentCount == 3 ? [0, 1, 1] : [0];
        JpegDocumentWriter.WriteScanHeader(stream, componentIds, dcTableIds, acTableIds, 0, 63, 0);

        var writer = new JpegBitWriter();
        var dcPredictors = new int[componentCount];
        Span<float> samples = stackalloc float[64];
        Span<float> dctCoefficients = stackalloc float[64];
        Span<int> quantized = stackalloc int[64];

        for (var mcuRow = 0; mcuRow < mcusPerColumn; mcuRow++) {
            for (var mcuCol = 0; mcuCol < mcusPerLine; mcuCol++) {
                for (var c = 0; c < componentCount; c++) {
                    var planeWidth = mcusPerLine * hSampling[c] * 8;

                    for (var v = 0; v < vSampling[c]; v++) {
                        for (var h = 0; h < hSampling[c]; h++) {
                            var blockRow = (mcuRow * vSampling[c]) + v;
                            var blockCol = (mcuCol * hSampling[c]) + h;
                            ExtractBlock(componentPlanes[c], planeWidth, blockRow, blockCol, samples);
                            ForwardDct.Transform(samples, dctCoefficients);
                            Quantize(dctCoefficients, quantTables[c], quantized);
                            JpegBlockEncoder.EncodeBlock(writer, quantized, ref dcPredictors[c], dcTables[c], acTables[c]);
                        }
                    }
                }
            }
        }

        writer.PadAndFlush();
        writer.CopyTo(stream);
    }

    private static void EncodeProgressive(
        Stream stream,
        float[][] componentPlanes,
        int componentCount,
        byte[] componentIds,
        byte[] hSampling,
        byte[] vSampling,
        int hMax,
        int vMax,
        int mcusPerLine,
        int mcusPerColumn,
        int width,
        int height,
        ushort[][] quantTables,
        JpegHuffmanEncodeTable[] dcTables,
        JpegHuffmanEncodeTable[] acTables)
    {
        var blocksPerLineForMcu = new int[componentCount];
        var blocksPerColumnForMcu = new int[componentCount];
        var blocksPerLine = new int[componentCount];
        var blocksPerColumn = new int[componentCount];
        var coefficients = new int[componentCount][];

        for (var c = 0; c < componentCount; c++) {
            blocksPerLineForMcu[c] = mcusPerLine * hSampling[c];
            blocksPerColumnForMcu[c] = mcusPerColumn * vSampling[c];
            blocksPerLine[c] = CeilDiv(CeilDiv(width, 8) * hSampling[c], hMax);
            blocksPerColumn[c] = CeilDiv(CeilDiv(height, 8) * vSampling[c], vMax);
            coefficients[c] = new int[blocksPerLineForMcu[c] * blocksPerColumnForMcu[c] * 64];
        }

        Span<float> samples = stackalloc float[64];
        Span<float> dctCoefficients = stackalloc float[64];

        for (var c = 0; c < componentCount; c++) {
            var planeWidth = blocksPerLineForMcu[c] * 8;
            for (var blockRow = 0; blockRow < blocksPerColumnForMcu[c]; blockRow++) {
                for (var blockCol = 0; blockCol < blocksPerLineForMcu[c]; blockCol++) {
                    ExtractBlock(componentPlanes[c], planeWidth, blockRow, blockCol, samples);
                    ForwardDct.Transform(samples, dctCoefficients);
                    var blockOffset = ((blockRow * blocksPerLineForMcu[c]) + blockCol) * 64;
                    Quantize(dctCoefficients, quantTables[c], coefficients[c].AsSpan(blockOffset, 64));
                }
            }
        }

        byte[] dcTableIds = componentCount == 3 ? [0, 1, 1] : [0];
        JpegDocumentWriter.WriteScanHeader(stream, componentIds, dcTableIds, dcTableIds, 0, 0, 0);

        var dcWriter = new JpegBitWriter();
        var dcPredictors = new int[componentCount];

        for (var mcuRow = 0; mcuRow < mcusPerColumn; mcuRow++) {
            for (var mcuCol = 0; mcuCol < mcusPerLine; mcuCol++) {
                for (var c = 0; c < componentCount; c++) {
                    for (var v = 0; v < vSampling[c]; v++) {
                        for (var h = 0; h < hSampling[c]; h++) {
                            var blockRow = (mcuRow * vSampling[c]) + v;
                            var blockCol = (mcuCol * hSampling[c]) + h;
                            var blockOffset = ((blockRow * blocksPerLineForMcu[c]) + blockCol) * 64;
                            JpegBlockEncoder.EncodeDc(dcWriter, coefficients[c][blockOffset], ref dcPredictors[c], dcTables[c]);
                        }
                    }
                }
            }
        }

        dcWriter.PadAndFlush();
        dcWriter.CopyTo(stream);

        for (var c = 0; c < componentCount; c++) {
            byte[] scanComponentId = [componentIds[c]];
            byte[] acTableId = [(byte)(componentCount == 3 && c > 0 ? 1 : 0)];
            JpegDocumentWriter.WriteScanHeader(stream, scanComponentId, acTableId, acTableId, 1, 63, 0);

            var acWriter = new JpegBitWriter();
            for (var blockRow = 0; blockRow < blocksPerColumn[c]; blockRow++) {
                for (var blockCol = 0; blockCol < blocksPerLine[c]; blockCol++) {
                    var blockOffset = ((blockRow * blocksPerLineForMcu[c]) + blockCol) * 64;
                    JpegBlockEncoder.EncodeAc(acWriter, coefficients[c].AsSpan(blockOffset, 64), acTables[c]);
                }
            }

            acWriter.PadAndFlush();
            acWriter.CopyTo(stream);
        }
    }

    private static float[][] BuildComponentPlanes(
        byte[] pixels,
        int width,
        int height,
        int componentCount,
        int hMax,
        int vMax,
        byte[] hSampling,
        byte[] vSampling,
        int mcusPerLine,
        int mcusPerColumn)
    {
        var planes = new float[componentCount][];

        if (componentCount == 1) {
            planes[0] = BuildPaddedPlane(pixels, width, height, mcusPerLine * 8, mcusPerColumn * 8);
            return planes;
        }

        var pixelCount = width * height;
        var yFull = new float[pixelCount];
        var cbFull = new float[pixelCount];
        var crFull = new float[pixelCount];

        Parallel.For(0, height, y => RgbRowToYCbCr(pixels, y * width, width, yFull, cbFull, crFull));

        planes[0] = BuildFullPlane(yFull, width, height, mcusPerLine * hSampling[0] * 8, mcusPerColumn * vSampling[0] * 8, hMax / hSampling[0], vMax / vSampling[0]);
        planes[1] = BuildFullPlane(cbFull, width, height, mcusPerLine * hSampling[1] * 8, mcusPerColumn * vSampling[1] * 8, hMax / hSampling[1], vMax / vSampling[1]);
        planes[2] = BuildFullPlane(crFull, width, height, mcusPerLine * hSampling[2] * 8, mcusPerColumn * vSampling[2] * 8, hMax / hSampling[2], vMax / vSampling[2]);
        return planes;
    }

    private static void RgbRowToYCbCr(byte[] pixels, int rowBase, int width, float[] yFull, float[] cbFull, float[] crFull)
    {
        var pixelBase = rowBase * 3;
        var x = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            Span<float> r = stackalloc float[lanes];
            Span<float> g = stackalloc float[lanes];
            Span<float> b = stackalloc float[lanes];

            for (; x + lanes <= width; x += lanes) {
                for (var lane = 0; lane < lanes; lane++) {
                    var offset = pixelBase + ((x + lane) * 3);
                    r[lane] = pixels[offset];
                    g[lane] = pixels[offset + 1];
                    b[lane] = pixels[offset + 2];
                }

                var rv = new Vector<float>(r);
                var gv = new Vector<float>(g);
                var bv = new Vector<float>(b);

                var yv = (rv * 0.299f) + (gv * 0.587f) + (bv * 0.114f);
                var cbv = new Vector<float>(128f) - (rv * 0.168736f) - (gv * 0.331264f) + (bv * 0.5f);
                var crv = new Vector<float>(128f) + (rv * 0.5f) - (gv * 0.418688f) - (bv * 0.081312f);

                yv.CopyTo(yFull.AsSpan(rowBase + x, lanes));
                cbv.CopyTo(cbFull.AsSpan(rowBase + x, lanes));
                crv.CopyTo(crFull.AsSpan(rowBase + x, lanes));
            }
        }

        for (; x < width; x++) {
            var offset = pixelBase + (x * 3);
            var r = pixels[offset];
            var g = pixels[offset + 1];
            var b = pixels[offset + 2];
            yFull[rowBase + x] = (0.299f * r) + (0.587f * g) + (0.114f * b);
            cbFull[rowBase + x] = 128f - (0.168736f * r) - (0.331264f * g) + (0.5f * b);
            crFull[rowBase + x] = 128f + (0.5f * r) - (0.418688f * g) - (0.081312f * b);
        }
    }

    private static float[] BuildPaddedPlane(byte[] pixels, int width, int height, int paddedWidth, int paddedHeight)
    {
        var plane = new float[paddedWidth * paddedHeight];
        for (var y = 0; y < paddedHeight; y++) {
            var srcY = Math.Min(y, height - 1);
            for (var x = 0; x < paddedWidth; x++) {
                var srcX = Math.Min(x, width - 1);
                plane[(y * paddedWidth) + x] = pixels[(srcY * width) + srcX] - 128f;
            }
        }

        return plane;
    }

    private static float[] BuildFullPlane(float[] full, int width, int height, int paddedWidth, int paddedHeight, int hRatio, int vRatio)
    {
        var componentWidth = CeilDiv(width, hRatio);
        var componentHeight = CeilDiv(height, vRatio);
        var plane = new float[paddedWidth * paddedHeight];

        for (var y = 0; y < paddedHeight; y++) {
            var componentY = Math.Min(y, componentHeight - 1);
            for (var x = 0; x < paddedWidth; x++) {
                var componentX = Math.Min(x, componentWidth - 1);
                plane[(y * paddedWidth) + x] = SampleBox(full, width, height, componentX, componentY, hRatio, vRatio) - 128f;
            }
        }

        return plane;
    }

    private static float SampleBox(float[] full, int width, int height, int componentX, int componentY, int hRatio, int vRatio)
    {
        var sum = 0f;
        var count = 0;
        for (var dy = 0; dy < vRatio; dy++) {
            var srcY = Math.Min((componentY * vRatio) + dy, height - 1);
            for (var dx = 0; dx < hRatio; dx++) {
                var srcX = Math.Min((componentX * hRatio) + dx, width - 1);
                sum += full[(srcY * width) + srcX];
                count++;
            }
        }

        return sum / count;
    }

    private static void ExtractBlock(float[] plane, int planeWidth, int blockRow, int blockCol, Span<float> samples)
    {
        var originY = blockRow * 8;
        var originX = blockCol * 8;
        for (var y = 0; y < 8; y++) {
            for (var x = 0; x < 8; x++) {
                samples[(y * 8) + x] = plane[((originY + y) * planeWidth) + originX + x];
            }
        }
    }

    private static void Quantize(ReadOnlySpan<float> dctCoefficients, ushort[] quantTable, Span<int> quantized)
    {
        for (var i = 0; i < 64; i++) {
            quantized[i] = (int)MathF.Round(dctCoefficients[i] / quantTable[i]);
        }
    }

    private static int CeilDiv(int numerator, int denominator) => (numerator + denominator - 1) / denominator;
}
