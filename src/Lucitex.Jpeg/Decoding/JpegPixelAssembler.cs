using System.Numerics;
using System.Buffers.Binary;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;

namespace Lucitex.Jpeg.Decoding;

internal static class JpegPixelAssembler
{
    private const int k_ScaleBits = 16;
    private const int k_Half = 1 << (k_ScaleBits - 1);

    private static readonly int[] s_CrToR = BuildDirectTable(1.40200);
    private static readonly int[] s_CbToB = BuildDirectTable(1.77200);
    private static readonly int[] s_CrToG = BuildScaledTable(-0.71414);
    private static readonly int[] s_CbToG = BuildScaledTable(-0.34414);

    public static void Reconstruct(JpegDecoder decoder, byte[] output, bool interpolateChroma = true)
    {
        var planes = new byte[decoder.Frame.Components.Count][];
        var quantTables = new ushort[planes.Length][];
        var totalRows = 0;
        try {
            for (var c = 0; c < planes.Length; c++) {
                var component = decoder.Components[c];
                quantTables[c] = decoder.GetQuantTable(component.Component.QuantTableId).Values;
                planes[c] = ArrayPool<byte>.Shared.Rent(checked(component.SamplesPerLine * component.SamplesPerColumn));
                totalRows += component.BlocksPerColumn;
            }
            ForRows(totalRows, decoder.Frame.Width * 8, row => {
                var c = 0;
                while (row >= decoder.Components[c].BlocksPerColumn) {
                    row -= decoder.Components[c].BlocksPerColumn;
                    c++;
                }
                ReconstructBlockRow(decoder.Components[c], quantTables[c], planes[c], row);
            });
            Assemble(decoder, planes, output, interpolateChroma);
        }
        finally {
            foreach (var plane in planes) {
                if (plane is not null) {
                    ArrayPool<byte>.Shared.Return(plane);
                }
            }
        }
    }

    private static void Assemble(JpegDecoder decoder, byte[][] planes, byte[] output, bool interpolateChroma)
    {
        var frame = decoder.Frame;
        var componentCount = frame.Components.Count;
        var width = frame.Width;
        var height = frame.Height;
        var hMax = frame.HMax;
        var vMax = frame.VMax;
        var useColorTransform = componentCount == 3 && !decoder.AdobeTransformIsRaw;

        if (interpolateChroma && useColorTransform &&
            decoder.Components[0].Component.HSampling == hMax && decoder.Components[0].Component.VSampling == vMax &&
            IsChromaHalfSampled(decoder, hMax, vMax)) {
            ReconstructInterpolated(decoder, planes, output);
            return;
        }

        var xMaps = new int[componentCount][];
        for (var c = 0; c < componentCount; c++) {
            xMaps[c] = BuildAxisMap(width, decoder.Components[c].Component.HSampling, hMax, decoder.Components[c].SamplesPerLine);
        }

        if (componentCount == 1) {
            var plane = planes[0];
            var component = decoder.Components[0];
            var xMap = xMaps[0];
            ForRows(height, width, y => {
                var sampleY = Math.Min((y * component.Component.VSampling) / vMax, component.SamplesPerColumn - 1);
                var rowBase = sampleY * component.SamplesPerLine;
                var outRowBase = y * width;
                for (var x = 0; x < width; x++) {
                    output[outRowBase + x] = plane[rowBase + xMap[x]];
                }
            });

            return;
        }

        var plane0 = planes[0];
        var plane1 = planes[1];
        var plane2 = planes[2];
        var component0 = decoder.Components[0];
        var component1 = decoder.Components[1];
        var component2 = decoder.Components[2];
        var xMap0 = xMaps[0];
        var xMap1 = xMaps[1];
        var xMap2 = xMaps[2];

        ForRows(height, width, y => {
            var rowBase0 = Math.Min((y * component0.Component.VSampling) / vMax, component0.SamplesPerColumn - 1) * component0.SamplesPerLine;
            var rowBase1 = Math.Min((y * component1.Component.VSampling) / vMax, component1.SamplesPerColumn - 1) * component1.SamplesPerLine;
            var rowBase2 = Math.Min((y * component2.Component.VSampling) / vMax, component2.SamplesPerColumn - 1) * component2.SamplesPerLine;
            var outRowBase = y * width * 3;

            for (var x = 0; x < width; x++) {
                var sample0 = plane0[rowBase0 + xMap0[x]];
                var sample1 = plane1[rowBase1 + xMap1[x]];
                var sample2 = plane2[rowBase2 + xMap2[x]];
                var outIndex = outRowBase + (x * 3);

                if (useColorTransform) {
                    output[outIndex] = ClampByte(sample0 + s_CrToR[sample2]);
                    output[outIndex + 1] = ClampByte(sample0 + ((s_CrToG[sample2] + s_CbToG[sample1] + k_Half) >> k_ScaleBits));
                    output[outIndex + 2] = ClampByte(sample0 + s_CbToB[sample1]);
                }
                else {
                    output[outIndex] = sample0;
                    output[outIndex + 1] = sample1;
                    output[outIndex + 2] = sample2;
                }
            }
        });

    }

