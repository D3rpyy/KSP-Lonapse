# Lonapse: developer notes

This is the technical reference. The player guide is the `README.md` in the repo root.

A KSP 1.12.5 plugin that plays radio chatter (crew voices, Mission Control, beeps and static) that reacts to what
happens to your active crewed vessel. A calm orbit is nearly silent. A tumbling capsule gets a loud, fast and
overlapping crew. It is inspired by the Chatterer mod but contains none of its code or clips.

Lonapse is the language of the kerbals, and the mod is named after it. The voices speak Lonapse: Spanish with the letters of every word reversed ("Separación" becomes "nóicarapeS"). The
lines are rendered offline with the Piper neural TTS and pitched up into the Kerbal register by `audio/build_lonapse.py`
(see `audio/lonapse/README.md` and `audio/CREDITS.md`).

## Build

Build and install from this repo. Give the game folder with `KSP_DIR` or write it into `.ksp_dir` in the repo root
(one line, not in git):

```bash
./build.sh                # compile and install the DLL
./build.sh --compile-only # compile only, into build/
KSP_DIR=/path/to/KSP ./build.sh
python3 -I audio/build_clips.py --install   # build and install the sound clips (separate pipeline)
./package.sh              # refresh GameData/Lonapse in the repo and build the release zip in dist/
```

`build.sh` compiles with `mcs` against the game's own DLLs in `KSP_Data/Managed/` and copies the DLL to
`GameData/Lonapse/Plugins/Lonapse.dll`. It writes nothing outside `GameData/Lonapse/`, it
does not touch `Sounds/` and it never overwrites an existing `PluginData/settings.cfg`.

This mod used to be called Quindar (`GameData/Quindar/`). `build.sh` copies the old `PluginData` (settings and
backups) into `GameData/Lonapse/PluginData/` without overwriting anything and then deletes `GameData/Quindar/`.
Without `build.sh`, the plugin copies the old settings itself on its first start. While the old `Quindar.dll` is
still installed, Lonapse stays off and asks for `GameData/Quindar/` to be deleted, because both would play at once.

Layout in the game:

```
GameData/Lonapse/
  Plugins/Lonapse.dll
  Sounds/...                (from the audio pipeline)
  Phrases/phrases.tsv       (phrase table: scenes and subtitles, from the audio pipeline)
  PluginData/settings.cfg   (written by the plugin on first start)
```

The plugin also runs with zero clips. It then stays silent and logs a warning for each empty folder.
Check `KSP.log` for lines starting with `[Lonapse]`.

## Audio folders

Clips live in `GameData/Lonapse/Sounds/<folder>/`. The plugin finds them by folder, and the window shows the count per
folder against its target (red when below):

| Folder | Target | Used for |
|---|---|---|
| `crew/calm`, `crew/tense`, `crew/panic` | 24 each | crew radio lines by stress band |
| `crew/relief` | 8 | after blackout, touchdown and docking (falls back to `crew/calm`) |
| `crew/callout` | 8 | one-word callouts (0.3 to 0.6 s, dry): staging, parachutes, docking distances |
| `control/calm`, `control/tense` | 24, 12 | Mission Control |
| `beeps/ping`, `beeps/descent`, `beeps/stage` | 3, 2, 2 | telemetry, descent blips, stage tone |
| `beeps/landing`, `beeps/splashdown` | 1 each | optional touchdown tones |
| `beeps/quindar_in`, `beeps/quindar_out` | 1 each | MC transmission tones |
| `static/burst`, `static/loop`, `static/squelch` | 6, 2, 2 | blackout static, MC bed, squelch tail |
| `beeps/click_desk` | 1 | station and base radio key click |
| `beeps/headset_open`, `beeps/headset_close` | 1 each | ship and EVA radio key-up and key-down (short squelch noise) |
| `static/breath_loop` | 1 | helmet breathing bed on EVA |
| `beeps/roger` | 1 | roger beep at the end of a crew transmission |

The crew and control folders hold the Lonapse clips, named `<clip_key>_m.ogg` and `<clip_key>_f.ogg`. The plugin reads
`Phrases/phrases.tsv` to group them into scenes (a crew line plus an MC answer, a two-line intercom talk, a callout plus
an acknowledgement). Male kerbals get the `_m` clips, female kerbals the `_f` clips. Without the table the plugin falls
back to random clips per folder.

