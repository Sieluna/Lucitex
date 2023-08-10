using Lucitex.Core.Semantic;

namespace Lucitex.Core.Execution;

public sealed record DecodeLimitViolation
{
    public required string Limit { get; init; }

    public required string Message { get; init; }
}

public static class DecodeLimitsValidator
{
    public static IReadOnlyList<DecodeLimitViolation> Validate(ImageAssetDescriptor asset, DecodeLimits limits)
    {
        var violations = new List<DecodeLimitViolation>();

        if (asset.Parts.Count > limits.MaxParts) {
            violations.Add(Violation(nameof(limits.MaxParts), $"{asset.Parts.Count} parts exceeds limit of {limits.MaxParts}."));
        }

        foreach (var part in asset.Parts) {
            ValidatePart(part, limits, violations);
        }

        return violations;
    }

    private static void ValidatePart(ImagePartDescriptor part, DecodeLimits limits, List<DecodeLimitViolation> violations)
    {
        if (part.Channels.Channels.Count > limits.MaxChannels) {
            violations.Add(Violation(nameof(limits.MaxChannels), $"part '{part.Name}' has {part.Channels.Channels.Count} channels, exceeding limit of {limits.MaxChannels}."));
        }

        var extent = part.Topology.BaseExtent;
        if (extent.Width > limits.MaxDimensions || extent.Height > limits.MaxDimensions || extent.Depth > limits.MaxDimensions) {
            violations.Add(Violation(nameof(limits.MaxDimensions), $"part '{part.Name}' extent {extent} exceeds dimension limit of {limits.MaxDimensions}."));
        }

        try {
            var pixels = checked(extent.Width * extent.Height * extent.Depth);
            if (pixels > limits.MaxPixels) {
                violations.Add(Violation(nameof(limits.MaxPixels), $"part '{part.Name}' has {pixels} pixels, exceeding limit of {limits.MaxPixels}."));
            }
        }
        catch (OverflowException) {
            violations.Add(Violation(nameof(limits.MaxPixels), $"part '{part.Name}' pixel count overflows Int64 arithmetic."));
        }

        if (part.Topology.Levels.Count > limits.MaxLevels) {
            violations.Add(Violation(nameof(limits.MaxLevels), $"part '{part.Name}' has {part.Topology.Levels.Count} levels, exceeding limit of {limits.MaxLevels}."));
        }

        if (part.Topology.ArrayElementCount > limits.MaxArrayElements) {
            violations.Add(Violation(nameof(limits.MaxArrayElements), $"part '{part.Name}' has {part.Topology.ArrayElementCount} array elements, exceeding limit of {limits.MaxArrayElements}."));
        }

        var metadataBytes = part.Metadata.Entries.Sum(entry => (long)(entry.RawRepresentation?.Length ?? 0));
        if (metadataBytes > limits.MaxMetadataBytes) {
            violations.Add(Violation(nameof(limits.MaxMetadataBytes), $"part '{part.Name}' metadata is {metadataBytes} bytes, exceeding limit of {limits.MaxMetadataBytes}."));
        }
    }

    private static DecodeLimitViolation Violation(string limit, string message) => new() { Limit = limit, Message = message };
}
