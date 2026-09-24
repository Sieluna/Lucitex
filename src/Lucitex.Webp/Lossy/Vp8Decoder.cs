namespace Lucitex.Webp.Lossy;

internal sealed class Vp8MacroblockInfo
{
    public int SegmentId;
    public bool Skip;
    public int YMode;
    public int UvMode;
    public byte[]? BModes;
    public bool HasCoefficients;
}

internal sealed class Vp8DecodedFrame
{
    public required int Width;
    public required int Height;
    public required byte[] Y;
    public required byte[] U;
    public required byte[] V;
    public required int YStride;
    public required int UvStride;
}

internal static class Vp8Decoder
{
    public static Vp8DecodedFrame Decode(ReadOnlySpan<byte> data)
    {
        var header = Vp8FrameHeader.ParseUncompressed(data, out var firstPartitionSize, out var payloadOffset);
        if (payloadOffset + firstPartitionSize > data.Length) {
            throw new InvalidDataException("VP8 first partition exceeds frame data.");
        }

        var firstPartition = new Vp8BoolDecoder(data.Slice(payloadOffset, firstPartitionSize).ToArray());
        header.ParseCompressed(firstPartition);

        var mbCols = header.MbCols;
        var mbRows = header.MbRows;
        var mbInfos = new Vp8MacroblockInfo[mbRows * mbCols];
        DecodeMacroblockModes(header, firstPartition, mbInfos, mbCols, mbRows);

        var tokenDataOffset = payloadOffset + firstPartitionSize;
        var partitions = SlicePartitions(data, tokenDataOffset, header.NumTokenPartitions);
        var decoders = new Vp8BoolDecoder[partitions.Length];
        for (var i = 0; i < partitions.Length; i++) {
            decoders[i] = new Vp8BoolDecoder(partitions[i]);
        }

        var yStride = mbCols * 16;
        var uvStride = mbCols * 8;
        var y = new byte[yStride * mbRows * 16];
        var u = new byte[uvStride * mbRows * 8];
        var v = new byte[uvStride * mbRows * 8];

        var dequant = header.BuildDequantFactors();

        var aboveContext = new byte[mbCols][];
        for (var i = 0; i < mbCols; i++) {
            aboveContext[i] = new byte[9];
        }
        var leftContext = new byte[9];

        Span<short> coeffs = new short[25 * 16];
        Span<short> residual = new short[16];

        for (var mbY = 0; mbY < mbRows; mbY++) {
            Array.Clear(leftContext);
            var decoder = decoders[mbY % decoders.Length];
            for (var mbX = 0; mbX < mbCols; mbX++) {
                var mb = mbInfos[(mbY * mbCols) + mbX];
                coeffs.Clear();
                if (mb.Skip) {
                    ResetSkippedContext(leftContext, aboveContext[mbX], mb.YMode != Vp8Tables.BPred);
                    mb.HasCoefficients = false;
                }
                else {
                    var factors = dequant[mb.SegmentId];
                    mb.HasCoefficients = DecodeMacroblockTokens(decoder, leftContext, aboveContext[mbX], coeffs, mb.YMode != Vp8Tables.BPred, factors, header.CoeffProbs);
                }

                ReconstructMacroblock(header, mb, mbX, mbY, mbCols, y, yStride, u, v, uvStride, coeffs, residual);
            }
            decoders[mbY % decoders.Length] = decoder;
        }

        ApplyLoopFilter(header, mbInfos, mbCols, mbRows, y, yStride, u, v, uvStride);

        return new Vp8DecodedFrame {
            Width = header.Width,
            Height = header.Height,
            Y = y,
            U = u,
            V = v,
            YStride = yStride,
            UvStride = uvStride,
        };
    }

