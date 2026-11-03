# Lonapse: sources, build and tables

Lonapse is the language of the kerbals. Every crew and Mission Control line in the mod is spoken in it. This file describes how the Lonapse clips are made. Lonapse is reversed spelling only. There is no audio reversal in this build.

## What is in this folder

| File | Role |
|---|---|
| `phrases/*.tsv` | The source tables, one file per event family. I edit them by hand. |
| `bodies.tsv` | Body names for the `{body}` placeholder. |
| `README.md` | This file. |

The source files are:

| File | Contents | Key prefix for new rows |
|---|---|---|
| `phrases/launch.tsv` | Staging, throttle and countdown callouts. Serious launch and ascent radio. | `la_` |
| `phrases/return.tsv` | Reentry and descent radio, chutes, touchdown, splashdown. | `re_` |
| `phrases/panic.tsv` | Stress radio in the `tense` and `panic` bands, one cause per scene. | `pa_` |
| `phrases/space_radio.tsv` | Goofy kerbal radio with Mission Control in orbit, coast and landed. | `sp_` |
| `phrases/mc_jokes.tsv` | Mission Control jokes to the crew. | `mj_` |
| `phrases/intercom.tsv` | Crew-only scenes with two or more lines. Always funny. | `ic_` |
| `phrases/blackout.tsv` | Intercom scenes for the radio blackout (phase `blackout`). | `bl_` |
| `phrases/events.tsv` | Docking, relief, maneuver nodes, station and EVA checks. | `ev_` |
| `phrases/bodies.tsv` | Scenes that name the current body with `{body}`. | `bo_` |
| `phrases/probe_pet.tsv` | v5: Mission Control talks to the probe, the probe beeps back (`probe_chat`, `probe_sleepy`). v6: the probe calls first (`probe_hail`), stress per cause (`probe_stress`), "say again?" (`probe_say_again`), milestones (`probe_milestone`), rover moments (`probe_rover`) and data (`probe_data`). | `pp_` |
| `phrases/probe_events.tsv` | v5: probe scenes for science, power, SOI, part lost and signal back. v6: also touchdown, splashdown, docked and burn_soon. | `pe_` |

Rows that came from the old `phrases_es.tsv` kept their old keys. Only new rows use the prefixes.

The build script is `audio/build_lonapse.py`. The clips go into the shared folder `audio/build/Sounds/`. The built table, the manifest and the render cache go into `audio/build/lonapse/`. Both paths are in `.gitignore`.

## The idea

Lonapse is Spanish with the letters of every word reversed. The script does this:

1. It normalizes the text to NFC, so an ñ is always one character.
2. It reverses the letters of every word. Punctuation and spaces stay in place. The word order stays the same.
3. Piper (a neural TTS) speaks the reversed text with a Spanish voice.
4. ffmpeg shifts the voice up into the Kerbal register. Then come trim, fades and level.

Examples: "hola" becomes "aloh". "Español" becomes "loñapsE". "¿Qué tal?" becomes "¿éuQ lat?". A capital letter moves with its letter.

A lowercase letter stays lowercase. The word "español" with a lowercase e becomes "loñapse".

An accent stays on its own letter. "Separación" becomes "nóicarapeS".

## Table format

Every file in `phrases/` is UTF-8 with tabs. Lines that start with `#` are comments. The first other line is the header. The script and the C# loader find the columns by name, so the order does not matter. I keep this order:

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	spanish	english
```

Three more columns are optional (plan v4): `trait`, `persona` and `topic`. A file without them still loads, and every row then gets `any`. An empty cell also means `any`. A file that uses them puts them after `cause`:

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	trait	persona	topic	spanish	english
```

