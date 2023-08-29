using Lucitex.Core.Color;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;

namespace Lucitex.Conversion;

public static class ConversionPlanner
{
    public static ConversionPlanResult Plan(ImageAssetDescriptor source, CodecCapabilities targetCapabilities, ConversionPolicy policy)
    {
        var diagnostics = new List<LossDiagnostic>();
        var parts = new List<PartConversionPlan>();
        var targetParts = new List<ImagePartDescriptor>();

        var sourceParts = targetCapabilities.SupportsMultiplePartsPerAsset ? source.Parts : source.Parts.Take(1).ToList();
        if (sourceParts.Count < source.Parts.Count) {
            diagnostics.Add(new LossDiagnostic {
                Category = LossCategory.Topology,
                Message = $"Target format supports a single part per asset; dropping {source.Parts.Count - sourceParts.Count} additional part(s).",
            });
        }

        for (var partIndex = 0; partIndex < sourceParts.Count; partIndex++) {
            var sourcePart = sourceParts[partIndex];
            var steps = new List<ConversionStep>();

            if (sourceParts.Count < source.Parts.Count) {
                steps.Add(new SelectPartStep(partIndex));
            }

            if (sourcePart.Representation is not PlainSampleRepresentation plain) {
                return ConversionPlanResult.Failure([
                    new LossDiagnostic {
                        Category = LossCategory.Transcode,
                        Message = $"Part {partIndex} uses {sourcePart.Representation.GetType().Name}, which this planner does not handle yet.",
                    },
                ]);
            }

            var (channelSteps, resultChannels, channelDiagnostics) = PlanChannels(sourcePart.Channels, targetCapabilities, policy);
            if (channelDiagnostics.Count > 0 && !AllowsLoss(policy, channelDiagnostics)) {
                return ConversionPlanResult.Failure(channelDiagnostics);
            }

            steps.AddRange(channelSteps);
            diagnostics.AddRange(channelDiagnostics);

            var (sampleSteps, resultChannelSchema, sampleDiagnostics) = PlanSampleTypes(resultChannels, targetCapabilities);
            if (sampleDiagnostics.Count > 0 && !AllowsLoss(policy, sampleDiagnostics)) {
                return ConversionPlanResult.Failure(sampleDiagnostics);
            }

            steps.AddRange(sampleSteps);
            diagnostics.AddRange(sampleDiagnostics);

            var (alphaSteps, alphaDiagnostics) = PlanAlpha(sourcePart.Alpha);
            steps.AddRange(alphaSteps);
            diagnostics.AddRange(alphaDiagnostics);

            var (orientationSteps, orientationDiagnostics) = PlanOrientation(sourcePart.Spatial.Orientation, targetCapabilities);
            if (orientationDiagnostics.Count > 0 && !AllowsLoss(policy, orientationDiagnostics)) {
                return ConversionPlanResult.Failure(orientationDiagnostics);
            }

            steps.AddRange(orientationSteps);
            diagnostics.AddRange(orientationDiagnostics);

            var (metadataSteps, metadataDiagnostics) = PlanMetadata(sourcePart.Metadata, policy);
            steps.AddRange(metadataSteps);
            diagnostics.AddRange(metadataDiagnostics);

            parts.Add(new PartConversionPlan { SourcePartIndex = partIndex, Steps = steps });

            targetParts.Add(sourcePart with {
                Channels = resultChannelSchema,
                Representation = new PlainSampleRepresentation {
                    Planes = [new() { Channels = resultChannelSchema.Channels.Select(c => c.Name).ToList(), Extent = plain.Planes[0].Extent }],
                },
                Spatial = sourcePart.Spatial with { Orientation = targetCapabilities.SupportsOrientationMetadata ? sourcePart.Spatial.Orientation : Lucitex.Core.Spatial.LogicalOrientation.Identity },
                Metadata = Lucitex.Core.Metadata.MetadataCollection.Empty,
            });
        }

        if (policy == ConversionPolicy.Strict && diagnostics.Count > 0) {
            return ConversionPlanResult.Failure(diagnostics);
        }

        var plan = new ConversionPlan {
            SourceDescriptor = source,
            TargetDescriptor = source with { Parts = targetParts },
            Parts = parts,
            Diagnostics = diagnostics,
            Policy = policy,
        };

        return ConversionPlanResult.Ok(plan);
    }

    private static bool AllowsLoss(ConversionPolicy policy, IReadOnlyList<LossDiagnostic> diagnostics) => policy switch {
        ConversionPolicy.Strict => diagnostics.Count == 0,
        ConversionPolicy.Preserve => diagnostics.All(d => !d.IsSemanticLoss),
        ConversionPolicy.Preview or ConversionPolicy.Explicit => true,
        _ => false,
    };