    private static byte[][] SlicePartitions(ReadOnlySpan<byte> data, int offset, int count)
    {
        var result = new byte[count][];
        var tableSize = (count - 1) * 3;
        if (offset + tableSize > data.Length) {
            throw new InvalidDataException("Truncated VP8 partition size table.");
        }
        var cursor = offset + tableSize;
        for (var i = 0; i < count; i++) {
            int size;
            if (i < count - 1) {
                var tableEntry = offset + (i * 3);
                size = data[tableEntry] | (data[tableEntry + 1] << 8) | (data[tableEntry + 2] << 16);
            }
            else {
                size = data.Length - cursor;
            }
            if (size < 0 || cursor + size > data.Length) {
                throw new InvalidDataException("Invalid VP8 token partition size.");
            }
            result[i] = data.Slice(cursor, size).ToArray();
            cursor += size;
        }
        return result;
    }

    private static void DecodeMacroblockModes(Vp8FrameHeader header, Vp8BoolDecoder d, Vp8MacroblockInfo[] mbInfos, int mbCols, int mbRows)
    {
        var aboveBModes = new byte[mbCols * 4];
        var leftBModes = new byte[4];

        for (var mbY = 0; mbY < mbRows; mbY++) {
            Array.Fill(leftBModes, (byte)0);
            for (var mbX = 0; mbX < mbCols; mbX++) {
                var mb = new Vp8MacroblockInfo();

                mb.SegmentId = header is { Segment.Enabled: true, Segment.UpdateMap: true }
                    ? d.GetTree(Vp8Tables.MbSegmentTree, header.Segment.TreeProbs)
                    : 0;

                mb.Skip = header.MbNoSkipCoeff && d.GetBool(header.ProbSkipFalse) != 0;

                mb.YMode = d.GetTree(Vp8Tables.KfYModeTree, Vp8Tables.KfYModeProb);

                if (mb.YMode == Vp8Tables.BPred) {
                    var bModes = new byte[16];
                    for (var by = 0; by < 4; by++) {
                        for (var bx = 0; bx < 4; bx++) {
                            var above = by == 0 ? aboveBModes[(mbX * 4) + bx] : bModes[((by - 1) * 4) + bx];
                            var left = bx == 0 ? leftBModes[by] : bModes[(by * 4) + bx - 1];
                            var mode = (byte)d.GetTree(Vp8Tables.BModeTree, GetKfBModeProb(above, left));
                            bModes[(by * 4) + bx] = mode;
                        }
                    }
                    mb.BModes = bModes;
                    for (var i = 0; i < 4; i++) {
                        aboveBModes[(mbX * 4) + i] = bModes[12 + i];
                        leftBModes[i] = bModes[(i * 4) + 3];
                    }
                }
                else {
                    var derived = (byte)DerivedBMode(mb.YMode);
                    for (var i = 0; i < 4; i++) {
                        aboveBModes[(mbX * 4) + i] = derived;
                        leftBModes[i] = derived;
                    }
                }

                mb.UvMode = d.GetTree(Vp8Tables.UvModeTree, Vp8Tables.KfUvModeProb);

                mbInfos[(mbY * mbCols) + mbX] = mb;
            }
        }
    }

    private static int DerivedBMode(int yMode) => yMode switch {
        Vp8Tables.DcPred => Vp8Tables.BDcPred,
        Vp8Tables.VPred => Vp8Tables.BVePred,
        Vp8Tables.HPred => Vp8Tables.BHePred,
        _ => Vp8Tables.BTmPred,
    };

    private static ReadOnlySpan<byte> GetKfBModeProb(byte above, byte left)
    {
        var table = Vp8Tables.KfBModeProb;
        var result = new byte[9];
        for (var i = 0; i < 9; i++) {
            result[i] = table[above, left, i];
        }
        return result;
    }

    private static void ResetSkippedContext(byte[] left, byte[] above, bool hasY2)
    {
        for (var i = 0; i < 8; i++) {
            left[i] = 0;
            above[i] = 0;
        }
        if (hasY2) {
            left[8] = 0;
            above[8] = 0;
        }
    }

