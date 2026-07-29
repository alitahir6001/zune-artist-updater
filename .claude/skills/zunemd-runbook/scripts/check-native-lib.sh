#!/bin/sh
# Read-only check: which native xune_sync library would the CLI load?
# Replicates the probe order in XuneSyncNative.cs (run from the repo root so
# "current directory" candidates match a `dotnet run` from the same place).
# Usage: check-native-lib.sh [APP_BASE_DIR]
#   APP_BASE_DIR emulates AppContext.BaseDirectory (default: bin/Debug/net8.0).

case "$(uname -s)" in
    Darwin) FILE=libxune_sync.dylib ;;
    Linux)  FILE=libxune_sync.so ;;
    *)      FILE=xune_sync.dll ;;   # MSYS/Cygwin on Windows
esac

BASE="${1:-bin/Debug/net8.0}"

echo "Probe order for '$FILE' (first hit wins):"
found=""
i=0
for p in \
    "${XUNE_SYNC_LIB:-}" \
    "$BASE/$FILE" \
    "./$FILE" \
    "vendor/XuneSyncLibrary/build/$FILE" \
    "$BASE/vendor/XuneSyncLibrary/build/$FILE" \
    "vendor/XuneSyncLibrary/build/Release/$FILE" \
    "$BASE/vendor/XuneSyncLibrary/build/Release/$FILE"
do
    i=$((i + 1))
    if [ $i -eq 1 ]; then
        label="\$XUNE_SYNC_LIB"
        [ -z "$p" ] && { echo "  $i. $label (unset)"; continue; }
    else
        label="$p"
    fi
    if [ -f "$p" ]; then
        echo "  $i. $label  <-- EXISTS$( [ -z "$found" ] && echo ' (this one loads)')"
        [ -z "$found" ] && found="$p"
    else
        echo "  $i. $label  (missing)"
    fi
done

echo
if [ -n "$found" ]; then
    echo "Would load: $found"
    file "$found" 2>/dev/null
else
    echo "FAIL: no native library found - build it first (see the runbook build section)"
    exit 1
fi
