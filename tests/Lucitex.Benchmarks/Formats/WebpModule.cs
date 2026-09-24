using System.Buffers.Binary;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Suites;

namespace Lucitex.Benchmarks.Formats;

internal sealed class WebpModule : IFormatModule
{
    public string Id => "webp";
    public int Channels => 4;
    public bool Lossless => true;
    public string DefaultProfile => "webp-lossless";
    public IReadOnlyList<string> Profiles { get; } = ["webp-lossless"];
    public IReadOnlyList<Type> BenchmarkTypes { get; } = [typeof(WebpEncodeBenchmarks), typeof(WebpDecodeBenchmarks)];
    public IReadOnlyList<Library> IndependentDecoders => [Library.ImageSharp, Library.SkiaSharp, Library.NetVips, Library.MagickNet];

    public byte[] CreateFixture(TestImage source, ComparisonCase comparison) => new ImageSharpAdapter().Encode(source, comparison);

    public object ValidateEncoding(ComparisonCase comparison, byte[] encoded, byte[] reference)
    {
        if (encoded.Length < 26 || !encoded.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !encoded.AsSpan(8, 4).SequenceEqual("WEBP"u8) || BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)) != encoded.Length - 8) {
            throw new InvalidDataException("Invalid WebP RIFF container.");
        }
        var found = false;
        for (var offset = 12; offset < encoded.Length;) {
            if (encoded.Length - offset < 8) {
                throw new InvalidDataException("Truncated WebP chunk.");
            }
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(offset + 4)));
            if ((long)size + (size & 1) > encoded.Length - offset - 8) {
                throw new InvalidDataException("Truncated WebP payload.");
            }
            var type = encoded.AsSpan(offset, 4);
            if (type.SequenceEqual("VP8 "u8) || type.SequenceEqual("ALPH"u8) || type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8)) {
                throw new InvalidDataException("The WebP comparison requires static lossless VP8L.");
            }
            if (type.SequenceEqual("VP8X"u8) && (size != 10 || (encoded[offset + 8] & 2) != 0)) {
                throw new InvalidDataException("The WebP comparison requires a static canvas.");
            }
            if ((size & 1) != 0 && encoded[offset + 8 + size] != 0) {
                throw new InvalidDataException("Invalid WebP chunk padding.");
            }
            if (type.SequenceEqual("VP8L"u8)) {
                if (found || size < 5 || encoded[offset + 8] != 0x2f || (encoded[offset + 12] >> 5) != 0) {
                    throw new InvalidDataException("Invalid WebP lossless header.");
                }
                found = true;
            }
            offset = checked(offset + 8 + size + (size & 1));
        }
        if (!found) {
            throw new InvalidDataException("WebP lossless payload is missing.");
        }
        return new {
            Bitstream = "VP8L", ExactRgba = true,
            Effort = comparison.Operation == Operation.Decode ? "Shared ImageSharp fixture: method 4, quality 100"
                : comparison.Library == Library.Lucitex ? "Balanced" : comparison.Library == Library.SkiaSharp ? "Lossless quality 100" : "Method 4, quality 100",
            Fairness = "Effort settings are library-specific. RGBA including hidden RGB must match exactly; speed, memory and size are measured separately."
        };
    }
}
