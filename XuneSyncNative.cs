using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ZuneHdMetadataUpdater;

/// <summary>
/// P/Invoke bindings for XuneSyncLibrary (libxune_sync). Only the functions the serve
/// and artists commands need. See vendor/XuneSyncLibrary/include/xune_sync/xune_sync_api.h.
/// </summary>
internal static partial class XuneSyncNative
{
    private const string Lib = "xune_sync";

    static XuneSyncNative()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, _, _) =>
        {
            if (name != Lib)
                return IntPtr.Zero;
            var fileName = OperatingSystem.IsWindows() ? "xune_sync.dll"
                : OperatingSystem.IsMacOS() ? "libxune_sync.dylib" : "libxune_sync.so";
            string[] candidates =
            [
                Environment.GetEnvironmentVariable("XUNE_SYNC_LIB") ?? "",
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Environment.CurrentDirectory, fileName),
                Path.Combine(Environment.CurrentDirectory, "vendor", "XuneSyncLibrary", "build", fileName),
                Path.Combine(AppContext.BaseDirectory, "vendor", "XuneSyncLibrary", "build", fileName),
                // Visual Studio (multi-config) generator puts the DLL under build/<Config>/
                Path.Combine(Environment.CurrentDirectory, "vendor", "XuneSyncLibrary", "build", "Release", fileName),
                Path.Combine(AppContext.BaseDirectory, "vendor", "XuneSyncLibrary", "build", "Release", fileName),
            ];
            foreach (var path in candidates)
                if (path.Length > 0 && File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                    return handle;
            return IntPtr.Zero;
        });
    }

    public enum ArtistMetadataMode
    {
        Disabled = 0,
        Static = 1,
        Proxy = 2,
        Hybrid = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ArtistMetadataConfig
    {
        public ArtistMetadataMode mode;
        public IntPtr proxy_catalog_server;
        public IntPtr proxy_image_server;
        public IntPtr proxy_art_server;
        public IntPtr proxy_mix_server;
        public int proxy_timeout_ms;
        public IntPtr server_ip;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZuneMusicArtist
    {
        public IntPtr name;
        public IntPtr filename;
        public IntPtr guid;
        public uint atom_id;
    }

    // Leading fields of ZuneMusicLibrary only - nothing past artist_count is read.
    [StructLayout(LayoutKind.Sequential)]
    public struct ZuneMusicLibraryHead
    {
        public IntPtr tracks;
        public uint track_count;
        public IntPtr albums;
        public uint album_count;
        public IntPtr artists;
        public uint artist_count;
    }

    // MEMORY CONTRACT (see header): return a malloc-compatible string the C++ side
    // frees, or IntPtr.Zero for 404. Use AllocFreeableUtf8 — CoTaskMem is malloc-backed
    // on macOS/Linux only; on Windows it is the COM heap, so free() would corrupt it.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr PathResolverCallback(IntPtr artistUuid, IntPtr endpointType, IntPtr resourceId, IntPtr userData);

    /// <summary>
    /// UTF-8 C string the native library can pass to free(). On Windows this must come
    /// from the CRT heap the DLL links against (ucrtbase via /MD, the CMake default —
    /// do not build xune_sync with a static CRT).
    /// </summary>
    public static IntPtr AllocFreeableUtf8(string s)
    {
        if (!OperatingSystem.IsWindows())
            return Marshal.StringToCoTaskMemUTF8(s);
        var bytes = Encoding.UTF8.GetBytes(s);
        var ptr = ucrt_malloc((nuint)bytes.Length + 1);
        if (ptr == IntPtr.Zero)
            throw new OutOfMemoryException();
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    [DllImport("ucrtbase.dll", EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ucrt_malloc(nuint size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallback(IntPtr message);

    [DllImport(Lib)] public static extern IntPtr zune_get_last_error();
    [DllImport(Lib)] public static extern IntPtr zune_device_create();
    [DllImport(Lib)] public static extern void zune_device_destroy(IntPtr handle);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_connect_usb(IntPtr handle);
    [DllImport(Lib)] public static extern void zune_device_disconnect(IntPtr handle);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_detect_on_usb();

    [DllImport(Lib)] public static extern void zune_device_set_log_callback(IntPtr handle, LogCallback? callback);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    public static extern void zune_device_set_mtpz_data_path(IntPtr handle, string path);

    [DllImport(Lib)] public static extern IntPtr zune_device_get_name(IntPtr handle);
    [DllImport(Lib)] public static extern IntPtr zune_device_get_family_name(IntPtr handle);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_supports_network_mode(IntPtr handle);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_initialize_http_subsystem(IntPtr handle);

    [DllImport(Lib)]
    public static extern int zune_device_start_artist_metadata_interceptor(IntPtr handle, ref ArtistMetadataConfig config);
    [DllImport(Lib)]
    public static extern void zune_device_stop_artist_metadata_interceptor(IntPtr handle);

    [DllImport(Lib)]
    public static extern void zune_device_set_path_resolver_callback(IntPtr handle, PathResolverCallback callback, IntPtr userData);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_trigger_network_mode(IntPtr handle);

    [DllImport(Lib)] public static extern void zune_device_enable_network_polling(IntPtr handle);

    [DllImport(Lib)]
    public static extern int zune_device_poll_network_data(IntPtr handle, int timeoutMs);

    [DllImport(Lib)]
    public static extern void zune_device_set_verbose_network_logging(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_read_network_state(IntPtr handle,
        out int active, out int progress, out int phase, out int status);

    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool zune_device_teardown_network_session(IntPtr handle);

    // Read-only library fetch. The result is native-owned: release it with the free call.
    [DllImport(Lib)] public static extern IntPtr zune_device_get_music_library(IntPtr handle);
    [DllImport(Lib)] public static extern void zune_device_free_music_library(IntPtr library);

    /// <summary>
    /// The library only ever sets its last-error string in zune_mtp_send_object, so every
    /// serve-path failure returns "" (non-null). Real detail comes from the log callback.
    /// </summary>
    public static string LastError()
    {
        var ptr = zune_get_last_error();
        var msg = ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
        return string.IsNullOrEmpty(msg) ? "(no detail - re-run with --verbose)" : msg;
    }
}
