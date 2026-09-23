using System.Buffers.Binary;
using System.IO.Compression;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Suites;

namespace Lucitex.Benchmarks.Formats;

internal sealed class PngModule : IFormatModule
{
    public string Id => "png";
    public int Channels => 4;
    public bool Lossless => true;
    public string DefaultProfile => "png-default";
    public IReadOnlyList<string> Profiles { get; } = ["png-default", "png-none", "png-paeth"];
    public IReadOnlyList<Type> BenchmarkTypes { get; } = [typeof(PngEncodeBenchmarks), typeof(PngDecodeBenchmarks)];
    public byte[] CreateFixture(TestImage source, ComparisonCase comparison) => new ImageSharpAdapter().Encode(source, comparison);

    public object ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference)
    {
        if (encoded.Length < 33 || !encoded.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !encoded.AsSpan(12, 4).SequenceEqual("IHDR"u8) || encoded[24] != 8 || encoded[25] != 6 || encoded[28] != 0) {
            throw new InvalidDataException("PNG profile requires non-interlaced RGBA8 output.");
        }
        if (comparison.Profile is "png-none" or "png-paeth") {
            ValidateFilters(encoded, comparison.Profile == "png-none" ? (byte)0 : (byte)4);
        }
        return new { Width = BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(16)),
            Height = BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(20)), BitDepth = 8, ColorType = "RGBA", Interlaced = false };
    }

    private static void ValidateFilters(byte[] encoded, byte expected)
    {
        using var compressed = new MemoryStream();
        for (var offset = 8; offset < encoded.Length;) {
            if (offset + 12 > encoded.Length) {
                throw new InvalidDataException("Truncated PNG chunk.");
            }
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(offset)));
            if (length > encoded.Length - offset - 12) {
                throw new InvalidDataException("Truncated PNG chunk payload.");
            }
            if (encoded.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8)) {
                compressed.Write(encoded.AsSpan(offset + 8, length));
            }
            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var rowBytes = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(16)) * 4);
        var height = BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(20));
        var row = new byte[rowBytes];
        for (var y = 0; y < height; y++) {
            var filter = zlib.ReadByte();
            if (filter != expected) {
                throw new InvalidDataException($"PNG row {y} uses filter {filter}; profile requires {expected}.");
            }
            zlib.ReadExactly(row);
        }
        if (zlib.ReadByte() != -1) {
            throw new InvalidDataException("Extra PNG scanline data.");
        }
    }
}