Plan v5 adds two more optional columns: `core` and `beep`. Without `core` (or with an empty cell) a row gets `any`. Without `beep` a row gets an empty beep. A probe file puts them after `topic` (or after `cause`):

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	core	beep	spanish	english
```

| Column | Allowed values |
|---|---|
| `clip_key` | `[a-z0-9_]+`, unique across all files. No `__` (that is for body variants). The file name becomes `<clip_key>_<m|f>.ogg`. |
| `event` | `chatter`, `staging`, `chute_semi`, `chute_full`, `throttle_full`, `throttle_cut`, `dock_approach`, `dock_close`, `dock_hard`, `docked`, `touchdown`, `splashdown`, `relief`, `plan_node`, `burn_soon`, `station_check`, `eva_check`, `countdown`, since v4 `soi_enter`, `science`, `part_lost`, `power_low`, `power_ok`, `signal_lost`, `signal_back`, `crew_lost`, since v5 the probe events `probe_chat`, `probe_sleepy`, and since v6 `probe_hail`, `probe_stress`, `probe_say_again`, `probe_milestone`, `probe_rover`, `probe_data` |
| `severity` | `chatter`, `callout`, `warning` |
| `band` | `calm`, `tense`, `panic`, `none` |
| `role` | `crew`, `mc`, and since v5 `probe` (see "Probe mode" below) |
| `scene` | `[a-z0-9_]+`. The lines of one conversation share the scene. A scene lives in one file. |
| `seq` | integer, 1 or higher. The seq numbers of a scene run 1, 2, 3 without gaps. |
| `gender` | `m`, `f`, `both` |
| `phase` | `any`, `prelaunch`, `launch`, `ascent`, `orbit`, `coast`, `reentry`, `descent`, `landed`, `blackout`, `serious`, `goofy` |
| `cause` | `any`, `spin`, `gforce`, `q`, `ground`, `heat`, `plasma`, `fuel`, `impulse`, `engine_out` |
| `trait` | optional. `any`, `pilot`, `engineer`, `scientist`, `tourist`. The crew speaker must have this trait. |
| `persona` | optional. `any`, `badass`, `brave`, `nervous`, `dim`, `veteran`. The crew speaker must have this persona. |
| `core` | optional (v5). `any`, `stayputnik`, `okto`, `hecs`, `rovemate`, `snob`, `generic` (the keys of `ProbeCores.Keys`). Holds for the whole scene. |
| `beep` | optional (v5). The beep mood of a `probe` row: `happy`, `offended`, `curious`, `scared`, `sleepy`, `proud`, `bored`. Required on probe rows, empty on every other row. |
| `topic` | optional. `any` on every event except two. For `science` also the experiment ids `crewreport`, `evareport`, `mysterygoo`, `surfacesample`, `mobilematerialslab`, `temperaturescan`, `barometerscan`, `seismicscan`, `gravityscan`, `atmosphereanalysis`, `asteroidsample`, `cometsample`, `infraredtelescope`, `magnetometer`, `roc`. For `soi_enter` also `home` (the home body). For `chatter`, `power_low` and `power_ok` also `solar`: the scene talks about solar panels and only plays on a ship that has one. |
| `spanish` | the Spanish source, not empty. May hold `{body}`. Empty on the subtitle-only role `probe`. |
| `english` | the English subtitle, not empty. In game the subtitles show only this text. Holds `{body}` when `spanish` does. |

`phase` says when a scene may play. `serious` stands for launch, ascent, reentry and descent. `goofy` stands for prelaunch, orbit, coast and landed. `any` plays in every phase. The code that decides the current phase is `src/FlightPhase.cs`.

`cause` says which stress component the line is about. The names are the components in `src/StressModel.cs`, plus `engine_out`. Use `any` for a line that fits every cause. A tense or panic line only names what the game measures. So no fire: the game has no fire. Use heat instead.

Rules the build enforces:

- All rows of one scene have the same `event`, `severity`, `band`, `phase`, `cause`, `trait`, `persona`, `topic` and `core`.
- `topic` is `any` except on `science` and `soi_enter` (see the table above).
- Explosion words only appear in `part_lost` rows. The check looks at `spanish` and `english`, case-insensitive, at the start of a word: `explo` (but not `explor`, so "explore" is fine), `boom`, `blast` (but not "blast off"), `explot` and `estall`. `blew` only counts as "blew up", "blew apart", "blew out" or "blew off". So "the intern blew away" in `mc_jokes.tsv` passes. The same rule is in `EXPLOSION_RE` in the script and in `PhraseTable.HasExplosionWord` in C#.
- A scene id is unique across files.
- An `mc` row has one voice. So `gender` is `m` or `both`, never `f`.
- `gender m` makes only the male file. `gender f` makes only the female file. `gender both` makes both.
- Every event has at least 2 rows. For `chatter` I need at least 3 rows per band (`calm`, `tense`, `panic`). For the new v4 events a shortfall is only a warning (`WARN table:`) until their rows are written.
- An intercom scene is never in a serious phase (`serious`, `launch`, `ascent`, `reentry`, `descent`).
- `{body}` is the only placeholder. It is in both `spanish` and `english`, or in neither, at most once per text. A `probe` row cannot hold it (see the subtitle rule below).
- The v5 rules in "Probe mode" below.

The C# loader (`PhraseTable.Parse`) checks the same v5 rules and the same placeholder rules as the script, with the same messages. The script checks more (scene in one file, seq 1..n, minimum counts, intercom phase). I compared both on a test table with 9 good and 17 bad v5 rows. They accepted and rejected the same rows.

## Probe mode (plan v5 and v6)

Probe mode plays when a vessel has no crew but a probe core. Its scenes use the roles below. Since v6 only Mission Control and the probe talk. The v5 room roles (`flight`, `intern`, `coffee`), `room.tsv`, `room_chat` and the bet events are gone.

| Role | Sound | Clip | Rules |
|---|---|---|---|
| `probe` | beeps from `beeps/probe_<core>_<beep>` (built by `build_clips.py`) | none, subtitle only | `spanish` empty, `english` required, short like "[beeps excitedly]". `beep` required. |

`mc` stays Mission Control on the radio. `crew` has no place in probe scenes.

The `gender` of a subtitle-only row does not matter. I write `both`.

Rules the build and the C# loader enforce:

- The probe events are `probe_chat`, `probe_sleepy`, `probe_hail`, `probe_stress`, `probe_say_again`, `probe_milestone`, `probe_rover` and `probe_data`. They have no `crew` rows. A `probe_hail` or `probe_data` scene starts with a `probe` row (the director only picks those).
- v6: a `probe_say_again` scene has exactly 2 rows, `mc` then `probe`. The director plays the MC line and then repeats the probe beep with that subtitle.
- v6: `probe_stress` uses the `cause` column. The director tries the main stress cause first, then `cause any`.
- v6: topics. `staging` has `separation` and `ignition`, `probe_milestone` has `day1`, `day10`, `day100`, `day365`, `orbit` and `landing`, `probe_rover` has `fast`, `air`, `tilt` and `idle`. A milestone needs its exact topic.
- v6: the probe callouts (`staging`, `chute_semi`, `chute_full`, `throttle_full`, `throttle_cut`) only pick scenes that start with a `probe` row. The MC rows after it play now and then, not always.
- v6: a `probe` subtitle is `[beeps` plus 1 to 3 lowercase words and `]`, for example "[beeps excitedly]", "[beeps very slowly]" or "[beeps a bit louder]". No commas, no quotes, no second clause. The plugin uses the same form when it overrides a mood (for example "[beeps nervously]" under stress). The check is `PROBE_SUBTITLE_RE` in the script and `PhraseTable.ProbeSubtitleOk` in C#.
- The probe role (`probe`) and a `core` other than `any` are only allowed in the probe events and in the probe variants of `science`, `power_low`, `power_ok`, `soi_enter`, `part_lost`, `signal_lost`, `signal_back`, `touchdown`, `splashdown`, `docked`, `burn_soon` and since v6 `staging`, `chute_semi`, `chute_full`, `throttle_full`, `throttle_cut` and `plan_node`.
- `signal_lost` has no probe rows. Mission Control cannot reach the probe, so nobody talks on the radio.
- A `probe` row needs a `beep` mood. Every other row has an empty `beep`.

A scene is a probe scene when its `core` is not `any`, when one of its lines has a probe role, or when its event is a probe event. The helper is `PhraseTable.IsProbeScene` in C# and `is_probe_scene` in the script. Crew mode never picks a probe scene. Probe mode only picks probe scenes. So a probe version of `science` or `power_ok` needs a `core` or a probe role. A scene with only `mc` rows and `core any` is a crew scene.

`core any` fits every probe core. A scene with a core only plays for that core. `PhraseTable.ProbeScenesFor(event, core, phase, coreExact, filter)` gives the scenes. With `coreExact` it only gives the scenes of exactly that core, so the director can try the core first and then fall back to `any`.

A shortfall in any probe event is only a warning.

`trait`, `persona` and `topic` hold for the whole scene. In game `any` matches every speaker and every topic. For `topic` the plugin looks for an exact match first (`PhraseTable.TopicMatchesExact`). Whether it then falls back to `any` scenes (`PhraseTable.TopicMatches`) is the caller's choice. The plan says an unknown experiment falls back to `any`.

A scene with only `crew` rows and more than one row is an intercom talk between crew members. A scene with `crew` and `mc` is a radio exchange with Mission Control. In a radio exchange one crew member talks. The plugin takes care of that.

## `{body}` and bodies.tsv

A row with `{body}` becomes one row per line of `bodies.tsv`. That file has the columns `key spanish english`. It holds the 17 stock bodies and `generic`. The key is the lowercase `bodyName` (`kerbin`, `mun`, ...). `generic` is for every other body: "el planeta" and "the planet".

The variant gets the clip key `<clip_key>__<body key>` and the body key in the column `body`. Example: `chat_calm_s4_2` becomes `chat_calm_s4_2__kerbin`, `chat_calm_s4_2__mun` and so on. A row without `{body}` in the same scene stays one row with an empty `body`.

The Spanish body names are written backwards on purpose. Lonapse reverses every word, so the clip then says the real name. "nibréK" is spoken as "Kérbin". I spelled the names for a Spanish voice: Jool is "luY" (spoken "Yul"), Eeloo is "ulI" ("Ilu"), Laythe is "ziéL" ("Léiz"). Only `generic` is plain Spanish ("el planeta"). It is spoken as Lonapse like every other word. The English names carry "the" where English needs it ("the Mun"). If `{body}` starts an English sentence, the build makes the first letter a capital.

So write `{body}` where a name fits in Spanish and in English. "Qué vista sobre {body}." works for "Kerbin", "la Mun" and "el planeta". Avoid a word before `{body}` that changes with the name, like "del" or "de la".

In game a body scene counts as one scene. `PhraseScene.LinesFor(body)` gives the lines for one body, and falls back to `generic`. Every body variant is a real clip, so one `{body}` row with `gender both` gives 36 clips.

## Adding rows

1. Pick the file by event family (table above). Use its key prefix: `la_`, `re_`, `pa_`, `sp_`, `mj_`, `ic_`, `bl_`, `ev_`, `bo_`.
2. Give the lines of one exchange the same `scene` and number them with `seq` from 1.
3. Fill `phase` and `cause`. Serious work (launch, ascent, reentry, descent) is short and procedural. Orbit, coast and landed are goofy. Intercom is always goofy or `blackout`.
4. Check the tables without rendering:

```bash
python3 -I audio/build_lonapse.py --validate-only
```

It prints the rows per file, per phase and per cause, and every bad row as `phrases/<file>:<line>`.

5. Build. Only new or changed clips are rendered.

## Folders

The clips go into these folders:

| Folder | What goes in |
|---|---|
| `Sounds/crew/callout` | crew with `severity callout` |
| `Sounds/crew/relief` | crew with `event relief` |
| `Sounds/crew/calm`, `tense`, `panic` | other crew rows by band. Band `none` goes to `calm`. |
| `Sounds/control/calm` | Mission Control with band `calm` or `none` |
| `Sounds/control/tense` | Mission Control with band `tense` or `panic` |

The subtitle-only role (`probe`) has no clip. Their `folder` is empty.

The rule is in the code twice: `folder_for` in `audio/build_lonapse.py` and `PhraseTable.FolderFor` in `src/PhraseTable.cs`. The build writes the folder as an extra column `folder` in `phrases.tsv`. The C# parser rejects a row when that column differs from its own rule. So the offline tests catch a difference.

## The built table

`audio/build/lonapse/phrases.tsv` holds every row after the `{body}` expansion, with these columns:

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	trait	persona	topic	core	beep	spanish	lonapse_text	folder	body	english
```

