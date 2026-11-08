---
status: accepted
date: 2026-07-02
---
# Windows support via a temporary WinUSB driver swap and a ucrtbase-heap callback contract

## Context and Problem Statement
On Windows the Zune's stock driver owns the device, so the library's WinUSB backend can't reach it (WINDOWS.md:68-77). Separately, the path-resolver callback returns a C string that the native side `free()`s, so C# must allocate on the same CRT heap the DLL frees from (`.claude/skills/zunemd-architecture-contract/SKILL.md:42-50`). Wiring up Windows forced choices on the driver, the allocator, the CRT, and where the `.mtpz-data` default path is resolved.

## Considered Options
* Callback allocator: `ucrtbase.dll!malloc` vs `Marshal.StringToCoTaskMemUTF8` (COM heap on Windows; rejected)
* Native CRT: dynamic Release `/MD` vs static CRT (crashes in `free()`; rejected) vs Debug `/MDd` (frees on `ucrtbased.dll`; rejected)
* `.mtpz-data` default: resolved in C# vs the native `$HOME` fallback (`$HOME` is normally unset on Windows; rejected)
* Driver swap tool: bundled `xune-driver-setup.exe` vs Zadig (both documented)

## Decision Outcome
Chosen option: swap the Zune to WinUSB only while `serve` runs (and back afterwards), allocate callback strings with ucrtbase `malloc` via `AllocFreeableUtf8`, build the native DLL with the dynamic Release CRT, and resolve the `~/.mtpz-data` default in C#, because each rejected option either corrupts the heap or fails on a stock Windows setup (CLAUDE.md gotcha 1; `.claude/skills/zunemd-change-control/SKILL.md:28-39`, rules 1-2; `.claude/skills/zunemd-architecture-contract/SKILL.md:72`).

### Consequences
* Good, because the CoTaskMem heap bug was caught before it shipped (`.claude/skills/zunemd-change-control/SKILL.md:74-76`).
* Good, because the driver swap changes only a PC setting and is reversible with either tool (WINDOWS.md:79-92, 99).
* Bad, because users must swap drivers back and forth to use the Zune desktop app (WINDOWS.md:74-77).
* Bad, because the Windows `serve` path is not yet device-verified (CLAUDE.md § Phase / status).

## More Information
Evidence: CLAUDE.md gotcha 1 and § Phase / status ("Windows support wired up 2026-07-02"); `.claude/skills/zunemd-change-control/SKILL.md:28-39,74-76`; `.claude/skills/zunemd-architecture-contract/SKILL.md:42-50,72`; WINDOWS.md:58-92; XuneSyncNative.cs:82-107; ServeCommand.cs:60-64. Predates git history (first commit ca0ebcf, 2026-07-29). Verification plan: `.claude/skills/zunemd-windows-shakedown-campaign/SKILL.md`.
