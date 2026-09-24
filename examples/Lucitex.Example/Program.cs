using System.Diagnostics;
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
    targetFormat = targetFormat.Parse(ReadParameters(args));

    var temporaryPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $".lucitex-{Guid.NewGuid():N}.tmp");
    try {
        var stopwatch = Stopwatch.StartNew();
        using var source = File.OpenRead(sourcePath);
        var sourceBytes = source.Length;
        Lucitex.Conversion.ConversionPlan plan;
        long outputBytes;
        using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write)) {
            plan = targetFormat.From(sourceFormat, source).WriteTo(target);
            outputBytes = target.Length;
        }
        File.Move(temporaryPath, targetPath, overwrite: true);
        stopwatch.Stop();
        var extent = plan.SourceDescriptor.Parts[0].Topology.BaseExtent;
        Console.WriteLine($"{sourceFormat.Id} -> {targetFormat.Id}: {extent.Width}x{extent.Height} ({plan.Policy}, plan {plan.PlanFingerprint[..12]})");
        Console.WriteLine($"{sourceBytes:N0} -> {outputBytes:N0} bytes; output/input: {(double)outputBytes / sourceBytes:P1}; {stopwatch.Elapsed.TotalMilliseconds:N0} ms");
        foreach (var parameter in targetFormat.Parameters) {
            Console.WriteLine($"  {parameter.Id}={parameter.DefaultValue}");
        }
        foreach (var diagnostic in plan.Diagnostics) {
            Console.WriteLine($"  [{diagnostic.Category}] {diagnostic.Message}");
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

static void PrintHelp(Format? selected)
{
    Console.WriteLine("usage: Lucitex.Example <source-image> <target-image> [--option value ...]");
    Console.WriteLine("       Lucitex.Example --help [format]");
    Console.WriteLine("Options apply to the target encoder. Both --option value and --option=value are supported.");
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
