namespace Lucitex.Core.Color;

public enum AlphaMode
{
    None,
    Straight,
    Premultiplied,
}

public sealed record AlphaDescriptor
{
    public AlphaMode Mode { get; init; } = AlphaMode.None;
}
