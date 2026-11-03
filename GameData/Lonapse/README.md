<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/lonapse-logo-dark.svg">
  <img src="docs/images/lonapse-logo-light.svg" alt="Lonapse" width="100%">
</picture>

[![KSP 1.12.x](https://img.shields.io/badge/KSP-1.12.x-blue)](https://www.kerbalspaceprogram.com/)
[![Version 0.5.2](https://img.shields.io/badge/version-0.5.2-green)](CHANGELOG.md)
[![Code: MIT](https://img.shields.io/badge/code-MIT-lightgrey)](LICENSE)
[![Audio: CC BY 4.0](https://img.shields.io/badge/audio-CC%20BY%204.0-lightgrey)](LICENSE)

Radio chatter mod for Kerbal Space Program that reacts dynamically to your flight and EVA.

Your crew talks to Mission Control and to each other about what is actually going on during the flight. A quiet coast in orbit stays
mostly quiet. A capsule tumbling through space gets a crew shouting over each other while Mission Control tries to
keep them calm. Probes get their own voice too in the form of beeping noises.

Each kerbal has their own voice based on their gender, and their own personality too! The personality is based on the Kerbal's stats as seen in the Astronaut Complex. For example, a Kerbal with high stupidity would make more jokes in flight, while one that has a low stupidity would make fewer jokes. The voice is generated based on a seed, and the seed stays the same even after restart. That means that the Kerbals will keep the voice that is unique to them.

loñapsE is the language of the kerbals. Under the hood it is Spanish with every word spelled backwards ("Separación" becomes "nóicarapeS"), rendered
with a text to speech voice and pitched up. It sounds like a real language without being one you can follow. If you are not fluent in loñapsE and you want to know what your Kerbals are saying during a flight you can turn on the subtitles and read their conversations. 

## What it does

- **Crew and Mission Control talk.** Each kerbal has a voice and a temperament. This is a new mechanic called stress. Stress comes from G force, spin,
  heat, fuel, a hard landing and so on, and every kerbal reacts in their own way. A brave pilot can stay calm while a
  nervous scientist panics easily.
- **Callouts.** The crew will call out when staging, parachutes, full throttle or throttle cutoff happens. They will also call out during docking maneuvers.
- **Cabin intercom.** With two or more kerbals aboard they chat among themselves now and then with some jokes.
- **The radio sounds like a radio.** Quindar tones on Mission Control, static that follows your CommNet signal (or
  RemoteTech), reentry blackout, a delay on the answers far from Kerbin and dropouts on a weak link. This way the radio is simulated in a way to your connection with the KSC.
- **Probes.** Each (vanilla) probe core has its own beep voice with moods (happy, curious, scared, sleepy and more). Mission
  Control checks in, reacts to staging and burns, and notices when the batteries run low.
- **EVA suit radio.** Breathing in the helmet that speeds up when you run, short check-ins with Mission Control and a
  few words when your kerbal falls over. The suit relays through a ship nearby.
- **Animated talking kerbals.** Jaws move when a Kerbal talks and the others turn their heads to the speaker. This is visible in EVA, IVA and in the
  portraits while flying.
- **Launch countdown.** Press Alt+C on the pad and launch control counts you down (well, up. It is reversed after all).
- **Stations and bases.** These get regular check-ins from mission control on a larger interval. The crew will still joke on the intercom though.
- **English subtitles** with a colour per speaker. The number of subtitle lines, their size and position are fully customizable.

## Install

1. Download `Lonapse-0.5.2.zip` from the releases page. The `GameData` folder in this repo has the same files.
2. Copy the `GameData/Lonapse` folder into the `GameData` folder of your KSP install.

That's all. Lonapse has no dependencies. You can also install it with CKAN once it is listed there.

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

## Compatibility

- Built and tested on KSP 1.12.5. Other 1.12 versions should work but I have not tried them.
- I play on Linux. Windows and macOS should be fine since it is a plain plugin, but I have not tested them.
- Don't run it together with Chatterer. Both would talk at the same time.
- RemoteTech is supported for the radio signal simulation, but I have not tested it in game yet.
- Modded probe cores will not have their own personality, and instead will get a generic probe voice with "Probe" as the subtitle name.

## Known limits

- Subtitles are English only.
- Not everything has been tried in every situation. I need help with this! If something sounds wrong, please open an issue and attach your `KSP.log`.
  Lines from this mod start with `[Lonapse]`.

## Building from source

The repo has the plugin source, the phrase tables and the audio scripts. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
for the build and [audio/README.md](audio/README.md) for the sound pipeline.

## Credits and license

Inspired by [Chatterer](https://github.com/Athlonic/Chatterer). Lonapse contains none of its code or sounds.

The voices are spoken by [Piper](https://github.com/OHF-Voice/piper1-gpl), a neural text to speech engine, with three
Spanish voice models. Their credits are in [audio/CREDITS.md](audio/CREDITS.md). The beeps and static are synthesized.

I wrote the mod with help from Claude (an AI assistant) for code and documentation. The design, the testing in game
and the decisions are my own.

The code is MIT licensed. The sounds and the phrase text are CC BY 4.0. See [LICENSE](LICENSE).
