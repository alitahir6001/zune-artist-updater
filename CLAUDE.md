<!-- Durable project knowledge, auto-loaded every session. Permanent facts only: live state -> .ai/handoff.md, session history -> .ai/history.md. Gotchas are append-only (compress, never delete). Edit only when the user asks. -->

# zunehd-artist-updater (zunemd)

## What this project is
A .NET 8 CLI (`zunemd`) that restores the ZuneHD artist-card experience (biography +
background images) by replacing the dead Zune.net backend. `update` tags MP3s with
MusicBrainz IDs and downloads artist data; `serve` answers the device's catalog
requests over USB via the vendored XuneSyncLibrary; `artists` lists the device's artists
and the ID it holds for each (read-only). Detailed, verified skills live in
`.claude/skills/` — read those before editing. Live state: `.ai/handoff.md`.
Design decisions: docs/adr/ (index: docs/adr/README.md). Write new ones with the write-adr skill.

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
**No `serve` run has ever been device-verified, on any platform.** ("macOS path working"
was asserted here through 2026-08-15 with nothing behind it; the runbook's provenance line
covers grep-checked facts, not a device run, and README still hedges image acceptance with
"is believed to".) The `update` path *is* verified — exercised end-to-end on macOS
2026-08-15 against real MP3s. So the open risk is two-layered: Windows USB plumbing on top
of an XML/protocol layer nobody has confirmed a device accepts. Windows support wired up
2026-07-02 (WinUSB, ucrtbase-malloc for the resolver callback, driver-swap docs); the
shakedown is the active task (`.ai/handoff.md`, campaign skill). No tests, no CI. Now a git repo, published to
github.com/alitahir6001/zune-artist-updater (public); the LGPL native lib is forked to
github.com/alitahir6001/XuneSyncLibrary, pinned at c2a6eeb, and cloned into `vendor/`
(gitignored) per the README. The P/Invoke boundary was fully audited against the vendored
headers on 2026-08-15 and matches exactly — treat it as verified, not suspect.

## Active gotchas
<!-- Numbered, append-only. Never delete an entry — compress it instead if this grows long. -->
1. **Windows resolver-callback strings must use ucrtbase `malloc` (`AllocFreeableUtf8`), never `Marshal.StringToCoTaskMemUTF8`.** Native side calls C `free()`; CoTaskMem is the COM heap on Windows → corruption. Corollary: native lib must keep the dynamic *release* CRT (`/MD`). A static-CRT build crashes in `free()`; a **Debug** build implies `/MDd`, whose `free()` uses `ucrtbased.dll` while C# allocates from `ucrtbase.dll` — same corruption, different cause. Always `cmake --build --config Release`. Why: ADR-0005.
2. **Native callback delegates must stay reachable** for the interceptor's lifetime — native stores raw fn pointers. Kept as `static readonly` fields in ServeCommand.cs. GC of a delegate = call into freed memory.
3. **`artist_data/{mbid}/` folder names must be lowercase MBIDs**; serve lowercases the device's UUID before path lookup. Mismatch = 404 on device.
4. **MusicBrainz match quality is the main corruption risk** — `limit=1` takes the top hit, so `update` now refuses below `MinMatchScore` (90) and falls back to the track performer when the album artist is a compilation placeholder. Loosening either re-opens silently writing a WRONG MBID into the user's files; the per-file guard prevents rewrites, not wrong writes.
5. **`update` rewrites the user's MP3 ID3 tags** — back up the music folder before the first run; keep that warning in docs. *Measured 2026-08-15:* the TagLibSharp write is surgical — one TXXX frame added into existing padding, ID3v2.3 not upgraded, art/all other frames byte-identical, audio MD5 and file size unchanged. Metadata *loss* is not the risk here; wrong MBIDs (gotcha 4) are.
6. **XML templates + data layout are copied from XuneArtistImagesServer** and are load-bearing (element order, namespaces, `urn:uuid:` forms, `{mbid[:8]}-NNNN-...` image ids). Can't be validated off-device — never restructure on reasoning alone.
7. **`zune_get_last_error()` is dead on the serve path.** The library assigns it in exactly one place (`zune_mtp_send_object`), which `serve` never calls, and returns `.c_str()` on an empty `std::string` — non-null, so a null check won't catch it. **All serve failure detail comes from the log callback, i.e. `--verbose`.** Debug Windows runs with `--verbose` from the first attempt, never as a fallback.
8. **Nothing may throw out of `ResolvePath`** (ServeCommand.cs) — it's a reverse P/Invoke callback and the runtime can't unwind through native frames, so an escaped exception kills the process. It's wrapped in try/catch returning `IntPtr.Zero` (= 404, the correct fallback); keep it that way, including the guard on its own logging call.
9. **`ZuneXml.Images` hardcodes `<width>1000</width><height>1000</height>` for every image**, but the actual file's size varies by source. It's true only by coincidence when the image came from Deezer (`picture_xl` is always 1000×1000) and wrong whenever Wikipedia supplied it. **A second candidate explanation for the blank-background caveat README blames on progressive JPEGs** — untested either way. Contract-bound (gotcha 6): needs device proof, not a reasoned fix.
10. **MusicBrainz 503s are routine, not a bug** — two hit during ~10 min of testing on 2026-08-15 with the rate budget nearly untouched (1177/1200). `update` survives them and reports accurately; re-run to fill gaps, which is cheap now that it resumes (skips any artist whose `tracks.xml` exists). Don't "fix" this by reducing request spacing.
11. **The artist-ID chain is unproven, and it gates everything.** The device only requests a card for an artist it holds an ID for (MTP prop 0xDA97, set by whatever synced the track). Nothing shows the Zune desktop app copies the `MusicBrainz Artist Id` tag there; Xune's uploader does read it. The user's Zune already shows cards for most artists (user, 2026-10-13), so it likely holds old Zune-catalog IDs — unverified. Run `artists` before `update`, and scope `update` to artists missing a card.

## Conventions / what NOT to do
- **Read `.claude/skills/` first.** `zunemd-change-control` gates changes; don't route around it. Don't duplicate facts the skills already own.
- **Git: don't push or do outward-facing git actions without asking.** The repo is now
  public (published 2026-07-12); local commits are fine when the user asks, but pushing,
  force-pushing, and rewriting pushed history are the user's call.
- **Don't edit `vendor/XuneSyncLibrary/`** casually (vendored + gitignored); don't "fix" its non-compiling test executables (tests are OFF by design).
- **Don't reduce MusicBrainz request spacing** (1.1s) or drop the User-Agent → IP ban.
- **Don't commit/distribute `.mtpz-data`.** No firmware code paths (device-safety promise).
- **Device-affecting changes need real-device proof**, not just a green `dotnet build`.
