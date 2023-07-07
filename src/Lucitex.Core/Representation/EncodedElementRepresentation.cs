using Lucitex.Core.Spatial;

namespace Lucitex.Core.Representation;

public enum EncodedElementClass
{
    Packed,
    SharedExponent,
    BlockCompressed,
    Opaque,
}

public readonly record struct PackedField(string Name, int Offset, int Bits);

public sealed record PackedFieldLayout
{
    public required IReadOnlyList<PackedField> Fields { get; init; }
}

public sealed record EncodedElementRepresentation : PayloadRepresentation
{
    public required EncodedFormatId Format { get; init; }

    public required Extent3I TexelExtentPerElement { get; init; }

    public required int BitsPerElement { get; init; }

    public required EncodedElementClass Class { get; init; }

    public PackedFieldLayout? PackedLayout { get; init; }
}
