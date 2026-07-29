---
name: zunemd-research-frontier
description: Open problems where zunemd could advance past its current state — real album/similar-artist/track feeds, image re-encoding to kill the blank-background caveat, smarter MusicBrainz matching, more audio formats, and on-demand (hybrid) metadata fetching. For each - why the current approach falls short, the project's specific asset, the first three concrete steps in this repo, and a falsifiable done-milestone. Read when the user asks what to build next, wants to extend features, or asks about albums, similar artists, image quality, m4a/wma support, or hybrid mode. Keywords: roadmap, next feature, enhancement, frontier.
---

# zunemd research frontier

Candidate work, ranked by value-for-effort. Nothing here is committed; everything
routes through `zunemd-change-control` (device-affecting items need real-device
evidence per the `zunemd-runbook` validation bar). **Not this skill:** the one
*committed* open problem — the Windows first-run — has its own executable campaign:
`zunemd-windows-shakedown-campaign`.

## 1. Baseline-JPEG re-encoding (kills the project's oldest caveat)

- **Falls short today:** Wikipedia originals are often progressive JPEGs at huge
  resolutions; the HD firmware's acceptance is UNVERIFIED, and the documented
  workaround is manual (hand-drop a baseline `1.jpg`).
- **Asset:** all images already flow through one choke point,
  `ArtistDataFetcher.DownloadAsync` — one place to add re-encoding. The runbook's
  `check-artist-data.sh` already detects progressive encoding, so before/after is
  measurable.
- **First three steps:** (1) add `SixLabors.ImageSharp` to the csproj; (2) in
  `DownloadAsync`, decode and re-encode to baseline JPEG, capping the long edge
  (device screen is 480×272 — try 1024 first, it's a display-scaling question);
  (3) run `update` on a test folder and confirm `check-artist-data.sh` reports all
  baseline.
- **Result when:** an artist whose background was blank on a real device shows a
  background after re-encoded data is served — and no previously-working artist
  regresses. (If backgrounds were never actually blank, the experiment instead
  *verifies* progressive acceptance — either outcome retires the caveat.)

## 2. Real albums / similarArtists / tracks feeds

- **Falls short today:** ZuneXml.EmptyFeed stubs mean the artist card has no
  discography or similar-artists content.
- **Asset:** MusicBrainzClient already does rate-limited WS/2 calls
  (`inc=release-groups` and the `artist?query=` endpoints are the same API); the
  XuneArtistImagesServer reference implementation shows the exact feed XML the
  firmware accepts; the serve-side routing for these endpoints already exists.
- **First three steps:** (1) fetch the album-feed shape from XuneArtistImagesServer's
  source and diff it against `ZuneXml.EmptyFeed`; (2) add
  `GetReleaseGroupsAsync(mbid)` to MusicBrainzClient.cs; (3) emit a populated
  `albums.xml` for one artist and serve it to the device.
- **Result when:** the device's artist card lists ≥1 album title that came from
  MusicBrainz. Falsifier: the device rejects or ignores the populated feed (then the
  finding is the exact rejected shape — also valuable).

## 3. Match confidence for MusicBrainz lookups

- **Falls short today:** first search hit wins (`limit=1`, no score check) — the
  known-weak point that can permanently tag the wrong MBID into a user's files
  (`zunemd-architecture-contract` § weak points).
- **Asset:** the WS/2 search response already includes a `score` field per artist;
  the resolution cache in UpdateCommand.cs is the single decision point.
- **First three steps:** (1) parse `score` in `SearchArtistAsync`; (2) below a
  threshold (start: 90), print the top 3 candidates and skip tagging instead of
  guessing; (3) add a `--yes`/`--min-score` flag if interactivity is unwanted.
- **Result when:** a deliberately ambiguous artist name (e.g. a one-word band name
  shared by two acts) produces a skip-with-candidates instead of a silent wrong tag,
  and a normal library run tags the same MBIDs as before.

## 4. Beyond MP3: .m4a/.wma scanning

- **Falls short today:** `*.mp3` glob only; Zune-supported AAC/WMA libraries get no
  metadata at all.
- **Asset:** TagLibSharp (already referenced) reads tags for both formats; the rest
  of the pipeline is format-agnostic (it only needs artist name + a writable tag).
- **First three steps:** (1) extend the enumeration in UpdateCommand.cs to
  `.mp3/.m4a/.wma`; (2) verify TagLibSharp round-trips `MusicBrainzArtistId` on each
  format (UNVERIFIED for WMA — its tag mapping may differ); (3) confirm the Zune
  desktop actually syncs that tag to the device for non-MP3 formats — this is the
  real unknown and needs a device test.
- **Result when:** an artist present only via .m4a files gets a working artist card
  on the device. Falsifier: the desktop app drops the tag for that container.

## 5. On-demand fetching (hybrid mode) — speculative

- **Falls short today:** everything must be pre-fetched by `update`; an artist synced
  after the last `update` run 404s.
- **Asset:** the vendored API already defines hybrid mode and a
  `zune_cache_storage_callback_t` (unbound; see xune_sync_api.h) — resolver miss →
  proxy fetch → cache to disk. zunemd's fetch logic in ArtistDataFetcher could serve
  as the "proxy" by fetching at request time.
- **First three steps:** (1) read the hybrid-mode plumbing in the vendored library to
  learn what the proxy actually targets (a real HTTP upstream? then a localhost
  bridge server is needed); (2) bind the cache-storage callback in XuneSyncNative.cs;
  (3) prototype: on resolver miss, synchronously run the ArtistDataFetcher path and
  return the fresh file.
- **Result when:** an artist with no pre-existing `artist_data` folder gets a
  populated card during a single `serve` session. Risk: request-time fetch latency vs
  the device's HTTP timeout — measure before building more.

## Provenance and maintenance

Assessed 2026-07-06 (no git history). Re-check before starting any item:

- Item 1 choke point: `grep -n "DownloadAsync" ArtistDataFetcher.cs`
- Item 2 stubs still stubs? `grep -n "EmptyFeed" ArtistDataFetcher.cs ZuneXml.cs`
- Item 3 still first-hit? `grep -n "limit=1\|score" MusicBrainzClient.cs`
- Item 4 still MP3-only? `grep -n '"\*.mp3"' UpdateCommand.cs`
- Item 5 callback still unbound? `grep -n "cache_storage" XuneSyncNative.cs vendor/XuneSyncLibrary/include/xune_sync/xune_sync_api.h`
