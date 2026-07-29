---
name: zune-catalog-reference
description: Domain reference for the Zune catalog protocol as zunemd implements it — MBIDs as artist GUIDs, catalog.zune.net URL and Atom XML shapes, the image-UUID scheme, device network-state tuples, MTPZ, PPP-over-USB, endpoint types, and the metadata data sources (MusicBrainz, Wikidata, Wikipedia, Deezer). Read when interpreting device requests or state output, editing XML templates, debugging a blank artist card, or adding a data source. Keywords: MBID, catalog.zune.net, Atom feed, artwork id, progress phase status, MTPZ, deviceBackgroundImage.
---

# Zune catalog / metadata domain reference

What a mid-level engineer needs to know about the (dead) Zune.net catalog protocol as
this repo recreates it. **Not this skill:** build/run steps → `zunemd-runbook`; edit
gating → `zunemd-change-control`; why the code is structured this way →
`zunemd-architecture-contract`.

## Glossary

| Term | Meaning here |
|---|---|
| **MBID** | MusicBrainz artist ID, a GUID. Doubles as the Zune "artist GUID": written into each MP3's `MusicBrainz Artist Id` ID3 tag by `update`; the Zune desktop app syncs it to the device; the device then uses it in its catalog URLs. This identity chain is the whole trick that makes the recreation work. |
| **MTPZ** | Microsoft's encrypted MTP variant. Requires the well-known leaked key blob at `~/.mtpz-data` (or `--mtpz PATH`). Never distributed with this repo. |
| **PPP over USB** | During `serve`, the device negotiates a PPP link over the USB MTP connection, gets DNS that resolves `catalog.zune.net` to the host, and speaks plain HTTP inside that link. No real network is involved; the `server_ip` handed out (hardcoded `192.168.0.30` in ServeCommand.cs) is arbitrary — all traffic stays inside the PPP link. |
| **Interceptor** | The vendored library's HTTP handler that answers the device's catalog requests. zunemd runs it in **Static** mode: every URL is resolved to a local file via a C# callback; no upstream server. (Proxy/Hybrid modes exist in the API but need a live catalog server.) |
| **XuneArtistImagesServer** | The community Zune.net recreation whose XML shapes real ZuneHD firmware is known to accept. ZuneXml.cs templates are copied from it — it is the reference implementation when in doubt. |

## URLs the device requests

All of the form `/v3.0/en-US/music/artist/{mbid}/<endpoint>`. The native handler
(vendor `MetadataRequestHandler.cpp`) parses them and invokes the C# resolver with an
**endpoint type string** — exact strings, verified against both sides:

| Endpoint string | Served file (in `artist_data/{mbid}/`) | Content |
|---|---|---|
| `overview` | overview.xml | Atom entry: artist name, sort name, thumbnail image id |
| `biography` | biography.xml | Atom entry, bio text in `<a:content type="html">` |
| `images` | images.xml | Atom feed listing gallery image ids |
| `artwork` | `{N}.jpg` / thumb.jpg (by resource id, below) | JPEG bytes |
| `devicebackgroundimage` | 1.jpg | Full-screen background JPEG |
| `albums` / `similarartists` / `tracks` | albums.xml / similarArtists.xml / tracks.xml | Empty Atom feed stubs (device is satisfied with empty) |

Resolver contract: return a file path → 200 with the file; return null → 404 (device
shows the card without that element; not fatal).

## Image-id scheme

Deterministic UUIDs, `ZuneXml.ImageUuid`: `{first 8 hex of mbid}-{NNNN}-0000-0000-000000000000`
where `NNNN` is `0010` for the thumbnail and `0001`–`0006` for gallery images.
`serve` maps an artwork request back by splitting the id on `-`: part 0 finds the
artist folder by MBID prefix, part 1 parses as the image number (10 → thumb.jpg,
N → N.jpg). Folder names must be **lowercase** MBIDs — the resolver lowercases the
device's UUID before building paths.

## Device network-state tuple

`zune_device_read_network_state` returns 4 ints, printed by `serve` as
`active/progress/phase/status`. Semantics (from the vendored API header):

| Tuple | Meaning |
|---|---|
| 1, 0, 0, 0 | Idle — no networking activity |
| 1, 0, 1, 1 | Device has pending work (fetching metadata) |
| 1, 0, 3, 2 | Done with current batch / HD steady-state |
| 1, 100, 4, 3 | **Definitive completion** — safe to disconnect |

## Data sources (`update` pipeline)

1. **MusicBrainz** search (`/ws/2/artist?query=artist:"NAME"&limit=1`) → best match
   MBID. Global etiquette rules apply — see `zunemd-change-control` rule 4.
2. **Biography**: MusicBrainz url-relations → prefer the `wikipedia` relation if it is
   an en.wikipedia.org link; else `wikidata` relation → Wikidata EntityData JSON →
   `enwiki` sitelink → Wikipedia REST `page/summary` extract. Fallback: placeholder
   text "{Artist}. No biography available."
3. **Images** (keyless APIs, JPEG only):
   - Wikipedia summary `originalimage` (only if `.jpg/.jpeg`) — usually the largest,
     preferred for the background (1.jpg).
   - Deezer `search/artist` → `picture_xl` — square, preferred for thumb.jpg.
     Deezer's placeholder lives under `/images/artist//` and is filtered out.
   - Downloads under 1 KB are rejected as junk (`ArtistDataFetcher.DownloadAsync`).

## Hard limits and device quirks

- **10 MB per file**: the interceptor refuses larger files
  (`MAX_FILE_SIZE`, vendor `MetadataRequestHandler.cpp:343`).
- **Progressive JPEG suspicion**: Wikipedia originals are often progressive; the HD
  firmware is *believed* to accept them (UNVERIFIED on device). If a background stays
  blank, swap in a baseline JPEG named 1.jpg and re-run `serve`. The runbook's
  `check-artist-data.sh` flags progressive encoding.
- **Zune HD only**: `serve` requires network-mode hardware; other Zune families fail
  the `zune_device_supports_network_mode` check.
- If the device seems stuck, reboot it (hold power + media button) — `serve` is
  read-only from the device's perspective.

## Provenance and maintenance

Verified 2026-07-06 against the working tree (no git history exists).

- Endpoint strings: `grep -n '"overview"\|"devicebackgroundimage"' vendor/XuneSyncLibrary/lib/src/protocols/http/MetadataRequestHandler.cpp ServeCommand.cs`
- State tuples: `grep -n -A8 "Key values" vendor/XuneSyncLibrary/include/xune_sync/xune_sync_api.h`
- Image-id scheme: `grep -n "ImageUuid" ZuneXml.cs ServeCommand.cs`
- 10 MB limit: `grep -rn "MAX_FILE_SIZE" vendor/XuneSyncLibrary/lib/src/protocols/http/MetadataRequestHandler.cpp`
- Data-source URLs: `grep -n "https://" MusicBrainzClient.cs ArtistDataFetcher.cs`
