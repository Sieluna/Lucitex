using Lucitex.Benchmarks.Data;
using Lucitex.Benchmarks.Formats;

namespace Lucitex.Benchmarks.Codecs;

internal sealed class CodecSession
{
    public ComparisonCase Case { get; }
    public TestImage Image { get; }
    public byte[] DecodeInput { get; }
    public byte[] ReferenceEncoded { get; }
    public byte[] Output { get; }
    private readonly CodecAdapter _adapter;

    public CodecSession(ComparisonCase comparison)
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
        ReferenceEncoded = FormatCatalog.Get(comparison.Format).CreateFixture(Image, comparison);
        if (comparison.Operation == Operation.Convert) {
            var inputModule = FormatCatalog.Get(comparison.InputFormat);
            var channels = inputModule.Channels;
            var pixels = channels == Image.Channels ? Image.Pixels
                : channels == 4 ? PixelLayout.ToRgba(Image.Pixels) : PixelLayout.ToRgb(Image.Pixels);
            var input = Image with { Channels = channels, Pixels = pixels };
            var inputCase = comparison with { Profile = inputModule.DefaultProfile, Operation = Operation.Encode, Library = Library.ImageSharp };
            DecodeInput = inputModule.CreateFixture(input, inputCase);
        }
        else {
            DecodeInput = ReferenceEncoded;
        }
    }

    public byte[] Run() => Case.Operation switch {
        Operation.Encode => _adapter.Encode(Image, Case),
        Operation.Convert => _adapter.Convert(DecodeInput, Image, Case),
        _ => RunDecode(),
    };

    public void Decode(Library library, byte[] encoded, byte[] destination) => CodecAdapter.Create(library).Decode(encoded, Image, destination);

    private byte[] RunDecode()
    {
        _adapter.Decode(DecodeInput, Image, Output);
        return Output;
    }
}
