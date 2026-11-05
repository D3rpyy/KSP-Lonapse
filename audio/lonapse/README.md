# loñapsE: the language of the kerbals

loñapsE is the language the kerbals speak in Lonapse. Every crew and Mission Control line in the mod is spoken in it.
This page explains how the voice lines are made, how the phrase tables work and how you can add your own lines. The
short version: I write the lines in Spanish, a script spells every word backwards, and a text to speech voice reads
them out loud. The audio itself is not reversed, only the spelling.

## What is in this folder

| File | Role |
|---|---|
| `phrases/*.tsv` | The source tables, one file per kind of event. I write these by hand. |
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
| `phrases/probe_pet.tsv` | Mission Control talks to the probe, the probe beeps back (`probe_chat`, `probe_sleepy`). The probe calls first (`probe_hail`), stress per cause (`probe_stress`), "say again?" (`probe_say_again`), milestones (`probe_milestone`), rover moments (`probe_rover`) and data (`probe_data`). | `pp_` |
| `phrases/probe_events.tsv` | Probe scenes for science, power, SOI, part lost, signal back, touchdown, splashdown, docked and burn_soon. | `pe_` |
| `phrases/chatter_more.tsv` | 0.6.0: calm radio with Mission Control, mostly jokes. Goofy phases, some per trait and persona. | `cm_` |
| `phrases/intercom_more.tsv` | 0.6.0: more crew-only chats. Always funny. | `im_` |
| `phrases/tense_more.tsv` | 0.6.0: tense radio. Many `cause any` scenes, so a generic line no longer repeats. | `tm_` |
| `phrases/serious_more.tsv` | 0.6.0: calm radio in launch, ascent, reentry and descent. Short and mostly procedural. | `sr_` |
| `phrases/probe_crew.tsv` | 0.6.0: a crewed vessel with a probe core. The probe beeps in the cabin and a crew member answers (event `probe_crew`). | `pc_` |
| `phrases/events_more.tsv` | 0.6.0: more crew lines for throttle, chutes, staging, power, signal, landing, docking, EVA, stations and burns. | `em_` |
| `phrases/probe_more.tsv` | 0.6.0: more probe mode lines for throttle, chutes, data, splashdown, touchdown, docking, burns and say again. | `pm_` |

Some older rows have keys without these prefixes.

The build script is `audio/build_lonapse.py`. The clips go into the shared folder `audio/build/Sounds/`. The built table, the manifest and the render cache go into `audio/build/lonapse/`. Both paths are in `.gitignore`.

## How a line becomes loñapsE

Every line goes through the same steps:

1. It normalizes the text to NFC, so an ñ is always one character.
2. It reverses the letters of every word. Punctuation and spaces stay in place. The word order stays the same.
3. Piper (a neural TTS) speaks the reversed text with a Spanish voice.
4. ffmpeg shifts the voice up into the Kerbal register. Then come trim, fades and level.

Examples: "hola" becomes "aloh". "Español" becomes "loñapsE". "¿Qué tal?" becomes "¿éuQ lat?". A capital letter moves with its letter.

A lowercase letter stays lowercase. The word "español" with a lowercase e becomes "loñapse".

An accent stays on its own letter. "Separación" becomes "nóicarapeS".

## Table format

Every file in `phrases/` is a plain tab separated table (UTF-8). Lines that start with `#` are comments. The first other line is the header. The columns are found by name, so their order doesn't matter, but I keep this order:

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	spanish	english
```

Three more columns are optional: `trait`, `persona` and `topic`. A file without them still loads, and every row then gets `any`. An empty cell also means `any`. A file that uses them puts them after `cause`:

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	trait	persona	topic	spanish	english
```