An empty folder is skipped with one log warning. If `crew/callout` is empty, staging uses the stage tone and docking
callouts use a short calm line.

## When does it play

Only in the flight scene, and only when the active vessel has at least one crew member. An EVA kerbal has a suit radio
to Mission Control (`evaRadio`, on by default): rare MC check-ins, a soft breathing bed and the headset squelch. It stays
calm unless something is really wrong (low jetpack propellant while thrusting, far from any ship). The suit has no antenna
of its own, so since v6 it relays through the best loaded ship nearby (`evaRelay`, on by default). That ship needs a
command pod or probe core. The kerbal then gets the ship's signal, scaled by the suit range: full up to `evaRelayFullKm`
(1 km), gone at `evaRelayZeroKm` (2.3 km). A weak ship signal gives a weak EVA radio, and a ship without a link means no
link on EVA either. The breathing bed speeds up gradually while the kerbal runs: running builds exertion over about
`evaExertRise` (20 s) and it fades over `evaExertFall` (30 s), up to `evaBreathRate` (1.4x) as fast and `evaBreathGain`
(1.5x) as loud. Hops and falls on EVA never count as a touchdown. When the kerbal ragdolls on the ground, the kerbal and
Mission Control have a short `eva_fall` exchange, at most once per `evaFallCooldown` (45 s). With `evaRadio` off an EVA kerbal counts as no crew. When the crew count drops to 0 (EVA, transfer, switching to a probe) everything fades out within 0.3 s and is
then stopped. When the active vessel is destroyed, audio stops at once. The pause menu pauses all sounds. Map view
keeps playing.

Time warp: during any on-rails warp (above 1x) everything is silent: no voices, no pings, no static. Physics warp
keeps playing normally. When rails warp ends, one short ping about 1 s later signals that the radio is back. Voices
follow a few seconds after that. Short warp taps do not ping every time: there is no warp-end ping within 15 s of the
previous one (`warpPingCooldown`). All scheduler and beep timers run in real time, so warp cannot cause a burst of calls.

## Settings

Click the toolbar button (antenna icon) in flight or map view. The window has seven tabs:

- **General**: Enabled, Muted (with its hotkey), the radio, intercom and beeps on or off, how much radio chatter
  (0.25x to 4x, for crews and probes alike, probe calls included) and the gap between intercom chats. The launch countdown with Start or Stop, its hotkey and "Stage on
  liftoff".
- **Sound**: volumes in percent (master, Mission Control, crew, intercom, callouts, beeps, static, EVA radio), how far
  the game audio drops under a voice, the roger beep and the master alarm.
- **Crew**: throttle callouts, maneuver node talk, the answer delay far from Kerbin, which events Mission Control
  reacts to, personality, facial expressions, turning to the speaker, and the EVA suit radio with its relay.
- **Probes**: probe mode, pet talk, the probe calling on its own (with its gap at 1x radio amount) and sleepy on low power. Under
  Reactions each v6 probe reaction has its own toggle.
- **Subtitles**: on or off, speaker name, colour per speaker, position, text size, background, number of lines and a
  preview.

- **Advanced**: landing and stage tones, voice boost, weak signal noise and dropouts, the signal mode (auto, off,
  fixed), the calm radio gap, the probe talk gap, how much a line should fit the speaker's job and character, the probe
  charge levels and beep pitch, and "Reset timing to defaults" (every TUNING value back to its default).
- **Debug**: the debug overlay, a test exchange, mouth and head turn calibration, a phrase test and the clip count per
  folder against its target (red when below target).

While the mouse is over the window, clicks and scrolling do not reach the game (camera, map, part menus, maneuver nodes).

Everything is saved in `GameData/Lonapse/PluginData/settings.cfg`. If that file cannot be read, the plugin uses
defaults and copies the broken file to `settings.cfg.bad` before it writes a new one.

When a newer version changes the default of a TUNING value, an older file is migrated on load. Only the values whose
defaults changed are reset (logged one by one with `[Lonapse]`). Everything else you set is kept, and a copy of the old
file is saved as `settings.cfg.v<N>.bak`. The key `tuningVersion` records the format (4 since 0.3.1). Version 4 replaces `intercomInterval`,
`intercomMaxStress` and `intercomAbortStress` only when they still hold an old default (600 or 240, 0.35, 0.5).

