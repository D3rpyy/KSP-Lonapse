# Lonapse audio pipeline

Every sound in Lonapse is made by a script. Nothing is recorded, and nothing is taken from another mod or game. This
page explains how the beeps and static are built and how you can check or change them. The voices have their own page:
[lonapse/README.md](lonapse/README.md).

Two scripts build everything the plugin plays:

| Folder | Built by |
|---|---|
| `Sounds/beeps/`, `Sounds/static/` | `build_clips.py` (WAV, synthesized, no recordings) |
| `Sounds/crew/`, `Sounds/control/` | `build_lonapse.py` (loñapsE voices from Piper TTS, OGG) |

Both scripts write into `audio/build/Sounds/`, and that folder is what ships to the game. Each script only touches its
own folders, so you can rebuild the beeps without losing the voices and the other way around. `build_clips.py` only
needs Python 3 and `ffmpeg` with `ffprobe`.

## Building the beeps and static

```bash
python3 -I audio/build_clips.py                    # build and verify
python3 -I audio/build_clips.py --install          # build, verify, sync into the game
python3 -I audio/build_clips.py --clean --seed 1   # throw away audio/work and audio/build first
python3 -I audio/build_clips.py --verify-only      # re-check an existing build
```

| Flag | Effect |
|---|---|
| `--clean` | Delete `audio/work/`, `audio/build/manifest.json`, `Sounds/beeps` and `Sounds/static`. The voice folders are kept. |
| `--install` | After a successful build, sync the whole `audio/build/Sounds/` tree (WAV and OGG) into `GameData/Lonapse/Sounds/` and copy the phrase table to `GameData/Lonapse/Phrases/phrases.tsv`. Copies new or changed files and deletes stale files, only inside that folder. Aborts when `GameData/Squad` is missing, and when the voices are not built yet. The game folder comes from `KSP_DIR` or the first line of `.ksp_dir` in the repo root. |
| `--seed N` | Random seed (default 1). The same seed gives byte-identical WAV files. |

When it is done, the script prints a table per folder with the number of clips, the shortest and longest clip and
how loud they are. If anything is out of bounds it stops with an error, so a broken clip never makes it into the
game. It also writes
`audio/build/manifest.json` with file, folder, duration, rms and peak per clip.

## Checking the voices

There are thousands of voice clips, way too many to listen to one by one. `qa_lonapse.py` measures every clip
and picks a small sample for you to listen to.

```bash
python3 -I audio/qa_lonapse.py                       # checks audio/build
python3 -I audio/qa_lonapse.py --build DIR --seed 3  # another build folder, another sample
```

It reads `lonapse/phrases.tsv` and the OGG files under `Sounds/crew` and `Sounds/control`, decodes every
clip with ffmpeg and reports these as errors (exit code 1):

- a clip the table needs is missing, or an OGG is not in the table
- duration outside 0.3 to 3.0 s for a callout, 0.3 to 9 s for other lines
- seconds per letter of the loñapsE text outside 0.5 to 2.0 times the median of its voice and band
  (only for texts of 10 letters or more, screams excepted)
- peak above -1.0 dBFS or clipped samples
- a pause inside the clip longer than 0.8 s (1.0 s for panic)
- gated RMS more than 2 dB from the build target (-20 dBFS)
- identical decoded audio under two different clips

Tighter limits give warnings. All limits are in `LIMITS` at the top of the script.

Outputs, both under `<build>/lonapse/`:

- `qa_report.json`: every clip with its measurements, errors and warnings, and the medians per group.
- `review/index.html` and `review/review.m3u`: a random sample of 2 clips per source file, phase, band and
  role (`--per-stratum`, `--seed`), plus every flagged clip, plus every clip whose decoded audio changed
  since the previous `qa_report.json`. Each entry shows the key, the English text, the loñapsE text and
  the tags, with a player and OK and Bad buttons and a note field. The verdicts stay in the browser
  (localStorage). The Export button downloads them as `lonapse_verdicts.tsv`. Open the page from disk.
  The players use relative paths to `../../Sounds/`.

Keep in mind that the script only measures. A clip can pass every check and still sound wrong, so do listen to the
sample.

## Output

`audio/build/Sounds/<folder>/<folder with _>_NNN.wav`, WAV PCM 16-bit, mono, 44.1 kHz.

