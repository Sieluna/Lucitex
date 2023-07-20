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

    public ExrWriter(Stream stream, ImageAssetDescriptor descriptor, ExrCompressionId compression)
    {
        if (!ExrCompressor.IsSupported(compression))
        {
            throw new NotSupportedException($"EXR compression '{compression}' is not implemented for writing.");
        }

        _stream = stream;
        _header = ExrDescriptorMapper.ToExrHeader(descriptor, compression);

        _width = _header.DataWindow.Width;
        _height = _header.DataWindow.Height;
        _dataMinX = _header.DataWindow.XMin;
        _dataMinY = _header.DataWindow.YMin;

        _rowStrideBytes = _header.Channels.Sum(c => (int)_width * c.BytesPerSample);
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
        ExrHeaderWriter.WriteFileVersion(headerWriter, ExrVersionFlags.None);
        ExrHeaderWriter.WriteHeader(headerWriter, _header);
        var headerBytes = headerBuffer.ToArray();

        var linesPerChunk = ExrCompressor.NumScanlinesPerChunk(_header.Compression);
        var chunkCount = (int)((_height + linesPerChunk - 1) / linesPerChunk);

        var chunkPayloads = new byte[chunkCount][];
        var chunkYs = new int[chunkCount];

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var rowStart = chunkIndex * linesPerChunk;
            var rowsInChunk = Math.Min(linesPerChunk, (int)_height - rowStart);
            var rawSize = _rowStrideBytes * rowsInChunk;
            var raw = _pixelBuffer.AsSpan(rowStart * _rowStrideBytes, rawSize);

            chunkPayloads[chunkIndex] = ExrCompressor.Compress(_header.Compression, raw);
            chunkYs[chunkIndex] = (int)_dataMinY + rowStart;
        }

        var chunkDataStart = headerBytes.Length + (long)chunkCount * 8;
        var offsets = new long[chunkCount];
        var running = chunkDataStart;
        for (var i = 0; i < chunkCount; i++)
        {
            offsets[i] = running;
            running += 8 + chunkPayloads[i].Length;
        }

        _stream.Write(headerBytes);

        var outWriter = new ExrBinaryWriter(_stream);
        foreach (var offset in offsets)
        {
            outWriter.WriteInt64(offset);
        }

        for (var i = 0; i < chunkCount; i++)
        {
            outWriter.WriteInt32(chunkYs[i]);
            outWriter.WriteInt32(chunkPayloads[i].Length);
            outWriter.WriteBytes(chunkPayloads[i]);
        }

        _finished = true;
    }
}