Other keys you can edit there:

| Key | Default | Meaning |
|---|---|---|
| `radioEnabled` | `True` | Crew <-> Mission Control radio |
| `intercomEnabled` | `True` | Crew-to-crew intercom chats |
| `beepsEnabled` | `True` | Pings, descent blips, stage tones |
| `landingTones` | `False` | Landing and splashdown tones |
| `stageTone` | `False` | Stage tone besides the pilot callout |
| `intercomVolume` | `0.8` | Volume of intercom chats |
| `muteKey` | `Y` | Unity KeyCode name of the mute hotkey |
| `muteModifier` | `LeftAlt` | KeyCode that must be held, or `None`. Left/right variants both count |
| `countdownKey` | `C` | KeyCode of the launch countdown hotkey (start on the pad, press again to stop), `None` = off. "Set key" in the window |
| `countdownModifier` | `LeftAlt` | KeyCode that must be held for the countdown hotkey, or `None` |
| `weakSignalVolume` | `0.4` | How loud the extra static, bursts and grit on Mission Control get at a weak signal, 0 to 1 (1 = the earlier v2 level) |
| `masterAlarm` | `True` | Soft two-tone caution loop far in the background while a kerbal panics |
| `useGameVolume` | `True` | Scale by the stock Voice volume (and Master, if KSP does not apply it already) |
| `useDsp` | `True` | Tremolo in an audio filter. Set `False` if you hear crackle, tremolo then runs on the volume |
| `maxCrewVoices` | `2` | Maximum overlapping crew voices (1 to 3) |
| `verboseLog` | `False` | Log every utterance |
| `calloutVolume` | `1.0` | Extra gain on callouts, 0 to 2 |
| `voiceBoostDb` | `6` | Gain on every voice after the compressor, 0 to 12 dB |
| `voiceCompressor` | `True` | Compressor on crew and MC voices |
| `duckMode` | `listener` | `listener` lowers the game audio under a voice, `off` disables it |
| `duckDepth` | `0.35` | How far the game audio ducks, 0 to 0.8 |
| `throttleCallouts` | `True` | Full throttle and throttle cutoff callouts |
| `roverStress` | `True` | Rover component in the stress model |
| `stationStyle` | `auto` | Stations and bases: `auto` (MC check-ins at low stress, exchanges at high), `check`, `exchange` |
| `radioProfile` | `auto` | Force `station`, `ship` or `eva` keying and band-pass (debug) |
| `evaRadio` | `True` | EVA suit radio |
| `evaRadioVolume` | `0.8` | Volume of everything on EVA, 0 to 1 |
| `evaRelay` | `True` | v6: the EVA suit radio reaches Mission Control through a nearby ship with a control point |
| `probeHail` | `True` | v6 probe mode: the probe calls Mission Control on its own, about every `hailInterval` (360 s) divided by `rateMultiplier` |
| `probeCallouts` | `True` | v6 probe mode: the probe beeps at staging, chutes and throttle, MC answers now and then (`probeCalloutMcChance` 0.35, at most once per `probeCalloutMcGap` 20 s) |
| `probePlanTalk` | `True` | v6 probe mode: MC tells the probe about a new maneuver node and the burn warning |
| `probeStressTalk` | `True` | v6 probe mode: under stress MC calms the probe about the main cause (heat, spin, g, fuel, ...) |
| `probeSayAgain` | `True` | v6 probe mode: below `sayAgainSignal` (0.35) MC sometimes asks "say again?" and the probe beeps again (`sayAgainChance` 0.35, `sayAgainCooldown` 120 s) |
| `probeMilestones` | `True` | v6 probe mode: mission day 1, 10, 100 and 365, first orbit and first landing per body. Remembered per session only |
| `probeRover` | `True` | v6 probe mode: probe rover moments (fast, air time, tilt, idle), one per `roverMomentCooldown` (180 s) |
| `probeTransmit` | `True` | v6 probe mode: data stream beeps while an antenna sends science (`dataBeepGap` 12 s, at most `dataBeepMax` 4 per send) |
| `planChatter` | `True` | Pre-planning exchange after a node change, warning before a burn |
| `signalMode` | `auto` | `auto` (CommNet, RemoteTech, distance to KSC), `off` (static ignores the signal), `fixed`. With CommNet off in the game (and no RemoteTech) the signal is full everywhere, like the game. The distance fallback only runs when CommNet is on but unreadable |
| `signalFixed` | `0.7` | Signal value for `fixed`, 0 to 1 |
| `signalOcclusion` | `True` | Distance fallback: no signal when Kerbin blocks the line to KSC |
| `subtitles` | `False` | Show an English subtitle for each line ("Enable subtitles" in the window) |
| `countdownAutoStage` | `False` | Launch countdown: activate the next stage on the liftoff call |
| `rogerBeep` | `True` | Short beep when the crew lets go of the key |
| `lightDelay` | `True` | MC answers later far from Kerbin: about 0.9 s at the Mun, 2 s at Minmus, up to `lightDelayMax` (10 s) at the outer planets |
| `signalDropouts` | `True` | Weak signal: MC lines get short gaps and a bit crunch |
| `TUNING { ... }` | | Every threshold, weight and time constant, see below |

