using System.Buffers.Binary;
using Lucitex.Core.Execution;
using Lucitex.Webp.Lossy;

namespace Lucitex.Webp;

internal sealed record WebpMetadata(byte[]? Icc = null, byte[]? Exif = null, byte[]? Xmp = null)
{
    public bool IsEmpty => Icc is null && Exif is null && Xmp is null;
}

internal sealed record WebpDocument(int Width, int Height, bool IsLossy, WebpBuffer<byte> Payload, WebpBuffer<byte>? AlphaPayload, WebpMetadata Metadata);

internal static class WebpContainer
{
    public static WebpDocument Read(Stream stream, DecodeLimits limits, WebpMemory memory)
    {
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WEBP"u8)) {
            throw new InvalidDataException("Invalid WebP RIFF signature.");
        }
        var remaining = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) - 4L;
        if (remaining < 8 || (remaining & 1) != 0) {
            throw new InvalidDataException("Invalid WebP RIFF size.");
        }
        WebpBuffer<byte>? payload = null;
        WebpBuffer<byte>? alphaPayload = null;
        var isLossy = false;
        byte[]? icc = null;
        byte[]? exif = null;
        byte[]? xmp = null;
        var width = 0;
        var height = 0;
        var canvasWidth = 0;
        var canvasHeight = 0;
        var flags = 0;
        var chunkCount = 0;
        long metadataBytes = 0;
        Span<byte> discard = stackalloc byte[8192];
        try {
            while (remaining > 0) {
                if (remaining < 8) {
                    throw new InvalidDataException("Truncated WebP chunk header.");
                }
                stream.ReadExactly(header[..8]);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
                var padded = (long)size + (size & 1);
                remaining -= 8;
                if (padded > remaining) {
                    throw new InvalidDataException("WebP chunk exceeds its RIFF container.");
                }
                var type = BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (type == FourCc("VP8X"u8)) {
                    if (chunkCount != 0 || size != 10) {
                        throw new InvalidDataException("Invalid WebP extended header.");
                    }
                    stream.ReadExactly(header[..10]);
                    flags = header[0];
                    if ((flags & 2) != 0) {
                        throw new NotSupportedException("Animated WebP is not supported.");
                    }
                    canvasWidth = Read24(header[4..]) + 1;
                    canvasHeight = Read24(header[7..]) + 1;
                }
                else if (type == FourCc("VP8L"u8)) {
                    if (payload is not null || size <= 5) {
                        throw new InvalidDataException("Invalid or duplicate WebP lossless payload.");
                    }
                    stream.ReadExactly(header[..5]);
                    var dimensions = BinaryPrimitives.ReadUInt32LittleEndian(header[1..]);
                    if (header[0] != 0x2f || dimensions >> 29 != 0) {
                        throw new InvalidDataException("Invalid VP8L signature or version.");
                    }
                    width = (int)(dimensions & 16383) + 1;
                    height = (int)((dimensions >> 14) & 16383) + 1;
                    var decodedBytes = (long)width * height * 4;
                    if (width > limits.MaxDimensions || height > limits.MaxDimensions || (long)width * height > limits.MaxPixels || decodedBytes > limits.MaxDecodedBytes || decodedBytes / (double)size > limits.MaxCompressionRatio) {
                        throw new ImageFormatException("webp", "LimitExceeded", "WebP dimensions, decoded size or compression ratio exceeds the configured limit.");
                    }
                    if (size - 5 > int.MaxValue) {
                        throw new ImageFormatException("webp", "LimitExceeded", "WebP payload is too large.");
                    }
                    payload = memory.Rent<byte>((int)size - 5);
                    stream.ReadExactly(payload.Span);
                }
                else if (type == FourCc("VP8 "u8)) {
                    if (payload is not null || size < 10) {
                        throw new InvalidDataException("Invalid or duplicate WebP lossy payload.");
                    }
                    if (size > int.MaxValue) {
                        throw new ImageFormatException("webp", "LimitExceeded", "WebP payload is too large.");
                    }
                    var chunk = memory.Rent<byte>((int)size);
                    stream.ReadExactly(chunk.Span);
                    var frameHeader = Vp8FrameHeader.ParseUncompressed(chunk.Span, out _, out _);
                    width = frameHeader.Width;
                    height = frameHeader.Height;
                    var decodedBytes = (long)width * height * 4;
                    if (width > limits.MaxDimensions || height > limits.MaxDimensions || (long)width * height > limits.MaxPixels || decodedBytes > limits.MaxDecodedBytes || decodedBytes / (double)size > limits.MaxCompressionRatio) {
                        chunk.Dispose();
                        throw new ImageFormatException("webp", "LimitExceeded", "WebP dimensions, decoded size or compression ratio exceeds the configured limit.");
                    }
                    payload = chunk;
                    isLossy = true;
                }
                else if (type == FourCc("ALPH"u8)) {
                    if (alphaPayload is not null || size < 1) {
                        throw new InvalidDataException("Invalid or duplicate WebP ALPH chunk.");
                    }
                    if (size > int.MaxValue) {
                        throw new ImageFormatException("webp", "LimitExceeded", "WebP alpha payload is too large.");
                    }
                    var chunk = memory.Rent<byte>((int)size);
                    stream.ReadExactly(chunk.Span);
                    alphaPayload = chunk;
                }
                else if (type == FourCc("ANIM"u8) || type == FourCc("ANMF"u8)) {
                    throw new NotSupportedException("Animated WebP is not supported.");
                }
                else {
                    metadataBytes += size;
                    if (metadataBytes > limits.MaxMetadataBytes) {
                        throw new ImageFormatException("webp", "LimitExceeded", "WebP metadata exceeds the configured limit.");
                    }
                    if (type == FourCc("ICCP"u8) || type == FourCc("EXIF"u8) || type == FourCc("XMP "u8)) {
                        if (canvasWidth == 0 || size > int.MaxValue ||
                            (type == FourCc("ICCP"u8) && (icc is not null || payload is not null)) ||
                            (type == FourCc("EXIF"u8) && exif is not null) || (type == FourCc("XMP "u8) && xmp is not null)) {
                            throw new InvalidDataException("Invalid WebP metadata chunk.");
                        }
                        memory.Reserve(size);
                        var bytes = new byte[(int)size];
                        stream.ReadExactly(bytes);
                        if (type == FourCc("ICCP"u8)) {
                            icc = bytes;
                        }
                        else if (type == FourCc("EXIF"u8)) {
                            exif = bytes;
                        }
                        else {
                            xmp = bytes;
                        }
                    }
                    else {
                        long left = size;
                        while (left > 0) {
                            var count = (int)Math.Min(discard.Length, left);
                            stream.ReadExactly(discard[..count]);
                            left -= count;
                        }
                    }
                }
                if ((size & 1) != 0 && stream.ReadByte() != 0) {
                    throw new InvalidDataException("Invalid or truncated WebP chunk padding.");
                }
                remaining -= padded;
                chunkCount++;
            }
            if (payload is null || (canvasWidth != 0 && (canvasWidth != width || canvasHeight != height)) ||
                ((flags & 32) != 0) != (icc is not null) || ((flags & 8) != 0) != (exif is not null) || ((flags & 4) != 0) != (xmp is not null) ||
                ((flags & 16) != 0) != (alphaPayload is not null) || (alphaPayload is not null && !isLossy)) {
                throw new InvalidDataException("WebP chunks do not match the container header.");
            }
            return new WebpDocument(width, height, isLossy, payload, alphaPayload, new WebpMetadata(icc, exif, xmp));
        }
        catch {
            payload?.Dispose();
            alphaPayload?.Dispose();
            throw;
        }
    }

    public static void WriteHeader(Stream stream, int payloadSize, int width, int height, bool alpha, WebpMetadata metadata)
    {
        var size = 4L + ChunkSize(payloadSize);
        if (!metadata.IsEmpty) {
            size += 18 + MetadataSize(metadata.Icc) + MetadataSize(metadata.Exif) + MetadataSize(metadata.Xmp);
        }
        Span<byte> header = stackalloc byte[12];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)size));
        "WEBP"u8.CopyTo(header[8..]);
        stream.Write(header);
        if (!metadata.IsEmpty) {
            WriteChunkHeader(stream, "VP8X"u8, 10);
            header[..10].Clear();
            header[0] = (byte)((alpha ? 16 : 0) | (metadata.Icc is null ? 0 : 32) | (metadata.Exif is null ? 0 : 8) | (metadata.Xmp is null ? 0 : 4));
            Write24(header[4..], width - 1);
            Write24(header[7..], height - 1);
            stream.Write(header[..10]);
            WriteMetadata(stream, "ICCP"u8, metadata.Icc);
        }
        WriteChunkHeader(stream, "VP8L"u8, payloadSize);
    }

    public static void WriteTrailingMetadata(Stream stream, WebpMetadata metadata)
    {
        WriteMetadata(stream, "EXIF"u8, metadata.Exif);
        WriteMetadata(stream, "XMP "u8, metadata.Xmp);
    }

    private static long ChunkSize(int size) => 8L + size + (size & 1);

    private static long MetadataSize(byte[]? bytes) => bytes is null ? 0 : ChunkSize(bytes.Length);

    private static void WriteMetadata(Stream stream, ReadOnlySpan<byte> type, byte[]? bytes)
    {
        if (bytes is null) {
            return;
        }
        WriteChunkHeader(stream, type, bytes.Length);
        stream.Write(bytes);
        if ((bytes.Length & 1) != 0) {
            stream.WriteByte(0);
        }
    }

    private static void WriteChunkHeader(Stream stream, ReadOnlySpan<byte> type, int size)
    {
        Span<byte> header = stackalloc byte[8];
        type.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)size);
        stream.Write(header);
    }

    private static uint FourCc(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadUInt32LittleEndian(value);

    private static int Read24(ReadOnlySpan<byte> value) => value[0] | (value[1] << 8) | (value[2] << 16);

    private static void Write24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
