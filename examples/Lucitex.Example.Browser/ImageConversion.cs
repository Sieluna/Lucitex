using System.Runtime.InteropServices.JavaScript;
using Lucitex.Conversion;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Dds;
using Lucitex.Exr;
using Lucitex.Hdr;
using Lucitex.Ktx2;
using Lucitex.Png;

namespace Lucitex.Example.Browser;

public static partial class ImageConversion
{
    private static readonly IImageCodec[] s_Codecs = [new PngCodec(), new DdsCodec(), new ExrCodec(), new HdrCodec(), new Ktx2Codec()];

    [JSExport]
    public static byte[] Convert(byte[] source, string sourceExtension, string targetExtension)
    {
        var sourceCodec = ResolveCodec(sourceExtension);
        var targetCodec = ResolveCodec(targetExtension);

        using var sourceStream = new MemoryStream(source, writable: false);
        using var reader = sourceCodec.OpenReader(sourceStream);
        var sourceDescriptor = reader.Describe();

        var planResult = ConversionPlanner.Plan(sourceDescriptor, targetCodec.Capabilities, ConversionPolicy.Preview);
        if (!planResult.Success) {
            var reasons = string.Join('\n', planResult.Diagnostics.Select(d => $"[{d.Category}] {d.Message}"));
            throw new NotSupportedException($"cannot convert '{sourceCodec.FormatId}' to '{targetCodec.FormatId}':\n{reasons}");
        }

        var plan = planResult.Plan!;
        using var targetStream = new MemoryStream();
        using var writer = targetCodec.CreateWriter(targetStream, plan.TargetDescriptor);
        ConversionExecutor.Execute(plan, reader, sourceCodec.Capabilities.SampleByteOrder, writer, targetCodec.Capabilities.SampleByteOrder);
        return targetStream.ToArray();
    }

    private static IImageCodec ResolveCodec(string extension)
    {
        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        var codec = s_Codecs.FirstOrDefault(c => c.Extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase));
        if (codec is null) {
            throw new NotSupportedException($"no codec registered for extension '{normalized}'");
        }

        return codec;
    }
}
