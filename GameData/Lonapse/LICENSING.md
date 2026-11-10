# Licensing

Lonapse has two licenses. The bundled Unicode data and runtime voice keep their own. The license files in the repo
root are [LICENSE](../LICENSE) (MIT), [LICENSE-CC-BY-4.0](../LICENSE-CC-BY-4.0) and
[LICENSE-GPL-3.0](../LICENSE-GPL-3.0), plus [LICENSE-UNICODE](../LICENSE-UNICODE) for the Chinese pronunciation data.
The GPL covers espeak-ng in the runtime voice, not the Lonapse code.

1. **The code** (except the generated Unicode data listed below, the files under `src/`, `tests/`, the build scripts and the Python scripts under `audio/`) is
   licensed under the MIT License, see [LICENSE](../LICENSE).

2. **The sound clips** (`GameData/Lonapse/Sounds/`) and **the phrase text** (`audio/lonapse/`,
   `GameData/Lonapse/Phrases/`) are licensed under the Creative Commons Attribution 4.0 International License
   (CC BY 4.0), see [LICENSE-CC-BY-4.0](../LICENSE-CC-BY-4.0). Credit: "Lonapse by D3rpyy".

3. **The runtime voice** in `GameData/Lonapse/PluginData/Runtime` (Piper, piper-phonemize, ONNX Runtime, espeak-ng
   and three voice models) is not part of Lonapse. Each part keeps its own license: MIT, GPL-3.0-or-later
   (espeak-ng, see [LICENSE-GPL-3.0](../LICENSE-GPL-3.0)), CC0, Apache-2.0 and the Unlicense. The texts and the source links are in
   `PluginData/Runtime/LICENSES` (in the repo: [runtime/licenses](../runtime/licenses/README.md)). Lonapse only
   starts Piper as a separate program. The source code of Piper, piper-phonemize and espeak-ng is on GitHub at
   the commits listed in [runtime/licenses](../runtime/licenses/README.md#source-code).

4. **Chinese pronunciation data** in `src/ChinesePronunciation.Data.cs` is derived from Unicode 17.0.0 Unihan
   `kMandarin` readings. Copyright © 2025 Unicode, Inc. Distributed under Unicode License v3, see
   [LICENSE-UNICODE](../LICENSE-UNICODE) (shipped as `LICENSE-UNICODE.txt`). The generator is
   `tools/build_chinese_pronunciation.py`. Source https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip.

Third-party credits for the TTS voices are in [audio/CREDITS.md](../audio/CREDITS.md).

The RPM glyph bundle in `GameData/Lonapse/Fonts` contains GNU Unifont 17.0.05 glyphs under the SIL Open Font
License 1.1 and Jigmo 20250912 glyphs under CC0 1.0. The modified subset is named Lonapse RPM Glyphs.
See the bundled `Fonts/README.md`, `Fonts/OFL-1.1.txt`, and `Fonts/Jigmo-CC0.txt` for sources and license notices.
