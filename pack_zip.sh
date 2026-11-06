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

# read the listing once (grep -q on a pipe closes it early, and with pipefail that looks like a failure)
LIST="$(unzip -Z1 "$ZIP")"
n=$(grep -cE '\.(wav|ogg)$' <<<"$LIST" || true)
r=$(grep -c 'PluginData/Runtime/' <<<"$LIST" || true)
for f in Plugins/Lonapse.dll PluginData/Runtime/windows/piper.exe PluginData/Runtime/linux/piper; do
  grep -qx "GameData/Lonapse/$f" <<<"$LIST" || { echo "ERROR: $f is missing from the zip" >&2; exit 1; }
done
echo "Packed: $ZIP ($(stat -c %s "$ZIP") bytes, $n sound files, $r runtime voice entries)"
