using System.Text.Json;

namespace ZuneHdMetadataUpdater;

/// <summary>MusicBrainz WS/2 client. Global 1 req/sec limit, mandatory User-Agent.</summary>
internal sealed class MusicBrainzClient
{
    public const string UserAgent = "ZuneHdArtistMetadataUpdater/0.1 ( publicjunkfolder@gmail.com )";

    private readonly HttpClient _http;
    private DateTime _lastRequest = DateTime.MinValue;

    public MusicBrainzClient(HttpClient http) => _http = http;

    public record ArtistMatch(string Mbid, string Name, string SortName);

    public async Task<ArtistMatch?> SearchArtistAsync(string name)
    {
        var url = $"https://musicbrainz.org/ws/2/artist?query=artist:{Uri.EscapeDataString('"' + name + '"')}&limit=1&fmt=json";
        using var doc = await GetJsonAsync(url);
        var artists = doc.RootElement.GetProperty("artists");
        if (artists.GetArrayLength() == 0)
            return null;
        var a = artists[0];
        return new ArtistMatch(
            a.GetProperty("id").GetString()!,
            a.GetProperty("name").GetString()!,
            a.TryGetProperty("sort-name", out var sn) ? sn.GetString()! : a.GetProperty("name").GetString()!);
    }

    /// <summary>Returns url-relation targets keyed by relation type (wikidata, wikipedia, ...).</summary>
    public async Task<Dictionary<string, string>> GetUrlRelationsAsync(string mbid)
    {
        var rels = new Dictionary<string, string>();
        using var doc = await GetJsonAsync($"https://musicbrainz.org/ws/2/artist/{mbid}?inc=url-rels&fmt=json");
        if (!doc.RootElement.TryGetProperty("relations", out var relations))
            return rels;
        foreach (var rel in relations.EnumerateArray())
        {
            var type = rel.GetProperty("type").GetString()!;
            var target = rel.GetProperty("url").GetProperty("resource").GetString()!;
            rels.TryAdd(type, target);
        }
        return rels;
    }

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        var wait = _lastRequest.AddSeconds(1.1) - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait);
        _lastRequest = DateTime.UtcNow;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }
}
