using Lucitex.Webp.Lossless;

namespace Lucitex.Webp;

internal static class WebpAlpha
{
    public static byte[] Decode(ReadOnlySpan<byte> chunk, int width, int height, WebpMemory memory)
    {
        if (chunk.Length < 1) {
            throw new InvalidDataException("Empty WebP ALPH chunk.");
        }
        var header = chunk[0];
        var filtering = (header >> 2) & 3;
        var compression = header & 3;
        if (compression > 1 || (header >> 4) > 1) {
            throw new InvalidDataException("Invalid WebP ALPH compression, preprocessing or reserved bits.");
        }

        var pixelCount = checked(width * height);
        var alpha = new byte[pixelCount];
        var payload = chunk[1..];

        if (compression == 0) {
            if (payload.Length < pixelCount) {
                throw new InvalidDataException("Truncated raw WebP alpha data.");
            }
            payload[..pixelCount].CopyTo(alpha);
        }
        else {
            using var pixels = Vp8LDecoder.Decode(payload, width, height, memory);
            var span = pixels.Span;
            for (var i = 0; i < pixelCount; i++) {
                alpha[i] = (byte)(span[i] >> 8);
            }
        }

        ApplyFilter(alpha, width, height, filtering);
        return alpha;
    }

    private static void ApplyFilter(byte[] alpha, int width, int height, int filtering)
    {
        if (filtering == 0) {
            return;
        }

        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var index = (y * width) + x;
                int predictor;
                if (x == 0 && y == 0) {
                    predictor = 0;
                }
                else if (x == 0) {
                    predictor = alpha[index - width];
                }
                else if (y == 0) {
                    predictor = alpha[index - 1];
                }
                else {
                    predictor = filtering switch {
                        1 => alpha[index - 1],
                        2 => alpha[index - width],
                        _ => Clip(alpha[index - 1] + alpha[index - width] - alpha[index - width - 1]),
                    };
                }
                alpha[index] = (byte)((predictor + alpha[index]) & 0xFF);
            }
        }
    }

    private static int Clip(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}
