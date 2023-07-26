namespace Lucitex.Png.Format;

public readonly record struct PngPaletteEntry(byte R, byte G, byte B);

public readonly record struct PngChromaticity(double X, double Y);

public sealed record PngChromaticities
{
    public required PngChromaticity White { get; init; }

    public required PngChromaticity Red { get; init; }

    public required PngChromaticity Green { get; init; }

    public required PngChromaticity Blue { get; init; }
}

public sealed record PngTextEntry
{
    public required string Keyword { get; init; }

    public required string Text { get; init; }
}

public sealed record PngRawChunk
{
    public required string Type { get; init; }

    public required byte[] Data { get; init; }
}

public sealed record PngDocument
{
    public required PngIhdr Ihdr { get; init; }

    public IReadOnlyList<PngPaletteEntry> Palette { get; init; } = [];

    public byte[]? TransparencyData { get; init; }

    public float? Gamma { get; init; }

    public byte? SrgbRenderingIntent { get; init; }

    public PngChromaticities? Chromaticities { get; init; }

    public byte[]? IccProfile { get; init; }

    public string? IccProfileName { get; init; }

    public IReadOnlyList<PngTextEntry> TextEntries { get; init; } = [];

    public IReadOnlyList<PngRawChunk> UnknownChunks { get; init; } = [];
}
