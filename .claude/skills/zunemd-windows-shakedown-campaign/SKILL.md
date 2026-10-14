---
name: zunemd-windows-shakedown-campaign
description: Decision-gated campaign for the first real-device zunemd serve run on Windows 10 — the project's hardest open problem (the full WinUSB + PPP-over-USB path has never been exercised on Windows). Numbered phases with exact PowerShell commands, expected output at every gate, branch-on-failure triage, ranked failure hypotheses with kill-tests, fenced-off wrong paths, and measurable success criteria. Read when running, testing, or debugging zunemd on Windows, doing the driver swap, or when a Windows serve run fails. Keywords: Windows 10, WinUSB, Zadig, driver swap, shakedown, first run, zune_device_connect_usb.
---

# Windows serve shakedown campaign

Goal: prove (or precisely fail) the Windows `serve` path on the user's Windows 10
machine with a real Zune HD. As of 2026-07-06 all Windows code exists and compiles in
theory but **no one has run the PPP-over-USB path on Windows** — the upstream library
README even (wrongly) claims Windows is unsupported. Treat every gate as new territory.

**Not this skill:** macOS/general operation → `zunemd-runbook`. What the driver swap
is and device-safety background → WINDOWS.md (already written for this audience).
State-tuple meanings → `zune-catalog-reference`.

Safety recap (full version in WINDOWS.md): nothing writes to the device; driver swap
is PC-side only and reversible; device recovery is a reboot (power + media button).

## Phase 0 — Preconditions

```powershell
dotnet --version           # expect 8.x
cmake --version            # expect present
Test-Path "$env:USERPROFILE\.mtpz-data"   # expect True
```

- Any False/missing → WINDOWS.md §1 (winget installs, vcpkg bootstrap + `openssl curl taglib --triplet x64-windows`).
- Run build phases from a **Developer PowerShell for VS 2022**.

## Phase 1 — Native build

```powershell
cd vendor\XuneSyncLibrary
cmake -S . -B build -DZUNE_BUILD_TESTS=OFF -DCMAKE_TOOLCHAIN_FILE=C:\vcpkg\scripts\buildsystems\vcpkg.cmake
cmake --build build --config Release
cd ..\..
Test-Path vendor\XuneSyncLibrary\build\Release\xune_sync.dll          # GATE: True
Test-Path vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe  # expect True (libwdi is vendored)
```

- CMake configure errors about openssl/curl/taglib → vcpkg triplet not installed or
  toolchain path wrong.
- **Do not** re-enable tests to "check the build" — they are known not to compile
  (`zunemd-change-control` § incidents). The DLL is the deliverable.
- **Do not** switch the CRT to static (`/MT`) for any reason — guaranteed heap
  corruption at the first resolver callback (`zunemd-architecture-contract` § memory
  contract).

## Phase 2 — CLI build + native-load gate (no device needed)

```powershell
dotnet build
dotnet run -- serve
```

GATE — expected output: `Data directory not found: ...` (if no artist_data yet) or
`No Zune device detected on USB.` Both are **passes**: they prove the DLL loaded and
`zune_device_detect_on_usb` executed.

- `DllNotFoundException` / `Unable to load DLL 'xune_sync'` instead → the DLL or its
  vcpkg runtime DLLs didn't land next to the exe. Check
  `bin\Debug\net8.0\xune_sync.dll` and `libcrypto*.dll`/`libcurl*.dll` beside it;
  fallback: `$env:XUNE_SYNC_LIB = "<abs path to xune_sync.dll>"`.
- Crash/access violation already at this stage → record exact output; this is a
  finding (hypothesis C below), not a config error.

## Phase 2b — Read the device first (`artists`; UNVERIFIED on device, added 2026-10-13)

Do Phase 4 (driver swap) now, then:

```powershell
dotnet run -- artists --verbose | Tee-Object -FilePath artists-run1.log
```

GATE: `Connected: <name> (<family>)`, a table of artists, then
`N artist(s): X with an ID, Y without.` Failure branches: `No Zune device detected` /
`USB connect failed` → same as 5b/5c; `MTPZ keys not found` → fix the key path;
`Could not read the device library` → capture the verbose log.

**Stop and decide from this output before Phase 3.** The device only requests a card for
an artist it holds an ID for, and nothing yet shows the Zune desktop app carries the
`MusicBrainz Artist Id` tag to the device. The user's Zune already shows cards for most
artists (user, 2026-10-13), so scope Phase 3 to ONE artist folder that has no card.
Phase 3's sync needs the original Zune driver: restore it (Phase 6 command), sync, then
repeat Phase 4 before Phase 5.

## Phase 3 — Data + music

```powershell
dotnet run -- update "C:\Users\<you>\Music\<one artist>"     # back up that folder first (rewrites ID3 tags)
```

GATE: `Done. N artist(s) written to ...artist_data` with N ≥ 1, and per-artist
`biography:`/`background:` lines. Then sync the music itself with the **Zune desktop
app** (original Zune driver still active) — do this BEFORE the driver swap.

## Phase 4 — Driver swap to WinUSB

Get the instance id: Device Manager → Zune → Details → *Device instance path*
(e.g. `USB\VID_045E&PID_0710\<serial>`). Elevated prompt:

```powershell
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe status  "<instance-id>"
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe install "<instance-id>" --backup
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe status  "<instance-id>"   # GATE: reports WinUSB
```

- Helper missing/failing → Zadig fallback (WINDOWS.md §3 option B); the campaign does
  not depend on the helper.
- While WinUSB is active the Zune desktop app cannot see the device — that is
  expected, not a failure. Restore in Phase 6.

## Phase 5 — THE serve run

