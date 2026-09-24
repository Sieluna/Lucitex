using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Exr;
using Lucitex.Hdr;
using Lucitex.Jpeg;
using Lucitex.Ktx2;
using Lucitex.Png;
using Lucitex.Webp;

namespace Lucitex.Fuzz;

internal static class ManagedDecoder
{
    public static DecodeOutcome Decode(ImageFormat format, byte[] data, DecodeLimits? limits = null)
    {
        var subresources = 0;
        long decodedBytes = 0;
        try {
            limits ??= FuzzLimits.Decode;
            using var stream = new MemoryStream(data, writable: false);
            using var reader = CreateCodec(format).OpenReader(stream, limits);
            var descriptor = reader.Describe();
            var violations = DecodeLimitsValidator.Validate(descriptor, limits);
            if (violations.Count != 0) {
                return new(DecodeStatus.ResourceLimit, string.Join("; ", violations.Select(v => v.Message)));
            }

            byte[] buffer = [];
            foreach (var read in SubresourceLayout.Enumerate(descriptor, limits)) {
                if (buffer.Length < read.ByteCount) {
                    buffer = new byte[read.ByteCount];
                }
                var written = reader.Read(read.Region, buffer.AsSpan(0, read.ByteCount));
                if (written != read.ByteCount) {
                    throw new InvalidOperationException($"Reader returned {written} bytes; expected {read.ByteCount} for {read.Region.Subresource}.");
                }
                subresources++;
                decodedBytes += written;
            }
            if (subresources == 0) {
                throw new InvalidOperationException("Reader described no readable subresources.");
            }
            return new(DecodeStatus.Accepted, Subresources: subresources, DecodedBytes: decodedBytes);
        }
        catch (ImageFormatException exception) {
            var status = exception.Code == "LimitExceeded" ? DecodeStatus.ResourceLimit
                : exception.Code.StartsWith("Unsupported.", StringComparison.Ordinal) ? DecodeStatus.Unsupported
                : DecodeStatus.Rejected;
            return new(status,
                $"{exception.Format}/{exception.Code}: {exception.Message}", subresources, decodedBytes);
        }
        catch (NotSupportedException exception) {
            return new(DecodeStatus.Unsupported, exception.Message, subresources, decodedBytes);
        }
        catch (Exception exception) {
            return new(DecodeStatus.Crash, exception.ToString(), subresources, decodedBytes);
        }
    }

    private static IImageCodec CreateCodec(ImageFormat format) => format switch {
        ImageFormat.Png => new PngCodec(),
        ImageFormat.Exr => new ExrCodec(),
        ImageFormat.Hdr => new HdrCodec(),
        ImageFormat.Ktx2 => new Ktx2Codec(),
        ImageFormat.Jpeg => new JpegCodec(),
        ImageFormat.Webp => new WebpCodec(),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
