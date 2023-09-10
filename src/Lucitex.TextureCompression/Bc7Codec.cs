using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;



namespace Lucitex.TextureCompression
{
    internal enum Bc7Mode : uint
    {
        Type0,
        Type1,
        Type2,
        Type3,
        Type4,
        Type5,
        Type6,
        Type7,
        Type8Reserved
    }

    internal struct Bc7DecoderBlock
    {
        public ulong LowBits;
        public ulong HighBits;


        public static readonly int[][] Subsets2PartitionTable = {
            new[] {0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1},
            new[] {0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1},
            new[] {0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1},
            new[] {0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1},
            new[] {0, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1},
            new[] {0, 0, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1},
            new[] {0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1},
            new[] {0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1},
            new[] {0, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0},
            new[] {0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0},
            new[] {0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0},
            new[] {0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0},
            new[] {0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1},
            new[] {0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0},
            new[] {0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0},
            new[] {0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0},
            new[] {0, 0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0},
            new[] {0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0},
            new[] {0, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0},
            new[] {0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0},
            new[] {0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1},
            new[] {0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0},
            new[] {0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0},
            new[] {0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0},
            new[] {0, 1, 0, 1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0},
            new[] {0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 1},
            new[] {0, 1, 0, 1, 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 1},
            new[] {0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0},
            new[] {0, 0, 0, 1, 0, 0, 1, 1, 1, 1, 0, 0, 1, 0, 0, 0},
            new[] {0, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1, 0, 0},
            new[] {0, 0, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0},
            new[] {0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 0},
            new[] {0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 1},
            new[] {0, 1, 1, 0, 0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1},
            new[] {0, 0, 0, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0},
            new[] {0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0},
            new[] {0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0},
            new[] {0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0},
            new[] {0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0},
            new[] {0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 1},
            new[] {0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 1},
            new[] {0, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0},
            new[] {0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 0},
            new[] {0, 1, 1, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 1},
            new[] {0, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1},
            new[] {0, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1},
            new[] {0, 0, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1},
            new[] {0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0},
            new[] {0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0},
            new[] {0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1}
        };

        public static readonly int[][] Subsets3PartitionTable = {
            new[] {0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 1, 2, 2, 2, 2},
            new[] {0, 0, 0, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 2, 1},
            new[] {0, 0, 0, 0, 2, 0, 0, 1, 2, 2, 1, 1, 2, 2, 1, 1},
            new[] {0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 1, 0, 1, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2},
            new[] {0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 2, 2},
            new[] {0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1},
            new[] {0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2},
            new[] {0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2},
            new[] {0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2},
            new[] {0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2},
            new[] {0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2, 1, 2, 2, 2},
            new[] {0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0, 2, 2, 2, 0},
            new[] {0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2},
            new[] {0, 1, 1, 1, 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0},
            new[] {0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2},
            new[] {0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1},
            new[] {0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2, 0, 2, 2, 2},
            new[] {0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2, 1, 2, 2, 2, 1},
            new[] {0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2},
            new[] {0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 1, 0, 2, 2, 1, 0},
            new[] {0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0},
            new[] {0, 0, 1, 2, 0, 0, 1, 2, 1, 1, 2, 2, 2, 2, 2, 2},
            new[] {0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1, 0, 1, 1, 0},
            new[] {0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1},
            new[] {0, 0, 2, 2, 1, 1, 0, 2, 1, 1, 0, 2, 0, 0, 2, 2},
            new[] {0, 1, 1, 0, 0, 1, 1, 0, 2, 0, 0, 2, 2, 2, 2, 2},
            new[] {0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1},
            new[] {0, 0, 0, 0, 2, 0, 0, 0, 2, 2, 1, 1, 2, 2, 2, 1},
            new[] {0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 2, 2, 2},
            new[] {0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 2, 0, 0, 1, 1},
            new[] {0, 0, 1, 1, 0, 0, 1, 2, 0, 0, 2, 2, 0, 2, 2, 2},
            new[] {0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0},
            new[] {0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0},
            new[] {0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0},
            new[] {0, 1, 2, 0, 2, 0, 1, 2, 1, 2, 0, 1, 0, 1, 2, 0},
            new[] {0, 0, 1, 1, 2, 2, 0, 0, 1, 1, 2, 2, 0, 0, 1, 1},
            new[] {0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0, 1, 1},
            new[] {0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1},
            new[] {0, 0, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2, 1, 1, 2, 2},
            new[] {0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1},
            new[] {0, 2, 2, 0, 1, 2, 2, 1, 0, 2, 2, 0, 1, 2, 2, 1},
            new[] {0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 0, 1, 0, 1},
            new[] {0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1},
            new[] {0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2},
            new[] {0, 2, 2, 2, 0, 1, 1, 1, 0, 2, 2, 2, 0, 1, 1, 1},
            new[] {0, 0, 0, 2, 1, 1, 1, 2, 0, 0, 0, 2, 1, 1, 1, 2},
            new[] {0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2},
            new[] {0, 2, 2, 2, 0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2},
            new[] {0, 0, 0, 2, 1, 1, 1, 2, 1, 1, 1, 2, 0, 0, 0, 2},
            new[] {0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2},
            new[] {0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2, 2, 2, 2, 2},
            new[] {0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2},
            new[] {0, 0, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2},
            new[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2},
            new[] {0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1},
            new[] {0, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2, 1, 2, 2, 2},
            new[] {0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2},
            new[] {0, 1, 1, 1, 2, 0, 1, 1, 2, 2, 0, 1, 2, 2, 2, 0},
        };

