using System.Buffers.Binary;
using System.IO.Compression;
using Lucitex.Benchmarks.Data;

namespace Lucitex.Benchmarks.Validation;

internal sealed record Ktx2Structure(int Width, int Height, uint Supercompression, int DataOffset, int DataLength);

internal static class Ktx2Reference
{
    private static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(TestImage image, bool compress)
    {
        var payload = image.Pixels;
        if (compress) {
            using var stream = new MemoryStream();
            using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, true)) {
                zlib.Write(payload);
            }
            payload = stream.ToArray();
        }
        var result = new byte[checked(200 + payload.Length)];
        Identifier.CopyTo(result);
        void UInt32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
        void UInt64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(offset), value);
        UInt32(12, 37);
        UInt32(16, 1);
        UInt32(20, (uint)image.Width);
        UInt32(24, (uint)image.Height);
        UInt32(36, 1);
        UInt32(40, 1);
        UInt32(44, compress ? 3u : 0u);
        UInt32(48, 104);
        UInt32(52, 92);
        UInt64(80, 200);
        UInt64(88, (ulong)payload.Length);
        UInt64(96, (ulong)image.Pixels.Length);
        Descriptor().CopyTo(result, 104);
        payload.CopyTo(result, 200);
        return result;
    }

    public static Ktx2Structure Inspect(byte[] encoded)
    {
        if (encoded.Length < 104 || !encoded.AsSpan(0, 12).SequenceEqual(Identifier)) {
            throw new InvalidDataException("Invalid KTX2 identifier/header.");
        }
        uint UInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(offset));
        ulong UInt64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(encoded.AsSpan(offset));
        var width = checked((int)UInt32(20));
        var height = checked((int)UInt32(24));
        if (UInt32(12) != 37 || UInt32(16) != 1 || width <= 0 || height <= 0 || UInt32(28) != 0
            || UInt32(32) != 0 || UInt32(36) != 1 || UInt32(40) != 1 || UInt32(44) is not (0 or 3)
            || UInt64(64) != 0 || UInt64(72) != 0 || UInt64(96) != checked((ulong)width * (ulong)height * 4)) {
            throw new InvalidDataException("KTX2 profile requires one 2D RGBA8 UNORM level, no layers/faces, None/Zlib supercompression.");
        }
        var dfd = checked((int)UInt32(48));
        var offset = checked((int)UInt64(80));
        var length = checked((int)UInt64(88));
        var kvdOffset = checked((int)UInt32(56));
        var kvdLength = checked((int)UInt32(60));
        if (dfd < 104 || UInt32(52) != 92 || dfd > encoded.Length - 92 || offset < dfd + 92
            || offset > encoded.Length || length != encoded.Length - offset
            || (kvdLength != 0 && (kvdOffset < dfd + 92 || kvdLength > offset - kvdOffset))
            || (UInt32(44) == 0 && (offset % 4 != 0 || (ulong)length != UInt64(96)))
            || !encoded.AsSpan(dfd, 92).SequenceEqual(Descriptor())) {
            throw new InvalidDataException("KTX2 index, payload size or RGBA8 descriptor is invalid.");
        }
        return new Ktx2Structure(width, height, UInt32(44), offset, length);
    }

    public static void Decode(TestImage layout, byte[] encoded, byte[] destination)
    {
        var info = Inspect(encoded);
        if (info.Width != layout.Width || info.Height != layout.Height) {
            throw new InvalidDataException("KTX2 dimensions differ from source.");
        }
        var pixels = new byte[checked(info.Width * info.Height * 4)];
        if (info.Supercompression == 0) {
            encoded.AsSpan(info.DataOffset, info.DataLength).CopyTo(pixels);
        }
        else {
            using var stream = new MemoryStream(encoded, info.DataOffset, info.DataLength, false);
            using var zlib = new ZLibStream(stream, CompressionMode.Decompress);
            zlib.ReadExactly(pixels);
            if (zlib.ReadByte() != -1) {
                throw new InvalidDataException("Extra KTX2 decompressed data.");
            }
        }
        (layout.Channels == 4 ? pixels : PixelLayout.ToRgb(pixels)).CopyTo(destination, 0);
    }

    private static byte[] Descriptor()
    {
        var descriptor = new byte[92];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 92);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(8), (88u << 16) | 2);
        descriptor[12] = 1;
        descriptor[13] = 1;
        descriptor[14] = 1;
        descriptor[20] = 4;
        for (var channel = 0; channel < 4; channel++) {
            var sample = descriptor.AsSpan(28 + channel * 16, 16);
            BinaryPrimitives.WriteUInt16LittleEndian(sample, (ushort)(channel * 8));
            sample[2] = 7;
            sample[3] = channel == 3 ? (byte)15 : (byte)channel;
            BinaryPrimitives.WriteUInt32LittleEndian(sample[12..], 255);
        }
        return descriptor;
    }
}
