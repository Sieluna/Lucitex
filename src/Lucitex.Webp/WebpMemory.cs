using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Lucitex.Core.Execution;

namespace Lucitex.Webp;

internal sealed class WebpMemory(long limit)
{
    private long _used;

    public void Reserve(long bytes)
    {
        if (bytes < 0 || bytes > limit - _used) {
            throw new ImageFormatException("webp", "LimitExceeded", "WebP working memory exceeds the configured limit.");
        }
        _used += bytes;
    }

    public void Release(long bytes) => _used -= bytes;

    public WebpBuffer<T> Rent<T>(int length) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var estimated = (long)BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, length)) * Unsafe.SizeOf<T>();
        Reserve(estimated);
        T[] array;
        try {
            array = ArrayPool<T>.Shared.Rent(length);
        }
        catch {
            Release(estimated);
            throw;
        }
        var actual = (long)array.Length * Unsafe.SizeOf<T>();
        Release(estimated);
        try {
            Reserve(actual);
        }
        catch {
            ArrayPool<T>.Shared.Return(array);
            throw;
        }
        return new WebpBuffer<T>(this, array, length);
    }
}

internal sealed class WebpBuffer<T>(WebpMemory memory, T[] array, int length) : IDisposable where T : unmanaged
{
    private T[]? _array = array;

    public int Length { get; } = length;

    public Span<T> Span => (_array ?? throw new ObjectDisposedException(nameof(WebpBuffer<T>))).AsSpan(0, Length);

    public void Dispose()
    {
        if (_array is not { } buffer) {
            return;
        }
        _array = null;
        memory.Release((long)buffer.Length * Unsafe.SizeOf<T>());
        ArrayPool<T>.Shared.Return(buffer);
    }
}
