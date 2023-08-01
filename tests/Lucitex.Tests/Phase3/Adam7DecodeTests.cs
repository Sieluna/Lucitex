using System.IO.Compression;
using Lucitex.Core.Execution;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Png;
using Lucitex.Png.Filtering;
using Lucitex.Png.Format;

namespace Lucitex.Tests.Phase3;

public class Adam7DecodeTests
{
    private static byte[] EncodeAsAdam7(PngIhdr ihdr, byte[] finalPixels)
    {
        var samplesPerPixel = ihdr.SamplesPerPixel;
        var bitDepth = ihdr.BitDepth;
        var finalRowBytes = ihdr.RowByteLength(ihdr.Width);

        using var inflated = new MemoryStream();

        for (var passIndex = 0; passIndex < 7; passIndex++)
        {
            var (passWidth, passHeight) = Adam7.PassDimensions(ihdr.Width, ihdr.Height, passIndex);
            if (passWidth == 0 || passHeight == 0)
            {
                continue;
            }

            var (xStart, yStart, xStep, yStep) = Adam7.Passes[passIndex];
            var passRowBytes = ihdr.RowByteLength(passWidth);

            for (var py = 0; py < passHeight; py++)
            {
                var y = yStart + (py * yStep);
                var finalRow = finalPixels.AsSpan(y * finalRowBytes, finalRowBytes);
                var passRow = new byte[passRowBytes];

                for (var px = 0; px < passWidth; px++)
                {
                    var x = xStart + (px * xStep);
                    for (var s = 0; s < samplesPerPixel; s++)
                    {
                        var value = PngBitPacking.ReadSample(finalRow, (x * samplesPerPixel) + s, bitDepth);
                        PngBitPacking.WriteSample(passRow, (px * samplesPerPixel) + s, bitDepth, value);
                    }
                }

                inflated.WriteByte((byte)PngFilterType.None);
                inflated.Write(passRow);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(inflated.ToArray());
        }

        return compressed.ToArray();
    }

    [Fact]
    public void Read_Adam7InterlacedTruecolorAlpha_ReconstructsOriginalImage()
    {
        var ihdr = new PngIhdr(13, 9, 8, PngColorType.TruecolorAlpha, PngInterlaceMethod.Adam7);
        var rowBytes = ihdr.RowByteLength(ihdr.Width);
        var finalPixels = new byte[rowBytes * ihdr.Height];
        new Random(2024).NextBytes(finalPixels);

        var compressedIdat = EncodeAsAdam7(ihdr, finalPixels);
        var document = new PngDocument { Ihdr = ihdr };

        using var stream = new MemoryStream();
        PngDocumentWriter.Write(stream, document, compressedIdat);
        stream.Position = 0;

        var codec = new PngCodec();
        var reader = codec.OpenReader(stream);
        var described = reader.Describe();

        Assert.Equal(13, described.Parts[0].Topology.BaseExtent.Width);
        Assert.Equal(9, described.Parts[0].Topology.BaseExtent.Height);

        var destination = new byte[finalPixels.Length];
        var region = new WorkRegion
        {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(ihdr.Width, ihdr.Height),
        };
        reader.Read(region, destination);

        Assert.Equal(finalPixels, destination);
    }

    [Fact]
    public void Read_Adam7InterlacedGrayscale1Bit_ReconstructsPackedBits()
    {
        var ihdr = new PngIhdr(16, 11, 1, PngColorType.Grayscale, PngInterlaceMethod.Adam7);
        var rowBytes = ihdr.RowByteLength(ihdr.Width);
        var finalPixels = new byte[rowBytes * ihdr.Height];
        new Random(99).NextBytes(finalPixels);

        var compressedIdat = EncodeAsAdam7(ihdr, finalPixels);
        var document = new PngDocument { Ihdr = ihdr };

        using var stream = new MemoryStream();
        PngDocumentWriter.Write(stream, document, compressedIdat);
        stream.Position = 0;

        var codec = new PngCodec();
        var reader = codec.OpenReader(stream);

        var destination = new byte[finalPixels.Length];
        var region = new WorkRegion
        {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = ImageBox.FromOrigin(ihdr.Width, ihdr.Height),
        };
        reader.Read(region, destination);

        Assert.Equal(finalPixels, destination);
    }
}
