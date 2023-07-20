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

        if (flags.HasFlag(ExrVersionFlags.Tiled))
        {
            throw new ImageFormatException("exr", "Unsupported.Exr.Tiled", "Tiled EXR files are not supported yet.");
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

        _width = header.DataWindow.Width;
        var height = header.DataWindow.Height;
        _dataMinX = header.DataWindow.XMin;
        _dataMinY = header.DataWindow.YMin;

        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(header.Compression);
        var chunkCount = header.ChunkCount ?? (int)((height + linesPerChunk - 1) / linesPerChunk);

        var offsets = new long[chunkCount];
        for (var i = 0; i < chunkCount; i++)
        {
            offsets[i] = binaryReader.ReadInt64();
        }

        _rowStrideBytes = header.Channels.Sum(c => (int)_width * c.BytesPerSample);

        var totalBytes = checked((long)_rowStrideBytes * height);
        if (totalBytes > limits.MaxDecodedBytes)
        {
            throw new ImageFormatException(
                "exr",
                "LimitExceeded",
                $"Decoded size {totalBytes} exceeds MaxDecodedBytes limit of {limits.MaxDecodedBytes}.");
        }

        _pixelBuffer = new byte[totalBytes];

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
}
