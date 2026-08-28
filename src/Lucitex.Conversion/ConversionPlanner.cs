using Lucitex.Core.Color;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;

namespace Lucitex.Conversion;

public static class ConversionPlanner
{
    public static ConversionPlanResult Plan(
        ImageAssetDescriptor source,
        CodecCapabilities targetCapabilities,
        ConversionPolicy policy,
        EncodedFormatId? targetEncodedFormat = null)
    {
        if (targetEncodedFormat is { } requestedFormat && !targetCapabilities.SupportsEncodedFormat(requestedFormat)) {
            return ConversionPlanResult.Failure([
                new LossDiagnostic {
                    Category = LossCategory.Transcode,
                    Message = $"Target format cannot store encoded format '{requestedFormat}'.",
                },
            ]);
        }

        if (targetEncodedFormat is { } unsupportedFormat && !CanEncodeEncoded(unsupportedFormat)) {
            return ConversionPlanResult.Failure([
                new LossDiagnostic {
                    Category = LossCategory.Transcode,
                    Message = $"No encoder is available for encoded format '{unsupportedFormat}'.",
                },
            ]);
        }

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

            PlainSampleRepresentation plain;
            ChannelSchema sourceChannels;
            if (sourcePart.Representation is PlainSampleRepresentation sourcePlain) {
                plain = sourcePlain;
                sourceChannels = sourcePart.Channels;
            }
            else if (sourcePart.Representation is EncodedElementRepresentation encoded &&
                targetCapabilities.SupportsEncodedFormat(encoded.Format) &&
                (targetEncodedFormat is null || targetEncodedFormat.Value == encoded.Format) &&
                (targetCapabilities.SupportsOrientationMetadata || sourcePart.Spatial.Orientation == Lucitex.Core.Spatial.LogicalOrientation.Identity)) {
                steps.Add(new TranscodeEncodedElementsStep(encoded.Format, encoded.Format));
                var (repackMetadataSteps, repackMetadataDiagnostics) = PlanMetadata(sourcePart.Metadata, policy);
                if (repackMetadataDiagnostics.Count > 0 && !AllowsLoss(policy, repackMetadataDiagnostics)) {
                    return ConversionPlanResult.Failure(repackMetadataDiagnostics);
                }

                steps.AddRange(repackMetadataSteps);
                diagnostics.AddRange(repackMetadataDiagnostics);
                targetParts.Add(sourcePart with {
                    Spatial = sourcePart.Spatial with {
                        Orientation = targetCapabilities.SupportsOrientationMetadata
                            ? sourcePart.Spatial.Orientation
                            : Lucitex.Core.Spatial.LogicalOrientation.Identity,
                    },
                    Metadata = Lucitex.Core.Metadata.MetadataCollection.Empty,
                });
                parts.Add(new PartConversionPlan { SourcePartIndex = partIndex, Steps = steps.ToArray() });
                continue;
            }
            else if (sourcePart.Representation is EncodedElementRepresentation decodable && CanDecodeEncoded(decodable.Format)) {
                steps.Add(new DecodeEncodedElementsStep(decodable.Format));
                sourceChannels = sourcePart.Channels;
                plain = new PlainSampleRepresentation {
                    Planes =
                    [
                        new SamplePlaneDescriptor {
                            Channels = sourceChannels.Channels.Select(channel => channel.Name).ToList(),
                            Extent = sourcePart.Topology.BaseExtent,
                            Layout = PlaneLayout.Interleaved,
                        },
                    ],
                };
            }
            else if (sourcePart.Representation is IndexedRepresentation indexed) {
                if (indexed.Palette.RawEntries is null) {
                    return ConversionPlanResult.Failure([
                        new LossDiagnostic {
                            Category = LossCategory.Transcode,
                            Message = $"Part {partIndex} is indexed but its source reader did not attach the palette content, so it can't be expanded.",
                        },
                    ]);
                }

                if (indexed.IndexType.Bits % 8 != 0) {
                    return ConversionPlanResult.Failure([
                        new LossDiagnostic {
                            Category = LossCategory.Transcode,
                            Message = $"Part {partIndex} uses a {indexed.IndexType.Bits}-bit palette index, which this planner can't expand yet.",
                        },
                    ]);
                }

                steps.Add(new ExpandIndexedStep());
                sourceChannels = indexed.Palette.EntryChannels;
                plain = new PlainSampleRepresentation {
                    Planes =
                    [
                        new SamplePlaneDescriptor {
                            Channels = sourceChannels.Channels.Select(channel => channel.Name).ToList(),
                            Extent = sourcePart.Topology.BaseExtent,
                            Layout = PlaneLayout.Interleaved,
                        },
                    ],
                };
            }
            else {
                return ConversionPlanResult.Failure([
                    new LossDiagnostic {
                        Category = LossCategory.Transcode,
                        Message = $"Part {partIndex} uses {sourcePart.Representation.GetType().Name}, which this planner does not handle yet.",
                    },
                ]);
            }

            var (channelSteps, resultChannels, channelDiagnostics) = PlanChannels(sourceChannels, targetCapabilities, policy);
            if (channelDiagnostics.Count > 0 && !AllowsLoss(policy, channelDiagnostics)) {
                return ConversionPlanResult.Failure(channelDiagnostics);
            }

            steps.AddRange(channelSteps);
            diagnostics.AddRange(channelDiagnostics);

            var encodedTarget = targetEncodedFormat ?? AutoEncodedTarget(targetCapabilities);
            if (encodedTarget is { } channelTarget) {
                var encodedChannels = PlanEncodedChannels(resultChannels, channelTarget);
                if (!encodedChannels.Success) {
                    return ConversionPlanResult.Failure(encodedChannels.Diagnostics);
                }

                if (encodedChannels.Diagnostics.Count > 0 && !AllowsLoss(policy, encodedChannels.Diagnostics)) {
                    return ConversionPlanResult.Failure(encodedChannels.Diagnostics);
                }

                steps.AddRange(encodedChannels.Steps);
                diagnostics.AddRange(encodedChannels.Diagnostics);
                resultChannels = encodedChannels.Channels;
            }
            else if (targetCapabilities.SupportedChannelCounts is { } supportedChannelCounts &&
                !supportedChannelCounts.Contains(resultChannels.Channels.Count)) {
                var padded = PadChannelCount(resultChannels, supportedChannelCounts);
                if (!padded.Success) {
                    return ConversionPlanResult.Failure(padded.Diagnostics);
                }

                steps.AddRange(padded.Steps);
                resultChannels = padded.Channels;
            }

            var (sampleSteps, resultChannelSchema, sampleDiagnostics) = encodedTarget is { } sampleTarget
                ? PlanEncodedSampleTypes(resultChannels, sampleTarget)
                : PlanSampleTypes(resultChannels, targetCapabilities);
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

            PayloadRepresentation targetRepresentation;
            if (encodedTarget is { } targetFormat) {
                var compressionDiagnostic = new LossDiagnostic {
                    Category = LossCategory.Compression,
                    Message = $"Encoding to '{targetFormat}' introduces representation quantization.",
                };
                if (!AllowsLoss(policy, [compressionDiagnostic])) {
                    return ConversionPlanResult.Failure([compressionDiagnostic]);
                }

                diagnostics.Add(compressionDiagnostic);
                steps.Add(new EncodeEncodedElementsStep(targetFormat));
                targetRepresentation = EncodedRepresentation(targetFormat);
            }
            else {
                targetRepresentation = new PlainSampleRepresentation {
                    Planes = [new() { Channels = resultChannelSchema.Channels.Select(c => c.Name).ToList(), Extent = plain.Planes[0].Extent }],
                };
            }

            targetParts.Add(sourcePart with {
                Channels = resultChannelSchema,
                Representation = targetRepresentation,
                Spatial = sourcePart.Spatial with { Orientation = targetCapabilities.SupportsOrientationMetadata ? sourcePart.Spatial.Orientation : Lucitex.Core.Spatial.LogicalOrientation.Identity },
                Metadata = Lucitex.Core.Metadata.MetadataCollection.Empty,
            });
            parts.Add(new PartConversionPlan { SourcePartIndex = partIndex, Steps = steps.ToArray() });
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

    private static bool CanDecodeEncoded(EncodedFormatId format) => format.Name is
        nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1) or
        nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5) or
        nameof(EncodedFormatId.Rgbe) or nameof(EncodedFormatId.Bc1) or nameof(EncodedFormatId.Bc2) or
        nameof(EncodedFormatId.Bc3) or nameof(EncodedFormatId.Bc4) or nameof(EncodedFormatId.Bc5) or
        nameof(EncodedFormatId.Bc6H) or nameof(EncodedFormatId.Bc6HSigned) or nameof(EncodedFormatId.Bc7);

    private static bool CanEncodeEncoded(EncodedFormatId format) => format.Name is
        nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1) or
        nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5) or
        nameof(EncodedFormatId.Rgbe) or nameof(EncodedFormatId.Bc1) or nameof(EncodedFormatId.Bc2) or
        nameof(EncodedFormatId.Bc3) or nameof(EncodedFormatId.Bc4) or nameof(EncodedFormatId.Bc5);

    private static EncodedFormatId? AutoEncodedTarget(CodecCapabilities targetCapabilities) =>
        targetCapabilities.SupportsEncodedFormat(EncodedFormatId.Rgbe) ? EncodedFormatId.Rgbe : null;

    private static EncodedElementRepresentation EncodedRepresentation(EncodedFormatId format)
    {
        if (format.Name is nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1) or
            nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5)) {
            var fields = format.Name switch {
                nameof(EncodedFormatId.R10G10B10A2) => new[] { new PackedField("R", 0, 10), new PackedField("G", 10, 10), new PackedField("B", 20, 10), new PackedField("A", 30, 2) },
                nameof(EncodedFormatId.B5G6R5) => [new PackedField("B", 0, 5), new PackedField("G", 5, 6), new PackedField("R", 11, 5)],
                nameof(EncodedFormatId.B5G5R5A1) => [new PackedField("B", 0, 5), new PackedField("G", 5, 5), new PackedField("R", 10, 5), new PackedField("A", 15, 1)],
                nameof(EncodedFormatId.R11G11B10Float) => [new PackedField("R", 0, 11), new PackedField("G", 11, 11), new PackedField("B", 22, 10)],
                _ => [new PackedField("R", 0, 9), new PackedField("G", 9, 9), new PackedField("B", 18, 9), new PackedField("E", 27, 5)],
            };
            return new EncodedElementRepresentation {
                Format = format,
                TexelExtentPerElement = new Lucitex.Core.Spatial.Extent3I(1, 1, 1),
                BitsPerElement = format.Name is nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1) ? 16 : 32,
                Class = format == EncodedFormatId.Rgb9E5 ? EncodedElementClass.SharedExponent : EncodedElementClass.Packed,
                PackedLayout = new PackedFieldLayout { Fields = fields },
            };
        }

        if (format == EncodedFormatId.Rgbe) {
            return new EncodedElementRepresentation {
                Format = format,
                TexelExtentPerElement = new Lucitex.Core.Spatial.Extent3I(1, 1, 1),
                BitsPerElement = 32,
                Class = EncodedElementClass.SharedExponent,
                PackedLayout = new PackedFieldLayout {
                    Fields =
                    [
                        new PackedField("R", 0, 8),
                        new PackedField("G", 8, 8),
                        new PackedField("B", 16, 8),
                        new PackedField("E", 24, 8),
                    ],
                },
            };
        }

        var bits = format.Name is nameof(EncodedFormatId.Bc1) or nameof(EncodedFormatId.Bc4) ? 64 : 128;
        return new EncodedElementRepresentation {
            Format = format,
            TexelExtentPerElement = new Lucitex.Core.Spatial.Extent3I(4, 4, 1),
            BitsPerElement = bits,
            Class = EncodedElementClass.BlockCompressed,
        };
    }

    private static (bool Success, IReadOnlyList<ConversionStep> Steps, ChannelSchema Channels, IReadOnlyList<LossDiagnostic> Diagnostics)
        PlanEncodedChannels(ChannelSchema channels, EncodedFormatId format)
    {
        string[] requiredNames = format.Name switch {
            nameof(EncodedFormatId.Bc4) => ["R"],
            nameof(EncodedFormatId.Bc5) => ["R", "G"],
            nameof(EncodedFormatId.Rgbe) or nameof(EncodedFormatId.B5G6R5) or
                nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5) => ["R", "G", "B"],
            _ => ["R", "G", "B", "A"],
        };
        var sourceByName = channels.Channels.ToDictionary(channel => channel.Name.FullName);
        var result = new List<ChannelDescriptor>(requiredNames.Length);
        var steps = new List<ConversionStep>();
        var diagnostics = new List<LossDiagnostic>();
        foreach (var name in requiredNames) {
            if (sourceByName.TryGetValue(name, out var channel)) {
                result.Add(channel);
                continue;
            }

            if (name == "A") {
                var alpha = new ChannelDescriptor {
                    Name = "A",
                    Semantic = ChannelSemantic.Alpha,
                    SampleType = SampleType.UNorm8,
                    Sampling = SampleGrid.Unit,
                };
                result.Add(alpha);
                steps.Add(new SynthesizeChannelStep("A", SampleType.UNorm8, 1));
                continue;
            }

            diagnostics.Add(new LossDiagnostic {
                Category = LossCategory.Channel,
                Message = $"Encoded format '{format}' requires channel '{name}'.",
                ChannelName = name,
            });
            return (false, steps, channels, diagnostics);
        }

        var dropped = channels.Channels.Where(channel => !requiredNames.Contains(channel.Name.FullName)).ToList();
        if (dropped.Count > 0) {
            diagnostics.Add(new LossDiagnostic {
                Category = LossCategory.Channel,
                Message = $"Encoding to '{format}' drops channel(s): {string.Join(", ", dropped.Select(channel => channel.Name.FullName))}.",
            });
        }

        if (!channels.Channels.Select(channel => channel.Name.FullName).SequenceEqual(requiredNames)) {
            steps.Add(new SelectChannelsStep(result.Select(channel => channel.Name).ToList()));
        }

        return (true, steps, new ChannelSchema { Channels = result }, diagnostics);
    }

    // DDS/KTX2 pick one fixed-layout pixel format for a whole part, and their tables only define
    // 1, 2, or 4-channel formats - there is no 3-channel 8-bit hardware format. A 3-channel source
    // (an opaque RGB PNG is the common case) gets an opaque alpha channel synthesized so it lands on
    // a channel count the target can actually represent, the same way PlanEncodedChannels already
    // does for encoded formats that require alpha. Only a shortfall of exactly one channel is
    // recoverable this way; anything else means the target genuinely cannot represent this many
    // channels as a single fixed-layout element.
    private static (bool Success, IReadOnlyList<ConversionStep> Steps, ChannelSchema Channels, IReadOnlyList<LossDiagnostic> Diagnostics)
        PadChannelCount(ChannelSchema channels, IReadOnlyList<int> supportedChannelCounts)
    {
        var count = channels.Channels.Count;
        var hasAlpha = channels.Channels.Any(channel => channel.Name.FullName == "A");

        if (!hasAlpha && supportedChannelCounts.Contains(count + 1)) {
            var alpha = new ChannelDescriptor {
                Name = "A",
                Semantic = ChannelSemantic.Alpha,
                SampleType = channels.Channels[0].SampleType,
                Sampling = SampleGrid.Unit,
            };
            var padded = new List<ChannelDescriptor>(channels.Channels) { alpha };
            IReadOnlyList<ConversionStep> steps = [new SynthesizeChannelStep("A", alpha.SampleType, 1)];
            return (true, steps, new ChannelSchema { Channels = padded }, []);
        }

        return (false, [], channels, [
            new LossDiagnostic {
                Category = LossCategory.Channel,
                Message = $"Target format has no fixed pixel layout for {count} channel(s).",
            },
        ]);
    }

    private static (IReadOnlyList<ConversionStep> Steps, ChannelSchema Channels, IReadOnlyList<LossDiagnostic> Diagnostics)
        PlanEncodedSampleTypes(ChannelSchema channels, EncodedFormatId format)
    {
        var steps = new List<ConversionStep>();
        var diagnostics = new List<LossDiagnostic>();
        var result = new List<ChannelDescriptor>(channels.Channels.Count);
        foreach (var channel in channels.Channels) {
            var target = format.Name switch {
                nameof(EncodedFormatId.R10G10B10A2) when channel.Name.FullName == "A" => SampleType.UNorm8,
                nameof(EncodedFormatId.R10G10B10A2) => SampleType.UNorm16,
                nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5) or
                    nameof(EncodedFormatId.Bc6H) or nameof(EncodedFormatId.Bc6HSigned) => SampleType.Float16,
                nameof(EncodedFormatId.Rgbe) => SampleType.Float32,
                _ => SampleType.UNorm8,
            };
            if (channel.SampleType != target) {
                steps.Add(new ConvertSampleTypeStep(channel.Name, channel.SampleType, target));
                diagnostics.Add(new LossDiagnostic {
                    Category = IsNarrowing(channel.SampleType, target) ? LossCategory.DynamicRange : LossCategory.Numeric,
                    Message = $"Channel '{channel.Name}' converts from {channel.SampleType} to {target}.",
                    ChannelName = channel.Name.FullName,
                });
            }

            result.Add(channel with { SampleType = target });
        }

        return (steps, new ChannelSchema { Channels = result }, diagnostics);
    }

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
