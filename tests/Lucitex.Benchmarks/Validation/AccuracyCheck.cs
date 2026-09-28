using System.Security.Cryptography;
using Lucitex.Benchmarks.Codecs;
using Lucitex.Benchmarks.Measurement;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks.Validation;

internal sealed record DecoderCheck(string Stage, string Decoder, string Policy, bool Required,
    double Tolerance, QualityMetrics? Agreement, string? Error)
{
    public bool Passed => Error is null && Agreement is { } a && a.Mse <= Tolerance;
}

internal sealed record CompressionSizeTarget(string Encoder, long Bytes, string OutputSha256);
internal sealed record CompressionMetrics(long EncodedBytes, long ReferenceBytes, long RawBytes,
    string ReferenceEncoder, double SourceMse, double ReferenceMse, bool Lossless)
{
    public double SizeRatio => (double)EncodedBytes / ReferenceBytes;
    public double RawToEncodedRatio => (double)RawBytes / EncodedBytes;
    public bool FidelityPassed => EncodedBytes > 0 && ReferenceBytes > 0 && RawBytes > 0
        && double.IsFinite(SourceMse) && SourceMse >= 0 && double.IsFinite(ReferenceMse) && ReferenceMse >= 0
        && (!Lossless || SourceMse == 0 && ReferenceMse == 0);
}

internal sealed record ValidationResult(
    ComparisonCase Case, string Source, string InputSha256, string EncodedInputSha256, string ResultSha256,
    int? EncodedBytes, QualityMetrics SourceQuality, QualityMetrics ReferenceAgreement, string ReferenceDecoder, object? Structure,
    QualityMetrics WorkloadQuality, string QualityInputSha256, string EncoderInputSha256, string ReferenceEncodedSha256,
    int CommonReferenceBytes, string CompressionReferenceSha256,
    IReadOnlyList<DecoderCheck> DecoderChecks, IReadOnlyList<string> CorrectnessErrors, PairingSelection? Selection,
    CompressionMetrics? Compression = null, ProcessMemoryResult? ProcessMemory = null,
    CompressionSizeTarget? SizeTarget = null)
{
    public const double SizeLimit = 1.2;
    public bool CorrectnessPassed => CorrectnessErrors.Count == 0
        && DecoderChecks.All(c => c.Error is null && (!c.Required || c.Passed));
    public double? BestSizeRatio => EncodedBytes is { } size && SizeTarget is { } target ? (double)size / target.Bytes : null;
    public bool PairingMatched => Selection?.Matched != false;
    public bool EnforceSize => Compression is not null && Case.Library == Library.Lucitex
        && Case.Candidate == "default" && !Case.RateMatched && Case.Profile != "webp-lossless-fast";
    public bool SizeTargetPassed => !EnforceSize || !PairingMatched || BestSizeRatio <= SizeLimit;
    public string SizePolicy => Compression is null ? "N/A"
        : Case.Candidate != "default" ? "Tradeoff candidate; size is a ranking objective"
        : Case.Library != Library.Lucitex ? "Comparison only; no size gate"
        : Case.RateMatched ? "Rate comparison; size acceptance belongs to quality comparison"
        : EnforceSize ? "Lucitex <=1.2x smallest qualifying comparable output" : "Lucitex Fast; size advisory";
    public bool FidelityPassed => Compression?.FidelityPassed != false;
    public bool EligibleForTiming => CorrectnessPassed && FidelityPassed;
    public bool AcceptanceFailed => EligibleForTiming && EnforceSize && PairingMatched && !SizeTargetPassed;
    public string EncoderSettings => Case.Operation == Operation.Decode ? "N/A" : Case.EncoderSettings(Selection?.Quality ?? Case.EncoderQuality);
    public double? CommonSizeRatio => EncodedBytes is { } size ? (double)size / CommonReferenceBytes : null;
    public string CompressionBasis => Case.RequiresPairing ? "Common Q90 reference; symmetric pairing tolerance"
        : Case.Operation == Operation.Convert ? "Reference re-encoded from this codec's actual decoded pixels"
        : "Identical source pixels";
    public string DecoderPolicy => string.Join("; ", DecoderChecks.Where(c => c.Required)
        .Select(c => c.Stage + ": " + c.Policy).Distinct().Order());
    public string ComparisonContract => Case.Operation == Operation.Decode ? "Identical encoded bytes; declared decoder policy"
        : Case.Operation == Operation.Convert ? "Identical encoded input; end-to-end quality against shared canonical decode"
        : "Identical source pixels";
    public string QualityContract => Compression is null ? "Decoder correctness; no encoder compression score"
        : Compression.Lossless ? "Exact encoder-input samples"
        : !PairingMatched ? "Unmatched target; diagnostic timing only, not equal quality or rate"
        : Case.RateMatched ? "File bytes within 2% of reference; distortion is measured, not gated"
        : "MSE within 5% of reference; exact when reference MSE is zero";

    public void EnsurePassed()
    {
        if (!CorrectnessPassed) throw new InvalidDataException("Correctness gate failed: "
            + string.Join("; ", CorrectnessErrors.Concat(DecoderChecks.Where(c => c.Error is not null || c.Required && !c.Passed)
                .Select(c => $"{c.Stage}/{c.Decoder}: {c.Error ?? $"MSE {c.Agreement?.Mse} > {c.Tolerance}"}"))));
        if (!FidelityPassed) throw new InvalidDataException("Invalid compression metrics or lossless fidelity failure.");
    }
}

