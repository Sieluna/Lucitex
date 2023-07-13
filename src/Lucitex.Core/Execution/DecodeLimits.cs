namespace Lucitex.Core.Execution;

public sealed record DecodeLimits
{
    public long MaxDimensions { get; init; } = 1 << 16;

    public long MaxPixels { get; init; } = 1L << 32;

    public int MaxParts { get; init; } = 256;

    public int MaxChannels { get; init; } = 256;

    public int MaxLevels { get; init; } = 32;

    public int MaxArrayElements { get; init; } = 4096;

    public long MaxMetadataBytes { get; init; } = 64 * 1024 * 1024;

    public long MaxDecodedBytes { get; init; } = 8L * 1024 * 1024 * 1024;

    public long MaxWorkingSet { get; init; } = 2L * 1024 * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 1000.0;

    public long MaxDeepSamples { get; init; } = 1L << 32;

    public static DecodeLimits Default { get; } = new();
}