It is installed as `GameData/Lonapse/Phrases/phrases.tsv`. The C# loader (`PhraseTable.Parse`) finds the columns by name. An older table without `phase`, `cause`, `trait`, `persona`, `topic`, `core`, `beep` or `body` still loads. Then those columns are `any` (`beep` and `body` empty). A built table from before v5 fails `--verify-only` (the header differs) until the next build. A subtitle-only row has an empty `spanish`, `lonapse_text` and `folder`.

## Voices

| Kind | Piper voice | License (from the model card) | Pitch up |
|---|---|---|---|
| crew, male | `es_ES-davefx-medium` | CC0 | 5 semitones |
| crew, female | `es_MX-claude-high` | Apache-2.0 | 4 semitones |
| Mission Control | `es_MX-ald-medium` | Unlicense | 2 semitones |

I have not heard the gender of the voices. I estimated it from the median pitch of a test sentence: claude about 185 Hz, ald about 152 Hz and davefx about 104 Hz. Check it by ear.

Every band adds tempo and pitch: tense 1.10 times as fast and 0.7 semitone higher, panic 1.10 times and 1 semitone (it was 3, that sounded like chipmunks), callouts 1.08 times and 0.5 semitone. Mission Control in a panic speaks a bit slower to stay calm: 0.95 times, and 0.3 semitone higher. That small lift is there because the old delivery (0.92 times, no lift) sounded deeper than the calm one. I have not listened to the new delivery.

