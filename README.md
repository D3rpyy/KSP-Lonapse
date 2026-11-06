<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/LonapseBannerV2.png">
  <img src="docs/images/lonapse-logo-light.svg" alt="Lonapse" width="100%">
</picture>

[![KSP 1.12.x](https://img.shields.io/badge/KSP-1.12.x-blue)](https://www.kerbalspaceprogram.com/)
[![Version 0.7.0](https://img.shields.io/badge/version-0.7.0-green)](CHANGELOG.md)
[![Code: MIT](https://img.shields.io/badge/code-MIT-lightgrey)](LICENSE)
[![Audio: CC BY 4.0](https://img.shields.io/badge/audio-CC%20BY%204.0-lightgrey)](LICENSE-CC-BY-4.0)
[![Runtime voice: GPL-3.0 and others](https://img.shields.io/badge/runtime%20voice-GPL--3.0%20%2B%20MIT%20%2B%20others-lightgrey)](runtime/licenses/README.md)
[![CKAN](https://img.shields.io/badge/CKAN-Indexed-green.svg)](https://github.com/KSP-CKAN/CKAN)
[![SpaceDock](https://img.shields.io/badge/SpaceDock-Available-blue.svg)](https://spacedock.info/mod/4786/Lonapse#stats)

Radio chatter mod for Kerbal Space Program that reacts dynamically to your flight and EVA.

Your crew talks to Mission Control and to each other about what is actually going on during the flight. A quiet coast in orbit stays
mostly quiet. A capsule tumbling through space gets a crew shouting over each other while Mission Control tries to
keep them calm. Probes get their own voice too in the form of beeping noises.

Each kerbal has their own voice based on their gender, and their own personality too! The personality is based on the Kerbal's stats as seen in the Astronaut Complex. For example, a Kerbal with high stupidity would make more jokes in flight, while one that has a low stupidity would make fewer jokes. The voice is generated based on a seed, and the seed stays the same even after restart. That means that the Kerbals will keep the voice that is unique to them.

loñapsE is the language of the kerbals. Under the hood it is Spanish with every word spelled backwards ("Separación" becomes "nóicarapeS"), rendered
with a text to speech voice and pitched up. It sounds like a real language without being one you can follow. If you are not fluent in loñapsE, the English subtitles show what your Kerbals are saying. They are on by default.

## What it does

- **Crew and Mission Control talk.** Each kerbal has a voice and a temperament. This is a new mechanic called stress. Stress comes from G force, spin,
  heat, fuel, a hard landing and so on, and every kerbal reacts in their own way. A brave pilot can stay calm while a
  nervous scientist panics easily.
- **Callouts.** The crew will call out when staging, parachutes, full throttle or throttle cutoff happens. They will also call out during docking maneuvers or when they do a science experiment.
- **Cabin intercom.** With two or more kerbals aboard they chat among themselves now and then with some jokes. Jokes about floating around the cabin only come up in space.
- **The radio sounds like a radio.** Quindar tones on Mission Control, static that follows your CommNet signal (or
  RemoteTech), reentry blackout, a delay on the answers far from Kerbin and dropouts on a weak link. A light static plays under every transmission, which you can set in the Sound tab. This way the radio follows your connection with the KSC.
- **Probes.** Each (vanilla) probe core has its own beep voice with moods (happy, curious, scared, sleepy and more). Mission
  Control checks in, reacts to staging, burns, landings and docking, and notices when the batteries run low.
- **Probes and crew together.** A crewed vessel with a probe core gets small cabin chats: the probe beeps a report and
  a kerbal answers, often in their own way (the engineer checks the wiring, the scientist wants the data again). This
  also works with only one kerbal aboard. You can turn it off in the Probes tab.
- **EVA suit radio.** Breathing in the helmet that speeds up when you run, short check-ins with Mission Control and a
  few words when your kerbal falls over. The suit relays through a ship nearby.
- **Animated talking kerbals.** Jaws move when a Kerbal talks and the others turn their heads to the speaker. This is visible in EVA, IVA and in the
  portraits while flying.
- **Launch countdown.** Press Alt+C on the pad and launch control counts you down (well, up. It is reversed after all). If you want a real countdown from 10, pick Down in the General tab.
- **Stations and bases.** These get regular check-ins from mission control on a larger interval. The crew will still joke on the intercom though.
- **Runtime voice: they say your names.** A small speech engine (Piper) ships with the mod and speaks new lines while you play, in the same voices as the recorded ones. It is on by default and runs on Windows and Linux (see Runtime voice files below).
  - **Names.** Mission Control ends a line with the name of the kerbal who just spoke or with your vessel's name, and kerbals on the intercom call each other by name. A planet from a mod is called by its real name.
  - **Real numbers.** Now and then Mission Control reports your orbit, altitude, speed, fuel, battery, the next burn, the mission day, funds, science or reputation. Low over the ground it reads your height and descent speed, on a docking approach the distance to the target. After science it says the points, after a contract the funds.
  - **Crew history.** Mission Control knows your kerbals' careers: a first flight, the tenth mission, a first time at Duna, a landing here before, or their level. Two kerbals can chat about it too.
  - **22 crew chats with values**, some only for the right jobs, like a pilot and an engineer talking about the fuel.
  - **The voice says what the subtitle shows.** For lines whose subtitle fills in a word (a lost thing, a food, a part, a name), the voice speaks that exact version.
  - **Your own lines.** Add a row or a file of your own to `Phrases/Runtime/` (the format is at the top of `reports.tsv`), or a file in `Phrases/Extra/`, and restart KSP. No audio to render. When a line is not ready within a second, the recorded line plays instead.
- **English subtitles** with a colour per speaker. Most lines have several wordings, so a line you heard before can read differently. The number of subtitle lines, their size, width and position are fully customizable.
- **Lots of lines.** 1896 recorded interactions with 6478 extra subtitle wordings, plus 53 runtime lines and 22 runtime chats. The mod remembers which ones you heard, also after a restart, so you hear the rest before one comes back.

## Install

1. Download `Lonapse-0.8.0.zip` from the releases page. The `GameData` folder in this repo has the same files, except the runtime voice (`PluginData/Runtime`), which is only in the zip.
2. Copy the `GameData/Lonapse` folder into the `GameData` folder of your KSP install.

That's all. Lonapse has no dependencies. The mod is available for install via the CKAN. Additionally, the mod is available on [SpaceDock](https://spacedock.info/mod/4786/Lonapse).

To update, delete the old `GameData/Lonapse` folder first, but keep `GameData/Lonapse/PluginData` if you want to keep
your settings.

## Settings

Click the antenna button on the toolbar in flight. There you can set how chatty the radio is, the volumes, which
events Mission Control reacts to, the probe behaviour, the subtitles and a bunch more just to your liking.

Hotkeys:

- **Alt+Y** mutes and unmutes.
- **Alt+C** starts or stops the launch countdown on the pad.

You can change both keys in the window.

Settings are saved in `GameData/Lonapse/PluginData/settings.cfg`.

## Probe voices

Every probe beep is made from a few values: a pitch, a wave (`sine triangle square saw pulse fm`), a rhythm
(`even swing stutter lazy`), a glide from note to note, the speed, the note jumps, vibrato and grit. Each of the
seven moods (happy, offended, curious, scared, sleepy, proud, bored) has its own note pattern, and a voice can bring
its own patterns too. The stock cores use prebuilt clips. Any other voice is made in the game when it first beeps.

**In the game.** Fly a probe and open the Probes tab. Under Voice you can pick another voice for that core part,
change its sound with the sliders and buttons, and press Play to hear a mood. A change to a stock voice changes every
probe with that voice. New voice makes a copy that only that part uses. Reset voice brings back the original. All
of it is saved in `settings.cfg` and wins over what a mod sets. Own mood patterns can only be written in a config
file, but a voice you change in the window keeps them.

**In a config file.** Any `.cfg` in GameData can add a voice:

```
LONAPSE_PROBE_VOICE
{
    name = myprobe          // the name parts use
    displayName = My Probe  // the subtitle name
    lines = okto            // whose lines: stayputnik okto hecs rovemate snob qbe mk2drone mpo mtm generic
    baseHz = 760            // 200 to 3000
    wave = fm
    rhythm = swing
    glide = 0.4             // 0 to 1
    speed = 1.2             // 0.5 to 2, optional (1)
    spread = 1.5            // note jumps, 0.25 to 2, optional (1)
    vibrato = 0.2           // semitones, 0 to 1, optional (0)
    buzz = 0.3              // grit, 0 to 1, optional (0)

    MOOD                    // optional, one per mood you want to replace
    {
        name = happy
        notes = 0>4 4>7 7>12:0.15     // one line: three variants at 0, +2 and -2 semitones
        length = 0.07       // note length in s when a note has none (0.1)
        gap = 0.03          // pause after each note in s (0.04)
        rhythm = true       // false: the voice rhythm does not change this pattern
        // vibrato and buzz work here too, on top of the voice values
    }
}
```

A note is `start>end:length@level`. Start and end are semitones from `baseHz` (-36 to 36), so `0>4` slides up four
semitones and a single number like `5` is a flat note. Length (0.01 to 1 s) and level (0 to 1) are optional.
`rest:0.1` adds a pause after the note before it. Up to three `notes` lines make three variants of your own.
Moods you don't write keep the stock pattern. Mistakes are written to `KSP.log` with `[Lonapse]` in front.

A voice is only text. Lonapse makes the sound in the game, so a mod ships one `.cfg` file and no audio.
A voice named after a stock core (`okto`, `hecs` and so on) replaces that core's voice. To give any other part a
voice, add `ModuleLonapse` to it. A part author can put the `MODULE` block in the part's own config. To add it to a
part from another mod, or only when Lonapse is installed, use a Module Manager patch:

```
@PART[myProbeCore]:NEEDS[Lonapse]
{
    MODULE
    {
        name = ModuleLonapse
        voice = myprobe     // a stock voice or a LONAPSE_PROBE_VOICE name
        priority = 0        // with several cores on a vessel, the highest wins
        displayName = STP   // optional, the subtitle name of this part
    }
}
```

`displayName` on the module wins over the `displayName` of the voice. So several parts can share one voice and
still show their own name in the subtitles. It may be a localization tag like `#LOC_myMod_stp`. Without it, the
part shows the name of its voice.

The stock cores get the module too, from `Patches/Lonapse_StockCores.cfg` (priority -1, so a modded core with
priority 0 or more still wins). Module Manager is not required: without it that patch does nothing and the stock
cores keep the same voices from Lonapse's own list. With MM, a patch of yours can run `:AFTER[Lonapse]` to change a
stock core's voice.

The module is saved in craft and save files like any part module. If you remove Lonapse, KSP writes a warning about
the missing module to its log. Nothing else happens.

## Lines for your own mod

Another mod can give its own events a voice: a repair that is done, supplies that run low, an experiment that is
finished. That mod ships its lines in `GameData/<ItsMod>/Lonapse/*.tsv` and tells Lonapse when the event happens.
Lonapse picks who talks and fits it into the radio. Neither mod needs the other. How it works is in
[docs/API.md](docs/API.md), with a wrapper to copy and an example file in [docs/api](docs/api).

## Compatibility

- Built and tested on KSP 1.12.5. Other 1.12 versions should work but I have not tried them.
- I play on Linux. Windows and macOS should be fine since it is a plain plugin, but I have not tested them.
- Don't run it together with Chatterer. Both would talk at the same time.
- RemoteTech is supported for the radio signal simulation, but I have not tested it in game yet.

## Known limits

- Subtitles are English only.
- Not everything has been tried in every situation. I need help with this! If something sounds wrong, please open an issue and attach your `KSP.log`.
  Lines from this mod start with `[Lonapse]`.

## Building from source

The repo has the plugin source, the phrase tables and the audio scripts. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
for the build and [audio/README.md](audio/README.md) for the sound pipeline.

## Runtime voice files

To speak lines with live values (names, numbers, bodies), Lonapse ships a copy of the Piper text to speech program
in `GameData/Lonapse/PluginData/Runtime`. Lonapse starts it as a separate program and sends it text. It runs on your
own computer. Lonapse does not download anything.

| Part | Files | Version and source | License |
|---|---|---|---|
| Piper | `piper.exe`, `piper_phonemize.dll` | 1.2.0, release [2023.11.14-2](https://github.com/rhasspy/piper/releases/tag/2023.11.14-2) of [rhasspy/piper](https://github.com/rhasspy/piper) and [piper-phonemize](https://github.com/rhasspy/piper-phonemize) | MIT |
| espeak-ng | `espeak-ng.dll`, `espeak-ng-data/` | 1.52, the build in that Piper release, source at [espeak-ng](https://github.com/espeak-ng/espeak-ng) | GPL-3.0-or-later |
| ONNX Runtime | `onnxruntime.dll`, `onnxruntime_providers_shared.dll` | the build in that Piper release, [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime) | MIT |
| Voices | `voices/*.onnx`, `voices/*.onnx.json` | the three voices in [audio/CREDITS.md](audio/CREDITS.md), from [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices), changed by me: weights quantized to int8, decoder kept float32 (see below) | CC0, Apache-2.0, Unlicense |

The program files for Windows and Linux are unchanged from the official Piper release. On Linux the libraries are
stored under the name Piper loads, so there are no symlinks in the zip. You can check the files yourself, or look the
hashes up on [VirusTotal](https://www.virustotal.com):

| File | sha256 |
|---|---|
| `windows/piper.exe` | `96f3da3811151580073e40bb4dd20eb0fb8115f5f5f76e2fb54282b3edfa5c1f` |
| `windows/piper_phonemize.dll` | `4b5f344b2f31204a8a94a0bf485f93e4971671e81188a0d67f326e113bfb0b2e` |
| `windows/espeak-ng.dll` | `9588480f8197df62fd8461a8431f8eaec6e8e7749c5ffcbe7fee656fe40a2189` |
| `windows/onnxruntime.dll` | `a630f67f4a82b6689e4178bf81d362d945522dd907b2a92cb43cebf72c83a06f` |
| `windows/onnxruntime_providers_shared.dll` | `71705b9bd76baced583eb37fe5ec2101946b4de50f1e9bdb156109f7e8082980` |
| `linux/piper` | `12672a94ca6716e5a8f335cfa68bf43bd9a33284960e3f9d16b85090bf7aab6b` |
| `linux/libespeak-ng.so.1` | `a18ed1133dd1594f9d076ecdf8eec51c87d9138d879de0f1516417a2679d5ecf` |
| `linux/libpiper_phonemize.so.1` | `602f2c559fc279f8d90b873f78ffbf56c1a5304a64351279d4a8c0cb5eb21fbd` |
| `linux/libonnxruntime.so.1.14.1` | `9a6828636dd7bed5887e6ce898cac1010cbbd593e6931b63423d189d42165434` |
| `voices/es_ES-davefx-medium.onnx` | `558ede4ebfd7bcb1c9cec4026a8575fff386d98de3afd372e78b722c08d8e034` |
| `voices/es_MX-claude-high.onnx` | `d6c55bd42f7b00a54d1ab18ac849c7940283764359890155988011386cff65fe` |
| `voices/es_MX-ald-medium.onnx` | `07cfd143e350cc5e6400f2da0b98409b8a16ca4524e3b26c11900d709c573468` |

There is no runtime voice on macOS. The macOS build of this Piper release is missing its libraries and cannot start.
On a Mac, Lonapse plays the recorded lines only. I play KSP for Windows under Proton, so the Linux build is not tested
in the game.

The licence texts and the source links are in `PluginData/Runtime/LICENSES`. The source code of Piper,
piper-phonemize and espeak-ng is on GitHub at the commits listed in
[runtime/licenses](runtime/licenses/README.md#source-code).
`./fetch_runtime.sh` in the repo downloads the Piper release, checks every file against these hashes and puts the
runtime folder together.

The voices are smaller than the originals (about 22 MB instead of 63 MB each). I quantized them with ONNX Runtime
`quantize_dynamic` (8 bit weights). The decoder, the last part of the model that makes the waveform, is left at
float32, because quantizing it too gave audible static in the pauses. Because of this change the voice hashes differ
from the ones on Hugging Face. `audio/quantize_runtime_voices.py` makes them, and gives the same hashes with
onnx 1.23.2 and onnxruntime 1.31.1.

`piper.exe` is not code signed, and a game that starts another program can look suspicious to a virus scanner. I
have not tested this with any antivirus program. If yours blocks it, compare the hashes above first. Lonapse keeps
working without the runtime voice, it then uses the recorded clips only.

## Credits and license

Inspired by [Chatterer](https://github.com/Athlonic/Chatterer). Lonapse contains none of its code or sounds.

The recorded voices are spoken by [Piper](https://github.com/OHF-Voice/piper1-gpl), a neural text to speech engine,
with three Spanish voice models. Their credits are in [audio/CREDITS.md](audio/CREDITS.md). The beeps and static are
synthesized.

The runtime voice uses [Piper 1.2.0](https://github.com/rhasspy/piper) by Michael Hansen, with
[piper-phonemize](https://github.com/rhasspy/piper-phonemize), [espeak-ng](https://github.com/espeak-ng/espeak-ng)
and [ONNX Runtime](https://github.com/microsoft/onnxruntime), and changed copies of the same three voices from
[rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices). See Runtime voice files above.

I wrote the mod with help from Claude (an AI assistant) for code and documentation. The design, the testing in game
and the decisions are my own.

The code is MIT licensed ([LICENSE](LICENSE)). The sounds and the phrase text are CC BY 4.0
([LICENSE-CC-BY-4.0](LICENSE-CC-BY-4.0)). Which part has which license is in [docs/LICENSING.md](docs/LICENSING.md).
The runtime voice in `PluginData/Runtime` is not part of Lonapse and keeps its own licences: MIT (Piper,
piper-phonemize, ONNX Runtime), GPL-3.0-or-later (espeak-ng, [LICENSE-GPL-3.0](LICENSE-GPL-3.0)), and CC0, Apache-2.0 and the Unlicense (the voices).
The texts are in [runtime/licenses](runtime/licenses/README.md), and so are the links to the source code.