        public static readonly int[] Subsets2AnchorIndices = {
            15, 15, 15, 15, 15, 15, 15, 15,
            15, 15, 15, 15, 15, 15, 15, 15,
            15, 2, 8, 2, 2, 8, 8, 15,
            2, 8, 2, 2, 8, 8, 2, 2,
            15, 15, 6, 8, 2, 8, 15, 15,
            2, 8, 2, 2, 2, 15, 15, 6,
            6, 2, 6, 8, 15, 15, 2, 2,
            15, 15, 15, 15, 15, 2, 2, 15
        };

        public static readonly int[] Subsets3AnchorIndices2 = {
            3, 3, 15, 15, 8, 3, 15, 15,
            8, 8, 6, 6, 6, 5, 3, 3,
            3, 3, 8, 15, 3, 3, 6, 10,
            5, 8, 8, 6, 8, 5, 15, 15,
            8, 15, 3, 5, 6, 10, 8, 15,
            15, 3, 15, 5, 15, 15, 15, 15,
            3, 15, 5, 5, 5, 8, 5, 10,
            5, 10, 8, 13, 15, 12, 3, 3
        };

        public static readonly int[] Subsets3AnchorIndices3 = {
            15, 8, 8, 3, 15, 15, 3, 8,
            15, 15, 15, 15, 15, 15, 15, 8,
            15, 8, 15, 3, 15, 8, 15, 8,
            3, 15, 6, 10, 15, 15, 10, 8,
            15, 3, 15, 10, 10, 8, 9, 10,
            6, 15, 8, 15, 3, 6, 6, 8,
            15, 3, 15, 15, 15, 15, 15, 15,
            15, 15, 15, 15, 3, 15, 15, 8
        };

        public static readonly RawBlock4X4Rgba32 ErrorBlock = new RawBlock4X4Rgba32(new ColorRgba32(0, 0, 0));

        public Bc7Mode Type {
            get {
                for (var i = 0; i < 8; i++) {
                    var mask = (ulong)(1 << i);
                    if ((LowBits & mask) == mask) {
                        return (Bc7Mode)i;
                    }
                }

                return Bc7Mode.Type8Reserved;
            }
        }

        public int NumSubsets => Type switch {
            Bc7Mode.Type0 => 3,
            Bc7Mode.Type1 => 2,
            Bc7Mode.Type2 => 3,
            Bc7Mode.Type3 => 2,
            Bc7Mode.Type7 => 2,
            _ => 1
        };