New TUNING values in v2: `calloutHoldPad`, `duckAttack`, `duckRelease`, `throttleFullAt`, `throttleCutAt`,
`throttleMinGap`, `genderPitchFemale`, `genderPitchMale`, `launchCap`, `launchCapAlt`, `panicMinGap`, `panicCooldown`,
`speakerCooldown`, `talkBurst`, `talkPerMinute`, `panicEscTau`, `panicEscStep`, `panicEscCap`, `evaSpinWeight`, `wEva`,
`evaLowProp`, `evaLowEC`, `evaFarDist`, `wRover`, `roverSpeedLo/Hi`, `roverRoughLo/Hi`, `roverAirTimeLo/Hi`,
`roverTiltLo/Hi`, `dockSoftSpeed`, `dockHardSpeed`, `impulseDockHard`, `signalStaticMax`, `signalRangeFullKm`,
`signalRangeZeroKm`, `signalPollSec`, `signalHoldTime`, `planRateSec`, `burnWarnSec`, `radioKeyVolume`, `screamCooldown`,
`alarmVolume`, `alarmFadeIn`, `alarmFadeOut`, `alarmHold`.
Stress builds slowly (`attackTau` 3 s) and spin counts from `spinLo` 1 rad/s to `spinHi` 5 rad/s. Sudden events
(impulses, an imminent ground impact) use `fastAttackTau` 0.35 s. Tuning version 6 migrates the old defaults.
All are starting values, not measured ones.

## v2 behaviour in short

- Callouts (staging, parachutes, full throttle, throttle cutoff, docking distances) have their own channel. Chatter that
  is playing fades out in 0.15 s and the queue waits until the callout ended (`calloutHoldPad`). Warnings (hard dock,
  burn soon) also cut chatter, but never a callout.
- A Mission Control exchange has one crew speaker. Intercom talks use two.
- Panic lines are limited: at most one every `panicMinGap` seconds, one per kerbal per `panicCooldown`, and every
  panic line raises the panic threshold a little (`panicEscStep`, decays with `panicEscTau`). A token bucket
  (`talkBurst`, `talkPerMinute`) limits scheduled exchanges.
- During a launch, until `launchCapAlt`, the crew stays at "tense" at most, unless there is a real failure (heat,
  loss of control for 2 s, or an engine flameout with propellant left).
- A docking contact faster than `dockHardSpeed` is a hard dock: stress impulse and a warning exchange.
- The descent blips stop at touchdown, then the optional landing tone and one touchdown exchange follow.
- Radio keying: a desk click on stations and bases, a short headset squelch in ships and on EVA, also around pings.
- The MC static bed follows the signal. With CommNet on and no link, Mission Control is silent.
- Panic: the crew shouts in broken pieces with gasps in between. Mission Control answers slowly and calmly. A rare
  scream is possible, at most one per kerbal per `screamCooldown` (60 s). While someone panics a soft master alarm
  fades in at `alarmVolume` (0.12), stays `alarmHold` seconds after the panic and fades out slowly.
