#!/usr/bin/env bash
# Experimental (runtime voice): copies the standalone Piper binary and the quantized voices into the KSP install,
# at GameData/Lonapse/Runtime/<platform>/ and GameData/Lonapse/Runtime/voices/. These files are not in git.
#   ./install_runtime.sh <piper folder> <voices folder> [windows|linux|macos]
# <piper folder> holds piper(.exe), its libraries and espeak-ng-data (a rhasspy/piper release, unpacked).
# <voices folder> holds the <voice>.onnx and .onnx.json files. KSP under Proton/Wine is the Windows player: windows.
set -euo pipefail
REPO="$(cd "$(dirname "$0")" && pwd)"
if [[ -z "${KSP_DIR:-}" && -f "$REPO/.ksp_dir" ]]; then
  KSP_DIR="$(grep -m1 -v '^[[:space:]]*$' "$REPO/.ksp_dir" | sed 's/[[:space:]]*$//')"
fi
SRC="${1:?usage: install_runtime.sh <piper folder> <voices folder> [windows|linux|macos]}"
VOICES="${2:?usage: install_runtime.sh <piper folder> <voices folder> [windows|linux|macos]}"
PLAT="${3:-windows}"
[[ -n "${KSP_DIR:-}" && -d "$KSP_DIR/GameData/Squad" ]] || { echo "ERROR: no KSP install (set KSP_DIR or .ksp_dir)" >&2; exit 1; }
if pgrep -f KSP_x64 >/dev/null 2>&1; then echo "ERROR: KSP is running, close it first" >&2; exit 1; fi
RT="$KSP_DIR/GameData/Lonapse/Runtime"
mkdir -p "$RT/$PLAT" "$RT/voices"
# the Arabic diacritics model and the pkgconfig folder are not needed for Spanish
rsync -a --delete --exclude 'libtashkeel_model.ort' --exclude 'pkgconfig' "$SRC/" "$RT/$PLAT/"
for v in es_ES-davefx-medium es_MX-claude-high es_MX-ald-medium; do
  [[ -f "$VOICES/$v.onnx" && -f "$VOICES/$v.onnx.json" ]] || { echo "ERROR: missing $VOICES/$v.onnx(.json)" >&2; exit 1; }
  cp -f "$VOICES/$v.onnx" "$VOICES/$v.onnx.json" "$RT/voices/"
done
echo "Installed runtime voice ($PLAT) into $RT: $(du -sh "$RT" | cut -f1)"
