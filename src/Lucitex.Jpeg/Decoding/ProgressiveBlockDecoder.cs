using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal static class ProgressiveBlockDecoder
{
    public static void DecodeDcFirst(JpegBitReader reader, JpegComponentState component, int blockOffset, JpegHuffmanDecodeTable dcTable, int approximationLow)
    {
        var size = dcTable.Decode(reader);
        var diff = reader.ReceiveExtend(size);
        component.DcPredictor += diff;
        component.Coefficients[blockOffset] = (short)(component.DcPredictor << approximationLow);
    }

    public static void DecodeDcRefine(JpegBitReader reader, JpegComponentState component, int blockOffset, int approximationLow)
    {
        if (reader.ReadBit() == 1) {
            component.Coefficients[blockOffset] |= (short)(1 << approximationLow);
        }
    }

    public static void DecodeAcFirst(
        JpegBitReader reader,
        JpegComponentState component,
        int blockOffset,
        JpegHuffmanDecodeTable acTable,
        int spectralStart,
        int spectralEnd,
        int approximationLow,
        ref int eobRun)
    {
        if (eobRun > 0) {
            eobRun--;
            return;
        }

        var coefficients = component.Coefficients;
        var k = spectralStart;

        while (k <= spectralEnd) {
            var value = acTable.DecodeAc(reader, out var run);
            if (value == 0) {
                if (run < 15) {
                    eobRun = (1 << run) - 1;
                    if (run > 0) {
                        eobRun += reader.ReadBits(run);
                    }

                    break;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > spectralEnd) {
                throw new ImageFormatException("jpeg", "BadEntropyData", "Progressive AC coefficient run exceeded the spectral band bounds.");
            }

            coefficients[blockOffset + JpegZigZag.Order[k]] = (short)(value * (1 << approximationLow));
            k++;
        }
    }

    public static void DecodeAcRefine(
        JpegBitReader reader,
        JpegComponentState component,
        int blockOffset,
        JpegHuffmanDecodeTable acTable,
        int spectralStart,
        int spectralEnd,
        int approximationLow,
        ref int eobRun)
    {
        var coefficients = component.Coefficients;
        var bit = 1 << approximationLow;
        var k = spectralStart;

        if (eobRun > 0) {
            eobRun--;
            RefineRemainingCoefficients(reader, coefficients, blockOffset, k, spectralEnd, bit);
            return;
        }

        while (k <= spectralEnd) {
            var runSize = acTable.Decode(reader);
            var run = runSize >> 4;
            var size = runSize & 0xF;
            if (size == 0 && run < 15) {
                eobRun = (1 << run) - 1 + reader.ReadBits(run);
                RefineRemainingCoefficients(reader, coefficients, blockOffset, k, spectralEnd, bit);
                return;
            }
            if (size > 1) {
                throw new ImageFormatException("jpeg", "BadEntropyData", "Progressive AC refinement requires a one-bit coefficient magnitude.");
            }

            var newValue = size == 1 ? reader.ReadBit() == 1 ? bit : -bit : 0;

            while (k <= spectralEnd) {
                var index = blockOffset + JpegZigZag.Order[k];
                if (coefficients[index] != 0) {
                    RefineExistingCoefficient(reader, coefficients, index, bit);
                }
                else {
                    if (run == 0) {
                        break;
                    }

                    run--;
                }

                k++;
            }
            if (k > spectralEnd) {
                throw new ImageFormatException("jpeg", "BadEntropyData", "Progressive AC refinement run exceeded the spectral band bounds.");
            }
            coefficients[blockOffset + JpegZigZag.Order[k]] = (short)newValue;
            k++;
        }
    }

    private static void RefineRemainingCoefficients(JpegBitReader reader, short[] coefficients, int blockOffset, int start, int end, int bit)
    {
        for (var k = start; k <= end; k++) {
            RefineExistingCoefficient(reader, coefficients, blockOffset + JpegZigZag.Order[k], bit);
        }
    }

    private static void RefineExistingCoefficient(JpegBitReader reader, short[] coefficients, int index, int bit)
    {
        var value = coefficients[index];
        if (value == 0 || reader.ReadBit() != 1) {
            return;
        }

        if ((value & bit) == 0) {
            coefficients[index] = (short)(value > 0 ? value + bit : value - bit);
        }
    }
}
