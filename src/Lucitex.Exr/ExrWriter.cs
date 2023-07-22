using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal sealed class ExrWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly ExrHeader _header;
    private readonly byte[] _pixelBuffer;
    private readonly int _rowStrideBytes;
    private readonly long _dataMinX;
    private readonly long _dataMinY;
    private readonly long _width;
    private readonly long _height;
    private bool _finished;

    public ExrWriter(Stream stream, ImageAssetDescriptor descriptor, ExrCompressionId compression, ExrTileDesc? tiles)
    {
        if (!ExrCompressor.IsSupported(compression))
        {
            throw new NotSupportedException($"EXR compression '{compression}' is not implemented for writing.");
        }

        if (tiles is { LevelMode: not ExrTileLevelMode.OneLevel })
        {
            throw new NotSupportedException("Writing mipmapped/ripmapped tiled EXR files is not supported yet.");
        }

        _stream = stream;
        _header = ExrDescriptorMapper.ToExrHeader(descriptor, compression) with { Tiles = tiles };

        _width = _header.DataWindow.Width;
        _height = _header.DataWindow.Height;
        _dataMinX = _header.DataWindow.XMin;
        _dataMinY = _header.DataWindow.YMin;

        _rowStrideBytes = ExrReader.ComputeChannelOffsets(_header.Channels, _width).RowStride;
        _pixelBuffer = new byte[checked(_rowStrideBytes * _height)];
    }

    public WriterExecutionContract Contract { get; } = new()
    {
        RequiresDescriptorUpfront = true,
        RequiresDimensionsUpfront = true,
        WriteGranularity = new Extent3I(1, 1, 1),
        WriteOrder = WriteOrder.Arbitrary,
        RandomAccess = false,
        RequiresSeekableOutput = false,
        SupportsIncompleteLevels = false,
        SupportsSparseRegions = false,
    };

    public void Write(WorkRegion region, ReadOnlySpan<byte> data)
    {
        if (region.Region.MinX != _dataMinX || region.Region.MaxXExclusive != _dataMinX + _width)
        {
            throw new NotSupportedException("Partial-row EXR writes are not supported yet.");
        }

        var startRow = (int)(region.Region.MinY - _dataMinY);
        var rowCount = (int)region.Region.Height;
        var byteCount = rowCount * _rowStrideBytes;

        data[..byteCount].CopyTo(_pixelBuffer.AsSpan(startRow * _rowStrideBytes, byteCount));
    }

    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        using var headerBuffer = new MemoryStream();
        var headerWriter = new ExrBinaryWriter(headerBuffer);
        ExrHeaderWriter.WriteFileVersion(headerWriter, _header.Tiles is null ? ExrVersionFlags.None : ExrVersionFlags.Tiled);
        ExrHeaderWriter.WriteHeader(headerWriter, _header);
        var headerBytes = headerBuffer.ToArray();

        var (chunkYs, chunkTileCoords, chunkPayloads) = _header.Tiles is { } tiles
            ? BuildTiledChunks(tiles)
            : BuildScanlineChunks();

        var chunkCount = chunkPayloads.Length;
        var chunkDataStart = headerBytes.Length + (long)chunkCount * 8;
        var offsets = new long[chunkCount];
        var running = chunkDataStart;
        var perChunkHeaderBytes = _header.Tiles is null ? 8 : 20;

        for (var i = 0; i < chunkCount; i++)
        {
            offsets[i] = running;
            running += perChunkHeaderBytes + chunkPayloads[i].Length;
        }

        _stream.Write(headerBytes);

        var outWriter = new ExrBinaryWriter(_stream);
        foreach (var offset in offsets)
        {
            outWriter.WriteInt64(offset);
        }

        for (var i = 0; i < chunkCount; i++)
        {
            if (_header.Tiles is null)
            {
                outWriter.WriteInt32(chunkYs[i]);
            }
            else
            {
                var (dx, dy) = chunkTileCoords[i];
                outWriter.WriteInt32(dx);
                outWriter.WriteInt32(dy);
                outWriter.WriteInt32(0);
                outWriter.WriteInt32(0);
            }

            outWriter.WriteInt32(chunkPayloads[i].Length);
            outWriter.WriteBytes(chunkPayloads[i]);
        }

        _finished = true;
    }

    private (int[] ChunkYs, (int Dx, int Dy)[] TileCoords, byte[][] Payloads) BuildScanlineChunks()
    {
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(_header.Compression);
        var chunkCount = (int)((_height + linesPerChunk - 1) / linesPerChunk);

        var payloads = new byte[chunkCount][];
        var ys = new int[chunkCount];

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var rowStart = chunkIndex * linesPerChunk;
            var rowsInChunk = Math.Min(linesPerChunk, (int)_height - rowStart);
            var rawSize = _rowStrideBytes * rowsInChunk;
            var raw = _pixelBuffer.AsSpan(rowStart * _rowStrideBytes, rawSize);

            payloads[chunkIndex] = ExrCompressor.Compress(_header.Compression, raw);
            ys[chunkIndex] = (int)_dataMinY + rowStart;
        }

        return (ys, [], payloads);
    }

    private (int[] ChunkYs, (int Dx, int Dy)[] TileCoords, byte[][] Payloads) BuildTiledChunks(ExrTileDesc tiles)
    {
        var (tilesX, tilesY) = ExrTiling.TileGrid(_width, _height, tiles.XSize, tiles.YSize);
        var chunkCount = tilesX * tilesY;

        var payloads = new byte[chunkCount][];
        var coords = new (int Dx, int Dy)[chunkCount];
        var fullImageOffsets = ExrReader.ComputeChannelOffsets(_header.Channels, _width);

        var index = 0;
        for (var dy = 0; dy < tilesY; dy++)
        {
            var y0 = dy * (int)tiles.YSize;
            var tileHeight = (int)Math.Min(tiles.YSize, _height - y0);

            for (var dx = 0; dx < tilesX; dx++)
            {
                var x0 = dx * (int)tiles.XSize;
                var tileWidth = (int)Math.Min(tiles.XSize, _width - x0);

                var tileOffsets = ExrReader.ComputeChannelOffsets(_header.Channels, tileWidth);
                var tileBuffer = new byte[tileOffsets.RowStride * tileHeight];

                GatherTileFromImage(tileBuffer, tileOffsets, fullImageOffsets, x0, y0, tileWidth, tileHeight);

                payloads[index] = ExrCompressor.Compress(_header.Compression, tileBuffer);
                coords[index] = (dx, dy);
                index++;
            }
        }

        return ([], coords, payloads);
    }

    private void GatherTileFromImage(
        byte[] tileBuffer,
        ExrReader.ChannelOffsets tileOffsets,
        ExrReader.ChannelOffsets fullImageOffsets,
        int x0,
        int y0,
        int tileWidth,
        int tileHeight)
    {
        for (var row = 0; row < tileHeight; row++)
        {
            var tileRowBase = row * tileOffsets.RowStride;
            var imageRowBase = (y0 + row) * _rowStrideBytes;

            for (var c = 0; c < _header.Channels.Count; c++)
            {
                var bytesPerSample = tileOffsets.BytesPerSample[c];
                var length = tileWidth * bytesPerSample;

                var source = _pixelBuffer.AsSpan(imageRowBase + fullImageOffsets.Offsets[c] + (x0 * bytesPerSample), length);
                var destination = tileBuffer.AsSpan(tileRowBase + tileOffsets.Offsets[c], length);

                source.CopyTo(destination);
            }
        }
    }
}
