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

// Executes a ConversionPlan against a real reader/writer pair. Every channel's samples are decoded
// into a canonical float32 buffer (via SampleTypeConversionKernel.ToFloat32), then re-encoded into
// the target's sample types (via FromFloat32) - this is what lets ConvertSampleType steps fall out
// "for free" once both ends agree on float32 as the interchange representation.
//
// A part's planes can mix layouts (e.g. EXR reports one Planar plane per channel; PNG/DDS/KTX2 report
// one Interleaved plane for all channels), and both are handled by the same per-channel byte-offset
// formula: for a channel c, byteOffset(x, y) = y*rowStride + rowRelativeBase(c) + x*perPixelStride(c).
// perPixelStride(c) is that channel's own byte size under Planar (channels form separate row-length
// blocks) or the whole plane's bytes-per-pixel under Interleaved (channels are pixel-adjacent);
// rowRelativeBase(c) is the cumulative size of everything before it in plane/channel declaration order.
//
// Scope: a single whole-window WorkRegion per part (no chunked/streaming reads yet), and only
// SelectChannels/ConvertSampleType/PremultiplyAlpha/UnpremultiplyAlpha/ApplyOrientation/ColorTransform
// steps are implemented - Execute throws NotSupportedException with the step name for anything else,
// so a gap here is loud rather than silently wrong.
public static class ConversionExecutor
{
    private readonly record struct ChannelLocation(int RowRelativeBase, int PerPixelStride, int BytesPerSample);

    private static readonly HashSet<string> s_ColorChannelNames = ["R", "G", "B", "Y"];

