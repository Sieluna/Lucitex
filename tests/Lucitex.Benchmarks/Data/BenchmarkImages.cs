using System.Buffers.Binary;

namespace Lucitex.Benchmarks.Data;

internal static class BenchmarkImages
{
    public const int Width = 1024;
    public const int Height = 1024;

    public static float[] Gradient(int count, int seed = 7)
    {
        var random = new Random(seed);
        var values = new float[count];
        for (var i = 0; i < count; i++) {
            var t = (float)i / count;
            values[i] = MathF.Abs(MathF.Sin(t * 37f) * 0.5f) + (float)(random.NextDouble() * 0.02);
        }

        return values;
    }

    public static float[] HighDynamicRange(int count, int seed = 11)
    {
        var random = new Random(seed);
        var values = new float[count];
        for (var i = 0; i < count; i++) {
            var t = (float)i / count;
            values[i] = MathF.Exp((t * 12f) - 6f) * (0.9f + ((float)random.NextDouble() * 0.2f));
        }

        return values;
    }

    public static byte[] Rgba8(int width, int height, int seed = 3)
    {
        var random = new Random(seed);
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var offset = ((y * width) + x) * 4;
                var flat = (x / 37) % 5 == 0;
                var noise = flat ? 0 : random.Next(0, 12);
                data[offset] = (byte)Math.Clamp((x * 255 / width) + noise, 0, 255);
                data[offset + 1] = (byte)Math.Clamp((y * 255 / height) + noise, 0, 255);
                data[offset + 2] = (byte)Math.Clamp(((x + y) * 255 / (width + height)) + noise, 0, 255);
                data[offset + 3] = 255;
            }
        }

        return data;
    }

    public static byte[] Rgb8(int width, int height, int seed = 5)
    {
        var rgba = Rgba8(width, height, seed);
        var data = new byte[width * height * 3];
        for (var pixel = 0; pixel < width * height; pixel++) {
            data[pixel * 3] = rgba[pixel * 4];
            data[(pixel * 3) + 1] = rgba[(pixel * 4) + 1];
            data[(pixel * 3) + 2] = rgba[(pixel * 4) + 2];
        }

        return data;
    }

    public static ushort[] HalfBits(int count, int seed = 13)
    {
        var source = HighDynamicRange(count, seed);
        var bits = new ushort[count];
        for (var i = 0; i < count; i++) {
            bits[i] = BitConverter.HalfToUInt16Bits((Half)source[i]);
        }

        return bits;
    }

    public static byte[] HalfBytes(int count, int seed = 13)
    {
        var bits = HalfBits(count, seed);
        var bytes = new byte[count * sizeof(ushort)];
        for (var i = 0; i < count; i++) {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * sizeof(ushort)), bits[i]);
        }

        return bytes;
    }

    public static byte[] UNorm16Bytes(int count, bool bigEndian, int seed = 17)
    {
        var random = new Random(seed);
        var bytes = new byte[count * sizeof(ushort)];
        for (var i = 0; i < count; i++) {
            var value = (ushort)Math.Clamp((int)((i * 65535L / count) + random.Next(0, 400)), 0, 65535);
            var slice = bytes.AsSpan(i * sizeof(ushort));
            if (bigEndian) {
                BinaryPrimitives.WriteUInt16BigEndian(slice, value);
            }
            else {
                BinaryPrimitives.WriteUInt16LittleEndian(slice, value);
            }
        }

        return bytes;
    }

    public static byte[] Rgbe(int count, int seed = 19)
    {
        var random = new Random(seed);
        var data = new byte[count * 4];
        for (var i = 0; i < count; i++) {
            data[i * 4] = (byte)random.Next(1, 256);
            data[(i * 4) + 1] = (byte)random.Next(1, 256);
            data[(i * 4) + 2] = (byte)random.Next(1, 256);
            data[(i * 4) + 3] = (byte)random.Next(100, 160);
        }

        return data;
    }
}
