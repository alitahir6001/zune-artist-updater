namespace ZuneHdMetadataUpdater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var dataDir = GetOption(args, "--data-dir") ?? Path.Combine(Environment.CurrentDirectory, "artist_data");

        switch (args[0])
        {
            case "update":
            {
                if (args.Length < 2 || args[1].StartsWith("--"))
                {
                    Console.Error.WriteLine("update requires a music folder path");
                    return 1;
                }
                return await UpdateCommand.Run(args[1], dataDir);
            }
            case "serve":
                return ServeCommand.Run(dataDir, GetOption(args, "--mtpz"), args.Contains("--verbose"));
            default:
                PrintUsage();
                return 1;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            zunemd - ZuneHD artist metadata updater

            Usage:
              zunemd update <music-folder> [--data-dir DIR]
                  Scan MP3s, look up artists on MusicBrainz, write the MBID back to
                  each file's ID3 tag, and download biography + images into DIR
                  (default: ./artist_data).

              zunemd serve [--data-dir DIR] [--mtpz PATH] [--verbose]
                  Connect to a Zune HD over USB and serve the artist data when the
                  device requests /v3.0/en-US/music/artist/{mbid}/... URLs.
                  --mtpz points at the MTPZ keys file (default: ~/.mtpz-data).

            Typical flow: plug in the Zune HD, run `zunemd update ~/Music`, sync the
            music, then `zunemd serve` and let the device fetch artist metadata.
            """);
    }
}
