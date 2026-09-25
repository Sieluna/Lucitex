using System.Diagnostics;
using System.Globalization;
using Lucitex.Example.Shared;

try {
    if (args.Length == 0 || args[0] is "--help" or "-h") {
        PrintHelp(args.Length > 1 ? Formats.Resolve(args[1]) : null);
        return 0;
    }
    if (args.Length < 2) {
        throw new ArgumentException("Expected a source image and a target image. Use --help to see available options.");
    }

    var sourcePath = Path.GetFullPath(args[0]);
    var targetPath = Path.GetFullPath(args[1]);
    if (string.Equals(sourcePath, targetPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
        throw new ArgumentException("Source and target must be different files.");
    }
    var sourceFormat = Formats.Resolve(Path.GetExtension(sourcePath));
    var targetFormat = Formats.Resolve(Path.GetExtension(targetPath));

    (int Width, int Height)? resize = null;
    (int X, int Y, int Width, int Height)? crop = null;
    long? targetSizeBytes = null;
    var codecParameters = new List<KeyValuePair<string, string>>();
    foreach (var (id, value) in ReadParameters(args)) {
        switch (id) {
            case "resize":
                resize = ParseResize(value);
                break;
            case "crop":
                crop = ParseCrop(value);
                break;
            case "target-size":
                targetSizeBytes = ParseByteSize(value);
                break;
            default:
                codecParameters.Add(KeyValuePair.Create(id, value));
                break;
        }
    }
    targetFormat = targetFormat.Parse(codecParameters);

    var temporaryPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $".lucitex-{Guid.NewGuid():N}.tmp");
    try {
        var stopwatch = Stopwatch.StartNew();
        var sourceBytes = File.ReadAllBytes(sourcePath);
        long outputBytes;
        int? achievedQuality = null;
        var reachedTarget = true;
        Lucitex.Conversion.ConversionPlan plan;

        if (targetSizeBytes is { } target) {
            if (!TargetSizeCompressor.SupportsTargetSize(targetFormat)) {
                throw new ArgumentException($"'{targetFormat.Id}' has no adjustable quality, so --target-size has nothing to search over.");
            }

            var result = TargetSizeCompressor.Compress(sourceFormat, sourceBytes, targetFormat, target, resize: resize, crop: crop);
            File.WriteAllBytes(temporaryPath, result.Bytes);
            outputBytes = result.Bytes.LongLength;
            achievedQuality = result.Quality;
            reachedTarget = result.ReachedTarget;
            plan = result.Plan;
            targetFormat = targetFormat.Parse([new("quality", result.Quality.ToString(CultureInfo.InvariantCulture))]);
        }
        else {
            using var source = new MemoryStream(sourceBytes, writable: false);
            var conversion = targetFormat.From(sourceFormat, source);
            if (crop is { } region) {
                conversion = conversion.Crop(region.X, region.Y, region.Width, region.Height);
            }
            if (resize is { } size) {
                conversion = conversion.Resize(size.Width, size.Height);
            }

            using var target2 = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write);
            plan = conversion.WriteTo(target2);
            outputBytes = target2.Length;
        }

        File.Move(temporaryPath, targetPath, overwrite: true);
        stopwatch.Stop();

        var extent = plan.TargetDescriptor.Parts[0].Topology.BaseExtent;

        Console.WriteLine($"{sourceFormat.Id} -> {targetFormat.Id}: {extent.Width}x{extent.Height}");
        Console.WriteLine($"{sourceBytes.LongLength:N0} -> {outputBytes:N0} bytes; output/input: {(double)outputBytes / sourceBytes.LongLength:P1}; {stopwatch.Elapsed.TotalMilliseconds:N0} ms");
        if (achievedQuality is { } quality) {
            Console.WriteLine(reachedTarget
                ? $"  target-size={targetSizeBytes:N0}B reached at quality={quality}"
                : $"  target-size={targetSizeBytes:N0}B NOT reached even at the lowest quality ({quality}); this is the smallest this encoder can produce");
        }

        foreach (var parameter in targetFormat.Parameters) {
            Console.WriteLine($"  {parameter.Id}={parameter.DefaultValue}");
        }
    }
    finally {
        if (File.Exists(temporaryPath)) {
            File.Delete(temporaryPath);
        }
    }
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) {
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static (int Width, int Height) ParseResize(string value)
{
    var separator = value.IndexOfAny(['x', 'X']);
    if (separator <= 0 || separator == value.Length - 1 ||
        !int.TryParse(value[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
        !int.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
        width <= 0 || height <= 0) {
        throw new ArgumentException($"'--resize {value}' must look like '128x128' (positive integer width and height).");
    }

    return (width, height);
}

static (int X, int Y, int Width, int Height) ParseCrop(string value)
{
    var parts = value.Split(',');
    if (parts.Length == 4 &&
        int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
        int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
        int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) &&
        x >= 0 && y >= 0 && width > 0 && height > 0) {
        return (x, y, width, height);
    }

    throw new ArgumentException($"'--crop {value}' must look like 'x,y,width,height' (e.g. '10,10,200,150').");
}

static long ParseByteSize(string value)
{
    var trimmed = value.Trim();
    var multiplier = 1L;
    if (trimmed.EndsWith("KB", StringComparison.OrdinalIgnoreCase)) {
        multiplier = 1024;
        trimmed = trimmed[..^2];
    }
    else if (trimmed.EndsWith('K') || trimmed.EndsWith('k')) {
        multiplier = 1024;
        trimmed = trimmed[..^1];
    }
    else if (trimmed.EndsWith("MB", StringComparison.OrdinalIgnoreCase)) {
        multiplier = 1024 * 1024;
        trimmed = trimmed[..^2];
    }
    else if (trimmed.EndsWith('M') || trimmed.EndsWith('m')) {
        multiplier = 1024 * 1024;
        trimmed = trimmed[..^1];
    }
    else if (trimmed.EndsWith("B", StringComparison.OrdinalIgnoreCase)) {
        trimmed = trimmed[..^1];
    }

    if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0) {
        throw new ArgumentException($"'--target-size {value}' must look like '10K', '2MB' or a raw byte count.");
    }

    return (long)(amount * multiplier);
}

static void PrintHelp(Format? selected)
{
    Console.WriteLine("usage: Lucitex.Example <source-image> <target-image> [--option value ...]");
    Console.WriteLine("       Lucitex.Example --help [format]");
    Console.WriteLine("Options apply to the target encoder. Both --option value and --option=value are supported.");
    Console.WriteLine();
    Console.WriteLine("  --crop <x,y,w,h>      Crop to a pixel rectangle before any resize, e.g. --crop 10,10,200,150");
    Console.WriteLine("  --resize <WxH>        Resize to exact pixel dimensions before encoding, e.g. --resize 128x128");
    Console.WriteLine("  --target-size <size>  Binary-search 'quality' to fit this budget, e.g. --target-size 10K or 2MB");
    Console.WriteLine("                        Only formats with a 'quality' option (JPEG, lossy WebP) support this.");
    var codecs = selected is null ? Formats.All : [selected];
    foreach (var codec in codecs) {
        Console.WriteLine($"\n{codec.Id} ({string.Join(", ", codec.Extensions)}):");
        var options = codec.Parameters;
        if (options.Count == 0) {
            Console.WriteLine("  This encoder has no adjustable options.");
        }
        foreach (var option in options) {
            var values = option.Kind switch {
                ParameterKind.Integer => $"{option.Minimum}..{option.Maximum}",
                ParameterKind.Boolean => "true|false",
                _ => string.Join('|', option.Choices!.Select(choice => choice.Value)),
            };
            Console.WriteLine($"  --{option.Id} <{values}> (default: {option.DefaultValue})");
            Console.WriteLine($"    {option.Description}");
        }
    }
}

static IEnumerable<KeyValuePair<string, string>> ReadParameters(string[] arguments)
{
    for (var i = 2; i < arguments.Length; i++) {
        var argument = arguments[i];
        if (!argument.StartsWith("--", StringComparison.Ordinal) || argument.Length == 2) {
            throw new ArgumentException($"Expected --parameter value, received '{argument}'.");
        }
        var separator = argument.IndexOf('=');
        if (separator >= 0) {
            yield return KeyValuePair.Create(argument[2..separator], argument[(separator + 1)..]);
        }
        else {
            if (++i >= arguments.Length || arguments[i].StartsWith("--", StringComparison.Ordinal)) {
                throw new ArgumentException($"Parameter '{argument[2..]}' requires a value.");
            }
            yield return KeyValuePair.Create(argument[2..], arguments[i]);
        }
    }
}