    private static (IReadOnlyList<ConversionStep> Steps, ChannelSchema Channels, IReadOnlyList<LossDiagnostic> Diagnostics) PlanChannels(
        ChannelSchema sourceChannels, CodecCapabilities targetCapabilities, ConversionPolicy policy)
    {
        if (targetCapabilities.SupportsArbitraryChannelNames && sourceChannels.Channels.Count <= targetCapabilities.MaxChannelsPerPart) {
            return ([], sourceChannels, []);
        }

        // Fixed-channel-name targets (PNG, DDS, KTX2) all pattern-match on a specific channel order
        // (e.g. exactly ["R","G","B","A"]), not just a set of names - so channels have to be
        // canonicalized into that order, not merely filtered down to recognized names. EXR in
        // particular reports channels in the file's alphabetical order (A,B,G,R), which is why this
        // reordering is required even when nothing is actually being dropped.
        var canonicalOrder = new[] { "R", "G", "B", "A", "Y" };
        var kept = sourceChannels.Channels
            .Where(c => canonicalOrder.Contains(c.Name.FullName))
            .OrderBy(c => Array.IndexOf(canonicalOrder, c.Name.FullName))
            .Take(targetCapabilities.MaxChannelsPerPart)
            .ToList();

        if (kept.Count == 0) {
            return ([], sourceChannels, [
                new LossDiagnostic { Category = LossCategory.Channel, Message = "No channel in this part maps to a channel name the target format recognizes." },
            ]);
        }

        var dropped = sourceChannels.Channels.Except(kept).Select(c => c.Name.FullName).ToList();
        var diagnostics = new List<LossDiagnostic>();
        if (dropped.Count > 0) {
            diagnostics.Add(new LossDiagnostic {
                Category = LossCategory.Channel,
                Message = $"Dropping channel(s) not representable by the target format: {string.Join(", ", dropped)}.",
            });
        }

        var reordered = !kept.Select(c => c.Name.FullName).SequenceEqual(sourceChannels.Channels.Select(c => c.Name.FullName));
        var keptSchema = new ChannelSchema { Channels = kept };
        var steps = new List<ConversionStep>();
        if (dropped.Count > 0 || reordered) {
            steps.Add(new SelectChannelsStep(kept.Select(c => c.Name).ToList()));
        }

        return (steps, keptSchema, diagnostics);
    }

    private static (IReadOnlyList<ConversionStep> Steps, ChannelSchema Channels, IReadOnlyList<LossDiagnostic> Diagnostics) PlanSampleTypes(
        ChannelSchema channels, CodecCapabilities targetCapabilities)
    {
        var steps = new List<ConversionStep>();
        var diagnostics = new List<LossDiagnostic>();
        var resultChannels = new List<ChannelDescriptor>();

        foreach (var channel in channels.Channels) {
            if (targetCapabilities.SupportsSampleType(channel.SampleType)) {
                resultChannels.Add(channel);
                continue;
            }

            var target = ChooseReplacementSampleType(channel.SampleType, targetCapabilities);
            steps.Add(new ConvertSampleTypeStep(channel.Name, channel.SampleType, target));
            diagnostics.Add(new LossDiagnostic {
                Category = IsNarrowing(channel.SampleType, target) ? LossCategory.DynamicRange : LossCategory.Numeric,
                Message = $"Channel '{channel.Name}' converts from {channel.SampleType} to {target}.",
                ChannelName = channel.Name.FullName,
            });

            resultChannels.Add(channel with { SampleType = target });
        }

        return (steps, new ChannelSchema { Channels = resultChannels }, diagnostics);
    }

    private static SampleType ChooseReplacementSampleType(SampleType source, CodecCapabilities targetCapabilities)
    {
        if (source.Kind == ScalarKind.Float && targetCapabilities.SupportsSampleType(SampleType.UNorm16)) {
            return SampleType.UNorm16;
        }

        if (targetCapabilities.SupportsSampleType(SampleType.UNorm8)) {
            return SampleType.UNorm8;
        }

        if (targetCapabilities.SupportsSampleType(SampleType.Float32)) {
            return SampleType.Float32;
        }

        return targetCapabilities.SupportedSampleTypes[0];
    }

    private static bool IsNarrowing(SampleType from, SampleType to) => from.Kind == ScalarKind.Float && to.Kind != ScalarKind.Float;

    private static (IReadOnlyList<ConversionStep> Steps, IReadOnlyList<LossDiagnostic> Diagnostics) PlanAlpha(AlphaDescriptor? sourceAlpha)
    {
        var mode = sourceAlpha?.Mode ?? AlphaMode.Straight;
        if (mode != AlphaMode.Premultiplied) {
            return ([], []);
        }

        return ([new UnpremultiplyAlphaStep()], []);
    }

    private static (IReadOnlyList<ConversionStep> Steps, IReadOnlyList<LossDiagnostic> Diagnostics) PlanOrientation(
        Lucitex.Core.Spatial.LogicalOrientation orientation, CodecCapabilities targetCapabilities)
    {
        if (targetCapabilities.SupportsOrientationMetadata || orientation.Equals(Lucitex.Core.Spatial.LogicalOrientation.Identity)) {
            return ([], []);
        }

        return (
            [new ApplyOrientationStep(orientation, Lucitex.Core.Spatial.LogicalOrientation.Identity)],
            [new LossDiagnostic { Category = LossCategory.Orientation, Message = "Target format cannot store orientation metadata; pixels are physically reoriented instead.", IsSemanticLoss = false }]);
    }

    private static (IReadOnlyList<ConversionStep> Steps, IReadOnlyList<LossDiagnostic> Diagnostics) PlanMetadata(
        Lucitex.Core.Metadata.MetadataCollection metadata, ConversionPolicy policy)
    {
        if (metadata.Entries.Count == 0) {
            return ([], []);
        }

        var namespaces = metadata.Entries.Select(e => e.Namespace).Distinct();
        var steps = new List<ConversionStep>();
        var diagnostics = new List<LossDiagnostic>();

        foreach (var ns in namespaces) {
            steps.Add(new DropMetadataStep(ns, "No metadata mapping is defined between formats yet."));
            diagnostics.Add(new LossDiagnostic {
                Category = LossCategory.Metadata,
                Message = $"Metadata namespace '{ns}' has no mapping to the target format and is dropped.",
            });
        }

        return (steps, diagnostics);
    }
}