I make crew panic lines in pieces. I cut the line at the punctuation. Every piece gets its own tempo (0.93 to 1.12 times) and Piper with more variation (`--noise-scale` 0.9 and `--noise-w-scale` 1.1). Between two pieces there is a gasp (filtered noise, 0.16 to 0.28 s) or a short pause. Now and then the voice cracks: the end of a piece goes up 3 semitones. Then comes light distortion (tanh). Screams (`scream_*`) are stretched with a shaky vibrato. All random choices depend on the clip, so a new build makes the same choices. All these numbers are at the top of `build_lonapse.py` in `DELIVERY` and `BAND_DELIVERY`. I have not tuned them by ear.

The pitch shift uses `asetrate`. That moves the formants too. It gives the small, bright Kerbal sound. Piper speaks slower by the same ratio, so the tempo after the shift sounds normal.

## Installing Piper

Piper runs in its own Python environment. Once:

```bash
cd path/to/KSP-Lonapse
python3 -m venv audio/.venv
audio/.venv/bin/pip install piper-tts
python3 -I audio/build_lonapse.py --fetch-voices
```

`--fetch-voices` downloads the voice models from Hugging Face into `audio/voices/` and checks the sha256. Together they are about 190 MB. They are not in git.

## Building

```bash
python3 -I audio/build_lonapse.py                  # build and verify
python3 -I audio/build_lonapse.py --validate-only  # only check the tables
python3 -I audio/build_lonapse.py --verify-only    # check the existing build
python3 -I audio/build_lonapse.py --jobs 4         # number of render processes
python3 -I audio/build_lonapse.py --clean          # remove the Lonapse clips and the cache first
python3 -I audio/build_lonapse.py --voice-m es_MX-ald-medium   # try another voice
python3 -I audio/build_lonapse.py --try chatter_panic_03 chat_calm_s4_2 --out /tmp/lonapse_try
```

