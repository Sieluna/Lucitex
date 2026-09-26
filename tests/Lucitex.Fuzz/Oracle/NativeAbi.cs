using System.Runtime.InteropServices;

namespace Lucitex.Fuzz;

internal enum NativeStatus : uint
{
    Accepted = 0,
    Rejected = 1,
    ResourceLimit = 3,
    Unsupported = 4,
    InvalidRequest = 64,
    InternalError = 70,
}

internal enum NativeFormat : uint
{
    Png = 1,
    Exr = 2,
    Ktx2 = 3,
    Jpeg = 4,
    Webp = 5,
}

internal enum NativeStructKind : uint
{
    Request = 1,
    Limits = 2,
    Result = 3,
}

internal static class NativeAbi
{
    public const uint Version = 1;
}

internal enum NativeOperation : uint
{
    Validate,
    WebpRgba,
    WebpYuv,
    WebpEncode,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeRequest
{
    public uint StructSize;
    public NativeOperation Operation;
    public NativeFormat Format;
    public uint Width;
    public uint Height;
    public float Quality;
    public ulong InputLength;
    public byte* Input;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeLimits
{
    public ulong MaxInputBytes;
    public ulong MaxDimensions;
    public ulong MaxPixels;
    public ulong MaxDecodedBytes;
    public ulong MaxChannels;
    public ulong MaxLevels;
    public ulong MaxArrayElements;
    public ulong MaxWorkingSet;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeResult
{
    public uint StructSize;
    public uint Warnings;
    public uint Width;
    public uint Height;
    public ulong DecodedBytes;
    public ulong Subresources;
    public ulong OutputLength;
    public byte* Output;
    public fixed byte Message[512];
}

internal sealed record NativePayload(DecodeOutcome Outcome, byte[] Bytes, uint Width = 0, uint Height = 0, uint Warnings = 0);
