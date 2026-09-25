using Lucitex.Conversion;

namespace Lucitex.Example.Shared;

public sealed record TargetSizeResult(byte[] Bytes, int Quality, bool ReachedTarget, ConversionPlan Plan);

public static class TargetSizeCompressor
{
    public static bool SupportsTargetSize(Format format) => format.Parameters.Any(parameter => parameter.Id == "quality");

    public static TargetSizeResult Compress(Format source, byte[] sourceBytes, Format target, long targetBytes,
        ConversionPolicy policy = ConversionPolicy.Preview, (int Width, int Height)? resize = null, (int X, int Y, int Width, int Height)? crop = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(target);
        if (targetBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(targetBytes), "Target size must be positive.");
        }

        var qualityParameter = target.Parameters.FirstOrDefault(parameter => parameter.Id == "quality")
            ?? throw new NotSupportedException($"'{target.Id}' has no 'quality' parameter to search over for a target size.");
        var low = qualityParameter.Minimum!.Value;
        var high = qualityParameter.Maximum!.Value;

        var (floorBytes, floorPlan) = Encode(source, sourceBytes, target.Parse([new("quality", low.ToString())]), policy, resize, crop);
        if (floorBytes.LongLength > targetBytes) {
            return new TargetSizeResult(floorBytes, low, ReachedTarget: false, floorPlan);
        }

        var bestBytes = floorBytes;
        var bestPlan = floorPlan;
        var bestQuality = low;
        low++;

        while (low <= high) {
            var mid = low + ((high - low) / 2);
            var (bytes, plan) = Encode(source, sourceBytes, target.Parse([new("quality", mid.ToString())]), policy, resize, crop);
            if (bytes.LongLength <= targetBytes) {
                bestBytes = bytes;
                bestPlan = plan;
                bestQuality = mid;
                low = mid + 1;
            }
            else {
                high = mid - 1;
            }
        }

        return new TargetSizeResult(bestBytes, bestQuality, ReachedTarget: true, bestPlan);
    }

    private static (byte[] Bytes, ConversionPlan Plan) Encode(Format source, byte[] sourceBytes, Format target, ConversionPolicy policy,
        (int Width, int Height)? resize, (int X, int Y, int Width, int Height)? crop)
    {
        using var input = new MemoryStream(sourceBytes, writable: false);
        using var output = new MemoryStream();
        var conversion = target.From(source, input, policy);
        if (crop is { } region) {
            conversion = conversion.Crop(region.X, region.Y, region.Width, region.Height);
        }
        if (resize is { } size) {
            conversion = conversion.Resize(size.Width, size.Height);
        }

        var plan = conversion.WriteTo(output);
        return (output.ToArray(), plan);
    }
}
