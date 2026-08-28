# ZuneHD Artist Metadata Updater

*Helping my ZuneHD 32 re-live its glory days. Long live Zune!*

When Zune.net went dark, the artist cards on the ZuneHD went blank with it — no biography,
no background photo, just a name. `zunemd` brings them back. It looks up every artist in
your library, pulls a bio and photos off the open web, and hands them to the device over
USB the same way Zune.net used to.

## It does not sync your music

Two different jobs, and this tool only does the second one.

| | Who does it |
|---|---|
| Getting songs onto the device | Zune desktop app (Windows) or Xune |
| Artist bios + background images | `zunemd` |

`zunemd` never writes to the device's music library. It does edit your *local* MP3s: it
stamps each file with the artist's MusicBrainz ID, which is how the ZuneHD knows which
artist card to ask for. **Back up your music folder before the first run.**

## What you need

- .NET 8 SDK — `brew install dotnet@8`
- Build tools for the native USB library — `brew install cmake openssl curl taglib`
- A `.mtpz-data` file at `~/.mtpz-data` (or `--mtpz PATH`). The Zune's USB protocol is
  encrypted, and this is the key blob it needs — the same well-known one libmtp uses. It
  is not shipped with this project.
- A Zune **HD**. Earlier Zunes lack the network hardware `serve` depends on.

Developed on macOS. Windows and Linux both work, but on Windows the Zune's stock driver
has to be temporarily swapped for WinUSB while `serve` runs — [WINDOWS.md](WINDOWS.md)
walks through the whole thing.

## Build

The USB code lives in a separate LGPL project, so it isn't checked in here. Clone it into
`vendor/` first — the URL below is a fork pinned at commit `c2a6eeb`, an unmodified copy
of [xune-software/XuneSyncLibrary](https://github.com/xune-software/XuneSyncLibrary), so
that everyone builds against the same known-good revision.

```bash
git clone https://github.com/alitahir6001/XuneSyncLibrary.git vendor/XuneSyncLibrary
git -C vendor/XuneSyncLibrary checkout c2a6eeb

cmake -S vendor/XuneSyncLibrary -B vendor/XuneSyncLibrary/build
cmake --build vendor/XuneSyncLibrary/build -j8

dotnet build
```

A few of that library's bundled test programs no longer compile against its own API. They
don't matter — `libxune_sync` itself builds, and that's the only piece used here.

## Usage

```bash
# 1. Scan your music, tag it, download bios and images into ./artist_data
dotnet run -- update ~/Music

# 2. Sync the music itself (Zune desktop or Xune) — not this tool

# 3. Plug in the ZuneHD and serve the metadata
dotnet run -- serve --verbose
```

While `serve` is running, the ZuneHD thinks it's talking to Zune.net: it asks for each
artist's biography and images over the USB cable, and `zunemd` answers from
`artist_data/`, logging every request. Once the state line reads
`progress=100 phase=4 status=3` the device has everything — unplug it and open an artist.

## How it works

**`update`** reads the artist from each MP3's tags (falling back to an
`Artist - Title.mp3` filename), asks MusicBrainz for that artist's ID, and writes the ID
back into the file. The biography comes from Wikipedia, by way of the artist's Wikidata
entry; photos come from Wikipedia and Deezer. All keyless, all rate-limited to be a good
citizen.

**`serve`** brings up the device's USB network link and stands in for `catalog.zune.net`,
resolving each URL the device requests to a file under `artist_data/`. The XML it serves
matches the community [XuneArtistImagesServer](https://github.com/xune-software/XuneArtistImagesServer)
recreation, which is the shape the ZuneHD firmware is known to accept.

## Known caveats

- The `serve` path hasn't been confirmed against a real device yet — `update` has.
- Wikipedia's photos are sometimes large progressive JPEGs. The firmware is believed to
  handle them, but if a background comes up blank, drop a baseline JPEG named `1.jpg` into
  that artist's folder and re-run `serve`.
- Images over 10 MB won't be sent (library limit).
