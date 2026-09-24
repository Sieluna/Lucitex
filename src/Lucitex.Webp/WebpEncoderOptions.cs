namespace Lucitex.Webp;

public enum WebpCompressionEffort
{
    Fast,
    Balanced,
}

public sealed record WebpEncoderOptions
{
    public WebpCompressionEffort Effort { get; init; } = WebpCompressionEffort.Balanced;

    public long MaxWorkingSet { get; init; } = 512L * 1024 * 1024;
}
