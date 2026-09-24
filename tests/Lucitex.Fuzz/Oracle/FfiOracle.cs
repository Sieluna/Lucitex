using System.Runtime.InteropServices;
using System.Text;
using Lucitex.Core.Execution;

namespace Lucitex.Fuzz;

internal sealed unsafe class FfiOracle : IDecodeOracle
{
    private readonly object _sync = new();
    private readonly NativeLibraryHandle? _library;
    private readonly string? _loadError;
    private readonly delegate* unmanaged[Cdecl]<NativeRequest*, NativeLimits*, NativeResult*, uint> _execute;
    private readonly delegate* unmanaged[Cdecl]<NativeResult*, void> _release;
    private bool _disposed;

    public FfiOracle(string libraryPath)
    {
        try {
            _library = new NativeLibraryHandle(libraryPath);
            var handle = _library.DangerousGetHandle();
            var version = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(handle, "lucitex_oracle_abi_version");
            var size = (delegate* unmanaged[Cdecl]<uint, uint>)NativeLibrary.GetExport(handle, "lucitex_oracle_struct_size");
            if (version() != 1 || size(1) != sizeof(NativeRequest) || size(2) != sizeof(NativeLimits) || size(3) != sizeof(NativeResult)) {
                throw new BadImageFormatException("Native oracle ABI version or struct layout does not match this harness.");
            }
            _execute = (delegate* unmanaged[Cdecl]<NativeRequest*, NativeLimits*, NativeResult*, uint>)NativeLibrary.GetExport(handle, "lucitex_oracle_execute");
            _release = (delegate* unmanaged[Cdecl]<NativeResult*, void>)NativeLibrary.GetExport(handle, "lucitex_oracle_release");
            GC.KeepAlive(_library);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) {
            _loadError = exception.Message;
            _library?.Dispose();
        }
    }

    public bool Supports(ImageFormat format) => format is ImageFormat.Png or ImageFormat.Exr or ImageFormat.Ktx2 or ImageFormat.Jpeg or ImageFormat.Webp;

    public DecodeOutcome Decode(ImageFormat format, byte[] data) => Decode(format, data, FuzzLimits.Decode);

    public DecodeOutcome Decode(ImageFormat format, byte[] data, DecodeLimits limits) =>
        Invoke(format, data, NativeOperation.Validate, limits).Outcome;

    public NativePayload DecodeWebpRgba(byte[] data) => Invoke(ImageFormat.Webp, data, NativeOperation.WebpRgba, FuzzLimits.Decode);

    public NativePayload DecodeWebpYuv(byte[] data) => Invoke(ImageFormat.Webp, data, NativeOperation.WebpYuv, FuzzLimits.Decode);

    public NativePayload EncodeWebpRgba(byte[] rgba, uint width, uint height, float quality = 80) =>
        Invoke(ImageFormat.Webp, rgba, NativeOperation.WebpEncode, FuzzLimits.Decode, width, height, quality);

    private NativePayload Invoke(ImageFormat format, byte[] data, NativeOperation operation, DecodeLimits limits,
        uint width = 0, uint height = 0, float quality = 80)
    {
        lock (_sync) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_loadError is not null) {
                return new(new(DecodeStatus.InfrastructureFailure, _loadError), []);
            }
            if (!Supports(format)) {
                return new(new(DecodeStatus.Unsupported, $"No native oracle for {format}."), []);
            }
            var nativeLimits = new NativeLimits {
                MaxInputBytes = Math.Max(FuzzLimits.MaxInputBytes, checked((ulong)limits.MaxWorkingSet)),
                MaxDimensions = checked((ulong)limits.MaxDimensions),
                MaxPixels = checked((ulong)limits.MaxPixels),
                MaxDecodedBytes = checked((ulong)limits.MaxDecodedBytes),
                MaxChannels = checked((ulong)limits.MaxChannels),
                MaxLevels = checked((ulong)limits.MaxLevels),
                MaxArrayElements = checked((ulong)limits.MaxArrayElements),
                MaxWorkingSet = checked((ulong)limits.MaxWorkingSet),
            };
            var result = new NativeResult { StructSize = (uint)sizeof(NativeResult) };
            try {
                fixed (byte* input = data) {
                    var request = new NativeRequest {
                        StructSize = (uint)sizeof(NativeRequest),
                        Operation = operation,
                        Format = format switch {
                            ImageFormat.Png => 1,
                            ImageFormat.Exr => 2,
                            ImageFormat.Ktx2 => 3,
                            ImageFormat.Jpeg => 4,
                            ImageFormat.Webp => 5,
                            _ => 0,
                        },
                        Width = width,
                        Height = height,
                        Quality = quality,
                        InputLength = (ulong)data.Length,
                        Input = input,
                    };
                    var code = _execute(&request, &nativeLimits, &result);
                    var status = code switch {
                        0 => DecodeStatus.Accepted,
                        1 => DecodeStatus.Rejected,
                        3 => DecodeStatus.ResourceLimit,
                        4 => DecodeStatus.Unsupported,
                        64 => DecodeStatus.InfrastructureFailure,
                        70 => DecodeStatus.Crash,
                        _ => DecodeStatus.InfrastructureFailure,
                    };
                    if (result.StructSize != sizeof(NativeResult) || result.OutputLength > int.MaxValue ||
                        result.OutputLength > Math.Max(nativeLimits.MaxDecodedBytes, nativeLimits.MaxInputBytes) ||
                        result.DecodedBytes > long.MaxValue || result.Subresources > int.MaxValue ||
                        (result.Output == null && result.OutputLength != 0)) {
                        return new(new(DecodeStatus.InfrastructureFailure, "Invalid native result layout or length."), []);
                    }
                    var message = new ReadOnlySpan<byte>(result.Message, 512);
                    var end = message.IndexOf((byte)0);
                    var detail = Encoding.UTF8.GetString(end < 0 ? message : message[..end]);
                    var bytes = result.OutputLength == 0 ? [] : new ReadOnlySpan<byte>(result.Output, (int)result.OutputLength).ToArray();
                    return new(new(status, string.IsNullOrEmpty(detail) ? null : detail, (int)result.Subresources, (long)result.DecodedBytes),
                        bytes, result.Width, result.Height, result.Warnings);
                }
            }
            finally {
                _release(&result);
                GC.KeepAlive(_library);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync) {
            if (!_disposed) {
                _disposed = true;
                _library?.Dispose();
            }
        }
    }
}