    private static bool DecodeMacroblockTokens(Vp8BoolDecoder d, byte[] left, byte[] above, Span<short> coeffs, bool hasY2, Vp8DequantFactors factors, byte[,,,] coeffProbs)
    {
        var anyNonZero = false;

        if (hasY2) {
            anyNonZero |= DecodeBlock(d, left, above, 24, 1, coeffs.Slice(24 * 16, 16), factors.Y2, coeffProbs);
        }

        var yType = hasY2 ? 0 : 3;
        var yFactors = factors.Y1;
        for (var i = 0; i < 16; i++) {
            anyNonZero |= DecodeBlock(d, left, above, i, yType, coeffs.Slice(i * 16, 16), yFactors, coeffProbs);
        }

        for (var i = 16; i < 24; i++) {
            anyNonZero |= DecodeBlock(d, left, above, i, 2, coeffs.Slice(i * 16, 16), factors.Uv, coeffProbs);
        }

        return anyNonZero;
    }

    private static bool DecodeBlock(Vp8BoolDecoder d, byte[] left, byte[] above, int blockIndex, int type, Span<short> output, int[] dqf, byte[,,,] coeffProbs)
    {
        var leftSlot = Vp8Tables.LeftContextIndex[blockIndex];
        var aboveSlot = Vp8Tables.AboveContextIndex[blockIndex];
        var ctx = left[leftSlot] + above[aboveSlot];

        var firstCoeff = type == 0 ? 1 : 0;
        var c = firstCoeff;
        var skipEobCheck = false;
        var lastNonZero = -1;

        Span<byte> probs = stackalloc byte[11];
        while (c < 16) {
            var band = Vp8Tables.CoeffBands[c];
            for (var i = 0; i < 11; i++) {
                probs[i] = coeffProbs[type, band, ctx, i];
            }

            var start = 0;
            if (skipEobCheck) {
                start = 2;
            }
            var token = d.GetTree(Vp8Tables.CoeffTree, probs, start);

            if (token == Vp8Tables.DctEob) {
                break;
            }

            int absValue;
            if (token <= 4) {
                absValue = token;
            }
            else {
                var categoryIndex = token - Vp8Tables.DctCat1;
                var extraProbs = Vp8Tables.CategoryProbs[categoryIndex];
                var extra = 0;
                foreach (var p in extraProbs) {
                    extra = (extra << 1) + d.GetBool(p);
                }
                absValue = Vp8Tables.CategoryBase[categoryIndex] + extra;
            }

            if (absValue != 0) {
                var sign = d.GetFlag();
                var value = sign != 0 ? -absValue : absValue;
                output[Vp8Tables.Zigzag[c]] = (short)(value * dqf[c == 0 ? 0 : 1]);
                lastNonZero = c;
            }

            ctx = absValue == 0 ? 0 : absValue == 1 ? 1 : 2;
            skipEobCheck = absValue == 0;
            c++;
        }

        var hasCoefficients = lastNonZero >= 0;
        left[leftSlot] = above[aboveSlot] = (byte)(hasCoefficients ? 1 : 0);
        return hasCoefficients;
    }

