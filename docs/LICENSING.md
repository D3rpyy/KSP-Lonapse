# Licensing

Lonapse has two licenses. The runtime voice that ships with it keeps its own. The three license files in the repo
root are [LICENSE](../LICENSE) (MIT), [LICENSE-CC-BY-4.0](../LICENSE-CC-BY-4.0) and
[LICENSE-GPL-3.0](../LICENSE-GPL-3.0). The GPL covers espeak-ng in the runtime voice, not the Lonapse code.

1. **The code** (everything under `src/`, `tests/`, the build scripts and the Python scripts under `audio/`) is
   licensed under the MIT License, see [LICENSE](../LICENSE).

2. **The sound clips** (`GameData/Lonapse/Sounds/`) and **the phrase text** (`audio/lonapse/`,
   `GameData/Lonapse/Phrases/`) are licensed under the Creative Commons Attribution 4.0 International License
   (CC BY 4.0), see [LICENSE-CC-BY-4.0](../LICENSE-CC-BY-4.0). Credit: "Lonapse by D3rpyy".

3. **The runtime voice** in `GameData/Lonapse/PluginData/Runtime` (Piper, piper-phonemize, ONNX Runtime, espeak-ng
   and three voice models) is not part of Lonapse. Each part keeps its own license: MIT, GPL-3.0-or-later
   (espeak-ng, see [LICENSE-GPL-3.0](../LICENSE-GPL-3.0)), CC0, Apache-2.0 and the Unlicense. The texts and the source links are in
   `PluginData/Runtime/LICENSES` (in the repo: [runtime/licenses](../runtime/licenses/README.md)). Lonapse only
   starts Piper as a separate program. The source code of Piper, piper-phonemize and espeak-ng is attached to each
   GitHub release as `Lonapse-<version>-runtime-source.zip`.

Third-party credits for the TTS voices are in [audio/CREDITS.md](../audio/CREDITS.md).
