---
status: accepted
date: 2026-07-06
---
# Hand-written P/Invoke bindings for only what the tool needs; destructive APIs stay unbound

## Context and Problem Statement
XuneSyncLibrary exposes far more than zunemd needs, including APIs that change device contents: `zune_device_erase_all_content`, `zune_device_delete_file`, `zune_mtp_delete_object`, `zune_mtp_send_object`, and the `zune_upload_*` family (vendor header `include/xune_sync/xune_sync_api.h`). WINDOWS.md promises users the tool cannot brick or alter the device (WINDOWS.md:94-111). The question was how much of the native API to bind.

## Considered Options
* Hand-write bindings for only the functions `serve` and `artists` need
* Bind upload/erase APIs too (barred without an explicit user decision, `.claude/skills/zunemd-change-control/SKILL.md:67-70`, rule 8)

## Decision Outcome
Chosen option: hand-written bindings for only what `serve` and `artists` need, with the header as source of truth, because it keeps the "cannot brick" promise checkable by reading one file (`.claude/skills/zunemd-architecture-contract/SKILL.md:38-41`). Reaffirmed 2026-10-13 when the `artists` command added only the read-only `zune_device_get_music_library` / `zune_device_free_music_library` pair (5c27488; XuneSyncNative.cs:163-165).

### Consequences
* Good, because XuneSyncNative.cs holds 23 library bindings and none of them writes, erases, or uploads (counted from XuneSyncNative.cs at 2026-11-01).
* Good, because the device-safety claim in WINDOWS.md stays true by construction.
* Bad, because every new device feature needs a new binding written and checked against the header by hand.

## More Information
Evidence: `.claude/skills/zunemd-architecture-contract/SKILL.md:38-41`; `.claude/skills/zunemd-change-control/SKILL.md:67-70`; WINDOWS.md:94-111; XuneSyncNative.cs:112-165; 5c27488 (2026-10-13). Decision date not recorded; first evidenced 2026-07-06 (skills provenance, no git history then). Related: ADR-0008.
