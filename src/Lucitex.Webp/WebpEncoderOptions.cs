namespace Lucitex.Webp;

public enum WebpCompressionEffort
{
    Fast,
    Balanced,
}

public sealed record WebpEncoderOptions
{
    public bool Lossless { get; init; } = true;

    public int Quality { get; init; } = 75;

    public WebpCompressionEffort Effort { get; init; } = WebpCompressionEffort.Balanced;

    public long MaxWorkingSet { get; init; } = 512L * 1024 * 1024;
}
