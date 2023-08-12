namespace Lucitex.Ktx2.Format;

public readonly record struct Ktx2LevelIndexEntry(ulong ByteOffset, ulong ByteLength, ulong UncompressedByteLength);

public sealed record Ktx2Header
{
    public required VkFormat Format { get; init; }

    public uint TypeSize { get; init; } = 1;

    public required uint PixelWidth { get; init; }

    public required uint PixelHeight { get; init; }

    public uint PixelDepth { get; init; }

    public uint LayerCount { get; init; }

    public uint FaceCount { get; init; } = 1;

    public required IReadOnlyList<Ktx2LevelIndexEntry> Levels { get; init; }

    public Ktx2SupercompressionScheme SupercompressionScheme { get; init; } = Ktx2SupercompressionScheme.None;

    public bool IsCubemap => FaceCount == 6;

    public bool Is3D => PixelDepth > 0;

    public int EffectiveArrayElementCount => (int)Math.Max(1, LayerCount);
}
