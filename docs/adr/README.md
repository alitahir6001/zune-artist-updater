# Architecture Decision Records

This folder records the design decisions that shape zunemd and why they were made (MADR 4.0, one file per decision).

* [0001](0001-separate-update-and-serve-commands-linked-by-artist-data.md) - Separate `update` and `serve` commands, linked only by `artist_data/{mbid}/` - accepted - 2026-07-06
* [0002](0002-static-interceptor-mode.md) - Run the metadata interceptor in Static mode, with all content prefetched - accepted - 2026-07-06
* [0003](0003-copy-xuneartistimagesserver-xml-and-layout-exactly.md) - Copy XuneArtistImagesServer's XML templates and data layout exactly - accepted - 2026-07-06
* [0004](0004-bind-only-needed-native-apis-leave-destructive-ones-unbound.md) - Hand-written P/Invoke bindings for only what the tool needs; destructive APIs stay unbound - accepted - 2026-07-06
* [0005](0005-windows-support-via-winusb-and-ucrtbase-malloc.md) - Windows support via a temporary WinUSB driver swap and a ucrtbase-heap callback contract - accepted - 2026-07-02
* [0006](0006-do-not-redistribute-lgpl-native-library-use-pinned-fork.md) - Don't redistribute the LGPL native library; clone a pinned fork into `vendor/` - accepted - 2026-07-12
* [0007](0007-refuse-musicbrainz-matches-below-score-90.md) - Refuse MusicBrainz matches below score 90; compilations fall back to track performer - accepted - 2026-08-17
* [0008](0008-read-device-with-artists-before-update.md) - Add a read-only `artists` command and read the device before `update` - accepted - 2026-10-13
