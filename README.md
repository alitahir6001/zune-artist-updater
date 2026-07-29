# ZuneHD Artist Metadata Updater

*A tool to help my ZuneHD 32 re-live its glory days with updated artist metadata. Long live Zune!*

Restores the ZuneHD artist card experience (biography + background images) by replacing
the dead Zune.net backend. A .NET 8 CLI (`zunemd`) scans your music, resolves each artist
on MusicBrainz, writes the MBID into the ID3 tags, downloads artist data, and then serves
it directly to the ZuneHD over USB using
[XuneSyncLibrary](https://github.com/xune-software/XuneSyncLibrary)'s PPP/TCP/HTTP-over-MTP
stack.

## Important platform note

Developed on macOS (IOKit backend). The vendored XuneSyncLibrary also builds on
**Windows** (WinUSB backend + libwdi driver helper) and Linux (libusb) — on Windows the
Zune's stock driver must be swapped for WinUSB while `serve` runs; see
[WINDOWS.md](WINDOWS.md) for the full clone/build/test walkthrough. The classic Zune
desktop app (Windows-only) is still the easiest way to sync the *music* itself — the
`serve` step is what this tool replaces.

## Prerequisites

- .NET 8 SDK (`brew install dotnet@8`)
- Native build deps: `brew install cmake openssl curl taglib`
- **MTPZ keys**: the Zune's MTP stack is encrypted (MTPZ). You need a `.mtpz-data` file
  (the well-known leaked MTPZ key blob, same one libmtp/AFTL use) at `~/.mtpz-data`,
  or pass `--mtpz PATH`. It is not distributed with this project.

## Build

The native `XuneSyncLibrary` is **not** included in this repo (it is LGPL-2.1 and lives
in its own project); clone it into `vendor/` at the pinned commit first. The clone below
points at a fork pinned for reproducibility — it is an unmodified copy of
[xune-software/XuneSyncLibrary](https://github.com/xune-software/XuneSyncLibrary) at
commit `c2a6eeb`.

```bash
# 0. Fetch the native library (once) at the commit this project is built against
git clone https://github.com/alitahir6001/XuneSyncLibrary.git vendor/XuneSyncLibrary
git -C vendor/XuneSyncLibrary checkout c2a6eeb

# 1. Native library (once)
cd vendor/XuneSyncLibrary
cmake -S . -B build && cmake --build build -j8   # -> build/libxune_sync.dylib
cd ../..

# 2. CLI
dotnet build
```

(Some of the library's bundled *test executables* fail to compile against its current
API; the library target `libxune_sync.dylib` builds fine, and that is all we need.)

## Usage

```bash
# 1. Scan music, tag MBIDs, download bios + images into ./artist_data
dotnet run -- update ~/Music

# 2. Sync the music to the ZuneHD (Zune desktop on Windows, or Xune)

# 3. With the ZuneHD plugged in over USB, serve the metadata
dotnet run -- serve            # add --verbose for the native library's log
```

During `serve`, the device negotiates PPP over USB, resolves `catalog.zune.net` to the
host, and requests `/v3.0/en-US/music/artist/{mbid}/biography`, `/images`,
`/deviceBackgroundImage`, etc. Each request is answered from `artist_data/` and logged.
When the state line shows `progress=100 phase=4 status=3` the device has finished
fetching; disconnect and open the artist on the device.

## How it works

- **update** — reads the artist from each MP3's ID3 tag (TagLibSharp); if the tag is
  missing, guesses from the filename (`Artist - Title.mp3`). Queries the MusicBrainz API
  (1 req/sec, proper User-Agent) for the artist MBID and writes it back to the file's
  `MusicBrainz Artist Id` tag — the artist GUID the device uses in its metadata URLs.
  The biography comes from the artist's MusicBrainz URL relationships
  (Wikidata → English Wikipedia summary); images come from Wikipedia's page image and
  Deezer's artist portrait (both keyless, JPEG only).
- **serve** — P/Invokes `libxune_sync`: connect USB → init HTTP subsystem → start the
  artist-metadata interceptor in static mode → trigger network mode → poll. The
  interceptor calls back into C# to resolve each requested URL to a file.

## Data layout

```
artist_data/{mbid}/
├── overview.xml        # Zune catalog Atom entry
├── biography.xml       # bio text (a:content type="html")
├── images.xml          # gallery list; image ids are {mbid[:8]}-NNNN-0000-...
├── albums.xml, similarArtists.xml, tracks.xml   # empty-feed stubs
├── 1.jpg               # background + first gallery image
├── 2.jpg               # second gallery image (when two sources found)
└── thumb.jpg           # square thumbnail (image id NNNN = 0010)
```

XML shapes match the community Zune.net recreation
([XuneArtistImagesServer](https://github.com/xune-software/XuneArtistImagesServer)),
which is what the ZuneHD firmware is known to accept.

## Known caveats

- Wikipedia originals are sometimes progressive JPEGs at large resolutions; the ZuneHD
  firmware is believed to accept them, but if a background stays blank on device, drop a
  baseline JPEG named `1.jpg` into the artist folder and re-run `serve`.
- The interceptor serves files up to 10 MB (library limit).
- `serve` requires a Zune **HD** (only family with the network-mode hardware).

## License & attribution

- The native [XuneSyncLibrary](https://github.com/xune-software/XuneSyncLibrary) is
  **LGPL-2.1** and is cloned separately (see Build) rather than redistributed here; it
  is dynamically linked (P/Invoke), so the LGPL relinking requirement is satisfied.
- The Zune catalog XML shapes are derived from the community
  [XuneArtistImagesServer](https://github.com/xune-software/XuneArtistImagesServer)
  recreation.
- `.mtpz-data` (the MTPZ key blob) is **not** distributed with this project and must be
  supplied by the user.
- A license for this project's own code has not been chosen yet.
