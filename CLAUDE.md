<!-- Durable, project-specific knowledge for the agent. Auto-loaded every session.
     Maintenance: permanent facts only (ephemeral state -> .ai/handoff.md); add gotchas as
     you hit them and never delete them (compress instead); update "Recent context" at
     session end. Soft cap ~200-250 lines, ideally far less.
     Read order at session start: this file -> .ai/rules.md -> .ai/current-task.md
     -> .ai/handoff.md -> .ai/backlog.md. -->

# zunehd-artist-updater (zunemd)

## What this project is
A .NET 8 CLI (`zunemd`) that restores the ZuneHD artist-card experience (biography +
background images) by replacing the dead Zune.net backend. `update` tags MP3s with
MusicBrainz IDs and downloads artist data; `serve` answers the device's catalog
requests over USB via the vendored XuneSyncLibrary. Detailed, verified skills live in
`.claude/skills/` — read those before editing. Read `.ai/` for live state.

## Stack
- .NET 8, C# (single project, no subdirs). One NuGet dep: TagLibSharp (ID3 tags).
- Vendored native C++ lib `XuneSyncLibrary` (P/Invoked as `xune_sync`), built via CMake;
  macOS IOKit / Windows WinUSB / Linux libusb backends. Needs `.mtpz-data` (leaked MTPZ
  key blob, not in repo).
- External keyless HTTP APIs: MusicBrainz WS/2, Wikidata, Wikipedia REST, Deezer.

## Architecture (mental model)
Two commands that never run together; `artist_data/{mbid}/` is the only interface
between them:
```
update: MP3s ──MusicBrainz──> MBIDs ──> write MBID to ID3 tag (device gets it via music sync)
                                    └──> artist_data/{mbid}/ (Atom XML + JPEGs)
serve:  artist_data/{mbid}/ ──path-resolver callback──> libxune_sync ──USB/PPP──> ZuneHD
```
Full detail: `.claude/skills/zunemd-architecture-contract` and `zune-catalog-reference`.

## Phase / status
macOS path working. Windows support wired up 2026-07-02 (WinUSB, ucrtbase-malloc for
the resolver callback, driver-swap docs) but the **first real-device Windows `serve`
run is still pending** — that shakedown is the current task (`.ai/current-task.md`,
campaign skill). No tests, no CI. Now a git repo, published to
github.com/alitahir6001/zune-artist-updater (public); the LGPL native lib is forked to
github.com/alitahir6001/XuneSyncLibrary, pinned at c2a6eeb, and cloned into `vendor/`
(gitignored) per the README.

## Active gotchas
<!-- Numbered, append-only. Never delete an entry — compress it instead if this grows long. -->
1. **Windows resolver-callback strings must use ucrtbase `malloc` (`AllocFreeableUtf8`), never `Marshal.StringToCoTaskMemUTF8`.** Native side calls C `free()`; CoTaskMem is the COM heap on Windows → corruption. Corollary: native lib must keep the dynamic CRT (`/MD`); a static-CRT build crashes in `free()`.
2. **Native callback delegates must stay reachable** for the interceptor's lifetime — native stores raw fn pointers. Kept as `static readonly` fields in ServeCommand.cs. GC of a delegate = call into freed memory.
3. **`artist_data/{mbid}/` folder names must be lowercase MBIDs**; serve lowercases the device's UUID before path lookup. Mismatch = 404 on device.
4. **First-hit MusicBrainz match** (`limit=1`, no score check) can silently tag the WRONG MBID into the user's MP3 files. The per-file guard prevents rewrites, not wrong writes.
5. **`update` rewrites the user's MP3 ID3 tags** — back up the music folder before the first run; keep that warning in docs.
6. **XML templates + data layout are copied from XuneArtistImagesServer** and are load-bearing (element order, namespaces, `urn:uuid:` forms, `{mbid[:8]}-NNNN-...` image ids). Can't be validated off-device — never restructure on reasoning alone.

## Conventions / what NOT to do
- **Read `.claude/skills/` first.** `zunemd-change-control` gates changes; don't route around it. Don't duplicate facts the skills already own.
- **Git: don't push or do outward-facing git actions without asking.** The repo is now
  public (published 2026-07-12); local commits are fine when the user asks, but pushing,
  force-pushing, and rewriting pushed history are the user's call.
- **Don't edit `vendor/XuneSyncLibrary/`** casually (vendored + gitignored); don't "fix" its non-compiling test executables (tests are OFF by design).
- **Don't reduce MusicBrainz request spacing** (1.1s) or drop the User-Agent → IP ban.
- **Don't commit/distribute `.mtpz-data`.** No firmware code paths (device-safety promise).
- **Device-affecting changes need real-device proof**, not just a green `dotnet build`.

## Recent context (last few sessions)
<!-- Small/single-purpose projects can skip this — .ai/backlog.md already covers it. -->
- **2026-07-12 (session 2):** Built the `.claude/skills/` library (6 skills + 2 verified diagnostic scripts) and scaffolded `.ai/` + this file. No source changes. Windows first-device `serve` run remains the open task.
- **2026-07-02 (session 1):** Wired up Windows support (WinUSB, ucrtbase-malloc callback, DLL probing, driver-swap docs in WINDOWS.md). Full PPP-over-USB serve path on Windows untested.
