using System.IO.Compression;
using Lucitex.Png.Format;

namespace Lucitex.Png;

public sealed record PngEncoderOptions
{
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Fastest;
    public PngFilterType? Filter { get; init; }
}
