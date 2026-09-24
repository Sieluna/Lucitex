namespace Lucitex.Webp.Lossy;

internal sealed class Vp8SegmentHeader
{
    public bool Enabled;
    public bool UpdateMap;
    public bool AbsoluteValues;
    public readonly int[] QuantIndex = new int[4];
    public readonly int[] LoopFilterLevel = new int[4];
    public readonly byte[] TreeProbs = [255, 255, 255];
}

internal sealed class Vp8LoopFilterHeader
{
    public bool Simple;
    public int Level;
    public int Sharpness;
    public bool DeltaEnabled;
    public readonly int[] RefDelta = new int[4];
    public readonly int[] ModeDelta = new int[4];
}

internal sealed class Vp8QuantHeader
{
    public int YacQi;
    public int YDcDelta;
    public int Y2DcDelta;
    public int Y2AcDelta;
    public int UvDcDelta;
    public int UvAcDelta;
}

internal sealed class Vp8DequantFactors
{
    public readonly int[] Y1 = new int[2];
    public readonly int[] Y2 = new int[2];
    public readonly int[] Uv = new int[2];
}

internal sealed class Vp8FrameHeader
{
    public int Width;
    public int Height;
    public int MbCols;
    public int MbRows;
    public bool ClampRequired;

    public readonly Vp8SegmentHeader Segment = new();
    public readonly Vp8LoopFilterHeader LoopFilter = new();
    public readonly Vp8QuantHeader Quant = new();
    public int NumTokenPartitions;
    public bool MbNoSkipCoeff;
    public int ProbSkipFalse;

    public readonly byte[,,,] CoeffProbs = (byte[,,,])Vp8Tables.DefaultCoeffProbs.Clone();

    public static Vp8FrameHeader ParseUncompressed(ReadOnlySpan<byte> data, out int firstPartitionSize, out int payloadOffset)
    {
        if (data.Length < 10) {
            throw new InvalidDataException("Truncated VP8 frame tag.");
        }
        var tag = data[0] | ((uint)data[1] << 8) | ((uint)data[2] << 16);
        if (((tag >> 1) & 7) > 3 || (tag & 16) == 0) {
            throw new InvalidDataException("Invalid VP8 key frame profile or visibility.");
        }
        var keyFrame = (tag & 1) == 0;
        if (!keyFrame) {
            throw new NotSupportedException("Only VP8 key frames are supported in WebP.");
        }
        firstPartitionSize = (int)((tag >> 5) & 0x7FFFF);
        if (data[3] != 0x9d || data[4] != 0x01 || data[5] != 0x2a) {
            throw new InvalidDataException("Invalid VP8 key frame start code.");
        }
        var widthField = data[6] | (data[7] << 8);
        var heightField = data[8] | (data[9] << 8);
        var header = new Vp8FrameHeader {
            Width = widthField & 0x3FFF,
            Height = heightField & 0x3FFF,
        };
        if (header.Width == 0 || header.Height == 0) {
            throw new InvalidDataException("Invalid VP8 frame dimensions.");
        }
        header.MbCols = (header.Width + 15) / 16;
        header.MbRows = (header.Height + 15) / 16;
        payloadOffset = 10;
        return header;
    }

    public void ParseCompressed(Vp8BoolDecoder d)
    {
        _ = d.GetFlag();
        ClampRequired = d.GetFlag() == 0;

        Segment.Enabled = d.GetFlag() != 0;
        if (Segment.Enabled) {
            ParseSegmentation(d);
        }

        LoopFilter.Simple = d.GetFlag() != 0;
        LoopFilter.Level = (int)d.GetLiteral(6);
        LoopFilter.Sharpness = (int)d.GetLiteral(3);
        ParseLoopFilterAdjustments(d);

        NumTokenPartitions = 1 << (int)d.GetLiteral(2);

        ParseQuantIndices(d);

        _ = d.GetFlag();

        ParseTokenProbUpdates(d);

        MbNoSkipCoeff = d.GetFlag() != 0;
        ProbSkipFalse = MbNoSkipCoeff ? (int)d.GetLiteral(8) : 0;
    }

