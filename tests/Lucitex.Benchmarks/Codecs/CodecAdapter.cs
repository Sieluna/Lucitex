using Lucitex.Benchmarks.Data;

namespace Lucitex.Benchmarks.Codecs;

internal abstract class CodecAdapter
{
    public abstract byte[] Encode(TestImage image, ComparisonCase comparison);
    public abstract void Decode(byte[] encoded, TestImage layout, byte[] destination);

    public virtual byte[] Convert(byte[] encoded, TestImage layout, ComparisonCase comparison)
    {
        var pixels = new byte[layout.Pixels.Length];
        Decode(encoded, layout, pixels);
        return Encode(layout with { Pixels = pixels }, comparison);
    }

    public static CodecAdapter Create(Library library) => library switch {
        Library.Lucitex => new LucitexAdapter(),
        Library.ImageSharp => new ImageSharpAdapter(),
        Library.SkiaSharp => new SkiaSharpAdapter(),
        Library.NetVips => new NetVipsAdapter(),
        Library.MagickNet => new MagickNetAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(library)),
    };
}