    private static bool IsChromaHalfSampled(JpegDecoder decoder, int hMax, int vMax)
    {
        for (var c = 1; c < decoder.Components.Count; c++) {
            var component = decoder.Components[c].Component;
            if (component.HSampling * 2 != hMax || (component.VSampling != vMax && component.VSampling * 2 != vMax)) {
                return false;
            }
        }

        return true;
    }

    private static int[] BuildAxisMap(int outputExtent, int componentSampling, int maxSampling, int samplesPerAxis)
    {
        var map = new int[outputExtent];
        for (var i = 0; i < outputExtent; i++) {
            map[i] = Math.Min((i * componentSampling) / maxSampling, samplesPerAxis - 1);
        }

        return map;
    }

    private static void ReconstructInterpolated(JpegDecoder decoder, byte[][] planes, byte[] output)
    {
        var width = decoder.Frame.Width;
        var height = decoder.Frame.Height;
        if ((long)width * height < 8192) {
            var rows = ArrayPool<byte>.Shared.Rent(width * 2);
            try {
                for (var y = 0; y < height; y++) {
                    ReconstructRow(y, rows);
                }
            }
            finally {
                ArrayPool<byte>.Shared.Return(rows);
            }
            return;
        }
        Parallel.For(0, height, () => ArrayPool<byte>.Shared.Rent(width * 2), (y, _, rows) => {
            ReconstructRow(y, rows);
            return rows;
        }, rows => ArrayPool<byte>.Shared.Return(rows));

        void ReconstructRow(int y, byte[] rows)
        {
            InterpolateRow(planes[1], decoder.Components[1], decoder.Frame.VMax, y, rows.AsSpan(0, width));
            InterpolateRow(planes[2], decoder.Components[2], decoder.Frame.VMax, y, rows.AsSpan(width, width));
            var offset = y * width;
            ConvertRow(planes[0].AsSpan(offset, width), rows.AsSpan(0, width), rows.AsSpan(width, width), output.AsSpan(offset * 3, width * 3));
        }
    }

    private static void ForRows(int rows, int pixelsPerRow, Action<int> action)
    {
        if ((long)rows * pixelsPerRow < 8192) {
            for (var row = 0; row < rows; row++) {
                action(row);
            }
        }
        else {
            Parallel.For(0, rows, action);
        }
    }

