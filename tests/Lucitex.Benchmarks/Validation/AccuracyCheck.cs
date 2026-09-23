using System.Security.Cryptography;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Measurement;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks.Validation;

internal sealed record ValidationResult(
    ComparisonCase Case, string Source, string InputSha256, string EncodedInputSha256, string ResultSha256,
    int? EncodedBytes, QualityMetrics SourceQuality, QualityMetrics ReferenceAgreement, string ReferenceDecoder, object? Structure,
    ProcessMemoryResult? ProcessMemory = null);

internal static class AccuracyCheck
{
    public static ValidationResult Run(CodecSession session)
    {
        var c = session.Case;
        var module = FormatCatalog.Get(c.Format);
        var inputHash = session.Image.Sha256;
        var encodedInputHash = Hash(session.DecodeInput);
        var output = session.Run();
        var outputHash = Hash(output);
        if (!outputHash.Equals(Hash(session.Run()), StringComparison.Ordinal)) {
            throw new InvalidDataException("Repeated operation produced different bytes.");
        }
        if (inputHash != session.Image.Sha256 || encodedInputHash != Hash(session.DecodeInput)) {
            throw new InvalidDataException("Codec modified caller-owned input data.");
        }
        var decoded = new byte[session.Image.Pixels.Length];
        var reference = new byte[decoded.Length];
        var encoded = c.Operation != Operation.Decode ? output : session.DecodeInput;
        module.ValidateDimensions(session.Image, encoded);
        if (c.Operation != Operation.Decode) {
            module.DecodeReference(session.Image, encoded, decoded);
        }
        else {
            output.CopyTo(decoded, 0);
        }
        var quality = module.CompareQuality(session.Image, session.Image.Pixels, decoded);
        var (agreement, referenceDecoder) = CompareIndependent(session, c.Format, encoded, decoded,
            c.Operation != Operation.Decode ? module.ReferenceLibrary : c.Library);
        var lossless = quality;
        if (c.Operation == Operation.Convert && module.Lossless) {
            session.Decode(c.Library, session.DecodeInput, reference);
            lossless = module.CompareQuality(session.Image, reference, decoded);
            var (inputAgreement, _) = CompareIndependent(session, c.InputFormat, session.DecodeInput, reference, c.Library);
            if (inputAgreement.Mse > FormatCatalog.Get(c.InputFormat).ReferenceMseTolerance) {
                throw new InvalidDataException($"Conversion input decoder MSE={inputAgreement.Mse:F4} exceeds tolerance.");
            }
        }
        if (module.Lossless && (!lossless.Exact || !agreement.Exact)) {
            throw new InvalidDataException($"{c.Format} is not pixel-exact under its sample contract: source max error={quality.MaxError}, reference max error={agreement.MaxError}.");
        }
        if (module.Lossless && FormatCatalog.Get(c.InputFormat).Lossless && !quality.Exact) {
            throw new InvalidDataException("Lossless conversion differs from the original analytic pixels.");
        }
        if (agreement.Mse > module.ReferenceMseTolerance) {
            throw new InvalidDataException($"{c.Format} independent-decoder MSE={agreement.Mse:F4} exceeds tolerance {module.ReferenceMseTolerance}.");
        }
        var structure = module.ValidateEncoding(c, encoded, session.ReferenceEncoded);
        if (!module.Lossless) {
            module.DecodeReference(session.Image, session.ReferenceEncoded, reference);
            var referenceQuality = module.CompareQuality(session.Image, session.Image.Pixels, reference);
            if (c.Operation != Operation.Decode && quality.Mse > referenceQuality.Mse * 1.25 + 2) {
                throw new InvalidDataException($"JPEG source MSE={quality.Mse:F4} exceeds the reference quality envelope ({referenceQuality.Mse:F4} * 1.25 + 2).");
            }
        }
        return new ValidationResult(c, session.Image.Source, inputHash, encodedInputHash,
            outputHash, c.Operation != Operation.Decode ? encoded.Length : null, quality, agreement, referenceDecoder, structure);
    }

    private static (QualityMetrics Metrics, string Decoder) CompareIndependent(CodecSession session, string format, byte[] encoded, byte[] actual, Library? excluded)
    {
        var module = FormatCatalog.Get(format);
        var candidates = new List<(QualityMetrics Metrics, string Decoder)>();
        var reference = new byte[actual.Length];
        if (module.ReferenceLibrary is null) {
            module.DecodeReference(session.Image, encoded, reference);
            return (module.CompareQuality(session.Image, reference, actual), module.ReferenceName);
        }
        foreach (var library in module.IndependentDecoders) {
            if (library == excluded || !CodecCapabilities.Supports(library, format)) {
                continue;
            }
            try {
                session.Decode(library, encoded, reference);
            }
            catch (Exception exception) {
                throw new InvalidDataException($"Reference decoder {library} failed while validating {format} pixels.", exception);
            }
            candidates.Add((module.CompareQuality(session.Image, reference, actual), library.ToString()));
        }
        if (format == "jpeg" && excluded != Library.MagickNet) {
            new MagickNetAdapter(fancyUpsampling: false).Decode(encoded, session.Image, reference);
            candidates.Add((QualityMetrics.Compare(reference, actual), "Magick.NET nearest"));
        }
        return candidates.Count != 0 ? candidates.MinBy(c => c.Metrics.Mse)
            : throw new InvalidDataException($"No independent decoder is available for {format}.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
