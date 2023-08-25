using System.Security.Cryptography;
using System.Text;
using Lucitex.Core.Semantic;

namespace Lucitex.Conversion;

public sealed record PartConversionPlan
{
    public required int SourcePartIndex { get; init; }

    public required IReadOnlyList<ConversionStep> Steps { get; init; }
}

public sealed record ConversionPlan
{
    public const string AlgorithmVersion = "1";

    public required ImageAssetDescriptor SourceDescriptor { get; init; }

    public required ImageAssetDescriptor TargetDescriptor { get; init; }

    public required IReadOnlyList<PartConversionPlan> Parts { get; init; }

    public required IReadOnlyList<LossDiagnostic> Diagnostics { get; init; }

    public required ConversionPolicy Policy { get; init; }

    public string PlanFingerprint => ComputeFingerprint(this);

    private static string ComputeFingerprint(ConversionPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(AlgorithmVersion).Append('|').Append(plan.Policy);

        foreach (var part in plan.Parts) {
            builder.Append("|part=").Append(part.SourcePartIndex);
            foreach (var step in part.Steps) {
                builder.Append(';').Append(step);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}

public sealed record ConversionPlanResult
{
    public required bool Success { get; init; }

    public ConversionPlan? Plan { get; init; }

    public IReadOnlyList<LossDiagnostic> Diagnostics { get; init; } = [];

    public static ConversionPlanResult Ok(ConversionPlan plan) => new() { Success = true, Plan = plan, Diagnostics = plan.Diagnostics };

    public static ConversionPlanResult Failure(IReadOnlyList<LossDiagnostic> diagnostics) => new() { Success = false, Diagnostics = diagnostics };
}