- Subtitles are English only, one toggle.
- The staging callout follows the stage contents: a decoupler or fairing gives a separation line (also when the stage
  has an engine too), an engine alone an ignition line, neither (for example only parachutes) a neutral "Stage!".
  `KSP.log` names the kind on every "Stage callout" line.
- Launch countdown (optional): on the pad, press "Start countdown" in the window or `Alt+C`. Launch control counts in Lonapse,
  so it counts up: "onU, soD, serT ... zeiD", one number per second, then "¡eugepseD!" (liftoff). Staging on or after
  "zeiD" is a normal launch. Staging earlier, or "Stop countdown", stops the count with a yell ("¡haaaaaaA!").
  With `countdownAutoStage` the next stage fires on the liftoff call. Chatter and throttle callouts wait during the count.
- With `useDsp = False` the voice boost and a `calloutVolume` above 1 go through the source volume, which stops at 1.

## Hotkey

`Alt+Y` toggles mute and shows a short screen message. `Alt+C` starts the launch countdown on the pad and stops it when it runs. Set another key with "Set key" in the window (Esc cancels, Del clears). It is ignored while a text field has focus or while the game
has locked keyboard input. Some Linux window
managers grab Alt key combinations. If so, set `muteModifier = None` or pick another key in `settings.cfg`.

## How stress works

Every physics tick the plugin reads the vessel state and computes components between 0 and 1:

| Component | Rises with | Weight |
|---|---|---|
| spin | angular velocity, 0.35 to 2.5 rad/s | 1.00 |
| gforce | G load, 2.5 to 8 g | 0.85 |
| q | dynamic pressure, 15 to 45 kPa | 0.60 |
| ground | descending fast close to the ground (time to impact under 25 s) | 0.90 |
| heat | hottest part relative to its max temperature, 55 to 92% | 0.80 |
| plasma | reentry plasma, same model as the CommNet blackout | 0.35 |
| fuel | under 10% fuel while the throttle is up (see below) | 0.40 |
| impulse | short kicks from staging, liftoff and parts exploding | 1.00 |

Spin, gforce and impulse are ignored while the vessel is on rails and for 3 s after loading, unpacking or a vessel
switch, because the physics state jitters then.

Fuel rule: propellants are grouped (LiquidFuel + Oxidizer as one group, SolidFuel, MonoPropellant, XenonGas). When
ignited engines are found, only the propellants they burn count, otherwise only liquid and solid count. Empty SolidFuel
never counts, so a spent SRB, sepratron or escape tower does not cause fuel stress. The vessel is low on fuel only
when every counting group is low (maximum over the groups).

They combine as `1 - product(1 - weight * component)`. One strong component is enough, several medium ones add up.
The result is smoothed: it rises fast (0.35 s), holds 1.5 s, then decays slowly (9 s). These times are real seconds,
also during physics warp.

Stress drives:

- **Rate**: mean time between calls goes from 110 s at stress 0 to 1 s at stress 1 (10.5 s at 0.5), with +-40% jitter.
- **Crew delivery**: calm, tense or panic clips. Under stress the crew gets louder and more distorted, and the pitch
  rises by at most 0.5 semitone (`crewStressSemitones`), with the total crew pitch capped at 1.12 (`crewPitchMax`).
  Lines are clipped only above personal stress 0.85, by at most 20% (`truncStart`, `truncMinFrac`). At most 2 crew
  voices overlap (`maxCrewVoices`), and overlap is rarer than before (`overlapMax` 0.35).
- **Mission Control**: gets calmer and slower as the crew gets more stressed.
- **Per kerbal**: the name seeds a stable pitch, tremolo rate and timbre (Jeb always sounds like Jeb). Courage and
  stupidity change how strongly each kerbal reacts and when they move to tense or panic. Badass kerbals are steadier.
  Kerbals knocked out by G stay silent.
- **G strain**: high G makes crew voices lower, muffled and wobbling.

Beeps: slow telemetry pings in orbit and coasting. Soft descent blips only below 500 m radar altitude while descending
(`descentMaxH`), from one every 5 s down to one every 1.5 s at most (`descentSlow`, `descentFast`), at a lower volume
(`descentVolLo`, `descentVolHi`). Landing and splashdown tones exist but are off by default (`landingTones`).

