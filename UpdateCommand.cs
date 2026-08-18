namespace ZuneHdMetadataUpdater;

/// <summary>
/// Scans a music folder, resolves each artist's MusicBrainz MBID, writes the MBID
/// back into every MP3's ID3 tag (so the Zune desktop syncs it to the device as the
/// artist GUID), and downloads biography + images into the data directory.
/// </summary>
internal static class UpdateCommand
{
    /// <summary>MusicBrainz search confidence below which we refuse to write an MBID.</summary>
    private const int MinMatchScore = 90;

    private static readonly HashSet<string> CompilationArtists = new(StringComparer.OrdinalIgnoreCase)
        { "Various Artists", "Various", "VA", "Various Composers", "Soundtrack", "Original Soundtrack", "Unknown Artist" };

    private static bool IsCompilation(string? artist) =>
        artist != null && CompilationArtists.Contains(artist.Trim());

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
        var written = 0;
        var reused = 0;
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
                // On compilations the album artist is a placeholder; the track performer
                // is the real artist, and it's the artist the device's card is about.
                var artist = tagFile.Tag.FirstAlbumArtist;
                if (string.IsNullOrWhiteSpace(artist) || IsCompilation(artist))
                    artist = tagFile.Tag.FirstPerformer;
                if (string.IsNullOrWhiteSpace(artist))
                {
                    artist = ArtistFromFilename(file);
                    Console.WriteLine($"  {Path.GetFileName(file)}: no artist tag, searching MusicBrainz for \"{artist}\"");
                }
                if (IsCompilation(artist))
                {
                    Console.Error.WriteLine($"  SKIP {Path.GetFileName(file)}: compilation placeholder artist (\"{artist}\")");
                    failures++;
                    continue;
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
                    if (match == null)
                        Console.Error.WriteLine($"    no MusicBrainz match for \"{artist}\"");
                    else if (match.Score < MinMatchScore)
                    {
                        // The top hit below this is usually a different artist, and a wrong
                        // MBID gets written into the user's files - refuse rather than guess.
                        Console.Error.WriteLine($"    low-confidence match for \"{artist}\": {match.Name} " +
                                                $"(score {match.Score}) - skipped, tag it manually if that's right");
                        match = null;
                    }
                    else
                        Console.WriteLine($"    -> {match.Name} ({match.Mbid}, score {match.Score})");
                    resolved[artist] = match;
                }

                if (match == null)
                {
                    failures++;
                    continue;
                }

                if (tagFile.Tag.MusicBrainzArtistId != match.Mbid)
                {
                    try
                    {
                        tagFile.Tag.MusicBrainzArtistId = match.Mbid;
                        tagFile.Save();
                        Console.WriteLine($"  {Path.GetFileName(file)}: wrote MBID to ID3 tag");
                    }
                    catch (Exception e)
                    {
                        // Read-only, or held open by the Zune desktop software.
                        Console.Error.WriteLine($"  SKIP {Path.GetFileName(file)}: cannot write tag ({e.Message})");
                        failures++;
                        continue;
                    }
                }

                if (fetchedMbids.Add(match.Mbid))
                {
                    // tracks.xml is written last, so its presence means a previous run
                    // finished this artist - skip the ~3 API calls and re-download.
                    if (File.Exists(Path.Combine(dataDir, match.Mbid.ToLowerInvariant(), "tracks.xml")))
                    {
                        Console.WriteLine($"  {match.Name}: artist data already present, skipping");
                        reused++;
                        continue;
                    }

                    Console.WriteLine($"  Fetching artist data for {match.Name}...");
                    try
                    {
                        await fetcher.FetchAsync(dataDir, match.Mbid, match.Name, match.SortName);
                        written++;
                    }
                    catch (Exception e)
                    {
                        // A transient MusicBrainz/Wikipedia error shouldn't end the run.
                        Console.Error.WriteLine($"    fetch failed for {match.Name}: {e.Message}");
                        failures++;
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {written} artist(s) written to {dataDir}" +
                          (reused > 0 ? $", {reused} already present" : "") +
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
