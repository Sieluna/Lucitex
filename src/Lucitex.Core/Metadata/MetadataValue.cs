using System.Text.Json.Serialization;

namespace Lucitex.Core.Metadata;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StringMetadataValue), "string")]
[JsonDerivedType(typeof(Int64MetadataValue), "int64")]
[JsonDerivedType(typeof(DoubleMetadataValue), "double")]
[JsonDerivedType(typeof(BytesMetadataValue), "bytes")]
public abstract record MetadataValue;

public sealed record StringMetadataValue(string Value) : MetadataValue;

public sealed record Int64MetadataValue(long Value) : MetadataValue;

public sealed record DoubleMetadataValue(double Value) : MetadataValue;

public sealed record BytesMetadataValue(byte[] Value) : MetadataValue;
