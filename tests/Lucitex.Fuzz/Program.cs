using System.Diagnostics;
using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Metadata;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Exr;
using Lucitex.Exr.Format;
using Lucitex.Ktx2;
using Lucitex.Png;

return FuzzApplication.Run(args);

internal static class FuzzApplication
{
    private static readonly DecodeLimits s_Limits = new() {
        MaxDimensions = 512,
        MaxPixels = 512 * 512,
        MaxParts = 8,
        MaxChannels = 32,
        MaxLevels = 16,
        MaxMetadataBytes = 1024 * 1024,
        MaxDecodedBytes = 16 * 1024 * 1024,
        MaxWorkingSet = 32 * 1024 * 1024,
        MaxCompressionRatio = 100,
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help") {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        return args[0] switch {
            "run" => RunFuzz(args[1..]),
            "replay" => Replay(args[1..]),
            "generate" => Generate(args[1..]),
            _ => UsageError($"Unknown command '{args[0]}'."),
        };
    }

    private static int RunFuzz(string[] args)
    {
        var iterations = ReadIntOption(args, "--iterations", 10_000);
        var randomSeed = ReadIntOption(args, "--seed", 0x5EED);
        var oracle = ReadStringOption(args, "--oracle");
        var artifacts = ReadStringOption(args, "--artifacts") ?? Path.Combine(AppContext.BaseDirectory, "fuzz-artifacts");

        if (iterations <= 0) {
            return UsageError("--iterations must be positive.");
        }

        var seeds = SeedCorpus.Create();
        var random = new Random(randomSeed);
        var crashes = 0;
        var disagreements = 0;
        var oracleChecks = 0;

        if (oracle is not null) {
            foreach (var seed in seeds) {
                if (!NativeOracle.Accepts(oracle, seed.Format, seed.Bytes)) {
                    Console.Error.WriteLine($"Oracle rejected generated seed '{seed.Name}'.");
                    return 1;
                }
            }
        }

        for (var iteration = 0; iteration < iterations; iteration++) {
            var seed = seeds[random.Next(seeds.Count)];
            var candidate = Mutator.Mutate(seed.Bytes, random);
            var managed = ManagedDecoder.Decode(seed.Format, candidate);

            if (managed.Crash is not null) {
                crashes++;
                var path = SaveArtifact(artifacts, seed.Format, randomSeed, iteration, candidate);
                Console.Error.WriteLine($"Crash {managed.Crash.GetType().Name}: {managed.Crash.Message} ({path})");
                continue;
            }

            if (oracle is null) {
                continue;
            }

            var nativeAccepted = NativeOracle.Accepts(oracle, seed.Format, candidate);
            oracleChecks++;
            if (nativeAccepted == managed.Accepted) {
                continue;
            }

            disagreements++;
            var disagreementPath = SaveArtifact(artifacts, seed.Format, randomSeed, iteration, candidate);
            Console.Error.WriteLine($"Oracle disagreement managed={managed.Accepted} native={nativeAccepted} ({disagreementPath})");
        }

        Console.WriteLine($"iterations={iterations} seed={randomSeed} crashes={crashes} oracleChecks={oracleChecks} disagreements={disagreements}");
        return crashes == 0 && disagreements == 0 ? 0 : 1;
    }

    private static int Replay(string[] args)
    {
        if (args.Length != 2 || !ImageFormatExtensions.TryParse(args[0], out var format)) {
            return UsageError("replay requires: <png|exr|ktx2> <path>.");
        }

        var result = ManagedDecoder.Decode(format, File.ReadAllBytes(args[1]));
        if (result.Crash is not null) {
            Console.Error.WriteLine(result.Crash);
            return 1;
        }

        Console.WriteLine(result.Accepted ? "accepted" : "rejected");
        return 0;
    }

    private static int Generate(string[] args)
    {
        if (args.Length != 1) {
            return UsageError("generate requires: <directory>.");
        }

        Directory.CreateDirectory(args[0]);
        foreach (var seed in SeedCorpus.Create()) {
            File.WriteAllBytes(Path.Combine(args[0], seed.Name), seed.Bytes);
        }

        return 0;
    }

    private static string SaveArtifact(string directory, ImageFormat format, int seed, int iteration, byte[] data)
    {
        Directory.CreateDirectory(directory);
        var extension = format.Extension();
        var path = Path.Combine(directory, $"{extension}-{seed:x8}-{iteration:D8}.{extension}");
        File.WriteAllBytes(path, data);
        return path;
    }

    private static int ReadIntOption(string[] args, string name, int fallback)
    {
        var value = ReadStringOption(args, name);
        return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadStringOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) {
            return null;
        }

        if (index + 1 >= args.Length) {
            throw new ArgumentException($"Missing value for {name}.");
        }

        return args[index + 1];
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Lucitex.Fuzz run [--iterations N] [--seed N] [--oracle PATH] [--artifacts DIR]");
        Console.WriteLine("Lucitex.Fuzz replay <png|exr|ktx2> <path>");
        Console.WriteLine("Lucitex.Fuzz generate <directory>");
    }

    internal static DecodeLimits DecoderLimits => s_Limits;
}

