---
status: accepted
date: 2026-10-13
---
# Add a read-only `artists` command and read the device before `update`

## Context and Problem Statement
The device only requests an artist card for an artist it holds an ID for, and nothing shows the Zune desktop app copies the `MusicBrainz Artist Id` tag to the device (CLAUDE.md gotcha 11). The user's Zune already shows cards for most artists, so it likely holds old Zune-catalog IDs (gotcha 11, unverified). The old flow tagged everything with `update`, synced, then ran `serve`, which would rewrite tags without knowing whether the device would ever ask for them.

## Considered Options
* Old flow: `update` everything, sync, then `serve`
* Read the device first with a read-only `artists` command, then `update` only artists without a card

## Decision Outcome
Chosen option: "read the device first", because it shows whether the artist-ID chain holds before any tags are rewritten (WINDOWS.md:113-133). `artists` lists the device's artists and the ID it holds for each, using only the read-only library fetch (5c27488, 77c4bb8, b3a05b5).

### Consequences
* Good, because the riskiest unknown (gotcha 11) is checked before touching the user's files.
* Good, because `artists` adds no write path to the device (see ADR-0004).
* Bad, because the Windows test flow now needs an extra driver swap round-trip (WINDOWS.md:118-129).
* Bad, because `artists` is not yet device-verified (README.md:91).

## More Information
Evidence: 5c27488 (bindings), 77c4bb8 (ArtistsCommand.cs), b3a05b5 (CLI wiring), f60a73e (WINDOWS.md test flow), 3a7f234 (campaign skill gate), all 2026-10-13; CLAUDE.md gotcha 11; WINDOWS.md:113-133. Related: ADR-0004.
