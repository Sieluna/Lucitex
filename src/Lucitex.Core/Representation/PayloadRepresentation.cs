using System.Text.Json.Serialization;

namespace Lucitex.Core.Representation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PlainSampleRepresentation), "plain")]
[JsonDerivedType(typeof(EncodedElementRepresentation), "encoded")]
[JsonDerivedType(typeof(IndexedRepresentation), "indexed")]
[JsonDerivedType(typeof(DeepRepresentation), "deep")]
public abstract record PayloadRepresentation;