    private void ParseSegmentation(Vp8BoolDecoder d)
    {
        Segment.UpdateMap = d.GetFlag() != 0;
        var updateData = d.GetFlag() != 0;
        if (updateData) {
            Segment.AbsoluteValues = d.GetFlag() != 0;
            for (var i = 0; i < 4; i++) {
                Segment.QuantIndex[i] = d.GetFlag() != 0 ? d.GetSignedValue(7) : 0;
            }
            for (var i = 0; i < 4; i++) {
                Segment.LoopFilterLevel[i] = d.GetFlag() != 0 ? d.GetSignedValue(6) : 0;
            }
        }
        if (Segment.UpdateMap) {
            for (var i = 0; i < 3; i++) {
                Segment.TreeProbs[i] = d.GetFlag() != 0 ? (byte)d.GetLiteral(8) : (byte)255;
            }
        }
    }

    private void ParseLoopFilterAdjustments(Vp8BoolDecoder d)
    {
        LoopFilter.DeltaEnabled = d.GetFlag() != 0;
        if (!LoopFilter.DeltaEnabled) {
            return;
        }
        if (d.GetFlag() == 0) {
            return;
        }
        for (var i = 0; i < 4; i++) {
            if (d.GetFlag() != 0) {
                LoopFilter.RefDelta[i] = d.GetSignedValue(6);
            }
        }
        for (var i = 0; i < 4; i++) {
            if (d.GetFlag() != 0) {
                LoopFilter.ModeDelta[i] = d.GetSignedValue(6);
            }
        }
    }

    private void ParseQuantIndices(Vp8BoolDecoder d)
    {
        Quant.YacQi = (int)d.GetLiteral(7);
        Quant.YDcDelta = d.GetFlag() != 0 ? d.GetSignedValue(4) : 0;
        Quant.Y2DcDelta = d.GetFlag() != 0 ? d.GetSignedValue(4) : 0;
        Quant.Y2AcDelta = d.GetFlag() != 0 ? d.GetSignedValue(4) : 0;
        Quant.UvDcDelta = d.GetFlag() != 0 ? d.GetSignedValue(4) : 0;
        Quant.UvAcDelta = d.GetFlag() != 0 ? d.GetSignedValue(4) : 0;
    }

    private void ParseTokenProbUpdates(Vp8BoolDecoder d)
    {
        for (var i = 0; i < 4; i++) {
            for (var j = 0; j < 8; j++) {
                for (var k = 0; k < 3; k++) {
                    for (var t = 0; t < 11; t++) {
                        if (d.GetBool(Vp8Tables.CoeffUpdateProbs[i, j, k, t]) != 0) {
                            CoeffProbs[i, j, k, t] = (byte)d.GetLiteral(8);
                        }
                    }
                }
            }
        }
    }

    public Vp8DequantFactors[] BuildDequantFactors()
    {
        var segmentCount = Segment.Enabled ? 4 : 1;
        var result = new Vp8DequantFactors[4];
        for (var i = 0; i < segmentCount; i++) {
            var q = Quant.YacQi;
            if (Segment.Enabled) {
                q = Segment.AbsoluteValues ? Segment.QuantIndex[i] : q + Segment.QuantIndex[i];
            }
            var factors = new Vp8DequantFactors();
            factors.Y1[0] = DcQuant(q + Quant.YDcDelta);
            factors.Y1[1] = AcQuant(q);
            factors.Uv[0] = Math.Min(132, DcQuant(q + Quant.UvDcDelta));
            factors.Uv[1] = AcQuant(q + Quant.UvAcDelta);
            factors.Y2[0] = DcQuant(q + Quant.Y2DcDelta) * 2;
            factors.Y2[1] = Math.Max(8, AcQuant(q + Quant.Y2AcDelta) * 155 / 100);
            result[i] = factors;
        }
        for (var i = segmentCount; i < 4; i++) {
            result[i] = result[0];
        }
        return result;
    }

    private static int ClampQ(int q) => q < 0 ? 0 : q > 127 ? 127 : q;

    private static int DcQuant(int q) => Vp8Tables.DcQLookup[ClampQ(q)];

    private static int AcQuant(int q) => Vp8Tables.AcQLookup[ClampQ(q)];
}
