#!/bin/sh
# Read-only validation of a zunemd artist_data directory.
# Usage: check-artist-data.sh [DATA_DIR]   (default: ./artist_data)
# Checks, per artist folder:
#   - folder name is a 36-char lowercase MBID (device URLs are matched lowercase)
#   - required XML files exist and start with an XML declaration
#   - JPEGs have the FFD8 magic, are under the 10 MB interceptor limit,
#     and reports baseline (SOF0) vs progressive (SOF2) encoding (heuristic hex
#     scan; progressive backgrounds are a suspect if the device shows a blank card)
#   - warns when 1.jpg (background) is missing
# Exit code: 0 = all checks passed, 1 = at least one problem found.

DATA_DIR="${1:-./artist_data}"
[ -d "$DATA_DIR" ] || { echo "FAIL: data dir not found: $DATA_DIR (run 'zunemd update' first)"; exit 1; }

problems=0
artists=0

for dir in "$DATA_DIR"/*/; do
    [ -d "$dir" ] || continue
    artists=$((artists + 1))
    name=$(basename "$dir")
    echo "== $name"

    case "$name" in
        [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]-*-*-*-????????????) ;;
        *) echo "  PROBLEM: folder name is not a lowercase MBID (serve lowercases the device's UUID before lookup)"
           problems=$((problems + 1)) ;;
    esac

    for f in overview.xml biography.xml images.xml albums.xml similarArtists.xml tracks.xml; do
        if [ ! -f "$dir$f" ]; then
            echo "  PROBLEM: missing $f"
            problems=$((problems + 1))
        elif ! head -c 5 "$dir$f" | grep -q '^<?xml'; then
            echo "  PROBLEM: $f does not start with an XML declaration"
            problems=$((problems + 1))
        fi
    done

    [ -f "${dir}1.jpg" ] || echo "  WARN: no 1.jpg - artist card will have no background image"

    for jpg in "$dir"*.jpg; do
        [ -f "$jpg" ] || continue
        base=$(basename "$jpg")
        size=$(wc -c < "$jpg" | tr -d ' ')
        magic=$(head -c 2 "$jpg" | xxd -p)
        if [ "$magic" != "ffd8" ]; then
            echo "  PROBLEM: $base is not a JPEG (magic $magic)"
            problems=$((problems + 1))
            continue
        fi
        if [ "$size" -gt 10485760 ]; then
            echo "  PROBLEM: $base is ${size} bytes (> 10 MB interceptor limit - device gets an error)"
            problems=$((problems + 1))
        fi
        hex=$(xxd -p "$jpg" | tr -d '\n')
        case "$hex" in
            *ffc2*) enc="progressive (SOF2) - suspect if background stays blank on device" ;;
            *ffc0*) enc="baseline (SOF0)" ;;
            *)      enc="no SOF0/SOF2 marker found (unusual)" ;;
        esac
        echo "  OK: $base ${size} bytes, $enc"
    done
done

echo
if [ "$artists" -eq 0 ]; then
    echo "FAIL: no artist folders in $DATA_DIR"
    exit 1
fi
echo "$artists artist folder(s), $problems problem(s)"
[ "$problems" -eq 0 ]
