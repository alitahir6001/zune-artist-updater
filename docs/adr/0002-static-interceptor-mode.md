---
status: accepted
date: 2026-07-06
---
# Run the metadata interceptor in Static mode, with all content prefetched

## Context and Problem Statement
XuneSyncLibrary's artist-metadata interceptor offers Static, Proxy and Hybrid modes. Proxy and Hybrid forward misses to a live upstream catalog server; Zune.net is dead and no replacement upstream exists (`.claude/skills/zunemd-architecture-contract/SKILL.md:67`).

## Considered Options
* Static: answer every request from local files
* Proxy: forward requests to an upstream catalog server
* Hybrid: serve local files, fall back to upstream and cache

## Decision Outcome
Chosen option: "Static", because Proxy and Hybrid need a live upstream catalog server and none exists, and `update` already prefetches everything `serve` needs (`.claude/skills/zunemd-architecture-contract/SKILL.md:67`). Configured at ServeCommand.cs:97-102 (`mode = ArtistMetadataMode.Static`, line 99).

### Consequences
* Good, because `serve` needs no network access, which keeps ADR-0001's separation intact.
* Bad, because an artist with no prefetched folder simply gets a 404; the device cannot fill gaps on demand.
* Neutral, because the header's cache-storage callback (Hybrid) stays unbound; it is listed as candidate work in `.claude/skills/zunemd-research-frontier/SKILL.md` (`.claude/skills/zunemd-architecture-contract/SKILL.md:67`).

## More Information
Evidence: `.claude/skills/zunemd-architecture-contract/SKILL.md:67`; ServeCommand.cs:99; present in ca0ebcf (2026-07-29). Decision date not recorded; first evidenced 2026-07-06 (skills provenance, no git history then). Related: ADR-0001.
