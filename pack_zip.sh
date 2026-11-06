#!/usr/bin/env bash
# Zips the release: the committed GameData/Lonapse (without PluginData) plus the runtime voice from
# audio/build/runtime/Runtime (./fetch_runtime.sh) as GameData/Lonapse/PluginData/Runtime.
# Used by package.sh and by the release workflow, so both make the same zip.
# Usage: ./pack_zip.sh <version>   writes dist/Lonapse-<version>.zip
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO"
VERSION="${1:?usage: pack_zip.sh <version>}"
RUNTIME="$REPO/audio/build/runtime/Runtime"
for f in windows/piper.exe linux/piper voices/es_ES-davefx-medium.onnx voices/es_MX-claude-high.onnx \
         voices/es_MX-ald-medium.onnx LICENSES/README.md; do
  [[ -f "$RUNTIME/$f" ]] || { echo "ERROR: $RUNTIME/$f is missing, run ./fetch_runtime.sh first" >&2; exit 1; }
done
[[ -f GameData/Lonapse/Plugins/Lonapse.dll ]] || { echo "ERROR: GameData/Lonapse/Plugins/Lonapse.dll is missing" >&2; exit 1; }

mkdir -p dist
ZIP="$REPO/dist/Lonapse-$VERSION.zip"
rm -f "$ZIP"
zip -qr -X "$ZIP" GameData/Lonapse -x 'GameData/Lonapse/PluginData/*'
STAGE="$(mktemp -d "$REPO/dist/runtime_stage.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/GameData/Lonapse/PluginData"
cp -r "$RUNTIME" "$STAGE/GameData/Lonapse/PluginData/Runtime"
(cd "$STAGE" && zip -qr -X "$ZIP" GameData)

n=$(unzip -l "$ZIP" | grep -cE '\.(wav|ogg)$')
r=$(unzip -l "$ZIP" | grep -c 'PluginData/Runtime/')
unzip -l "$ZIP" | grep -q 'GameData/Lonapse/PluginData/Runtime/windows/piper.exe' || { echo "ERROR: piper.exe is missing from the zip" >&2; exit 1; }
echo "Packed: $ZIP ($(stat -c %s "$ZIP") bytes, $n sound files, $r runtime voice entries)"
