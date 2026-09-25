using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Dds.Format;

namespace Lucitex.Dds;

internal sealed class DdsWriter : IAsyncImageWriter
{
    private readonly Stream _stream;
    private readonly DdsHeader _header;
    private readonly int _itemCount;
    private readonly long[] _levelByteSizes;
    private readonly byte[][] _levelBuffers;
    private bool _finished;
    private readonly DdsOperationState _state = new();

    public DdsWriter(Stream stream, ImageAssetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) {
            throw new ArgumentException("DDS writer requires a writable stream.", nameof(stream));
        }
        _stream = stream;
        var part = descriptor.Parts[0];
        _header = DdsDescriptorMapper.ToDdsHeader(part);

        _itemCount = checked((int)_header.ArraySize * (_header.IsCubemap ? 6 : 1));

        _levelByteSizes = new long[_header.MipMapCount];
        var w = (long)_header.Width;
        var h = (long)_header.Height;
        var d = _header.Dimension == D3d10ResourceDimension.Texture3D ? (long)_header.Depth : 1;

        for (var mip = 0; mip < _header.MipMapCount; mip++) {
            var sliceBytes = DdsFormatTable.SliceBytes(_header.Format, w, h);
            _levelByteSizes[mip] = checked(sliceBytes * d);

            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            d = Math.Max(1, d / 2);
        }

        _levelBuffers = new byte[_itemCount * _header.MipMapCount][];
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
        _state.Enter();
        try { WriteCore(region, data); }
        finally { _state.Exit(); }
    }

    public ValueTask WriteAsync(WorkRegion region, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(region, data.Span);
        return ValueTask.CompletedTask;
    }

    private void WriteCore(WorkRegion region, ReadOnlySpan<byte> data)
    {
        if (_finished) {
            throw new InvalidOperationException("The DDS writer has already finished.");
        }
        var subresource = region.Subresource;
        if (subresource.Part != 0 || (uint)subresource.ArrayElement >= _header.ArraySize ||
            (uint)subresource.Face >= (_header.IsCubemap ? 6u : 1u)) {
            throw new ArgumentOutOfRangeException(nameof(region), "DDS subresource is out of range.");
        }
        var item = ItemIndex(subresource.ArrayElement, subresource.Face);
        var mip = subresource.Level.X;

        if ((uint)mip >= _header.MipMapCount || subresource.Level != LevelKey.Mip(mip)) {
            throw new ArgumentOutOfRangeException(nameof(region), "DDS mip level is out of range.");
        }

        var width = Math.Max(1L, (long)_header.Width >> Math.Min(mip, 32));
        var height = Math.Max(1L, (long)_header.Height >> Math.Min(mip, 32));
        if (region.Region.MinX != 0 || region.Region.MinY != 0 ||
            region.Region.MaxXExclusive != width || region.Region.MaxYExclusive != height) {
            throw new NotSupportedException("Partial DDS subresource writes are not supported yet.");
        }

        var levelBytes = checked((int)_levelByteSizes[mip]);
        if (data.Length < levelBytes) {
            throw new ArgumentException("Data is too short for this DDS subresource.", nameof(data));
        }
        var buffer = new byte[levelBytes];
        data[..levelBytes].CopyTo(buffer);
        _levelBuffers[(item * _header.MipMapCount) + mip] = buffer;
    }

    public void Finish()
    {
        _state.Enter();
        try {
            if (_finished) {
                return;
            }
            try {
                foreach (var bytes in OutputBuffers()) {
                    _stream.Write(bytes.Span);
                }
                _finished = true;
                Array.Clear(_levelBuffers);
            }
            catch { _state.Fault(); throw; }
        }
        finally { _state.Exit(); }
    }

    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        _state.Enter();
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (_finished) {
                return;
            }
            try {
                foreach (var bytes in OutputBuffers()) {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
                _finished = true;
                Array.Clear(_levelBuffers);
            }
            catch { _state.Fault(); throw; }
        }
        finally { _state.Exit(); }
    }

    private IEnumerable<ReadOnlyMemory<byte>> OutputBuffers()
    {
        using var header = new MemoryStream(148);
        DdsHeaderWriter.Write(new DdsBinaryWriter(header), _header);
        yield return header.ToArray();

        byte[]? zeros = null;
        for (var item = 0; item < _itemCount; item++) {
            for (var mip = 0; mip < _header.MipMapCount; mip++) {
                var index = (item * _header.MipMapCount) + mip;
                var buffer = _levelBuffers[index];
                var remaining = _levelByteSizes[mip];
                long offset = 0;
                while (remaining > 0) {
                    var length = (int)Math.Min(remaining, 65536);
                    yield return buffer is null
                        ? (zeros ??= new byte[65536]).AsMemory(0, length)
                        : buffer.AsMemory(checked((int)offset), length);
                    offset += length;
                    remaining -= length;
                }
            }
        }
    }

    public void Dispose()
    {
        _state.Dispose();
        Array.Clear(_levelBuffers);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private int ItemIndex(int arrayElement, int face) => _header.IsCubemap
        ? checked((arrayElement * 6) + face)
        : arrayElement;
}
