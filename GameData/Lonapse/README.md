# Lonapse

[![KSP 1.12.x](https://img.shields.io/badge/KSP-1.12.x-blue)](https://www.kerbalspaceprogram.com/)
[![Version 0.5.0](https://img.shields.io/badge/version-0.5.0-green)](CHANGELOG.md)
[![Code: MIT](https://img.shields.io/badge/code-MIT-lightgrey)](LICENSE)
[![Audio: CC BY 4.0](https://img.shields.io/badge/audio-CC%20BY%204.0-lightgrey)](LICENSE)

Radio chatter for Kerbal Space Program that pays attention to your flight.

Your crew talks to Mission Control, and to each other, about what is actually going on. A quiet coast in orbit stays
mostly quiet. A capsule tumbling through reentry gets a crew shouting over each other while Mission Control tries to
keep them calm. Probes get their own voice too: they beep, and Mission Control talks to them like a slightly
worried pet owner.

The kerbals speak Lonapse. That is Spanish with every word spelled backwards, rendered with a text to speech voice and
pitched up. It sounds like a language without being one you can follow. Turn on the English subtitles if you want to
know what they are saying.

## What it does

- **Crew and Mission Control talk.** Each kerbal has a voice and a temperament. Stress comes from G force, spin,
  heat, fuel, a hard landing and so on, and every kerbal reacts in their own way. A brave pilot can stay calm while a
  nervous passenger is already panicking.
- **Callouts.** Staging, parachutes, full throttle, cutoff, docking distances.
- **Cabin intercom.** With two or more kerbals aboard they chat among themselves now and then. It sounds like a small
  room, not like the radio.
- **The radio sounds like a radio.** Quindar tones on Mission Control, static that follows your CommNet signal (or
  RemoteTech), reentry blackout, a delay on the answers far from Kerbin and dropouts on a weak link.
- **Probes.** Each probe core has its own beep voice with moods (happy, curious, scared, sleepy and more). Mission
  Control checks in, reacts to staging and burns, and notices when the batteries run low.
- **EVA suit radio.** Breathing in the helmet that speeds up when you run, short check-ins with Mission Control and a
  few words when your kerbal falls over. The suit relays through a ship nearby.
- **Talking kerbals.** Jaws move while they talk and the others turn their heads to the speaker, in IVA and in the
  portraits.
- **Launch countdown.** Press Alt+C on the pad and launch control counts you down (well, up. It is Lonapse).
- **Stations and bases** get a slower, more relaxed rhythm with regular check-ins.
- **English subtitles** with a colour per speaker. You can move them and change the size.

## Install

1. Download `Lonapse-0.5.0.zip` from the releases page. The `GameData` folder in this repo has the same files.
2. Copy the `GameData/Lonapse` folder into the `GameData` folder of your KSP install.

That's all. Lonapse has no dependencies. You can also install it with CKAN once it is listed there.

To update, delete the old `GameData/Lonapse` folder first, but keep `GameData/Lonapse/PluginData` if you want to keep
your settings.

**Coming from Quindar?** That was the old name of this mod. Delete `GameData/Quindar`. Lonapse picks up your old
settings by itself. It stays silent while the old Quindar plugin is still installed, because both would talk at once.

## Settings

Click the antenna button on the toolbar in flight. There you can set how chatty the radio is, the volumes, which
events Mission Control reacts to, the probe behaviour and the subtitles.

Hotkeys:

- **Alt+Y** mutes and unmutes.
- **Alt+C** starts or stops the launch countdown on the pad.

You can change both keys in the window. Some Linux window managers grab Alt combinations. If a hotkey does nothing,
pick another one.

Settings are saved in `GameData/Lonapse/PluginData/settings.cfg`.

## Compatibility

- Built and tested on KSP 1.12.5. Other 1.12 versions should work but I have not tried them.
- I play on Linux. Windows and macOS should be fine since it is a plain plugin, but I have not tested them.
- Don't run it together with Chatterer. Both would talk at the same time.
- RemoteTech is supported for the signal, but I have not tested it in game.

## Known limits

- The stress thresholds are my own guesses from playing. If your crew panics too easily or not at all, the Advanced
  tab has the knobs.
- Subtitles are English only.
- Not everything has been tried in every situation. If something sounds wrong, open an issue and attach your `KSP.log`.
  Lines from this mod start with `[Lonapse]`.

## Building from source

The repo has the plugin source, the phrase tables and the audio scripts. See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
for the build and [audio/README.md](audio/README.md) for the sound pipeline.

## Credits and license

Inspired by [Chatterer](https://github.com/Athlonic/Chatterer). Lonapse contains none of its code or sounds.

The voices are rendered with [Piper](https://github.com/OHF-Voice/piper1-gpl) and three Spanish voice models. Their
credits are in [audio/CREDITS.md](audio/CREDITS.md). The beeps and static are synthesized.

The code is MIT licensed. The sounds and the phrase text are CC BY 4.0. See [LICENSE](LICENSE).
