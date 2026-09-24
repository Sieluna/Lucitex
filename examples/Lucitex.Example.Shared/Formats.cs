using System.IO.Compression;
using Lucitex.Dds;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Hdr;
using Lucitex.Jpeg;
using Lucitex.Ktx2;
using Lucitex.Ktx2.Format;
using Lucitex.Png;
using Lucitex.Png.Format;
using Lucitex.Webp;

namespace Lucitex.Example.Shared;

public static class Formats
{
    private static readonly PngCodec s_Png = new();
    private static readonly JpegCodec s_Jpeg = new();
    private static readonly WebpCodec s_Webp = new();
    private static readonly Ktx2Codec s_Ktx2 = new();

    public static Format<JpegEncoderOptions> Jpeg { get; } = new(s_Jpeg, new(), s_Jpeg.CreateWriter,
        Field<JpegEncoderOptions>.Integer("quality", "Quality",
            "Higher values preserve more detail and usually produce larger files. Even 100 is lossy.", 1, 100,
            o => o.Quality, (o, value) => o with { Quality = value }),
        Field<JpegEncoderOptions>.Select("chroma-subsampling", "Chroma subsampling",
            "4:4:4 preserves full color resolution; 4:2:0 usually produces smaller files. Grayscale is unaffected.",
            o => o.ChromaSubsampling, (o, value) => o with { ChromaSubsampling = value },
            ("444", "4:4:4 — full color resolution", JpegChromaSubsampling.Ratio444),
            ("422", "4:2:2 — half horizontal color resolution", JpegChromaSubsampling.Ratio422),
            ("420", "4:2:0 — half horizontal and vertical color resolution", JpegChromaSubsampling.Ratio420)),
        Field<JpegEncoderOptions>.Boolean("progressive", "Progressive encoding",
            "Store the image in successive scans for progressive display.", o => o.Progressive, (o, value) => o with { Progressive = value }),
        Field<JpegEncoderOptions>.Boolean("optimize-huffman", "Optimize Huffman tables",
            "Adapt entropy tables to the image without further quality loss.", o => o.OptimizeHuffmanTables, (o, value) => o with { OptimizeHuffmanTables = value }));

    public static Format<WebpEncoderOptions> Webp { get; } = new(s_Webp, new() { Lossless = false }, s_Webp.CreateWriter,
        Field<WebpEncoderOptions>.Boolean("lossless", "Lossless encoding",
            "Preserve pixels exactly with VP8L. Disable for VP8 lossy compression with adjustable quality.", o => o.Lossless, (o, value) => o with { Lossless = value }),
        Field<WebpEncoderOptions>.Integer("quality", "Quality",
            "Higher values preserve more detail. Applies to lossy encoding; even 100 uses chroma subsampling and is not lossless.", 0, 100,
            o => o.Quality, (o, value) => o with { Quality = value }).When("lossless", "false"),
        Field<WebpEncoderOptions>.Select("effort", "Compression effort",
            "Balanced evaluates more predictions; Fast reduces encoding work.",
            o => o.Effort, (o, value) => o with { Effort = value },
            ("fast", "Fast", WebpCompressionEffort.Fast), ("balanced", "Balanced", WebpCompressionEffort.Balanced)));

    public static Format<PngEncoderOptions> Png { get; } = new(s_Png, new(), s_Png.CreateWriter,
        Field<PngEncoderOptions>.Select("compression", "Compression effort",
            "Trade encoding speed for file size. Every setting is lossless.",
            o => o.CompressionLevel, (o, value) => o with { CompressionLevel = value },
            ("none", "No compression", CompressionLevel.NoCompression), ("fast", "Fast", CompressionLevel.Fastest),
            ("optimal", "Optimal", CompressionLevel.Optimal), ("smallest", "Smallest size", CompressionLevel.SmallestSize)),
        Field<PngEncoderOptions>.Select<PngFilterType?>("filter", "Row filter",
            "Adaptive selects a filter for each row. Fixed filters can affect file size without changing pixels.",
            o => o.Filter, (o, value) => o with { Filter = value },
            ("adaptive", "Adaptive", null), ("none", "None", PngFilterType.None), ("sub", "Sub", PngFilterType.Sub),
            ("up", "Up", PngFilterType.Up), ("average", "Average", PngFilterType.Average), ("paeth", "Paeth", PngFilterType.Paeth)));

    public static Format<ExrCompressionId> Exr { get; } = new(new ExrCodec(), ExrCompressionId.Zip,
        (stream, descriptor, compression) => new ExrCodec(compression).CreateWriter(stream, descriptor),
        Field<ExrCompressionId>.Select("compression", "Compression",
            "Choose a lossless compression method. Effectiveness depends on the image data.",
            value => value, (_, value) => value,
            ("none", "None", ExrCompressionId.None), ("rle", "RLE", ExrCompressionId.Rle),
            ("zips", "ZIP — individual scanlines", ExrCompressionId.Zips), ("zip", "ZIP — blocks of scanlines", ExrCompressionId.Zip),
            ("piz", "PIZ", ExrCompressionId.Piz)));

    public static Format<Ktx2SupercompressionScheme> Ktx2 { get; } = new(s_Ktx2, Ktx2SupercompressionScheme.None, s_Ktx2.CreateWriter,
        Field<Ktx2SupercompressionScheme>.Select("compression", "Supercompression",
            "Compress the stored texture data without changing its pixel values.", value => value, (_, value) => value,
            ("none", "None", Ktx2SupercompressionScheme.None), ("zlib", "Zlib", Ktx2SupercompressionScheme.Zlib)));

    public static Format Dds { get; } = new(new DdsCodec());
    public static Format Hdr { get; } = new(new HdrCodec());

    public static IReadOnlyList<Format> All { get; } = Array.AsReadOnly<Format>([Png, Jpeg, Webp, Exr, Ktx2, Dds, Hdr]);

    private static readonly Dictionary<string, Format> s_ByName = All
        .SelectMany(format => format.Extensions.Append(format.Id).Select(name => KeyValuePair.Create(name, format)))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    public static Format Resolve(string name) => s_ByName.TryGetValue(name, out var format) || s_ByName.TryGetValue("." + name, out format)
        ? format : throw new NotSupportedException($"No format registered for '{name}'.");
}
