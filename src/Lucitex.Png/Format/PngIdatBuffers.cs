using System.Buffers;

namespace Lucitex.Png.Format;

internal sealed class PngIdatBuffers : IDisposable
{
    private readonly List<byte[]> _buffers = [];

    public byte[] Rent(int length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        _buffers.Add(buffer);
        return buffer;
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers) {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        _buffers.Clear();
    }
}
