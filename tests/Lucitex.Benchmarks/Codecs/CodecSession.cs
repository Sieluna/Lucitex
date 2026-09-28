using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Formats;
using Lucitex.Benchmarks.Validation;
using System.Security.Cryptography;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class CodecSession
{
    public ComparisonCase Case { get; }
    public TestImage Image { get; }
    public byte[] DecodeInput { get; }
    public byte[] ReferenceEncoded { get; }
    public byte[] Output { get; }
    public TestImage QualitySource { get; }
    public PairingSelection? Selection { get; }
    public int SelectedQuality => _settings.EncoderQuality;
    private static readonly Dictionary<string, QualityCandidate[]> s_Candidates = new();
    internal static void ResetCalibration() => s_Candidates.Clear();
    private readonly CodecAdapter _adapter;
    private readonly ComparisonCase _settings;
    private readonly string _sourceHash;
    private readonly string _inputHash;
    private readonly string _qualityHash;
    private readonly string _referenceHash;

    public CodecSession(ComparisonCase comparison, bool frozenInputs = false)
    {
        if (comparison.UnsupportedReason() is { } reason) {
            throw new NotSupportedException(reason);
        }
        Case = comparison;
        Image = FormatCatalog.Get(comparison.Format).CreateImage(comparison.Image);
        if (comparison.Operation == Operation.Convert && Image.Channels == 4
            && (comparison.Format == "jpeg" || comparison.InputFormat == "jpeg")) {
            Image = Image with { Pixels = PixelLayout.ToRgba(PixelLayout.ToRgb(Image.Pixels)), Source = Image.Source + "; conversion fixture alpha=255" };
        }
        Output = new byte[Image.Pixels.Length];
        _adapter = CodecAdapter.Create(comparison.Library);
        var module = FormatCatalog.Get(comparison.Format);
        QualitySource = Image;
        int? selectedQuality = null;
        if (frozenInputs) {
            var inputs = ValidationStore.ReadInputs(comparison);
            DecodeInput = inputs.Encoded;
            ReferenceEncoded = inputs.Reference;
            QualitySource = Image with { Pixels = inputs.Canonical };
            Selection = inputs.Pairing;
            if (comparison.RequiresPairing != (Selection is not null)) throw new InvalidDataException("Frozen pairing policy mismatch.");
            selectedQuality = Selection?.Quality;
        }
        else if (comparison.Operation == Operation.Convert) {
            var inputModule = FormatCatalog.Get(comparison.InputFormat);
            var channels = inputModule.Channels;
            var pixels = channels == Image.Channels ? Image.Pixels
                : channels == 4 ? PixelLayout.ToRgba(Image.Pixels) : PixelLayout.ToRgb(Image.Pixels);
            var input = Image with { Channels = channels, Pixels = pixels };
            var inputCase = comparison with { Profile = inputModule.DefaultProfile, Operation = Operation.Encode, Library = Library.ImageSharp, Candidate = "default" };
            DecodeInput = inputModule.CreateFixture(input, inputCase);
            var canonicalPixels = new byte[Image.Pixels.Length];
            inputModule.ValidateDimensions(input, DecodeInput);
            inputModule.DecodeReference(Image, DecodeInput, canonicalPixels);
            QualitySource = Image with { Pixels = canonicalPixels };
            ReferenceEncoded = module.CreateFixture(QualitySource, comparison with { EncoderQuality = 90, Candidate = "default" });
        }
        else {
            ReferenceEncoded = module.CreateFixture(Image, comparison with { EncoderQuality = 90, Candidate = "default" });
            DecodeInput = ReferenceEncoded;
        }
        _settings = comparison with { EncoderQuality = selectedQuality ?? comparison.EncoderQuality };
        _sourceHash = Image.Sha256;
        _inputHash = Hash(DecodeInput);
        _qualityHash = QualitySource.Sha256;
        _referenceHash = Hash(ReferenceEncoded);
        if (comparison.RequiresPairing && !frozenInputs) {
            var referencePixels = new byte[Image.Pixels.Length];
            module.DecodeReference(Image, ReferenceEncoded, referencePixels);
            var referenceMse = module.CompareQuality(Image, QualitySource.Pixels, referencePixels).Mse;
            var target = comparison.RateMatched ? ReferenceEncoded.Length : referenceMse;
            var tolerance = target * (comparison.RateMatched ? 0.02 : 0.05);
            var candidates = Array.Empty<QualityCandidate>();
            double calibrationMs = 0;
            var reused = false;
            var key = $"{comparison.Library}/{comparison.Operation}/{comparison.Candidate}/{_sourceHash}/{_inputHash}/{_qualityHash}/{_referenceHash}";
            if (s_Candidates.TryGetValue(key, out var cached)) {
                candidates = cached;
                reused = true;
            }
            else {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var trials = new List<QualityCandidate>(100);
                for (var q = 1; q <= 100; q++) {
                    var settings = comparison with { EncoderQuality = q };
                    var bytes = comparison.Operation == Operation.Convert
                        ? _adapter.Convert(DecodeInput, Image, settings) : _adapter.Encode(Image, settings);
                    module.ValidateDimensions(Image, bytes);
                    module.DecodeReference(Image, bytes, referencePixels);
                    trials.Add(new(q, bytes.Length, module.CompareQuality(Image, QualitySource.Pixels, referencePixels).Mse, Hash(bytes)));
                }
                candidates = trials.ToArray();
                s_Candidates.Add(key, candidates);
                calibrationMs = timer.Elapsed.TotalMilliseconds;
            }
            selectedQuality = candidates.OrderBy(c => Math.Abs((comparison.RateMatched ? c.Bytes : c.Mse) - target))
                .ThenBy(c => comparison.RateMatched ? c.Mse : c.Bytes).ThenBy(c => c.Quality).First().Quality;
            _settings = comparison with { EncoderQuality = selectedQuality.Value };
            var selectedBytes = Run();
            module.DecodeReference(Image, selectedBytes, referencePixels);
            var actual = comparison.RateMatched ? selectedBytes.Length : module.CompareQuality(Image, QualitySource.Pixels, referencePixels).Mse;
            Selection = new(comparison.RateMatched ? "Bytes" : "MSE", target, tolerance, actual, selectedQuality.Value,
                candidates, calibrationMs, reused);
        }
        VerifyInputIntegrity();
    }

    public byte[] Run() => Case.Operation switch {
        Operation.Encode => _adapter.Encode(Image, _settings),
        Operation.Convert => _adapter.Convert(DecodeInput, Image, _settings),
        _ => RunDecode(),
    };

    public void Decode(Library library, byte[] encoded, byte[] destination) => CodecAdapter.Create(library).Decode(encoded, Image, destination);

    public void VerifyInputIntegrity()
    {
        if (_sourceHash != Image.Sha256 || _inputHash != Hash(DecodeInput)
            || _qualityHash != QualitySource.Sha256 || _referenceHash != Hash(ReferenceEncoded))
            throw new InvalidDataException("Codec or calibration modified a shared input/reference buffer.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private byte[] RunDecode()
    {
        _adapter.Decode(DecodeInput, Image, Output);
        return Output;
    }
}

internal sealed record QualityCandidate(int Quality, int Bytes, double Mse, string OutputSha256);
internal sealed record PairingSelection(string Metric, double Target, double Tolerance, double Actual, int Quality,
    IReadOnlyList<QualityCandidate> Candidates, double CalibrationMilliseconds, bool ReusedCandidates)
{
    public double Deviation => Actual - Target;
    public double? DeviationPercent => Target > 0 ? Deviation / Target * 100 : null;
    public bool Matched => double.IsFinite(Actual) && Math.Abs(Deviation) <= Tolerance;
    public bool Exhaustive => Candidates.Count == 100;
}
