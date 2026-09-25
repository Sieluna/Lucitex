namespace Lucitex.Benchmarks;

[Flags]
internal enum Suite
{
    Jpeg = 1 << 0,
    Png = 1 << 1,
    Exr = 1 << 2,
    Ktx2 = 1 << 3,
    Webp = 1 << 4,
    Convert = 1 << 5,
    Kernels = 1 << 6,
    Core = 1 << 7,
    Formats = Jpeg | Png | Exr | Ktx2 | Webp,
    All = Formats | Convert | Kernels | Core,
}
