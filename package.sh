#!/usr/bin/env bash
# Refresh GameData/Lonapse in the repo from the builds, then zip it as dist/Lonapse-<version>.zip.
# Usage: ./package.sh
# Needs the KSP DLLs to compile (KSP_DIR or .ksp_dir, see build.sh) and a finished audio build:
#   python3 -I audio/build_clips.py   (beeps, radio static and breathing in sfx)
#   python3 -I audio/build_sfx.py     (docking effects from audio/sfx_sources, into sfx)
#   python3 -I audio/build_lonapse.py (voices, phrase table)
#   ./fetch_runtime.sh                (runtime voice: Piper and the voices, into audio/build/runtime/Runtime)
# Commit the changed GameData/ afterwards.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO"
GD="$REPO/GameData/Lonapse"
AVC_FILE="$GD/Lonapse.version"

VERSION="$(sed -n 's/.*const string Version = "\([0-9.]*\)".*/\1/p' src/LonapseAddon.cs)"
if [[ -z "$VERSION" ]]; then echo "ERROR: no Version found in src/LonapseAddon.cs" >&2; exit 1; fi
AVC="$(python3 -c 'import json,sys;v=json.load(open(sys.argv[1]))["VERSION"];print("%d.%d.%d"%(v["MAJOR"],v["MINOR"],v["PATCH"]))' "$AVC_FILE")"
if [[ "$AVC" != "$VERSION" ]]; then
  echo "ERROR: Lonapse.version says $AVC but LonapseAddon.Version is $VERSION" >&2
  exit 1
fi
grep -q "^## $VERSION" CHANGELOG.md || { echo "ERROR: CHANGELOG.md has no entry for $VERSION" >&2; exit 1; }

SOUNDS="$REPO/audio/build/Sounds"
TABLE="$REPO/audio/build/lonapse/phrases.tsv"
for d in beeps sfx sfx/airlock_dock sfx/airlock_undock crew control; do
  [[ -d "$SOUNDS/$d" ]] || { echo "ERROR: $SOUNDS/$d is missing, build the audio first" >&2; exit 1; }
done
[[ -f "$TABLE" ]] || { echo "ERROR: $TABLE is missing, run audio/build_lonapse.py first" >&2; exit 1; }

echo "Running the offline tests"
bash tests/run.sh >/dev/null || { echo "ERROR: tests failed, run tests/run.sh to see why" >&2; exit 1; }
"$REPO/build.sh" --compile-only

# Refresh GameData/Lonapse. PluginData in the repo (user settings) is never touched or packed. pack_zip.sh adds the
# runtime voice to the zip from audio/build/runtime/Runtime, as GameData/Lonapse/PluginData/Runtime.
mkdir -p "$GD/Plugins" "$GD/Sounds" "$GD/Phrases"
cp build/Lonapse.dll "$GD/Plugins/Lonapse.dll"
# Only audio files. A .cfg under GameData would be parsed by KSP.
rsync -a --delete --prune-empty-dirs --include='*/' --include='*.wav' --include='*.ogg' --exclude='*' \
  "$SOUNDS/" "$GD/Sounds/"
cp "$TABLE" "$GD/Phrases/phrases.tsv"
cp LICENSE "$GD/LICENSE.txt"
cp LICENSE-CC-BY-4.0 "$GD/LICENSE-CC-BY-4.0.txt"
cp docs/LICENSING.md "$GD/LICENSING.md"
cp README.md CHANGELOG.md "$GD/"
cp audio/CREDITS.md "$GD/CREDITS.md"

echo "Updated: $GD"
"$REPO/pack_zip.sh" "$VERSION"
