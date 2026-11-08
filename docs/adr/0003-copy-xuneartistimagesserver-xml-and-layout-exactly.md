---
status: accepted
date: 2026-07-06
---
# Copy XuneArtistImagesServer's XML templates and data layout exactly

## Context and Problem Statement
The ZuneHD firmware expects specific Atom XML and image-id shapes, and nothing off-device can validate them. The community XuneArtistImagesServer recreation is the shape the firmware is known to accept (README.md:84-87). The question was whether to design our own responses or reproduce that server's.

## Considered Options
* (alternatives not recorded)

## Decision Outcome
Chosen option: copy the reference server's XML templates and `artist_data/` layout exactly (element order, namespaces, `urn:uuid:` id forms, `{mbid[:8]}-NNNN-...` image ids), because it is the only shape known to work on real firmware. Any change needs a real-device check, not reasoning alone (CLAUDE.md gotcha 6; `.claude/skills/zunemd-change-control/SKILL.md:53-58`, rule 5).

### Consequences
* Good, because the project starts from the one known-accepted format.
* Bad, because inherited quirks come along: `ZuneXml.Images` hardcodes `1000x1000` for every image even when the file is a different size, and fixing it also needs device proof (CLAUDE.md gotcha 9).
* Bad, because no `serve` run has been device-verified yet, so the format is still unconfirmed for this project (CLAUDE.md § Phase / status).

## More Information
Evidence: CLAUDE.md gotchas 6 and 9; `.claude/skills/zunemd-change-control/SKILL.md:53-58`; README.md:84-87; byte-shape details in `.claude/skills/zune-catalog-reference/SKILL.md`. Decision date not recorded; first evidenced 2026-07-06 (skills provenance, no git history then). Related: ADR-0001.
