using Lucitex.Core.Diagnostics;
using Lucitex.Core.Execution;

namespace Lucitex.Tests.Core.Execution;

public class ErrorModelTests
{
    [Fact]
    public void RepresentationResult_Ok_CarriesValueAndNoDiagnostics()
    {
        var result = RepresentationResult<int>.Ok(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void RepresentationResult_Unsupported_CarriesDiagnosticsAndNoValue()
    {
        var diagnostic = new UnsupportedFeatureDiagnostic { Feature = "Exr.Compression.Dwaa" };

        var result = RepresentationResult<int>.Unsupported(diagnostic);

        Assert.False(result.Success);
        Assert.Equal(0, result.Value);
        Assert.Single(result.Diagnostics);
        Assert.Equal("Exr.Compression.Dwaa", result.Diagnostics[0].Feature);
    }

    [Fact]
    public void ImageFormatException_CarriesStructuredContext()
    {
        var exception = new ImageFormatException(
            format: "exr",
            code: "BadChunkOffset",
            message: "Chunk offset points outside the file.",
            offset: 128,
            context: "part=0 level=(0,0,0)");

        Assert.Equal("exr", exception.Format);
        Assert.Equal("BadChunkOffset", exception.Code);
        Assert.Equal(128, exception.Offset);
        Assert.Equal("part=0 level=(0,0,0)", exception.Context);
    }
}
