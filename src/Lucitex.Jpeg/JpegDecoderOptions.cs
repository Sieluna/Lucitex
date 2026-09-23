namespace Lucitex.Jpeg;

public sealed record JpegDecoderOptions
{
    public bool InterpolateChroma { get; init; } = true;
}
