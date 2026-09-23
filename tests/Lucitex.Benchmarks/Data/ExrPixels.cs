using System.Buffers.Binary;

namespace Lucitex.Benchmarks.Data;

internal static class ExrPixels
{
    private static ReadOnlySpan<int> ChannelOrder => [3, 2, 1, 0];

    public static byte[] Pack(TestImage image)
    {
        var data = new byte[checked(image.Width * image.Height * 8)];
        for (var y = 0; y < image.Height; y++) {
            for (var channel = 0; channel < 4; channel++) {
                for (var x = 0; x < image.Width; x++) {
                    var value = image.Pixels[(y * image.Width + x) * 4 + ChannelOrder[channel]] / 255f;
                    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(((y * 4 + channel) * image.Width + x) * 2),
                        BitConverter.HalfToUInt16Bits((Half)value));
                }
            }
        }
        return data;
    }

    public static void Unpack(TestImage image, byte[] data, byte[] destination)
    {
        for (var y = 0; y < image.Height; y++) {
            for (var channel = 0; channel < 4; channel++) {
                var target = ChannelOrder[channel];
                if (target >= image.Channels) {
                    continue;
                }
                for (var x = 0; x < image.Width; x++) {
                    var bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(((y * 4 + channel) * image.Width + x) * 2));
                    var value = (float)BitConverter.UInt16BitsToHalf(bits);
                    if (!float.IsFinite(value) || value < 0 || value > 1) {
                        throw new InvalidDataException("EXR normalized conversion profile requires finite samples in [0,1].");
                    }
                    destination[(y * image.Width + x) * image.Channels + target] = (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
                }
            }
        }
    }
}
