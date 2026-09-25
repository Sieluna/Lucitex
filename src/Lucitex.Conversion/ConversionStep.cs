using Lucitex.Core.Color;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Spatial;
using Lucitex.Core.Topology;

namespace Lucitex.Conversion;

public abstract record ConversionStep;

public sealed record SelectPartStep(int PartIndex) : ConversionStep;

public sealed record SelectChannelsStep(IReadOnlyList<ChannelPath> Channels) : ConversionStep;

public readonly record struct ChannelMapping(ChannelPath Source, ChannelPath Target);

public sealed record MapChannelsStep(IReadOnlyList<ChannelMapping> Mappings) : ConversionStep;

public sealed record SynthesizeChannelStep(ChannelPath Channel, SampleType SampleType, double ConstantValue) : ConversionStep;

public sealed record ExpandIndexedStep : ConversionStep;

public sealed record QuantizeToPaletteStep(int MaxEntries) : ConversionStep;

public sealed record DecodeEncodedElementsStep(EncodedFormatId Format) : ConversionStep;

public sealed record EncodeEncodedElementsStep(EncodedFormatId Format) : ConversionStep;

public sealed record TranscodeEncodedElementsStep(EncodedFormatId SourceFormat, EncodedFormatId TargetFormat) : ConversionStep;

public sealed record ConvertSampleTypeStep(ChannelPath Channel, SampleType From, SampleType To) : ConversionStep;

public sealed record ChangeSampleGridStep(ChannelPath Channel, SampleGrid From, SampleGrid To) : ConversionStep;

public sealed record ApplyOrientationStep(LogicalOrientation From, LogicalOrientation To) : ConversionStep;

public sealed record ResampleStep(int TargetWidth, int TargetHeight) : ConversionStep;

public sealed record CropStep(int X, int Y, int Width, int Height) : ConversionStep;

public sealed record MapWindowStep(ImageBox From, ImageBox To) : ConversionStep;

public sealed record PremultiplyAlphaStep : ConversionStep;

public sealed record UnpremultiplyAlphaStep : ConversionStep;

public sealed record ColorTransformStep(TransferFunction From, TransferFunction To) : ConversionStep;

public sealed record RemoveSupercompressionStep(SupercompressionScheme Scheme) : ConversionStep;

public sealed record ApplySupercompressionStep(SupercompressionScheme Scheme) : ConversionStep;

public sealed record PreserveMetadataStep(string Namespace) : ConversionStep;

public sealed record TranslateMetadataStep(string SourceNamespace, string TargetNamespace) : ConversionStep;

public sealed record DropMetadataStep(string Namespace, string Reason) : ConversionStep;
