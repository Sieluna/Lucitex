namespace Lucitex.Jpeg.Format;

internal sealed class JpegComponent
{
    public required byte Id { get; init; }

    public required byte HSampling { get; init; }

    public required byte VSampling { get; init; }

    public required byte QuantTableId { get; init; }
}

internal sealed class JpegFrameHeader
{
    public required bool Progressive { get; init; }

    public required int Precision { get; init; }

    public required int Height { get; init; }

    public required int Width { get; init; }

    public required IReadOnlyList<JpegComponent> Components { get; init; }

    public int HMax => Components.Max(c => (int)c.HSampling);

    public int VMax => Components.Max(c => (int)c.VSampling);
}

internal sealed class JpegQuantizationTable
{
    public required int Id { get; init; }

    public required ushort[] Values { get; init; }
}

internal sealed class JpegHuffmanSpec
{
    public required int Id { get; init; }

    public required bool IsAc { get; init; }

    public required byte[] Bits { get; init; }

    public required byte[] Values { get; init; }
}

internal sealed class JpegScanComponent
{
    public required byte ComponentSelector { get; init; }

    public required byte DcTableSelector { get; init; }

    public required byte AcTableSelector { get; init; }
}

internal sealed class JpegScanHeader
{
    public required IReadOnlyList<JpegScanComponent> Components { get; init; }

    public required byte SpectralStart { get; init; }

    public required byte SpectralEnd { get; init; }

    public required byte SuccessiveApproxHigh { get; init; }

    public required byte SuccessiveApproxLow { get; init; }
}

internal sealed class JpegAppSegment
{
    public required byte Marker { get; init; }

    public required byte[] Data { get; init; }
}