```powershell
dotnet run -- serve --verbose
```

Gates in order, with branches:

| # | Expected line | If instead… |
|---|---|---|
| 5a | `Serving artist data from ... (N artist(s))` | artist_data missing → Phase 3 |
| 5b | (implicit: device detected) | `No Zune device detected` → driver swap didn't take (Phase 4 status), cable, device asleep |
| 5c | `Connected: <name> (<family>)` | `USB connect failed` → **hypothesis ranking below** — this is the most likely first-failure point |
| 5d | no "does not support network mode" error | that error → device is not a Zune HD; hard stop |
| 5e | `Initializing HTTP subsystem...` passes | failure → record `--verbose` log; hypothesis C |
| 5f | `Network mode up.` within 10 attempts | all 10 fail → reboot device (power + media button), re-plug, re-run once; still failing → hypothesis C/D with log |
| 5g | `GET overview/biography/... -> <file>` lines as you open an artist on the device | polling silent → open the artist card on the device to trigger fetches; still silent → hypothesis D |
| 5h | `device state: ... progress=100 phase=4 status=3 (metadata fetch complete)` | stuck at `phase=1 status=1` → device still working, wait; stuck >5 min → capture log, hypothesis D |

## Failure hypotheses, ranked (with kill-tests)

Work top-down; each has a test that kills or confirms it cheaply. Capture the full
`--verbose` output before touching anything.

1. **Driver/plumbing** (most likely, cheapest): WinUSB not actually bound, wrong
   device instance, USB3 port quirk.
   *Kill-test:* `xune-driver-setup.exe status` says WinUSB AND Device Manager shows
   the device error-free. If yes, hypothesis dead — move on.
2. **MTPZ keys**: wrong/missing blob → connect fails during the MTPZ handshake.
   *Kill-test:* connect-failure text (from `zune_get_last_error`) mentions
   MTPZ/handshake/auth, and the same `.mtpz-data` file works on the macOS machine
   with the same device. If mac works with that file, the blob is good.
3. **WinUSB backend defect in the vendored AFTL layer** (code exists, never
   exercised): fails at detect/connect/HTTP-init with a non-MTPZ error.
   *Kill-test:* verbose log pinpoints the failing USB operation; the same step
   succeeds on macOS with the same device. Confirmed → this is a real finding: record
   the exact op and log; a vendor-code fix is a gated decision
   (`zunemd-change-control` § Vendor class), not a quick patch.
4. **Windows-specific PPP/TCP path defect**: connect works, network mode or data flow
   fails.
   *Kill-test:* same as 3 but past 5f; diff the verbose logs of a mac run vs the
   Windows run at the same phase.
5. **Resolver-callback heap issue** (crash at first `GET` line): should be impossible
   with `AllocFreeableUtf8` + `/MD`, which is exactly why a crash here is
   high-information.
   *Kill-test:* crash occurs at the first resolved request, access-violation
   flavored. Confirmed → verify the DLL links the CRT dynamically: run
   `dumpbin /dependents bin\Debug\net8.0\xune_sync.dll` from the Developer
   PowerShell and look for CRT imports (`api-ms-win-crt-heap-*.dll` or
   `ucrtbase.dll`); a static-CRT build shows neither. (UNVERIFIED: exact import
   names vary by toolchain — read the output, don't pattern-match.)

## Fenced-off wrong paths

- **Static CRT rebuild** — forbidden, causes the exact crash it pretends to fix.
- **Swapping `AllocFreeableUtf8` back to `Marshal.StringToCoTaskMemUTF8`** on Windows — heap corruption (`zunemd-change-control` rule 1).
- **Editing vendor USB code before hypotheses 1–2 are dead** — most first-run failures are driver/keys.
- **Firmware reinstall / factory reset as a debugging step** — never part of this tool's problem space; the tool has no firmware path.
- **Running `serve` with the stock Zune driver active** — cannot work; only WinUSB exposes the device to libusb-style access.
- **"Fixing" vendored test compile errors** — tests are OFF by design.

## Phase 6 — Restore + promote results

```powershell
vendor\XuneSyncLibrary\build\Release\xune-driver-setup.exe restore "<instance-id>"   # Zune desktop works again
```

**Success criteria (all measurable, all-or-nothing):**
1. Gate 5h line printed (`progress=100 phase=4 status=3`).
2. ≥1 `GET biography ... -> biography.xml` and ≥1 `GET devicebackgroundimage ... -> 1.jpg` with non-404 results in the log.
3. On the disconnected device, the artist card shows (a) biography text and (b) a
   background image. Each is present or absent — no "looks better".
4. Driver restore verified: Zune desktop app sees the device again.

**Promotion protocol** (routes through `zunemd-change-control` — docs are Free class):
- Success → update WINDOWS.md "Known unknowns" to state the path is verified (date it),
  and update the auto-memory `windows-testing-plan`.
- Failure → do NOT downgrade docs to guesses: record the failing gate number, exact
  error, and verbose log location in WINDOWS.md as a dated finding; the confirmed
  hypothesis becomes the next campaign.

## Provenance and maintenance

Written 2026-07-06 against the working tree (no git history). Commands sourced from
WINDOWS.md and verified against the code paths (driver-setup verbs against
`vendor/XuneSyncLibrary/tools/xune-driver-setup/main.cpp`; gate lines against
ServeCommand.cs strings).

- Gate lines still accurate? `grep -n "Console.WriteLine\|Console.Error" ServeCommand.cs`
- Driver-setup verbs: `head -10 vendor/XuneSyncLibrary/tools/xune-driver-setup/main.cpp`
- Campaign obsolete once WINDOWS.md "Known unknowns" says the path is device-verified.
