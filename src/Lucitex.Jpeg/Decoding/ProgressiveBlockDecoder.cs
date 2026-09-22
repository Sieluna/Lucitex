using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal static class ProgressiveBlockDecoder
{
    public static void DecodeDcFirst(JpegBitReader reader, JpegComponentState component, int blockOffset, JpegHuffmanDecodeTable dcTable, int approximationLow)
    {
        var size = dcTable.Decode(reader);
        var diff = size == 0 ? 0 : reader.ReceiveExtend(size);
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
            var runSize = acTable.Decode(reader);
            var run = runSize >> 4;
            var size = runSize & 0xF;

            if (size == 0) {
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

            coefficients[blockOffset + JpegZigZag.Order[k]] = (short)(reader.ReceiveExtend(size) * (1 << approximationLow));
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
            for (; k <= spectralEnd; k++) {
                RefineExistingCoefficient(reader, coefficients, blockOffset + JpegZigZag.Order[k], bit);
            }

            return;
        }

        while (k <= spectralEnd) {
            var runSize = acTable.Decode(reader);
            var run = runSize >> 4;
            var size = runSize & 0xF;
            var newValue = 0;

            if (size == 0) {
                if (run < 15) {
                    eobRun = (1 << run) - 1;
                    if (run > 0) {
                        eobRun += reader.ReadBits(run);
                    }

                    run = 64;
                }
                else {
                    run = 16;
                }
            }
            else {
                newValue = reader.ReadBit() == 1 ? bit : -bit;
            }

            while (k <= spectralEnd) {
                var index = blockOffset + JpegZigZag.Order[k];
                if (coefficients[index] != 0) {
                    RefineExistingCoefficient(reader, coefficients, index, bit);
                }
                else {
                    if (run == 0) {
                        if (newValue != 0) {
                            coefficients[index] = (short)newValue;
                        }

                        k++;
                        break;
                    }

                    run--;
                }

                k++;
            }
        }
    }

    private static void RefineExistingCoefficient(JpegBitReader reader, short[] coefficients, int index, int bit)
    {
        if (reader.ReadBit() != 1) {
            return;
        }

        var value = coefficients[index];
        if ((value & bit) == 0) {
            coefficients[index] = (short)(value > 0 ? value + bit : value - bit);
        }
    }
}
