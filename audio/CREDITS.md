# Audio credits

The voice clips in `Sounds/crew/` and `Sounds/control/` are loñapsE lines spoken by Piper voices (below).

Beeps (`Sounds/beeps/`) and static (`Sounds/static/`) are synthesized by `build_clips.py` with
ffmpeg's `sine`, `aevalsrc` and `anoisesrc` sources and plain Python. They contain no recorded material.

No audio from the Chatterer mod or any other mod was used.

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
The voice models themselves are not shipped, only the audio rendered with them.

Piper phonemizes through espeak-ng, so espeak-ng (GPL-3.0-or-later) is also part of the pipeline. Whether
audio produced by a GPL program falls under the GPL is a legal question this project has not settled. The
project distributes the clips with this note.

The phrase text is written by the project itself, in plain Spanish, and is reversed letter by letter by the
build script. No recorded material is used in the voice clips.
