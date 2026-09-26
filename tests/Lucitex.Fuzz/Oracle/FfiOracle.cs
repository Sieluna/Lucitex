using System.Text;
using Lucitex.Core.Execution;

namespace Lucitex.Fuzz;

internal sealed unsafe class FfiOracle : IDecodeOracle
{
    private readonly object _sync = new();
    private readonly string? _loadError;
    private bool _disposed;

    public FfiOracle(string libraryPath)
    {
        try {
            NativeOracleImports.ConfigureLibrary(libraryPath);
            var version = NativeOracleImports.AbiVersion();
            var requestSize = NativeOracleImports.StructSize(NativeStructKind.Request);
            var limitsSize = NativeOracleImports.StructSize(NativeStructKind.Limits);
            var resultSize = NativeOracleImports.StructSize(NativeStructKind.Result);
            NativeOracleImports.ValidateExports();
            if (version != NativeAbi.Version || requestSize != sizeof(NativeRequest) || limitsSize != sizeof(NativeLimits) || resultSize != sizeof(NativeResult)) {
                throw new BadImageFormatException("Native oracle ABI version or struct layout does not match this harness.");
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
                InvalidOperationException or TypeInitializationException) {
            _loadError = exception.GetBaseException().Message;
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
                        Format = ToNativeFormat(format),
                        Width = width,
                        Height = height,
                        Quality = quality,
                        InputLength = (ulong)data.Length,
                        Input = input,
                    };
                    var code = NativeOracleImports.Execute(&request, &nativeLimits, &result);
                    var status = ((NativeStatus)code) switch {
                        NativeStatus.Accepted => DecodeStatus.Accepted,
                        NativeStatus.Rejected => DecodeStatus.Rejected,
                        NativeStatus.ResourceLimit => DecodeStatus.ResourceLimit,
                        NativeStatus.Unsupported => DecodeStatus.Unsupported,
                        NativeStatus.InvalidRequest => DecodeStatus.InfrastructureFailure,
                        NativeStatus.InternalError => DecodeStatus.Crash,
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
                NativeOracleImports.Release(&result);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync) {
            if (!_disposed) {
                _disposed = true;
            }
        }
    }

    private static NativeFormat ToNativeFormat(ImageFormat format) => format switch {
        ImageFormat.Png => NativeFormat.Png,
        ImageFormat.Exr => NativeFormat.Exr,
        ImageFormat.Ktx2 => NativeFormat.Ktx2,
        ImageFormat.Jpeg => NativeFormat.Jpeg,
        ImageFormat.Webp => NativeFormat.Webp,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
