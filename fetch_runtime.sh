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
# Usage: ./fetch_runtime.sh [--python <python with runtime/requirements.txt>]
#   The python is only needed when the voices in audio/build/runtime_voices are missing or differ. They are then made
#   from the original voices in audio/voices, which are downloaded from Hugging Face (pinned sha256) when missing.
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

# the .onnx.json of each voice as it ships. The ald config on Hugging Face says "PhonemeType.ESPEAK", a known
# export mistake: it is fixed to "espeak" like in the other two voices.
declare -A JSON_SHA=(
  [es_ES-davefx-medium]=0e0dda87c732f6f38771ff274a6380d9252f327dca77aa2963d5fbdf9ec54842
  [es_MX-claude-high]=1afc81f703c0e4cb3b4d7c0dca096b8b54a98806807f0170cf5eb5557723c12d
  [es_MX-ald-medium]=5a71498158e04afc8099bfd019c7e87c68eb9d042505a2b1a87e5c1ac2b1a61d
)
# the original voices (audio/CREDITS.md), only downloaded when the runtime voices have to be made
HF="https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/es"
ORIGINALS=(
  "es_ES-davefx-medium|$HF/es_ES/davefx/medium|6658b03b1a6c316ee4c265a9896abc1393353c2d9e1bca7d66c2c442e222a917"
  "es_MX-claude-high|$HF/es_MX/claude/high|3ef40a71ea63852cd8ab7e6fa7d2ecdcfa67a0b47c9c48e3f10e02ee02083ea0"
  "es_MX-ald-medium|$HF/es_MX/ald/medium|019b3803293c93e34a206dd2e53a3889209a514e786fd7144f7b70196c579b63"
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
    [[ "$(sha "$VOICES/$v.onnx.json")" == "${JSON_SHA[$v]}" ]] || return 1
  done
}
if ! voices_ok; then
  [[ -n "$PY" ]] || { echo "ERROR: $VOICES is missing or differs. Make it with --python <python with onnx>" >&2; exit 1; }
  for o in "${ORIGINALS[@]}"; do
    IFS='|' read -r n u s <<<"$o"
    fetch "$n.onnx" "$u/$n.onnx" "$s" "$REPO/audio/voices"
    j="$REPO/audio/voices/$n.onnx.json"
    [[ -f "$j" ]] || fetch "$n.onnx.json" "$u/$n.onnx.json" "" "$REPO/audio/voices"
    if grep -q '"phoneme_type": "PhonemeType.ESPEAK"' "$j"; then
      sed -i 's/"phoneme_type": "PhonemeType.ESPEAK"/"phoneme_type": "espeak"/' "$j"
      [[ -z "$(tail -c1 "$j")" ]] || echo >> "$j"
    fi
    [[ "$(sha "$j")" == "${JSON_SHA[$n]}" ]] || { echo "ERROR: $j has sha256 $(sha "$j"), expected ${JSON_SHA[$n]}" >&2; exit 1; }
  done
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