    private static void ConvertRow(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> output)
    {
        var x = 0;
        if (Avx2.IsSupported) {
            var rMask = Vector128.Create((byte)0, 128, 128, 1, 128, 128, 2, 128, 128, 3, 128, 128, 4, 128, 128, 5);
            var gMask = Vector128.Create((byte)128, 0, 128, 128, 1, 128, 128, 2, 128, 128, 3, 128, 128, 4, 128, 128);
            var bMask = Vector128.Create((byte)128, 128, 0, 128, 128, 1, 128, 128, 2, 128, 128, 3, 128, 128, 4, 128);
            var rTail = Vector128.Create((byte)128, 128, 6, 128, 128, 7, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128);
            var gTail = Vector128.Create((byte)5, 128, 128, 6, 128, 128, 7, 128, 128, 128, 128, 128, 128, 128, 128, 128);
            var bTail = Vector128.Create((byte)128, 5, 128, 128, 6, 128, 128, 7, 128, 128, 128, 128, 128, 128, 128, 128);
            for (; x <= luma.Length - 8; x += 8) {
                var yv = LoadEight(luma, x);
                var u = LoadEight(cb, x) - Vector256.Create(128);
                var v = LoadEight(cr, x) - Vector256.Create(128);
                var r = Pack(yv + ((v * 91881 + Vector256.Create(k_Half)) >> 16));
                var g = Pack(yv + ((v * -46802 + u * -22554 + Vector256.Create(k_Half)) >> 16));
                var b = Pack(yv + ((u * 116130 + Vector256.Create(k_Half)) >> 16));
                var first = Ssse3.Shuffle(r, rMask) | Ssse3.Shuffle(g, gMask) | Ssse3.Shuffle(b, bMask);
                var last = Ssse3.Shuffle(r, rTail) | Ssse3.Shuffle(g, gTail) | Ssse3.Shuffle(b, bTail);
                first.CopyTo(output.Slice(x * 3, 16));
                BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(x * 3 + 16, 8), last.AsUInt64().ToScalar());
            }
        }
        for (; x < luma.Length; x++) {
            var sample = luma[x];
            output[x * 3] = ClampByte(sample + s_CrToR[cr[x]]);
            output[x * 3 + 1] = ClampByte(sample + ((s_CrToG[cr[x]] + s_CbToG[cb[x]] + k_Half) >> k_ScaleBits));
            output[x * 3 + 2] = ClampByte(sample + s_CbToB[cb[x]]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> LoadEight(ReadOnlySpan<byte> source, int offset)
    {
        var bytes = Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(source), offset))).AsByte();
        return Avx2.ConvertToVector256Int32(bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Pack(Vector256<int> values)
    {
        values = Vector256.Clamp(values, Vector256<int>.Zero, Vector256.Create(255));
        return Vector128.Narrow(Vector256.Narrow(values, Vector256<int>.Zero).GetLower(), Vector128<short>.Zero).AsByte();
    }

    private static void InterpolateRow(byte[] plane, JpegComponentState component, int vMax, int y, Span<byte> output)
    {
        var width = component.SamplesPerLine;
        var halfHeight = component.Component.VSampling * 2 == vMax;
        var currentY = halfHeight ? y / 2 : y;
        var otherY = halfHeight ? Math.Clamp(currentY + ((y & 1) == 0 ? -1 : 1), 0, component.SamplesPerColumn - 1) : currentY;
        var current = plane.AsSpan(currentY * width, width);
        var other = plane.AsSpan(otherY * width, width);
        var center = current[0] * 3 + other[0];
        var left = center;
        var x = 0;
        if (Ssse3.IsSupported) {
            var interleave = Vector128.Create((byte)0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7, 15);
            for (; x + 8 < width; x += 8) {
                var values = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref current[x])).AsByte()).AsInt16();
                var neighbors = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(ref other[x])).AsByte()).AsInt16();
                var centers = values + values + values + neighbors;
                var lefts = Sse2.ShiftLeftLogical128BitLane(centers.AsByte(), 2).AsInt16() | Vector128.CreateScalar((short)left);
                var next = current[x + 8] * 3 + other[x + 8];
                var rights = Sse2.ShiftRightLogical128BitLane(centers.AsByte(), 2).AsInt16().WithElement(7, (short)next);
                var triple = centers + centers + centers;
                var even = (triple + lefts + Vector128.Create((short)8)) >> 4;
                var odd = (triple + rights + Vector128.Create((short)8)) >> 4;
                var packed = Sse2.PackUnsignedSaturate(even, odd);
                Ssse3.Shuffle(packed, interleave).CopyTo(output.Slice(x * 2, 16));
                left = centers.GetElement(7);
            }
            center = current[x] * 3 + other[x];
        }
        for (; x < width; x++) {
            var right = x + 1 < width ? current[x + 1] * 3 + other[x + 1] : center;
            var target = x * 2;
            output[target] = (byte)((center * 3 + left + 8) >> 4);
            if (target + 1 < output.Length) {
                output[target + 1] = (byte)((center * 3 + right + 8) >> 4);
            }
            left = center;
            center = right;
        }
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static int[] BuildDirectTable(double scale)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++) {
            table[i] = (int)Math.Round(scale * (i - 128));
        }

        return table;
    }

    private static int[] BuildScaledTable(double scale)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++) {
            table[i] = (int)Math.Round(scale * (i - 128) * (1 << k_ScaleBits));
        }

        return table;
    }

    private static void ReconstructBlockRow(JpegComponentState component, ushort[] quantValues, byte[] plane, int blockRow)
    {
        var coefficients = component.Coefficients;
        var blocksPerLine = component.BlocksPerLine;
        var samplesPerLine = component.SamplesPerLine;
        var samplesPerColumn = component.SamplesPerColumn;

        Span<int> dequantized = stackalloc int[64];
        Span<float> spatial = stackalloc float[64];

        for (var blockCol = 0; blockCol < blocksPerLine; blockCol++) {
            var blockOffset = component.BlockOffset(blockRow, blockCol);
            var block = coefficients.AsSpan(blockOffset, 64);
            var originY = blockRow * 8;
            var originX = blockCol * 8;
            if (!block[1..].ContainsAnyExcept((short)0)) {
                var dc = (byte)Math.Clamp((int)(block[0] * quantValues[0] * 0.125f + 128.5f), 0, 255);
                for (var y = 0; y < Math.Min(8, samplesPerColumn - originY); y++) {
                    plane.AsSpan((originY + y) * samplesPerLine + originX, Math.Min(8, samplesPerLine - originX)).Fill(dc);
                }
                continue;
            }
            Dequantize(block, quantValues, dequantized);

            DctTransform.Inverse(dequantized, spatial);

            if (Vector256.IsHardwareAccelerated && originX + 8 <= samplesPerLine && originY + 8 <= samplesPerColumn) {
                for (var y = 0; y < 8; y++) {
                    var values = Vector256.ConvertToInt32(Vector256.Create(spatial.Slice(y * 8, 8)) + Vector256.Create(128.5f));
                    values = Vector256.Clamp(values, Vector256<int>.Zero, Vector256.Create(255));
                    var words = Vector256.Narrow(values, Vector256<int>.Zero).GetLower();
                    var bytes = Vector128.Narrow(words, Vector128<short>.Zero).AsUInt64().ToScalar();
                    BinaryPrimitives.WriteUInt64LittleEndian(plane.AsSpan((originY + y) * samplesPerLine + originX, 8), bytes);
                }
                continue;
            }
            for (var y = 0; y < 8; y++) {
                var sampleY = originY + y;
                if (sampleY >= samplesPerColumn) {
                    continue;
                }

                for (var x = 0; x < 8; x++) {
                    var sampleX = originX + x;
                    if (sampleX >= samplesPerLine) {
                        continue;
                    }

                    var value = spatial[(y * 8) + x] + 128.5f;
                    plane[(sampleY * samplesPerLine) + sampleX] = (byte)Math.Clamp((int)value, 0, 255);
                }
            }
        }
    }

    private static void Dequantize(ReadOnlySpan<short> coefficients, ushort[] quantValues, Span<int> dequantized)
    {
        var i = 0;
        var lanes = Vector<short>.Count;

        if (Vector.IsHardwareAccelerated) {
            for (; i + lanes <= 64; i += lanes) {
                var c = new Vector<short>(coefficients.Slice(i, lanes));
                var q = new Vector<ushort>(quantValues.AsSpan(i, lanes));

                Vector.Widen(c, out var cLo, out var cHi);
                Vector.Widen(q, out var qLo, out var qHi);

                (cLo * Vector.AsVectorInt32(qLo)).CopyTo(dequantized.Slice(i, lanes / 2));
                (cHi * Vector.AsVectorInt32(qHi)).CopyTo(dequantized.Slice(i + (lanes / 2), lanes / 2));
            }
        }

        for (; i < 64; i++) {
            dequantized[i] = coefficients[i] * quantValues[i];
        }
    }
}
