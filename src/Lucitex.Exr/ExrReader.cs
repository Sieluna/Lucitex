using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal sealed class ExrReader : IImageReader
{
    private readonly ImageAssetDescriptor _descriptor;
    private readonly byte[] _pixelBuffer;
    private readonly int _rowStrideBytes;
    private readonly long _dataMinX;
    private readonly long _dataMinY;
    private readonly long _width;

    public ExrReader(Stream stream, DecodeLimits limits)
    {
        var binaryReader = new ExrBinaryReader(stream);
        var flags = ExrHeaderReader.ReadFileVersion(binaryReader, out _);

        if (flags.HasFlag(ExrVersionFlags.MultiPart))
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.MultiPart", "Multipart EXR files are not supported yet.");
        }

        if (flags.HasFlag(ExrVersionFlags.NonImage))
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.DeepData", "Deep-data EXR files are not supported yet.");
        }

        var header = ExrHeaderReader.ReadHeader(binaryReader);
        _descriptor = ExrDescriptorMapper.ToImageAssetDescriptor(header);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0)
        {
            throw new ImageFormatException("exr", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        if (!ExrCompressor.IsSupported(header.Compression))
        {
            throw new ImageFormatException(
                "exr",
                $"Unsupported.Exr.Compression.{header.Compression}",
                $"EXR compression '{header.Compression}' is not implemented.");
        }

        if (header.Channels.Any(c => c.XSampling != 1 || c.YSampling != 1))
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.ChannelSubsampling", "Subsampled channels are not decodable yet.");
        }

        if (flags.HasFlag(ExrVersionFlags.Tiled))
        {
            if (header.Tiles is null)
            {
                throw new ImageFormatException("exr", "MissingAttribute", "Tiled header is missing the required 'tiles' attribute.");
            }

            if (header.Tiles.Value.LevelMode != ExrTileLevelMode.OneLevel)
            {
                throw new ImageFormatException("exr", "Unsupported.Exr.MipRipmapTiles", "Mipmapped/ripmapped tiled EXR files are not supported yet.");
            }
        }

        _width = header.DataWindow.Width;
        var height = header.DataWindow.Height;
        _dataMinX = header.DataWindow.XMin;
        _dataMinY = header.DataWindow.YMin;

        var channelOffsets = ComputeChannelOffsets(header.Channels, _width);
        _rowStrideBytes = channelOffsets.RowStride;

        var totalBytes = checked((long)_rowStrideBytes * height);
        if (totalBytes > limits.MaxDecodedBytes)
        {
            throw new ImageFormatException(
                "exr",
                "LimitExceeded",
                $"Decoded size {totalBytes} exceeds MaxDecodedBytes limit of {limits.MaxDecodedBytes}.");
        }

        _pixelBuffer = new byte[totalBytes];

        if (flags.HasFlag(ExrVersionFlags.Tiled))
        {
            ReadTiledOneLevel(stream, binaryReader, header, channelOffsets, height);
        }
        else
        {
            ReadScanlines(stream, binaryReader, header, height);
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        if (region.Region.MinX != _dataMinX || region.Region.MaxXExclusive != _dataMinX + _width)
        {
            throw new NotSupportedException("Partial-row EXR reads are not supported yet.");
        }

        var startRow = (int)(region.Region.MinY - _dataMinY);
        var rowCount = (int)region.Region.Height;
        var byteCount = rowCount * _rowStrideBytes;

        _pixelBuffer.AsSpan(startRow * _rowStrideBytes, byteCount).CopyTo(destination);
        return byteCount;
    }

    private void ReadScanlines(Stream stream, ExrBinaryReader binaryReader, ExrHeader header, long height)
    {
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(header.Compression);
        var chunkCount = header.ChunkCount ?? (int)((height + linesPerChunk - 1) / linesPerChunk);

        var offsets = new long[chunkCount];
        for (var i = 0; i < chunkCount; i++)
        {
            offsets[i] = binaryReader.ReadInt64();
        }

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            stream.Position = offsets[chunkIndex];
            var y = binaryReader.ReadInt32();
            var packedSize = binaryReader.ReadInt32();
            var packed = binaryReader.ReadBytes(packedSize);

            var rowInChunkStart = y - (int)_dataMinY;
            var rowsInThisChunk = Math.Min(linesPerChunk, (int)height - rowInChunkStart);
            var unpackedSize = _rowStrideBytes * rowsInThisChunk;

            var destinationOffset = rowInChunkStart * _rowStrideBytes;
            ExrCompressor.Decompress(header.Compression, packed, _pixelBuffer.AsSpan(destinationOffset, unpackedSize));
        }
    }

    private void ReadTiledOneLevel(Stream stream, ExrBinaryReader binaryReader, ExrHeader header, ChannelOffsets fullImageOffsets, long height)
    {
        var tiles = header.Tiles!.Value;
        var (tilesX, tilesY) = ExrTiling.TileGrid(_width, height, tiles.XSize, tiles.YSize);
        var chunkCount = header.ChunkCount ?? tilesX * tilesY;

        var offsets = new long[chunkCount];
        for (var i = 0; i < chunkCount; i++)
        {
            offsets[i] = binaryReader.ReadInt64();
        }

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            stream.Position = offsets[chunkIndex];
            var dx = binaryReader.ReadInt32();
            var dy = binaryReader.ReadInt32();
            binaryReader.ReadInt32();
            binaryReader.ReadInt32();
            var packedSize = binaryReader.ReadInt32();
            var packed = binaryReader.ReadBytes(packedSize);

            var x0 = dx * (int)tiles.XSize;
            var y0 = dy * (int)tiles.YSize;
            var tileWidth = (int)Math.Min(tiles.XSize, _width - x0);
            var tileHeight = (int)Math.Min(tiles.YSize, height - y0);

            var tileOffsets = ComputeChannelOffsets(header.Channels, tileWidth);
            var unpacked = new byte[tileOffsets.RowStride * tileHeight];
            ExrCompressor.Decompress(header.Compression, packed, unpacked);

            ScatterTileIntoImage(unpacked, tileOffsets, fullImageOffsets, x0, y0, tileWidth, tileHeight, header.Channels.Count);
        }
    }

    private void ScatterTileIntoImage(
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
            var imageRowBase = (y0 + row) * _rowStrideBytes;

            for (var c = 0; c < channelCount; c++)
            {
                var bytesPerSample = tileOffsets.BytesPerSample[c];
                var length = tileWidth * bytesPerSample;

                var source = tileBuffer.AsSpan(tileRowBase + tileOffsets.Offsets[c], length);
                var destination = _pixelBuffer.AsSpan(imageRowBase + fullImageOffsets.Offsets[c] + (x0 * bytesPerSample), length);

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
