using System.Buffers.Binary;
using Lucitex.Conversion.Kernels;
using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;
using Lucitex.Compression;

namespace Lucitex.Conversion;

// Every channel decodes into a canonical float32 buffer and re-encodes from there, so a
// ConvertSampleType step falls out for free once both ends agree on float32 as the interchange type.
public static class ConversionExecutor
{
    private readonly record struct ChannelPlacement(int PlaneIndex, int BaseOffset, int PerSampleStride, int BytesPerSample);

    private const int k_ParallelPixelThreshold = 64 * 64;

    private static readonly HashSet<string> s_ColorChannelNames = ["R", "G", "B", "Y"];

    public static void Execute(ConversionPlan plan, IImageReader source, SampleByteOrder sourceByteOrder, IImageWriter target, SampleByteOrder targetByteOrder) =>
        Execute(plan, source, sourceByteOrder, target, targetByteOrder, null, default);

    public static void Execute(ConversionPlan plan, IImageReader source, SampleByteOrder sourceByteOrder,
        IImageWriter target, SampleByteOrder targetByteOrder, ImageExecutionOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        foreach (var work in WorkItems(plan, sourceByteOrder, targetByteOrder, options ?? ImageExecutionOptions.Default, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (work is Compute compute) {
                compute.Run();
                continue;
            }
            var transfer = (Transfer)work;
            if (transfer.IsRead) {
                ValidateRead(source.Read(transfer.Region, transfer.Buffer), transfer.Buffer.Length);
            }
            else {
                target.Write(transfer.Region, transfer.Buffer);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        target.Finish();
    }

    public static Task ExecuteAsync(ConversionPlan plan, IAsyncImageReader source, SampleByteOrder sourceByteOrder,
        IAsyncImageWriter target, SampleByteOrder targetByteOrder, ImageExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        return Task.Run(async () => {
            foreach (var work in WorkItems(plan, sourceByteOrder, targetByteOrder, options ?? ImageExecutionOptions.Default, cancellationToken, streamRows: true)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (work is Compute compute) {
                    await compute.RunAsync().ConfigureAwait(false);
                    continue;
                }
                var transfer = (Transfer)work;
                if (transfer.IsRead) {
                    var read = await source.ReadAsync(transfer.Region, transfer.Buffer, cancellationToken).ConfigureAwait(false);
                    ValidateRead(read, transfer.Buffer.Length);
                }
                else {
                    await target.WriteAsync(transfer.Region, transfer.Buffer, cancellationToken).ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            await target.FinishAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private static void ValidateRead(int actual, int expected)
    {
        if (actual != expected) {
            throw new InvalidDataException($"The reader returned {actual} bytes for a region requiring {expected} bytes.");
        }
    }

    private abstract record Work;
    private sealed record Transfer(WorkRegion Region, byte[] Buffer, bool IsRead) : Work;
    private sealed record Compute(Action Run, Func<Task> RunAsync) : Work;

    private static IEnumerable<Work> WorkItems(ConversionPlan plan, SampleByteOrder sourceByteOrder,
        SampleByteOrder targetByteOrder, ImageExecutionOptions options, CancellationToken cancellationToken, bool streamRows = false)
    {
        Compute CpuWork(int count, int pixels, Action<int> action)
        {
            var degree = pixels < k_ParallelPixelThreshold ? 1 : options.MaxDegreeOfParallelism;
            return new Compute(
                () => ExecutionScheduler.For(0, count, action, degree, cancellationToken),
                () => ExecutionScheduler.ForAsync(0, count, action, degree, cancellationToken));
        }

        for (var i = 0; i < plan.Parts.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var partPlan = plan.Parts[i];
            var sourcePart = plan.SourceDescriptor.Parts[partPlan.SourcePartIndex];
            var targetPart = plan.TargetDescriptor.Parts[i];

            foreach (var step in partPlan.Steps) {
                cancellationToken.ThrowIfCancellationRequested();
                if (step is not (SelectPartStep or SelectChannelsStep or ConvertSampleTypeStep or PremultiplyAlphaStep or UnpremultiplyAlphaStep
                    or ApplyOrientationStep or ResampleStep or CropStep or ColorTransformStep or DropMetadataStep or PreserveMetadataStep
                    or DecodeEncodedElementsStep or EncodeEncodedElementsStep or TranscodeEncodedElementsStep or SynthesizeChannelStep or ExpandIndexedStep
                    or ChangeSampleGridStep or MapChannelsStep)) {
                    throw new NotSupportedException($"ConversionExecutor does not support {step.GetType().Name} yet.");
                }
            }

            var window = sourcePart.Spatial.DataWindow;
            var width = checked((int)window.Width);
            var height = checked((int)window.Height);
            var pixelCount = width * height;
            var region = new WorkRegion { Subresource = new SubresourceId(partPlan.SourcePartIndex, 0, 0, LevelKey.Base), Region = window };

            if (sourcePart.Representation is EncodedElementRepresentation sourceEncoded &&
                targetPart.Representation is EncodedElementRepresentation targetEncoded &&
                sourceEncoded.Format == targetEncoded.Format &&
                partPlan.Steps.OfType<TranscodeEncodedElementsStep>().Any(step => step.SourceFormat == sourceEncoded.Format && step.TargetFormat == targetEncoded.Format)) {
                var encoded = new byte[EncodedByteCount(sourceEncoded, sourcePart.Topology.BaseExtent)];
                yield return new Transfer(region, encoded, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();
                yield return new Transfer(region with { Subresource = region.Subresource with { Part = i } }, encoded, IsRead: false);
                continue;
            }

            var resizeSteps = streamRows ? partPlan.Steps.OfType<ResampleStep>().ToArray() : [];
            if (streamRows && resizeSteps.Length == 1 &&
                sourcePart.Representation is PlainSampleRepresentation rowSource &&
                targetPart.Representation is PlainSampleRepresentation rowTarget &&
                partPlan.Steps.All(step => step is SelectPartStep or SelectChannelsStep or ConvertSampleTypeStep or
                    ResampleStep or DropMetadataStep or PreserveMetadataStep) &&
                targetPart.Channels.Channels.All(channel => channel.Sampling.Step.X == 1 && channel.Sampling.Step.Y == 1) &&
                targetPart.Channels.Channels.All(channel => sourcePart.Channels.Channels.Any(sourceChannel => sourceChannel.Name == channel.Name))) {
                var resize = resizeSteps[0];
                var sourceChannels = sourcePart.Channels.Channels.ToDictionary(channel => channel.Name);
                var rowTargetChannels = targetPart.Channels.Channels.ToDictionary(channel => channel.Name);
                var sourceLayout = new SampleLayout(rowSource.Planes, sourceChannels, window);
                var targetWindow = ImageBox.FromOrigin(resize.TargetWidth, resize.TargetHeight);
                var rowTargetLayout = new SampleLayout(rowTarget.Planes, rowTargetChannels, targetWindow);
                var input = new byte[sourceLayout.TotalBytes];
                yield return new Transfer(region, input, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();
                var output = new byte[rowTargetLayout.TotalBytes];
                var names = rowTarget.Planes.SelectMany(plane => plane.Channels).ToArray();
                var packedRgba = names.Length == 4 && rowSource.Planes.Count == 1 && rowTarget.Planes.Count == 1 &&
                    rowSource.Planes[0].Layout == PlaneLayout.Interleaved && rowTarget.Planes[0].Layout == PlaneLayout.Interleaved &&
                    rowSource.Planes[0].Channels.SequenceEqual(names) &&
                    sourceChannels.Values.All(channel => channel.Sampling.Step.X == 1 && channel.Sampling.Step.Y == 1) &&
                    sourceChannels.Values.All(channel => channel.SampleType == sourceChannels[names[0]].SampleType) &&
                    rowTargetChannels.Values.All(channel => channel.SampleType == rowTargetChannels[names[0]].SampleType);
                if (packedRgba) {
                    var sourceType = sourceChannels[names[0]].SampleType;
                    var targetType = rowTargetChannels[names[0]].SampleType;
                    var inputRowBytes = checked(width * 4 * sourceLayout.BytesPerSample(names[0]));
                    var outputRowBytes = checked(resize.TargetWidth * 4 * rowTargetLayout.BytesPerSample(names[0]));
                    var inputRowBases = sourceLayout.BuildRowBases(names[0], height);
                    var outputRowBases = rowTargetLayout.BuildRowBases(names[0], resize.TargetHeight);
                    var packedFilter = new ResampleKernel.RowPlan(width, height, resize.TargetWidth, resize.TargetHeight, 4);
                    const int packedRowsPerBatch = 32;
                    yield return CpuWork((resize.TargetHeight + packedRowsPerBatch - 1) / packedRowsPerBatch, pixelCount, batch => {
                        var first = batch * packedRowsPerBatch;
                        ResampleKernel.ResizeRows(packedFilter, first, Math.Min(resize.TargetHeight, first + packedRowsPerBatch),
                            (y, floats) => SampleTypeConversionKernel.ToFloat32(
                                input.AsSpan(checked((int)inputRowBases[y]), inputRowBytes), sourceType, sourceByteOrder, floats),
                            (y, floats) => SampleTypeConversionKernel.FromFloat32(floats, targetType, targetByteOrder,
                                output.AsSpan(checked((int)outputRowBases[y]), outputRowBytes)), cancellationToken);
                    });
                    yield return new Transfer(region with {
                        Subresource = region.Subresource with { Part = i },
                        Region = targetWindow,
                    }, output, IsRead: false);
                    continue;
                }
                var sourceRows = names.Select(name => new ChannelRows(sourceLayout, name, width, height)).ToArray();
                var targetRows = names.Select(name => new ChannelRows(rowTargetLayout, name, resize.TargetWidth, resize.TargetHeight)).ToArray();
                var filter = new ResampleKernel.RowPlan(width, height, resize.TargetWidth, resize.TargetHeight);
                const int rowsPerBatch = 32;
                yield return CpuWork((resize.TargetHeight + rowsPerBatch - 1) / rowsPerBatch, pixelCount, batch => {
                    var first = batch * rowsPerBatch;
                    var end = Math.Min(resize.TargetHeight, first + rowsPerBatch);
                    for (var c = 0; c < names.Length; c++) {
                        var channel = c;
                        var name = names[channel];
                        var sourceBytes = new byte[width * sourceRows[channel].BytesPerSample];
                        var targetBytes = new byte[resize.TargetWidth * targetRows[channel].BytesPerSample];
                        ResampleKernel.ResizeRows(filter, first, end,
                            (y, floats) => {
                                sourceRows[channel].Gather(input, sourceBytes, y, width);
                                SampleTypeConversionKernel.ToFloat32(sourceBytes, sourceChannels[name].SampleType, sourceByteOrder, floats);
                            },
                            (y, floats) => {
                                SampleTypeConversionKernel.FromFloat32(floats, rowTargetChannels[name].SampleType, targetByteOrder, targetBytes);
                                targetRows[channel].Scatter(targetBytes, output, y, resize.TargetWidth);
                            }, cancellationToken);
                    }
                });
                yield return new Transfer(region with {
                    Subresource = region.Subresource with { Part = i },
                    Region = targetWindow,
                }, output, IsRead: false);
                continue;
            }

            var floatChannels = new Dictionary<ChannelPath, float[]>();
            if (sourcePart.Representation is PlainSampleRepresentation sourcePlain) {
                var sourceChannels = sourcePart.Channels.Channels.ToDictionary(c => c.Name);
                var sourceLayout = new SampleLayout(sourcePlain.Planes, sourceChannels, window);
                var sourceBuffer = new byte[sourceLayout.TotalBytes];
                yield return new Transfer(region, sourceBuffer, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();

                var sourceNames = sourcePlain.Planes.SelectMany(plane => plane.Channels).ToArray();
                var decoded = new float[sourceNames.Length][];

                yield return CpuWork(sourceNames.Length, pixelCount, index => {
                    var channelName = sourceNames[index];
                    var channel = sourceChannels[channelName];
                    var bytesPerSample = sourceLayout.BytesPerSample(channelName);

                    var raw = new byte[pixelCount * bytesPerSample];
                    GatherChannel(sourceBuffer, sourceLayout, channelName, width, height, raw);

                    var floats = new float[pixelCount];
                    SampleTypeConversionKernel.ToFloat32(raw, channel.SampleType, sourceByteOrder, floats);
                    decoded[index] = floats;
                });

                for (var index = 0; index < sourceNames.Length; index++) {
                    floatChannels[sourceNames[index]] = decoded[index];
                }
            }
            else if (sourcePart.Representation is EncodedElementRepresentation { Format.Name: nameof(EncodedFormatId.Rgbe) } &&
                partPlan.Steps.OfType<DecodeEncodedElementsStep>().Any(step => step.Format == EncodedFormatId.Rgbe)) {
                var encoded = new byte[checked(pixelCount * 4)];
                yield return new Transfer(region, encoded, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();
                var red = new float[pixelCount];
                var green = new float[pixelCount];
                var blue = new float[pixelCount];
                RgbeConversionKernel.Decode(encoded, red, green, blue);
                floatChannels["R"] = red;
                floatChannels["G"] = green;
                floatChannels["B"] = blue;
            }
            else if (sourcePart.Representation is EncodedElementRepresentation packed &&
                IsPackedPixelFormat(packed.Format) &&
                partPlan.Steps.OfType<DecodeEncodedElementsStep>().Any(step => step.Format == packed.Format)) {
                var encoded = new byte[checked(pixelCount * ((packed.BitsPerElement + 7) / 8))];
                yield return new Transfer(region, encoded, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();
                var red = new float[pixelCount];
                var green = new float[pixelCount];
                var blue = new float[pixelCount];
                var alpha = packed.Format.Name is nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G5R5A1) ? new float[pixelCount] : [];
                PackedPixelConversionKernel.Decode(packed.Format, encoded, red, green, blue, alpha);
                floatChannels["R"] = red;
                floatChannels["G"] = green;
                floatChannels["B"] = blue;
                if (alpha.Length > 0) {
                    floatChannels["A"] = alpha;
                }
            }
            else if (sourcePart.Representation is EncodedElementRepresentation bc6H &&
                bc6H.Format.Name is nameof(EncodedFormatId.Bc6H) or nameof(EncodedFormatId.Bc6HSigned) &&
                partPlan.Steps.OfType<DecodeEncodedElementsStep>().Any(step => step.Format == bc6H.Format)) {
                var encoded = new byte[Bc6HImageCodec.EncodedByteCount(width, height)];
                yield return new Transfer(region, encoded, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();
                var decoded = new float[checked(pixelCount * 3)];
                yield return new Compute(
                    () => Bc6HImageCodec.Decode(encoded, width, height, bc6H.Format == EncodedFormatId.Bc6HSigned, decoded, options.MaxDegreeOfParallelism, cancellationToken),
                    () => Bc6HImageCodec.DecodeAsync(encoded, width, height, bc6H.Format == EncodedFormatId.Bc6HSigned, decoded, options.MaxDegreeOfParallelism, cancellationToken));
                for (var channelIndex = 0; channelIndex < 3; channelIndex++) {
                    var values = new float[pixelCount];
                    for (var pixel = 0; pixel < pixelCount; pixel++) {
                        values[pixel] = decoded[(pixel * 3) + channelIndex];
                    }

                    floatChannels[sourcePart.Channels.Channels[channelIndex].Name] = values;
                }
            }
            else if (sourcePart.Representation is EncodedElementRepresentation blockCompressed &&
                TryGetBcFormat(blockCompressed.Format, out var bcFormat) &&
                partPlan.Steps.OfType<DecodeEncodedElementsStep>().Any(step => step.Format == blockCompressed.Format)) {
                var encoded = new byte[BcImageCodec.EncodedByteCount(bcFormat, width, height)];
                yield return new Transfer(region, encoded, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();
                var channelCount = BcImageCodec.ChannelCount(bcFormat);
                var decoded = new byte[checked(pixelCount * channelCount)];
                yield return new Compute(
                    () => BcImageCodec.Decode(bcFormat, encoded, width, height, decoded, options.MaxDegreeOfParallelism, cancellationToken),
                    () => BcImageCodec.DecodeAsync(bcFormat, encoded, width, height, decoded, options.MaxDegreeOfParallelism, cancellationToken));
                var decodedChannels = new float[channelCount][];
                yield return CpuWork(channelCount, pixelCount, channelIndex => {
                    var raw = new byte[pixelCount];
                    SampleInterleaveKernel.Gather(decoded.AsSpan(channelIndex), raw, channelCount, 1, pixelCount);

                    var values = new float[pixelCount];
                    SampleTypeConversionKernel.ToFloat32(raw, SampleType.UNorm8, SampleByteOrder.LittleEndian, values);
                    decodedChannels[channelIndex] = values;
                });

                for (var channelIndex = 0; channelIndex < channelCount; channelIndex++) {
                    floatChannels[sourcePart.Channels.Channels[channelIndex].Name] = decodedChannels[channelIndex];
                }
            }
            else if (sourcePart.Representation is IndexedRepresentation indexed &&
                partPlan.Steps.OfType<ExpandIndexedStep>().Any()) {
                var indexBytesPerSample = indexed.IndexType.Bits / 8;
                var indexBuffer = new byte[checked(pixelCount * indexBytesPerSample)];
                yield return new Transfer(region, indexBuffer, IsRead: true);
                cancellationToken.ThrowIfCancellationRequested();

                var indices = new int[pixelCount];
                for (var pixel = 0; pixel < pixelCount; pixel++) {
                    indices[pixel] = indexBytesPerSample switch {
                        1 => indexBuffer[pixel],
                        2 => sourceByteOrder == SampleByteOrder.LittleEndian
                            ? BinaryPrimitives.ReadUInt16LittleEndian(indexBuffer.AsSpan(pixel * 2, 2))
                            : BinaryPrimitives.ReadUInt16BigEndian(indexBuffer.AsSpan(pixel * 2, 2)),
                        _ => throw new NotSupportedException($"ConversionExecutor does not support a {indexed.IndexType.Bits}-bit palette index."),
                    };
                }

                var rawEntries = indexed.Palette.RawEntries
                    ?? throw new NotSupportedException("ConversionExecutor cannot expand an indexed representation without palette content.");
                var entryChannels = indexed.Palette.EntryChannels.Channels;
                var entryBytesPerSample = indexed.Palette.EntrySampleType.Bits / 8;
                var entryStride = entryChannels.Count * entryBytesPerSample;

                var expandedChannels = new float[entryChannels.Count][];
                yield return CpuWork(entryChannels.Count, pixelCount, channelIndex => {
                    var raw = new byte[checked(pixelCount * entryBytesPerSample)];
                    for (var pixel = 0; pixel < pixelCount; pixel++) {
                        var entryOffset = (indices[pixel] * entryStride) + (channelIndex * entryBytesPerSample);
                        rawEntries.AsSpan(entryOffset, entryBytesPerSample).CopyTo(raw.AsSpan(pixel * entryBytesPerSample, entryBytesPerSample));
                    }

                    var values = new float[pixelCount];
                    SampleTypeConversionKernel.ToFloat32(raw, indexed.Palette.EntrySampleType, sourceByteOrder, values);
                    expandedChannels[channelIndex] = values;
                });

                for (var channelIndex = 0; channelIndex < entryChannels.Count; channelIndex++) {
                    floatChannels[entryChannels[channelIndex].Name] = expandedChannels[channelIndex];
                }
            }
            else {
                throw new NotSupportedException($"ConversionExecutor does not support {sourcePart.Representation.GetType().Name} as a source.");
            }

            foreach (var step in partPlan.Steps) {
                cancellationToken.ThrowIfCancellationRequested();
                switch (step) {
                    case MapChannelsStep mapping:
                        floatChannels = mapping.Mappings.ToDictionary(item => item.Target, item => floatChannels[item.Source].ToArray());
                        break;
                    case SynthesizeChannelStep synthesizeStep:
                        floatChannels[synthesizeStep.Channel] = Enumerable.Repeat((float)synthesizeStep.ConstantValue, pixelCount).ToArray();
                        break;

                    case ApplyOrientationStep orientationStep: {
                            var entries = floatChannels.ToArray();
                            var destinations = new float[entries.Length][];
                            var extents = new (int Width, int Height)[entries.Length];

                            yield return CpuWork(entries.Length, pixelCount, index => {
                                var destination = new float[entries[index].Value.Length];
                                extents[index] = OrientationKernel.ApplyToIdentity(entries[index].Value, width, height, orientationStep.From, destination);
                                destinations[index] = destination;
                            });

                            var reoriented = new Dictionary<ChannelPath, float[]>();
                            for (var index = 0; index < entries.Length; index++) {
                                reoriented[entries[index].Key] = destinations[index];
                            }

                            floatChannels = reoriented;
                            (width, height) = extents[0];
                            pixelCount = width * height;
                            region = region with { Region = ImageBox.FromOrigin(width, height) };
                            window = region.Region;
                            break;
                        }

                    case ResampleStep resampleStep: {
                            var entries = floatChannels.ToArray();
                            var destinations = new float[entries.Length][];
                            var targetPixelCount = resampleStep.TargetWidth * resampleStep.TargetHeight;

                            yield return CpuWork(entries.Length, pixelCount, index => {
                                var destination = new float[targetPixelCount];
                                ResampleKernel.Resize(entries[index].Value, width, height, resampleStep.TargetWidth, resampleStep.TargetHeight, destination);
                                destinations[index] = destination;
                            });

                            var resampled = new Dictionary<ChannelPath, float[]>();
                            for (var index = 0; index < entries.Length; index++) {
                                resampled[entries[index].Key] = destinations[index];
                            }

                            floatChannels = resampled;
                            width = resampleStep.TargetWidth;
                            height = resampleStep.TargetHeight;
                            pixelCount = targetPixelCount;
                            region = region with { Region = ImageBox.FromOrigin(width, height) };
                            window = region.Region;
                            break;
                        }

                    case CropStep cropStep: {
                            var entries = floatChannels.ToArray();
                            var destinations = new float[entries.Length][];
                            var croppedPixelCount = cropStep.Width * cropStep.Height;

                            yield return CpuWork(entries.Length, pixelCount, index => {
                                var destination = new float[croppedPixelCount];
                                var source = entries[index].Value;
                                for (var row = 0; row < cropStep.Height; row++) {
                                    source.AsSpan(((cropStep.Y + row) * width) + cropStep.X, cropStep.Width)
                                        .CopyTo(destination.AsSpan(row * cropStep.Width, cropStep.Width));
                                }
                                destinations[index] = destination;
                            });

                            var cropped = new Dictionary<ChannelPath, float[]>();
                            for (var index = 0; index < entries.Length; index++) {
                                cropped[entries[index].Key] = destinations[index];
                            }

                            floatChannels = cropped;
                            width = cropStep.Width;
                            height = cropStep.Height;
                            pixelCount = croppedPixelCount;
                            region = region with { Region = ImageBox.FromOrigin(width, height) };
                            window = region.Region;
                            break;
                        }

                    case PremultiplyAlphaStep when floatChannels.TryGetValue("A", out var alpha):
                        foreach (var (name, values) in floatChannels) {
                            if (s_ColorChannelNames.Contains(name.FullName)) {
                                AlphaKernel.Premultiply(values, alpha);
                            }
                        }

                        break;

                    case UnpremultiplyAlphaStep when floatChannels.TryGetValue("A", out var alpha):
                        foreach (var (name, values) in floatChannels) {
                            if (s_ColorChannelNames.Contains(name.FullName)) {
                                AlphaKernel.Unpremultiply(values, alpha);
                            }
                        }

                        break;

                    case ColorTransformStep colorStep: {
                            if (colorStep is not ({ From: TransferFunction.Linear, To: TransferFunction.Srgb } or
                                { From: TransferFunction.Srgb, To: TransferFunction.Linear })) {
                                throw new NotSupportedException($"ConversionExecutor does not support a color transform from {colorStep.From} to {colorStep.To}.");
                            }

                            var toSrgb = colorStep.To == TransferFunction.Srgb;
                            var colorValues = floatChannels
                                .Where(entry => s_ColorChannelNames.Contains(entry.Key.FullName))
                                .Select(entry => entry.Value)
                                .ToArray();

                            yield return CpuWork(colorValues.Length, pixelCount, index => {
                                if (toSrgb) {
                                    ColorTransformKernel.LinearToSrgb(colorValues[index]);
                                }
                                else {
                                    ColorTransformKernel.SrgbToLinear(colorValues[index]);
                                }
                            });

                            break;
                        }
                }
            }

            if (targetPart.Representation is EncodedElementRepresentation { Format.Name: nameof(EncodedFormatId.Rgbe) } &&
                partPlan.Steps.OfType<EncodeEncodedElementsStep>().Any(step => step.Format == EncodedFormatId.Rgbe)) {
                var encoded = new byte[checked(pixelCount * 4)];
                RgbeConversionKernel.Encode(floatChannels["R"], floatChannels["G"], floatChannels["B"], encoded);
                yield return new Transfer(region, encoded, IsRead: false);
                continue;
            }

            if (targetPart.Representation is EncodedElementRepresentation packedTarget &&
                IsPackedPixelFormat(packedTarget.Format) &&
                partPlan.Steps.OfType<EncodeEncodedElementsStep>().Any(step => step.Format == packedTarget.Format)) {
                var encoded = new byte[checked(pixelCount * ((packedTarget.BitsPerElement + 7) / 8))];
                var alpha = packedTarget.Format.Name is nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G5R5A1) ? floatChannels["A"] : [];
                PackedPixelConversionKernel.Encode(packedTarget.Format, floatChannels["R"], floatChannels["G"], floatChannels["B"], alpha, encoded);
                yield return new Transfer(region with { Subresource = region.Subresource with { Part = i } }, encoded, IsRead: false);
                continue;
            }

            if (targetPart.Representation is EncodedElementRepresentation blockTarget &&
                TryGetBcFormat(blockTarget.Format, out var targetBcFormat) &&
                partPlan.Steps.OfType<EncodeEncodedElementsStep>().Any(step => step.Format == blockTarget.Format)) {
                var channelCount = BcImageCodec.ChannelCount(targetBcFormat);
                var interleaved = new byte[checked(pixelCount * channelCount)];
                yield return CpuWork(channelCount, pixelCount, channelIndex => {
                    var channel = targetPart.Channels.Channels[channelIndex];
                    var raw = new byte[pixelCount];
                    SampleTypeConversionKernel.FromFloat32(floatChannels[channel.Name], SampleType.UNorm8, SampleByteOrder.LittleEndian, raw);
                    SampleInterleaveKernel.Scatter(raw, interleaved.AsSpan(channelIndex), channelCount, 1, pixelCount);
                });

                var encoded = new byte[BcImageCodec.EncodedByteCount(targetBcFormat, width, height)];
                yield return new Compute(
                    () => BcImageCodec.Encode(targetBcFormat, interleaved, width, height, encoded, options.MaxDegreeOfParallelism, cancellationToken),
                    () => BcImageCodec.EncodeAsync(targetBcFormat, interleaved, width, height, encoded, options.MaxDegreeOfParallelism, cancellationToken));
                yield return new Transfer(region with { Subresource = region.Subresource with { Part = i } }, encoded, IsRead: false);
                continue;
            }

            if (targetPart.Representation is not PlainSampleRepresentation targetPlain) {
                throw new NotSupportedException($"ConversionExecutor does not support {targetPart.Representation.GetType().Name} as a target.");
            }

            var targetChannels = targetPart.Channels.Channels.ToDictionary(c => c.Name);
            var targetLayout = new SampleLayout(targetPlain.Planes, targetChannels, window);

            var targetBuffer = new byte[targetLayout.TotalBytes];
            var targetNames = targetPlain.Planes.SelectMany(plane => plane.Channels).ToArray();

            yield return CpuWork(targetNames.Length, pixelCount, index => {
                var channelName = targetNames[index];
                var channel = targetChannels[channelName];
                var bytesPerSample = targetLayout.BytesPerSample(channelName);

                var raw = new byte[pixelCount * bytesPerSample];
                SampleTypeConversionKernel.FromFloat32(floatChannels[channelName], channel.SampleType, targetByteOrder, raw);
                ScatterChannel(raw, targetLayout, channelName, width, height, targetBuffer);
            });

            yield return new Transfer(region, targetBuffer, IsRead: false);
        }
    }

    private static void GatherChannel(
        ReadOnlySpan<byte> source,
        SampleLayout layout,
        ChannelPath channel,
        int width,
        int height,
        Span<byte> destination)
    {
        var bytesPerSample = layout.BytesPerSample(channel);
        var rowBases = layout.BuildRowBases(channel, height);
        var rowBytes = width * bytesPerSample;

        if (layout.TryGetColumnStride(channel, out var stride)) {
            for (var y = 0; y < height; y++) {
                SampleInterleaveKernel.Gather(
                    source[checked((int)rowBases[y])..],
                    destination.Slice(y * rowBytes, rowBytes),
                    stride,
                    bytesPerSample,
                    width);
            }

            return;
        }

        var columnOffsets = layout.BuildColumnOffsets(channel, width);
        for (var y = 0; y < height; y++) {
            var rowBase = rowBases[y];
            for (var x = 0; x < width; x++) {
                source.Slice(checked((int)(rowBase + columnOffsets[x])), bytesPerSample)
                    .CopyTo(destination.Slice((y * rowBytes) + (x * bytesPerSample), bytesPerSample));
            }
        }
    }

    private static void ScatterChannel(
        ReadOnlySpan<byte> source,
        SampleLayout layout,
        ChannelPath channel,
        int width,
        int height,
        Span<byte> destination)
    {
        var bytesPerSample = layout.BytesPerSample(channel);
        var rowBases = layout.BuildRowBases(channel, height);
        var rowBytes = width * bytesPerSample;

        if (layout.TryGetColumnStride(channel, out var stride)) {
            for (var y = 0; y < height; y++) {
                SampleInterleaveKernel.Scatter(
                    source.Slice(y * rowBytes, rowBytes),
                    destination[checked((int)rowBases[y])..],
                    stride,
                    bytesPerSample,
                    width);
            }

            return;
        }

        var columnOffsets = layout.BuildColumnOffsets(channel, width);
        for (var y = 0; y < height; y++) {
            var rowBase = rowBases[y];
            for (var x = 0; x < width; x++) {
                source.Slice((y * rowBytes) + (x * bytesPerSample), bytesPerSample)
                    .CopyTo(destination.Slice(checked((int)(rowBase + columnOffsets[x])), bytesPerSample));
            }
        }
    }

    private sealed class ChannelRows
    {
        public int BytesPerSample { get; }
        private readonly long[] _rowBases;
        private readonly int _stride;
        private readonly int[]? _columnOffsets;

        public ChannelRows(SampleLayout layout, ChannelPath channel, int width, int height)
        {
            BytesPerSample = layout.BytesPerSample(channel);
            _rowBases = layout.BuildRowBases(channel, height);
            if (!layout.TryGetColumnStride(channel, out _stride)) {
                _columnOffsets = layout.BuildColumnOffsets(channel, width);
            }
        }

        public void Gather(ReadOnlySpan<byte> source, Span<byte> destination, int row, int width)
        {
            var rowBase = checked((int)_rowBases[row]);
            if (_columnOffsets is null) {
                SampleInterleaveKernel.Gather(source[rowBase..], destination, _stride, BytesPerSample, width);
            }
            else {
                for (var x = 0; x < width; x++) {
                    source.Slice(checked(rowBase + _columnOffsets[x]), BytesPerSample)
                        .CopyTo(destination.Slice(x * BytesPerSample, BytesPerSample));
                }
            }
        }

        public void Scatter(ReadOnlySpan<byte> source, Span<byte> destination, int row, int width)
        {
            var rowBase = checked((int)_rowBases[row]);
            if (_columnOffsets is null) {
                SampleInterleaveKernel.Scatter(source, destination[rowBase..], _stride, BytesPerSample, width);
            }
            else {
                for (var x = 0; x < width; x++) {
                    source.Slice(x * BytesPerSample, BytesPerSample)
                        .CopyTo(destination.Slice(checked(rowBase + _columnOffsets[x]), BytesPerSample));
                }
            }
        }
    }

    private sealed class SampleLayout
    {
        private readonly ImageBox _window;
        private readonly int _rowCount;
        private readonly SampleGrid[] _planeSampling;
        private readonly int[] _planeRowBytes;
        private readonly Dictionary<ChannelPath, ChannelPlacement> _placements = [];
        private readonly long[]? _rowOffsets;
        private readonly int _uniformRowBytes;

        public SampleLayout(
            IReadOnlyList<SamplePlaneDescriptor> planes,
            IReadOnlyDictionary<ChannelPath, ChannelDescriptor> channels,
            ImageBox window)
        {
            _window = window;
            _rowCount = checked((int)window.Height);
            _planeSampling = new SampleGrid[planes.Count];
            _planeRowBytes = new int[planes.Count];

            var ragged = false;

            for (var planeIndex = 0; planeIndex < planes.Count; planeIndex++) {
                var plane = planes[planeIndex];
                var sampling = PlaneSampling(plane, channels);
                var sampleCountX = checked((int)sampling.CountColumns(window.MinX, window.MaxXExclusive - 1));

                _planeSampling[planeIndex] = sampling;
                ragged |= sampling.Step.Y != 1;

                var bytesPerPixel = plane.Channels.Sum(name => ConversionExecutor.BytesPerSample(channels[name]));
                _planeRowBytes[planeIndex] = checked(sampleCountX * bytesPerPixel);

                var offset = 0;
                foreach (var name in plane.Channels) {
                    var bytesPerSample = ConversionExecutor.BytesPerSample(channels[name]);
                    _placements[name] = new ChannelPlacement(
                        planeIndex,
                        offset,
                        plane.Layout == PlaneLayout.Interleaved ? bytesPerPixel : bytesPerSample,
                        bytesPerSample);

                    offset += plane.Layout == PlaneLayout.Interleaved ? bytesPerSample : sampleCountX * bytesPerSample;
                }
            }

            if (ragged) {
                _rowOffsets = new long[_rowCount + 1];
                for (var row = 0; row < _rowCount; row++) {
                    _rowOffsets[row + 1] = _rowOffsets[row] + RowBytes(row);
                }

                TotalBytes = _rowOffsets[_rowCount];
            }
            else {
                _uniformRowBytes = _planeRowBytes.Sum();
                TotalBytes = (long)_uniformRowBytes * _rowCount;
            }
        }

        public long TotalBytes { get; }

        public int BytesPerSample(ChannelPath channel) => _placements[channel].BytesPerSample;

        public long[] BuildRowBases(ChannelPath channel, int height)
        {
            var placement = _placements[channel];
            var sampling = _planeSampling[placement.PlaneIndex];
            var bases = new long[height];

            for (var y = 0; y < height; y++) {
                var row = SourceRow(sampling, y);
                bases[y] = RowOffset(row) + PlaneOffsetInRow(row, placement.PlaneIndex) + placement.BaseOffset;
            }

            return bases;
        }

        // Without column subsampling every sample sits one fixed stride after the previous one, so the
        // per-column offset table collapses to that stride.
        public bool TryGetColumnStride(ChannelPath channel, out int stride)
        {
            var placement = _placements[channel];
            stride = placement.PerSampleStride;

            return _planeSampling[placement.PlaneIndex].Step.X == 1;
        }

        public int[] BuildColumnOffsets(ChannelPath channel, int width)
        {
            var placement = _placements[channel];
            var sampling = _planeSampling[placement.PlaneIndex];
            var offsets = new int[width];

            for (var x = 0; x < width; x++) {
                var column = sampling.ClampColumnToSample(_window.MinX + x, _window.MinX);
                var index = sampling.CountColumns(_window.MinX, column) - 1;
                offsets[x] = checked((int)(index * placement.PerSampleStride));
            }

            return offsets;
        }

        private int SourceRow(SampleGrid sampling, int y)
        {
            var row = sampling.ClampRowToSample(_window.MinY + y, _window.MinY) - _window.MinY;

            while (row >= _rowCount) {
                row -= sampling.Step.Y;
            }

            if (row < 0) {
                throw new NotSupportedException(
                    $"Channel sampling leaves no stored row inside the data window for image row {y}.");
            }

            return checked((int)row);
        }

        private long RowOffset(int row) => _rowOffsets?[row] ?? ((long)row * _uniformRowBytes);

        private int RowBytes(int row)
        {
            var total = 0;
            for (var planeIndex = 0; planeIndex < _planeRowBytes.Length; planeIndex++) {
                if (_planeSampling[planeIndex].IncludesRow(_window.MinY + row)) {
                    total = checked(total + _planeRowBytes[planeIndex]);
                }
            }

            return total;
        }

        private int PlaneOffsetInRow(int row, int planeIndex)
        {
            var offset = 0;

            for (var i = 0; i < planeIndex; i++) {
                if (_planeSampling[i].IncludesRow(_window.MinY + row)) {
                    offset += _planeRowBytes[i];
                }
            }

            return offset;
        }

        private static SampleGrid PlaneSampling(
            SamplePlaneDescriptor plane,
            IReadOnlyDictionary<ChannelPath, ChannelDescriptor> channels)
        {
            var sampling = channels[plane.Channels[0]].Sampling;

            foreach (var name in plane.Channels) {
                if (channels[name].Sampling != sampling) {
                    throw new NotSupportedException(
                        $"Plane containing '{name}' mixes sampling grids, which ConversionExecutor cannot address.");
                }
            }

            return sampling;
        }
    }

    private static int BytesPerSample(ChannelDescriptor channel)
    {
        if (channel.SampleType.Bits % 8 != 0) {
            throw new NotSupportedException($"ConversionExecutor does not support sub-byte sample types (channel '{channel.Name}', {channel.SampleType.Bits} bits).");
        }

        return channel.SampleType.Bits / 8;
    }

    private static int EncodedByteCount(EncodedElementRepresentation representation, Extent3L extent)
    {
        var elementsX = checked((extent.Width + representation.TexelExtentPerElement.Width - 1) / representation.TexelExtentPerElement.Width);
        var elementsY = checked((extent.Height + representation.TexelExtentPerElement.Height - 1) / representation.TexelExtentPerElement.Height);
        var elementsZ = checked((extent.Depth + representation.TexelExtentPerElement.Depth - 1) / representation.TexelExtentPerElement.Depth);
        return checked((int)((elementsX * elementsY * elementsZ * representation.BitsPerElement + 7) / 8));
    }

    private static bool TryGetBcFormat(EncodedFormatId format, out BcFormat bcFormat)
    {
        switch (format.Name) {
            case nameof(EncodedFormatId.Bc1):
                bcFormat = BcFormat.Bc1;
                return true;
            case nameof(EncodedFormatId.Bc2):
                bcFormat = BcFormat.Bc2;
                return true;
            case nameof(EncodedFormatId.Bc3):
                bcFormat = BcFormat.Bc3;
                return true;
            case nameof(EncodedFormatId.Bc4):
                bcFormat = BcFormat.Bc4;
                return true;
            case nameof(EncodedFormatId.Bc5):
                bcFormat = BcFormat.Bc5;
                return true;
            case nameof(EncodedFormatId.Bc7):
                bcFormat = BcFormat.Bc7;
                return true;
            default:
                bcFormat = default;
                return false;
        }
    }

    private static bool IsPackedPixelFormat(EncodedFormatId format) => format.Name is
        nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G6R5) or nameof(EncodedFormatId.B5G5R5A1) or
        nameof(EncodedFormatId.R11G11B10Float) or nameof(EncodedFormatId.Rgb9E5);
}
