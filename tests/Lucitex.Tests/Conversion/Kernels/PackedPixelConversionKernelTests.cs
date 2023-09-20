using System.Runtime.InteropServices;
using Lucitex.Conversion.Kernels;
using Lucitex.Core.Representation;

namespace Lucitex.Tests.Conversion.Kernels;

public class PackedPixelConversionKernelTests
{
    [Theory]
    [InlineData("B5G6R5", 1)]
    [InlineData("B5G6R5", 257)]
    [InlineData("B5G5R5A1", 3)]
    [InlineData("B5G5R5A1", 259)]
    public void Bgr16_DecodeEncode_RoundTripsEveryField(string formatName, int count)
    {
        var format = formatName == "B5G6R5" ? EncodedFormatId.B5G6R5 : EncodedFormatId.B5G5R5A1;
        var words = new ushort[count];
        var bytes = MemoryMarshal.AsBytes(words.AsSpan());
        new Random(307).NextBytes(bytes);
        var red = new float[count];
        var green = new float[count];
        var blue = new float[count];
        var alpha = format == EncodedFormatId.B5G5R5A1 ? new float[count] : [];

        PackedPixelConversionKernel.Decode(format, bytes, red, green, blue, alpha);
        var roundTripped = new byte[bytes.Length];
        PackedPixelConversionKernel.Encode(format, red, green, blue, alpha, roundTripped);

        Assert.Equal(bytes.ToArray(), roundTripped);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(257)]
    public void R10G10B10A2_DecodeEncode_RoundTripsEveryField(int count)
    {
        var words = new uint[count];
        var bytes = MemoryMarshal.AsBytes(words.AsSpan());
        new Random(311).NextBytes(bytes);
        var red = new float[count];
        var green = new float[count];
        var blue = new float[count];
        var alpha = new float[count];

        PackedPixelConversionKernel.Decode(EncodedFormatId.R10G10B10A2, bytes, red, green, blue, alpha);
        var roundTripped = new byte[bytes.Length];
        PackedPixelConversionKernel.Encode(EncodedFormatId.R10G10B10A2, red, green, blue, alpha, roundTripped);

        Assert.Equal(bytes.ToArray(), roundTripped);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(259)]
    public void R11G11B10_Decode_MatchesHalfLayoutReference(int count)
    {
        var words = new uint[count];
        var bytes = MemoryMarshal.AsBytes(words.AsSpan());
        new Random(313).NextBytes(bytes);
        var red = new float[count];
        var green = new float[count];
        var blue = new float[count];

        PackedPixelConversionKernel.Decode(EncodedFormatId.R11G11B10Float, bytes, red, green, blue);

        for (var i = 0; i < count; i++) {
            AssertSameFloat(DecodeUnsignedFloat(words[i] & 0x7ffu, 6), red[i]);
            AssertSameFloat(DecodeUnsignedFloat((words[i] >> 11) & 0x7ffu, 6), green[i]);
            AssertSameFloat(DecodeUnsignedFloat(words[i] >> 22, 5), blue[i]);
        }
    }

    [Fact]
    public void R11G11B10_Encode_HandlesNormalsSubnormalsAndSpecialValues()
    {
        float[] red = [1f, MathF.ScaleB(1f, -20), float.PositiveInfinity, float.NaN, -1f];
        float[] green = [0.5f, MathF.ScaleB(1f, -19), 0f, 0f, 0f];
        float[] blue = [0.25f, MathF.ScaleB(1f, -18), 0f, 0f, 0f];
        var encoded = new byte[red.Length * 4];

        PackedPixelConversionKernel.Encode(EncodedFormatId.R11G11B10Float, red, green, blue, [], encoded);
        var words = MemoryMarshal.Cast<byte, uint>(encoded);

        Assert.Equal((15u << 6) | ((14u << 6) << 11) | ((13u << 5) << 22), words[0]);
        Assert.Equal(1u | (2u << 11) | (2u << 22), words[1]);
        Assert.Equal(31u << 6, words[2]);
        Assert.Equal((31u << 6) | 32u, words[3]);
        Assert.Equal(0u, words[4]);
    }

    [Fact]
    public void Rgb9E5_EncodeDecode_MatchesSharedExponentReference()
    {
        float[] red = [0f, 0.5f, 65408f, float.NaN];
        float[] green = [0f, 0.25f, 65408f, float.NaN];
        float[] blue = [0f, 0.125f, 65408f, float.NaN];
        var encoded = new byte[red.Length * 4];

        PackedPixelConversionKernel.Encode(EncodedFormatId.Rgb9E5, red, green, blue, [], encoded);
        var words = MemoryMarshal.Cast<byte, uint>(encoded);
        Assert.Equal(0u, words[0]);
        Assert.Equal(256u | (128u << 9) | (64u << 18) | (15u << 27), words[1]);
        Assert.Equal(511u | (511u << 9) | (511u << 18) | (31u << 27), words[2]);
        Assert.Equal(0u, words[3]);

        var actualRed = new float[4];
        var actualGreen = new float[4];
        var actualBlue = new float[4];
        PackedPixelConversionKernel.Decode(EncodedFormatId.Rgb9E5, encoded, actualRed, actualGreen, actualBlue);
        Assert.Equal([0f, 0.5f, 65408f, 0f], actualRed);
        Assert.Equal([0f, 0.25f, 65408f, 0f], actualGreen);
        Assert.Equal([0f, 0.125f, 65408f, 0f], actualBlue);
    }

    [Fact]
    public void BuffersWithIncompatibleLengths_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => PackedPixelConversionKernel.Decode(
            EncodedFormatId.R10G10B10A2, new byte[3], new float[1], new float[1], new float[1], new float[1]));
        Assert.Throws<ArgumentException>(() => PackedPixelConversionKernel.Encode(
            EncodedFormatId.R10G10B10A2, new float[1], new float[1], new float[1], [], new byte[4]));
    }

    private static float DecodeUnsignedFloat(uint value, int mantissaBits) =>
        (float)BitConverter.UInt16BitsToHalf((ushort)(value << (10 - mantissaBits)));

    private static void AssertSameFloat(float expected, float actual)
    {
        if (float.IsNaN(expected)) {
            Assert.True(float.IsNaN(actual));
        }
        else {
            Assert.Equal(expected, actual);
        }
    }
}