One-word callouts (radio, needs the folder `crew/callout`). One kerbal says one word, preferably the first pilot
aboard, otherwise any conscious crew member. It uses that kerbal's voice and the radio crew filters, and stress
changes pitch and volume as for other lines. Callouts happen:

- on staging. The callout is allowed as soon as the crew gate is open, so the launch stage does not wait for the
  chatter holdoff after scene load. If `crew/callout` has no clips, or the radio group is off, the `beeps/stage` tone
  plays instead (with the stage tone on, it plays as well). Staging is detected through `onStageActivate`, with a poll
  of `Vessel.currentStage` as a fallback. Every stage logs either the callout or the reason it was skipped.
- on parachute deployment. The first transition of a chute to semi-deployed speaks. Full deployment speaks again
  only when it is more than 3 s later (`chuteFullMinGap`). Chutes within 5 s of each other count as one group with one
  callout (`chuteGroupCooldown`). The parachute states are polled at 4 Hz on a cached module list.
- at the docking distances 30, 15 and 5 m (see Docking).

Staging and parachute callouts share a 1.5 s cooldown (`calloutCooldown`), so staging that opens chutes gives one word.

Reentry blackout: strong plasma first gives static and chopped transmissions, then radio silence. When it ends (after at
least 3 s of silence) you hear a static burst and a relieved exchange.

## Docking

The crew makes a short tense call and Mission Control answers when all of these hold for 1 s (`dockApproachHold`):

- the active vessel has a target vessel (or a docking port on it) closer than 50 m (`dockApproachDist`),
- the relative speed is below 2 m/s (`dockApproachSpeed`),
- the range is closing at more than 0.05 m/s (`dockClosingRate`),
- the target was seen beyond 50 m earlier in this approach.

So there is no call when you load next to a targeted vessel, switch between two parked ships, drift away after
undocking, or sit still inside 50 m. The call comes once per approach. After the call it re-arms only beyond 100 m
(`dockRearmDist`). A new target, a vessel switch, an undock or a dock resets it, and the next approach must again start
beyond 50 m. When docking completes the crew makes a relieved call.

Close approach. Once within 30 m (`dockCloseDist`) and closing (after the target was seen beyond 50 m), there is a
crew <-> MC exchange every 8 to 15 s (`dockCloseMin`, `dockCloseMax`). The crew line is calm, or tense when closing
faster than 0.4 m/s (`dockTenseClosing`). MC is calm, with quindar. Crossing 30, 15 and 5 m gives a short crew callout
from `crew/callout`, or a calm line cut to 1.2 s if that folder is empty (`dockCalloutLen`). The phase ends on contact
(relief call) or when backing out beyond 40 m (`dockCloseExit`). The log shows the approach start, each callout,
docking complete and undock.

Gentle docking does not count as stress. While the nearest docking ports are closer than 3 m (`dockGuardDist`) at low
speed, spin, G and small impulses are ignored. This lasts at most 5 s per approach (`dockGuardMax`). After contact
(`onPartCouple`) and docking it lasts another 2 s (`dockQuietTime`). The port distance is measured from your nearest
docking node to the targeted port, or to the nearest docking node on the target vessel. Part deaths always count. If a
part breaks during docking, the guard ends at once.

## Station and base mode

When the active vessel's type is Station or Base (set by you in the vessel rename dialog, works for planetary bases too):

- Calls come at half the rate (`stationRateFactor`).
- The crew mix is relaxed: below burst stress, a line is one band calmer with probability 0.6 (`stationCalmBias`).
- Mission Control starts and answers exchanges as usual (`stationMcFactor` 1.0).
- Mission Control also checks in on its own every 150 to 300 s (`stationMcMin`, `stationMcMax`): a calm MC line with
  quindar and a calm crew reply. The first check-in comes after half that time.
- Telemetry pings come at half the rate (`stationPingFactor` = 2, the interval multiplier).

The debug overlay shows the mode.

## Crew intercom chats