| Folder | Clips | What it holds |
|---|---|---|
| `beeps/ping` | 4 | Soft telemetry pings, 1 to 3 sine blips between 850 and 1350 Hz |
| `beeps/descent` | 3 | Single crisp blip near 1000 Hz |
| `beeps/stage` | 3 | Two-tone chirp, high then low (e.g. 1500 then 1000 Hz) |
| `beeps/landing` | 2 | Rising three-note touchdown tone |
| `beeps/splashdown` | 2 | Descending, warbled sweep |
| `beeps/quindar_in` | 1 | 2525 Hz, 0.25 s, 5 ms fades |
| `beeps/quindar_out` | 1 | 2475 Hz, 0.25 s, 5 ms fades |
| `static/burst` | 8 | Gated, band-passed crackle and hiss with pops |
| `static/loop` | 3 | 5 to 7 s radio hiss, start and end crossfaded so it loops without a click |
| `static/squelch` | 3 | Short squelch tail with a falling chirp |

Levels (gated RMS over 10 ms frames above max(-50 dBFS, loudest frame - 30 dB), as measured by ffmpeg
`astats`): beeps -22 dBFS, static -24 dBFS, all within 1 dB. Peaks stay at or below
-1.3 dBFS. Every clip except the loops has a 5 to 8 ms fade in and out and at most 50 ms of
leading or trailing silence.

## Making your own clips

Want to change a sound or add your own lines? Here is how.

- **Hand-made clips:** put WAV files (PCM 16-bit, mono, 44.1 kHz) into `GameData/Lonapse/Sounds/<folder>/`,
  named `<folder with _>_NNN.wav` with contiguous numbers from `000`. `--install` deletes files that are not in
  `audio/build/Sounds/`, so copy your clips there before installing. Aim for the same levels: about -20 dBFS RMS
  for voices, peaks under -1 dBFS.
- **New voice lines:** add rows to the matching file in `audio/lonapse/phrases/` (see `lonapse/README.md`).
  Check them with `python3 -I audio/build_lonapse.py --validate-only`, then build and run `qa_lonapse.py`.
  No code change is needed.
- **Tweaking beeps and static:** the `FOLDERS` table at the top of `build_clips.py`.
- Never put a `.cfg` file under `Sounds/`. KSP's GameDatabase would parse it.

## Radio profile clips

The radio sounds a bit different depending on where you are. A station or base has a desk microphone, a ship and
an EVA suit have a headset. These clips make that happen. They are synthesized in `build_clips.py` from plain tones
and noise.

| Folder | Clips | What it holds |
|---|---|---|
| `beeps/click_desk` | 3 | Desk-microphone click for the station profile. Band-passed white noise (1.8 to 3 kHz center, slightly random) with a fast exponential decay (tau 2.5 to 4.5 ms). 15 to 26 ms long, 5 ms fade in, 6 ms fade out. Set to -17 dBFS gated RMS, 5 dB hotter than the other beeps, because a click this short measures quiet at -22. |
| `beeps/headset_open` | 2 | Push-to-talk static for the ship and EVA profiles (key-up). White noise in a radio band of about 350 Hz to 3.4 kHz (two passes of each filter for a steeper edge), a random carrier flutter every 5 to 14 ms (depth about 35 %), and soft tanh saturation. Comes up in 4 ms, holds, falls away over the last 60 ms. 0.20 to 0.26 s. No tone, no clicks. |
| `beeps/headset_close` | 2 | The squelch tail (key-down). Same noise, slightly brighter (low-pass 4.2 kHz), starts at full level and decays over the whole clip. 0.16 to 0.22 s. |
| `static/breath_loop` | 2 | EVA helmet breathing, exactly 8.0 s, two breaths per loop. Inhale: pink noise in a narrow band near 1.9 kHz, 1.0 to 1.3 s, smooth bell envelope. A short hold. Exhale: wider band near 0.95 kHz, 1.4 to 1.8 s, quick onset and a long tail. Under it a faint suit-fan hiss (pink noise, 120 Hz to 1.4 kHz, crossfaded over 0.5 s at the seam). The mix gets a short double echo (6 and 11 ms) and a low-pass at 3 kHz, for a small closed helmet. The filters run over the loop's own tail first, so the seam has the same history as a wrap. |
| `beeps/roger` | 2 | Roger beep when the crew lets go of the key. One 1250 Hz tone of 110 ms, or a quick rising pair (1050 then 1400 Hz). |
| `beeps/master_alarm` | 1 | Soft caution loop for very high stress, exactly 2.0 s. 660 Hz and 880 Hz alternate every 0.5 s with 0.12 s raised-cosine crossfades, a faint second harmonic and a 4 Hz tremolo (0.6 to 1.0). Every part has a whole number of cycles in 2 s, so the seam is clean. The plugin plays it at `alarmVolume` 0.12. |

