#!/usr/bin/env bash
# Installs the in-game API test: a made-up mod in GameData/ExampleMod with the example lines, a test DLL that calls
# Lonapse only through the wrapper, a test experiment, a soil probe part and a drill rig part (Module Manager).
# It only touches GameData/ExampleMod. Lonapse itself is installed by ./build.sh as usual.
# Usage: tests/ingame/install.sh [--compile-only] [--remove]
# The KSP install comes from $KSP_DIR, or from the first line of .ksp_dir in the repo root.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$REPO/tests/ingame"
if [[ -z "${KSP_DIR:-}" && -f "$REPO/.ksp_dir" ]]; then
  KSP_DIR="$(grep -m1 -v '^[[:space:]]*$' "$REPO/.ksp_dir" | sed 's/[[:space:]]*$//')"
fi
[[ -n "${KSP_DIR:-}" ]] || { echo "ERROR: no KSP install given. Set KSP_DIR or write its path into $REPO/.ksp_dir" >&2; exit 1; }

COMPILE_ONLY=0
REMOVE=0
for arg in "$@"; do
  case "$arg" in
    --compile-only) COMPILE_ONLY=1 ;;
    --remove) REMOVE=1 ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

GD="$KSP_DIR/GameData/ExampleMod"
case "$GD" in */GameData/ExampleMod) ;; *) echo "ERROR: path sanity check failed: $GD" >&2; exit 1 ;; esac

if pgrep -f "KSP_x64.exe" >/dev/null 2>&1 || pgrep -x "KSP.x86_64" >/dev/null 2>&1; then
  if [[ $COMPILE_ONLY -eq 0 ]]; then echo "ERROR: KSP is running. Close it first, or use --compile-only." >&2; exit 1; fi
fi

if [[ $REMOVE -eq 1 ]]; then
  if [[ -d "$GD" ]]; then rm -rf -- "$GD"; echo "Removed: $GD"; else echo "Not installed: $GD"; fi
  exit 0
fi

MG="$KSP_DIR/KSP_Data/Managed"
[[ -f "$MG/Assembly-CSharp.dll" ]] || { echo "ERROR: Assembly-CSharp.dll not found in: $MG" >&2; exit 1; }
command -v mcs >/dev/null || { echo "ERROR: mcs not found (install mono)" >&2; exit 1; }
refs=()
for a in Assembly-CSharp Assembly-CSharp-firstpass UnityEngine UnityEngine.CoreModule UnityEngine.IMGUIModule \
         UnityEngine.InputLegacyModule UnityEngine.TextRenderingModule; do
  refs+=("-r:$MG/$a.dll")
done

# the wrapper as a modder uses it: copied, with the namespace of the mod. No reference to Lonapse.dll.
OUT="$REPO/build/ingame"
mkdir -p "$OUT"
sed 's/^namespace YourMod$/namespace ExampleMod/' "$REPO/docs/api/LonapseWrapper.cs" > "$OUT/LonapseWrapper.cs"
grep -q '^namespace ExampleMod$' "$OUT/LonapseWrapper.cs" || { echo "ERROR: wrapper namespace not found" >&2; exit 1; }
mcs -target:library -nostdlib -noconfig \
    -r:"$MG/mscorlib.dll" -r:"$MG/System.dll" -r:"$MG/System.Core.dll" "${refs[@]}" \
    -warn:4 -out:"$OUT/ExampleMod.dll" "$HERE/ExampleModTest.cs" "$OUT/LonapseWrapper.cs"
echo "Compiled: $OUT/ExampleMod.dll (no reference to Lonapse.dll)"

if [[ $COMPILE_ONLY -eq 1 ]]; then echo "Compile only, nothing installed."; exit 0; fi

[[ -d "$KSP_DIR/GameData/Squad" ]] || { echo "ERROR: $KSP_DIR/GameData/Squad not found, refusing to install" >&2; exit 1; }
[[ -d "$KSP_DIR/GameData/Lonapse" ]] || echo "Note: Lonapse is not installed yet, run ./build.sh"
ls "$KSP_DIR"/GameData/ModuleManager*.dll >/dev/null 2>&1 || echo "Note: no Module Manager, the soil probe and the drill rig will be missing"
[[ -d "$KSP_DIR/GameData/SquadExpansion/Serenity" ]] || echo "Note: no Breaking Ground, the drill rig will be missing"

mkdir -p "$GD/Plugins" "$GD/Lonapse"
cp -f "$OUT/ExampleMod.dll" "$GD/Plugins/ExampleMod.dll"
cp -f "$REPO/docs/api/example_mod.tsv" "$GD/Lonapse/example_mod.tsv"
cp -f "$HERE/ExampleMod.cfg" "$GD/ExampleMod.cfg"
rm -f "$GD/Lonapse/reload_test.tsv"
echo "Installed: $GD (Plugins/ExampleMod.dll, Lonapse/example_mod.tsv, ExampleMod.cfg)"
echo "The test steps are in tests/ingame/CHECKLIST.md. Remove it again with: tests/ingame/install.sh --remove"