Two more columns are optional: `core` and `beep`. Without `core` (or with an empty cell) a row gets `any`. Without `beep` a row gets an empty beep. A probe file puts them after `topic` (or after `cause`):

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	core	beep	spanish	english
```

| Column | Allowed values |
|---|---|
| `clip_key` | `[a-z0-9_]+`, unique across all files. No `__` (that is for body variants). The file name becomes `<clip_key>_<m|f>.ogg`. |
| `event` | `chatter`, `staging`, `chute_semi`, `chute_full`, `throttle_full`, `throttle_cut`, `dock_approach`, `dock_close`, `dock_hard`, `docked`, `touchdown`, `splashdown`, `relief`, `plan_node`, `burn_soon`, `station_check`, `eva_check`, `countdown`, `soi_enter`, `science`, `part_lost`, `power_low`, `power_ok`, `signal_lost`, `signal_back`, `crew_lost`, and the probe events `probe_chat`, `probe_sleepy`, `probe_hail`, `probe_stress`, `probe_say_again`, `probe_milestone`, `probe_rover`, `probe_data` |
| `severity` | `chatter`, `callout`, `warning` |
| `band` | `calm`, `tense`, `panic`, `none` |
| `role` | `crew`, `mc` and `probe` (see "Probe mode" below) |
| `scene` | `[a-z0-9_]+`. The lines of one conversation share the scene. A scene lives in one file. |
| `seq` | integer, 1 or higher. The seq numbers of a scene run 1, 2, 3 without gaps. |
| `gender` | `m`, `f`, `both` |
| `phase` | `any`, `prelaunch`, `launch`, `ascent`, `orbit`, `coast`, `reentry`, `descent`, `landed`, `blackout`, `serious`, `goofy` |
| `cause` | `any`, `spin`, `gforce`, `q`, `ground`, `heat`, `plasma`, `fuel`, `impulse`, `engine_out` |
| `trait` | optional. `any`, `pilot`, `engineer`, `scientist`, `tourist`. The crew speaker must have this trait. |
| `persona` | optional. `any`, `badass`, `brave`, `nervous`, `dim`, `veteran`. The crew speaker must have this persona. |
| `core` | optional. `any`, `stayputnik`, `okto`, `hecs`, `rovemate`, `snob`, `generic` (the keys of `ProbeCores.Keys`). Holds for the whole scene. |
| `beep` | optional. The beep mood of a `probe` row: `happy`, `offended`, `curious`, `scared`, `sleepy`, `proud`, `bored`. Required on probe rows, empty on every other row. |
| `topic` | optional. `any` on every event except two. For `science` also the experiment ids `crewreport`, `evareport`, `mysterygoo`, `surfacesample`, `mobilematerialslab`, `temperaturescan`, `barometerscan`, `seismicscan`, `gravityscan`, `atmosphereanalysis`, `asteroidsample`, `cometsample`, `infraredtelescope`, `magnetometer`, `roc`. For `soi_enter` also `home` (the home body). For `chatter`, `power_low` and `power_ok` also `solar`: the scene talks about solar panels and only plays on a ship that has one. |
| `spanish` | the Spanish source, not empty. May hold `{body}`. Empty on the subtitle-only role `probe`. |
| `english` | the English subtitle, not empty. In game the subtitles show only this text. Holds `{body}` when `spanish` does. |

`phase` says when a scene may play. `serious` stands for launch, ascent, reentry and descent. `goofy` stands for prelaunch, orbit, coast and landed. `any` plays in every phase. The code that decides the current phase is `src/FlightPhase.cs`.

`cause` says which stress component the line is about. The names are the components in `src/StressModel.cs`, plus `engine_out`. Use `any` for a line that fits every cause. A tense or panic line only names what the game measures. So no fire: the game has no fire. Use heat instead.

The build checks these rules for you, so you can't break the game with a typo:

- All rows of one scene have the same `event`, `severity`, `band`, `phase`, `cause`, `trait`, `persona`, `topic` and `core`.
- `topic` is `any` except on `science` and `soi_enter` (see the table above).
- Explosion words only appear in `part_lost` rows. The check looks at `spanish` and `english`, case-insensitive, at the start of a word: `explo` (but not `explor`, so "explore" is fine), `boom`, `blast` (but not "blast off"), `explot` and `estall`. `blew` only counts as "blew up", "blew apart", "blew out" or "blew off". So "the intern blew away" in `mc_jokes.tsv` passes. The same rule is in `EXPLOSION_RE` in the script and in `PhraseTable.HasExplosionWord` in C#.
- A scene id is unique across files.
- An `mc` row has one voice. So `gender` is `m` or `both`, never `f`.
- `gender m` makes only the male file. `gender f` makes only the female file. `gender both` makes both.
- Every event has at least 2 rows. For `chatter` I need at least 3 rows per band (`calm`, `tense`, `panic`). For some newer events a shortfall is only a warning (`WARN table:`) until their rows are written.
- An intercom scene is never in a serious phase (`serious`, `launch`, `ascent`, `reentry`, `descent`).
- `{body}` is the only placeholder. It is in both `spanish` and `english`, or in neither, at most once per text. A `probe` row cannot hold it (see the subtitle rule below).
- The rules in "Probe mode" below.

The C# loader (`PhraseTable.Parse`) checks the same probe rules and the same placeholder rules as the script, with the same messages. The script checks more (scene in one file, seq 1..n, minimum counts, intercom phase).

## Scene variants (experimental)

This is on the branch `experimental/scene-variants`. It is not tested in game yet.

A scene always played the same way. With the optional column `alt` a line can have alternatives. The row with an empty `alt` is the base line. A row with the same `scene` and `seq` and an `alt` like `b` or `c` is an alternative for it. Each time the scene plays, the game picks one line per seq. A line nobody heard yet comes first. After that, the one heard longest ago. So a scene that comes back gets another punchline or another reaction.

```
clip_key	event	severity	band	role	scene	seq	gender	phase	cause	alt	spanish	english
bl_shield_sale_1	chatter	chatter	calm	crew	bl_shield_sale	1	both	blackout	any		¿Confías en el escudo térmico?	Do you trust the heat shield?
bl_shield_sale_2	chatter	chatter	calm	crew	bl_shield_sale	2	both	blackout	any		Lo compraron en oferta.	They bought it on sale.
bl_shield_sale_2b	chatter	chatter	calm	crew	bl_shield_sale	2	both	blackout	any	b	...	...
bl_shield_sale_3	chatter	chatter	calm	crew	bl_shield_sale	3	both	blackout	any		No me ayudas.	You're not helping.
bl_shield_sale_3b	chatter	chatter	calm	crew	bl_shield_sale	3	both	blackout	any	b	...	...
```

Every line is picked on its own, so 2 options on seq 2 and 2 on seq 3 give 4 versions. That means every alternative has to fit the lines before and after it, in every combination. `python3 audio/variants.py` prints every version of every scene with alternatives, so I can read them before I render anything. `--spanish` adds the Spanish, and a scene name limits the output to that scene.

The rules, checked by the script and by `PhraseTable.Parse`:

- `alt` is empty or `[a-z0-9]+`. An `alt` value is used once per scene and seq.
- An alternative needs a base row with the same scene and seq, and the same `role`.
- Its `gender` is `both` or the same as the base row. So a kerbal who can say the base line can say every alternative.
- An alternative cannot use `{body}`.
- Line counts, the intercom rule and the speaker gender of a scene come from the base rows only.

The game remembers heard lines in the same file as the heard scenes (`PluginData/played.txt`), as `line:<clip_key>`.

## Probe mode

Probe mode plays when a vessel has no crew but does have a probe core. Then only Mission Control and the probe talk. The probe has no voice clips, it beeps, and the subtitle tells you what the beep means.

| Role | Sound | Clip | Rules |
|---|---|---|---|
| `probe` | beeps from `beeps/probe_<core>_<beep>` (built by `build_clips.py`) | none, subtitle only | `spanish` empty, `english` required, short like "[beeps excitedly]". `beep` required. |

`mc` stays Mission Control on the radio. `crew` has no place in probe scenes.

The `gender` of a subtitle-only row does not matter. I write `both`.

Rules the build and the C# loader enforce:

- The probe events are `probe_chat`, `probe_sleepy`, `probe_hail`, `probe_stress`, `probe_say_again`, `probe_milestone`, `probe_rover` and `probe_data`. They have no `crew` rows. A `probe_hail` or `probe_data` scene starts with a `probe` row (the director only picks those).
- A `probe_say_again` scene has exactly 2 rows, `mc` then `probe`. The director plays the MC line and then repeats the probe beep with that subtitle.
- `probe_stress` uses the `cause` column. The director tries the main stress cause first, then `cause any`.
- Topics: `staging` has `separation` and `ignition`, `probe_milestone` has `day1`, `day10`, `day100`, `day365`, `orbit` and `landing`, `probe_rover` has `fast`, `air`, `tilt` and `idle`. A milestone needs its exact topic.
- The probe callouts (`staging`, `chute_semi`, `chute_full`, `throttle_full`, `throttle_cut`) only pick scenes that start with a `probe` row. The MC rows after it play now and then, not always.
- A `probe` subtitle is `[beeps` plus 1 to 3 lowercase words and `]`, for example "[beeps excitedly]", "[beeps very slowly]" or "[beeps a bit louder]". No commas, no quotes, no second clause. The plugin uses the same form when it overrides a mood (for example "[beeps nervously]" under stress). The check is `PROBE_SUBTITLE_RE` in the script and `PhraseTable.ProbeSubtitleOk` in C#.
- The probe role (`probe`) and a `core` other than `any` are only allowed in the probe events and in the probe variants of `science`, `power_low`, `power_ok`, `soi_enter`, `part_lost`, `signal_lost`, `signal_back`, `touchdown`, `splashdown`, `docked`, `burn_soon`, `staging`, `chute_semi`, `chute_full`, `throttle_full`, `throttle_cut` and `plan_node`.
- `signal_lost` has no probe rows. Mission Control cannot reach the probe, so nobody talks on the radio.
- A `probe` row needs a `beep` mood. Every other row has an empty `beep`.

### Probe and crew in the cabin (0.6.0)

The event `probe_crew` is for a crewed vessel with a probe core. It is not a probe mode event, it only plays with crew
aboard. The probe beeps in the cabin (no radio, no signal needed) and one crew member speaks the `crew` rows on the
intercom. The director runs it through the intercom timer, so it also plays with only one kerbal aboard. The rules:

- A `probe_crew` scene has at least one `probe` row and at least one `crew` row, in any order.
- No `mc` rows. Mission Control is not in the cabin.
- Never in a serious phase (`serious`, `launch`, `ascent`, `reentry`, `descent`), like the intercom.
- `trait` and `persona` work as on other crew scenes: they choose which kerbal answers.
- The probe rows follow the usual probe rules (empty `spanish`, a `beep` mood, a short `[beeps ...]` subtitle).

A scene is a probe scene when its `core` is not `any`, when one of its lines has a probe role, or when its event is a probe event. The helper is `PhraseTable.IsProbeScene` in C# and `is_probe_scene` in the script. Crew mode never picks a probe scene. Probe mode only picks probe scenes. So a probe version of `science` or `power_ok` needs a `core` or a probe role. A scene with only `mc` rows and `core any` is a crew scene.

`core any` fits every probe core. A scene with a core only plays for that core. `PhraseTable.ProbeScenesFor(event, core, phase, coreExact, filter)` gives the scenes. With `coreExact` it only gives the scenes of exactly that core, so the director can try the core first and then fall back to `any`.

A shortfall in any probe event is only a warning.

`trait`, `persona` and `topic` hold for the whole scene. In game `any` matches every speaker and every topic. For `topic` the plugin looks for an exact match first (`PhraseTable.TopicMatchesExact`). Whether it then falls back to `any` scenes (`PhraseTable.TopicMatches`) is the caller's choice.

A scene with only `crew` rows and more than one row is an intercom talk between crew members. A scene with `crew` and `mc` is a radio exchange with Mission Control. In a radio exchange one crew member talks. The plugin takes care of that.

## `{body}` and bodies.tsv

A row with `{body}` becomes one row per line of `bodies.tsv`. That file has the columns `key spanish english`. It holds the 17 stock bodies and `generic`. The key is the lowercase `bodyName` (`kerbin`, `mun`, ...). `generic` is for every other body: "el planeta" and "the planet".

The variant gets the clip key `<clip_key>__<body key>` and the body key in the column `body`. Example: `chat_calm_s4_2` becomes `chat_calm_s4_2__kerbin`, `chat_calm_s4_2__mun` and so on. A row without `{body}` in the same scene stays one row with an empty `body`.

The Spanish body names are written backwards on purpose. loñapsE reverses every word, so the clip ends up saying the real name. "nibréK" is spoken as "Kérbin". I spelled the names for a Spanish voice: Jool is "luY" (spoken "Yul"), Eeloo is "ulI" ("Ilu"), Laythe is "ziéL" ("Léiz"). Only `generic` is plain Spanish ("el planeta"). It is spoken in loñapsE like every other word. The English names carry "the" where English needs it ("the Mun"). If `{body}` starts an English sentence, the build makes the first letter a capital.

So write `{body}` where a name fits in Spanish and in English. "Qué vista sobre {body}." works for "Kerbin", "la Mun" and "el planeta". Avoid a word before `{body}` that changes with the name, like "del" or "de la".

In game a body scene counts as one scene. `PhraseScene.LinesFor(body)` gives the lines for one body, and falls back to `generic`. Every body variant is a real clip, so one `{body}` row with `gender both` gives 36 clips.

## Adding your own lines

Want to give the kerbals something new to say? It doesn't need any code.

1. Pick the file by event family (table above). Use its key prefix: `la_`, `re_`, `pa_`, `sp_`, `mj_`, `ic_`, `bl_`, `ev_`, `bo_`, `cm_`, `im_`, `tm_`, `sr_`, `pc_`, `em_`, `pm_`.
2. Give the lines of one exchange the same `scene` and number them with `seq` from 1.
3. Fill `phase` and `cause`. Serious work (launch, ascent, reentry, descent) is short and procedural. Orbit, coast and landed are goofy. Intercom is always goofy or `blackout`.
4. Check the tables without rendering:

```bash
python3 -I audio/build_lonapse.py --validate-only
```

It shows how many lines each file has, and points you to every bad row as `phrases/<file>:<line>`.

5. Build. Only the new or changed lines are rendered, so this is quick.

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

It is installed as `GameData/Lonapse/Phrases/phrases.tsv`. The C# loader (`PhraseTable.Parse`) finds the columns by name. A subtitle-only row has an empty `spanish`, `lonapse_text` and `folder`.

## Voices

| Kind | Piper voice | License (from the model card) | Pitch up |
|---|---|---|---|
| crew, male | `es_ES-davefx-medium` | CC0 | 5 semitones |
| crew, female | `es_MX-claude-high` | Apache-2.0 | 4 semitones |
| Mission Control | `es_MX-ald-medium` | Unlicense | 2 semitones |

The gender of each voice comes from the median pitch of a test sentence: claude about 185 Hz, ald about 152 Hz and davefx about 104 Hz.

Every band adds tempo and pitch: tense 1.10 times as fast and 0.7 semitone higher, panic 1.10 times and 1 semitone, callouts 1.08 times and 0.5 semitone. Mission Control in a panic speaks a bit slower to stay calm: 0.95 times, and 0.3 semitone higher. The lift keeps panic MC from sounding deeper than calm MC.

I make crew panic lines in pieces. I cut the line at the punctuation. Every piece gets its own tempo (0.93 to 1.12 times) and Piper with more variation (`--noise-scale` 0.9 and `--noise-w-scale` 1.1). Between two pieces there is a gasp (filtered noise, 0.16 to 0.28 s) or a short pause. Now and then the voice cracks: the end of a piece goes up 3 semitones. Then comes light distortion (tanh). Screams (`scream_*`) are stretched with a shaky vibrato. All random choices depend on the clip, so a new build makes the same choices. All these numbers are at the top of `build_lonapse.py` in `DELIVERY` and `BAND_DELIVERY`.

The pitch shift uses `asetrate`. That moves the formants too. It gives the small, bright Kerbal sound. Piper speaks slower by the same ratio, so the tempo after the shift sounds normal.

## Installing Piper

You only need this if you want to render voice lines yourself. Piper runs in its own Python environment. Set it up
once:

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
python3 -I audio/build_lonapse.py --clean          # remove the voice clips and the cache first
python3 -I audio/build_lonapse.py --voice-m es_MX-ald-medium   # try another voice
python3 -I audio/build_lonapse.py --try chatter_panic_03 chat_calm_s4_2 --out /tmp/lonapse_try
```

`--try` renders the clips of the given keys into a scratch folder, with the time per clip. A base key also selects its body variants. It does not touch the cache or `Sounds/`. I use it to listen to a delivery change before a full build.

The script installs nothing into the game. `python3 -I audio/build_clips.py --install` puts the whole `Sounds/` folder into the game. It refuses when the voice clips are missing.

The script stops with an error when a table is wrong. It reports every bad row with its file and line number, and the shortfall per event.

### Render cache

Every clip has a render key. It is a hash of the clip key, the gender, the loñapsE text, the voice and its model hash, the pitch and tempo, the level targets, the panic settings and `RENDER_VERSION`. The rendered OGG and its measurements are stored under that key in `audio/build/lonapse/cache/`. A build only renders clips whose key is new. Then it copies the clips into `Sounds/` and removes clips the tables no longer have. Verification reuses the stored measurements when the bytes on disk match the cache. So a build after a small table change takes seconds.

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

## Licence note

The licenses come from the model cards of the voices. Piper itself is GPL-3.0 or later. Whether the output of a GPL program falls under the GPL is not settled. See `audio/CREDITS.md`.
