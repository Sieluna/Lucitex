using Lucitex.Conversion.Kernels;

namespace Lucitex.Tests.Conversion;

public class RgbeConversionKernelTests
{
    [Fact]
    public void KnownValue_RoundTripsExactly()
    {
        byte[] rgbe = [128, 64, 32, 129];
        Span<float> red = stackalloc float[1];
        Span<float> green = stackalloc float[1];
        Span<float> blue = stackalloc float[1];

        RgbeConversionKernel.Decode(rgbe, red, green, blue);

        Assert.Equal(1, red[0]);
        Assert.Equal(0.5f, green[0]);
        Assert.Equal(0.25f, blue[0]);
        Span<byte> encoded = stackalloc byte[4];
        RgbeConversionKernel.Encode(red, green, blue, encoded);
        Assert.True(encoded.SequenceEqual(rgbe));
    }

    [Fact]
    public void VectorLengthAndTail_RoundTripRepresentableValues()
    {
        const int pixelCount = 37;
        var source = new byte[pixelCount * 4];
        for (var pixel = 0; pixel < pixelCount; pixel++) {
            source[pixel * 4] = (byte)(128 + pixel);
            source[(pixel * 4) + 1] = (byte)(64 + pixel);
            source[(pixel * 4) + 2] = (byte)(32 + pixel);
            source[(pixel * 4) + 3] = (byte)(120 + (pixel % 16));
        }

        var red = new float[pixelCount];
        var green = new float[pixelCount];
        var blue = new float[pixelCount];
        RgbeConversionKernel.Decode(source, red, green, blue);
        var destination = new byte[source.Length];
        RgbeConversionKernel.Encode(red, green, blue, destination);

        Assert.Equal(source, destination);
    }
}
