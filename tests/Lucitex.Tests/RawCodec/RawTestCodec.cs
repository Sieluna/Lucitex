using System.Buffers.Binary;
using System.Text;
using Lucitex.Core.Execution;
using Lucitex.Core.Execution.Codecs;
using Lucitex.Core.Representation;
using Lucitex.Core.Sampling;
using Lucitex.Core.Semantic;
using Lucitex.Core.Serialization;
using Lucitex.Core.Spatial;

namespace Lucitex.Tests.RawCodec;

public sealed class RawTestCodec : IImageCodec
{
    private static ReadOnlySpan<byte> Magic => "RAW1"u8;

    public string FormatId => "raw-test";

    public IReadOnlyList<string> Extensions { get; } = [".rawtest"];

    public FormatProbeResult Probe(ReadOnlySpan<byte> header)
    {
        if (header.Length < Magic.Length)
        {
            return FormatProbeResult.NoMatch(Magic.Length);
        }

        return header[..Magic.Length].SequenceEqual(Magic)
            ? new FormatProbeResult { Format = FormatId, Confidence = ProbeConfidence.Certain, RequiredBytes = Magic.Length }
            : FormatProbeResult.NoMatch(Magic.Length);
    }

    public IImageReader OpenReader(Stream stream, DecodeLimits? limits = null) =>
        new RawTestReader(stream, limits ?? DecodeLimits.Default);

    public IImageWriter CreateWriter(Stream stream, ImageAssetDescriptor descriptor) =>
        new RawTestWriter(stream, descriptor);

    internal static int SampleByteSize(SampleType type) => type.Bits switch
    {
        <= 8 => 1,
        <= 16 => 2,
        <= 32 => 4,
        _ => 8,
    };
}

internal sealed class RawTestReader : IImageReader
{
    private readonly Stream _stream;
    private readonly ImageAssetDescriptor _descriptor;
    private readonly long _dataStart;
    private readonly long _width;
    private readonly int _bytesPerTexel;

    public RawTestReader(Stream stream, DecodeLimits limits)
    {
        _stream = stream;
        stream.Position = 0;

        Span<byte> magic = stackalloc byte[4];
        stream.ReadExactly(magic);
        if (!magic.SequenceEqual("RAW1"u8))
        {
            throw new Lucitex.Core.Execution.ImageFormatException("raw-test", "BadMagic", "Stream is not a raw-test asset.");
        }

        Span<byte> lengthBytes = stackalloc byte[4];
        stream.ReadExactly(lengthBytes);
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        var jsonBytes = new byte[jsonLength];
        stream.ReadExactly(jsonBytes);
        _descriptor = DescriptorJsonSerializer.Deserialize(Encoding.UTF8.GetString(jsonBytes));

        var violations = DecodeLimitsValidator.Validate(_descriptor, limits);
        if (violations.Count > 0)
        {
            throw new Lucitex.Core.Execution.ImageFormatException(
                "raw-test",
                "LimitExceeded",
                string.Join("; ", violations.Select(v => v.Message)));
        }

        Span<byte> bytesPerTexelBytes = stackalloc byte[4];
        stream.ReadExactly(bytesPerTexelBytes);
        _bytesPerTexel = BinaryPrimitives.ReadInt32LittleEndian(bytesPerTexelBytes);

        _dataStart = stream.Position;
        _width = _descriptor.Parts[0].Topology.BaseExtent.Width;
    }

    public ImageAssetDescriptor Describe() => _descriptor;

    public int Read(WorkRegion region, Span<byte> destination)
    {
        var rowBytes = checked((int)(region.Region.Width * _bytesPerTexel));
        var written = 0;

        for (var y = region.Region.MinY; y < region.Region.MaxYExclusive; y++)
        {
            var rowOffset = _dataStart + ((y * _width) + region.Region.MinX) * _bytesPerTexel;
            _stream.Position = rowOffset;
            _stream.ReadExactly(destination.Slice(written, rowBytes));
            written += rowBytes;
        }

        return written;
    }
}

internal sealed class RawTestWriter : IImageWriter
{
    private readonly Stream _stream;
    private readonly long _dataStart;
    private readonly long _width;
    private readonly int _bytesPerTexel;

    public RawTestWriter(Stream stream, ImageAssetDescriptor descriptor)
    {
        if (!stream.CanSeek)
        {
            throw new ArgumentException("raw-test writer requires a seekable stream.", nameof(stream));
        }

        _stream = stream;

        var part = descriptor.Parts[0];
        _width = part.Topology.BaseExtent.Width;

        var plane = ((PlainSampleRepresentation)part.Representation).Planes[0];
        var channelSampleType = part.Channels.Channels[0].SampleType;
        _bytesPerTexel = plane.Channels.Count * RawTestCodec.SampleByteSize(channelSampleType);

        var json = DescriptorJsonSerializer.Serialize(descriptor);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        stream.Write("RAW1"u8);

        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, jsonBytes.Length);
        stream.Write(lengthBytes);
        stream.Write(jsonBytes);

        Span<byte> bytesPerTexelBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytesPerTexelBytes, _bytesPerTexel);
        stream.Write(bytesPerTexelBytes);

        _dataStart = stream.Position;
    }

    public WriterExecutionContract Contract { get; } = new()
    {
        RequiresDescriptorUpfront = true,
        RequiresDimensionsUpfront = true,
        WriteGranularity = new Extent3I(1, 1, 1),
        WriteOrder = WriteOrder.Arbitrary,
        RandomAccess = true,
        RequiresSeekableOutput = true,
        SupportsIncompleteLevels = false,
        SupportsSparseRegions = false,
    };

    public void Write(WorkRegion region, ReadOnlySpan<byte> data)
    {
        var rowBytes = checked((int)(region.Region.Width * _bytesPerTexel));
        var rowCount = region.Region.Height;

        for (var i = 0; i < rowCount; i++)
        {
            var y = region.Region.MinY + i;
            var rowOffset = _dataStart + ((y * _width) + region.Region.MinX) * _bytesPerTexel;
            _stream.Position = rowOffset;
            _stream.Write(data.Slice(i * rowBytes, rowBytes));
        }
    }

    public void Finish() => _stream.Flush();
}
