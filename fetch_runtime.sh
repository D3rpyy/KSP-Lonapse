#!/usr/bin/env bash
# Runtime voice: puts together everything that ships in GameData/Lonapse/PluginData/Runtime, in
# audio/build/runtime/Runtime/. package.sh zips that folder and build.sh installs it.
#   windows/  Piper 1.2.0 for Windows (release 2023.11.14-2), unchanged, without the files Spanish does not need
#   linux/    the same for Linux x86-64. The libraries are stored under the name piper loads, without symlinks.
#   voices/   the three voices with int8 weights and a float32 decoder (audio/quantize_runtime_voices.py)
#   LICENSES/ runtime/licenses from the repo
# macOS: the Piper release for macOS has no .dylib files and cannot start, so there is no macos/ folder. Lonapse
# then plays the recorded lines only.
# Also downloads the source of Piper, piper-phonemize and espeak-ng at the pinned commits into
# audio/build/runtime_source/, zipped for the GitHub release (the GPL asks for the espeak-ng source).
# Every download and every shipped voice is checked against a pinned sha256.
# Usage: ./fetch_runtime.sh [--python <python with onnx and onnxruntime>]
#   The python is only needed when the voices in audio/build/runtime_voices are missing or differ.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO"
PY=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --python) PY="${2:?--python needs a path}"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

VERSION="$(sed -n 's/.*const string Version = "\([0-9.]*\)".*/\1/p' src/LonapseAddon.cs)"
DL="$REPO/audio/build/runtime_download"
OUT="$REPO/audio/build/runtime/Runtime"
SRC_OUT="$REPO/audio/build/runtime_source"
VOICES="$REPO/audio/build/runtime_voices"
PIPER="https://github.com/rhasspy/piper/releases/download/2023.11.14-2"

# name|url|sha256
DOWNLOADS=(
  "piper_windows_amd64.zip|$PIPER/piper_windows_amd64.zip|f3c58906402b24f3a96d92145f58acba6d86c9b5db896d207f78dc80811efcea"
  "piper_linux_x86_64.tar.gz|$PIPER/piper_linux_x86_64.tar.gz|a50cb45f355b7af1f6d758c1b360717877ba0a398cc8cbe6d2a7a3a26e225992"
)
# source archives: the sha256 is checked when it is filled in, else printed so it can be pinned
SOURCES=(
  "piper-2023.11.14-2.zip|https://github.com/rhasspy/piper/archive/refs/tags/2023.11.14-2.zip|"
  "piper-phonemize-fccd4f3.zip|https://github.com/rhasspy/piper-phonemize/archive/fccd4f335aa68ac0b72600822f34d84363daa2bf.zip|"
  "espeak-ng-0f65aa3.zip|https://github.com/rhasspy/espeak-ng/archive/0f65aa301e0d6bae5e172cc74197d32a6182200f.zip|"
)
# the voices as they ship (README, Runtime voice files)
declare -A VOICE_SHA=(
  [es_ES-davefx-medium]=558ede4ebfd7bcb1c9cec4026a8575fff386d98de3afd372e78b722c08d8e034
  [es_MX-claude-high]=d6c55bd42f7b00a54d1ab18ac849c7940283764359890155988011386cff65fe
  [es_MX-ald-medium]=07cfd143e350cc5e6400f2da0b98409b8a16ca4524e3b26c11900d709c573468
)

sha() { sha256sum "$1" | cut -d' ' -f1; }

fetch() { # name url sha dir
  local name="$1" url="$2" want="$3" dir="$4"
  mkdir -p "$dir"
  if [[ ! -f "$dir/$name" ]] || [[ -n "$want" && "$(sha "$dir/$name")" != "$want" ]]; then
    echo "Downloading $name"
    curl -sSfL "$url" -o "$dir/$name.part"
    mv -f "$dir/$name.part" "$dir/$name"
  fi
  local got; got="$(sha "$dir/$name")"
  if [[ -n "$want" && "$got" != "$want" ]]; then
    echo "ERROR: $name has sha256 $got, expected $want" >&2; exit 1
  fi
  [[ -n "$want" ]] && echo "Checked: $name" || echo "Downloaded: $name (sha256 $got, not pinned)"
}