    private static void ReconstructMacroblock(Vp8FrameHeader header, Vp8MacroblockInfo mb, int mbX, int mbY, int mbCols, byte[] y, int yStride, byte[] u, byte[] v, int uvStride, ReadOnlySpan<short> coeffs, Span<short> residual)
    {
        if (mb.YMode == Vp8Tables.BPred) {
            for (var by = 0; by < 4; by++) {
                for (var bx = 0; bx < 4; bx++) {
                    var idx = (by * 4) + bx;
                    Vp8Predict.PredictSubblock(y, yStride, mbCols, mbX, mbY, bx, by, mb.BModes![idx]);
                    Vp8Transform.InverseDct(coeffs.Slice(idx * 16, 16), residual);
                    AddResidual(y, yStride, (mbY * 16) + (by * 4), (mbX * 16) + (bx * 4), residual);
                }
            }
        }
        else {
            Vp8Predict.PredictBlock(y, yStride, mbY * 16, mbX * 16, 16, mb.YMode);

            Span<short> y2 = stackalloc short[16];
            Span<short> y2Input = stackalloc short[16];
            for (var i = 0; i < 16; i++) {
                y2Input[i] = coeffs[(24 * 16) + i];
            }
            Vp8Transform.InverseWht(y2Input, y2);

            Span<short> block = stackalloc short[16];
            for (var i = 0; i < 16; i++) {
                var by = i / 4;
                var bx = i % 4;
                coeffs.Slice(i * 16, 16).CopyTo(block);
                block[0] = y2[i];
                Vp8Transform.InverseDct(block, residual);
                AddResidual(y, yStride, (mbY * 16) + (by * 4), (mbX * 16) + (bx * 4), residual);
            }
        }

        Vp8Predict.PredictBlock(u, uvStride, mbY * 8, mbX * 8, 8, mb.UvMode);
        Vp8Predict.PredictBlock(v, uvStride, mbY * 8, mbX * 8, 8, mb.UvMode);

        for (var i = 0; i < 4; i++) {
            var bx = i % 2;
            var by = i / 2;
            Vp8Transform.InverseDct(coeffs.Slice((16 + i) * 16, 16), residual);
            AddResidual(u, uvStride, (mbY * 8) + (by * 4), (mbX * 8) + (bx * 4), residual);
        }
        for (var i = 0; i < 4; i++) {
            var bx = i % 2;
            var by = i / 2;
            Vp8Transform.InverseDct(coeffs.Slice((20 + i) * 16, 16), residual);
            AddResidual(v, uvStride, (mbY * 8) + (by * 4), (mbX * 8) + (bx * 4), residual);
        }
    }

    private static void AddResidual(byte[] plane, int stride, int originRow, int originCol, ReadOnlySpan<short> residual)
    {
        for (var r = 0; r < 4; r++) {
            var rowOffset = ((originRow + r) * stride) + originCol;
            for (var c = 0; c < 4; c++) {
                var value = plane[rowOffset + c] + residual[(r * 4) + c];
                plane[rowOffset + c] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
            }
        }
    }

