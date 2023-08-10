using Lucitex.Dds.Format;

namespace Lucitex.Tests.Dds.Format;

public class DdsHeaderRoundTripTests
{
    private static DdsHeader RoundTrip(DdsHeader header)
    {
        using var stream = new MemoryStream();
        DdsHeaderWriter.Write(new DdsBinaryWriter(stream), header);
        stream.Position = 0;
        return DdsHeaderReader.Read(new DdsBinaryReader(stream));
    }

    [Fact]
    public void WriteThenRead_Simple2DTexture_PreservesAllFields()
    {
        var header = new DdsHeader {
            Width = 64,
            Height = 32,
            Depth = 1,
            MipMapCount = 1,
            ArraySize = 1,
            IsCubemap = false,
            Dimension = D3d10ResourceDimension.Texture2D,
            Format = DxgiFormat.R8G8B8A8Unorm,
        };

        var roundTripped = RoundTrip(header);

        Assert.Equal(header, roundTripped);
    }

    [Fact]
    public void WriteThenRead_MipmappedTexture_PreservesMipCount()
    {
        var header = new DdsHeader {
            Width = 64,
            Height = 64,
            Depth = 1,
            MipMapCount = 7,
            ArraySize = 1,
            IsCubemap = false,
            Dimension = D3d10ResourceDimension.Texture2D,
            Format = DxgiFormat.Bc1Unorm,
        };

        var roundTripped = RoundTrip(header);

        Assert.Equal(7u, roundTripped.MipMapCount);
        Assert.Equal(DxgiFormat.Bc1Unorm, roundTripped.Format);
    }

    [Fact]
    public void WriteThenRead_TextureArray_PreservesArraySize()
    {
        var header = new DdsHeader {
            Width = 32,
            Height = 32,
            Depth = 1,
            MipMapCount = 1,
            ArraySize = 8,
            IsCubemap = false,
            Dimension = D3d10ResourceDimension.Texture2D,
            Format = DxgiFormat.R8Unorm,
        };

        var roundTripped = RoundTrip(header);

        Assert.Equal(8u, roundTripped.ArraySize);
        Assert.False(roundTripped.IsCubemap);
    }

    [Fact]
    public void WriteThenRead_Cubemap_PreservesCubemapFlag()
    {
        var header = new DdsHeader {
            Width = 64,
            Height = 64,
            Depth = 1,
            MipMapCount = 1,
            ArraySize = 1,
            IsCubemap = true,
            Dimension = D3d10ResourceDimension.Texture2D,
            Format = DxgiFormat.Bc7Unorm,
        };

        var roundTripped = RoundTrip(header);

        Assert.True(roundTripped.IsCubemap);
        Assert.Equal(1u, roundTripped.ArraySize);
    }

    [Fact]
    public void WriteThenRead_CubemapArray_PreservesBoth()
    {
        var header = new DdsHeader {
            Width = 32,
            Height = 32,
            Depth = 1,
            MipMapCount = 1,
            ArraySize = 4,
            IsCubemap = true,
            Dimension = D3d10ResourceDimension.Texture2D,
            Format = DxgiFormat.R8G8B8A8Unorm,
        };

        var roundTripped = RoundTrip(header);

        Assert.True(roundTripped.IsCubemap);
        Assert.Equal(4u, roundTripped.ArraySize);
    }

    [Fact]
    public void WriteThenRead_Volume3D_PreservesDepth()
    {
        var header = new DdsHeader {
            Width = 16,
            Height = 16,
            Depth = 8,
            MipMapCount = 1,
            ArraySize = 1,
            IsCubemap = false,
            Dimension = D3d10ResourceDimension.Texture3D,
            Format = DxgiFormat.R8Unorm,
        };

        var roundTripped = RoundTrip(header);

        Assert.Equal(D3d10ResourceDimension.Texture3D, roundTripped.Dimension);
        Assert.Equal(8u, roundTripped.Depth);
    }

    [Fact]
    public void Read_LegacyDxt1FourCC_ResolvesToBc1()
    {
        using var stream = new MemoryStream();
        var writer = new DdsBinaryWriter(stream);

        writer.WriteUInt32(0x20534444);
        writer.WriteUInt32(124);
        writer.WriteUInt32(0x1 | 0x2 | 0x4 | 0x1000);
        writer.WriteUInt32(16);
        writer.WriteUInt32(16);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteBytes(new byte[44]);

        writer.WriteUInt32(32);
        writer.WriteUInt32(0x4);
        writer.WriteUInt32(0x31545844);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);

        writer.WriteUInt32(0x1000);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);
        writer.WriteUInt32(0);

        stream.Position = 0;
        var header = DdsHeaderReader.Read(new DdsBinaryReader(stream));

        Assert.Equal(DxgiFormat.Bc1Unorm, header.Format);
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        Assert.Throws<Lucitex.Core.Execution.ImageFormatException>(() => DdsHeaderReader.Read(new DdsBinaryReader(stream)));
    }
}
