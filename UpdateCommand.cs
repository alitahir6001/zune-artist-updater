namespace ZuneHdMetadataUpdater;

/// <summary>
/// Scans a music folder, resolves each artist's MusicBrainz MBID, writes the MBID
/// back into every MP3's ID3 tag (so the Zune desktop syncs it to the device as the
/// artist GUID), and downloads biography + images into the data directory.
/// </summary>
internal static class UpdateCommand
{
    public static async Task<int> Run(string musicFolder, string dataDir)
    {
        if (!Directory.Exists(musicFolder))
        {
            Console.Error.WriteLine($"Music folder not found: {musicFolder}");
            return 1;
        }

        var files = Directory.EnumerateFiles(musicFolder, "*.mp3", SearchOption.AllDirectories).ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No MP3 files found under {musicFolder}");
            return 1;
        }
        Console.WriteLine($"Found {files.Count} MP3 file(s)");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Wikimedia (and others) reject UA-less requests; MusicBrainz adds its own UA per request.
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", MusicBrainzClient.UserAgent);
        var musicBrainz = new MusicBrainzClient(http);
        var fetcher = new ArtistDataFetcher(http, musicBrainz);

        // artist name -> resolved match (null = lookup failed, don't retry)
        var resolved = new Dictionary<string, MusicBrainzClient.ArtistMatch?>(StringComparer.OrdinalIgnoreCase);
        var fetchedMbids = new HashSet<string>();
        var failures = 0;

        foreach (var file in files)
        {
            TagLib.File tagFile;
            try
            {
                tagFile = TagLib.File.Create(file);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"  SKIP {Path.GetFileName(file)}: unreadable ({e.Message})");
                failures++;
                continue;
            }

            using (tagFile)
            {
                var artist = tagFile.Tag.FirstAlbumArtist ?? tagFile.Tag.FirstPerformer;
                if (string.IsNullOrWhiteSpace(artist))
                {
                    artist = ArtistFromFilename(file);
                    Console.WriteLine($"  {Path.GetFileName(file)}: no artist tag, searching MusicBrainz for \"{artist}\"");
                }

                if (!resolved.TryGetValue(artist, out var match))
                {
                    Console.WriteLine($"  Looking up \"{artist}\" on MusicBrainz...");
                    try
                    {
                        match = await musicBrainz.SearchArtistAsync(artist);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"    lookup failed: {e.Message}");
                        match = null;
                    }
                    resolved[artist] = match;
                    if (match == null)
                        Console.Error.WriteLine($"    no MusicBrainz match for \"{artist}\"");
                    else
                        Console.WriteLine($"    -> {match.Name} ({match.Mbid})");
                }

                if (match == null)
                {
                    failures++;
                    continue;
                }

                if (tagFile.Tag.MusicBrainzArtistId != match.Mbid)
                {
                    tagFile.Tag.MusicBrainzArtistId = match.Mbid;
                    tagFile.Save();
                    Console.WriteLine($"  {Path.GetFileName(file)}: wrote MBID to ID3 tag");
                }

                if (fetchedMbids.Add(match.Mbid))
                {
                    Console.WriteLine($"  Fetching artist data for {match.Name}...");
                    await fetcher.FetchAsync(dataDir, match.Mbid, match.Name, match.SortName);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {fetchedMbids.Count} artist(s) written to {dataDir}" +
                          (failures > 0 ? $", {failures} file(s) skipped" : ""));
        Console.WriteLine("Next: sync the music to the Zune HD, then run `zunemd serve` with the device connected.");
        return 0;
    }

    /// <summary>Guess the artist from "Artist - Title.mp3" style names, stripping leading track numbers.</summary>
    private static string ArtistFromFilename(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"^\d+[\s.\-_]+", "");
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? name[..dash].Trim() : name.Trim();
    }
}
