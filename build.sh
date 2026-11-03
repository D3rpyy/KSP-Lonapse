#!/usr/bin/env bash
# Build Lonapse and install it into the KSP instance.
# Usage: ./build.sh [--compile-only] [--no-sounds]
# The KSP install comes from $KSP_DIR, or from the first line of .ksp_dir in the repo root (a local file, not in git).
#   KSP_DIR=/path/to/ksp ./build.sh
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -z "${KSP_DIR:-}" && -f "$REPO/.ksp_dir" ]]; then
  KSP_DIR="$(grep -m1 -v '^[[:space:]]*$' "$REPO/.ksp_dir" | sed 's/[[:space:]]*$//')"
fi
if [[ -z "${KSP_DIR:-}" ]]; then
  echo "ERROR: no KSP install given. Set KSP_DIR or write its path into $REPO/.ksp_dir" >&2
  exit 1
fi
COMPILE_ONLY=0
NO_SOUNDS=0

for arg in "$@"; do
  case "$arg" in
    --compile-only) COMPILE_ONLY=1 ;;
    --no-sounds) NO_SOUNDS=1 ;;
    -h|--help) sed -n '2,4p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

MG="$KSP_DIR/KSP_Data/Managed"
if [[ ! -f "$MG/Assembly-CSharp.dll" ]]; then
  echo "ERROR: Assembly-CSharp.dll not found in: $MG" >&2
  exit 1
fi
command -v mcs >/dev/null || { echo "ERROR: mcs not found (install mono)" >&2; exit 1; }

refs=()
for a in Assembly-CSharp Assembly-CSharp-firstpass UnityEngine UnityEngine.CoreModule \
         UnityEngine.AudioModule UnityEngine.IMGUIModule UnityEngine.InputLegacyModule \
         UnityEngine.PhysicsModule UnityEngine.AnimationModule UnityEngine.TextRenderingModule UnityEngine.UI; do
  refs+=("-r:$MG/$a.dll")
done

mkdir -p "$REPO/build"
OUT="$REPO/build/Lonapse.dll"
shopt -s nullglob
SRC=("$REPO"/src/*.cs)
shopt -u nullglob
if [[ ${#SRC[@]} -eq 0 ]]; then echo "ERROR: no sources in $REPO/src" >&2; exit 1; fi

echo "Compiling ${#SRC[@]} source files against: $MG"
mcs -target:library -nostdlib -noconfig \
    -r:"$MG/mscorlib.dll" -r:"$MG/System.dll" -r:"$MG/System.Core.dll" "${refs[@]}" \
    -optimize+ -warn:4 -out:"$OUT" "${SRC[@]}"
echo "Compiled: $OUT ($(stat -c %s "$OUT") bytes)"

if [[ $COMPILE_ONLY -eq 1 ]]; then
  echo "Compile only, nothing installed."
  exit 0
fi

# ---- install (only inside GameData/Lonapse) ----
if [[ ! -d "$KSP_DIR/GameData/Squad" ]]; then
  echo "ERROR: $KSP_DIR/GameData/Squad not found, refusing to install" >&2
  exit 1
fi
GD="$KSP_DIR/GameData/Lonapse"
case "$GD" in
  */GameData/Lonapse) ;;
  *) echo "ERROR: install path sanity check failed: $GD" >&2; exit 1 ;;
esac

# Mono maps the loaded DLL from disk. Overwriting it while KSP runs corrupts the running game.
if pgrep -f "KSP_x64.exe" >/dev/null 2>&1 || pgrep -x "KSP.x86_64" >/dev/null 2>&1; then
  echo "ERROR: KSP is running. Installing over a loaded Lonapse.dll corrupts the running game." >&2
  echo "Close KSP first, or use --compile-only." >&2
  exit 1
fi
mkdir -p "$GD/Plugins" "$GD/PluginData"

# Rename from Quindar: keep the old settings, then remove the old folder (both DLLs would load and play at once).
OLD="$KSP_DIR/GameData/Quindar"
if [[ -d "$OLD" && ! -L "$OLD" && -f "$OLD/Plugins/Quindar.dll" ]]; then
  if [[ -d "$OLD/PluginData" ]]; then
    cp -n "$OLD"/PluginData/* "$GD/PluginData/" 2>/dev/null || true
    echo "Rename: copied the old settings from $OLD/PluginData (existing files kept)"
  fi
  rm -rf -- "$OLD"
  echo "Rename: removed the old install $OLD"
fi

# Atomic replace: write a new file, then rename it over the old one (new inode).
cp -f "$OUT" "$GD/Plugins/Lonapse.dll.tmp"
mv -f "$GD/Plugins/Lonapse.dll.tmp" "$GD/Plugins/Lonapse.dll"
echo "Installed: $GD/Plugins/Lonapse.dll ($(stat -c %s "$GD/Plugins/Lonapse.dll") bytes)"

if [[ -f "$GD/PluginData/settings.cfg" ]]; then
  echo "Kept existing user settings: $GD/PluginData/settings.cfg"
else
  echo "No settings.cfg yet, the plugin writes defaults on first start."
fi

# Sounds/ is owned by the audio pipeline (python3 audio/build_clips.py --install). build.sh never writes there.
if [[ $NO_SOUNDS -eq 0 && -d "$REPO/audio/build/Sounds" ]]; then
  echo "Note: audio/build/Sounds exists. Install it with: python3 audio/build_clips.py --install"
fi

n=0
if [[ -d "$GD/Sounds" ]]; then
  n=$(find "$GD/Sounds" -type f \( -name '*.wav' -o -name '*.ogg' \) | wc -l)
fi
echo "Installed sound files: $n"
echo "Done. After starting KSP, check KSP.log for lines starting with [Lonapse]."
