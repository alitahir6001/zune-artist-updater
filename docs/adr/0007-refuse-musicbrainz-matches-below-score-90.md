---
status: accepted
date: 2026-08-17
---
# Refuse MusicBrainz matches below score 90; compilations fall back to track performer

## Context and Problem Statement
`update` writes the MBID it finds into the user's MP3s, so a wrong match silently corrupts their files (CLAUDE.md gotcha 4). The original lookup took MusicBrainz's first hit (`limit=1`) with no confidence check, and compilation albums tagged "Various Artists" sent a placeholder to the search instead of the real artist.

## Considered Options
* Take the first hit with no threshold (original behavior)
* Take the first hit only when its score is at least 90, and use the track performer when the album artist is a compilation placeholder

## Decision Outcome
Chosen option: "score floor of 90 plus compilation fallback", because a wrong MBID is worse than no MBID: below the floor the tool skips the artist and tells the user to tag it manually (UpdateCommand.cs:95-101). Compilation placeholders such as "Various Artists" fall back to `FirstPerformer` and are skipped if still a placeholder (UpdateCommand.cs:13-17, 64-79).

### Consequences
* Good, because low-confidence guesses are no longer written into the user's files.
* Bad, because some correct matches below 90 are skipped and need manual tagging.
* Neutral, because the query is still `limit=1` (MusicBrainzClient.cs:20); the floor filters the top hit, it does not compare candidates.

## More Information
Evidence: 58baf3f (adds `Score` to `ArtistMatch`), d799a9b (adds `MinMatchScore = 90`, compilation fallback, and the `tracks.xml` resume check), both 2026-08-17; CLAUDE.md gotcha 4; UpdateCommand.cs:10-17, 64-105. Loosening either rule re-opens wrong writes (gotcha 4).
