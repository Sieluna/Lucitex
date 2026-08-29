using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Topology;
using Lucitex.Exr.Compression;
using Lucitex.Exr.Format;

namespace Lucitex.Exr;

internal sealed class ExrReader : IImageReader
{
    private sealed class PartState
    {
        public required ExrHeader Header { get; init; }

        public required int PartIndex { get; init; }

        public required bool IsTiled { get; init; }

        public required long DataMinX { get; init; }

        public required long DataMinY { get; init; }

        public required long Width { get; init; }

        public required long Height { get; init; }

        public required IReadOnlyList<ExrTileLevel> Levels { get; init; }

        public required ExrBlockLayout[] LevelLayouts { get; init; }

        public required Dictionary<LevelKey, int> LevelIndices { get; init; }

        public long[] ChunkOffsets { get; set; } = [];

        public byte[][]? DecodedLevels { get; set; }

        public ExrBlockLayout BaseLayout => LevelLayouts[0];
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
        if (!stream.CanSeek) {
            throw new ArgumentException("OpenEXR reader requires a seekable stream.", nameof(stream));
        }

        _binaryReader = new ExrBinaryReader(stream, checked((int)Math.Min(limits.MaxWorkingSet, int.MaxValue)));

        var flags = ExrHeaderReader.ReadFileVersion(_binaryReader, out _);
        _isMultiPart = flags.HasFlag(ExrVersionFlags.MultiPart);

        var headers = ExrHeaderReader.ReadHeaderList(_binaryReader, _isMultiPart);
        if (headers.Count == 0) {
            throw new ImageFormatException("exr", "MissingAttribute", "The file contains no parts.");
        }

        ValidateHeaders(headers, limits);
        _descriptor = ExrDescriptorMapper.ToImageAssetDescriptor(headers);

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0) {
            throw new ImageFormatException("exr", "LimitExceeded", string.Join("; ", violations.Select(v => v.Message)));
        }

        _parts = headers.Select(BuildPartState).ToList();

        var chunkCounts = _parts.Select(ComputeChunkCount).ToArray();
        var totalChunkCount = chunkCounts.Aggregate(0L, (total, count) => checked(total + count));
        if (totalChunkCount > limits.MaxWorkingSet / sizeof(long)) {
            throw new ImageFormatException("exr", "LimitExceeded", "EXR chunk table exceeds MaxWorkingSet.");
        }

        for (var partIndex = 0; partIndex < _parts.Count; partIndex++) {
            var part = _parts[partIndex];
            var chunkCount = chunkCounts[partIndex];
            var offsets = new long[chunkCount];
            for (var i = 0; i < chunkCount; i++) {
                offsets[i] = _binaryReader.ReadInt64();
            }

            part.ChunkOffsets = offsets;
        }

        var chunkDataStart = stream.Position;
        foreach (var offset in _parts.SelectMany(part => part.ChunkOffsets)) {
            if (offset < chunkDataStart || offset > stream.Length - sizeof(int) * 2L) {
                throw new ImageFormatException("exr", "BadChunkOffset", $"EXR chunk offset {offset} is outside the chunk data range.");
            }
        }
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        var part = _parts[region.Subresource.Part];
        EnsureDecoded(part);

