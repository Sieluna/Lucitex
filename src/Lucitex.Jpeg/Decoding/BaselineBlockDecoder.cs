using Lucitex.Core.Execution;
using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Decoding;

internal static class BaselineBlockDecoder
{
    public static void DecodeBlock(
        JpegBitReader reader,
        JpegComponentState component,
        int blockOffset,
        JpegHuffmanDecodeTable dcTable,
        JpegHuffmanDecodeTable acTable) =>
        DecodeBlock(reader, component.Coefficients, blockOffset, ref component.DcPredictor, dcTable, acTable);

    public static void DecodeBlock(
        JpegBitReader reader,
        short[] coefficients,
        int blockOffset,
        ref int dcPredictor,
        JpegHuffmanDecodeTable dcTable,
        JpegHuffmanDecodeTable acTable)
    {
        var dcSize = dcTable.Decode(reader);
        var diff = reader.ReceiveExtend(dcSize);
        dcPredictor += diff;
        coefficients[blockOffset] = (short)dcPredictor;

        var k = 1;
        while (k <= 63) {
            var runSize = acTable.Decode(reader);
            var run = runSize >> 4;
            var size = runSize & 0xF;

            if (size == 0) {
                if (run != 15) {
                    break;
                }

                k += 16;
                continue;
            }

            k += run;
            if (k > 63) {
                throw new ImageFormatException("jpeg", "BadEntropyData", "AC coefficient run exceeded the block bounds.");
            }

            coefficients[blockOffset + JpegZigZag.Order[k]] = (short)reader.ReceiveExtend(size);
            k++;
        }
    }
}
