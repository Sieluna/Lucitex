using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;


namespace Lucitex.TextureCompression
{

    internal enum Bc6HMode : uint
    {
        Type0 = 0,
        Type1 = 1,
        Type2 = 2,
        Type6 = 6,
        Type10 = 10,
        Type14 = 14,
        Type18 = 18,
        Type22 = 22,
        Type26 = 26,
        Type30 = 30,
        Type3 = 3,
        Type7 = 7,
        Type11 = 11,
        Type15 = 15,
        Unknown
    }

    internal static class Bc6HModeExtensions
    {
        public static bool HasSubsets(this Bc6HMode type) => type switch {
            Bc6HMode.Type3 => false,
            Bc6HMode.Type7 => false,
            Bc6HMode.Type11 => false,
            Bc6HMode.Type15 => false,
            _ => true
        };

        public static bool HasTransformedEndpoints(this Bc6HMode type) => type switch {
            Bc6HMode.Type3 => false,
            Bc6HMode.Type30 => false,
            _ => true
        };

        public static int EndpointBits(this Bc6HMode type) => type switch {
            Bc6HMode.Type0 => 10,
            Bc6HMode.Type1 => 7,
            Bc6HMode.Type2 => 11,
            Bc6HMode.Type6 => 11,
            Bc6HMode.Type10 => 11,
            Bc6HMode.Type14 => 9,
            Bc6HMode.Type18 => 8,
            Bc6HMode.Type22 => 8,
            Bc6HMode.Type26 => 8,
            Bc6HMode.Type30 => 6,
            Bc6HMode.Type3 => 10,
            Bc6HMode.Type7 => 11,
            Bc6HMode.Type11 => 12,
            Bc6HMode.Type15 => 16,
            _ => 0
        };

        public static (int, int, int) DeltaBits(this Bc6HMode type) => type switch {
            Bc6HMode.Type0 => (5, 5, 5),
            Bc6HMode.Type1 => (6, 6, 6),
            Bc6HMode.Type2 => (5, 4, 4),
            Bc6HMode.Type6 => (4, 5, 4),
            Bc6HMode.Type10 => (4, 4, 5),
            Bc6HMode.Type14 => (5, 5, 5),
            Bc6HMode.Type18 => (6, 5, 5),
            Bc6HMode.Type22 => (5, 6, 5),
            Bc6HMode.Type26 => (5, 5, 6),
            Bc6HMode.Type30 => (0, 0, 0),
            Bc6HMode.Type3 => (0, 0, 0),
            Bc6HMode.Type7 => (9, 9, 9),
            Bc6HMode.Type11 => (8, 8, 8),
            Bc6HMode.Type15 => (4, 4, 4),
            _ => (0, 0, 0)
        };
    }

    internal struct Bc6HDecoderBlock
    {
        public ulong LowBits;
        public ulong HighBits;

