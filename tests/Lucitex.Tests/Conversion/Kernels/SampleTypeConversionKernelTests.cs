using Lucitex.Conversion.Kernels;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Sampling;

namespace Lucitex.Tests.Conversion.Kernels;

public class SampleTypeConversionKernelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(257)]
    public void UNorm8ToFloat32_MatchesScalarReference_AtVectorAndScalarPathSizes(int count)
    {
        var source = new byte[count];
        new Random(1).NextBytes(source);

        var destination = new float[count];
        SampleTypeConversionKernel.ToFloat32(source, SampleType.UNorm8, SampleByteOrder.LittleEndian, destination);

        for (var i = 0; i < count; i++) {
            Assert.Equal(source[i] / 255f, destination[i], 0.0000001f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(257)]
    public void Float32ToUNorm8_RoundTripsExactly_AtVectorAndScalarPathSizes(int count)
    {
        var original = new byte[count];
        new Random(2).NextBytes(original);

        var asFloat = new float[count];
        SampleTypeConversionKernel.ToFloat32(original, SampleType.UNorm8, SampleByteOrder.LittleEndian, asFloat);

        var roundTripped = new byte[count];
        SampleTypeConversionKernel.FromFloat32(asFloat, SampleType.UNorm8, SampleByteOrder.LittleEndian, roundTripped);

        Assert.Equal(original, roundTripped);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(129)]
    public void UNorm16ToFloat32_MatchesScalarReference_AtVectorAndScalarPathSizes(int count)
    {
        var values = new ushort[count];
        var random = new Random(3);
        for (var i = 0; i < count; i++) {
            values[i] = (ushort)random.Next(ushort.MaxValue + 1);
        }

        var source = new byte[count * 2];
        for (var i = 0; i < count; i++) {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(i * 2, 2), values[i]);
        }

        var destination = new float[count];
        SampleTypeConversionKernel.ToFloat32(source, SampleType.UNorm16, SampleByteOrder.LittleEndian, destination);

        for (var i = 0; i < count; i++) {
            Assert.Equal(values[i] / 65535f, destination[i], 0.0000001f);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(129)]
    public void UNorm16ToFloat32_RespectsBigEndianByteOrder(int count)
    {
        var values = new ushort[count];
        var random = new Random(4);
        for (var i = 0; i < count; i++) {
            values[i] = (ushort)random.Next(ushort.MaxValue + 1);
        }

        var source = new byte[count * 2];
        for (var i = 0; i < count; i++) {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(source.AsSpan(i * 2, 2), values[i]);
        }

        var destination = new float[count];
        SampleTypeConversionKernel.ToFloat32(source, SampleType.UNorm16, SampleByteOrder.BigEndian, destination);

        for (var i = 0; i < count; i++) {
            Assert.Equal(values[i] / 65535f, destination[i], 0.0000001f);
        }
    }

    public static IEnumerable<object[]> HalfBitPatterns()
    {
        ushort[] specialCases =
        [
            0x0000, 0x8000, // +0, -0
            0x3C00, 0xBC00, // +1, -1
            0x7BFF, 0xFBFF, // max normal, -max normal
            0x0001, 0x8001, // smallest positive/negative subnormal
            0x03FF, 0x83FF, // largest subnormal
            0x0400, 0x8400, // smallest normal
            0x7C00, 0xFC00, // +Inf, -Inf
            0x7E00, 0xFE00, // NaN, NaN
        ];

        foreach (var bits in specialCases) {
            yield return [bits];
        }

        var seen = new HashSet<ushort>();
        var random = new Random(5);
        while (seen.Count < 500) {
            var bits = (ushort)random.Next(ushort.MaxValue + 1);
            if (seen.Add(bits)) {
                yield return [bits];
            }
        }
    }

    [Theory]
    [MemberData(nameof(HalfBitPatterns))]
    public void Half16ToFloat32_MatchesBuiltinHalfConversion(ushort bits)
    {
        Span<byte> source = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(source, bits);

        Span<float> destination = stackalloc float[1];
        SampleTypeConversionKernel.ToFloat32(source, SampleType.Float16, SampleByteOrder.LittleEndian, destination);

        var expected = (float)BitConverter.UInt16BitsToHalf(bits);

        if (float.IsNaN(expected)) {
            Assert.True(float.IsNaN(destination[0]));
        }
        else {
            Assert.Equal(expected, destination[0]);
        }
    }

    [Fact]
    public void Half16ToFloat32_VectorizedPathMatchesBuiltinHalfConversion_AcrossFullBuffer()
    {
        var random = new Random(6);
        const int count = 1024;
        var bits = new ushort[count];
        for (var i = 0; i < count; i++) {
            bits[i] = (ushort)random.Next(ushort.MaxValue + 1);
        }

        var source = new byte[count * 2];
        for (var i = 0; i < count; i++) {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(i * 2, 2), bits[i]);
        }

        var destination = new float[count];
        SampleTypeConversionKernel.ToFloat32(source, SampleType.Float16, SampleByteOrder.LittleEndian, destination);

        for (var i = 0; i < count; i++) {
            var expected = (float)BitConverter.UInt16BitsToHalf(bits[i]);
            if (float.IsNaN(expected)) {
                Assert.True(float.IsNaN(destination[i]));
            }
            else {
                Assert.Equal(expected, destination[i]);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void Float32ToHalf16ToFloat32_RoundTripsWithinHalfPrecision(int count)
    {
        var random = new Random(7);
        var original = new float[count];
        for (var i = 0; i < count; i++) {
            original[i] = (float)(random.NextDouble() * 4) - 2f;
        }

        var halfBytes = new byte[count * 2];
        SampleTypeConversionKernel.FromFloat32(original, SampleType.Float16, SampleByteOrder.LittleEndian, halfBytes);

        var roundTripped = new float[count];
        SampleTypeConversionKernel.ToFloat32(halfBytes, SampleType.Float16, SampleByteOrder.LittleEndian, roundTripped);

        for (var i = 0; i < count; i++) {
            var expected = (float)(Half)original[i];
            Assert.Equal(expected, roundTripped[i]);
        }
    }

    [Fact]
    public void Float32ToFloat32_IsPassthrough()
    {
        float[] values = [1f, -2.5f, 0f, float.PositiveInfinity, float.NaN];
        var bytes = new byte[values.Length * 4];
        SampleTypeConversionKernel.FromFloat32(values, SampleType.Float32, SampleByteOrder.LittleEndian, bytes);

        var roundTripped = new float[values.Length];
        SampleTypeConversionKernel.ToFloat32(bytes, SampleType.Float32, SampleByteOrder.LittleEndian, roundTripped);

        for (var i = 0; i < values.Length; i++) {
            if (float.IsNaN(values[i])) {
                Assert.True(float.IsNaN(roundTripped[i]));
            }
            else {
                Assert.Equal(values[i], roundTripped[i]);
            }
        }
    }
}
