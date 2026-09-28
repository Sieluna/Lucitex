namespace Lucitex.Quality;

// Shared by fuzz, benchmark verification and policy regression tests.
internal sealed record CompressionQuality(long EncodedBytes, long ReferenceBytes, long RawBytes,
    string ReferenceEncoder, double MaxSizeRatio, double SourceMse, double ReferenceMse, bool Lossless, bool EnforceSize = true)
{
    public const double SizeLimit = 1.2;
    public double SizeRatio => (double)EncodedBytes / ReferenceBytes;
    public double RawToEncodedRatio => (double)RawBytes / EncodedBytes;
    public bool SizePassed => EncodedBytes > 0 && ReferenceBytes > 0 && RawBytes > 0
        && SizeRatio <= MaxSizeRatio;
    public bool FidelityPassed => double.IsFinite(SourceMse) && double.IsFinite(ReferenceMse)
        && SourceMse >= 0 && ReferenceMse >= 0
        && (Lossless ? SourceMse == 0 && ReferenceMse == 0 : SourceMse <= ReferenceMse * 1.25 + 2);
    public bool Passed => EncodedBytes > 0 && ReferenceBytes > 0 && RawBytes > 0
        && (!EnforceSize || SizePassed) && FidelityPassed;

    public void EnsurePassed()
    {
        if (!Passed) {
            throw new InvalidDataException($"Compression quality failed: encoded={EncodedBytes} B, "
                + $"reference={ReferenceBytes} B ({ReferenceEncoder}), ratio={SizeRatio:F4}x, limit={MaxSizeRatio:F2}x; "
                + $"source MSE={SourceMse:F4}, reference MSE={ReferenceMse:F4}, lossless={Lossless}.");
        }
    }
}
