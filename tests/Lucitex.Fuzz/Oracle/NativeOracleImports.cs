using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucitex.Fuzz;

internal static unsafe partial class NativeOracleImports
{
    private const string LibraryName = "lucitex_fuzz_native";
    private const string AbiVersionEntryPoint = "lucitex_oracle_abi_version";
    private const string StructSizeEntryPoint = "lucitex_oracle_struct_size";
    private const string ExecuteEntryPoint = "lucitex_oracle_execute";
    private const string ReleaseEntryPoint = "lucitex_oracle_release";
    private static readonly object Sync = new();
    private static string? _libraryPath;
    private static string? _loadedPath;
    private static IntPtr _libraryHandle;

    static NativeOracleImports()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeOracleImports).Assembly, ResolveLibrary);
    }

    internal static void ConfigureLibrary(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (Sync) {
            if (_loadedPath is not null && !PathsEqual(_loadedPath, fullPath)) {
                throw new InvalidOperationException(
                    $"The native oracle is already loaded from '{_loadedPath}' and cannot be changed to '{fullPath}' in this process.");
            }
            _libraryPath = fullPath;
        }
    }

    internal static void ValidateExports()
    {
        lock (Sync) {
            if (_libraryHandle == IntPtr.Zero) {
                throw new DllNotFoundException("The native oracle library has not been loaded.");
            }
            NativeLibrary.GetExport(_libraryHandle, ExecuteEntryPoint);
            NativeLibrary.GetExport(_libraryHandle, ReleaseEntryPoint);
        }
    }

    [LibraryImport(LibraryName, EntryPoint = AbiVersionEntryPoint)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = StructSizeEntryPoint)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint StructSize(NativeStructKind type);

    [LibraryImport(LibraryName, EntryPoint = ExecuteEntryPoint)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Execute(NativeRequest* request, NativeLimits* limits, NativeResult* result);

    [LibraryImport(LibraryName, EntryPoint = ReleaseEntryPoint)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Release(NativeResult* result);

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) {
            return IntPtr.Zero;
        }

        lock (Sync) {
            if (_libraryHandle != IntPtr.Zero) {
                return _libraryHandle;
            }
            if (_libraryPath is null) {
                return IntPtr.Zero;
            }

            _libraryHandle = NativeLibrary.Load(_libraryPath);
            _loadedPath = _libraryPath;
            return _libraryHandle;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