internal static class AccuracyCheck
{
    public static ValidationResult Run(CodecSession session)
    {
        session.VerifyInputIntegrity();
        var c = session.Case;
        var module = FormatCatalog.Get(c.Format);
        var inputHash = session.Image.Sha256;
        var encodedInputHash = Hash(session.DecodeInput);
        var qualityInputHash = session.QualitySource.Sha256;
        var output = session.Run();
        var outputHash = Hash(output);
        var errors = new List<string>();
        if (outputHash != Hash(session.Run())) errors.Add("Repeated operation produced different bytes.");
        if (inputHash != session.Image.Sha256 || encodedInputHash != Hash(session.DecodeInput)
            || qualityInputHash != session.QualitySource.Sha256) errors.Add("Codec modified caller-owned input data.");
        var decoded = new byte[session.Image.Pixels.Length];
        var reference = new byte[decoded.Length];
        var encoded = c.Operation != Operation.Decode ? output : session.DecodeInput;
        module.ValidateDimensions(session.Image, encoded);
        if (c.Operation != Operation.Decode) module.DecodeReference(session.Image, encoded, decoded);
        else output.CopyTo(decoded, 0);
        var quality = module.CompareQuality(session.Image, session.Image.Pixels, decoded);
        var workloadQuality = module.CompareQuality(session.Image, session.QualitySource.Pixels, decoded);
        if (session.Selection is { Candidates.Count: > 0 } selected) {
            var trial = selected.Candidates.Single(t => t.Quality == selected.Quality);
            if (trial.OutputSha256 != outputHash || trial.Mse != workloadQuality.Mse)
                errors.Add("Selected calibration output differs from the repeated validation workload.");
        }
        var checks = CompareIndependent(session, c.Format, encoded, decoded,
            c.Operation != Operation.Decode ? module.ReferenceLibrary : c.Library, "output");
        var primary = checks.First(c => c.Required);
        var encoderInput = session.Image.Pixels;
        if (c.Operation == Operation.Convert) {
            encoderInput = new byte[decoded.Length];
            session.Decode(c.Library, session.DecodeInput, encoderInput);
            checks.AddRange(CompareIndependent(session, c.InputFormat, session.DecodeInput, encoderInput, c.Library, "input"));
        }
        var encoderQuality = module.CompareQuality(session.Image, encoderInput, decoded);
        if (module.Lossless && !encoderQuality.Exact)
            errors.Add($"Lossless output differs from encoder input: max error={encoderQuality.MaxError}.");
        if (module.Lossless && FormatCatalog.Get(c.InputFormat).Lossless && !quality.Exact)
            errors.Add("Lossless conversion differs from the original source samples.");
        var structure = module.ValidateEncoding(c, encoded, session.ReferenceEncoded);
        CompressionMetrics? compression = null;
        var compressionReference = session.ReferenceEncoded;
        if (c.Operation != Operation.Decode) {
            var comparisonPixels = session.QualitySource.Pixels;
            var compressionError = workloadQuality.Mse;
            if (c.Operation == Operation.Convert && !c.RequiresPairing) {
                // Do not blame compression for extra detail produced by a different input decoder.
                // Keep the common source for end-to-end quality and the common-size diagnostic,
                // but enforce compression efficiency on identical actual encoder-input pixels.
                comparisonPixels = encoderInput;
                compressionError = encoderQuality.Mse;
                compressionReference = module.CreateFixture(session.Image with { Pixels = encoderInput }, c with { Candidate = "default" });
            }
            module.DecodeReference(session.Image, compressionReference, reference);
            var referenceQuality = module.CompareQuality(session.Image, comparisonPixels, reference);
            compression = new(encoded.Length, compressionReference.Length,
                c.Format == "exr" ? checked((long)session.Image.Width * session.Image.Height * 8) : session.Image.Pixels.Length,
                module.ReferenceEncoderName(c), compressionError, referenceQuality.Mse, module.Lossless);
        }
        session.VerifyInputIntegrity();
        return new(c, session.Image.Source, inputHash, encodedInputHash, outputHash,
            c.Operation != Operation.Decode ? encoded.Length : null, quality,
            primary.Agreement ?? new(double.MaxValue, null, 255, decoded.Length), primary.Decoder, structure,
            workloadQuality, qualityInputHash, Hash(encoderInput), Hash(session.ReferenceEncoded), session.ReferenceEncoded.Length,
            Hash(compressionReference), checks, errors,
            session.Selection, compression);
    }

