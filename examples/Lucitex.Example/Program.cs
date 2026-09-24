using Lucitex.Conversion;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Dds;
using Lucitex.Exr;
using Lucitex.Hdr;
using Lucitex.Jpeg;
using Lucitex.Ktx2;
using Lucitex.Png;
using Lucitex.Webp;

if (args.Length != 2) {
    Console.Error.WriteLine("usage: Lucitex.Example <source-image> <target-image>");
    return 1;
}

var sourcePath = args[0];
var targetPath = args[1];

IImageCodec[] codecs = [new PngCodec(), new JpegCodec(), new DdsCodec(), new ExrCodec(), new HdrCodec(), new Ktx2Codec(), new WebpCodec()];

try {
    var sourceCodec = ResolveCodec(sourcePath);
    var targetCodec = ResolveCodec(targetPath);

    using var sourceStream = File.OpenRead(sourcePath);
    using var reader = sourceCodec.OpenReader(sourceStream);
    var sourceDescriptor = reader.Describe();

    var planResult = ConversionPlanner.Plan(sourceDescriptor, targetCodec.Capabilities, ConversionPolicy.Preview);
    if (!planResult.Success) {
        Console.Error.WriteLine($"cannot convert '{sourceCodec.FormatId}' to '{targetCodec.FormatId}':");
        foreach (var diagnostic in planResult.Diagnostics) {
            Console.Error.WriteLine($"  [{diagnostic.Category}] {diagnostic.Message}");
        }

        return 1;
    }

    var plan = planResult.Plan!;

    using (var targetStream = File.Create(targetPath)) {
        using var writer = targetCodec.CreateWriter(targetStream, plan.TargetDescriptor);
        ConversionExecutor.Execute(plan, reader, sourceCodec.Capabilities.SampleByteOrder, writer, targetCodec.Capabilities.SampleByteOrder);
    }

    var extent = sourceDescriptor.Parts[0].Topology.BaseExtent;
    Console.WriteLine($"{sourceCodec.FormatId} -> {targetCodec.FormatId}: {extent.Width}x{extent.Height} ({plan.Policy}, plan {plan.PlanFingerprint[..12]})");

    foreach (var diagnostic in plan.Diagnostics) {
        Console.WriteLine($"  [{diagnostic.Category}] {diagnostic.Message}");
    }

    return 0;
}
catch (Exception ex) when (ex is NotSupportedException or IOException) {
    Console.Error.WriteLine(ex.Message);
    return 1;
}

IImageCodec ResolveCodec(string path)
{
    var extension = Path.GetExtension(path);
    var codec = codecs.FirstOrDefault(c => c.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    if (codec is null) {
        throw new NotSupportedException($"no codec registered for extension '{extension}'");
    }

    return codec;
}
