namespace Lucitex.Webp.Lossy;

internal static partial class Vp8Tables
{
    public const int DcPred = 0;
    public const int VPred = 1;
    public const int HPred = 2;
    public const int TmPred = 3;
    public const int BPred = 4;

    public const int BDcPred = 0;
    public const int BTmPred = 1;
    public const int BVePred = 2;
    public const int BHePred = 3;
    public const int BLdPred = 4;
    public const int BRdPred = 5;
    public const int BVrPred = 6;
    public const int BVlPred = 7;
    public const int BHdPred = 8;
    public const int BHuPred = 9;

    public static readonly sbyte[] KfYModeTree =
    [
        -BPred, 2,
        4, 6,
        -DcPred, -VPred,
        -HPred, -TmPred,
    ];

    public static readonly byte[] KfYModeProb = [145, 156, 163, 128];

    public static readonly sbyte[] UvModeTree =
    [
        -DcPred, 2,
        -VPred, 4,
        -HPred, -TmPred,
    ];

    public static readonly byte[] KfUvModeProb = [142, 114, 183];

    public static readonly sbyte[] BModeTree =
    [
        -BDcPred, 2,
        -BTmPred, 4,
        -BVePred, 6,
        8, 12,
        -BHePred, 10,
        -BRdPred, -BVrPred,
        -BLdPred, 14,
        -BVlPred, 16,
        -BHdPred, -BHuPred,
    ];

    public static readonly sbyte[] MbSegmentTree =
    [
        2, 4,
        0, -1,
        -2, -3,
    ];

    public const int DctEob = 11;
    public const int DctCat1 = 5;
    public const int DctCat2 = 6;
    public const int DctCat3 = 7;
    public const int DctCat4 = 8;
    public const int DctCat5 = 9;
    public const int DctCat6 = 10;

    public static readonly sbyte[] CoeffTree =
    [
        -DctEob, 2,
        -0, 4,
        -1, 6,
        8, 12,
        -2, 10,
        -3, -4,
        14, 16,
        -DctCat1, -DctCat2,
        18, 20,
        -DctCat3, -DctCat4,
        -DctCat5, -DctCat6,
    ];

    public static readonly byte[] Pcat1 = [159];
    public static readonly byte[] Pcat2 = [165, 145];
    public static readonly byte[] Pcat3 = [173, 148, 140];
    public static readonly byte[] Pcat4 = [176, 155, 140, 135];
    public static readonly byte[] Pcat5 = [180, 157, 141, 134, 130];
    public static readonly byte[] Pcat6 = [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129];

    public static readonly byte[][] CategoryProbs = [Pcat1, Pcat2, Pcat3, Pcat4, Pcat5, Pcat6];

    public static readonly int[] CategoryBase = [5, 7, 11, 19, 35, 67];

    public static readonly int[] CoeffBands = [0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7];

    public static readonly int[] LeftContextIndex =
    [
        0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3,
        4, 4, 5, 5, 6, 6, 7, 7, 8,
    ];

    public static readonly int[] AboveContextIndex =
    [
        0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
        4, 5, 4, 5, 6, 7, 6, 7, 8,
    ];
}