    public static void Execute(ConversionPlan plan, IImageReader source, SampleByteOrder sourceByteOrder, IImageWriter target, SampleByteOrder targetByteOrder)
    {
        for (var i = 0; i < plan.Parts.Count; i++) {
            var partPlan = plan.Parts[i];
            var sourcePart = plan.SourceDescriptor.Parts[partPlan.SourcePartIndex];
            var targetPart = plan.TargetDescriptor.Parts[i];

            foreach (var step in partPlan.Steps) {
                if (step is not (SelectPartStep or SelectChannelsStep or ConvertSampleTypeStep or PremultiplyAlphaStep or UnpremultiplyAlphaStep
                    or ApplyOrientationStep or ColorTransformStep or DropMetadataStep or PreserveMetadataStep
                    or DecodeEncodedElementsStep or EncodeEncodedElementsStep or TranscodeEncodedElementsStep or SynthesizeChannelStep or ExpandIndexedStep)) {
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
                source.Read(region, encoded);
                target.Write(region with { Subresource = region.Subresource with { Part = i } }, encoded);
                continue;
            }

            var floatChannels = new Dictionary<ChannelPath, float[]>();
            if (sourcePart.Representation is PlainSampleRepresentation sourcePlain) {
                var sourceChannels = sourcePart.Channels.Channels.ToDictionary(c => c.Name);
                var (sourceLocations, sourceRowStride) = ComputeLayout(sourcePlain.Planes, sourceChannels, width);
                var sourceBuffer = new byte[height * sourceRowStride];
                source.Read(region, sourceBuffer);

                foreach (var plane in sourcePlain.Planes) {
                    foreach (var channelName in plane.Channels) {
                        var channel = sourceChannels[channelName];
                        var location = sourceLocations[channelName];

                        var raw = new byte[pixelCount * location.BytesPerSample];
                        for (var y = 0; y < height; y++) {
                            var rowBase = (y * sourceRowStride) + location.RowRelativeBase;
                            for (var x = 0; x < width; x++) {
                                sourceBuffer.AsSpan(rowBase + (x * location.PerPixelStride), location.BytesPerSample)
                                    .CopyTo(raw.AsSpan(((y * width) + x) * location.BytesPerSample, location.BytesPerSample));
                            }
                        }

                        var floats = new float[pixelCount];
                        SampleTypeConversionKernel.ToFloat32(raw, channel.SampleType, sourceByteOrder, floats);
                        floatChannels[channelName] = floats;
                    }
                }
            }
            else if (sourcePart.Representation is EncodedElementRepresentation { Format.Name: nameof(EncodedFormatId.Rgbe) } &&
                partPlan.Steps.OfType<DecodeEncodedElementsStep>().Any(step => step.Format == EncodedFormatId.Rgbe)) {
                var encoded = new byte[checked(pixelCount * 4)];
                source.Read(region, encoded);
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
                source.Read(region, encoded);
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
                source.Read(region, encoded);
                var decoded = new float[checked(pixelCount * 3)];
                Bc6HImageCodec.Decode(encoded, width, height, bc6H.Format == EncodedFormatId.Bc6HSigned, decoded);
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
                source.Read(region, encoded);
                var channelCount = BcImageCodec.ChannelCount(bcFormat);
                var decoded = new byte[checked(pixelCount * channelCount)];
                BcImageCodec.Decode(bcFormat, encoded, width, height, decoded);
                for (var channelIndex = 0; channelIndex < channelCount; channelIndex++) {
                    var raw = new byte[pixelCount];
                    for (var pixel = 0; pixel < pixelCount; pixel++) {
                        raw[pixel] = decoded[(pixel * channelCount) + channelIndex];
                    }

                    var values = new float[pixelCount];
                    SampleTypeConversionKernel.ToFloat32(raw, SampleType.UNorm8, SampleByteOrder.LittleEndian, values);
                    floatChannels[sourcePart.Channels.Channels[channelIndex].Name] = values;
                }
            }
            else if (sourcePart.Representation is IndexedRepresentation indexed &&
                partPlan.Steps.OfType<ExpandIndexedStep>().Any()) {
                var indexBytesPerSample = indexed.IndexType.Bits / 8;
                var indexBuffer = new byte[checked(pixelCount * indexBytesPerSample)];
                source.Read(region, indexBuffer);

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

                for (var channelIndex = 0; channelIndex < entryChannels.Count; channelIndex++) {
                    var raw = new byte[checked(pixelCount * entryBytesPerSample)];
                    for (var pixel = 0; pixel < pixelCount; pixel++) {
                        var entryOffset = (indices[pixel] * entryStride) + (channelIndex * entryBytesPerSample);
                        rawEntries.AsSpan(entryOffset, entryBytesPerSample).CopyTo(raw.AsSpan(pixel * entryBytesPerSample, entryBytesPerSample));
                    }

                    var values = new float[pixelCount];
                    SampleTypeConversionKernel.ToFloat32(raw, indexed.Palette.EntrySampleType, sourceByteOrder, values);
                    floatChannels[entryChannels[channelIndex].Name] = values;
                }
            }
            else {
                throw new NotSupportedException($"ConversionExecutor does not support {sourcePart.Representation.GetType().Name} as a source.");
            }

            foreach (var step in partPlan.Steps) {
                switch (step) {
                    case SynthesizeChannelStep synthesizeStep:
                        floatChannels[synthesizeStep.Channel] = Enumerable.Repeat((float)synthesizeStep.ConstantValue, pixelCount).ToArray();
                        break;

                    case ApplyOrientationStep orientationStep: {
                            var reoriented = new Dictionary<ChannelPath, float[]>();
                            var newWidth = width;
                            var newHeight = height;
                            foreach (var (name, values) in floatChannels) {
                                var destination = new float[values.Length];
                                (newWidth, newHeight) = OrientationKernel.ApplyToIdentity(values, width, height, orientationStep.From, destination);
                                reoriented[name] = destination;
                            }

                            floatChannels = reoriented;
                            width = newWidth;
                            height = newHeight;
                            pixelCount = width * height;
                            region = region with { Region = ImageBox.FromOrigin(width, height) };
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

                    case ColorTransformStep colorStep:
                        foreach (var (name, values) in floatChannels) {
                            if (!s_ColorChannelNames.Contains(name.FullName)) {
                                continue;
                            }

                            if (colorStep is { From: TransferFunction.Linear, To: TransferFunction.Srgb }) {
                                ColorTransformKernel.LinearToSrgb(values);
                            }
                            else if (colorStep is { From: TransferFunction.Srgb, To: TransferFunction.Linear }) {
                                ColorTransformKernel.SrgbToLinear(values);
                            }
                            else {
                                throw new NotSupportedException($"ConversionExecutor does not support a color transform from {colorStep.From} to {colorStep.To}.");
                            }
                        }

                        break;
                }
            }

            if (targetPart.Representation is EncodedElementRepresentation { Format.Name: nameof(EncodedFormatId.Rgbe) } &&
                partPlan.Steps.OfType<EncodeEncodedElementsStep>().Any(step => step.Format == EncodedFormatId.Rgbe)) {
                var encoded = new byte[checked(pixelCount * 4)];
                RgbeConversionKernel.Encode(floatChannels["R"], floatChannels["G"], floatChannels["B"], encoded);
                target.Write(region, encoded);
                continue;
            }

            if (targetPart.Representation is EncodedElementRepresentation packedTarget &&
                IsPackedPixelFormat(packedTarget.Format) &&
                partPlan.Steps.OfType<EncodeEncodedElementsStep>().Any(step => step.Format == packedTarget.Format)) {
                var encoded = new byte[checked(pixelCount * ((packedTarget.BitsPerElement + 7) / 8))];
                var alpha = packedTarget.Format.Name is nameof(EncodedFormatId.R10G10B10A2) or nameof(EncodedFormatId.B5G5R5A1) ? floatChannels["A"] : [];
                PackedPixelConversionKernel.Encode(packedTarget.Format, floatChannels["R"], floatChannels["G"], floatChannels["B"], alpha, encoded);
                target.Write(region with { Subresource = region.Subresource with { Part = i } }, encoded);
                continue;
            }

            if (targetPart.Representation is EncodedElementRepresentation blockTarget &&
                TryGetBcFormat(blockTarget.Format, out var targetBcFormat) &&
                partPlan.Steps.OfType<EncodeEncodedElementsStep>().Any(step => step.Format == blockTarget.Format)) {
                var channelCount = BcImageCodec.ChannelCount(targetBcFormat);
                var interleaved = new byte[checked(pixelCount * channelCount)];
                for (var channelIndex = 0; channelIndex < channelCount; channelIndex++) {
                    var channel = targetPart.Channels.Channels[channelIndex];
                    var raw = new byte[pixelCount];
                    SampleTypeConversionKernel.FromFloat32(floatChannels[channel.Name], SampleType.UNorm8, SampleByteOrder.LittleEndian, raw);
                    for (var pixel = 0; pixel < pixelCount; pixel++) {
                        interleaved[(pixel * channelCount) + channelIndex] = raw[pixel];
                    }
                }

                var encoded = new byte[BcImageCodec.EncodedByteCount(targetBcFormat, width, height)];
                BcImageCodec.Encode(targetBcFormat, interleaved, width, height, encoded);
                target.Write(region with { Subresource = region.Subresource with { Part = i } }, encoded);
                continue;
            }

            if (targetPart.Representation is not PlainSampleRepresentation targetPlain) {
                throw new NotSupportedException($"ConversionExecutor does not support {targetPart.Representation.GetType().Name} as a target.");
            }

            var targetChannels = targetPart.Channels.Channels.ToDictionary(c => c.Name);
            var (targetLocations, targetRowStride) = ComputeLayout(targetPlain.Planes, targetChannels, width);

            var targetBuffer = new byte[height * targetRowStride];
            foreach (var plane in targetPlain.Planes) {
                foreach (var channelName in plane.Channels) {
                    var channel = targetChannels[channelName];
                    var location = targetLocations[channelName];
                    var floats = floatChannels[channelName];

                    var raw = new byte[pixelCount * location.BytesPerSample];
                    SampleTypeConversionKernel.FromFloat32(floats, channel.SampleType, targetByteOrder, raw);

                    for (var y = 0; y < height; y++) {
                        var rowBase = (y * targetRowStride) + location.RowRelativeBase;
                        for (var x = 0; x < width; x++) {
                            raw.AsSpan(((y * width) + x) * location.BytesPerSample, location.BytesPerSample)
                                .CopyTo(targetBuffer.AsSpan(rowBase + (x * location.PerPixelStride), location.BytesPerSample));
                        }
                    }
                }
            }

            target.Write(region, targetBuffer);
        }

        target.Finish();
    }

    private static (Dictionary<ChannelPath, ChannelLocation> Locations, int RowStrideBytes) ComputeLayout(
        IReadOnlyList<SamplePlaneDescriptor> planes, IReadOnlyDictionary<ChannelPath, ChannelDescriptor> channels, int width)
    {
        var locations = new Dictionary<ChannelPath, ChannelLocation>();
        var rowOffset = 0;

        foreach (var plane in planes) {
            var bytesPerSample = plane.Channels.ToDictionary(c => c, c => BytesPerSample(channels[c]));
            var planeBytesPerPixel = bytesPerSample.Values.Sum();
            var withinPlaneOffset = 0;

            foreach (var channelName in plane.Channels) {
                var sampleSize = bytesPerSample[channelName];
                var perPixelStride = plane.Layout == PlaneLayout.Interleaved ? planeBytesPerPixel : sampleSize;
                locations[channelName] = new ChannelLocation(rowOffset + withinPlaneOffset, perPixelStride, sampleSize);
                withinPlaneOffset += plane.Layout == PlaneLayout.Interleaved ? sampleSize : sampleSize * width;
            }

            rowOffset += planeBytesPerPixel * width;
        }

        return (locations, rowOffset);
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
