namespace Lucitex.Jpeg;

public enum JpegChromaSubsampling
{
    Ratio444,
    Ratio422,
    Ratio420,
}

public sealed record JpegEncoderOptions
{
    public int Quality { get; init; } = 90;
    public bool Progressive { get; init; }
    public bool OptimizeHuffmanTables { get; init; } = true;
    public JpegChromaSubsampling ChromaSubsampling { get; init; } = JpegChromaSubsampling.Ratio420;
}
