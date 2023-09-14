using System.Buffers.Binary;

namespace Lucitex.Ktx2.Format;

// Generates a minimal, structurally valid Khronos Data Format Descriptor (KDF) "basic
// descriptor block" for the formats this codec supports. Lucitex's own reader never parses
// this back — it trusts vkFormat as the source of truth, the same way ExrHeader/DdsHeader
// carry their own authoritative type. The DFD exists so the file matches the KTX2 container
// contract and so third-party tools have something structurally sane to look at.
//
// This is a best-effort rendering of the KDF "basic" model (colorModel = KHR_DF_MODEL_RGBSDA
// for uncompressed formats) and, for block-compressed formats, a single opaque sample spanning
// the whole block rather than a full per-channel breakdown.
//
// The raw/uncompressed path is cross-checked against libktx (the Khronos reference
// implementation) via tests/Lucitex.Fuzz.Native's ktx2 oracle. The block-compressed color-model
// IDs are not exercised that way yet — Lucitex's own BC1-BC7 kernels have no cross-platform
// reference to validate their block content against, so BC-compressed KTX2 files are excluded
// from oracle testing for now, and their DFD color-model IDs remain approximate, not
// spec-verified.
internal static class Ktx2DfdWriter
{
    private const byte k_ColorModelRgbsda = 1;
    private const byte k_ColorModelBc1A = 128;
    private const byte k_ColorModelBc2 = 129;
    private const byte k_ColorModelBc3 = 130;
    private const byte k_ColorModelBc4 = 131;
    private const byte k_ColorModelBc5 = 132;
    private const byte k_ColorModelBc6H = 133;
    private const byte k_ColorModelBc7 = 134;

    private const byte k_ChannelTypeSigned = 0x40;
    private const byte k_ChannelTypeFloat = 0x80;

    public static byte[] Write(VkFormat format, IReadOnlyList<(string Name, int BitLength, bool Float, bool Signed)>? uncompressedChannels)
    {
        var info = Ktx2FormatTable.Get(format);

        using var body = new MemoryStream();
        var writer = new Ktx2BinaryWriter(body);

        if (info.Kind == Ktx2FormatKind.Block) {
            WriteBasicBlockHeader(
                writer,
                ColorModelFor(format),
                blockWidth: info.BlockWidth,
                blockHeight: info.BlockHeight,
                bytesPerBlock: info.BytesPerElement,
                sampleCount: 1);

            WriteSample(writer, bitOffset: 0, bitLength: (info.BytesPerElement * 8) - 1, channelType: 0, floatType: false, signedType: false);
        }
        else if (format == VkFormat.E5B9G9R9Ufloat) {
            WriteBasicBlockHeader(
                writer,
                k_ColorModelRgbsda,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerBlock: info.BytesPerElement,
                sampleCount: 6);

            for (byte channel = 0; channel < 3; channel++) {
                WriteSample(writer, channel * 9, 8, channel, floatType: false, signedType: false, sampleLower: 0, sampleUpper: 0x2100);
                WriteSample(writer, 27, 4, channel, floatType: false, signedType: false, additionalQualifiers: 0x20, sampleLower: 15, sampleUpper: 31);
            }
        }
        else {
            var channels = uncompressedChannels ?? throw new ArgumentNullException(nameof(uncompressedChannels));

            WriteBasicBlockHeader(
                writer,
                k_ColorModelRgbsda,
                blockWidth: 1,
                blockHeight: 1,
                bytesPerBlock: info.BytesPerElement,
                sampleCount: channels.Count);

            var bitOffset = 0;
            foreach (var channel in channels) {
                var channelType = ChannelIdFor(channel.Name);
                WriteSample(writer, bitOffset, channel.BitLength - 1, channelType, channel.Float, channel.Signed);
                bitOffset += channel.BitLength;
            }
        }

        var descriptorBlock = body.ToArray();

        using var whole = new MemoryStream();
        Span<byte> totalSize = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(totalSize, (uint)(4 + descriptorBlock.Length));
        whole.Write(totalSize);
        whole.Write(descriptorBlock);

        return whole.ToArray();
    }

    private static void WriteBasicBlockHeader(
        Ktx2BinaryWriter writer,
        byte colorModel,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock,
        int sampleCount)
    {
        const int headerSize = 24;
        var blockSize = headerSize + (sampleCount * 16);

        const uint versionNumber = 2;
        writer.WriteUInt32(0);
        writer.WriteUInt32(((uint)blockSize << 16) | versionNumber);

        writer.Stream.WriteByte(colorModel);
        writer.Stream.WriteByte(1);
        writer.Stream.WriteByte(1);
        writer.Stream.WriteByte(0);

        writer.Stream.WriteByte((byte)(blockWidth - 1));
        writer.Stream.WriteByte((byte)(blockHeight - 1));
        writer.Stream.WriteByte(0);
        writer.Stream.WriteByte(0);

        writer.Stream.WriteByte((byte)Math.Min(255, bytesPerBlock));
        for (var i = 0; i < 7; i++) {
            writer.Stream.WriteByte(0);
        }
    }

    private static void WriteSample(
        Ktx2BinaryWriter writer,
        int bitOffset,
        int bitLength,
        byte channelType,
        bool floatType,
        bool signedType,
        byte additionalQualifiers = 0,
        uint? sampleLower = null,
        uint? sampleUpper = null)
    {
        Span<byte> offsetAndLength = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(offsetAndLength, (ushort)bitOffset);
        offsetAndLength[2] = (byte)bitLength;

        var qualifiedChannelType = (byte)(channelType | additionalQualifiers);
        if (floatType) {
            qualifiedChannelType |= k_ChannelTypeFloat;
        }

        if (signedType) {
            qualifiedChannelType |= k_ChannelTypeSigned;
        }

        offsetAndLength[3] = qualifiedChannelType;
        writer.WriteBytes(offsetAndLength);

        writer.Stream.WriteByte(0);
        writer.Stream.WriteByte(0);
        writer.Stream.WriteByte(0);
        writer.Stream.WriteByte(0);

        var defaultUpper = floatType ? 0x3F800000u : bitLength >= 31 ? uint.MaxValue : (1u << (bitLength + 1)) - 1u;
        writer.WriteUInt32(sampleLower ?? (floatType && signedType ? 0xBF800000u : 0u));
        writer.WriteUInt32(sampleUpper ?? defaultUpper);
    }

    private static byte ChannelIdFor(string name) => name switch {
        "R" => 0,
        "G" => 1,
        "B" => 2,
        "A" => 15,
        _ => 0,
    };

    private static byte ColorModelFor(VkFormat format) => format switch {
        VkFormat.Bc1RgbUnormBlock or VkFormat.Bc1RgbaUnormBlock => k_ColorModelBc1A,
        VkFormat.Bc2UnormBlock => k_ColorModelBc2,
        VkFormat.Bc3UnormBlock => k_ColorModelBc3,
        VkFormat.Bc4UnormBlock or VkFormat.Bc4SnormBlock => k_ColorModelBc4,
        VkFormat.Bc5UnormBlock or VkFormat.Bc5SnormBlock => k_ColorModelBc5,
        VkFormat.Bc6HUfloatBlock or VkFormat.Bc6HSfloatBlock => k_ColorModelBc6H,
        VkFormat.Bc7UnormBlock => k_ColorModelBc7,
        _ => throw new NotSupportedException($"No known DFD color model for {format}."),
    };
}
