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
        var blocksPerLineForMcu = new int[componentCount];
        var blocksPerColumnForMcu = new int[componentCount];
        var coefficients = new short[componentCount][];

        for (var c = 0; c < componentCount; c++) {
            blocksPerLineForMcu[c] = mcusPerLine * hSampling[c];
            blocksPerColumnForMcu[c] = mcusPerColumn * vSampling[c];
            coefficients[c] = new short[blocksPerLineForMcu[c] * blocksPerColumnForMcu[c] * 64];
        }

        ComputeBlockCoefficients(componentPlanes, componentCount, blocksPerLineForMcu, blocksPerColumnForMcu, quantTables, coefficients);

        var totalMcus = mcusPerLine * mcusPerColumn;
        var restartInterval = ChooseRestartInterval(totalMcus, coefficients);
        var segmentCount = restartInterval > 0 ? CeilDiv(totalMcus, restartInterval) : 1;

        byte[] dcTableIds = componentCount == 3 ? [0, 1, 1] : [0];
        byte[] acTableIds = componentCount == 3 ? [0, 1, 1] : [0];

        if (segmentCount > 1) {
            JpegDocumentWriter.WriteDri(stream, restartInterval);
        }

        JpegDocumentWriter.WriteScanHeader(stream, componentIds, dcTableIds, acTableIds, 0, 63, 0);

        var segmentWriters = new JpegBitWriter[segmentCount];

        void EncodeSegment(int segmentIndex)
        {
            var mcuStart = segmentIndex * restartInterval;
            var mcuEnd = segmentCount == 1 ? totalMcus : Math.Min(mcuStart + restartInterval, totalMcus);
            var writer = new JpegBitWriter();
            var dcPredictors = new int[componentCount];

            for (var mcuIndex = mcuStart; mcuIndex < mcuEnd; mcuIndex++) {
                var mcuRow = mcuIndex / mcusPerLine;
                var mcuCol = mcuIndex % mcusPerLine;

                for (var c = 0; c < componentCount; c++) {
                    var blocksPerLineC = blocksPerLineForMcu[c];

                    for (var v = 0; v < vSampling[c]; v++) {
                        for (var h = 0; h < hSampling[c]; h++) {
                            var blockRow = (mcuRow * vSampling[c]) + v;
                            var blockCol = (mcuCol * hSampling[c]) + h;
                            var blockOffset = ((blockRow * blocksPerLineC) + blockCol) * 64;
                            JpegBlockEncoder.EncodeBlock(writer, coefficients[c].AsSpan(blockOffset, 64), ref dcPredictors[c], dcTables[c], acTables[c]);
                        }
                    }
                }
            }

            writer.PadAndFlush();
            segmentWriters[segmentIndex] = writer;
        }

        if (segmentCount == 1) {
            EncodeSegment(0);
        }
        else {
            Parallel.For(0, segmentCount, EncodeSegment);
        }

        var restartMarker = JpegMarkers.Rst0;
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++) {
            if (segmentIndex > 0) {
                JpegDocumentWriter.WriteRestartMarker(stream, restartMarker);
                restartMarker = restartMarker == JpegMarkers.Rst7 ? JpegMarkers.Rst0 : (byte)(restartMarker + 1);
            }

            segmentWriters[segmentIndex].CopyTo(stream);
        }
    }

    private static int ChooseRestartInterval(int totalMcus, short[][] coefficients)
    {
        const int minInterval = 4;
        const int minNonZeroPerSegment = 50_000;

        if (totalMcus <= minInterval) {
            return 0;
        }

        var maxSegmentsByWork = Math.Max(1, CountNonZero(coefficients) / minNonZeroPerSegment);
        var targetSegments = (int)Math.Min(Math.Min(totalMcus / minInterval, Environment.ProcessorCount * 2), maxSegmentsByWork);
        if (targetSegments <= 1) {
            return 0;
        }

        return CeilDiv(totalMcus, targetSegments);
    }

    private static long CountNonZero(short[][] coefficients)
    {
        long count = 0;
        foreach (var component in coefficients) {
            for (var i = 0; i < component.Length; i++) {
                if (component[i] != 0) {
                    count++;
                }
            }
        }

        return count;
    }

    private static void ComputeBlockCoefficients(
        float[][] componentPlanes,
        int componentCount,
        int[] blocksPerLineForMcu,
        int[] blocksPerColumnForMcu,
        ushort[][] quantTables,
        short[][] coefficients)
    {
        for (var c = 0; c < componentCount; c++) {
            var planeWidth = blocksPerLineForMcu[c] * 8;
            var plane = componentPlanes[c];
            var quantTable = quantTables[c];
            var componentCoefficients = coefficients[c];
            var blocksPerLineC = blocksPerLineForMcu[c];

            var quantTableFloat = new float[64];
            for (var i = 0; i < 64; i++) {
                quantTableFloat[i] = quantTable[i];
            }

            Parallel.For(0, blocksPerColumnForMcu[c], blockRow => {
                Span<float> samples = stackalloc float[64];
                Span<float> dctCoefficients = stackalloc float[64];

                for (var blockCol = 0; blockCol < blocksPerLineC; blockCol++) {
                    ExtractBlock(plane, planeWidth, blockRow, blockCol, samples);
                    ForwardDct.Transform(samples, dctCoefficients);
                    var blockOffset = ((blockRow * blocksPerLineC) + blockCol) * 64;
                    Quantize(dctCoefficients, quantTableFloat, componentCoefficients.AsSpan(blockOffset, 64));
                }
            });

            componentPlanes[c] = null!;
        }
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
        var coefficients = new short[componentCount][];

        for (var c = 0; c < componentCount; c++) {
            blocksPerLineForMcu[c] = mcusPerLine * hSampling[c];
            blocksPerColumnForMcu[c] = mcusPerColumn * vSampling[c];
            blocksPerLine[c] = CeilDiv(CeilDiv(width, 8) * hSampling[c], hMax);
            blocksPerColumn[c] = CeilDiv(CeilDiv(height, 8) * vSampling[c], vMax);
            coefficients[c] = new short[blocksPerLineForMcu[c] * blocksPerColumnForMcu[c] * 64];
        }

        ComputeBlockCoefficients(componentPlanes, componentCount, blocksPerLineForMcu, blocksPerColumnForMcu, quantTables, coefficients);

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

        planes[0] = BuildLumaPlane(pixels, width, height, mcusPerLine * hSampling[0] * 8, mcusPerColumn * vSampling[0] * 8);
        planes[1] = BuildChromaPlane(pixels, width, height, mcusPerLine * hSampling[1] * 8, mcusPerColumn * vSampling[1] * 8, hMax / hSampling[1], vMax / vSampling[1], isCb: true);
        planes[2] = BuildChromaPlane(pixels, width, height, mcusPerLine * hSampling[2] * 8, mcusPerColumn * vSampling[2] * 8, hMax / hSampling[2], vMax / vSampling[2], isCb: false);
        return planes;
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

    private static float[] BuildLumaPlane(byte[] pixels, int width, int height, int paddedWidth, int paddedHeight)
    {
        var plane = new float[paddedWidth * paddedHeight];
        Parallel.For(0, paddedHeight, y => LumaRow(pixels, width, height, paddedWidth, y, plane));
        return plane;
    }

    private static void LumaRow(byte[] pixels, int width, int height, int paddedWidth, int y, float[] plane)
    {
        var srcY = Math.Min(y, height - 1);
        var pixelBase = srcY * width * 3;
        var rowBase = y * paddedWidth;
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

                var yv = (new Vector<float>(r) * 0.299f) + (new Vector<float>(g) * 0.587f) + (new Vector<float>(b) * 0.114f) - new Vector<float>(128f);
                yv.CopyTo(plane.AsSpan(rowBase + x, lanes));
            }
        }

        for (; x < width; x++) {
            var offset = pixelBase + (x * 3);
            plane[rowBase + x] = (0.299f * pixels[offset]) + (0.587f * pixels[offset + 1]) + (0.114f * pixels[offset + 2]) - 128f;
        }

        for (; x < paddedWidth; x++) {
            plane[rowBase + x] = plane[rowBase + width - 1];
        }
    }

    private static float[] BuildChromaPlane(byte[] pixels, int width, int height, int paddedWidth, int paddedHeight, int hRatio, int vRatio, bool isCb)
    {
        var componentWidth = CeilDiv(width, hRatio);
        var componentHeight = CeilDiv(height, vRatio);
        var plane = new float[paddedWidth * paddedHeight];

        Parallel.For(0, paddedHeight, y => {
            var componentY = Math.Min(y, componentHeight - 1);
            var rowBase = y * paddedWidth;
            for (var x = 0; x < paddedWidth; x++) {
                var componentX = Math.Min(x, componentWidth - 1);
                plane[rowBase + x] = SampleChromaBox(pixels, width, height, componentX, componentY, hRatio, vRatio, isCb) - 128f;
            }
        });

        return plane;
    }

    private static float SampleChromaBox(byte[] pixels, int width, int height, int componentX, int componentY, int hRatio, int vRatio, bool isCb)
    {
        var sum = 0f;
        var count = 0;
        for (var dy = 0; dy < vRatio; dy++) {
            var srcY = Math.Min((componentY * vRatio) + dy, height - 1);
            var rowOffset = srcY * width * 3;
            for (var dx = 0; dx < hRatio; dx++) {
                var srcX = Math.Min((componentX * hRatio) + dx, width - 1);
                var offset = rowOffset + (srcX * 3);
                var r = pixels[offset];
                var g = pixels[offset + 1];
                var b = pixels[offset + 2];
                sum += isCb
                    ? 128f - (0.168736f * r) - (0.331264f * g) + (0.5f * b)
                    : 128f + (0.5f * r) - (0.418688f * g) - (0.081312f * b);
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

    private static void Quantize(ReadOnlySpan<float> dctCoefficients, float[] quantTable, Span<short> quantized)
    {
        var i = 0;
        var lanes = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated) {
            var min = new Vector<float>(short.MinValue);
            var max = new Vector<float>(short.MaxValue);

            for (; i + (2 * lanes) <= 64; i += 2 * lanes) {
                var a = QuantizeLane(dctCoefficients.Slice(i, lanes), quantTable.AsSpan(i, lanes), min, max);
                var b = QuantizeLane(dctCoefficients.Slice(i + lanes, lanes), quantTable.AsSpan(i + lanes, lanes), min, max);
                Vector.Narrow(a, b).CopyTo(quantized.Slice(i, 2 * lanes));
            }
        }

        for (; i < 64; i++) {
            quantized[i] = (short)Math.Clamp(MathF.Round(dctCoefficients[i] / quantTable[i]), short.MinValue, short.MaxValue);
        }
    }

    private static Vector<int> QuantizeLane(ReadOnlySpan<float> dctCoefficients, ReadOnlySpan<float> quantTable, Vector<float> min, Vector<float> max)
    {
        var divided = Vector.Round(new Vector<float>(dctCoefficients) / new Vector<float>(quantTable));
        return Vector.ConvertToInt32(Vector.Min(Vector.Max(divided, min), max));
    }

    private static int CeilDiv(int numerator, int denominator) => (numerator + denominator - 1) / denominator;
}
