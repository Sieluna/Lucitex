using System.Buffers.Binary;

namespace Lucitex.Ktx2.Format;

internal static class Ktx2DfdValidator
{
    private const int k_BasicBlockHeaderSize = 24;
    private const int k_SampleSize = 16;
    private const byte k_ExponentQualifier = 0x20;
    private const byte k_ChannelIdMask = 0x0F;

    public static void Validate(ReadOnlySpan<byte> data, VkFormat format)
    {
        if (data.Length < sizeof(uint) + k_BasicBlockHeaderSize) {
            throw new InvalidDataException("KTX2 data format descriptor is too short.");
        }

        var totalSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (totalSize != data.Length) {
            throw new InvalidDataException("KTX2 data format descriptor size does not match its index entry.");
        }

        var offset = sizeof(uint);
        var foundBasicBlock = false;
        while (offset < data.Length) {
            if (data.Length - offset < 8) {
                throw new InvalidDataException("KTX2 data format descriptor block header is truncated.");
            }

            var identifier = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            var versionAndSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            var blockSize = checked((int)(versionAndSize >> 16));
            if (blockSize < 8 || (blockSize & 3) != 0 || blockSize > data.Length - offset) {
                throw new InvalidDataException("KTX2 data format descriptor block size is invalid.");
            }

            if (identifier == 0) {
                if (foundBasicBlock) {
                    throw new InvalidDataException("KTX2 contains more than one basic data format descriptor block.");
                }

                ValidateBasicBlock(data.Slice(offset, blockSize), versionAndSize, format);
                foundBasicBlock = true;
            }

            offset += blockSize;
        }

        if (offset != data.Length || !foundBasicBlock) {
            throw new InvalidDataException("KTX2 data format descriptor does not contain one complete basic block.");
        }
    }

    private static void ValidateBasicBlock(ReadOnlySpan<byte> block, uint versionAndSize, VkFormat format)
    {
        if ((versionAndSize & 0xFFFF) != 2 || block.Length < k_BasicBlockHeaderSize ||
            (block.Length - k_BasicBlockHeaderSize) % k_SampleSize != 0) {
            throw new InvalidDataException("KTX2 basic data format descriptor has an invalid version or size.");
        }

        var info = Ktx2FormatTable.Get(format);
        if (block[10] > 18) {
            throw new InvalidDataException("KTX2 data format descriptor has an invalid transfer function.");
        }
        if (block[10] is not 1 and not 2) {
            throw new NotSupportedException($"KTX2 transfer function {block[10]} is not supported.");
        }
        if (block[12] != info.BlockWidth - 1 || block[13] != info.BlockHeight - 1 || block[14] != 0 || block[15] != 0 ||
            block[16] != info.BytesPerElement || !block[17..24].SequenceEqual(new byte[7])) {
            throw new InvalidDataException("KTX2 basic data format descriptor does not match vkFormat.");
        }

        var sampleCount = (block.Length - k_BasicBlockHeaderSize) / k_SampleSize;
        if (sampleCount == 0) {
            throw new InvalidDataException("KTX2 basic data format descriptor contains no samples.");
        }

        // Shared-exponent formats (e.g. RGB9E5) legitimately describe their exponent bits once per
        // channel, each as its own sample tagged with the EXPONENT qualifier - those samples alias
        // the same physical bits (one shared exponent field), so they're checked for staying in
        // bounds and agreeing with each other on the bit range, but don't have to be sequential and
        // only count toward total coverage once, not once per channel that references them. Every
        // regular (mantissa) channel must have exactly one matching exponent-tagged sample - a mutated
        // file that drops one channel's exponent tag while duplicating another's would otherwise still
        // pass the bit-range check above, so channel identities are cross-checked too.
        var bitCount = checked(info.BytesPerElement * 8);
        var coveredBits = 0;
        int? exponentBitOffset = null;
        int? exponentBitLength = null;
        var mantissaChannels = new HashSet<int>();
        var exponentChannels = new HashSet<int>();

        for (var i = 0; i < sampleCount; i++) {
            var sample = block.Slice(k_BasicBlockHeaderSize + (i * k_SampleSize), k_SampleSize);
            var bitOffset = BinaryPrimitives.ReadUInt16LittleEndian(sample);
            var sampleBits = sample[2] + 1;
            var channelType = sample[3];
            var channelId = channelType & k_ChannelIdMask;

            if (!sample[4..8].SequenceEqual(new byte[4])) {
                throw new InvalidDataException("KTX2 basic data format descriptor sample layout is invalid.");
            }

            if ((channelType & k_ExponentQualifier) != 0) {
                if (bitOffset + sampleBits > bitCount) {
                    throw new InvalidDataException("KTX2 basic data format descriptor sample layout is invalid.");
                }

                if (exponentBitOffset is null) {
                    exponentBitOffset = bitOffset;
                    exponentBitLength = sampleBits;
                }
                else if (exponentBitOffset != bitOffset || exponentBitLength != sampleBits) {
                    throw new InvalidDataException("KTX2 basic data format descriptor exponent samples do not agree on the shared exponent bit range.");
                }

                if (!exponentChannels.Add(channelId)) {
                    throw new InvalidDataException("KTX2 basic data format descriptor has more than one exponent sample for the same channel.");
                }

                continue;
            }

            if (bitOffset != coveredBits || sampleBits > bitCount - coveredBits) {
                throw new InvalidDataException("KTX2 basic data format descriptor sample layout is invalid.");
            }

            if (!mantissaChannels.Add(channelId)) {
                throw new InvalidDataException("KTX2 basic data format descriptor has more than one sample for the same channel.");
            }

            coveredBits += sampleBits;
        }

        if (coveredBits + (exponentBitLength ?? 0) != bitCount) {
            throw new InvalidDataException("KTX2 basic data format descriptor samples do not cover one texel block.");
        }

        if (exponentBitLength is not null && !exponentChannels.SetEquals(mantissaChannels)) {
            throw new InvalidDataException("KTX2 basic data format descriptor exponent samples do not match one-to-one with the channels that have them.");
        }
    }
}
