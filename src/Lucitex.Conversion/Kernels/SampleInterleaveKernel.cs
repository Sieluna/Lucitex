using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucitex.Conversion.Kernels;

// Moves one channel between a codec's strided row layout and a packed per-channel buffer. Copying a
// sample at a time costs a bounds check and a call per pixel; widening the sample to the matching
// integer type lets the whole row run as one strided load/store loop instead.
internal static class SampleInterleaveKernel
{
    public static void Gather(ReadOnlySpan<byte> source, Span<byte> destination, int stride, int bytesPerSample, int count)
    {
        switch (bytesPerSample) {
            case 1 when stride % sizeof(byte) == 0:
                Gather<byte>(source, destination, stride, count);
                return;
            case 2 when stride % sizeof(ushort) == 0:
                Gather<ushort>(source, destination, stride, count);
                return;
            case 4 when stride % sizeof(uint) == 0:
                Gather<uint>(source, destination, stride, count);
                return;
            case 8 when stride % sizeof(ulong) == 0:
                Gather<ulong>(source, destination, stride, count);
                return;
            default:
                for (var i = 0; i < count; i++) {
                    source.Slice(i * stride, bytesPerSample).CopyTo(destination.Slice(i * bytesPerSample, bytesPerSample));
                }

                return;
        }
    }

    public static void Scatter(ReadOnlySpan<byte> source, Span<byte> destination, int stride, int bytesPerSample, int count)
    {
        switch (bytesPerSample) {
            case 1 when stride % sizeof(byte) == 0:
                Scatter<byte>(source, destination, stride, count);
                return;
            case 2 when stride % sizeof(ushort) == 0:
                Scatter<ushort>(source, destination, stride, count);
                return;
            case 4 when stride % sizeof(uint) == 0:
                Scatter<uint>(source, destination, stride, count);
                return;
            case 8 when stride % sizeof(ulong) == 0:
                Scatter<ulong>(source, destination, stride, count);
                return;
            default:
                for (var i = 0; i < count; i++) {
                    source.Slice(i * bytesPerSample, bytesPerSample).CopyTo(destination.Slice(i * stride, bytesPerSample));
                }

                return;
        }
    }

    private static void Gather<T>(ReadOnlySpan<byte> source, Span<byte> destination, int stride, int count)
        where T : unmanaged
    {
        var step = stride / Unsafe.SizeOf<T>();
        var samples = MemoryMarshal.Cast<byte, T>(source);
        var packed = MemoryMarshal.Cast<byte, T>(destination);

        if (step == 1) {
            samples[..count].CopyTo(packed);
            return;
        }

        for (var i = 0; i < count; i++) {
            packed[i] = samples[i * step];
        }
    }

    private static void Scatter<T>(ReadOnlySpan<byte> source, Span<byte> destination, int stride, int count)
        where T : unmanaged
    {
        var step = stride / Unsafe.SizeOf<T>();
        var packed = MemoryMarshal.Cast<byte, T>(source);
        var samples = MemoryMarshal.Cast<byte, T>(destination);

        if (step == 1) {
            packed[..count].CopyTo(samples);
            return;
        }

        for (var i = 0; i < count; i++) {
            samples[i * step] = packed[i];
        }
    }
}
