using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal static class JpegEncoder
{
    public static void Encode(Stream stream, byte[] pixels, int width, int height, int componentCount, JpegEncoderOptions options)
    {
        using var frame = new JpegEncodingFrame(width, height, componentCount, options);
        WriteHeader(stream, frame, options.Progressive);
        JpegCoefficientEncoder.Compute(pixels, frame);

        var restartInterval = options.Progressive ? 0 : ChooseRestartInterval(frame.McuCount, frame.NonZeroCount);
        if (options.OptimizeHuffmanTables) {
            OptimizeTables(frame, restartInterval);
        }
        WriteHuffmanTables(stream, frame);

        if (options.Progressive) {
            EncodeProgressive(stream, frame);
        }
        else {
            EncodeBaseline(stream, frame, restartInterval);
        }
        JpegDocumentWriter.WriteEoi(stream);
    }

    private static void WriteHeader(Stream stream, JpegEncodingFrame frame, bool progressive)
    {
        JpegDocumentWriter.WriteSoi(stream);
        JpegDocumentWriter.WriteJfifHeader(stream);
        for (var table = 0; table < frame.TableCount; table++) {
            JpegDocumentWriter.WriteQuantizationTable(stream, table, frame.QuantizationTables[table]);
        }
        JpegDocumentWriter.WriteFrameHeader(stream, progressive ? JpegMarkers.Sof2 : JpegMarkers.Sof0,
            frame.Width, frame.Height, frame.ComponentIds, frame.HorizontalSampling, frame.VerticalSampling, frame.TableIds);
    }

    private static void WriteHuffmanTables(Stream stream, JpegEncodingFrame frame)
    {
        for (var i = 0; i < frame.TableCount; i++) {
            JpegDocumentWriter.WriteHuffmanTable(stream, frame.DcTables[i].Spec);
            JpegDocumentWriter.WriteHuffmanTable(stream, frame.AcTables[i].Spec);
        }
    }

    private static void EncodeBaseline(Stream stream, JpegEncodingFrame frame, int restartInterval)
    {
        var segmentCount = restartInterval > 0 ? CeilDiv(frame.McuCount, restartInterval) : 1;

        var tableIds = frame.TableIds;

        if (segmentCount > 1) {
            JpegDocumentWriter.WriteDri(stream, restartInterval);
        }

        JpegDocumentWriter.WriteScanHeader(stream, frame.ComponentIds, tableIds, tableIds, 0, 63, 0);

        var segmentWriters = new JpegBitWriter[segmentCount];

        void EncodeSegment(int segmentIndex)
        {
            var mcuStart = segmentIndex * restartInterval;
            var mcuEnd = segmentCount == 1 ? frame.McuCount : Math.Min(mcuStart + restartInterval, frame.McuCount);
            var writer = new JpegBitWriter();
            segmentWriters[segmentIndex] = writer;
            var dcPredictors = new int[frame.ComponentCount];

            for (var mcuIndex = mcuStart; mcuIndex < mcuEnd; mcuIndex++) {
                var mcuRow = mcuIndex / frame.McuColumns;
                var mcuCol = mcuIndex % frame.McuColumns;

                for (var c = 0; c < frame.ComponentCount; c++) {
                    for (var v = 0; v < frame.VerticalSampling[c]; v++) {
                        for (var h = 0; h < frame.HorizontalSampling[c]; h++) {
                            var blockRow = (mcuRow * frame.VerticalSampling[c]) + v;
                            var blockCol = (mcuCol * frame.HorizontalSampling[c]) + h;
                            var blockOffset = frame.BlockOffset(c, blockRow, blockCol);
                            JpegBlockEncoder.EncodeBlock(writer, frame.Blocks[c][blockOffset / 64], frame.Tokens[c].AsSpan(blockOffset, 64), ref dcPredictors[c], frame.DcTables[c], frame.AcTables[c]);
                        }
                    }
                }
            }

            writer.PadAndFlush();
        }

        try {
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
        finally {
            foreach (var writer in segmentWriters) {
                writer?.Dispose();
            }
        }
    }

    private static void OptimizeTables(JpegEncodingFrame frame, int restartInterval)
    {
        Span<long> dcCounts = stackalloc long[512];
        dcCounts.Clear();
        Span<int> predictors = stackalloc int[3];
        predictors.Clear();
        for (var mcu = 0; mcu < frame.McuCount; mcu++) {
            if (restartInterval > 0 && mcu % restartInterval == 0) {
                predictors.Clear();
            }
            var row = mcu / frame.McuColumns;
            var col = mcu % frame.McuColumns;
            for (var c = 0; c < frame.ComponentCount; c++) {
                var table = frame.TableIds[c];
                for (var v = 0; v < frame.VerticalSampling[c]; v++) {
                    for (var h = 0; h < frame.HorizontalSampling[c]; h++) {
                        var blockRow = row * frame.VerticalSampling[c] + v;
                        var blockCol = col * frame.HorizontalSampling[c] + h;
                        var blockOffset = frame.BlockOffset(c, blockRow, blockCol);
                        var dc = frame.Blocks[c][blockOffset / 64].Dc;
                        dcCounts[table * 256 + JpegMagnitude.GetSize(dc - predictors[c])]++;
                        predictors[c] = dc;
                    }
                }
            }
        }
        for (var i = 0; i < frame.TableCount; i++) {
            frame.DcTables[i] = new JpegHuffmanEncodeTable(JpegHuffmanOptimizer.Build(dcCounts.Slice(i * 256, 256), i, false));
            frame.AcTables[i] = new JpegHuffmanEncodeTable(JpegHuffmanOptimizer.Build(frame.AcFrequencies.AsSpan(i * 256, 256), i, true));
        }
        if (frame.ComponentCount == 3) {
            frame.DcTables[2] = frame.DcTables[1];
            frame.AcTables[2] = frame.AcTables[1];
        }
    }

    private static int ChooseRestartInterval(int totalMcus, long nonZeroCount)
    {
        const int minInterval = 4;
        const int minNonZeroPerSegment = 8_192;
        if (totalMcus <= minInterval) {
            return 0;
        }
        var maxSegmentsByWork = Math.Max(1, nonZeroCount / minNonZeroPerSegment);
        var targetSegments = (int)Math.Min(Math.Min(totalMcus / minInterval, Environment.ProcessorCount * 2), maxSegmentsByWork);
        return targetSegments <= 1 ? 0 : CeilDiv(totalMcus, targetSegments);
    }
    private static void EncodeProgressive(Stream stream, JpegEncodingFrame frame)
    {
        var dcTableIds = frame.TableIds;
        JpegDocumentWriter.WriteScanHeader(stream, frame.ComponentIds, dcTableIds, dcTableIds, 0, 0, 0);

        using var dcWriter = new JpegBitWriter();
        var dcPredictors = new int[frame.ComponentCount];

        for (var mcuRow = 0; mcuRow < frame.McuRows; mcuRow++) {
            for (var mcuCol = 0; mcuCol < frame.McuColumns; mcuCol++) {
                for (var c = 0; c < frame.ComponentCount; c++) {
                    for (var v = 0; v < frame.VerticalSampling[c]; v++) {
                        for (var h = 0; h < frame.HorizontalSampling[c]; h++) {
                            var blockRow = (mcuRow * frame.VerticalSampling[c]) + v;
                            var blockCol = (mcuCol * frame.HorizontalSampling[c]) + h;
                            var blockOffset = frame.BlockOffset(c, blockRow, blockCol);
                            JpegBlockEncoder.EncodeDc(dcWriter, frame.Blocks[c][blockOffset / 64].Dc, ref dcPredictors[c], frame.DcTables[c]);
                        }
                    }
                }
            }
        }

        dcWriter.PadAndFlush();
        dcWriter.CopyTo(stream);

        for (var c = 0; c < frame.ComponentCount; c++) {
            byte[] scanComponentId = [frame.ComponentIds[c]];
            byte[] acTableId = [frame.TableIds[c]];
            JpegDocumentWriter.WriteScanHeader(stream, scanComponentId, acTableId, acTableId, 1, 63, 0);

            using var acWriter = new JpegBitWriter();
            var blocksPerLine = frame.BlockColumns(c);
            var blocksPerColumn = frame.BlockRows(c);
            for (var blockRow = 0; blockRow < blocksPerColumn; blockRow++) {
                for (var blockCol = 0; blockCol < blocksPerLine; blockCol++) {
                    var blockOffset = frame.BlockOffset(c, blockRow, blockCol);
                    JpegBlockEncoder.EncodeAc(acWriter, frame.Tokens[c].AsSpan(blockOffset, frame.Blocks[c][blockOffset / 64].TokenCount), frame.AcTables[c]);
                }
            }

            acWriter.PadAndFlush();
            acWriter.CopyTo(stream);
        }
    }

    private static int CeilDiv(int numerator, int denominator) => (numerator + denominator - 1) / denominator;
}