        public bool HasSubsets => Type switch {
            Bc7Mode.Type0 => true,
            Bc7Mode.Type1 => true,
            Bc7Mode.Type2 => true,
            Bc7Mode.Type3 => true,
            Bc7Mode.Type7 => true,
            _ => false
        };

        public int PartitionSetId => Type switch {
            Bc7Mode.Type0 => ByteHelper.Extract4(LowBits, 1),
            Bc7Mode.Type1 => ByteHelper.Extract6(LowBits, 2),
            Bc7Mode.Type2 => ByteHelper.Extract6(LowBits, 3),
            Bc7Mode.Type3 => ByteHelper.Extract6(LowBits, 4),
            Bc7Mode.Type7 => ByteHelper.Extract6(LowBits, 8),
            _ => -1
        };

        public byte RotationBits => Type switch {
            Bc7Mode.Type4 => ByteHelper.Extract2(LowBits, 5),
            Bc7Mode.Type5 => ByteHelper.Extract2(LowBits, 6),
            _ => 0
        };
        public int ColorComponentPrecision => Type switch {
            Bc7Mode.Type0 => 5,
            Bc7Mode.Type1 => 7,
            Bc7Mode.Type2 => 5,
            Bc7Mode.Type3 => 8,
            Bc7Mode.Type4 => 5,
            Bc7Mode.Type5 => 7,
            Bc7Mode.Type6 => 8,
            Bc7Mode.Type7 => 6,
            _ => 0
        };
        public int AlphaComponentPrecision => Type switch {

            Bc7Mode.Type4 => 6,
            Bc7Mode.Type5 => 8,
            Bc7Mode.Type6 => 8,
            Bc7Mode.Type7 => 6,
            _ => 0
        };

        public bool HasRotationBits => Type switch {
            Bc7Mode.Type4 => true,
            Bc7Mode.Type5 => true,
            _ => false
        };

        public bool HasPBits => Type switch {
            Bc7Mode.Type0 => true,
            Bc7Mode.Type1 => true,
            Bc7Mode.Type3 => true,
            Bc7Mode.Type6 => true,
            Bc7Mode.Type7 => true,
            _ => false
        };

        public bool HasAlpha => Type switch {
            Bc7Mode.Type4 => true,
            Bc7Mode.Type5 => true,
            Bc7Mode.Type6 => true,
            Bc7Mode.Type7 => true,
            _ => false
        };
        public int Type4IndexMode => Type switch {
            Bc7Mode.Type4 => ByteHelper.Extract1(LowBits, 7),
            _ => 0
        };

        public int ColorIndexBitCount => Type switch {
            Bc7Mode.Type0 => 3,
            Bc7Mode.Type1 => 3,
            Bc7Mode.Type2 => 2,
            Bc7Mode.Type3 => 2,
            Bc7Mode.Type4 when Type4IndexMode == 0 => 2,
            Bc7Mode.Type4 when Type4IndexMode == 1 => 3,
            Bc7Mode.Type5 => 2,
            Bc7Mode.Type6 => 4,
            Bc7Mode.Type7 => 2,
            _ => 0
        };

        public int AlphaIndexBitCount => Type switch {
            Bc7Mode.Type4 when Type4IndexMode == 0 => 3,
            Bc7Mode.Type4 when Type4IndexMode == 1 => 2,
            Bc7Mode.Type5 => 2,
            Bc7Mode.Type6 => 4,
            Bc7Mode.Type7 => 2,
            _ => 0
        };



