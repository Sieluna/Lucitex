using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lucitex.Fuzz;

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
    public uint Format;
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

internal sealed class NativeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public NativeLibraryHandle(string path) : base(ownsHandle: true)
    {
        SetHandle(NativeLibrary.Load(Path.GetFullPath(path)));
    }

    protected override bool ReleaseHandle()
    {
        NativeLibrary.Free(handle);
        return true;
    }
}

internal sealed record NativePayload(DecodeOutcome Outcome, byte[] Bytes, uint Width = 0, uint Height = 0, uint Warnings = 0);
