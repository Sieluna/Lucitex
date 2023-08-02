using System.Reflection;
using Lucitex.Core.Semantic;

namespace Lucitex.Tests.Core;

public class CoreHasNoCodecSpecificTypesTests
{
    private static readonly string[] ForbiddenSubstrings =
    [
        "Exr", "Png", "Dds", "Ktx", "Hdr", "Jpeg", "Jpg", "Tiff", "Tga", "Bmp", "Webp", "Gif", "Dpx", "Astc", "Etc2",
    ];

    [Fact]
    public void PublicTypes_ContainNoFormatSpecificNames()
    {
        var assembly = typeof(ImageAssetDescriptor).Assembly;

        var offenders = assembly.GetExportedTypes()
            .Where(type => ForbiddenSubstrings.Any(forbidden =>
                type.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceAnyCodecAssembly()
    {
        var assembly = typeof(ImageAssetDescriptor).Assembly;

        var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(referenced, name => name != null && name.Contains("Codecs", StringComparison.OrdinalIgnoreCase));
    }
}
