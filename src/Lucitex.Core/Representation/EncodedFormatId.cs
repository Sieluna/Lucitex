namespace Lucitex.Core.Representation;

public readonly record struct EncodedFormatId(string Name)
{
    public static EncodedFormatId R10G10B10A2 => new(nameof(R10G10B10A2));

    public static EncodedFormatId B5G6R5 => new(nameof(B5G6R5));

    public static EncodedFormatId B5G5R5A1 => new(nameof(B5G5R5A1));

    public static EncodedFormatId R11G11B10Float => new(nameof(R11G11B10Float));

    public static EncodedFormatId Rgb9E5 => new(nameof(Rgb9E5));

    public static EncodedFormatId Rgbe => new(nameof(Rgbe));

    public static EncodedFormatId Bc1 => new(nameof(Bc1));

    public static EncodedFormatId Bc2 => new(nameof(Bc2));

    public static EncodedFormatId Bc3 => new(nameof(Bc3));

    public static EncodedFormatId Bc4 => new(nameof(Bc4));

    public static EncodedFormatId Bc5 => new(nameof(Bc5));

    public static EncodedFormatId Bc6H => new(nameof(Bc6H));

    public static EncodedFormatId Bc7 => new(nameof(Bc7));

    public static EncodedFormatId Opaque(string name) => new(name);

    public override string ToString() => Name;
}
