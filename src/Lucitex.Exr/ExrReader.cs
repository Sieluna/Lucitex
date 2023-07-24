using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal sealed class ExrReader : IImageReader
{
    private sealed class PartState
    {
        public required ExrHeader Header { get; init; }

        public required bool IsTiled { get; init; }

        public required long DataMinX { get; init; }

        public required long DataMinY { get; init; }

        public required long Width { get; init; }

        public required long Height { get; init; }

        public required int RowStrideBytes { get; init; }

        public long[] ChunkOffsets { get; set; } = [];

        public byte[]? DecodedPixels { get; set; }
    }

    private readonly ImageAssetDescriptor _descriptor;
    private readonly List<PartState> _parts;
    private readonly Stream _stream;
    private readonly ExrBinaryReader _binaryReader;
    private readonly bool _isMultiPart;
    private readonly DecodeLimits _limits;

    public ExrReader(Stream stream, DecodeLimits limits)
    {
        _stream = stream;
        _limits = limits;
        _binaryReader = new ExrBinaryReader(stream);

        var flags = ExrHeaderReader.ReadFileVersion(_binaryReader, out _);
        _isMultiPart = flags.HasFlag(ExrVersionFlags.MultiPart);

        var headers = ExrHeaderReader.ReadHeaderList(_binaryReader, _isMultiPart);
        if (headers.Count == 0)
        {
            throw new ImageFormatException("exr", "MissingAttribute", "The file contains no parts.");
        }

        _descriptor = ExrDescriptorMapper.ToImageAssetDescriptor(headers);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0)
        {
            throw new ImageFormatException("exr", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _parts = headers.Select(BuildPartState).ToList();

        foreach (var part in _parts)
        {
            var chunkCount = ComputeChunkCount(part);
            var offsets = new long[chunkCount];
            for (var i = 0; i < chunkCount; i++)
            {
                offsets[i] = _binaryReader.ReadInt64();
            }

            part.ChunkOffsets = offsets;
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        var part = _parts[region.Subresource.Part];
        EnsureDecoded(part);

        if (region.Region.MinX != part.DataMinX || region.Region.MaxXExclusive != part.DataMinX + part.Width)
        {
            throw new NotSupportedException("Partial-row EXR reads are not supported yet.");
        }

        var startRow = (int)(region.Region.MinY - part.DataMinY);
        var rowCount = (int)region.Region.Height;
        var byteCount = rowCount * part.RowStrideBytes;

        part.DecodedPixels!.AsSpan(startRow * part.RowStrideBytes, byteCount).CopyTo(destination);
        return byteCount;
    }

    private static PartState BuildPartState(ExrHeader header) => new()
    {
        Header = header,
        IsTiled = header.Tiles is not null,
        DataMinX = header.DataWindow.XMin,
        DataMinY = header.DataWindow.YMin,
        Width = header.DataWindow.Width,
        Height = header.DataWindow.Height,
        RowStrideBytes = ComputeChannelOffsets(header.Channels, header.DataWindow.Width).RowStride,
    };

    private static int ComputeChunkCount(PartState part)
    {
        if (part.Header.ChunkCount is { } explicitCount)
        {
            return explicitCount;
        }

        if (part.IsTiled)
        {
            var tiles = part.Header.Tiles!.Value;
            var (tilesX, tilesY) = ExrTiling.TileGrid(part.Width, part.Height, tiles.XSize, tiles.YSize);
            return tilesX * tilesY;
        }

        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(part.Header.Compression);
        return (int)((part.Height + linesPerChunk - 1) / linesPerChunk);
    }

    private void EnsureDecoded(PartState part)
    {
        if (part.DecodedPixels is not null)
        {
            return;
        }

        if (part.Header.PartType is "deepscanline" or "deeptile")
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.DeepData", "Deep-data EXR parts are not supported yet.");
        }

        if (!ExrCompressor.IsSupported(part.Header.Compression))
        {
            throw new ImageFormatException(
                "exr",
                $"Unsupported.Exr.Compression.{part.Header.Compression}",
                $"EXR compression '{part.Header.Compression}' is not implemented.");
        }

        if (part.Header.Channels.Any(c => c.XSampling != 1 || c.YSampling != 1))
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.ChannelSubsampling", "Subsampled channels are not decodable yet.");
        }

        if (part.IsTiled && part.Header.Tiles!.Value.LevelMode != ExrTileLevelMode.OneLevel)
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.MipRipmapTiles", "Mipmapped/ripmapped tiled EXR files are not supported yet.");
        }

        var totalBytes = checked((long)part.RowStrideBytes * part.Height);
        if (totalBytes > _limits.MaxDecodedBytes)
        {
            throw new ImageFormatException(
                "exr",
                "LimitExceeded",
                $"Decoded size {totalBytes} exceeds MaxDecodedBytes limit of {_limits.MaxDecodedBytes}.");
        }

        var buffer = new byte[totalBytes];

        if (part.IsTiled)
        {
            DecodeTiles(part, buffer);
        }
        else
        {
            DecodeScanlines(part, buffer);
        }

        part.DecodedPixels = buffer;
    }

    private void DecodeScanlines(PartState part, byte[] buffer)
    {
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(part.Header.Compression);

        foreach (var offset in part.ChunkOffsets)
        {
            _stream.Position = offset;
            if (_isMultiPart)
            {
                _binaryReader.ReadInt32();
            }

            var y = _binaryReader.ReadInt32();
            var packedSize = _binaryReader.ReadInt32();
            var packed = _binaryReader.ReadBytes(packedSize);

            var rowInChunkStart = y - (int)part.DataMinY;
            var rowsInThisChunk = Math.Min(linesPerChunk, (int)part.Height - rowInChunkStart);
            var unpackedSize = part.RowStrideBytes * rowsInThisChunk;

            var destinationOffset = rowInChunkStart * part.RowStrideBytes;
            ExrCompressor.Decompress(part.Header.Compression, packed, buffer.AsSpan(destinationOffset, unpackedSize));
        }
    }

    private void DecodeTiles(PartState part, byte[] buffer)
    {
        var tiles = part.Header.Tiles!.Value;
        var fullImageOffsets = ComputeChannelOffsets(part.Header.Channels, part.Width);

        foreach (var offset in part.ChunkOffsets)
        {
            _stream.Position = offset;
            if (_isMultiPart)
            {
                _binaryReader.ReadInt32();
            }

            var dx = _binaryReader.ReadInt32();
            var dy = _binaryReader.ReadInt32();
            _binaryReader.ReadInt32();
            _binaryReader.ReadInt32();
            var packedSize = _binaryReader.ReadInt32();
            var packed = _binaryReader.ReadBytes(packedSize);

            var x0 = dx * (int)tiles.XSize;
            var y0 = dy * (int)tiles.YSize;
            var tileWidth = (int)Math.Min(tiles.XSize, part.Width - x0);
            var tileHeight = (int)Math.Min(tiles.YSize, part.Height - y0);

            var tileOffsets = ComputeChannelOffsets(part.Header.Channels, tileWidth);
            var unpacked = new byte[tileOffsets.RowStride * tileHeight];
            ExrCompressor.Decompress(part.Header.Compression, packed, unpacked);

            ScatterTileIntoImage(buffer, part.RowStrideBytes, unpacked, tileOffsets, fullImageOffsets, x0, y0, tileWidth, tileHeight, part.Header.Channels.Count);
        }
    }

    private static void ScatterTileIntoImage(
        byte[] imageBuffer,
        int imageRowStride,
        byte[] tileBuffer,
        ChannelOffsets tileOffsets,
        ChannelOffsets fullImageOffsets,
        int x0,
        int y0,
        int tileWidth,
        int tileHeight,
        int channelCount)
    {
        for (var row = 0; row < tileHeight; row++)
        {
            var tileRowBase = row * tileOffsets.RowStride;
            var imageRowBase = (y0 + row) * imageRowStride;

            for (var c = 0; c < channelCount; c++)
            {
                var bytesPerSample = tileOffsets.BytesPerSample[c];
                var length = tileWidth * bytesPerSample;

                var source = tileBuffer.AsSpan(tileRowBase + tileOffsets.Offsets[c], length);
                var destination = imageBuffer.AsSpan(imageRowBase + fullImageOffsets.Offsets[c] + (x0 * bytesPerSample), length);

                source.CopyTo(destination);
            }
        }
    }

    internal readonly record struct ChannelOffsets(int[] Offsets, int[] BytesPerSample, int RowStride);

    internal static ChannelOffsets ComputeChannelOffsets(IReadOnlyList<ExrChannelInfo> channels, long width)
    {
        var offsets = new int[channels.Count];
        var bytesPerSample = new int[channels.Count];
        var running = 0;

        for (var i = 0; i < channels.Count; i++)
        {
            offsets[i] = running;
            bytesPerSample[i] = channels[i].BytesPerSample;
            running += (int)width * channels[i].BytesPerSample;
        }

        return new ChannelOffsets(offsets, bytesPerSample, running);
    }
}
