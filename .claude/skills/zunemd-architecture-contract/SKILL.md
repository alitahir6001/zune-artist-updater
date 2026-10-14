---
name: zunemd-architecture-contract
description: Load-bearing design decisions in zunemd and why they hold — the artist_data directory as the interface between update and serve, the P/Invoke boundary and its cross-platform memory contract, native library load order, delegate lifetimes, why Static interceptor mode, and the known-weak points. Read before refactoring, moving files, changing the data layout, adding platforms, or touching XuneSyncNative.cs/ServeCommand.cs. Keywords: architecture, P/Invoke, marshalling, DllImportResolver, memory contract, invariants, weak points.
---

# zunemd architecture contract

**Not this skill:** build/run/debug → `zunemd-runbook`; edit gates and incidents →
`zunemd-change-control`; protocol facts → `zune-catalog-reference`.

## Shape

Seven C# files, no project subdirectories, one NuGet dependency (TagLibSharp), one
vendored native dependency (XuneSyncLibrary, C++). Two commands that never run
together:

```
update: MP3s ──TagLibSharp──> artist names ──MusicBrainz──> MBIDs
        ├── writes MBID into each MP3's ID3 tag  (device gets it via music sync)
        └── writes artist_data/{mbid}/  (XML + JPEGs)

serve:  artist_data/{mbid}/ ──path-resolver callback──> libxune_sync ──USB──> ZuneHD
```

**Invariant 1 — `artist_data/` is the only interface between the commands.** `update`
never touches USB; `serve` never touches the network or the MP3s. The layout (see
`zune-catalog-reference`) is therefore a public contract: hand-crafted folders must
work (the README's blank-background workaround depends on it), so `serve` must never
require metadata that only `update` could have produced.

**Invariant 2 — the device's identity for an artist is the MBID in the ID3 tag.**
If the tag and the `artist_data` folder name disagree, the device 404s. Both sides are
written from the same `ArtistMatch` in one `update` pass to keep them in sync; folder
names are lowercased and matched lowercase.

## The P/Invoke boundary (XuneSyncNative.cs)

- Bindings are hand-written for **only** the functions `serve` and `artists` need; the header
  `vendor/XuneSyncLibrary/include/xune_sync/xune_sync_api.h` is the source of truth.
  The library also exposes upload/erase/firmware-adjacent APIs — deliberately unbound
  (see `zunemd-change-control` rule 8).
- **Memory contract (single home for this fact):** the path-resolver callback returns
  a C string that the native side copies and then `free()`s. Therefore the C# side
  must allocate with something `free()`-compatible **on the CRT heap the native
  library links against**:
  - macOS/Linux: `Marshal.StringToCoTaskMemUTF8` (CoTaskMem is malloc-backed there).
  - Windows: `ucrtbase.dll!malloc` via `AllocFreeableUtf8` — CoTaskMem is the COM
    heap on Windows and `free()` on it corrupts the heap. Corollary: the DLL must be
    built with the dynamic CRT (`/MD`) so both sides share ucrtbase.
  - Returning `IntPtr.Zero` = 404; nothing to free.
- **Delegate lifetime:** native stores raw function pointers; delegates live in
  `static readonly` fields in ServeCommand.cs so the GC can't collect them while the
  interceptor runs.
- **Library resolution:** a `DllImportResolver` probes, in order: `$XUNE_SYNC_LIB` →
  app base dir → CWD → `vendor/XuneSyncLibrary/build/` (CWD then base) →
  `.../build/Release/` (CWD then base; the Visual Studio multi-config generator puts
  the DLL there). Platform file name: `xune_sync.dll` / `libxune_sync.dylib` /
  `libxune_sync.so`. The csproj additionally copies the built library (and, on
  Windows, vcpkg's applocal runtime DLLs) next to the exe when present.
- Struct marshalling: `ArtistMetadataConfig` is `LayoutKind.Sequential` and must
  match the header's field order exactly; `bool` returns are `UnmanagedType.I1`.

## Deliberate design decisions

| Decision | Why |
|---|---|
| Interceptor in **Static** mode | Proxy/Hybrid need a live upstream catalog server; none exists. All content is pre-fetched by `update`. The header's cache-storage callback (hybrid) is unbound — candidate work, see `zunemd-research-frontier`. |
| Poll loop in C#, 100 ms native timeout | `zune_device_poll_network_data` is designed to be driven from C# for clean Ctrl+C cancellation — no native monitoring thread to tear down. |
| State printed max every 5 s, only on change | The state read is an extra MTP round-trip (Op922f); polling it every loop iteration would spam the device and the console. |
| `server_ip` hardcoded `192.168.0.30` | Arbitrary; DNS inside the PPP link hands it to the device and all traffic stays in-link. Not worth a flag. |
| MBID resolution cached per artist name (case-insensitive), failures cached too | One MusicBrainz query per distinct artist per run; a failed lookup isn't retried within the run (rate-limit etiquette). |
| `~/.mtpz-data` default resolved in **C#**, not native | The native fallback reads `$HOME`, which is normally unset on Windows; `Environment.SpecialFolder.UserProfile` works everywhere. |
| `InvariantGlobalization` in csproj | Trims ICU dependency; nothing here needs culture-specific behavior. |

## Known-weak points (stated plainly)

- **First-hit MusicBrainz match.** `limit=1`, no score threshold, no confirmation.
  Ambiguous names (e.g. bands sharing a name) can tag the wrong MBID into the user's
  files. The per-file guard only prevents rewrites, not wrong writes.
- **Filename guessing** (`Artist - Title.mp3`, leading track numbers stripped) is a
  heuristic; wrong splits produce garbage lookups.
- **MP3-only scan.** `*.mp3` glob; .m4a/.wma (which Zune supports) are ignored.
- **Artwork folder lookup by 8-char MBID prefix** (`ResolveImageFile`): two artists
  sharing the first 8 hex chars would collide. Astronomically unlikely per library,
  but it is prefix-match, not exact.
- **Keyless third-party APIs** (Wikipedia REST, Wikidata EntityData, Deezer) can
  change shape without notice; all failures degrade silently to placeholder bio /
  missing images by design (broad catches in ArtistDataFetcher.cs).
- **No resume/state for `update`**: re-running is the retry mechanism; it re-fetches
  nothing per file unless the MBID differs, but artist data is re-downloaded per run
  (fetchedMbids is in-memory only). Cheap for small libraries; wasteful for huge ones.
- **Windows serve path untested on hardware** as of 2026-07-06 — the campaign skill
  `zunemd-windows-shakedown-campaign` exists to close exactly this.

## Provenance and maintenance

Verified 2026-07-06 against the working tree (no git history exists).

- Memory contract: `grep -n "MEMORY CONTRACT" XuneSyncNative.cs vendor/XuneSyncLibrary/include/xune_sync/xune_sync_api.h`
- Probe order: `grep -n -A16 "SetDllImportResolver" XuneSyncNative.cs`
- Static-mode config: `grep -n "ArtistMetadataMode.Static\|server_ip" ServeCommand.cs`
- Weak points: `grep -n "limit=1" MusicBrainzClient.cs && grep -n '"\*.mp3"' UpdateCommand.cs`
