using System.IO.Compression;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Png.Format;

namespace Lucitex.Png;

internal sealed class PngWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly PngDocument _document;
    private readonly byte[] _pixelBuffer;
    private readonly int _rowStrideBytes;
    private bool _finished;

    public PngWriter(Stream stream, ImageAssetDescriptor descriptor)
    {
        _stream = stream;
        _document = PngDescriptorMapper.ToPngDocument(descriptor.Parts[0]);
        _rowStrideBytes = _document.Ihdr.RowByteLength(_document.Ihdr.Width);
        _pixelBuffer = new byte[checked((long)_rowStrideBytes * _document.Ihdr.Height)];
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
        var width = _document.Ihdr.Width;

        if (region.Region.MinX != 0 || region.Region.MaxXExclusive != width)
        {
            throw new NotSupportedException("Partial-row PNG writes are not supported yet.");
        }

        var startRow = (int)region.Region.MinY;
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

        using var filtered = new MemoryStream();

        for (var y = 0; y < _document.Ihdr.Height; y++)
        {
            var row = _pixelBuffer.AsSpan(y * _rowStrideBytes, _rowStrideBytes);
            filtered.WriteByte((byte)PngFilterType.None);
            filtered.Write(row);
        }

        var compressed = Deflate(filtered.ToArray());
        PngDocumentWriter.Write(_stream, _document, compressed);

        _finished = true;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
