#!/usr/bin/env bash
# Build the ResourceTracker BepInEx mod and install it into the game's plugins folder.
#
# Override game path: SOLAR_EXPANSE_GAME=/path/to/Solar\ Expanse ./build.sh

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_GAME="$HOME/Applications/Sikarugir/Steam Wine.app/Contents/SharedSupport/prefix/drive_c/Program Files (x86)/Steam/steamapps/common/Solar Expanse"
GAME="${SOLAR_EXPANSE_GAME:-$DEFAULT_GAME}"

if [[ ! -d "$GAME/BepInEx/plugins" ]]; then
    echo "BepInEx plugins dir not found at: $GAME/BepInEx/plugins" >&2
    echo "Set SOLAR_EXPANSE_GAME to override." >&2
    exit 1
fi

SOLAR_EXPANSE_ROOT="$GAME" dotnet build "$HERE" -c Release --nologo --verbosity quiet
OUT="$HERE/bin/Release/net472"
DEST="$GAME/BepInEx/plugins"
cp "$OUT/ResourceTracker.dll" "$DEST/ResourceTracker.dll"
echo "installed → $DEST/ResourceTracker.dll"
