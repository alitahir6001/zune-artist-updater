using System.Runtime.InteropServices;
using static ZuneHdMetadataUpdater.XuneSyncNative;

namespace ZuneHdMetadataUpdater;

/// <summary>
/// Connects to the Zune HD over USB, starts the artist-metadata HTTP interceptor in
/// static mode, and answers the device's /v3.0/en-US/... requests from the data dir
/// via the path-resolver callback. Runs until Ctrl+C.
/// </summary>
internal static class ServeCommand
{
    private static string _dataDir = "";
    private static volatile bool _running = true;

    // Keep delegate instances alive for the lifetime of the interceptor - the native
    // side stores the raw function pointers.
    private static readonly PathResolverCallback ResolverDelegate = ResolvePath;
    private static readonly LogCallback LogDelegate = msg =>
        Console.WriteLine($"  [xune] {Marshal.PtrToStringUTF8(msg)}");

    public static int Run(string dataDir, string? mtpzPath, bool verbose)
    {
        _dataDir = dataDir;
        if (!Directory.Exists(dataDir))
        {
            Console.Error.WriteLine($"Data directory not found: {dataDir} - run `zunemd update` first");
            return 1;
        }
        Console.WriteLine($"Serving artist data from {dataDir} ({Directory.GetDirectories(dataDir).Length} artist(s))");

        if (!zune_device_detect_on_usb())
        {
            Console.Error.WriteLine("No Zune device detected on USB.");
            return 1;
        }

        var device = zune_device_create();
        try
        {
            if (verbose)
                zune_device_set_log_callback(device, LogDelegate);
            // Resolve the ~/.mtpz-data default here: the native fallback reads $HOME,
            // which is normally unset on Windows.
            mtpzPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mtpz-data");
            zune_device_set_mtpz_data_path(device, mtpzPath);

            Console.WriteLine("Connecting to Zune over USB...");
            if (!zune_device_connect_usb(device))
            {
                Console.Error.WriteLine($"USB connect failed: {LastError()}");
                Console.Error.WriteLine("MTPZ keys are required (~/.mtpz-data or --mtpz PATH).");
                return 1;
            }

            Console.WriteLine($"Connected: {Marshal.PtrToStringUTF8(zune_device_get_name(device))} " +
                              $"({Marshal.PtrToStringUTF8(zune_device_get_family_name(device))})");

            if (!zune_device_supports_network_mode(device))
            {
                Console.Error.WriteLine("This device does not support network mode (Zune HD only).");
                return 1;
            }

            Console.WriteLine("Initializing HTTP subsystem...");
            if (!zune_device_initialize_http_subsystem(device))
            {
                Console.Error.WriteLine($"HTTP subsystem init failed: {LastError()}");
                return 1;
            }

            zune_device_set_path_resolver_callback(device, ResolverDelegate, IntPtr.Zero);
            zune_device_set_verbose_network_logging(device, verbose);

            // server_ip is the address DNS hands the device for catalog.zune.net;
            // all traffic stays inside the PPP link, so any private IP works.
            var serverIp = Marshal.StringToCoTaskMemUTF8("192.168.0.30");
            var config = new ArtistMetadataConfig
            {
                mode = ArtistMetadataMode.Static,
                proxy_timeout_ms = 5000,
                server_ip = serverIp,
            };
            var rc = zune_device_start_artist_metadata_interceptor(device, ref config);
            Marshal.FreeCoTaskMem(serverIp);
            if (rc != 0)
            {
                Console.Error.WriteLine($"Interceptor start failed ({rc}): {LastError()}");
                return 1;
            }

            Console.WriteLine("Establishing network mode (PPP over USB)...");
            var networkUp = false;
            for (var attempt = 1; attempt <= 10 && !networkUp; attempt++)
            {
                networkUp = zune_device_trigger_network_mode(device);
                if (!networkUp)
                {
                    Console.WriteLine($"  attempt {attempt}/10 failed, retrying...");
                    Thread.Sleep(500);
                }
            }
            if (!networkUp)
            {
                Console.Error.WriteLine($"Could not establish network mode: {LastError()}");
                return 1;
            }
            zune_device_enable_network_polling(device);

            Console.WriteLine("Network mode up. The Zune should now request artist metadata.");
            Console.WriteLine("Waiting for requests - press Ctrl+C to stop.");
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };

            PollLoop(device);

            Console.WriteLine("Stopping...");
            zune_device_teardown_network_session(device);
            zune_device_stop_artist_metadata_interceptor(device);
            zune_device_disconnect(device);
            return 0;
        }
        finally
        {
            zune_device_destroy(device);
        }
    }

    private static void PollLoop(IntPtr device)
    {
        var lastState = (-1, -1, -1, -1);
        var lastStateCheck = DateTime.MinValue;

        while (_running)
        {
            var rc = zune_device_poll_network_data(device, 100);
            if (rc < 0)
            {
                Console.Error.WriteLine($"Polling stopped (code {rc}): {LastError()}");
                break;
            }

            if (DateTime.UtcNow - lastStateCheck > TimeSpan.FromSeconds(5))
            {
                lastStateCheck = DateTime.UtcNow;
                if (zune_device_read_network_state(device, out var active, out var progress, out var phase, out var status)
                    && (active, progress, phase, status) != lastState)
                {
                    lastState = (active, progress, phase, status);
                    Console.WriteLine($"  device state: active={active} progress={progress} phase={phase} status={status}" +
                                      ((progress, phase, status) == (100, 4, 3) ? " (metadata fetch complete)" : ""));
                }
            }
        }
    }

    /// <summary>
    /// Maps a device request (artist MBID + endpoint type + optional image id) to a
    /// local file. Returning IntPtr.Zero makes the interceptor answer 404.
    /// </summary>
    private static IntPtr ResolvePath(IntPtr artistUuidPtr, IntPtr endpointTypePtr, IntPtr resourceIdPtr, IntPtr _)
    {
        var uuid = (Marshal.PtrToStringUTF8(artistUuidPtr) ?? "").ToLowerInvariant();
        var endpoint = Marshal.PtrToStringUTF8(endpointTypePtr) ?? "";
        var resourceId = Marshal.PtrToStringUTF8(resourceIdPtr);

        var file = endpoint switch
        {
            "overview" => Path.Combine(_dataDir, uuid, "overview.xml"),
            "biography" => Path.Combine(_dataDir, uuid, "biography.xml"),
            "images" => Path.Combine(_dataDir, uuid, "images.xml"),
            "albums" => Path.Combine(_dataDir, uuid, "albums.xml"),
            "similarartists" => Path.Combine(_dataDir, uuid, "similarArtists.xml"),
            "tracks" => Path.Combine(_dataDir, uuid, "tracks.xml"),
            "devicebackgroundimage" => Path.Combine(_dataDir, uuid, "1.jpg"),
            "artwork" => ResolveImageFile(resourceId),
            _ => null,
        };

        var found = file != null && File.Exists(file);
        Console.WriteLine($"  GET {endpoint} {(uuid.Length > 0 ? uuid : resourceId)} -> {(found ? Path.GetFileName(file) : "404")}");
        return found ? AllocFreeableUtf8(Path.GetFullPath(file!)) : IntPtr.Zero;
    }

    /// <summary>Image ids are "{mbid-prefix}-{num:D4}-0000-...": num 10 = thumb.jpg, 1-6 = gallery.</summary>
    private static string? ResolveImageFile(string? resourceId)
    {
        if (resourceId == null)
            return null;
        var parts = resourceId.ToLowerInvariant().Split('-');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var num))
            return null;

        var dir = Directory.EnumerateDirectories(_dataDir)
            .FirstOrDefault(d => Path.GetFileName(d).StartsWith(parts[0], StringComparison.OrdinalIgnoreCase));
        return dir == null ? null : Path.Combine(dir, num == 10 ? "thumb.jpg" : $"{num}.jpg");
    }
}
