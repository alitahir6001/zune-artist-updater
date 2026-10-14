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

        string? dataDirOpt, mtpzOpt;
        try
        {
            if (UnknownOption(args) is { } bad)
            {
                Console.Error.WriteLine($"Unknown option: {bad}");
                PrintUsage();
                return 1;
            }
            dataDirOpt = GetOption(args, "--data-dir");
            mtpzOpt = GetOption(args, "--mtpz");
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }

        var dataDir = dataDirOpt ?? Path.Combine(Environment.CurrentDirectory, "artist_data");

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
                return ServeCommand.Run(dataDir, mtpzOpt, args.Contains("--verbose"));
            case "artists":
                return ArtistsCommand.Run(dataDir, mtpzOpt, args.Contains("--verbose"));
            default:
                PrintUsage();
                return 1;
        }
    }

    private static readonly string[] ValueFlags = ["--data-dir", "--mtpz"];
    private static readonly string[] BoolFlags = ["--verbose"];

    private static string? GetOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0)
            return null;
        // Without this, `serve --mtpz --verbose` would use "--verbose" as the key path
        // and then report the far more confusing "MTPZ keys are required".
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
            throw new ArgumentException($"{name} requires a value");
        return args[i + 1];
    }

    /// <summary>A typo'd `--verbose` would otherwise run silently with no diagnostics.</summary>
    private static string? UnknownOption(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--"))
                continue;
            if (!ValueFlags.Contains(args[i]) && !BoolFlags.Contains(args[i]))
                return args[i];
            if (ValueFlags.Contains(args[i]))
                i++;
        }
        return null;
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

              zunemd artists [--data-dir DIR] [--mtpz PATH] [--verbose]
                  Connect to a Zune over USB and list every artist on it with the ID
                  the device holds for that artist. Read-only.

            Typical flow: plug in the Zune HD, run `zunemd update ~/Music`, sync the
            music, then `zunemd serve` and let the device fetch artist metadata.
            """);
    }
}
