---
status: accepted
date: 2026-07-12
---
# Don't redistribute the LGPL native library; clone a pinned fork into `vendor/`

## Context and Problem Statement
zunemd depends on XuneSyncLibrary, a separate LGPL-2.1 project (README.md:38). Publishing the repo publicly forced a choice about whether to ship that library's source and which revision builders should use.

## Considered Options
* Redistribute the library in this repo (rejected; `.gitignore:8-9`, "not redistributed here")
* Have users clone the unpinned upstream
* Have users clone a fork pinned at a known-good commit into gitignored `vendor/`

## Decision Outcome
Chosen option: users clone a fork pinned at `c2a6eeb`, an unmodified copy of xune-software/XuneSyncLibrary, into `vendor/XuneSyncLibrary/`, which is gitignored, because it keeps LGPL code out of this repo and "everyone builds against the same known-good revision" (README.md:38-48; WINDOWS.md:41-44).

### Consequences
* Good, because builds are reproducible against one revision.
* Bad, because there is a manual clone-and-checkout step before `dotnet build`.
* Bad, because local edits under `vendor/` are invisible to git and lost on re-vendoring (`.claude/skills/zunemd-change-control/SKILL.md:24`).

## More Information
Evidence: README.md:38-48; WINDOWS.md:41-44; `.gitignore:8-9`; CLAUDE.md § Phase / status (fork and pin) and § Conventions ("published 2026-07-12"). Git history starts 2026-07-29 (ca0ebcf), so 2026-07-12 is the documented publish date, not a commit date.
