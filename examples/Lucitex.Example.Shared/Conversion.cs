using Lucitex.Conversion;

namespace Lucitex.Example.Shared;

public readonly struct Conversion
{
    private readonly Format _source;
    private readonly Stream _input;
    private readonly Format _target;
    private readonly ConversionPolicy _policy;

    internal Conversion(Format source, Stream input, Format target, ConversionPolicy policy)
    {
        _source = source;
        _input = input;
        _target = target;
        _policy = policy;
    }

    public ConversionPlan WriteTo(Stream output)
    {
        using var reader = _source.Codec.OpenReader(_input);
        var result = ConversionPlanner.Plan(reader.Describe(), _target.Codec.Capabilities, _policy);
        if (!result.Success) {
            var reasons = string.Join('\n', result.Diagnostics.Select(diagnostic => $"[{diagnostic.Category}] {diagnostic.Message}"));
            throw new NotSupportedException($"Cannot convert '{_source.Id}' to '{_target.Id}':\n{reasons}");
        }
        var plan = result.Plan!;
        using var writer = _target.OpenWriter(output, plan.TargetDescriptor);
        ConversionExecutor.Execute(plan, reader, _source.Codec.Capabilities.SampleByteOrder, writer, _target.Codec.Capabilities.SampleByteOrder);
        return plan;
    }
}
