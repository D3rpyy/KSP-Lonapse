# Audio credits

The voice clips in `Sounds/crew/` and `Sounds/control/` are loñapsE lines spoken by Piper voices (below).

Beeps (`Sounds/beeps/`) and the radio static and breathing in `Sounds/sfx/` (`burst`, `loop`, `squelch`,
`breath_loop`) are synthesized by `build_clips.py` with ffmpeg's `sine`, `aevalsrc` and `anoisesrc` sources and
plain Python. They contain no recorded material.

The docking effects (`Sounds/sfx/airlock_dock`, `airlock_undock`, `hatch_open`, `hatch_close`) and the flag plant (`flag_plant`) are made by `build_sfx.py` from the
recordings in `audio/sfx_sources/`. The source and licence of those recordings are not written down here yet.

The sphere of influence ping (`Sounds/sfx/soi_change`) is synthesized by `build_sfx.py` from sine notes. It contains no
recorded material.

No audio from the Chatterer mod or any other mod was used.

## Runtime voice

Lonapse ships a speech engine in `GameData/Lonapse/PluginData/Runtime` that speaks lines while you play. It is not
part of Lonapse. Each part keeps its own licence. The texts are in `PluginData/Runtime/LICENSES` (in the repo:
`runtime/licenses`).

| Part | Author | License |
|---|---|---|
| [Piper](https://github.com/rhasspy/piper) 1.2.0 (release 2023.11.14-2) | Michael Hansen | MIT |
| [piper-phonemize](https://github.com/rhasspy/piper-phonemize) | Michael Hansen | MIT |
| [espeak-ng](https://github.com/espeak-ng/espeak-ng) 1.52 (rhasspy fork) | the espeak-ng contributors | GPL-3.0-or-later |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) 1.14.1 | Microsoft | MIT |
| The three voices below | see the table below | CC0, Apache-2.0, Unlicense |

The program files are unchanged from the Piper release. The voices are changed: their weights are quantized to int8
and the decoder is kept at float32 (`audio/quantize_runtime_voices.py`). The source code of Piper, piper-phonemize
and espeak-ng is on GitHub at the commits listed in [runtime/licenses](../runtime/licenses/README.md#source-code).

## Lonapse voices: Piper

The loñapsE voice clips (`audio/build_lonapse.py`, phrase table in `audio/lonapse/`) are spoken by Piper
(`piper-tts` 1.8.0, https://github.com/OHF-Voice/piper1-gpl, GPL-3.0-or-later) with these voice models from
https://huggingface.co/rhasspy/piper-voices. The license of each voice is the one stated on its model card
(`MODEL_CARD` in the voice folder), checked on 2026-11-02:

| Voice | Used for | Model card license | sha256 of the .onnx |
|---|---|---|---|
| `es_ES-davefx-medium` | male crew | CC0 | `6658b03b1a6c316ee4c265a9896abc1393353c2d9e1bca7d66c2c442e222a917` |
| `es_MX-claude-high` | female crew | Apache-2.0 | `3ef40a71ea63852cd8ab7e6fa7d2ecdcfa67a0b47c9c48e3f10e02ee02083ea0` |
| `es_MX-ald-medium` | Mission Control | Unlicense | `019b3803293c93e34a206dd2e53a3889209a514e786fd7144f7b70196c579b63` |

The model cards state the license of the training dataset. They do not state a separate license for the
model weights. All three are permissive, and the Apache-2.0 voice needs this attribution, which this file provides.
The recorded clips are rendered with these models. Changed copies of the same three models ship in
`GameData/Lonapse/PluginData/Runtime/voices` for the runtime voice. The change: the weights are quantized to int8
with ONNX Runtime `quantize_dynamic` (QUInt8), and the decoder is kept at float32. See the README, Runtime voice files.

Piper phonemizes through espeak-ng, so espeak-ng (GPL-3.0-or-later) is also part of the pipeline. Whether
audio produced by a GPL program falls under the GPL is a legal question this project has not settled. The
project distributes the clips with this note.

The phrase text is written by the project itself, in plain Spanish, and is reversed letter by letter by the
build script. No recorded material is used in the voice clips.
