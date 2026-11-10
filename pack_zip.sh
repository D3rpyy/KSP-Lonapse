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
zip -qr -X "$ZIP" GameData/Lonapse -x 'GameData/Lonapse/PluginData/*' 'GameData/Lonapse/Sounds/crew/*' 'GameData/Lonapse/Sounds/control/*'
STAGE="$(mktemp -d "$REPO/dist/runtime_stage.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/GameData/Lonapse/PluginData"
cp -r "$RUNTIME" "$STAGE/GameData/Lonapse/PluginData/Runtime"
(cd "$STAGE" && zip -qr -X "$ZIP" GameData)

# read the listing once (grep -q on a pipe closes it early, and with pipefail that looks like a failure)
LIST="$(unzip -Z1 "$ZIP")"
n=$(grep -cE '\.(wav|ogg)$' <<<"$LIST" || true)
r=$(grep -c 'PluginData/Runtime/' <<<"$LIST" || true)
for f in Icons/button.png Plugins/Lonapse.dll PluginData/Runtime/windows/piper.exe PluginData/Runtime/linux/piper \
         Phrases/Crew/reports.tsv Phrases/Crew/launch.tsv Phrases/Probe/personalities.tsv \
         Phrases/Crew/intercom_chatter.tsv Phrases/Crew/radio_chatter.tsv \
         Phrases/Probe/reports.tsv Phrases/Probe/science_talk.tsv \
         Phrases/Probe/science_contracts.tsv Phrases/Probe/docking.tsv \
         Phrases/Probe/warp.tsv Phrases/Probe/stress.tsv \
         Patches/Lonapse_StockCores.cfg Compat/DeepFreeze/deepfreeze.tsv \
         Sounds/sfx/control_room_loop/sfx_control_room_loop_000.wav \
         Sounds/sfx/cabin_rattle/sfx_cabin_rattle_000.wav \
         Sounds/sfx/cabin_separation/sfx_cabin_separation_000.wav; do
  grep -qx "GameData/Lonapse/$f" <<<"$LIST" || { echo "ERROR: $f is missing from the zip" >&2; exit 1; }
done
# old table locations and schemas must not ship again
for f in Phrases/Crew/chats.tsv Phrases/Crew/chatter.tsv Phrases/Crew/cabin.tsv \
         Phrases/Crew/radio_chats.tsv Phrases/Crew/eva_chats.tsv Phrases/Crew/event_chats.tsv \
         Phrases/phrases.tsv Phrases/pools.tsv Phrases/Extra/probe_runtime.tsv Phrases/Runtime/probe_context.tsv Phrases/Runtime/probe_stress.tsv \
         Phrases/Runtime_Probe/probe_context.tsv Phrases/Runtime_Probe/probe_stress.tsv \
         Phrases/runtime.tsv Phrases/slots_es.tsv Phrases/Runtime/deepfreeze.tsv; do
  if grep -qx "GameData/Lonapse/$f" <<<"$LIST"; then echo "ERROR: old $f is in the zip, remove it from GameData" >&2; exit 1; fi
done
if grep -qE '^GameData/Lonapse/(Sounds/(crew|control)/|Phrases/(Runtime|Runtime_Probe)/)' <<<"$LIST"; then
  echo "ERROR: retired speech assets or table folders in zip" >&2; exit 1
fi
echo "Packed: $ZIP ($(stat -c %s "$ZIP") bytes, $n sound files, $r runtime voice entries)"