    private static List<DecoderCheck> CompareIndependent(CodecSession session, string format, byte[] encoded,
        byte[] actual, Library? excluded, string stage)
    {
        var module = FormatCatalog.Get(format);
        var checks = new List<DecoderCheck>();
        var reference = new byte[actual.Length];
        void Check(string decoder, string policy, bool required, Action decode)
        {
            QualityMetrics? agreement = null;
            string? error = null;
            try {
                decode();
                agreement = module.CompareQuality(session.Image, reference, actual);
            }
            catch (Exception exception) { error = exception.ToString(); }
            checks.Add(new(stage, decoder, policy, required, module.ReferenceMseTolerance, agreement, error));
        }
        if (module.ReferenceLibrary is null) {
            module.DecodeReference(session.Image, encoded, reference);
            checks.Add(new(stage, module.ReferenceName, "Exact normalized samples", true, 0,
                module.CompareQuality(session.Image, reference, actual), null));
            return checks;
        }
        var libraries = module.IndependentDecoders.Where(l => l != excluded && CodecCapabilities.Supports(l, format)).ToArray();
        if (libraries.Length == 0) throw new InvalidDataException($"No independent decoder for {format}.");
        var nearest = format == "jpeg" && (excluded == Library.ImageSharp
            || excluded == Library.Lucitex && session.Image.Width <= 4);
        var primaryLibrary = format == "jpeg" && excluded != Library.MagickNet ? Library.MagickNet : libraries[0];
        if (format == "jpeg" && excluded == Library.MagickNet) primaryLibrary = Library.NetVips;
        foreach (var library in libraries) {
            var required = module.Lossless || !nearest && library == primaryLibrary;
            var policy = module.Lossless ? "Exact normalized samples"
                : library == Library.ImageSharp ? "Nearest chroma" : "Interpolated chroma";
            Check(library.ToString(), policy, required, () => session.Decode(library, encoded, reference));
        }
        if (format == "jpeg") Check("Magick.NET nearest", "Nearest chroma", nearest,
            () => new MagickNetAdapter(fancyUpsampling: false).Decode(encoded, session.Image, reference));
        return checks;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