        public static readonly int[][] Subsets2PartitionTable = new int[32][]{
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
            new[] {0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0}
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

        public static readonly RawBlock4X4RgbFloat ErrorBlock = new RawBlock4X4RgbFloat(new ColorRgbFloat(0, 0, 0));

        public static readonly Bc6HMode[] Subsets1Types =
        {
            Bc6HMode.Type3,
            Bc6HMode.Type7,
            Bc6HMode.Type11,
            Bc6HMode.Type15
        };

        public static readonly Bc6HMode[] Subsets2Types =
        {
            Bc6HMode.Type0 ,
            Bc6HMode.Type1 ,
            Bc6HMode.Type2 ,
            Bc6HMode.Type6 ,
            Bc6HMode.Type10,
            Bc6HMode.Type14,
            Bc6HMode.Type18,
            Bc6HMode.Type22,
            Bc6HMode.Type26,
            Bc6HMode.Type30
        };

        public readonly Bc6HMode Type {
            get {
                const ulong smallMask = 0b11;
                const ulong bigMask = 0b11111;
                if ((LowBits & smallMask) < 2) {
                    return (Bc6HMode)(LowBits & smallMask);
                }
                else {
                    var typeNum = (LowBits & bigMask);
                    switch (typeNum) {
                        case 2: return Bc6HMode.Type2;
                        case 3: return Bc6HMode.Type3;
                        case 6: return Bc6HMode.Type6;
                        case 7: return Bc6HMode.Type7;
                        case 10: return Bc6HMode.Type10;
                        case 11: return Bc6HMode.Type11;
                        case 14: return Bc6HMode.Type14;
                        case 15: return Bc6HMode.Type15;
                        case 18: return Bc6HMode.Type18;
                        case 22: return Bc6HMode.Type22;
                        case 26: return Bc6HMode.Type26;
                        case 30: return Bc6HMode.Type30;
                        default: return Bc6HMode.Unknown;
                    }
                }
            }
        }

        public readonly bool HasSubsets => Type.HasSubsets();

        public readonly int NumEndpoints => HasSubsets ? 4 : 2;

        public readonly bool HasTransformedEndpoints => Type.HasTransformedEndpoints();

        public readonly int PartitionSetId => HasSubsets ? ByteHelper.Extract5(HighBits, 13) : -1;

        public readonly int EndpointBits => Type.EndpointBits();

        public readonly (int, int, int) DeltaBits => Type.DeltaBits();

        public readonly int ColorIndexBitCount => HasSubsets ? 3 : 4;

        internal readonly (int, int, int) ExtractEp0()
        {
            ulong r0 = 0;
            ulong g0 = 0;
            ulong b0 = 0;

            r0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 5, Math.Min(10, EndpointBits));
            g0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 15, Math.Min(10, EndpointBits));
            b0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 25, Math.Min(10, EndpointBits));

