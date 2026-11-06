# Runtime voice: licences and sources

These files ship in `GameData/Lonapse/PluginData/Runtime/LICENSES`. They cover the programs and voices in
`PluginData/Runtime`, which Lonapse starts as a separate program. Lonapse itself is MIT (code) and CC BY 4.0
(sounds and phrase text), see `LICENSE.txt` in the Lonapse folder.

| Part | Files in Runtime | License | Text here |
|---|---|---|---|
| Piper 1.2.0 | `piper.exe`, `piper` | MIT | `piper-LICENSE.md` |
| piper-phonemize | `piper_phonemize.dll`, `libpiper_phonemize.so.1` | MIT | `piper-phonemize-LICENSE.md` |
| ONNX Runtime 1.14.1 | `onnxruntime*.dll`, `libonnxruntime.so.1.14.1` | MIT | `onnxruntime-LICENSE.txt`, `onnxruntime-ThirdPartyNotices.txt` |
| espeak-ng 1.52 | `espeak-ng.dll`, `libespeak-ng.so.1`, `espeak-ng-data/` | GPL-3.0-or-later, some parts Apache-2.0, BSD-2-Clause and the Unicode licence | `espeak-ng-COPYING*.txt` |
| Voice `es_ES-davefx-medium` | `voices/es_ES-davefx-medium.onnx(.json)` | CC0 | `CC0-1.0.txt`, `es_ES-davefx-medium-MODEL_CARD.md` |
| Voice `es_MX-claude-high` | `voices/es_MX-claude-high.onnx(.json)` | Apache-2.0 | `Apache-2.0.txt`, `es_MX-claude-high-MODEL_CARD.md` |
| Voice `es_MX-ald-medium` | `voices/es_MX-ald-medium.onnx(.json)` | Unlicense | `Unlicense.txt`, `es_MX-ald-medium-MODEL_CARD.md` |

## Where the files come from

- The program files are unchanged from the Piper release
  [2023.11.14-2](https://github.com/rhasspy/piper/releases/tag/2023.11.14-2) (`piper_windows_amd64.zip`,
  `piper_linux_x86_64.tar.gz`). Files not needed for Spanish are left out (`libtashkeel_model.ort`, `pkgconfig`,
  the `espeak-ng` and `piper_phonemize` command line tools). On Linux the libraries are stored under the name the
  program loads, without the symlinks of the release.
- The voices come from [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices) v1.0.0. They are
  changed: the weights are quantized to int8 with ONNX Runtime `quantize_dynamic` (QUInt8), and the decoder is kept
  at float32. The script is `audio/quantize_runtime_voices.py` in the Lonapse repo. The `.onnx.json` files are
  unchanged. The model cards state the licence of the training dataset and no separate licence for the weights.

## Source code

- Piper: https://github.com/rhasspy/piper/tree/2023.11.14-2
- piper-phonemize: https://github.com/rhasspy/piper-phonemize/tree/fccd4f335aa68ac0b72600822f34d84363daa2bf
- espeak-ng (the rhasspy fork that piper-phonemize builds):
  https://github.com/rhasspy/espeak-ng/tree/0f65aa301e0d6bae5e172cc74197d32a6182200f
- ONNX Runtime: https://github.com/microsoft/onnxruntime/tree/v1.14.1

The Piper release does not record the exact espeak-ng and piper-phonemize commits. Its build fetched the `master`
branch of both. The commits above are the last ones on `master` before the Piper release was built
(14 November 2023). A copy of these sources is attached to every Lonapse GitHub release that ships the runtime
voice, as `Lonapse-<version>-runtime-source.zip`.