Levels: the beeps are at -22 dBFS gated RMS, except the desk click at -17 dBFS, and the breath bed is at
-30 dBFS. The bed is deliberately 6 dB quieter than `static/loop` (-24 dBFS), so it stays under the
voices and the static. The headset squelch sits at the same file level as the other beeps. Its softer feel comes from the runtime gain
(`evaRadioVolume` and the profile), not from a lower file level. All peaks stay below -1.3 dBFS.


## Probe beeps

Probes don't talk, they beep. Every stock probe core has its own beep voice, and every voice has seven moods.
`build_clips.py` makes all of them in plain Python.

| Folder | Clips | What it holds |
|---|---|---|
| `beeps/probe_<core>_<mood>` | 3 each | 6 cores x 7 moods = 42 folders, 126 clips. One beep phrase in the timbre of the core and the shape of the mood. |
| `beeps/probe_data` | 4 (min 3) | The data stream while an antenna sends science. 10 to 16 short even chirps on 4 pitches, generic timbre without glide (`make_probe_data`). |

The folder names are the same as `ProbeCores.Folders()` in `src/ProbeCore.cs`. `ClipLibrary` loads them, and an
empty core folder falls back to `beeps/probe_generic_<mood>`.

The timbre per core comes from `audio/probe/cores.tsv`. The columns are `key base_hz wave rhythm glide`:

| Column | Values | Meaning |
|---|---|---|
| `key` | `stayputnik okto hecs rovemate snob generic` | The core key from `ProbeCores.Keys`. Every key must be there once. |
| `base_hz` | 200 to 3000 | Pitch of note 0 of a mood. |
| `wave` | `sine triangle square saw pulse fm` | Band-limited additive waves (harmonics under 9 kHz). `pulse` has a 25 % duty cycle. `fm` is a bell-like tone (modulator at twice the carrier, index falls from 2.2 to 0.4). |
| `rhythm` | `even swing stutter lazy` | `swing` makes the notes long, short, long. `stutter` plays every note twice quickly. `lazy` makes notes 1.2 times and gaps 1.35 times longer. The scared trill ignores the rhythm. |
| `glide` | 0 to 1 | How far a note slides in from the pitch of the note before. 0 gives clean steps. |

The values I chose: stayputnik is a clean sine at 1320 Hz with no glide. Okto is a triangle at 880 Hz with
swing. HECS is a square at 700 Hz that stutters. RoveMate is a saw at 520 Hz, lazy, with a lot of glide.
Snob is the FM bell at 1050 Hz. Generic is a pulse at 940 Hz. The build stops when the file is wrong.

`src/ProbeSynth.cs` is a C# copy of this synth (0.6.0), so probe voices can also be made in the game.
`ProbeVoiceSpec.Stock()` holds the same values as `cores.tsv`, and the offline tests check that. After a change
to the beep synth or to `cores.tsv`, update the C# side and run `python3 -I audio/probe_reference.py`. It writes
`tests/probe_reference.tsv` with a fingerprint of every probe clip (random timing replaced by its midpoint), and
`tests/run.sh` compares the C# output against it.

The moods (`probe_mood_notes` in the script, offsets in semitones from `base_hz`):

| Mood | Shape |
|---|---|
| `happy` | 3 or 4 short chirps on a rising chord, every chirp slides up 3 semitones |
| `offended` | one octave down, two or three descending notes with a 38 Hz buzz and soft drive |
| `curious` | a short note, then a long rise of 7 semitones, like a question |
| `scared` | a fast trill of 8 to 11 notes, 5 semitones up, 34 ms per note |
| `sleepy` | two or three slow descending notes that get quieter, with a slow vibrato |
| `proud` | short, short, long. The long top note has a vibrato |
| `bored` | two to four flat notes, the last one sags a semitone |

The 3 clips of a folder differ in transposition (0, +2 and -2 semitones) and in the number of notes. Timing has a
small seeded jitter.

Levels: probe beeps -22 dBFS gated RMS, as the other beeps. Length window in `verify`: 0.08 to 1.8 s.