            switch (Type) {
                case Bc6HMode.Type2:

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 10;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 1) << 10;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 59, 1) << 10;
                    break;
                case Bc6HMode.Type6:

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 39, 1) << 10;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1) << 10;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 59, 1) << 10;
                    break;
                case Bc6HMode.Type10:

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 39, 1) << 10;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 1) << 10;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 10;
                    break;
                case Bc6HMode.Type7:
                    r0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 5, 10);
                    g0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 15, 10);
                    b0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 25, 10);

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 1) << 10;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 54, 1) << 10;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 64, 1) << 10;
                    break;
                case Bc6HMode.Type11:
                    r0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 5, 10);
                    g0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 15, 10);
                    b0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 25, 10);

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 1) << 10;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 54, 1) << 10;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 64, 1) << 10;

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 43, 1) << 11;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 53, 1) << 11;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 63, 1) << 11;
                    break;
                case Bc6HMode.Type15:
                    r0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 5, 10);
                    g0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 15, 10);
                    b0 = ByteHelper.ExtractFrom128(LowBits, HighBits, 25, 10);

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 44, 1) << 10;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 54, 1) << 10;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 64, 1) << 10;

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 43, 1) << 11;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 53, 1) << 11;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 63, 1) << 11;

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 42, 1) << 12;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 52, 1) << 12;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 62, 1) << 12;

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 41, 1) << 13;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 51, 1) << 13;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 61, 1) << 13;

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 14;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1) << 14;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 14;

                    r0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 39, 1) << 15;
                    g0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 49, 1) << 15;
                    b0 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 59, 1) << 15;
                    break;
            }

            return ((int)r0, (int)g0, (int)b0);
        }

        internal readonly (int, int, int) ExtractEp1()
        {
            ulong r1 = 0;
            ulong g1 = 0;
            ulong b1 = 0;

            if (HasTransformedEndpoints) {
                r1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 35, Math.Min(5, DeltaBits.Item1));
                g1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 45, Math.Min(5, DeltaBits.Item2));
                b1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 55, Math.Min(5, DeltaBits.Item3));
            }

            switch (Type) {
                case Bc6HMode.Type1:
                    r1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 5;
                    g1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1) << 5;
                    b1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 5;

                    break;
                case Bc6HMode.Type18:
                    r1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 5;

                    break;
                case Bc6HMode.Type22:
                    g1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1) << 5;

                    break;
                case Bc6HMode.Type26:
                    b1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 5;

                    break;
                case Bc6HMode.Type30:
                    r1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 35, 6);
                    g1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 45, 6);
                    b1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 55, 6);

                    break;
                case Bc6HMode.Type3:
                    r1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 35, 10);
                    g1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 45, 10);
                    b1 = ByteHelper.ExtractFrom128(LowBits, HighBits, 55, 10);

                    break;
                case Bc6HMode.Type7:
                    r1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 4) << 5;
                    g1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 4) << 5;
                    b1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 4) << 5;

                    break;
                case Bc6HMode.Type11:
                    r1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 3) << 5;
                    g1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 3) << 5;
                    b1 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 3) << 5;

                    break;
            }

            return ((int)r1, (int)g1, (int)b1);
        }

        internal readonly (int, int, int) ExtractEp2()
        {
            ulong r2 = 0;
            ulong g2 = 0;
            ulong b2 = 0;

            r2 = ByteHelper.ExtractFrom128(LowBits, HighBits, 65, Math.Min(5, DeltaBits.Item1));
            g2 = ByteHelper.ExtractFrom128(LowBits, HighBits, 41, 4);
            b2 = ByteHelper.ExtractFrom128(LowBits, HighBits, 61, 4);

            switch (Type) {
                case Bc6HMode.Type0:

                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 2, 1) << 4;
                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 3, 1) << 4;
                    break;
                case Bc6HMode.Type1:
                    r2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 5;

                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 1) << 4;
                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 2, 1) << 5;

                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 1) << 4;
                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 22, 1) << 5;

                    break;
                case Bc6HMode.Type6:

                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 75, 1) << 4;

                    break;
                case Bc6HMode.Type10:

                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 4;

                    break;
                case Bc6HMode.Type14:
                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 1) << 4;
                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 1) << 4;

                    break;
                case Bc6HMode.Type18:
                    r2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 5;

                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 1) << 4;
                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 1) << 4;

                    break;
                case Bc6HMode.Type22:

                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 1) << 4;
                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 23, 1) << 5;

                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 1) << 4;
                    break;
                case Bc6HMode.Type26:
                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 1) << 4;

                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 1) << 4;
                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 23, 1) << 5;
                    break;
                case Bc6HMode.Type30:

                    r2 = ByteHelper.ExtractFrom128(LowBits, HighBits, 65, 6);

                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 24, 1) << 4;
                    g2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 21, 1) << 5;

                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 14, 1) << 4;
                    b2 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 22, 1) << 5;

                    break;
            }

            return ((int)r2, (int)g2, (int)b2);
        }

        internal readonly (int, int, int) ExtractEp3()
        {
            ulong r3 = 0;
            ulong g3 = 0;
            ulong b3 = 0;

            r3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 71, Math.Min(5, DeltaBits.Item1));
            g3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 51, 4);

            switch (Type) {
                case Bc6HMode.Type0:
                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 4;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 4, 1) << 4;
                    break;
                case Bc6HMode.Type1:
                    r3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 5;

                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 3, 2) << 4;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 12, 2);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 23, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 32, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 1) << 4;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 1) << 5;

                    break;
                case Bc6HMode.Type2:

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;

                    break;
                case Bc6HMode.Type6:

                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 4;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 69, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;

                    break;
                case Bc6HMode.Type10:

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 69, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 75, 1) << 4;

                    break;
                case Bc6HMode.Type14:
                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 4;


                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 1) << 4;

                    break;
                case Bc6HMode.Type18:
                    r3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 5;

                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 13, 1) << 4;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 23, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 1) << 4;

                    break;
                case Bc6HMode.Type22:

                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 4;
                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 1) << 5;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 13, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 60, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 1) << 4;

                    break;
                case Bc6HMode.Type26:
                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 40, 1) << 4;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 50, 1);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 13, 1) << 1;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 70, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 76, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 1) << 4;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 1) << 5;

                    break;
                case Bc6HMode.Type30:

                    r3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 71, 6);


                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 11, 1) << 4;
                    g3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 31, 1) << 5;

                    b3 = ByteHelper.ExtractFrom128(LowBits, HighBits, 12, 2);
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 23, 1) << 2;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 32, 1) << 3;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 34, 1) << 4;
                    b3 |= ByteHelper.ExtractFrom128(LowBits, HighBits, 33, 1) << 5;

                    break;
            }

            return ((int)r3, (int)g3, (int)b3);
        }

        private readonly void ExtractRawEndpoints(bool signedBc6, Span<(int, int, int)> outEndpoints)
        {
            var endpointBits = EndpointBits;

            var (r0, g0, b0) = ExtractEp0();
            if (signedBc6) {
                r0 = Bc6HCodec.SignExtend(r0, endpointBits);
                g0 = Bc6HCodec.SignExtend(g0, endpointBits);
                b0 = Bc6HCodec.SignExtend(b0, endpointBits);
            }

            outEndpoints[0] = (r0, g0, b0);

            var (r1, g1, b1) = ExtractEp1();

            if (HasTransformedEndpoints) {
                r1 = Bc6HCodec.SignExtend(r1, DeltaBits.Item1);
                g1 = Bc6HCodec.SignExtend(g1, DeltaBits.Item2);
                b1 = Bc6HCodec.SignExtend(b1, DeltaBits.Item3);

                r1 = (r1 + r0) & ((1 << endpointBits) - 1);
                g1 = (g1 + g0) & ((1 << endpointBits) - 1);
                b1 = (b1 + b0) & ((1 << endpointBits) - 1);
            }
            if (signedBc6) {
                r1 = Bc6HCodec.SignExtend(r1, endpointBits);
                g1 = Bc6HCodec.SignExtend(g1, endpointBits);
                b1 = Bc6HCodec.SignExtend(b1, endpointBits);
            }

            outEndpoints[1] = (r1, g1, b1);

            if (HasSubsets) {
                var (r2, g2, b2) = ExtractEp2();
                var (r3, g3, b3) = ExtractEp3();

                if (HasTransformedEndpoints) {
                    r2 = Bc6HCodec.SignExtend(r2, DeltaBits.Item1);
                    g2 = Bc6HCodec.SignExtend(g2, DeltaBits.Item2);
                    b2 = Bc6HCodec.SignExtend(b2, DeltaBits.Item3);

                    r2 = (r2 + r0) & ((1 << endpointBits) - 1);
                    g2 = (g2 + g0) & ((1 << endpointBits) - 1);
                    b2 = (b2 + b0) & ((1 << endpointBits) - 1);

                    r3 = Bc6HCodec.SignExtend(r3, DeltaBits.Item1);
                    g3 = Bc6HCodec.SignExtend(g3, DeltaBits.Item2);
                    b3 = Bc6HCodec.SignExtend(b3, DeltaBits.Item3);

                    r3 = (r3 + r0) & ((1 << endpointBits) - 1);
                    g3 = (g3 + g0) & ((1 << endpointBits) - 1);
                    b3 = (b3 + b0) & ((1 << endpointBits) - 1);
                }

                if (signedBc6) {
                    r2 = Bc6HCodec.SignExtend(r2, endpointBits);
                    g2 = Bc6HCodec.SignExtend(g2, endpointBits);
                    b2 = Bc6HCodec.SignExtend(b2, endpointBits);

                    r3 = Bc6HCodec.SignExtend(r3, endpointBits);
                    g3 = Bc6HCodec.SignExtend(g3, endpointBits);
                    b3 = Bc6HCodec.SignExtend(b3, endpointBits);
                }

                outEndpoints[2] = (r2, g2, b2);
                outEndpoints[3] = (r3, g3, b3);
            }

        }

        internal static int UnQuantize(int component, int endpointBits, bool signedBc6)
        {
            int unq;
            var sign = false;

            if (!signedBc6) {
                if (endpointBits >= 15)
                    unq = component;
                else if (component == 0)
                    unq = 0;
                else if (component == ((1 << endpointBits) - 1))
                    unq = 0xFFFF;
                else
                    unq = ((component << 15) + 0x4000) >> (endpointBits - 1);

            }
            else {
                if (endpointBits >= 16)
                    unq = component;
                else {
                    if (component < 0) {
                        sign = true;
                        component = -component;
                    }

                    if (component == 0)
                        unq = 0;
                    else if (component >= ((1 << (endpointBits - 1)) - 1))
                        unq = 0x7FFF;
                    else
                        unq = ((component << 15) + 0x4000) >> (endpointBits - 1);

                    if (sign)
                        unq = -unq;
                }
            }
            return unq;
        }

        internal static (int, int, int) UnQuantize((int, int, int) components, int endpointBits, bool signedBc6)
        {
            return (
                UnQuantize(components.Item1, endpointBits, signedBc6),
                UnQuantize(components.Item2, endpointBits, signedBc6),
                UnQuantize(components.Item3, endpointBits, signedBc6)
            );
        }

        internal static float FinishUnQuantize(int component, bool signedBc6)
        {
            if (!signedBc6) {
                component = (component * 31) >> 6;
                return (float)BitConverter.UInt16BitsToHalf((ushort)component);
            }
            else {
                component = (component < 0) ? -(((-component) * 31) >> 5) : (component * 31) >> 5;
                var s = 0;
                if (component < 0) {
                    s = 0x8000;
                    component = -component;
                }
                return (float)BitConverter.UInt16BitsToHalf((ushort)(s | component));
            }
        }

        internal static (float, float, float) FinishUnQuantize((int, int, int) components, bool signedBc6)
        {
            return (
                FinishUnQuantize(components.Item1, signedBc6),
                FinishUnQuantize(components.Item2, signedBc6),
                FinishUnQuantize(components.Item3, signedBc6)
            );
        }

        private static int GetPartitionIndex(int numSubsets, int partitionSetId, int i)
        {
            switch (numSubsets) {
                case 1:
                    return 0;
                case 2:
                    return Subsets2PartitionTable[partitionSetId][i];
                default:
                    throw new ArgumentOutOfRangeException(nameof(numSubsets), numSubsets, "Number of subsets can only be 1, 2 or 3");
            }
        }

        private static int GetIndexOffset(int numSubsets, int partitionIndex, int bitCount, int index)
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
            throw new ArgumentOutOfRangeException(nameof(numSubsets), numSubsets, "Number of subsets can only be 1, 2 or 3");
        }
        private static int GetIndexBitCount(int numSubsets, int partitionIndex, int bitCount, int index)
        {
            if (index == 0) return bitCount - 1;
            if (numSubsets == 2) {
                var anchorIndex = Subsets2AnchorIndices[partitionIndex];
                if (index == anchorIndex) {
                    return bitCount - 1;
                }
            }
            return bitCount;
        }

        private readonly int GetIndexBegin()
        {
            return HasSubsets ? 82 : 65;
        }

        internal readonly int GetColorIndex(int numSubsets, int partitionIndex, int bitCount, int index)
        {
            var indexOffset = GetIndexOffset(numSubsets, partitionIndex, bitCount, index);
            var indexBitCount = GetIndexBitCount(numSubsets, partitionIndex, bitCount, index);
            var indexBegin = GetIndexBegin();
            return (int)ByteHelper.ExtractFrom128(LowBits, HighBits, indexBegin + indexOffset, indexBitCount);
        }

        internal static (int, int, int) InterpolateColor((int, int, int) endPointStart, (int, int, int) endPointEnd,
            int colorIndex, int colorBitCount)
        {
            var result = (
                Bc6HCodec.Interpolate(endPointStart.Item1, endPointEnd.Item1, colorIndex, colorBitCount),
                Bc6HCodec.Interpolate(endPointStart.Item2, endPointEnd.Item2, colorIndex, colorBitCount),
                Bc6HCodec.Interpolate(endPointStart.Item3, endPointEnd.Item3, colorIndex, colorBitCount)
            );

            return result;
        }

        public readonly RawBlock4X4RgbFloat Decode(bool signed)
        {
            var output = new RawBlock4X4RgbFloat();
            var pixels = output.AsSpan;

            if (Type == Bc6HMode.Unknown) {
                return ErrorBlock;
            }

            var numSubsets = 1;
            var partitionIndex = 0;

            if (HasSubsets) {
                numSubsets = 2;
                partitionIndex = PartitionSetId;
            }

            Span<(int, int, int)> endpointBuffer = stackalloc (int, int, int)[4];
            var endpoints = endpointBuffer[..NumEndpoints];
            ExtractRawEndpoints(signed, endpoints);

            for (var i = 0; i < NumEndpoints; i++) {
                endpoints[i] = UnQuantize(endpoints[i], EndpointBits, signed);
            }

            for (var i = 0; i < pixels.Length; i++) {
                var subsetIndex = GetPartitionIndex(numSubsets, partitionIndex, i);

                var endPointStart = endpoints[2 * subsetIndex];
                var endPointEnd = endpoints[2 * subsetIndex + 1];

                var colorIndex = GetColorIndex(numSubsets, partitionIndex, ColorIndexBitCount, i);

                var (r, g, b) = FinishUnQuantize(InterpolateColor(endPointStart, endPointEnd, colorIndex, ColorIndexBitCount), signed);

                pixels[i] = new ColorRgbFloat(r, g, b);
            }

            return output;
        }
    }
}

