using System.Text.Json;

namespace ZuneHdMetadataUpdater;

/// <summary>
/// Fetches a biography and images for an artist and writes the on-disk layout
/// the serve command maps device requests onto:
///   {data-dir}/{mbid}/overview.xml, biography.xml, images.xml, albums.xml,
///   similarArtists.xml, tracks.xml, thumb.jpg, 1.jpg (background + gallery), 2.jpg...
///
/// Biography: MusicBrainz url-rels -> Wikidata sitelink -> Wikipedia summary extract.
/// Images: Wikipedia page image + Deezer artist picture (both keyless, JPEG only).
/// </summary>
internal sealed class ArtistDataFetcher
{
    private readonly HttpClient _http;
    private readonly MusicBrainzClient _musicBrainz;

    public ArtistDataFetcher(HttpClient http, MusicBrainzClient musicBrainz)
    {
        _http = http;
        _musicBrainz = musicBrainz;
    }

    public async Task FetchAsync(string dataDir, string mbid, string artistName, string sortName)
    {
        var dir = Path.Combine(dataDir, mbid.ToLowerInvariant());
        Directory.CreateDirectory(dir);

        var rels = await _musicBrainz.GetUrlRelationsAsync(mbid);
        var wikiTitle = await ResolveWikipediaTitleAsync(rels);

        string bio = "";
        string? wikiImageUrl = null;
        if (wikiTitle != null)
        {
            (bio, wikiImageUrl) = await GetWikipediaSummaryAsync(wikiTitle);
            if (bio.Length > 0)
                Console.WriteLine($"    biography: Wikipedia ({wikiTitle})");
        }
        if (bio.Length == 0)
        {
            bio = $"{artistName}. No biography available.";
            Console.WriteLine("    biography: not found, using placeholder");
        }

        var deezerImageUrl = await GetDeezerImageAsync(artistName);

        // thumb: prefer the square Deezer portrait; background/gallery 1.jpg: prefer
        // the (usually larger) Wikipedia image, fall back to whichever exists.
        var thumbUrl = deezerImageUrl ?? wikiImageUrl;
        var backgroundUrl = wikiImageUrl ?? deezerImageUrl;

        var galleryNums = new List<int>();
        foreach (var url in new[] { backgroundUrl, deezerImageUrl }.Where(u => u != null).Distinct())
        {
            if (await DownloadAsync(url!, Path.Combine(dir, "1.jpg")))
            {
                galleryNums.Add(1);
                Console.WriteLine($"    background: {new Uri(url!).Host}");
                break;
            }
        }
        if (thumbUrl != null && await DownloadAsync(thumbUrl, Path.Combine(dir, "thumb.jpg")))
            Console.WriteLine($"    thumbnail:  {new Uri(thumbUrl).Host}");
        if (deezerImageUrl != null && wikiImageUrl != null && deezerImageUrl != wikiImageUrl
            && await DownloadAsync(deezerImageUrl, Path.Combine(dir, "2.jpg")))
            galleryNums.Add(2);

        if (galleryNums.Count == 0)
            Console.WriteLine("    WARNING: no images found - artist card will have no background");

        File.WriteAllText(Path.Combine(dir, "overview.xml"), ZuneXml.Overview(mbid, artistName, sortName));
        File.WriteAllText(Path.Combine(dir, "biography.xml"), ZuneXml.Biography(mbid, artistName, bio));
        File.WriteAllText(Path.Combine(dir, "images.xml"), ZuneXml.Images(mbid, galleryNums));
        File.WriteAllText(Path.Combine(dir, "albums.xml"), ZuneXml.EmptyFeed(mbid, "albums"));
        File.WriteAllText(Path.Combine(dir, "similarArtists.xml"), ZuneXml.EmptyFeed(mbid, "similarArtists"));
        File.WriteAllText(Path.Combine(dir, "tracks.xml"), ZuneXml.EmptyFeed(mbid, "tracks"));
    }

    private async Task<string?> ResolveWikipediaTitleAsync(Dictionary<string, string> rels)
    {
        if (rels.TryGetValue("wikipedia", out var wikiUrl) && wikiUrl.Contains("en.wikipedia.org/wiki/"))
            return Uri.UnescapeDataString(wikiUrl.Split("/wiki/")[1]);

        if (!rels.TryGetValue("wikidata", out var wikidataUrl))
            return null;
        var qid = wikidataUrl.TrimEnd('/').Split('/')[^1];
        try
        {
            using var doc = await GetJsonAsync($"https://www.wikidata.org/wiki/Special:EntityData/{qid}.json");
            return doc.RootElement.GetProperty("entities").GetProperty(qid)
                .GetProperty("sitelinks").GetProperty("enwiki").GetProperty("title").GetString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<(string Bio, string? ImageUrl)> GetWikipediaSummaryAsync(string title)
    {
        try
        {
            using var doc = await GetJsonAsync(
                $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}");
            var bio = doc.RootElement.TryGetProperty("extract", out var e) ? e.GetString() ?? "" : "";
            string? image = null;
            if (doc.RootElement.TryGetProperty("originalimage", out var oi))
            {
                var src = oi.GetProperty("source").GetString();
                if (src != null && (src.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                    || src.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                    image = src;
            }
            return (bio, image);
        }
        catch
        {
            return ("", null);
        }
    }

    private async Task<string?> GetDeezerImageAsync(string artistName)
    {
        try
        {
            using var doc = await GetJsonAsync(
                $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString('"' + artistName + '"')}&limit=1");
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0)
                return null;
            var url = data[0].GetProperty("picture_xl").GetString();
            // Deezer's placeholder image lives under /images/artist//
            return string.IsNullOrEmpty(url) || url.Contains("artist//") ? null : url;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> DownloadAsync(string url, string path)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            if (bytes.Length < 1024)
                return false;
            await File.WriteAllBytesAsync(path, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", MusicBrainzClient.UserAgent);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }
}
