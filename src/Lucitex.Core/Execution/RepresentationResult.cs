using Lucitex.Core.Diagnostics;

namespace Lucitex.Core.Execution;

public sealed record RepresentationResult<T>
{
    public required bool Success { get; init; }

    public T? Value { get; init; }

    public IReadOnlyList<UnsupportedFeatureDiagnostic> Diagnostics { get; init; } = [];

    public static RepresentationResult<T> Ok(T value) => new() { Success = true, Value = value };

    public static RepresentationResult<T> Unsupported(params UnsupportedFeatureDiagnostic[] diagnostics) =>
        new() { Success = false, Diagnostics = diagnostics };
}