namespace Lucitex.TextureCompression
{
    internal static class Bc6HCodec
    {
        private static ReadOnlySpan<byte> Weights3 => [0, 9, 18, 27, 37, 46, 55, 64];
        private static ReadOnlySpan<byte> Weights4 => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

        public static void Decode(ReadOnlySpan<byte> source, Span<float> destination, bool signed)
        {
            if (source.Length < 16 || destination.Length < 48) {
                throw new ArgumentException("BC6H buffers must contain one complete block.");
            }

            var block = new Bc6HDecoderBlock {
                LowBits = BinaryPrimitives.ReadUInt64LittleEndian(source),
                HighBits = BinaryPrimitives.ReadUInt64LittleEndian(source[8..]),
            };
            var decoded = block.Decode(signed);
            MemoryMarshal.Cast<ColorRgbFloat, float>(decoded.AsSpan).CopyTo(destination);
        }

        internal static int Interpolate(int first, int second, int index, int precision)
        {
            var weights = precision switch {
                3 => Weights3,
                4 => Weights4,
                _ => throw new ArgumentOutOfRangeException(nameof(precision)),
            };
            var weight = weights[index];
            return (((64 - weight) * first) + (weight * second) + 32) >> 6;
        }

        internal static int SignExtend(int value, int precision)
        {
            var signMask = 1 << (precision - 1);
            var numberMask = signMask - 1;
            return (value & signMask) != 0 ? ~numberMask | (value & numberMask) : value & numberMask;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct ColorRgbFloat
    {
        public float R;
        public float G;
        public float B;

        public ColorRgbFloat(float red, float green, float blue)
        {
            R = red;
            G = green;
            B = blue;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct RawBlock4X4RgbFloat
    {
        private ColorRgbFloat _p00, _p10, _p20, _p30;
        private ColorRgbFloat _p01, _p11, _p21, _p31;
        private ColorRgbFloat _p02, _p12, _p22, _p32;
        private ColorRgbFloat _p03, _p13, _p23, _p33;

        public RawBlock4X4RgbFloat(ColorRgbFloat color)
        {
            _p00 = _p10 = _p20 = _p30 = color;
            _p01 = _p11 = _p21 = _p31 = color;
            _p02 = _p12 = _p22 = _p32 = color;
            _p03 = _p13 = _p23 = _p33 = color;
        }

        public Span<ColorRgbFloat> AsSpan => MemoryMarshal.CreateSpan(ref _p00, 16);
    }
}
