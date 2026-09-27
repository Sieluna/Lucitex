namespace Lucitex.Webp;

public enum WebpCompressionEffort
{
    /// <summary>Prioritizes encoding speed with the smallest search budget; preserves all format features.</summary>
    Fast,
    /// <summary>Default search budget balancing encoding time and lossless compression size.</summary>
    Balanced,
    /// <summary>Largest search budget. Lossless size cannot exceed lower efforts; lossy size/PSNR are not monotonic.</summary>
    Best,
}

public sealed record WebpEncoderOptions
{
    /// <summary>Selects exact RGBA VP8L encoding; false selects lossy RGB VP8 with exact alpha.</summary>
    public bool Lossless { get; init; } = true;

    /// <summary>Lossy quantization quality, 0–100. Ignored for lossless encoding; 100 is not lossless VP8.</summary>
    public int Quality { get; init; } = 75;

    /// <summary>Search effort in either mode, independent of Quality. All efforts support the same format features.</summary>
    public WebpCompressionEffort Effort { get; init; } = WebpCompressionEffort.Balanced;

    /// <summary>Budget for tracked codec buffers, including pool capacity; excludes caller buffers and total process overhead.</summary>
    public long MaxWorkingSet { get; init; } = 512L * 1024 * 1024;
}

internal readonly record struct WebpEffortSettings(int PredictorTier, int MatchCandidates, int PaletteTier, int LossyModes)
{
    public static WebpEffortSettings For(WebpCompressionEffort effort) => effort switch {
        WebpCompressionEffort.Fast => new(0, 1, 0, 1),
        WebpCompressionEffort.Balanced => new(2, 16, 4, 2),
        WebpCompressionEffort.Best => new(4, 16, 4, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(effort)),
    };
}
