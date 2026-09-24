using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;

namespace Lucitex.Webp;

public sealed class WebpCodec : IImageCodec
{
    public string FormatId => "webp";

    public IReadOnlyList<string> Extensions { get; } = [".webp"];

    public CodecCapabilities Capabilities { get; } = new() {
        SupportedSampleTypes = [SampleType.UNorm8],
        SupportedChannelCounts = [3, 4],
        MaxChannelsPerPart = 4,
    };

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
        => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = 12 }
            : FormatProbeResult.NoMatch(12);

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try {
            return new WebpReader(stream, limits ?? DecodeLimits.Default);
        }
        catch (Exception exception) when (WebpFormatErrors.IsMalformed(exception)) {
            throw WebpFormatErrors.Wrap(exception);
        }
    }

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) => new WebpWriter(stream, descriptor, new WebpEncoderOptions());

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor, WebpEncoderOptions options) => new WebpWriter(stream, descriptor, options);
}

internal static class WebpFormatErrors
{
    public static bool IsMalformed(Exception exception) => exception is EndOfStreamException or InvalidDataException or OverflowException;

    public static ImageFormatException Wrap(Exception exception) => new("webp", "MalformedData", exception.Message, innerException: exception);
}