        private void ExtractRawEndpoints(Span<ColorRgba32> endpoints)
        {
            switch (Type) {
                case Bc7Mode.Type0:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 5, 4);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 9, 4);
                    endpoints[2].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 13, 4);
                    endpoints[3].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 17, 4);
                    endpoints[4].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 21, 4);
                    endpoints[5].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 25, 4);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 29, 4);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 4);
                    endpoints[2].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 37, 4);
                    endpoints[3].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 41, 4);
                    endpoints[4].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 45, 4);
                    endpoints[5].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 4);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 53, 4);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 57, 4);
                    endpoints[2].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 61, 4);
                    endpoints[3].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 65, 4);
                    endpoints[4].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 69, 4);
                    endpoints[5].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 73, 4);
                    break;
                case Bc7Mode.Type1:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 8, 6);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 6);
                    endpoints[2].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 20, 6);
                    endpoints[3].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 26, 6);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 32, 6);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 38, 6);
                    endpoints[2].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 6);
                    endpoints[3].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 6);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 56, 6);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 62, 6);
                    endpoints[2].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 68, 6);
                    endpoints[3].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 74, 6);
                    break;
                case Bc7Mode.Type2:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 9, 5);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 5);
                    endpoints[2].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 19, 5);
                    endpoints[3].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 5);
                    endpoints[4].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 29, 5);
                    endpoints[5].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 5);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 39, 5);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 5);
                    endpoints[2].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 5);
                    endpoints[3].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 54, 5);
                    endpoints[4].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 59, 5);
                    endpoints[5].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 64, 5);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 69, 5);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 74, 5);
                    endpoints[2].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 79, 5);
                    endpoints[3].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 84, 5);
                    endpoints[4].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 89, 5);
                    endpoints[5].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 94, 5);
                    break;
                case Bc7Mode.Type3:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 10, 7);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 17, 7);
                    endpoints[2].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 7);
                    endpoints[3].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 31, 7);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 38, 7);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 45, 7);
                    endpoints[2].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 52, 7);
                    endpoints[3].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 59, 7);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 66, 7);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 73, 7);
                    endpoints[2].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 80, 7);
                    endpoints[3].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 87, 7);
                    break;
                case Bc7Mode.Type4:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 8, 5);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 13, 5);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 18, 5);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 23, 5);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 28, 5);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 5);

                    endpoints[0].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 38, 6);
                    endpoints[1].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 6);
                    break;
                case Bc7Mode.Type5:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 8, 7);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 15, 7);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 22, 7);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 29, 7);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 36, 7);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 43, 7);

                    endpoints[0].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 8);
                    endpoints[1].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 58, 8);
                    break;
                case Bc7Mode.Type6:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 7, 7);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 7);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 21, 7);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 28, 7);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 35, 7);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 42, 7);

                    endpoints[0].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 7);
                    endpoints[1].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 56, 7);
                    break;
                case Bc7Mode.Type7:
                    endpoints[0].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 5);
                    endpoints[1].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 19, 5);
                    endpoints[2].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 5);
                    endpoints[3].R = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 29, 5);

                    endpoints[0].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 5);
                    endpoints[1].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 39, 5);
                    endpoints[2].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 5);
                    endpoints[3].G = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 5);

                    endpoints[0].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 54, 5);
                    endpoints[1].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 59, 5);
                    endpoints[2].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 64, 5);
                    endpoints[3].B = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 69, 5);

                    endpoints[0].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 74, 5);
                    endpoints[1].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 79, 5);
                    endpoints[2].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 84, 5);
                    endpoints[3].A = (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, 89, 5);
                    break;
                default:
                    throw new InvalidDataException();
            }
        }

        private byte ExtractPBit(int endpointIndex)
        {
            var bitIndex = Type switch {
                Bc7Mode.Type0 => 77 + endpointIndex,
                Bc7Mode.Type1 => 80 + (endpointIndex >> 1),
                Bc7Mode.Type3 or Bc7Mode.Type7 => 94 + endpointIndex,
                Bc7Mode.Type6 => 63 + endpointIndex,
                _ => throw new InvalidOperationException(),
            };
            return (byte)ByteHelper.ExtractFrom128(LowBits, HighBits, bitIndex, 1);
        }

        private void FinalizeEndpoints(Span<ColorRgba32> endpoints)
        {
            if (HasPBits) {
                for (var i = 0; i < endpoints.Length; i++) {
                    endpoints[i] <<= 1;
                    endpoints[i] |= ExtractPBit(i);
                }
            }

            var colorPrecision = ColorComponentPrecision;
            var alphaPrecision = AlphaComponentPrecision;
            for (var i = 0; i < endpoints.Length; i++) {
                endpoints[i].R = (byte)(endpoints[i].R << (8 - colorPrecision));
                endpoints[i].G = (byte)(endpoints[i].G << (8 - colorPrecision));
                endpoints[i].B = (byte)(endpoints[i].B << (8 - colorPrecision));
                endpoints[i].A = (byte)(endpoints[i].A << (8 - alphaPrecision));
                endpoints[i].R = (byte)(endpoints[i].R | (endpoints[i].R >> colorPrecision));
                endpoints[i].G = (byte)(endpoints[i].G | (endpoints[i].G >> colorPrecision));
                endpoints[i].B = (byte)(endpoints[i].B | (endpoints[i].B >> colorPrecision));
                endpoints[i].A = (byte)(endpoints[i].A | (endpoints[i].A >> alphaPrecision));
            }
            if (!HasAlpha) {
                for (var i = 0; i < endpoints.Length; i++) {
                    endpoints[i].A = 255;
                }
            }
        }

        private void ExtractEndpoints(Span<ColorRgba32> endpoints)
        {
            ExtractRawEndpoints(endpoints);
            FinalizeEndpoints(endpoints);
        }

        private int GetPartitionIndex(int numSubsets, int partitionSetId, int i)
        {
            switch (numSubsets) {
                case 1:
                    return 0;
                case 2:
                    return Subsets2PartitionTable[partitionSetId][i];
                case 3:
                    return Subsets3PartitionTable[partitionSetId][i];
                default:
                    throw new ArgumentOutOfRangeException(nameof(numSubsets), numSubsets, "Number of subsets can only be 1, 2 or 3");
            }
        }

        private int GetIndexOffset(Bc7Mode type, int numSubsets, int partitionIndex, int bitCount, int index)
        {
            if (index == 0) return 0;
            if (numSubsets == 1) {
                return bitCount * index - 1;
            }
            if (numSubsets == 2) {
                var anchorIndex = Subsets2AnchorIndices[partitionIndex];
                if (index <= anchorIndex) {
                    return bitCount * index - 1;
                }
                else {
                    return bitCount * index - 2;
                }
            }
            if (numSubsets == 3) {
                var anchor2Index = Subsets3AnchorIndices2[partitionIndex];
                var anchor3Index = Subsets3AnchorIndices3[partitionIndex];

                if (index <= anchor2Index && index <= anchor3Index) {
                    return bitCount * index - 1;
                }
                else if (index > anchor2Index && index > anchor3Index) {
                    return bitCount * index - 3;
                }
                else {
                    return bitCount * index - 2;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(numSubsets), numSubsets, "Number of subsets can only be 1, 2 or 3");
        }
        private int GetIndexBitCount(int numSubsets, int partitionIndex, int bitCount, int index)
        {
            if (index == 0) return bitCount - 1;
            if (numSubsets == 2) {
                var anchorIndex = Subsets2AnchorIndices[partitionIndex];
                if (index == anchorIndex) {
                    return bitCount - 1;
                }
            }
            else if (numSubsets == 3) {
                var anchor2Index = Subsets3AnchorIndices2[partitionIndex];
                var anchor3Index = Subsets3AnchorIndices3[partitionIndex];
                if (index == anchor2Index) {
                    return bitCount - 1;
                }
                if (index == anchor3Index) {
                    return bitCount - 1;
                }
            }
            return bitCount;
        }

        private int GetIndexBegin(Bc7Mode type, int bitCount, bool isAlpha)
        {
            switch (type) {
                case Bc7Mode.Type0:
                    return 83;
                case Bc7Mode.Type1:
                    return 82;
                case Bc7Mode.Type2:
                    return 99;
                case Bc7Mode.Type3:
                    return 98;
                case Bc7Mode.Type4:
                    if (bitCount == 2) {
                        return 50;
                    }
                    else {
                        return 81;
                    }
                case Bc7Mode.Type5:
                    if (isAlpha) {
                        return 97;
                    }
                    else {
                        return 66;
                    }
                case Bc7Mode.Type6:
                    return 65;
                case Bc7Mode.Type7:
                    return 98;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private int GetAlphaIndex(Bc7Mode type, int numSubsets, int partitionIndex, int bitCount, int index)
        {
            if (bitCount == 0) return 0;
            var indexOffset = GetIndexOffset(type, numSubsets, partitionIndex, bitCount, index);
            var indexBitCount = GetIndexBitCount(numSubsets, partitionIndex, bitCount, index);
            var indexBegin = GetIndexBegin(type, bitCount, true);
            return (int)ByteHelper.ExtractFrom128(LowBits, HighBits, indexBegin + indexOffset, indexBitCount);
        }

        private int GetColorIndex(Bc7Mode type, int numSubsets, int partitionIndex, int bitCount, int index)
        {
            var indexOffset = GetIndexOffset(type, numSubsets, partitionIndex, bitCount, index);
            var indexBitCount = GetIndexBitCount(numSubsets, partitionIndex, bitCount, index);
            var indexBegin = GetIndexBegin(type, bitCount, false);
            return (int)ByteHelper.ExtractFrom128(LowBits, HighBits, indexBegin + indexOffset, indexBitCount);
        }

        private ColorRgba32 InterpolateColor(ColorRgba32 endPointStart, ColorRgba32 endPointEnd,
            int colorIndex, int alphaIndex, int colorBitCount, int alphaBitCount)
        {
            var result = new ColorRgba32(
                Bc7Codec.Interpolate(endPointStart.R, endPointEnd.R, colorIndex, colorBitCount),
                Bc7Codec.Interpolate(endPointStart.G, endPointEnd.G, colorIndex, colorBitCount),
                Bc7Codec.Interpolate(endPointStart.B, endPointEnd.B, colorIndex, colorBitCount),
                Bc7Codec.Interpolate(endPointStart.A, endPointEnd.A, alphaIndex, alphaBitCount)
                );

            return result;
        }
        private static ColorRgba32 SwapChannels(ColorRgba32 source, int rotation)
        {
            switch (rotation) {
                case 0b00:
                    return source;
                case 0b01:
                    return new ColorRgba32(source.A, source.G, source.B, source.R);
                case 0b10:
                    return new ColorRgba32(source.R, source.A, source.B, source.G);
                case 0b11:
                    return new ColorRgba32(source.R, source.G, source.A, source.B);
                default:
                    return source;
            }
        }


        public RawBlock4X4Rgba32 Decode()
        {
            var output = new RawBlock4X4Rgba32();
            var type = Type;

            if (type == Bc7Mode.Type8Reserved) {
                return ErrorBlock;
            }
            var numSubsets = 1;
            var partitionIndex = 0;

            if (HasSubsets) {
                numSubsets = NumSubsets;
                partitionIndex = PartitionSetId;
            }

            var pixels = output.AsSpan;

            var hasRotationBits = HasRotationBits;
            int rotation = RotationBits;

            Span<ColorRgba32> endpointBuffer = stackalloc ColorRgba32[6];
            var endpoints = endpointBuffer[..(numSubsets * 2)];
            ExtractEndpoints(endpoints);
            for (var i = 0; i < pixels.Length; i++) {
                var subsetIndex = GetPartitionIndex(numSubsets, partitionIndex, i);

                var endPointStart = endpoints[2 * subsetIndex];
                var endPointEnd = endpoints[2 * subsetIndex + 1];

                var alphaBitCount = AlphaIndexBitCount;
                var colorBitCount = ColorIndexBitCount;
                var alphaIndex = GetAlphaIndex(type, numSubsets, partitionIndex, alphaBitCount, i);
                var colorIndex = GetColorIndex(type, numSubsets, partitionIndex, colorBitCount, i);

                var outputColor = InterpolateColor(endPointStart, endPointEnd, colorIndex, alphaIndex,
                    colorBitCount, alphaBitCount);

                if (hasRotationBits) {
                    outputColor = SwapChannels(outputColor, rotation);
                }

                pixels[i] = outputColor;
            }

            return output;
        }
    }
}

namespace Lucitex.TextureCompression
{
    internal static class Bc7Codec
    {
        private static ReadOnlySpan<byte> Weights2 => [0, 21, 43, 64];
        private static ReadOnlySpan<byte> Weights3 => [0, 9, 18, 27, 37, 46, 55, 64];
        private static ReadOnlySpan<byte> Weights4 => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

        public static void Decode(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            if (source.Length < 16 || destination.Length < 64) {
                throw new ArgumentException("BC7 buffers must contain one complete block.");
            }

            var block = new Bc7DecoderBlock {
                LowBits = BinaryPrimitives.ReadUInt64LittleEndian(source),
                HighBits = BinaryPrimitives.ReadUInt64LittleEndian(source[8..]),
            };
            var decoded = block.Decode();
            MemoryMarshal.Cast<ColorRgba32, byte>(decoded.AsSpan).CopyTo(destination);
        }

        internal static byte Interpolate(byte first, byte second, int index, int precision)
        {
            if (precision == 0) {
                return first;
            }

            var weights = precision switch {
                2 => Weights2,
                3 => Weights3,
                4 => Weights4,
                _ => throw new ArgumentOutOfRangeException(nameof(precision)),
            };
            var weight = weights[index];
            return (byte)((((64 - weight) * first) + (weight * second) + 32) >> 6);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ColorRgba32
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public ColorRgba32(byte red, byte green, byte blue, byte alpha = 255)
        {
            R = red;
            G = green;
            B = blue;
            A = alpha;
        }

        public static ColorRgba32 operator <<(ColorRgba32 value, int count) =>
            new((byte)(value.R << count), (byte)(value.G << count), (byte)(value.B << count), (byte)(value.A << count));

        public static ColorRgba32 operator |(ColorRgba32 value, int bits) =>
            new((byte)(value.R | bits), (byte)(value.G | bits), (byte)(value.B | bits), (byte)(value.A | bits));
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RawBlock4X4Rgba32
    {
        private ColorRgba32 _p00, _p10, _p20, _p30;
        private ColorRgba32 _p01, _p11, _p21, _p31;
        private ColorRgba32 _p02, _p12, _p22, _p32;
        private ColorRgba32 _p03, _p13, _p23, _p33;

        public RawBlock4X4Rgba32(ColorRgba32 color)
        {
            _p00 = _p10 = _p20 = _p30 = color;
            _p01 = _p11 = _p21 = _p31 = color;
            _p02 = _p12 = _p22 = _p32 = color;
            _p03 = _p13 = _p23 = _p33 = color;
        }

        public Span<ColorRgba32> AsSpan => MemoryMarshal.CreateSpan(ref _p00, 16);
    }

    internal static class ByteHelper
    {
        public static byte Extract1(ulong source, int index) => (byte)((source >> index) & 1);
        public static byte Extract2(ulong source, int index) => (byte)((source >> index) & 3);
        public static byte Extract4(ulong source, int index) => (byte)((source >> index) & 15);
        public static byte Extract5(ulong source, int index) => (byte)((source >> index) & 31);
        public static byte Extract6(ulong source, int index) => (byte)((source >> index) & 63);

        public static ulong ExtractFrom128(ulong low, ulong high, int index, int bitCount)
        {
            if (index + bitCount <= 64) {
                return Extract(low, index, bitCount);
            }

            if (index >= 64) {
                return Extract(high, index - 64, bitCount);
            }

            var lowBitCount = 64 - index;
            var highBitCount = bitCount - lowBitCount;
            return Extract(low, index, lowBitCount) | (Extract(high, 0, highBitCount) << lowBitCount);
        }

        private static ulong Extract(ulong source, int index, int bitCount)
        {
            var mask = (1UL << bitCount) - 1;
            return (source >> index) & mask;
        }
    }
}