`--try` renders the clips of the given keys into a scratch folder, with the time per clip. A base key also selects its body variants. It does not touch the cache or `Sounds/`. I use it to listen to a delivery change before a full build.

The script installs nothing into the game. `python3 -I audio/build_clips.py --install` puts the whole `Sounds/` folder into the game. It refuses when the Lonapse clips are missing.

The script stops with an error when a table is wrong. It reports every bad row with its file and line number, and the shortfall per event.

### Render cache

Every clip has a render key. It is a hash of the clip key, the gender, the Lonapse text, the voice and its model hash, the pitch and tempo, the level targets, the panic settings and `RENDER_VERSION`. The rendered OGG and its measurements are stored under that key in `audio/build/lonapse/cache/`. A build only renders clips whose key is new. Then it copies the clips into `Sounds/` and removes clips the tables no longer have. Verification reuses the stored measurements when the bytes on disk match the cache. So a build after a small table change takes seconds.

When I change the render chain in a way the key does not see (a filter, the gasp model, the trim rule), I raise `RENDER_VERSION` at the top of the script. `--clean` also empties the cache.

### Render time

I measured this on this machine (16 CPUs). One clip takes about 1 to 2.3 s in one process, including the measurement. Panic clips take the longest, because every piece is its own Piper call. With 8 render processes 36 calm clips took 12.2 s, about 0.34 s per clip of wall time. For about 2000 clips I expect roughly 11 to 15 minutes with 8 processes. I have not timed a full run of that size.

Piper is not bit for bit repeatable. A new render gives clips with the same text, voice and levels, but other bytes. The cache keeps the bytes stable as long as the key does not change.

## Level and checks

For every clip the script measures (with ffmpeg `astats`):

- gated RMS, target -20 dBFS with a margin of 1 dB
- peak, at most -1.3 dBFS after decoding the OGG
- silence at the start and the end, at most 50 ms
- fades of 8 ms, and 15 ms of silence at each end

All clips are OGG Vorbis, mono, 44.1 kHz, with `libvorbis -q:a 3`. The beeps and static are WAV. The voices are OGG because WAV would make the download several times bigger.

## What I have not tested

- I have not listened to the clips. You have to judge whether the voices sound like Kerbals.
- I have not listened to the backwards body names. Whether "luY" really sounds like Jool is a guess.
- The gender of the voices is an estimate from the pitch.
- I have not seen the wiki page. The reversal rule comes from the plan and from a search result.
- I have not heard the clips in the game.
- The licenses come from the model cards of the voices. Piper itself is GPL-3.0 or later. Whether the output of a GPL program falls under the GPL is not settled. See `audio/CREDITS.md`.
