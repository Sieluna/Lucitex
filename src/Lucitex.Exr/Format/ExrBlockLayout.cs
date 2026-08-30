using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;

namespace Lucitex.Exr.Format;

internal sealed class ExrBlockLayout
{
    private readonly int[] _sampleCounts;
    private readonly int[] _bytesPerSample;
    private readonly int[] _ySampling;
    private readonly long[]? _rowOffsets;
    private readonly int _uniformRowBytes;

    public ExrBlockLayout(IReadOnlyList<ExrChannelInfo> channels, int minX, int maxX, int minY, int maxY)
    {
        ChannelCount = channels.Count;
        MinY = minY;
        RowCount = maxY >= minY ? maxY - minY + 1 : 0;

        _sampleCounts = new int[channels.Count];
        _bytesPerSample = new int[channels.Count];
        _ySampling = new int[channels.Count];

        var subsampled = false;
        var ragged = false;

        for (var i = 0; i < channels.Count; i++) {
            var channel = channels[i];
            _sampleCounts[i] = NumSamples(channel.XSampling, minX, maxX);
            _bytesPerSample[i] = channel.BytesPerSample;
            _ySampling[i] = channel.YSampling;
            subsampled |= channel.XSampling != 1 || channel.YSampling != 1;
            ragged |= channel.YSampling != 1;
        }

        HasSubsampling = subsampled;

        if (ragged) {
            _rowOffsets = new long[RowCount + 1];
            for (var row = 0; row < RowCount; row++) {
                _rowOffsets[row + 1] = _rowOffsets[row] + RowBytes(row);
            }

            TotalBytes = _rowOffsets[RowCount];
        }
        else {
            for (var i = 0; i < channels.Count; i++) {
                _uniformRowBytes = checked(_uniformRowBytes + (_sampleCounts[i] * _bytesPerSample[i]));
            }

            TotalBytes = (long)_uniformRowBytes * RowCount;
        }
    }

    public int ChannelCount { get; }

    public int MinY { get; }

    public int RowCount { get; }

    public bool HasSubsampling { get; }

    public long TotalBytes { get; }

    public int UniformRowBytes => _rowOffsets is null
        ? _uniformRowBytes
        : throw new InvalidOperationException("A block with subsampled rows has no uniform row stride.");

    public long RowOffset(int rowIndex) => _rowOffsets?[rowIndex] ?? (long)rowIndex * _uniformRowBytes;

    public int RowBytes(int rowIndex)
    {
        if (_rowOffsets is null) {
            return _uniformRowBytes;
        }

        var total = 0;
        for (var i = 0; i < ChannelCount; i++) {
            if (IsRowSampled(rowIndex, i)) {
                total = checked(total + (_sampleCounts[i] * _bytesPerSample[i]));
            }
        }

        return total;
    }

    public int SampleCount(int channelIndex) => _sampleCounts[channelIndex];

    public int BytesPerSample(int channelIndex) => _bytesPerSample[channelIndex];

    public int ChannelRowBytes(int channelIndex) => _sampleCounts[channelIndex] * _bytesPerSample[channelIndex];

    public int SampledRowCount(int channelIndex) =>
        NumSamples(_ySampling[channelIndex], MinY, MinY + RowCount - 1);

    public bool IsRowSampled(int rowIndex, int channelIndex) =>
        (MinY + rowIndex) % _ySampling[channelIndex] == 0;

    public int ChannelOffsetInRow(int rowIndex, int channelIndex)
    {
        if (!IsRowSampled(rowIndex, channelIndex)) {
            return -1;
        }

        var offset = 0;
        for (var i = 0; i < channelIndex; i++) {
            if (IsRowSampled(rowIndex, i)) {
                offset += _sampleCounts[i] * _bytesPerSample[i];
            }
        }

        return offset;
    }

    public static int NumSamples(int sampling, int min, int max) =>
        checked((int)Grid(sampling).CountColumns(min, max));

    private static SampleGrid Grid(int sampling) =>
        new() { Origin = Long3.Zero, Step = new Int3(sampling, sampling, 1) };
}
