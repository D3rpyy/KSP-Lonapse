<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/LonapseBannerV2.png">
  <img src="docs/images/lonapse-logo-light.svg" alt="Lonapse" width="100%">
</picture>

[![KSP 1.12.x](https://img.shields.io/badge/KSP-1.12.x-blue)](https://www.kerbalspaceprogram.com/)
[![Version 0.6.0](https://img.shields.io/badge/version-0.6.0-green)](CHANGELOG.md)
[![Code: MIT](https://img.shields.io/badge/code-MIT-lightgrey)](LICENSE)
[![Audio: CC BY 4.0](https://img.shields.io/badge/audio-CC%20BY%204.0-lightgrey)](LICENSE)
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
- **English subtitles** with a colour per speaker. The number of subtitle lines, their size, width and position are fully customizable.
- **Lots of lines.** 1729 interactions with almost 3500 lines, 738 of them new in 0.6.0. The mod remembers which ones you heard, also after a restart, so you hear the rest before one comes back.

## Install

1. Download `Lonapse-0.6.0.zip` from the releases page. The `GameData` folder in this repo has the same files.
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
    lines = okto            // whose phrase table lines it gets: stayputnik okto hecs rovemate snob generic
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
    }
}
```

The module is saved in craft and save files like any part module. If you remove Lonapse, KSP writes a warning about
the missing module to its log. Nothing else happens.

## Compatibility

- Built and tested on KSP 1.12.5. Other 1.12 versions should work but I have not tried them.
- I play on Linux. Windows and macOS should be fine since it is a plain plugin, but I have not tested them.
- Don't run it together with Chatterer. Both would talk at the same time.
- RemoteTech is supported for the radio signal simulation, but I have not tested it in game yet.
- Modded probe cores get the generic probe voice with "Probe" as the subtitle name, unless they have a Lonapse voice (see Probe voices). You can also pick one for them in the window.

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
