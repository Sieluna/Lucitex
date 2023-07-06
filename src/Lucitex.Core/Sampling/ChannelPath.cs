namespace Lucitex.Core.Sampling;

public readonly record struct ChannelPath
{
    public required string FullName { get; init; }

    public IReadOnlyList<string> Segments => FullName.Split('.');

    public static ChannelPath Of(string fullName) => new() { FullName = fullName };

    public static implicit operator ChannelPath(string fullName) => Of(fullName);

    public override string ToString() => FullName;
}
