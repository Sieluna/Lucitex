using Lucitex.Core.Execution;

namespace Lucitex.Fuzz;

internal static class FuzzLimits
{
    public const int MaxInputBytes = 32 * 1024 * 1024;
    public const int MaxSubresources = 4096;

    public static DecodeLimits Decode { get; } = new() {
        MaxDimensions = 512,
        MaxPixels = 512 * 512,
        MaxParts = 8,
        MaxChannels = 32,
        MaxLevels = 16,
        MaxArrayElements = 16,
        MaxMetadataBytes = 1024 * 1024,
        MaxDecodedBytes = 16 * 1024 * 1024,
        MaxWorkingSet = 32 * 1024 * 1024,
        MaxCompressionRatio = 100,
    };
}
