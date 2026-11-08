---
status: accepted
date: 2026-07-06
---
# Separate `update` and `serve` commands, linked only by `artist_data/{mbid}/`

## Context and Problem Statement
zunemd has to do two very different jobs: fetch artist metadata from the internet and tag the user's MP3s, and answer the ZuneHD's catalog requests over USB. The question was whether these share state or run as one process, or stay independent with a file layout as the only interface (CLAUDE.md § Architecture).

## Considered Options
* (alternatives not recorded)

## Decision Outcome
Chosen option: two commands that never run together, with `artist_data/{mbid}/` as the only interface between them, because it keeps network and USB concerns apart and lets the data folder be produced by hand. `update` never touches USB; `serve` never touches the network or the MP3s (`.claude/skills/zunemd-architecture-contract/SKILL.md:25-29`, Invariant 1).

### Consequences
* Good, because hand-made folders work, which the README's blank-background workaround (drop a baseline `1.jpg` into an artist folder) depends on (README.md:92-94).
* Good, because each command can be run and debugged alone.
* Bad, because the `artist_data/` layout becomes a public contract: `serve` must never require metadata only `update` could produce (`.claude/skills/zunemd-architecture-contract/SKILL.md:27-29`), and folder names must be lowercase MBIDs (CLAUDE.md gotcha 3).

## More Information
Evidence: `.claude/skills/zunemd-architecture-contract/SKILL.md:13-29`; CLAUDE.md § Architecture; present in the initial commit ca0ebcf (2026-07-29). Decision date not recorded; first evidenced 2026-07-06 (skills provenance, no git history then). Layout details: `.claude/skills/zune-catalog-reference/SKILL.md`. Related: ADR-0003 (the layout itself is copied from the reference server).
