#!/usr/bin/env bash
# Build Lonapse and install it into the KSP instance: the DLL and every other file players get (the same files and
# sources package.sh puts in the zip). The runtime voice goes to PluginData/Runtime from audio/build/runtime/Runtime
# (./fetch_runtime.sh). The rest of PluginData (settings.cfg, played.txt) is never touched.
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

# ---- the rest of what players get, from the same sources as package.sh ----
# phrase tables: the text tables in GameData/Lonapse/Phrases (pools.tsv, ...) and Phrases/Runtime/*.tsv,
# phrases.tsv from the audio build when there is one. Phrases/Extra in the install is the player's own, not touched,
# and so is a file of the player's own in Phrases/Runtime (only the files the repo ships are replaced).
mkdir -p "$GD/Phrases" "$GD/Phrases/Runtime"
shopt -s nullglob
for f in "$REPO"/GameData/Lonapse/Phrases/*.tsv; do
  cp -f "$f" "$GD/Phrases/"
  echo "Installed: $GD/Phrases/$(basename "$f")"
done
for f in "$REPO"/GameData/Lonapse/Phrases/Runtime/*.tsv; do
  cp -f "$f" "$GD/Phrases/Runtime/"
  echo "Installed: $GD/Phrases/Runtime/$(basename "$f")"
done
# Module Manager patches (stock cores get ModuleLonapse). Optional for players: without MM they do nothing.
mkdir -p "$GD/Patches"
for f in "$REPO"/GameData/Lonapse/Patches/*.cfg; do
  cp -f "$f" "$GD/Patches/"
  echo "Installed: $GD/Patches/$(basename "$f")"
done
shopt -u nullglob
# 0.7.0 tables that moved: runtime.tsv is now Phrases/Runtime, slots_es.tsv is the spanish_rt column and pools.tsv
for old in runtime.tsv slots_es.tsv; do
  if [[ -f "$GD/Phrases/$old" ]]; then rm -f "$GD/Phrases/$old"; echo "Removed old: $GD/Phrases/$old"; fi
done
if [[ -f "$REPO/audio/build/lonapse/phrases.tsv" ]]; then
  cp -f "$REPO/audio/build/lonapse/phrases.tsv" "$GD/Phrases/phrases.tsv"
  echo "Installed: $GD/Phrases/phrases.tsv (from the audio build)"
fi

# docs and the version file, as in the zip
cp -f "$REPO/LICENSE" "$GD/LICENSE.txt"
cp -f "$REPO/LICENSE-CC-BY-4.0" "$GD/LICENSE-CC-BY-4.0.txt"
cp -f "$REPO/docs/LICENSING.md" "$GD/LICENSING.md"
cp -f "$REPO/README.md" "$REPO/CHANGELOG.md" "$GD/"
cp -f "$REPO/audio/CREDITS.md" "$GD/CREDITS.md"
cp -f "$REPO/GameData/Lonapse/Lonapse.version" "$GD/Lonapse.version"
echo "Installed: LICENSE.txt, LICENSE-CC-BY-4.0.txt, LICENSING.md, README.md, CHANGELOG.md, CREDITS.md, Lonapse.version"

# runtime voice: Piper and the voices, as in the zip. --delete only inside PluginData/Runtime.
RT_SRC="$REPO/audio/build/runtime/Runtime"
if [[ -f "$RT_SRC/windows/piper.exe" ]]; then
  case "$GD/PluginData/Runtime" in */GameData/Lonapse/PluginData/Runtime) ;; *) echo "ERROR: runtime path sanity check failed" >&2; exit 1 ;; esac
  mkdir -p "$GD/PluginData/Runtime"
  rsync -a --delete "$RT_SRC/" "$GD/PluginData/Runtime/"
  echo "Installed runtime voice from: $RT_SRC"
else
  echo "Runtime voice not installed: no $RT_SRC, run ./fetch_runtime.sh first"
fi

if [[ -f "$GD/PluginData/settings.cfg" ]]; then
  echo "Kept existing user settings: $GD/PluginData/settings.cfg"
else
  echo "No settings.cfg yet, the plugin writes defaults on first start."
fi

# sounds: audio/build/Sounds when it is complete (as package.sh), else the committed GameData/Lonapse/Sounds.
# Only audio files: a .cfg under GameData would be parsed by KSP. --delete removes clips that are no longer built.
if [[ $NO_SOUNDS -eq 0 ]]; then
  SND="$REPO/audio/build/Sounds"
  for d in beeps static crew control; do [[ -d "$SND/$d" ]] || SND="$REPO/GameData/Lonapse/Sounds"; done
  if [[ -d "$SND" ]]; then
    mkdir -p "$GD/Sounds"
    case "$GD/Sounds" in */GameData/Lonapse/Sounds) ;; *) echo "ERROR: sounds path sanity check failed" >&2; exit 1 ;; esac
    rsync -a --delete --prune-empty-dirs --include='*/' --include='*.wav' --include='*.ogg' --exclude='*' "$SND/" "$GD/Sounds/"
    echo "Installed sounds from: $SND"
  fi
else
  echo "Sounds skipped (--no-sounds)"
fi

n=0
if [[ -d "$GD/Sounds" ]]; then
  n=$(find "$GD/Sounds" -type f \( -name '*.wav' -o -name '*.ogg' \) | wc -l)
fi
echo "Installed sound files: $n"
echo "Done. After starting KSP, check KSP.log for lines starting with [Lonapse]."