    private static void ApplyLoopFilter(Vp8FrameHeader header, Vp8MacroblockInfo[] mbInfos, int mbCols, int mbRows, byte[] y, int yStride, byte[] u, byte[] v, int uvStride)
    {
        if (header.LoopFilter.Level == 0) {
            return;
        }

        for (var mbY = 0; mbY < mbRows; mbY++) {
            for (var mbX = 0; mbX < mbCols; mbX++) {
                var mb = mbInfos[(mbY * mbCols) + mbX];
                var filterLevel = ComputeFilterLevel(header, mb);
                if (filterLevel == 0) {
                    continue;
                }
                var (interiorLimit, hevThreshold) = Vp8LoopFilter.DeriveThresholds(filterLevel, header.LoopFilter.Sharpness, true);
                var skipInner = mb.YMode != Vp8Tables.BPred && !mb.HasCoefficients;

                var mbEdgeLimit = ((filterLevel + 2) * 2) + interiorLimit;
                var subEdgeLimit = (filterLevel * 2) + interiorLimit;

                if (header.LoopFilter.Simple) {
                    if (mbX > 0) {
                        Vp8LoopFilter.FilterVerticalEdgeSimple(y, yStride, mbX * 16, mbY * 16, 16, mbEdgeLimit);
                    }
                    if (!skipInner) {
                        for (var i = 1; i < 4; i++) {
                            Vp8LoopFilter.FilterVerticalEdgeSimple(y, yStride, (mbX * 16) + (i * 4), mbY * 16, 16, subEdgeLimit);
                        }
                    }
                    if (mbY > 0) {
                        Vp8LoopFilter.FilterHorizontalEdgeSimple(y, yStride, mbX * 16, mbY * 16, 16, mbEdgeLimit);
                    }
                    if (!skipInner) {
                        for (var i = 1; i < 4; i++) {
                            Vp8LoopFilter.FilterHorizontalEdgeSimple(y, yStride, mbX * 16, (mbY * 16) + (i * 4), 16, subEdgeLimit);
                        }
                    }
                    continue;
                }

                if (mbX > 0) {
                    Vp8LoopFilter.FilterVerticalEdgeNormal(y, yStride, mbX * 16, mbY * 16, 16, hevThreshold, interiorLimit, mbEdgeLimit, isMbEdge: true);
                    Vp8LoopFilter.FilterVerticalEdgeNormal(u, uvStride, mbX * 8, mbY * 8, 8, hevThreshold, interiorLimit, mbEdgeLimit, isMbEdge: true);
                    Vp8LoopFilter.FilterVerticalEdgeNormal(v, uvStride, mbX * 8, mbY * 8, 8, hevThreshold, interiorLimit, mbEdgeLimit, isMbEdge: true);
                }
                if (!skipInner) {
                    for (var i = 1; i < 4; i++) {
                        Vp8LoopFilter.FilterVerticalEdgeNormal(y, yStride, (mbX * 16) + (i * 4), mbY * 16, 16, hevThreshold, interiorLimit, subEdgeLimit, isMbEdge: false);
                    }
                    Vp8LoopFilter.FilterVerticalEdgeNormal(u, uvStride, (mbX * 8) + 4, mbY * 8, 8, hevThreshold, interiorLimit, subEdgeLimit, isMbEdge: false);
                    Vp8LoopFilter.FilterVerticalEdgeNormal(v, uvStride, (mbX * 8) + 4, mbY * 8, 8, hevThreshold, interiorLimit, subEdgeLimit, isMbEdge: false);
                }

                if (mbY > 0) {
                    Vp8LoopFilter.FilterHorizontalEdgeNormal(y, yStride, mbX * 16, mbY * 16, 16, hevThreshold, interiorLimit, mbEdgeLimit, isMbEdge: true);
                    Vp8LoopFilter.FilterHorizontalEdgeNormal(u, uvStride, mbX * 8, mbY * 8, 8, hevThreshold, interiorLimit, mbEdgeLimit, isMbEdge: true);
                    Vp8LoopFilter.FilterHorizontalEdgeNormal(v, uvStride, mbX * 8, mbY * 8, 8, hevThreshold, interiorLimit, mbEdgeLimit, isMbEdge: true);
                }
                if (!skipInner) {
                    for (var i = 1; i < 4; i++) {
                        Vp8LoopFilter.FilterHorizontalEdgeNormal(y, yStride, mbX * 16, (mbY * 16) + (i * 4), 16, hevThreshold, interiorLimit, subEdgeLimit, isMbEdge: false);
                    }
                    Vp8LoopFilter.FilterHorizontalEdgeNormal(u, uvStride, mbX * 8, (mbY * 8) + 4, 8, hevThreshold, interiorLimit, subEdgeLimit, isMbEdge: false);
                    Vp8LoopFilter.FilterHorizontalEdgeNormal(v, uvStride, mbX * 8, (mbY * 8) + 4, 8, hevThreshold, interiorLimit, subEdgeLimit, isMbEdge: false);
                }
            }
        }
    }

    private static int ComputeFilterLevel(Vp8FrameHeader header, Vp8MacroblockInfo mb)
    {
        var level = header.LoopFilter.Level;
        if (header.Segment.Enabled) {
            level = header.Segment.AbsoluteValues
                ? header.Segment.LoopFilterLevel[mb.SegmentId]
                : level + header.Segment.LoopFilterLevel[mb.SegmentId];
        }
        level = Math.Clamp(level, 0, 63);

        if (header.LoopFilter.DeltaEnabled) {
            level += header.LoopFilter.RefDelta[0];
            if (mb.YMode == Vp8Tables.BPred) {
                level += header.LoopFilter.ModeDelta[0];
            }
            level = Math.Clamp(level, 0, 63);
        }
        return level;
    }
}
