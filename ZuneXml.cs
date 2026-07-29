using System.Security;

namespace ZuneHdMetadataUpdater;

/// <summary>
/// Zune catalog XML (Atom) documents in the exact shape the Zune HD firmware expects.
/// Templates match the XuneArtistImagesServer Zune.net recreation.
/// </summary>
internal static class ZuneXml
{
    /// <summary>Deterministic image UUID: first 8 chars of the MBID + image number.
    /// Image 10 is the thumbnail, 1-6 are gallery images.</summary>
    public static string ImageUuid(string mbid, int imageNum) =>
        $"{mbid[..8]}-{imageNum:D4}-0000-0000-000000000000";

    public static string Overview(string mbid, string artistName, string sortName) =>
        $"""<?xml version="1.0" encoding="utf-8"?><a:entry xmlns:a="http://www.w3.org/2005/Atom" xmlns:os="http://a9.com/-/spec/opensearch/1.1/" xmlns="http://schemas.zune.net/catalog/music/2007/10"><a:link rel="zune://artist/biography" type="application/atom+xml" href="/v3.0/en-US/music/artist/{mbid}/biography" /><a:link rel="self" type="application/atom+xml" href="/v3.0/en-US/music/artist/{mbid}" /><a:updated>1900-01-01T00:00:00.000000Z</a:updated><a:title type="text">{Esc(artistName)}</a:title><a:id>urn:uuid:{mbid}</a:id><sortName>{Esc(sortName)}</sortName><playRank>0</playRank><playCount>0</playCount><favoriteCount>0</favoriteCount><sendCount>0</sendCount><isDisabled>False</isDisabled><startDate>1900-01-01T00:00:00Z</startDate><image><id>urn:uuid:{ImageUuid(mbid, 10)}</id></image><a:author><a:name>Microsoft Corporation</a:name></a:author></a:entry>""";

    public static string Biography(string mbid, string artistName, string bioText) =>
        $"""<?xml version="1.0" encoding="utf-8"?><a:entry xmlns:a="http://www.w3.org/2005/Atom" xmlns:os="http://a9.com/-/spec/opensearch/1.1/" xmlns="http://schemas.zune.net/catalog/music/2007/10"><a:link rel="self" type="application/atom+xml" href="/v3.0/en-US/music/artist/{mbid}/biography" /><a:updated>1900-01-01T00:00:00.0000000Z</a:updated><a:title type="text">{Esc(artistName)}</a:title><a:id>tag:catalog.zune.net,1900-01-01:/music/artist/{mbid}/biography</a:id><a:content type="html">{Esc(bioText)}</a:content><a:author><a:name>Microsoft Corporation</a:name></a:author></a:entry>""";

    public static string Images(string mbid, IEnumerable<int> galleryNums)
    {
        var entries = string.Join("", galleryNums.Select(n =>
        {
            var id = ImageUuid(mbid, n);
            return $"""<a:entry><a:updated>1900-01-01T00:00:00.000000Z</a:updated><a:title type="text">List Of Items</a:title><a:id>urn:uuid:{id}</a:id><instances><imageInstance><id>urn:uuid:{id}</id><url>http://art.zune.net/1/{id}/504/image.jpg</url><format>jpg</format><width>1000</width><height>1000</height></imageInstance></instances></a:entry>""";
        }));
        return $"""<?xml version="1.0" encoding="utf-8"?><a:feed xmlns:a="http://www.w3.org/2005/Atom" xmlns:os="http://a9.com/-/spec/opensearch/1.1/" xmlns="http://schemas.zune.net/catalog/music/2007/10"><a:link rel="self" type="application/atom+xml" href="/v3.0/en-US/music/artist/{mbid}/images" /><a:updated>1900-01-01T00:00:00.000000Z</a:updated><a:title type="text">List Of Items</a:title><a:id>tag:catalog.zune.net,1966-09-20:/music/artist/{mbid}/images</a:id>{entries}<a:author><a:name>Microsoft Corporation</a:name></a:author></a:feed>""";
    }

    /// <summary>Empty Atom feed for the albums / similarArtists / tracks stub endpoints.</summary>
    public static string EmptyFeed(string mbid, string endpoint) =>
        $"""<?xml version="1.0" encoding="utf-8"?><a:feed xmlns:a="http://www.w3.org/2005/Atom" xmlns:os="http://a9.com/-/spec/opensearch/1.1/" xmlns="http://schemas.zune.net/catalog/music/2007/10"><a:link rel="self" type="application/atom+xml" href="/v3.0/en-US/music/artist/{mbid}/{endpoint}" /><a:updated>1900-01-01T00:00:00.000000Z</a:updated><a:title type="text">List Of Items</a:title><a:id>tag:catalog.zune.net,1900-01-01:/music/artist/{mbid}/{endpoint}</a:id><a:author><a:name>Microsoft Corporation</a:name></a:author></a:feed>""";

    private static string Esc(string s) => SecurityElement.Escape(s);
}