for d in "${DOWNLOADS[@]}"; do IFS='|' read -r n u s <<<"$d"; fetch "$n" "$u" "$s" "$DL"; done

# ---- programs ----
case "$OUT" in */audio/build/runtime/Runtime) ;; *) echo "ERROR: output path sanity check failed" >&2; exit 1 ;; esac
rm -rf "$OUT"
mkdir -p "$OUT/windows" "$OUT/linux" "$OUT/voices" "$OUT/LICENSES"
TMP="$(mktemp -d "$REPO/audio/build/runtime_unpack.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

mkdir -p "$TMP/win" && unzip -q "$DL/piper_windows_amd64.zip" -d "$TMP/win"
for f in piper.exe piper_phonemize.dll espeak-ng.dll onnxruntime.dll onnxruntime_providers_shared.dll; do
  cp "$TMP/win/piper/$f" "$OUT/windows/"
done
cp -r "$TMP/win/piper/espeak-ng-data" "$OUT/windows/"

mkdir -p "$TMP/linux" && tar -xzf "$DL/piper_linux_x86_64.tar.gz" -C "$TMP/linux"
L="$TMP/linux/piper"
cp "$L/piper" "$OUT/linux/piper"
# the names piper asks for (readelf NEEDED), copied from the real files behind the symlinks
cp -L "$L/libespeak-ng.so.1" "$OUT/linux/libespeak-ng.so.1"
cp -L "$L/libpiper_phonemize.so.1" "$OUT/linux/libpiper_phonemize.so.1"
cp -L "$L/libonnxruntime.so.1.14.1" "$OUT/linux/libonnxruntime.so.1.14.1"
cp -r "$L/espeak-ng-data" "$OUT/linux/"
chmod 755 "$OUT/linux/piper"

# ---- voices ----
voices_ok() {
  local v
  for v in "${!VOICE_SHA[@]}"; do
    [[ -f "$VOICES/$v.onnx" && -f "$VOICES/$v.onnx.json" ]] || return 1
    [[ "$(sha "$VOICES/$v.onnx")" == "${VOICE_SHA[$v]}" ]] || return 1
  done
}
if ! voices_ok; then
  [[ -n "$PY" ]] || { echo "ERROR: $VOICES is missing or differs. Make it with --python <python with onnx>" >&2; exit 1; }
  "$PY" -I "$REPO/audio/quantize_runtime_voices.py" --out "$VOICES"
  voices_ok || { echo "ERROR: the new voices differ from the pinned sha256 (other onnx or onnxruntime version?)" >&2; exit 1; }
fi
for v in "${!VOICE_SHA[@]}"; do cp "$VOICES/$v.onnx" "$VOICES/$v.onnx.json" "$OUT/voices/"; done
echo "Checked: the three voices"

# ---- licences ----
cp "$REPO"/runtime/licenses/* "$OUT/LICENSES/"

# ---- source for the release ----
for d in "${SOURCES[@]}"; do IFS='|' read -r n u s <<<"$d"; fetch "$n" "$u" "$s" "$SRC_OUT/files"; done
cp "$REPO/runtime/licenses/README.md" "$SRC_OUT/files/README.md"
SZIP="$SRC_OUT/Lonapse-$VERSION-runtime-source.zip"
rm -f "$SZIP"
(cd "$SRC_OUT/files" && zip -q -X "$SZIP" ./*)
echo "Source for the GitHub release: $SZIP"

echo
echo "sha256 of the shipped program files and voices (README, Runtime voice files):"
(cd "$OUT" && find windows linux voices -maxdepth 1 -type f ! -name '*.json' | sort | while read -r f; do
  echo "  $(sha "$f")  $f"; done)
echo "Runtime voice ready: $OUT ($(du -sh "$OUT" | cut -f1))"