internal enum ImageFormat
{
    Png,
    Exr,
    Ktx2,
}

internal static class ImageFormatExtensions
{
    public static bool TryParse(string value, out ImageFormat format)
    {
        if (string.Equals(value, "png", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Png;
            return true;
        }

        if (string.Equals(value, "exr", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Exr;
            return true;
        }

        if (string.Equals(value, "ktx2", StringComparison.OrdinalIgnoreCase)) {
            format = ImageFormat.Ktx2;
            return true;
        }

        format = default;
        return false;
    }

    public static string Extension(this ImageFormat format) => format switch {
        ImageFormat.Png => "png",
        ImageFormat.Exr => "exr",
        ImageFormat.Ktx2 => "ktx2",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}

internal readonly record struct DecodeOutcome(bool Accepted, Exception? Crash);

internal static class ManagedDecoder
{
    public static DecodeOutcome Decode(ImageFormat format, byte[] data)
    {
        try {
            using var stream = new MemoryStream(data, writable: false);
            var codec = CreateCodec(format);
            var reader = codec.OpenReader(stream, FuzzApplication.DecoderLimits);
            var descriptor = reader.Describe();

            for (var partIndex = 0; partIndex < descriptor.Parts.Count; partIndex++) {
                var part = descriptor.Parts[partIndex];
                var byteCount = ComputeByteCount(part);
                var buffer = new byte[byteCount];
                var region = new WorkRegion {
                    Subresource = new SubresourceId(partIndex, 0, 0, LevelKey.Base),
                    Region = part.Spatial.DataWindow,
                };
                var written = reader.Read(region, buffer);
                if (written != byteCount) {
                    throw new InvalidOperationException($"Reader returned {written} bytes; expected {byteCount}.");
                }
            }

            return new DecodeOutcome(true, null);
        }
        catch (ImageFormatException) {
            return new DecodeOutcome(false, null);
        }
        catch (NotSupportedException) {
            return new DecodeOutcome(false, null);
        }
        catch (Exception exception) {
            return new DecodeOutcome(false, exception);
        }
    }

    private static IImageCodec CreateCodec(ImageFormat format) => format switch {
        ImageFormat.Png => new PngCodec(),
        ImageFormat.Exr => new ExrCodec(),
        ImageFormat.Ktx2 => new Ktx2Codec(),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static int ComputeByteCount(ImagePartDescriptor part)
    {
        var rowBits = part.Representation switch {
            IndexedRepresentation indexed => checked(part.Spatial.DataWindow.Width * indexed.IndexType.Bits),
            PlainSampleRepresentation => checked(part.Spatial.DataWindow.Width * part.Channels.Channels.Sum(channel => channel.SampleType.Bits)),
            _ => throw new NotSupportedException($"Fuzz decoder does not support {part.Representation.GetType().Name}."),
        };
        var rowBytes = checked((rowBits + 7) / 8);
        return checked((int)(rowBytes * part.Spatial.DataWindow.Height));
    }
}

internal readonly record struct SeedInput(string Name, ImageFormat Format, byte[] Bytes);

internal static class SeedCorpus
{
    public static IReadOnlyList<SeedInput> Create()
    {
        var seeds = new List<SeedInput>
        {
            new("rgba8.png", ImageFormat.Png, WritePng(PngDescriptor(17, 13, ["R", "G", "B", "A"], SampleType.UNorm8), 17 * 13 * 4, 1)),
            new("gray1.png", ImageFormat.Png, WritePng(PngDescriptor(19, 11, ["Y"], SampleType.UNorm1), ((19 + 7) / 8) * 11, 2)),
            new("rgb16.png", ImageFormat.Png, WritePng(PngDescriptor(9, 7, ["R", "G", "B"], SampleType.UNorm16), 9 * 7 * 6, 3)),
            new("palette4.png", ImageFormat.Png, WritePng(PaletteDescriptor(), ((15 + 1) / 2) * 9, 4)),
            new("rgba-none.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.None, 5)),
            new("rgba-rle.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.Rle, 6)),
            new("rgba-zip.exr", ImageFormat.Exr, WriteExr(ExrCompressionId.Zip, 7)),
            new("rgba8.ktx2", ImageFormat.Ktx2, WriteKtx2Rgba8(12, 9, 8)),
            new("r32f.ktx2", ImageFormat.Ktx2, WriteKtx2R32Float(11, 6, 9)),
        };
        return seeds;
    }

    private static ImageAssetDescriptor PngDescriptor(int width, int height, IReadOnlyList<string> names, SampleType sampleType)
    {
        var channels = names.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = sampleType,
            Sampling = SampleGrid.Unit,
        }).ToList();
        return Asset(width, height, channels, new PlainSampleRepresentation {
            Planes =
            [
                new SamplePlaneDescriptor
                {
                    Channels = names.Select(name => (ChannelPath)name).ToList(),
                    Extent = new Extent3L(width, height, 1),
                    Layout = PlaneLayout.Interleaved,
                },
            ],
        });
    }

    private static ImageAssetDescriptor PaletteDescriptor()
    {
        var descriptor = Asset(15, 9,
        [
            new ChannelDescriptor { Name = "Index", SampleType = SampleType.UNorm4, Sampling = SampleGrid.Unit },
        ],
        new IndexedRepresentation {
            IndexType = SampleType.UNorm4,
            Palette = new PaletteDescriptor {
                EntryCount = 16,
                EntryChannels = new ChannelSchema {
                    Channels =
                    [
                        new ChannelDescriptor { Name = "R", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
                        new ChannelDescriptor { Name = "G", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
                        new ChannelDescriptor { Name = "B", SampleType = SampleType.UNorm8, Sampling = SampleGrid.Unit },
                    ],
                },
                EntrySampleType = SampleType.UNorm8,
            },
        });

        var palette = Enumerable.Range(0, 16)
            .SelectMany(value => new[] { (byte)(value * 17), (byte)(255 - (value * 17)), (byte)(value * 7) })
            .ToArray();
        var part = descriptor.Parts[0] with {
            Metadata = new MetadataCollection {
                Entries = [new MetadataEntry { Namespace = "png", Name = "PLTE", RawRepresentation = palette }],
            },
        };
        return descriptor with { Parts = [part] };
    }

    private static byte[] WritePng(ImageAssetDescriptor descriptor, int byteCount, int randomSeed) =>
        Write(new PngCodec(), descriptor, byteCount, randomSeed);

    private static byte[] WriteExr(ExrCompressionId compression, int randomSeed)
    {
        const int width = 13;
        const int height = 10;
        var channels = new[] { "R", "G", "B", "A" }.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = SampleType.Float16,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes = channels.Select(channel => new SamplePlaneDescriptor {
                Channels = [channel.Name],
                Extent = new Extent3L(width, height, 1),
                Layout = PlaneLayout.Planar,
            }).ToList(),
        });
        return Write(new ExrCodec(compression), descriptor, width * height * channels.Count * 2, randomSeed);
    }

    private static byte[] WriteKtx2Rgba8(int width, int height, int randomSeed)
    {
        var channels = new[] { "R", "G", "B", "A" }.Select(name => new ChannelDescriptor {
            Name = name,
            SampleType = SampleType.UNorm8,
            Sampling = SampleGrid.Unit,
        }).ToList();
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes =
            [
                new SamplePlaneDescriptor
                {
                    Channels = channels.Select(channel => (ChannelPath)channel.Name).ToList(),
                    Extent = new Extent3L(width, height, 1),
                    Layout = PlaneLayout.Interleaved,
                },
            ],
        });
        return Write(new Ktx2Codec(), descriptor, width * height * 4, randomSeed);
    }

    private static byte[] WriteKtx2R32Float(int width, int height, int randomSeed)
    {
        var channels = new List<ChannelDescriptor>
        {
            new() { Name = "R", SampleType = SampleType.Float32, Sampling = SampleGrid.Unit },
        };
        var descriptor = Asset(width, height, channels, new PlainSampleRepresentation {
            Planes = [new SamplePlaneDescriptor { Channels = ["R"], Extent = new Extent3L(width, height, 1), Layout = PlaneLayout.Interleaved }],
        });
        return Write(new Ktx2Codec(), descriptor, width * height * 4, randomSeed);
    }

    private static ImageAssetDescriptor Asset(
        int width,
        int height,
        IReadOnlyList<ChannelDescriptor> channels,
        PayloadRepresentation representation)
    {
        var window = ImageBox.FromOrigin(width, height);
        var part = new ImagePartDescriptor {
            Name = "image",
            Spatial = new SpatialDomain {
                DataWindow = window,
                DisplayWindow = window,
                Orientation = LogicalOrientation.Identity,
                Traversal = StorageTraversal.IncreasingY,
            },
            Topology = new ResourceTopology {
                SpatialDimensions = 2,
                BaseExtent = new Extent3L(width, height, 1),
                Levels = [new ResolutionLevel { Key = LevelKey.Base, Extent = new Extent3L(width, height, 1) }],
            },
            Channels = new ChannelSchema { Channels = channels },
            Representation = representation,
            Color = new ColorEncoding { Transfer = TransferFunction.Linear },
        };
        return new ImageAssetDescriptor { Parts = [part] };
    }

    private static byte[] Write(IImageCodec codec, ImageAssetDescriptor descriptor, int byteCount, int randomSeed)
    {
        var pixels = new byte[byteCount];
        new Random(randomSeed).NextBytes(pixels);
        using var stream = new MemoryStream();
        var writer = codec.CreateWriter(stream, descriptor);
        writer.Write(new WorkRegion {
            Subresource = new SubresourceId(0, 0, 0, LevelKey.Base),
            Region = descriptor.Parts[0].Spatial.DataWindow,
        }, pixels);
        writer.Finish();
        return stream.ToArray();
    }
}

internal static class Mutator
{
    public static byte[] Mutate(byte[] source, Random random)
    {
        var result = source.ToList();
        var operationCount = random.Next(1, 9);
        for (var operation = 0; operation < operationCount; operation++) {
            switch (random.Next(5)) {
                case 0 when result.Count > 0:
                    result[random.Next(result.Count)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1 when result.Count > 0:
                    result[random.Next(result.Count)] = (byte)random.Next(256);
                    break;
                case 2 when result.Count > 1:
                    var removeStart = random.Next(result.Count);
                    var removeCount = random.Next(1, Math.Min(32, result.Count - removeStart) + 1);
                    result.RemoveRange(removeStart, removeCount);
                    break;
                case 3 when result.Count < source.Length + 256:
                    result.Insert(random.Next(result.Count + 1), (byte)random.Next(256));
                    break;
                case 4 when result.Count > 0:
                    var truncateAt = random.Next(result.Count);
                    result.RemoveRange(truncateAt, result.Count - truncateAt);
                    break;
            }
        }

        return result.ToArray();
    }
}

internal static class NativeOracle
{
    public static bool Accepts(string executable, ImageFormat format, byte[] data)
    {
        var extension = format.Extension();
        var path = Path.Combine(Path.GetTempPath(), $"lucitex-oracle-{Guid.NewGuid():N}.{extension}");
        try {
            File.WriteAllBytes(path, data);
            using var process = Process.Start(new ProcessStartInfo {
                FileName = executable,
                ArgumentList = { extension, path },
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException($"Could not start native oracle '{executable}'.");

            if (!process.WaitForExit(5_000)) {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"Native oracle timed out while decoding {extension} input.");
            }

            return process.ExitCode == 0;
        }
        finally {
            File.Delete(path);
        }
    }
}
