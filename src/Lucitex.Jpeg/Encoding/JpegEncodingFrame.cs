using Lucitex.Jpeg.Format;

namespace Lucitex.Jpeg.Encoding;

internal sealed class JpegEncodingFrame
{
    public JpegEncodingFrame(int width, int height, int componentCount, JpegEncoderOptions options)
    {
        Width = width;
        Height = height;
        var subsampling = options.ChromaSubsampling;
        HorizontalSampling = componentCount == 3 ? [(byte)(subsampling == JpegChromaSubsampling.Ratio444 ? 1 : 2), 1, 1] : [1];
        VerticalSampling = componentCount == 3 ? [(byte)(subsampling == JpegChromaSubsampling.Ratio420 ? 2 : 1), 1, 1] : [1];
        ComponentIds = componentCount == 3 ? [1, 2, 3] : [1];
        TableIds = componentCount == 3 ? [0, 1, 1] : [0];
        MaxHorizontalSampling = HorizontalSampling.Max();
        MaxVerticalSampling = VerticalSampling.Max();
        McuColumns = CeilDiv(width, 8 * MaxHorizontalSampling);
        McuRows = CeilDiv(height, 8 * MaxVerticalSampling);

        var lumaQuantization = JpegStandardTables.ScaleQuantizationTable(JpegStandardTables.LuminanceQuantization, options.Quality);
        var chromaQuantization = JpegStandardTables.ScaleQuantizationTable(JpegStandardTables.ChrominanceQuantization, options.Quality);
        QuantizationTables = componentCount == 3 ? [lumaQuantization, chromaQuantization, chromaQuantization] : [lumaQuantization];
        DcTables = new JpegHuffmanEncodeTable[componentCount];
        AcTables = new JpegHuffmanEncodeTable[componentCount];
        PaddedBlockColumns = new int[componentCount];
        PaddedBlockRows = new int[componentCount];
        Coefficients = new short[componentCount][];
        for (var component = 0; component < componentCount; component++) {
            DcTables[component] = JpegHuffmanEncodeTable.GetStandard(TableIds[component], isAc: false);
            AcTables[component] = JpegHuffmanEncodeTable.GetStandard(TableIds[component], isAc: true);
            PaddedBlockColumns[component] = McuColumns * HorizontalSampling[component];
            PaddedBlockRows[component] = McuRows * VerticalSampling[component];
            Coefficients[component] = new short[checked(PaddedBlockColumns[component] * PaddedBlockRows[component] * 64)];
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int ComponentCount => ComponentIds.Length;
    public int TableCount => Math.Min(2, ComponentCount);
    public int MaxHorizontalSampling { get; }
    public int MaxVerticalSampling { get; }
    public int McuColumns { get; }
    public int McuRows { get; }
    public int McuCount => McuColumns * McuRows;
    public byte[] ComponentIds { get; }
    public byte[] TableIds { get; }
    public byte[] HorizontalSampling { get; }
    public byte[] VerticalSampling { get; }
    public int[] PaddedBlockColumns { get; }
    public int[] PaddedBlockRows { get; }
    public ushort[][] QuantizationTables { get; }
    public JpegHuffmanEncodeTable[] DcTables { get; }
    public JpegHuffmanEncodeTable[] AcTables { get; }
    public short[][] Coefficients { get; }

    public int BlockColumns(int component) => CeilDiv(CeilDiv(Width, 8) * HorizontalSampling[component], MaxHorizontalSampling);

    public int BlockRows(int component) => CeilDiv(CeilDiv(Height, 8) * VerticalSampling[component], MaxVerticalSampling);

    public int BlockOffset(int component, int blockRow, int blockColumn) => ((blockRow * PaddedBlockColumns[component]) + blockColumn) * 64;

    private static int CeilDiv(int numerator, int denominator) => (numerator + denominator - 1) / denominator;
}
