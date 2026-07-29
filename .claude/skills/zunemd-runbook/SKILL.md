---
name: zunemd-runbook
description: Setup, build, run, configure, and debug zunemd (ZuneHD artist metadata updater) — from-scratch macOS setup, build commands, the full config/flag catalog, symptom→triage table, diagnostic scripts, and what counts as validation evidence in this repo. Read when building the project, running update or serve, hitting errors (USB connect failed, no device detected, DllNotFoundException, 404s, blank artist card), or verifying a change. Keywords: build, dotnet run, cmake, mtpz, triage, debug, validate.
---

# zunemd runbook

**Not this skill:** first Windows-device run → `zunemd-windows-shakedown-campaign`
(step-by-step with gates; this runbook stays macOS-first). Whether an edit is allowed →
`zunemd-change-control`. Protocol/state semantics → `zune-catalog-reference`.

## Setup from scratch (macOS)

```bash
brew install dotnet@8 cmake openssl curl taglib
# MTPZ keys (leaked blob used by libmtp/AFTL; NOT in this repo) -> ~/.mtpz-data
```

Windows setup is materially different (vcpkg, VS Build Tools, driver swap) — follow
WINDOWS.md or, for the first device run, the campaign skill.

## Build

```bash
cd vendor/XuneSyncLibrary && cmake -S . -B build && cmake --build build -j8 && cd ../..   # native, once
dotnet build                                                                              # CLI
```

Expected: `build/libxune_sync.dylib` exists; `dotnet build` reports 0 errors. Some of
the vendored *test executables* fail to compile — that is known-normal; only the
library target matters (`zunemd-change-control` § incidents).

## Run

```bash
dotnet run -- update ~/Music        # 1. tag MBIDs, download bios/images -> ./artist_data
# 2. sync music to the device (Zune desktop on Windows)
dotnet run -- serve --verbose       # 3. device plugged in; Ctrl+C to stop
```

Success signals for `serve`: `Connected: <name> (<family>)` → `Network mode up.` →
`GET <endpoint> <mbid> -> <file>` lines → `device state: ... progress=100 phase=4
status=3 (metadata fetch complete)`.

## Config catalog (complete — verified by grep 2026-07-06)

| Knob | Where | Default | Meaning |
|---|---|---|---|
| `update <music-folder>` | CLI arg | required | Root scanned recursively for `*.mp3` |
| `--data-dir DIR` | both commands | `./artist_data` | Artist data location |
| `--mtpz PATH` | serve | `~/.mtpz-data` (UserProfile on Windows) | MTPZ key blob |
| `--verbose` | serve | off | Native log callback + verbose TCP/IP logging |
| `XUNE_SYNC_LIB` | env var | unset | Absolute path to the native library; first probe candidate (then app dir, CWD, `vendor/.../build[/Release]`) |
| `server_ip` | hardcoded ServeCommand.cs | `192.168.0.30` | Arbitrary in-PPP address; not configurable by design |
| `proxy_timeout_ms` | hardcoded ServeCommand.cs | 5000 | Interceptor config field (unused in Static mode) |
| HTTP timeout | hardcoded UpdateCommand.cs | 30 s | Shared HttpClient |
| MusicBrainz spacing | hardcoded MusicBrainzClient.cs | 1.1 s | Do not reduce (`zunemd-change-control` rule 4) |

There are no other env/config reads (`grep -n "GetEnvironmentVariable\|GetFolderPath" *.cs`).

## Diagnostic scripts (read-only, in this skill's `scripts/`)

```bash
sh .claude/skills/zunemd-runbook/scripts/check-native-lib.sh      # which native lib loads, probe by probe
sh .claude/skills/zunemd-runbook/scripts/check-artist-data.sh     # validate artist_data layout, JPEG sanity, 10MB limit, progressive-JPEG flag
```

Both exit 0 = healthy. Run them before blaming the device. (Verified working
2026-07-06; the artist-data checker was exercised against a synthetic fixture since
`artist_data/` is generated and gitignored.)

## Symptom → triage

| Symptom | First moves |
|---|---|
| `DllNotFoundException` / native load failure | `check-native-lib.sh`; build the native lib; or set `XUNE_SYNC_LIB` |
| `No Zune device detected on USB` | Cable/port; device awake; **Windows: WinUSB driver not swapped** (campaign phase 3) |
| `USB connect failed` | MTPZ keys missing/wrong path (`--mtpz`); then driver (Windows); error detail comes from `zune_get_last_error` |
| `This device does not support network mode` | Not a Zune HD — hard stop, only HDs have the hardware |
| `Interceptor start failed (rc)` | Verify HTTP subsystem init succeeded; re-plug and retry; `--verbose` for native log |
| Network mode fails all 10 attempts | Reboot device (hold power + media button), re-plug, re-run |
| `GET ... -> 404` lines | `check-artist-data.sh`; MBID in device's request vs folder name (must be lowercase, exact); re-run `update` |
| Device fetches fine but artist card background blank | Progressive JPEG suspicion — replace `1.jpg` with a baseline JPEG, re-run `serve` (see `zune-catalog-reference` § quirks) |
| `update`: `no MusicBrainz match` for many files | Missing/garbage artist tags → filename-guess kicked in; fix ID3 tags |
| `update`: HTTP 403/503 from MusicBrainz | Rate-limit/UA violation — check nothing bypasses `MusicBrainzClient.GetJsonAsync` |
| Empty bio ("No biography available") | Artist lacks Wikipedia/Wikidata relations on MusicBrainz — data gap, not a bug |

## Validation bar (what counts as evidence here)

There are no tests and no CI. Claims are graded:

1. **Build evidence** (minimum for any C# change): `dotnet build` → 0 errors.
2. **Data evidence** (update-side changes): run `update` against a small real folder;
   `check-artist-data.sh` exits 0; eyeball one biography.xml for sane content.
3. **Device evidence** (serve-side / XML-template / native-boundary changes — the
   only proof that counts): a real Zune HD run reaching `progress=100 phase=4
   status=3` with the expected `GET` lines, then the artist card on the device
   showing biography text and a background image. Binary criteria, never "looks
   about right": each element is present or it isn't.
4. Anything not device-verified is labeled UNVERIFIED in docs and skills — this repo's
   docs already follow that convention (e.g. progressive-JPEG acceptance).

## Provenance and maintenance

Verified 2026-07-06 on macOS (arm64), working tree (no git history).

- Flags: `grep -n '"--' Program.cs` · Config hardcodes: `grep -n "192.168.0.30\|proxy_timeout_ms\|FromSeconds" ServeCommand.cs UpdateCommand.cs`
- Env reads: `grep -n "GetEnvironmentVariable\|GetFolderPath" *.cs`
- Scripts still pass: run both commands in the Diagnostic scripts section.
