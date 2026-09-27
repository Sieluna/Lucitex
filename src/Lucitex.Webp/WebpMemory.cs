using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Lucitex.Core.Execution;

namespace Lucitex.Webp;

internal sealed class WebpMemory(long limit)
{
    private const long k_LargeBufferThreshold = 1024 * 1024;
    private long _used;
    internal long PeakBytes { get; private set; }

    public void Reserve(long bytes)
    {
        if (bytes < 0 || bytes > limit - _used) {
            throw new ImageFormatException("webp", "LimitExceeded", "WebP working memory exceeds the configured limit.");
        }
        _used += bytes;
        PeakBytes = Math.Max(PeakBytes, _used);
    }

    public void Release(long bytes) => _used -= bytes;

    public WebpBuffer<T> Rent<T>(int length) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var requestedBytes = checked((long)length * Unsafe.SizeOf<T>());
        if (requestedBytes >= k_LargeBufferThreshold) {
            Reserve(requestedBytes);
            try {
                var exactArray = GC.AllocateUninitializedArray<T>(length);
                return new WebpBuffer<T>(this, exactArray, length, pooled: false);
            }
            catch {
                Release(requestedBytes);
                throw;
            }
        }

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
        return new WebpBuffer<T>(this, array, length, pooled: true);
    }
}

internal sealed class WebpBuffer<T>(WebpMemory memory, T[] array, int length, bool pooled) : IDisposable where T : unmanaged
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
        if (pooled) {
            ArrayPool<T>.Shared.Return(buffer);
        }
    }
}