        if (!part.LevelIndices.TryGetValue(region.Subresource.Level, out var levelIndex)) {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                $"EXR part {region.Subresource.Part} has no resolution level {region.Subresource.Level}.");
        }

        var level = part.Levels[levelIndex];
        var layout = part.LevelLayouts[levelIndex];

        if (region.Region.MinX != part.DataMinX || region.Region.MaxXExclusive != part.DataMinX + level.Width) {
            throw new NotSupportedException("Partial-row EXR reads are not supported yet.");
        }

        var startRow = (int)(region.Region.MinY - part.DataMinY);
        var rowCount = (int)region.Region.Height;

        var start = layout.RowOffset(startRow);
        var byteCount = checked((int)(layout.RowOffset(startRow + rowCount) - start));

        part.DecodedLevels![levelIndex].AsSpan(checked((int)start), byteCount).CopyTo(destination);
        return byteCount;
    }

    private static PartState BuildPartState(ExrHeader header, int partIndex)
    {
        var width = header.DataWindow.Width;
        var height = header.DataWindow.Height;

        var levels = header.Tiles is { } tiles
            ? ExrTiling.Levels(tiles, width, height)
            : [new ExrTileLevel(0, 0, (int)width, (int)height, 0, 0)];

        var levelMode = header.Tiles?.LevelMode ?? ExrTileLevelMode.OneLevel;
        var layouts = new ExrBlockLayout[levels.Count];
        var indices = new Dictionary<LevelKey, int>();

        for (var i = 0; i < levels.Count; i++) {
            layouts[i] = new ExrBlockLayout(
                header.Channels,
                header.DataWindow.XMin,
                header.DataWindow.XMin + levels[i].Width - 1,
                header.DataWindow.YMin,
                header.DataWindow.YMin + levels[i].Height - 1);

            indices[ExrDescriptorMapper.ToLevelKey(levelMode, levels[i])] = i;
        }

        return new PartState {
            Header = header,
            PartIndex = partIndex,
            IsTiled = header.Tiles is not null,
            DataMinX = header.DataWindow.XMin,
            DataMinY = header.DataWindow.YMin,
            Width = width,
            Height = height,
            Levels = levels,
            LevelLayouts = layouts,
            LevelIndices = indices,
        };
    }

    private static int ComputeChunkCount(PartState part)
    {
        if (part.Header.ChunkCount is { } explicitCount) {
            if (explicitCount <= 0) {
                throw new ImageFormatException("exr", "BadChunkCount", $"EXR chunk count {explicitCount} must be positive.");
            }

            return explicitCount;
        }

        if (part.IsTiled) {
            return part.Levels.Aggregate(0, (total, level) => checked(total + level.TileCount));
        }

        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(part.Header.Compression);
        return checked((int)((part.Height + linesPerChunk - 1) / linesPerChunk));
    }

    private void EnsureDecoded(PartState part)
    {
        if (part.DecodedLevels is not null) {
            return;
        }

        if (part.Header.PartType is "deepscanline" or "deeptile") {
            throw new ImageFormatException("exr", "Unsupported.Exr.DeepData", "Deep-data EXR parts are not supported yet.");
        }

        if (!ExrCompressor.IsSupported(part.Header.Compression)) {
            throw new ImageFormatException(
                "exr",
                $"Unsupported.Exr.Compression.{part.Header.Compression}",
                $"EXR compression '{part.Header.Compression}' is not implemented.");
        }

        if (part.IsTiled && part.BaseLayout.HasSubsampling) {
            throw new ImageFormatException("exr", "BadChannel", "Tiled EXR parts must not use subsampled channels.");
        }

        var totalBytes = part.LevelLayouts.Aggregate(0L, (total, layout) => checked(total + layout.TotalBytes));
        if (totalBytes > _limits.MaxDecodedBytes) {
            throw new ImageFormatException(
                "exr",
                "LimitExceeded",
                $"Decoded size {totalBytes} exceeds MaxDecodedBytes limit of {_limits.MaxDecodedBytes}.");
        }

        var buffers = part.LevelLayouts.Select(layout => new byte[layout.TotalBytes]).ToArray();

        try {
            if (part.IsTiled) {
                DecodeTiles(part, buffers);
            }
            else {
                DecodeScanlines(part, buffers[0]);
            }
        }
        catch (Exception exception) when (ExrFormatErrors.IsMalformed(exception)) {
            throw ExrFormatErrors.Wrap(exception, _stream);
        }

        part.DecodedLevels = buffers;
    }

    private void DecodeScanlines(PartState part, byte[] buffer)
    {
        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(part.Header.Compression);
        var lastRow = checked((int)part.DataMinY + (int)part.Height - 1);

        for (var chunkIndex = 0; chunkIndex < part.ChunkOffsets.Length; chunkIndex++) {
            var offset = part.ChunkOffsets[chunkIndex];
            _stream.Position = offset;
            if (_isMultiPart) {
                ValidateChunkPart(part, _binaryReader.ReadInt32());
            }

            var y = _binaryReader.ReadInt32();
            var expectedY = checked((int)part.DataMinY + (chunkIndex * linesPerChunk));
            if (y != expectedY) {
                throw new ImageFormatException("exr", "BadChunkLeader", $"EXR scanline chunk {chunkIndex} declares y={y}; expected {expectedY}.");
            }

            var packedSize = _binaryReader.ReadInt32();
            var packed = _binaryReader.ReadBytes(packedSize);

            var chunkLayout = new ExrBlockLayout(
                part.Header.Channels,
                part.Header.DataWindow.XMin,
                part.Header.DataWindow.XMax,
                y,
                Math.Min(y + linesPerChunk - 1, lastRow));

            var destinationOffset = checked((int)part.BaseLayout.RowOffset(y - (int)part.DataMinY));
            var unpackedSize = checked((int)chunkLayout.TotalBytes);

            ExrCompressor.Decompress(part.Header.Compression, packed, buffer.AsSpan(destinationOffset, unpackedSize), chunkLayout);
        }
    }

    private void DecodeTiles(PartState part, byte[][] buffers)
    {
        var expectedChunks = EnumerateTileChunks(part).ToArray();
        if (expectedChunks.Length != part.ChunkOffsets.Length) {
            throw new ImageFormatException(
                "exr",
                "BadChunkCount",
                $"EXR tiled part declares {part.ChunkOffsets.Length} chunks; the tile grid requires {expectedChunks.Length}.");
        }

        for (var chunkIndex = 0; chunkIndex < part.ChunkOffsets.Length; chunkIndex++) {
            var expected = expectedChunks[chunkIndex];
            var level = part.Levels[expected.LevelIndex];

            _stream.Position = part.ChunkOffsets[chunkIndex];
            if (_isMultiPart) {
                ValidateChunkPart(part, _binaryReader.ReadInt32());
            }

            var dx = _binaryReader.ReadInt32();
            var dy = _binaryReader.ReadInt32();
            var levelX = _binaryReader.ReadInt32();
            var levelY = _binaryReader.ReadInt32();

            if (dx != expected.Dx || dy != expected.Dy || levelX != level.LevelX || levelY != level.LevelY) {
                throw new ImageFormatException(
                    "exr",
                    "BadChunkLeader",
                    $"EXR tile chunk {chunkIndex} declares ({dx},{dy},{levelX},{levelY}); " +
                    $"expected ({expected.Dx},{expected.Dy},{level.LevelX},{level.LevelY}).");
            }

            var packedSize = _binaryReader.ReadInt32();
            var packed = _binaryReader.ReadBytes(packedSize);

            var tiles = part.Header.Tiles!.Value;
            var x0 = dx * (int)tiles.XSize;
            var y0 = dy * (int)tiles.YSize;
            var tileWidth = (int)Math.Min(tiles.XSize, level.Width - x0);
            var tileHeight = (int)Math.Min(tiles.YSize, level.Height - y0);

            var tileLayout = new ExrBlockLayout(part.Header.Channels, 0, tileWidth - 1, 0, tileHeight - 1);
            var unpacked = new byte[tileLayout.TotalBytes];
            ExrCompressor.Decompress(part.Header.Compression, packed, unpacked, tileLayout);

            ScatterTileIntoImage(
                buffers[expected.LevelIndex],
                part.LevelLayouts[expected.LevelIndex],
                unpacked,
                tileLayout,
                x0,
                y0,
                tileWidth,
                tileHeight);
        }
    }

    private static IEnumerable<(int LevelIndex, int Dx, int Dy)> EnumerateTileChunks(PartState part)
    {
        for (var levelIndex = 0; levelIndex < part.Levels.Count; levelIndex++) {
            var level = part.Levels[levelIndex];
            for (var dy = 0; dy < level.TilesY; dy++) {
                for (var dx = 0; dx < level.TilesX; dx++) {
                    yield return (levelIndex, dx, dy);
                }
            }
        }
    }

    private static void ValidateChunkPart(PartState part, int actualPartIndex)
    {
        if (actualPartIndex != part.PartIndex) {
            throw new ImageFormatException(
                "exr",
                "BadChunkLeader",
                $"EXR chunk for part {part.PartIndex} declares part index {actualPartIndex}.");
        }
    }

    private static void ScatterTileIntoImage(
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

                var source = tileBuffer.AsSpan(tileRowBase + tileLayout.ChannelOffsetInRow(0, c), length);
                var destination = imageBuffer.AsSpan(
                    imageRowBase + imageLayout.ChannelOffsetInRow(0, c) + (x0 * bytesPerSample), length);

                source.CopyTo(destination);
            }
        }
    }

    private static void ValidateHeaders(IReadOnlyList<ExrHeader> headers, DecodeLimits limits)
    {
        if (headers.Count > limits.MaxParts) {
            throw new ImageFormatException("exr", "LimitExceeded", $"EXR contains {headers.Count} parts, exceeding MaxParts.");
        }

        foreach (var header in headers) {
            if (header.DataWindow.Width <= 0 || header.DataWindow.Height <= 0 ||
                header.DisplayWindow.Width <= 0 || header.DisplayWindow.Height <= 0) {
                throw new ImageFormatException("exr", "BadWindow", "EXR data and display windows must have positive extents.");
            }

            if (header.Channels.Count is 0 || header.Channels.Count > limits.MaxChannels) {
                throw new ImageFormatException("exr", "LimitExceeded", $"EXR channel count {header.Channels.Count} is outside the supported limits.");
            }

            if (!Enum.IsDefined(header.Compression) || !Enum.IsDefined(header.LineOrder)) {
                throw new ImageFormatException("exr", "BadAttribute", "EXR contains an unknown compression or line-order value.");
            }

            foreach (var channel in header.Channels) {
                if (!Enum.IsDefined(channel.PixelType) || channel.XSampling <= 0 || channel.YSampling <= 0) {
                    throw new ImageFormatException("exr", "BadChannel", $"EXR channel '{channel.Name}' has an invalid type or sampling rate.");
                }
            }

            if (header.Tiles is { } tiles &&
                (tiles.XSize == 0 || tiles.YSize == 0 || !Enum.IsDefined(tiles.LevelMode) || !Enum.IsDefined(tiles.RoundingMode))) {
                throw new ImageFormatException("exr", "BadTiles", "EXR tile description is invalid.");
            }
        }
    }
}
