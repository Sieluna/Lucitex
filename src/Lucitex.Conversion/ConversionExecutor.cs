using Lucitex.Conversion.Kernels;
using Lucitex.Core.Color;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

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
                    or ApplyOrientationStep or ColorTransformStep or DropMetadataStep or PreserveMetadataStep)) {
                    throw new NotSupportedException($"ConversionExecutor does not support {step.GetType().Name} yet.");
                }
            }

            if (sourcePart.Representation is not PlainSampleRepresentation sourcePlain) {
                throw new NotSupportedException("ConversionExecutor only supports PlainSampleRepresentation parts.");
            }

            if (targetPart.Representation is not PlainSampleRepresentation targetPlain) {
                throw new NotSupportedException("ConversionExecutor only supports PlainSampleRepresentation parts.");
            }

            var window = sourcePart.Spatial.DataWindow;
            var width = checked((int)window.Width);
            var height = checked((int)window.Height);
            var pixelCount = width * height;
            var region = new WorkRegion { Subresource = new SubresourceId(partPlan.SourcePartIndex, 0, 0, LevelKey.Base), Region = window };

            var sourceChannels = sourcePart.Channels.Channels.ToDictionary(c => c.Name);
            var (sourceLocations, sourceRowStride) = ComputeLayout(sourcePlain.Planes, sourceChannels, width);

            var sourceBuffer = new byte[height * sourceRowStride];
            source.Read(region, sourceBuffer);

            var floatChannels = new Dictionary<ChannelPath, float[]>();
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

            foreach (var step in partPlan.Steps) {
                switch (step) {
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
}
