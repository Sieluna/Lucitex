using System.Buffers.Binary;

namespace Lucitex.Webp.Lossy;

internal static class Vp8Encoder
{
    public static void Encode(Stream stream, ReadOnlySpan<uint> pixels, int width, int height, WebpEncoderOptions options, WebpMemory memory, WebpMetadata metadata)
    {
        var columns = (width + 15) / 16;
        var rows = (height + 15) / 16;
        var yStride = columns * 16;
        var uvStride = columns * 8;
        using var yBuffer = memory.Rent<byte>(checked(yStride * rows * 16));
        using var uBuffer = memory.Rent<byte>(checked(uvStride * rows * 8));
        using var vBuffer = memory.Rent<byte>(checked(uvStride * rows * 8));
        using var contexts = memory.Rent<byte>(columns * 9);
        contexts.Span.Clear();
        using var header = new Vp8BoolEncoder(memory);
        using var tokens = new Vp8BoolEncoder(memory);
        var quantizer = (127 * (100 - options.Quality) + 50) / 100;
        WriteHeader(header, quantizer);
        var frame = new Vp8FrameHeader();
        frame.Quant.YacQi = quantizer;
        var factors = frame.BuildDequantFactors()[0];
        Span<byte> source = stackalloc byte[384];
        Span<short> coefficients = stackalloc short[400];
        Span<short> quantized = stackalloc short[400];
        Span<short> dc = stackalloc short[16];
        Span<byte> left = stackalloc byte[9];
        for (var my = 0; my < rows; my++) {
            left.Clear();
            for (var mx = 0; mx < columns; mx++) {
                LoadMacroblock(pixels, width, height, mx, my, source);
                var yMode = Predict(source[..256], yBuffer.Span, yStride, mx * 16, my * 16, 16, options.Effort);
                var uvMode = PredictChroma(source[256..], uBuffer.Span, vBuffer.Span, uvStride, mx * 8, my * 8, options.Effort);
                WriteModes(header, yMode, uvMode);
                Transform(source[..256], 16, yBuffer.Span, yStride, mx * 16, my * 16, coefficients[..256]);
                for (var block = 0; block < 16; block++) {
                    dc[block] = coefficients[block * 16];
                    coefficients[block * 16] = 0;
                }
                Vp8ForwardTransform.Wht(dc, coefficients[384..]);
                Transform(source.Slice(256, 64), 8, uBuffer.Span, uvStride, mx * 8, my * 8, coefficients.Slice(256, 64));
                Transform(source[320..], 8, vBuffer.Span, uvStride, mx * 8, my * 8, coefficients.Slice(320, 64));
                for (var block = 0; block < 25; block++) {
                    var dequant = block == 24 ? factors.Y2 : block < 16 ? factors.Y1 : factors.Uv;
                    for (var c = 0; c < 16; c++) {
                        var offset = (block * 16) + c;
                        var value = coefficients[offset];
                        var step = dequant[c == 0 ? 0 : 1];
                        var level = (Math.Abs((int)value) + (step / 2)) / step;
                        level = Math.Min(level, 2047) * Math.Sign(value);
                        quantized[offset] = (short)level;
                        coefficients[offset] = (short)(level * step);
                    }
                }
                var above = contexts.Span.Slice(mx * 9, 9);
                Vp8TokenEncoder.Write(tokens, quantized[384..], 24, 1, left, above);
                for (var block = 0; block < 24; block++) {
                    Vp8TokenEncoder.Write(tokens, quantized.Slice(block * 16, 16), block, block < 16 ? 0 : 2, left, above);
                }
                Vp8Transform.InverseWht(coefficients[384..], dc);
                for (var block = 0; block < 16; block++) {
                    coefficients[block * 16] = dc[block];
                }
                Reconstruct(coefficients[..256], yBuffer.Span, yStride, mx * 16, my * 16, 16);
                Reconstruct(coefficients.Slice(256, 64), uBuffer.Span, uvStride, mx * 8, my * 8, 8);
                Reconstruct(coefficients.Slice(320, 64), vBuffer.Span, uvStride, mx * 8, my * 8, 8);
            }
        }
        header.Finish();
        tokens.Finish();
        if (header.Bytes.Length > 0x7ffff) {
            throw new NotSupportedException("VP8 mode partition exceeds its 19-bit size limit.");
        }
        var hasAlpha = false;
        foreach (var pixel in pixels) {
            hasAlpha |= (pixel >> 24) != 255;
        }
        var payloadSize = checked(10 + header.Bytes.Length + tokens.Bytes.Length);
        WebpContainer.WriteHeader(stream, payloadSize, width, height, hasAlpha, metadata, false, hasAlpha ? checked(width * height + 1) : 0);
        if (hasAlpha) {
            WebpContainer.WriteAlpha(stream, pixels);
            WebpContainer.WriteChunkHeader(stream, "VP8 "u8, payloadSize);
        }
        Span<byte> tag = stackalloc byte[10];
        var bits = (header.Bytes.Length << 5) | 16;
        tag[0] = (byte)bits;
        tag[1] = (byte)(bits >> 8);
        tag[2] = (byte)(bits >> 16);
        tag[3] = 0x9d;
        tag[4] = 1;
        tag[5] = 0x2a;
        BinaryPrimitives.WriteUInt16LittleEndian(tag[6..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(tag[8..], (ushort)height);
        stream.Write(tag);
        stream.Write(header.Bytes);
        stream.Write(tokens.Bytes);
        if ((payloadSize & 1) != 0) {
            stream.WriteByte(0);
        }
        WebpContainer.WriteTrailingMetadata(stream, metadata);
    }

    private static void WriteHeader(Vp8BoolEncoder writer, int quantizer)
    {
        writer.Literal(0, 4);
        writer.Literal(0, 6);
        writer.Literal(0, 3);
        writer.Put(0);
        writer.Literal(0, 2);
        writer.Literal(quantizer, 7);
        writer.Literal(0, 5);
        writer.Put(0);
        for (var type = 0; type < 4; type++) {
            for (var band = 0; band < 8; band++) {
                for (var context = 0; context < 3; context++) {
                    for (var node = 0; node < 11; node++) {
                        writer.Put(0, Vp8Tables.CoeffUpdateProbs[type, band, context, node]);
                    }
                }
            }
        }
        writer.Put(0);
    }

    private static void WriteModes(Vp8BoolEncoder writer, int y, int uv)
    {
        writer.Put(1, Vp8Tables.KfYModeProb[0]);
        writer.Put(y < 2 ? 0 : 1, Vp8Tables.KfYModeProb[1]);
        writer.Put(y & 1, Vp8Tables.KfYModeProb[y < 2 ? 2 : 3]);
        writer.Put(uv == 0 ? 0 : 1, Vp8Tables.KfUvModeProb[0]);
        if (uv != 0) {
            writer.Put(uv == 1 ? 0 : 1, Vp8Tables.KfUvModeProb[1]);
            if (uv > 1) {
                writer.Put(uv == 3 ? 1 : 0, Vp8Tables.KfUvModeProb[2]);
            }
        }
    }

    private static int Predict(ReadOnlySpan<byte> source, Span<byte> plane, int stride, int x, int y, int size, WebpCompressionEffort effort)
    {
        var bestMode = 0;
        var bestScore = long.MaxValue;
        var modeCount = effort == WebpCompressionEffort.Fast ? 1 : 4;
        for (var mode = 0; mode < modeCount; mode++) {
            Vp8Predict.PredictBlock(plane, stride, y, x, size, mode);
            var score = Score(source, plane, stride, x, y, size);
            if (score < bestScore) {
                bestScore = score;
                bestMode = mode;
            }
        }
        Vp8Predict.PredictBlock(plane, stride, y, x, size, bestMode);
        return bestMode;
    }

    private static int PredictChroma(ReadOnlySpan<byte> source, Span<byte> u, Span<byte> v, int stride, int x, int y, WebpCompressionEffort effort)
    {
        var bestMode = 0;
        var bestScore = long.MaxValue;
        var modeCount = effort == WebpCompressionEffort.Fast ? 1 : 4;
        for (var mode = 0; mode < modeCount; mode++) {
            Vp8Predict.PredictBlock(u, stride, y, x, 8, mode);
            Vp8Predict.PredictBlock(v, stride, y, x, 8, mode);
            var score = Score(source[..64], u, stride, x, y, 8) + Score(source[64..], v, stride, x, y, 8);
            if (score < bestScore) {
                bestScore = score;
                bestMode = mode;
            }
        }
        Vp8Predict.PredictBlock(u, stride, y, x, 8, bestMode);
        Vp8Predict.PredictBlock(v, stride, y, x, 8, bestMode);
        return bestMode;
    }

    private static long Score(ReadOnlySpan<byte> source, ReadOnlySpan<byte> plane, int stride, int x, int y, int size)
    {
        long score = 0;
        for (var row = 0; row < size; row++) {
            for (var column = 0; column < size; column++) {
                var difference = source[(row * size) + column] - plane[((y + row) * stride) + x + column];
                score += difference * difference;
            }
        }
        return score;
    }

    private static void Transform(ReadOnlySpan<byte> source, int size, ReadOnlySpan<byte> plane, int stride, int x, int y, Span<short> coefficients)
    {
        var index = 0;
        for (var row = 0; row < size; row += 4) {
            for (var column = 0; column < size; column += 4) {
                Vp8ForwardTransform.Dct(source[((row * size) + column)..], size, plane[(((y + row) * stride) + x + column)..], stride, coefficients.Slice(index, 16));
                index += 16;
            }
        }
    }

    private static void Reconstruct(ReadOnlySpan<short> coefficients, Span<byte> plane, int stride, int x, int y, int size)
    {
        Span<short> residual = stackalloc short[16];
        var index = 0;
        for (var row = 0; row < size; row += 4) {
            for (var column = 0; column < size; column += 4) {
                Vp8Transform.InverseDct(coefficients.Slice(index, 16), residual);
                index += 16;
                for (var r = 0; r < 4; r++) {
                    for (var c = 0; c < 4; c++) {
                        var offset = ((y + row + r) * stride) + x + column + c;
                        plane[offset] = (byte)Math.Clamp(plane[offset] + residual[(r * 4) + c], 0, 255);
                    }
                }
            }
        }
    }

    private static void LoadMacroblock(ReadOnlySpan<uint> pixels, int width, int height, int mx, int my, Span<byte> target)
    {
        for (var y = 0; y < 16; y++) {
            for (var x = 0; x < 16; x++) {
                var pixel = pixels[(Math.Min((my * 16) + y, height - 1) * width) + Math.Min((mx * 16) + x, width - 1)];
                var r = (int)((pixel >> 16) & 255);
                var g = (int)((pixel >> 8) & 255);
                var b = (int)(pixel & 255);
                target[(y * 16) + x] = (byte)(((16839 * r + 33059 * g + 6420 * b + 32768) >> 16) + 16);
            }
        }
        for (var y = 0; y < 8; y++) {
            for (var x = 0; x < 8; x++) {
                var r = 0;
                var g = 0;
                var b = 0;
                for (var dy = 0; dy < 2; dy++) {
                    for (var dx = 0; dx < 2; dx++) {
                        var pixel = pixels[(Math.Min((my * 16) + (y * 2) + dy, height - 1) * width) + Math.Min((mx * 16) + (x * 2) + dx, width - 1)];
                        r += (int)((pixel >> 16) & 255);
                        g += (int)((pixel >> 8) & 255);
                        b += (int)(pixel & 255);
                    }
                }
                target[256 + (y * 8) + x] = (byte)(((-9719 * r - 19081 * g + 28800 * b + 131072) >> 18) + 128);
                target[320 + (y * 8) + x] = (byte)(((28800 * r - 24116 * g - 4684 * b + 131072) >> 18) + 128);
            }
        }
    }
}
