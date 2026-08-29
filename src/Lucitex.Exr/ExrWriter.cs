using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal sealed class ExrWriter : IImageWriter
{
    private sealed class PartState
    {
        public required ExrHeader Header { get; init; }

        public required byte[] PixelBuffer { get; init; }

        public required ExrBlockLayout Layout { get; init; }

        public required long DataMinX { get; init; }

        public required long DataMinY { get; init; }

        public required long Width { get; init; }

        public required long Height { get; init; }
    }

    private readonly Stream _stream;
    private readonly List<PartState> _parts;
    private readonly bool _isMultiPart;
    private bool _finished;

    public ExrWriter(Stream stream, ImageAssetDescriptor descriptor, ExrCompressionId compression, ExrTileDesc? tiles)
    {
        if (!ExrCompressor.IsSupported(compression)) {
            throw new NotSupportedException($"EXR compression '{compression}' is not implemented for writing.");
        }

        if (tiles is { LevelMode: not ExrTileLevelMode.OneLevel }) {
            throw new NotSupportedException("Writing mipmapped/ripmapped tiled EXR files is not supported yet.");
        }

        if (tiles is not null && descriptor.Parts.Any(HasSubsampledChannel)) {
            throw new NotSupportedException("Tiled EXR parts must not use subsampled channels.");
        }

        _stream = stream;
        _isMultiPart = descriptor.Parts.Count > 1;

        _parts = descriptor.Parts
            .Select((part, index) => BuildPartState(part, index, compression, tiles, _isMultiPart))
            .ToList();
    }

    public WriterExecutionContract Contract { get; } = new() {
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
        var part = _parts[region.Subresource.Part];

        if (region.Region.MinX != part.DataMinX || region.Region.MaxXExclusive != part.DataMinX + part.Width) {
            throw new NotSupportedException("Partial-row EXR writes are not supported yet.");
        }

        var startRow = (int)(region.Region.MinY - part.DataMinY);
        var rowCount = (int)region.Region.Height;

        var start = checked((int)part.Layout.RowOffset(startRow));
        var byteCount = checked((int)part.Layout.RowOffset(startRow + rowCount)) - start;

        data[..byteCount].CopyTo(part.PixelBuffer.AsSpan(start, byteCount));
    }

    public void Finish()
    {
        if (_finished) {
            return;
        }

        using var headerBuffer = new MemoryStream();
        var headerWriter = new ExrBinaryWriter(headerBuffer);
        var flags = _isMultiPart ? ExrVersionFlags.MultiPart : (_parts[0].Header.Tiles is null ? ExrVersionFlags.None : ExrVersionFlags.Tiled);
        ExrHeaderWriter.WriteFileVersion(headerWriter, flags);

        foreach (var part in _parts) {
            ExrHeaderWriter.WriteHeader(headerWriter, part.Header);
        }

        if (_isMultiPart) {
            headerWriter.WriteByte(0);
        }

        var headerBytes = headerBuffer.ToArray();

        var perPartChunks = _parts.Select(BuildChunksForPart).ToList();

        var chunkDataStart = headerBytes.Length + perPartChunks.Sum(p => (long)p.Payloads.Length * 8);
        var running = chunkDataStart;
        var offsetTables = new long[_parts.Count][];

        for (var partIndex = 0; partIndex < _parts.Count; partIndex++) {
            var (_, _, payloads) = perPartChunks[partIndex];
            var offsets = new long[payloads.Length];
            var perChunkHeaderBytes = (_isMultiPart ? 4 : 0) + (_parts[partIndex].Header.Tiles is null ? 8 : 20);

            for (var i = 0; i < payloads.Length; i++) {
                offsets[i] = running;
                running += perChunkHeaderBytes + payloads[i].Length;
            }

            offsetTables[partIndex] = offsets;
        }

        _stream.Write(headerBytes);
        var outWriter = new ExrBinaryWriter(_stream);

        foreach (var offsets in offsetTables) {
            foreach (var offset in offsets) {
                outWriter.WriteInt64(offset);
            }
        }

        for (var partIndex = 0; partIndex < _parts.Count; partIndex++) {
            var (chunkYs, tileCoords, payloads) = perPartChunks[partIndex];
            var isTiled = _parts[partIndex].Header.Tiles is not null;

            for (var i = 0; i < payloads.Length; i++) {
                if (_isMultiPart) {
                    outWriter.WriteInt32(partIndex);
                }

                if (isTiled) {
                    outWriter.WriteInt32(tileCoords[i].Dx);
                    outWriter.WriteInt32(tileCoords[i].Dy);
                    outWriter.WriteInt32(0);
                    outWriter.WriteInt32(0);
                }
                else {
                    outWriter.WriteInt32(chunkYs[i]);
                }

                outWriter.WriteInt32(payloads[i].Length);
                outWriter.WriteBytes(payloads[i]);
            }
        }

        _finished = true;
    }

    private static PartState BuildPartState(
        ImagePartDescriptor part,
        int index,
        ExrCompressionId compression,
        ExrTileDesc? tiles,
        bool isMultiPart)
    {
        var header = ExrDescriptorMapper.ToExrHeader(part, compression) with { Tiles = tiles };

        if (isMultiPart) {
            header = header with {
                PartName = header.PartName ?? $"part{index}",
                PartType = tiles is null ? "scanlineimage" : "tiledimage",
            };
        }

        var width = header.DataWindow.Width;
        var height = header.DataWindow.Height;
        var layout = new ExrBlockLayout(
            header.Channels, header.DataWindow.XMin, header.DataWindow.XMax, header.DataWindow.YMin, header.DataWindow.YMax);

        return new PartState {
            Header = header,
            PixelBuffer = new byte[layout.TotalBytes],
            Layout = layout,
            DataMinX = header.DataWindow.XMin,
            DataMinY = header.DataWindow.YMin,
            Width = width,
            Height = height,
        };
    }

    private static (int[] ChunkYs, (int Dx, int Dy)[] TileCoords, byte[][] Payloads) BuildChunksForPart(PartState part)
    {
        return part.Header.Tiles is { } tiles
            ? BuildTiledChunks(part, tiles)
            : BuildScanlineChunks(part);
    }

    private static (int[] ChunkYs, (int Dx, int Dy)[] TileCoords, byte[][] Payloads) BuildScanlineChunks(PartState part)
    {
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(part.Header.Compression);
        var chunkCount = (int)((part.Height + linesPerChunk - 1) / linesPerChunk);

        var payloads = new byte[chunkCount][];
        var ys = new int[chunkCount];

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
            var rowStart = chunkIndex * linesPerChunk;
            var rowsInChunk = Math.Min(linesPerChunk, (int)part.Height - rowStart);
            var start = checked((int)part.Layout.RowOffset(rowStart));
            var rawSize = checked((int)part.Layout.RowOffset(rowStart + rowsInChunk)) - start;
            var raw = part.PixelBuffer.AsSpan(start, rawSize);

            payloads[chunkIndex] = ExrCompressor.Compress(part.Header.Compression, raw);
            ys[chunkIndex] = (int)part.DataMinY + rowStart;
        }

        return (ys, [], payloads);
    }

    private static (int[] ChunkYs, (int Dx, int Dy)[] TileCoords, byte[][] Payloads) BuildTiledChunks(PartState part, ExrTileDesc tiles)
    {
        var (tilesX, tilesY) = ExrTiling.TileGrid(part.Width, part.Height, tiles.XSize, tiles.YSize);
        var chunkCount = tilesX * tilesY;

        var payloads = new byte[chunkCount][];
        var coords = new (int Dx, int Dy)[chunkCount];

        var index = 0;
        for (var dy = 0; dy < tilesY; dy++) {
            var y0 = dy * (int)tiles.YSize;
            var tileHeight = (int)Math.Min(tiles.YSize, part.Height - y0);

            for (var dx = 0; dx < tilesX; dx++) {
                var x0 = dx * (int)tiles.XSize;
                var tileWidth = (int)Math.Min(tiles.XSize, part.Width - x0);

                var tileLayout = new ExrBlockLayout(part.Header.Channels, 0, tileWidth - 1, 0, tileHeight - 1);
                var tileBuffer = new byte[tileLayout.TotalBytes];

                GatherTileFromImage(part, tileBuffer, tileLayout, x0, y0, tileWidth, tileHeight);

                payloads[index] = ExrCompressor.Compress(part.Header.Compression, tileBuffer);
                coords[index] = (dx, dy);
                index++;
            }
        }

        return ([], coords, payloads);
    }

    private static void GatherTileFromImage(
        PartState part,
        byte[] tileBuffer,
        ExrBlockLayout tileLayout,
        int x0,
        int y0,
        int tileWidth,
        int tileHeight)
    {
        for (var row = 0; row < tileHeight; row++) {
            var tileRowBase = row * tileLayout.UniformRowBytes;
            var imageRowBase = (y0 + row) * part.Layout.UniformRowBytes;

            for (var c = 0; c < tileLayout.ChannelCount; c++) {
                var bytesPerSample = tileLayout.BytesPerSample(c);
                var length = tileWidth * bytesPerSample;

                var source = part.PixelBuffer.AsSpan(
                    imageRowBase + part.Layout.ChannelOffsetInRow(0, c) + (x0 * bytesPerSample), length);
                var destination = tileBuffer.AsSpan(tileRowBase + tileLayout.ChannelOffsetInRow(0, c), length);

                source.CopyTo(destination);
            }
        }
    }

    private static bool HasSubsampledChannel(ImagePartDescriptor part) =>
        part.Channels.Channels.Any(channel => channel.Sampling.Step.X != 1 || channel.Sampling.Step.Y != 1);
}
