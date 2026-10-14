using System.Runtime.InteropServices;
using static ZuneHdMetadataUpdater.XuneSyncNative;

namespace ZuneHdMetadataUpdater;

/// <summary>
/// Connects to the Zune over USB, reads its music library, and lists every artist with
/// the ID the device holds for it. Read-only - nothing is written to the device.
/// </summary>
internal static class ArtistsCommand
{
    internal record DeviceArtist(string Name, string Guid, string ArtFile);

    public static int Run(string dataDir, string? mtpzPath, bool verbose)
    {
        if (!zune_device_detect_on_usb())
        {
            Console.Error.WriteLine("No Zune device detected on USB.");
            return 1;
        }

        // Same default as serve: the native fallback reads $HOME, unset on Windows.
        mtpzPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mtpz-data");
        if (!File.Exists(mtpzPath))
        {
            // Connect still succeeds without keys; the library read then fails with no reason.
            Console.Error.WriteLine($"MTPZ keys not found at {mtpzPath} (use --mtpz PATH).");
            return 1;
        }

        var device = zune_device_create();
        if (device == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Could not create device handle: {LastError()}");
            return 1;
        }

        var connected = false;
        try
        {
            if (verbose)
                zune_device_set_log_callback(device, ServeCommand.LogDelegate);
            zune_device_set_mtpz_data_path(device, mtpzPath);

            Console.WriteLine("Connecting to Zune over USB...");
            if (!zune_device_connect_usb(device))
            {
                Console.Error.WriteLine($"USB connect failed: {LastError()}");
                return 1;
            }
            connected = true;
            Console.WriteLine($"Connected: {Marshal.PtrToStringUTF8(zune_device_get_name(device))} " +
                              $"({Marshal.PtrToStringUTF8(zune_device_get_family_name(device))})");

            var library = zune_device_get_music_library(device);
            if (library == IntPtr.Zero)
            {
                Console.Error.WriteLine("Could not read the device library - re-run with --verbose for detail.");
                return 1;
            }

            List<DeviceArtist> artists;
            try { artists = ReadArtists(library); }
            finally { zune_device_free_music_library(library); }

            Print(artists, dataDir);
            return 0;
        }
        finally
        {
            if (connected) zune_device_disconnect(device);
            zune_device_destroy(device);
        }
    }

    /// <summary>Copies the artist array out of a native ZuneMusicLibrary.</summary>
    internal static List<DeviceArtist> ReadArtists(IntPtr library)
    {
        var head = Marshal.PtrToStructure<ZuneMusicLibraryHead>(library);
        var size = Marshal.SizeOf<ZuneMusicArtist>();
        var artists = new List<DeviceArtist>((int)head.artist_count);
        for (var i = 0; i < head.artist_count; i++)
        {
            var a = Marshal.PtrToStructure<ZuneMusicArtist>(head.artists + i * size);
            artists.Add(new DeviceArtist(
                Marshal.PtrToStringUTF8(a.name) ?? "",
                Marshal.PtrToStringUTF8(a.guid) ?? "",
                Marshal.PtrToStringUTF8(a.filename) ?? ""));
        }
        return artists;
    }

    private static void Print(List<DeviceArtist> artists, string dataDir)
    {
        var haveData = Directory.Exists(dataDir);
        // Two artists holding one ID would show the same card on the device.
        var shared = artists.Where(a => a.Guid.Length > 0).GroupBy(a => a.Guid)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var width = Math.Min(40, artists.Select(a => a.Name.Length).DefaultIfEmpty(6).Max());

        Console.WriteLine();
        Console.WriteLine($"{"Artist".PadRight(width)}  {"ID on device",-36}  art" + (haveData ? "  data" : ""));
        foreach (var a in artists.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var hasFolder = a.Guid.Length > 0 && Directory.Exists(Path.Combine(dataDir, a.Guid.ToLowerInvariant()));
            Console.WriteLine($"{a.Name.PadRight(width)}  {(a.Guid.Length > 0 ? a.Guid : "-"),-36}  " +
                              $"{(a.ArtFile.Length > 0 ? "yes" : "no"),-3}" +
                              (haveData ? $"  {(hasFolder ? "yes" : "no"),-4}" : "") +
                              (shared.Contains(a.Guid) ? "  shared ID" : ""));
        }

        var withId = artists.Count(a => a.Guid.Length > 0);
        Console.WriteLine();
        Console.WriteLine($"{artists.Count} artist(s): {withId} with an ID, {artists.Count - withId} without" +
                          (shared.Count > 0 ? $", {artists.Count(a => shared.Contains(a.Guid))} sharing an ID" : "") + ".");
    }
}
