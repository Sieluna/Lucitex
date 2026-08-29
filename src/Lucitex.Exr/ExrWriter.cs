using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal sealed class ExrWriter : IImageWriter
{
    private readonly record struct ExrChunk(int Y, int Dx, int Dy, int LevelX, int LevelY, byte[] Payload);

    private sealed class PartState
    {
        public required ExrHeader Header { get; init; }

        public required IReadOnlyList<ExrTileLevel> Levels { get; init; }

        public required ExrBlockLayout[] LevelLayouts { get; init; }

        public required byte[][] LevelBuffers { get; init; }

        public required Dictionary<LevelKey, int> LevelIndices { get; init; }

        public required long DataMinX { get; init; }

        public required long DataMinY { get; init; }

        public required long Width { get; init; }

        public required long Height { get; init; }

        public bool IsTiled => Header.Tiles is not null;
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

        if (!part.LevelIndices.TryGetValue(region.Subresource.Level, out var levelIndex)) {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                $"EXR part {region.Subresource.Part} has no resolution level {region.Subresource.Level}.");
        }

        var level = part.Levels[levelIndex];
        var layout = part.LevelLayouts[levelIndex];

        if (region.Region.MinX != part.DataMinX || region.Region.MaxXExclusive != part.DataMinX + level.Width) {
            throw new NotSupportedException("Partial-row EXR writes are not supported yet.");
        }

        var startRow = (int)(region.Region.MinY - part.DataMinY);
        var rowCount = (int)region.Region.Height;

        var start = checked((int)layout.RowOffset(startRow));
        var byteCount = checked((int)layout.RowOffset(startRow + rowCount)) - start;

        data[..byteCount].CopyTo(part.LevelBuffers[levelIndex].AsSpan(start, byteCount));
    }

    public void Finish()
    {
        if (_finished) {
            return;
        }

        using var headerBuffer = new MemoryStream();
        var headerWriter = new ExrBinaryWriter(headerBuffer);
        var flags = _isMultiPart ? ExrVersionFlags.MultiPart : (_parts[0].IsTiled ? ExrVersionFlags.Tiled : ExrVersionFlags.None);
        ExrHeaderWriter.WriteFileVersion(headerWriter, flags);

        foreach (var part in _parts) {
            ExrHeaderWriter.WriteHeader(headerWriter, part.Header);
        }

        if (_isMultiPart) {
            headerWriter.WriteByte(0);
        }

        var headerBytes = headerBuffer.ToArray();

        var perPartChunks = _parts.Select(BuildChunksForPart).ToList();

        var chunkDataStart = headerBytes.Length + perPartChunks.Sum(chunks => (long)chunks.Length * 8);
        var running = chunkDataStart;
        var offsetTables = new long[_parts.Count][];

        for (var partIndex = 0; partIndex < _parts.Count; partIndex++) {
            var chunks = perPartChunks[partIndex];
            var offsets = new long[chunks.Length];
            var perChunkHeaderBytes = (_isMultiPart ? 4 : 0) + (_parts[partIndex].IsTiled ? 20 : 8);

            for (var i = 0; i < chunks.Length; i++) {
                offsets[i] = running;
                running += perChunkHeaderBytes + chunks[i].Payload.Length;
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
            var isTiled = _parts[partIndex].IsTiled;

            foreach (var chunk in perPartChunks[partIndex]) {
                if (_isMultiPart) {
                    outWriter.WriteInt32(partIndex);
                }

                if (isTiled) {
                    outWriter.WriteInt32(chunk.Dx);
                    outWriter.WriteInt32(chunk.Dy);
                    outWriter.WriteInt32(chunk.LevelX);
                    outWriter.WriteInt32(chunk.LevelY);
                }
                else {
                    outWriter.WriteInt32(chunk.Y);
                }

                outWriter.WriteInt32(chunk.Payload.Length);
                outWriter.WriteBytes(chunk.Payload);
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

        var width = header.DataWindow.Width;
        var height = header.DataWindow.Height;

        var levels = tiles is { } tileDesc
            ? ExrTiling.Levels(tileDesc, width, height)
            : [new ExrTileLevel(0, 0, (int)width, (int)height, 0, 0)];

        if (isMultiPart) {
            header = header with {
                PartName = header.PartName ?? $"part{index}",
                PartType = tiles is null ? "scanlineimage" : "tiledimage",
                ChunkCount = CountChunks(levels, tiles, compression, height),
            };
        }

        var levelMode = tiles?.LevelMode ?? ExrTileLevelMode.OneLevel;
        var layouts = new ExrBlockLayout[levels.Count];
        var buffers = new byte[levels.Count][];
        var indices = new Dictionary<LevelKey, int>();

        for (var i = 0; i < levels.Count; i++) {
            layouts[i] = new ExrBlockLayout(
                header.Channels,
                header.DataWindow.XMin,
                header.DataWindow.XMin + levels[i].Width - 1,
                header.DataWindow.YMin,
                header.DataWindow.YMin + levels[i].Height - 1);

            buffers[i] = new byte[layouts[i].TotalBytes];
            indices[ExrDescriptorMapper.ToLevelKey(levelMode, levels[i])] = i;
        }

        return new PartState {
            Header = header,
            Levels = levels,
            LevelLayouts = layouts,
            LevelBuffers = buffers,
            LevelIndices = indices,
            DataMinX = header.DataWindow.XMin,
            DataMinY = header.DataWindow.YMin,
            Width = width,
            Height = height,
        };
    }

    private static int CountChunks(
        IReadOnlyList<ExrTileLevel> levels,
        ExrTileDesc? tiles,
        ExrCompressionId compression,
        long height)
    {
        if (tiles is not null) {
            return levels.Aggregate(0, (total, level) => checked(total + level.TileCount));
        }

        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(compression);
        return checked((int)((height + linesPerChunk - 1) / linesPerChunk));
    }

    private static ExrChunk[] BuildChunksForPart(PartState part) =>
        part.IsTiled ? BuildTiledChunks(part, part.Header.Tiles!.Value) : BuildScanlineChunks(part);

    private static ExrChunk[] BuildScanlineChunks(PartState part)
    {
        var layout = part.LevelLayouts[0];
        var buffer = part.LevelBuffers[0];
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(part.Header.Compression);
        var chunkCount = (int)((part.Height + linesPerChunk - 1) / linesPerChunk);

        var chunks = new ExrChunk[chunkCount];

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
            var rowStart = chunkIndex * linesPerChunk;
            var rowsInChunk = Math.Min(linesPerChunk, (int)part.Height - rowStart);
            var start = checked((int)layout.RowOffset(rowStart));
            var rawSize = checked((int)layout.RowOffset(rowStart + rowsInChunk)) - start;

            var chunkLayout = new ExrBlockLayout(
                part.Header.Channels,
                part.Header.DataWindow.XMin,
                part.Header.DataWindow.XMax,
                (int)part.DataMinY + rowStart,
                (int)part.DataMinY + rowStart + rowsInChunk - 1);

            var payload = ExrCompressor.Compress(part.Header.Compression, buffer.AsSpan(start, rawSize), chunkLayout);
            chunks[chunkIndex] = new ExrChunk((int)part.DataMinY + rowStart, 0, 0, 0, 0, payload);
        }

        return chunks;
    }

    private static ExrChunk[] BuildTiledChunks(PartState part, ExrTileDesc tiles)
    {
        var chunks = new List<ExrChunk>();

        for (var levelIndex = 0; levelIndex < part.Levels.Count; levelIndex++) {
            var level = part.Levels[levelIndex];
            var layout = part.LevelLayouts[levelIndex];
            var buffer = part.LevelBuffers[levelIndex];

            for (var dy = 0; dy < level.TilesY; dy++) {
                var y0 = dy * (int)tiles.YSize;
                var tileHeight = (int)Math.Min(tiles.YSize, level.Height - y0);

                for (var dx = 0; dx < level.TilesX; dx++) {
                    var x0 = dx * (int)tiles.XSize;
                    var tileWidth = (int)Math.Min(tiles.XSize, level.Width - x0);

                    var tileLayout = new ExrBlockLayout(part.Header.Channels, 0, tileWidth - 1, 0, tileHeight - 1);
                    var tileBuffer = new byte[tileLayout.TotalBytes];

                    GatherTileFromImage(buffer, layout, tileBuffer, tileLayout, x0, y0, tileWidth, tileHeight);

                    var payload = ExrCompressor.Compress(part.Header.Compression, tileBuffer, tileLayout);
                    chunks.Add(new ExrChunk(0, dx, dy, level.LevelX, level.LevelY, payload));
                }
            }
        }

        return chunks.ToArray();
    }

    private static void GatherTileFromImage(
        byte[] imageBuffer,
        ExrBlockLayout imageLayout,
        byte[] tileBuffer,
        ExrBlockLayout tileLayout,
        int x0,
        int y0,
        int tileWidth,
        int tileHeight)
    {
        for (var row = 0; row < tileHeight; row++) {
            var tileRowBase = row * tileLayout.UniformRowBytes;
            var imageRowBase = (y0 + row) * imageLayout.UniformRowBytes;

            for (var c = 0; c < tileLayout.ChannelCount; c++) {
                var bytesPerSample = tileLayout.BytesPerSample(c);
                var length = tileWidth * bytesPerSample;

                var source = imageBuffer.AsSpan(
                    imageRowBase + imageLayout.ChannelOffsetInRow(0, c) + (x0 * bytesPerSample), length);
                var destination = tileBuffer.AsSpan(tileRowBase + tileLayout.ChannelOffsetInRow(0, c), length);

                source.CopyTo(destination);
            }
        }
    }

    private static bool HasSubsampledChannel(ImagePartDescriptor part) =>
        part.Channels.Channels.Any(channel => channel.Sampling.Step.X != 1 || channel.Sampling.Step.Y != 1);
}