With at least two conscious crew aboard, two different kerbals sometimes chat over the cabin intercom: 2 to 4
alternating turns, never Mission Control. It sounds different from the radio. Crew radio lines go through a transmit band-pass of about
300 Hz to 4 kHz (`crewHp`, `crewLpCalm`, with the low-pass dropping further under stress), plus cabin echo and stress
distortion. The intercom has no quindar, no static and none of that band-pass. It has a wide band of 100 Hz to 8 kHz
(`intercomHp`, `intercomLp`), a small-room reverb that you can hear (`intercomRoomAtten` 600 mB, `intercomReverbAtten`
200 mB, `intercomReflAtten`, `intercomDecay`). A push-to-talk click plays at the start of every line and once after the
last line: 25 ms of filtered noise at 0.9 of the intercom volume (`intercomClickVolume`). It plays on its own audio
source, so it is never skipped when the crew channels are busy. The click is synthesized in code, so no files are
added to `Sounds/`. If the two kerbals' base pitches are closer than 0.05
(`intercomMinPitchGap`), the second voice is shifted so they stay apart.

- At stress 0 the mean interval is 300 s on ordinary ships (`intercomInterval`). It is 2x more often on long quiet
  coasts (stress under 0.1 for over 60 s: `intercomQuietBoost`, `quietCoastStress`, `quietCoastTime`), and 2.5x more
  often on stations and bases (`intercomStationBoost`, so 120 s).
- The crew talks more among themselves as stress rises. The interval shrinks log-linearly from that low-stress value to
  25 s at stress 0.6 (`intercomStressMin`, `intercomStressPeak`). Examples on a ship: 300 s at 0, about 87 s at 0.3,
  25 s at 0.6.
- Neither the crew count nor the radio rate multiplier changes the intercom rate.
- Each line uses the speaker's own band (calm, tense or panic), just like radio lines. So a brave kerbal can stay calm
  while a nervous one already panics.
- Above stress 0.7 (`intercomMaxStress`) no new chats start and the radio exchanges take over. If stress rises above
  0.78 (`intercomAbortStress`) during a chat, or one of the two partners passes out from G or leaves the vessel, the
  rest of the chat is dropped. Chats also pause during a reentry blackout.
- Chats follow the same crew-only gate as everything else.

## Tuning tips

Turn on the debug overlay. It shows every component, the smoothed stress, plasma values, the blackout state, the crew
gate reason, each kerbal's personal stress and band, the time to the next call and the playing channels.

- Too chatty in quiet flight: raise `tQuiet` (TUNING) or lower the rate multiplier.
- Not manic enough when spinning: lower `spinHi` (for example 1.8).
- Stays stressed too long after an event: lower `decayTau` or `holdTime`.
- Blackout never starts or starts too early: note `p`, `qv` and `shockTemp` in the overlay during a reentry and adjust
  `blackoutEnter` / `blackoutExit`. The qv and temperature thresholds come from the game's `Physics.cfg`.
- Landing feels too stressful: raise `groundVsLo` (descent speed where it starts to count).
- Crackle with the wobble effect: set `useDsp = False`.
- Docking call comes too early or too late: change `dockApproachDist` and `dockApproachSpeed`.
- Intercom chats too rare or too frequent: change `intercomInterval`, or turn them off in the window.
- Intercom reverb too strong or too weak: change `intercomRoomAtten` and `intercomReverbAtten` (higher means quieter reverb).
- Radio crew voices too thin: lower `crewHp` or raise `crewLpCalm`.

## Tests

`tests/run.sh` compiles the pure logic with plain Mono and runs it. It needs no KSP and no Unity. Covered:

- FNV-1a hash, stress components, combining and smoothing, the scheduler interval curve.
- Per-kerbal profile and bands, the shuffle bag, settings clamping and migration.
- Fuel groups, the docking watcher (approach, guard, close phase, callouts), warp ping cooldown.
- Intercom intervals (including the stress curve and band choice) and pitch gap.
- Crew pitch and truncation, descent blip interval.
- The click synthesis, and callout grouping and cooldown.

## What is untested

The offline tests pass and the plugin compiles against the KSP 1.12.5 DLLs. I play-test in game, but not every
feature has been tried. Not yet tried in game as of 0.5.0:

- The portrait cam talking and head turns with the end-of-frame refresh (`PortraitRefresh`).
- The engine-out hold, the EVA fall lines, the gradual EVA breathing and the solar topic filter.
- The migration from an old `GameData/Quindar/` install.
- RemoteTech (not installed on the test machine).

All TUNING defaults are starting values. They are not measured.
