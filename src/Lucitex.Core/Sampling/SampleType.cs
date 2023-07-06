namespace Lucitex.Core.Sampling;

public enum ScalarKind
{
    Float,
    UnsignedInt,
    SignedInt,
}

public enum NumericEncoding
{
    Raw,
    UNorm,
    SNorm,
}

public readonly record struct SampleType(ScalarKind Kind, byte Bits, NumericEncoding Encoding)
{
    public static SampleType Float16 => new(ScalarKind.Float, 16, NumericEncoding.Raw);

    public static SampleType Float32 => new(ScalarKind.Float, 32, NumericEncoding.Raw);

    public static SampleType UInt8 => new(ScalarKind.UnsignedInt, 8, NumericEncoding.Raw);

    public static SampleType UInt16 => new(ScalarKind.UnsignedInt, 16, NumericEncoding.Raw);

    public static SampleType UInt32 => new(ScalarKind.UnsignedInt, 32, NumericEncoding.Raw);

    public static SampleType UNorm1 => new(ScalarKind.UnsignedInt, 1, NumericEncoding.UNorm);

    public static SampleType UNorm2 => new(ScalarKind.UnsignedInt, 2, NumericEncoding.UNorm);

    public static SampleType UNorm4 => new(ScalarKind.UnsignedInt, 4, NumericEncoding.UNorm);

    public static SampleType UNorm8 => new(ScalarKind.UnsignedInt, 8, NumericEncoding.UNorm);

    public static SampleType UNorm16 => new(ScalarKind.UnsignedInt, 16, NumericEncoding.UNorm);

    public static SampleType SNorm8 => new(ScalarKind.SignedInt, 8, NumericEncoding.SNorm);

    public static SampleType SNorm16 => new(ScalarKind.SignedInt, 16, NumericEncoding.SNorm);
}
