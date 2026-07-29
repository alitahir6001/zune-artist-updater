---
name: zunemd-change-control
description: How changes are classified and gated in the zunemd (ZuneHD artist metadata updater) repo — non-negotiable rules, their rationale, and the incidents behind them. Read BEFORE editing any file here, especially XuneSyncNative.cs, ZuneXml.cs, ServeCommand.cs, vendor/, or anything touching the P/Invoke boundary, XML templates, MusicBrainz calls, or the Windows build. Keywords: change policy, safe to edit, review gate, non-negotiables, vendor policy, CRT, rate limit.
---

# zunemd change control

zunemd is a two-command .NET 8 CLI (`update`, `serve`) plus a vendored native C++
library. There are **no tests and no CI**. The repo is public (published 2026-07-12 to
github.com/alitahir6001/zune-artist-updater), so history exists from that point, but the
only authority on correctness is still (a) the vendored library's header contracts and
(b) a real Zune HD device. That makes change discipline the whole safety net.

**Not this skill:** how to build/run/debug → `zunemd-runbook`. Why the design is the
way it is → `zunemd-architecture-contract`. Protocol/domain facts → `zune-catalog-reference`.

## Change classes

| Class | Files | Gate |
|---|---|---|
| **Free** | README.md, WINDOWS.md, `.claude/skills/**` | Edit freely; keep claims verified against code. |
| **Normal** | Program.cs, UpdateCommand.cs, MusicBrainzClient.cs, ArtistDataFetcher.cs | Build must pass (`dotnet build`); respect the API-etiquette rules below. |
| **Contract-bound** | ServeCommand.cs, XuneSyncNative.cs, ZuneXml.cs, ZuneHdMetadataUpdater.csproj | Every non-negotiable below applies; changes here can crash the native side or silently break the device — real-device verification is the only proof (see runbook validation bar). |
| **Vendor** | `vendor/XuneSyncLibrary/**` | Do not edit casually. It is a vendored copy of an external project AND it is gitignored — local edits are invisible to any future git history and will be lost on re-vendoring. If a change is unavoidable, document it in README.md the same day. |

## Non-negotiables

1. **Windows resolver-callback strings must come from `ucrtbase` malloc, never
   `Marshal.StringToCoTaskMemUTF8`.** The native side calls C `free()` on the pointer
   the C# path resolver returns. On macOS/Linux, CoTaskMem is malloc-backed so it
   works; on Windows, CoTaskMem is the COM heap and `free()` corrupts it.
   *Incident:* found 2026-07-02 while wiring up Windows support; `AllocFreeableUtf8`
   in XuneSyncNative.cs is the fix. Details of the contract: see
   `zunemd-architecture-contract` § memory contract.

2. **The native library must keep the dynamic CRT (`/MD`, the CMake default).**
   Same contract: rule 1 allocates on the ucrtbase heap, so the DLL must free on
   ucrtbase too. A static-CRT build crashes in `free()`. (WINDOWS.md documents this
   for builders.)

3. **Native callback delegates must stay reachable for the interceptor's lifetime.**
   The native side stores raw function pointers; if the GC collects the delegate, the
   next device request calls freed memory. ServeCommand.cs keeps them in `static
   readonly` fields (`ResolverDelegate`, `LogDelegate`) — keep any new callback the
   same way.

4. **MusicBrainz etiquette: ≥1 second between requests and the project User-Agent on
   every request.** MusicBrainz bans violators by UA/IP, which would kill the `update`
   command for the user. `MusicBrainzClient.GetJsonAsync` enforces 1.1 s spacing and
   the UA; route ALL MusicBrainz calls through that client. Wikimedia also rejects
   UA-less requests, hence the default UA on the shared HttpClient (UpdateCommand.cs).

5. **Do not restructure the ZuneXml templates or the `artist_data/` layout on
   reasoning alone.** The XML shapes are copied from the community XuneArtistImagesServer
   recreation, which is what real ZuneHD firmware is known to accept — element order,
   namespaces, the `urn:uuid:` id forms, and the `{mbid[:8]}-NNNN-0000-...` image-id
   scheme are all load-bearing and cannot be validated off-device. Byte-shape details:
   `zune-catalog-reference`. Any template change needs a real-device check.

6. **`update` rewrites the user's MP3 files (ID3 tags).** Any change to tag-writing
   logic must preserve the "only save when the MBID actually changed" guard in
   UpdateCommand.cs, and docs must keep the "back up your music first" warning.

7. **Never commit or distribute `.mtpz-data`.** It is a leaked key blob; README/
   WINDOWS.md deliberately say "not distributed with this project". Keep it that way.

8. **No firmware code paths.** WINDOWS.md promises users the tool cannot brick a
   device because nothing writes firmware. Do not add anything that would falsify that
   promise (the vendored library has upload/erase APIs — do not bind them without an
   explicit user decision).

## Known incidents (why the rules exist)

- **2026-07-02 — CoTaskMem-on-Windows heap bug** (rules 1–2): caught during Windows
  wiring before it shipped; would have been a crash-on-first-request on the user's
  Windows 10 test machine.
- **Stale upstream README**: the vendored library's own README claims Windows is
  unsupported; its CMake/WinUSB backend proves otherwise. Lesson: verify vendor claims
  against vendor code, not vendor docs.
- **Vendor test executables don't compile** against the current API. On Windows the
  build uses `-DZUNE_BUILD_TESTS=OFF`; on macOS the library target succeeds and test
  failures are ignored. Don't "fix" vendor tests as a side quest.

## Provenance and maintenance

Facts verified 2026-07-06 against the working tree; repo published 2026-07-12.

- Rule 1/2 code: `grep -n "ucrt_malloc\|CoTaskMem" XuneSyncNative.cs ServeCommand.cs`
- Rule 3 delegates: `grep -n "static readonly.*Delegate" ServeCommand.cs`
- Rule 4 spacing/UA: `grep -n "AddSeconds(1.1)\|UserAgent" MusicBrainzClient.cs`
- Rule 6 guard: `grep -n "MusicBrainzArtistId != match.Mbid" UpdateCommand.cs`
- Git remotes/state: `git -C . remote -v && git -C . log --oneline -3`
