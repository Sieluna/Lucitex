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
        JpegHuffmanDecodeTable acTable)
    {
        var coefficients = component.Coefficients;

        var dcSize = dcTable.Decode(reader);
        var diff = reader.ReceiveExtend(dcSize);
        component.DcPredictor += diff;
        coefficients[blockOffset] = component.DcPredictor;

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

            coefficients[blockOffset + JpegZigZag.Order[k]] = reader.ReceiveExtend(size);
            k++;
        }
    }
}
