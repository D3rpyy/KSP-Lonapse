# Lonapse: developer notes

This is the technical side of Lonapse. If you just want to play, the [README](../README.md) in the repo root is all
you need. This page is for people who want to build the mod themselves, tweak the settings file or find out why the
crew does what it does.

Lonapse plays radio chatter that reacts to what happens to your vessel. A calm orbit is nearly silent. A tumbling
capsule gets a loud, fast crew talking over each other. It is inspired by the Chatterer mod, but it contains none of
its code or sounds.

All voices speak loñapsE, the language of the kerbals. That is Spanish with the letters of every word reversed
("Separación" becomes "nóicarapeS"). The lines are rendered with the Piper text to speech and pitched up so they sound
like kerbals. How that works is in [audio/lonapse/README.md](../audio/lonapse/README.md). The credits for the voices
are in [audio/CREDITS.md](../audio/CREDITS.md).

## Building

First tell the scripts where your KSP install is. You can set `KSP_DIR`, or write the path into a file called
`.ksp_dir` in the repo root (one line, it stays out of git).

```bash
./build.sh                                  # compile and install the DLL into your game
./build.sh --compile-only                   # compile only, into build/
KSP_DIR=/path/to/KSP ./build.sh             # use another install just this once
python3 -I audio/build_clips.py --install   # build the sounds and copy them into your game
./package.sh                                # refresh GameData/Lonapse in the repo and make the release zip in dist/
```

`build.sh` compiles with `mcs` against the game's own DLLs in `KSP_Data/Managed/` and copies the result to
`GameData/Lonapse/Plugins/Lonapse.dll`. It only writes inside `GameData/Lonapse/`. It leaves `Sounds/` alone and it
never overwrites your `PluginData/settings.cfg`. It also refuses to install while KSP is running, because replacing a
loaded DLL breaks the running game.

If an old `GameData/Quindar/` install is found, `build.sh` copies its settings over and removes it. The plugin itself
stays silent while `Quindar.dll` is still loaded, so the two never talk at the same time.

This is what ends up in the game:

```
GameData/Lonapse/
  Plugins/Lonapse.dll
  Sounds/...                (from the audio pipeline)
  Phrases/phrases.tsv       (all scenes and subtitles, from the audio pipeline)
  PluginData/settings.cfg   (written by the plugin on first start)
```

The plugin also runs without any sounds. It then stays silent and logs a warning for each empty folder. You can find
everything the mod logs in `KSP.log` on lines starting with `[Lonapse]`.

## Audio folders

The sounds live in `GameData/Lonapse/Sounds/<folder>/`. The plugin finds them by folder. The Debug tab shows how many
clips each folder has against its target, in red when it has too few.

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
| `static/breath_loop` | 1 | helmet breathing on EVA |
| `beeps/roger` | 1 | roger beep at the end of a crew transmission |

The crew and control folders hold the voice clips, named `<clip_key>_m.ogg` and `<clip_key>_f.ogg`. Male kerbals get
the `_m` clips and female kerbals the `_f` clips. The plugin reads `Phrases/phrases.tsv` to put the clips together into
scenes, like a crew line with an answer from Mission Control, or a chat between two kerbals. Without the table it
falls back to random clips per folder.

If `crew/callout` is empty, staging uses the stage tone and docking callouts use a short calm line instead.

## When does it play

Only in flight. A crewed vessel gets the crew and Mission Control. An uncrewed vessel with a probe core gets probe mode,
where the probe beeps and Mission Control talks to it.

On EVA your kerbal has a suit radio to Mission Control (`evaRadio`, on by default). You get a few check-ins, soft
breathing in the helmet and the headset squelch. It stays calm unless something is really wrong, like running out of
jetpack fuel far from any ship. The suit has no antenna of its own, so it relays through the best ship nearby
(`evaRelay`). That ship needs a command pod or probe core. The kerbal gets the ship's signal, full up to 1 km
(`evaRelayFullKm`) and gone at 2.3 km (`evaRelayZeroKm`). So a weak ship signal means a weak suit radio too.

The breathing speeds up when your kerbal runs. Running builds exertion over about 20 s (`evaExertRise`) and it fades
over 30 s (`evaExertFall`). At its peak the breathing is 1.4x as fast (`evaBreathRate`) and 1.5x as loud
(`evaBreathGain`). Hops and falls never count as a landing or as something hitting the kerbal. Tumbling on the
ground doesn't count as spinning, also not in the first 3 s after a fall (`evaSettleTime`). When your kerbal falls over, they have a short talk with
Mission Control about it, at most once every 45 s (`evaFallCooldown`).

When nobody is left on board (EVA, a crew transfer, switching to another vessel) everything fades out within 0.3 s.
When the vessel is destroyed the sound stops at once. The pause menu pauses all sounds. Map view keeps playing.

During time warp on rails everything is silent. Physics warp plays normally. When you come out of warp a short ping
tells you the radio is back, and the voices follow a few seconds later. If you tap warp on and off a lot, the ping
waits at least 15 s between two warps (`warpPingCooldown`). All timers run in real time, so warp can never cause a
burst of calls.

## Settings

Click the antenna button on the toolbar in flight or map view. The window has seven tabs:

- **General**: on or off, mute (with its hotkey), the radio, the intercom and the beeps, how much radio chatter you
  want (0.25x to 4x, for crews and probes alike) and the gap between intercom chats. Also the launch countdown with its
  hotkey and "Stage on liftoff".
- **Sound**: all volumes (master, Mission Control, crew, intercom, callouts, beeps, static, EVA radio), how much the
  game sound drops while someone talks, the roger beep and the master alarm.
- **Crew**: throttle callouts, maneuver node talk, the answer delay far from Kerbin, which events Mission Control
  reacts to, personality, facial expressions, turning to the speaker and the EVA suit radio.
- **Probes**: probe mode, pet talk, the probe calling on its own and sleepy on low power. Under Reactions every probe
  reaction has its own toggle.
- **Subtitles**: on or off, speaker name, colour per speaker, position, text size, background, number of lines and a
  preview.
- **Advanced**: landing and stage tones, voice boost, weak signal noise and dropouts, the signal mode (auto, off,
  fixed), the calm radio gap, the probe talk gap, how much a line should fit the speaker's job and character, the probe
  charge levels and beep pitch, and "Reset timing to defaults".
- **Debug**: the debug overlay, a test exchange, mouth and head turn calibration, a phrase test and the clip count per
  folder.

While your mouse is over the window, clicks and scrolling don't reach the game, so you won't move the camera or drag a
maneuver node by accident.

Everything is saved in `GameData/Lonapse/PluginData/settings.cfg`. If that file is broken, the plugin starts with the
defaults and keeps the broken file as `settings.cfg.bad`.

When an update changes a default, your file is updated when the game loads. Only values you never changed are moved to
the new default. Everything you set yourself is kept, and a copy of the old file is saved as `settings.cfg.v<N>.bak`.

These keys are not all in the window, but you can edit them in the file:

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
| `weakSignalVolume` | `0.4` | How loud the extra static, bursts and grit on Mission Control get at a weak signal, 0 to 1 |
| `masterAlarm` | `True` | Soft two-tone caution loop far in the background when the stress reaches `alarmStress` (0.9) |
| `useGameVolume` | `True` | Scale by the stock Voice volume (and Master, if KSP does not apply it already) |
| `useDsp` | `True` | Tremolo in an audio filter. Set `False` if you hear crackle, tremolo then runs on the volume |
| `maxCrewVoices` | `2` | Maximum overlapping crew voices (1 to 3) |
| `verboseLog` | `False` | Log every line that plays |
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
| `evaRelay` | `True` | The EVA suit radio reaches Mission Control through a nearby ship with a control point |
| `probeHail` | `True` | Probe mode: the probe calls Mission Control on its own, about every `hailInterval` (360 s) divided by `rateMultiplier` |
| `probeCallouts` | `True` | Probe mode: the probe beeps at staging, chutes and throttle, MC answers now and then (`probeCalloutMcChance` 0.35, at most once per `probeCalloutMcGap` 20 s) |
| `probePlanTalk` | `True` | Probe mode: MC tells the probe about a new maneuver node and the burn warning |
| `probeStressTalk` | `True` | Probe mode: under stress MC calms the probe about the main cause (heat, spin, g, fuel, ...) |
| `probeSayAgain` | `True` | Probe mode: below `sayAgainSignal` (0.35) MC sometimes asks "say again?" and the probe beeps again (`sayAgainChance` 0.35, `sayAgainCooldown` 120 s) |
| `probeMilestones` | `True` | Probe mode: mission day 1, 10, 100 and 365, first orbit and first landing per body. Remembered per session only |
| `probeRover` | `True` | Probe mode: probe rover moments (fast, air time, tilt, idle), one per `roverMomentCooldown` (180 s) |
| `probeTransmit` | `True` | Probe mode: data stream beeps while an antenna sends science (`dataBeepGap` 12 s, at most `dataBeepMax` 4 per send) |
| `planChatter` | `True` | Pre-planning exchange after a node change, warning before a burn |
| `signalMode` | `auto` | `auto` (CommNet, RemoteTech, distance to KSC), `off` (static ignores the signal), `fixed`. With CommNet off in the game (and no RemoteTech) the signal is full everywhere, like the game. The distance fallback only runs when CommNet is on but unreadable |
| `signalFixed` | `0.7` | Signal value for `fixed`, 0 to 1 |
| `signalOcclusion` | `True` | Distance fallback: no signal when Kerbin blocks the line to KSC |
| `subtitles` | `False` | Show an English subtitle for each line ("Enable subtitles" in the window) |
| `countdownAutoStage` | `False` | Launch countdown: activate the next stage on the liftoff call |
| `rogerBeep` | `True` | Short beep when the crew lets go of the key |
| `lightDelay` | `True` | MC answers later far from Kerbin: about 0.9 s at the Mun, 2 s at Minmus, up to `lightDelayMax` (10 s) at the outer planets |
| `signalDropouts` | `True` | Weak signal: MC lines get short gaps and a bit of crunch |
| `TUNING { ... }` | | Every threshold, weight and time constant, see below |

The `TUNING` block holds all the numbers behind the behaviour. They are my own starting values from playing, not
measured ones. If something feels off, this is where you can change it. The tuning tips further down help with that.

## How it behaves

- **Callouts come first.** Staging, parachutes, full throttle, throttle cutoff and docking distances have their own
  channel. Any chatter that is playing fades out in 0.15 s and waits until the callout is done. Warnings (a hard dock,
  a burn coming up) also cut the chatter, but never a callout.
- **One kerbal on the radio.** In a talk with Mission Control one crew member speaks. Intercom chats use two.
- **Panic stays special.** At most one panic line every `panicMinGap` seconds, and one per kerbal per `panicCooldown`.
  Every panic line also makes the next one a little harder (`panicEscStep`). A token bucket (`talkBurst`,
  `talkPerMinute`) stops the radio from flooding.
- **No broken record.** Once the crew has talked about a cause (like vibration, spin or heat), they don't bring up the
  same cause again for 45 s (`causeRepeatGap`). This holds at any radio amount, so a high setting at max Q doesn't
  turn into "this is shaking a lot" over and over.
- **Launches are tense, not panicky.** Up to `launchCapAlt` the crew stays at tense at most, unless something is really
  wrong: heat, losing control for 2 s, or an engine flameout with fuel left.
- **Hard docks.** Touching faster than `dockHardSpeed` gives a stress kick and a warning from Mission Control.
- **Touchdown.** The descent blips stop when you land, then the crew and Mission Control talk about the landing.
- **The radio clicks.** A desk click on stations and bases, a short headset squelch in ships and on EVA.
- **Static follows your signal.** With CommNet on and no link, Mission Control can't hear you and stays silent.
- **Panic sounds like panic.** The crew shouts in broken pieces with gasps in between. Mission Control answers slowly
  and calmly. Now and then someone screams, at most once a minute per kerbal (`screamCooldown`).
- **The master alarm is for real trouble.** A soft alarm plays far in the background only when the stress of the
  vessel reaches 0.9 (`alarmStress`). A nervous kerbal panicking at low stress doesn't set it off.
- **The staging callout knows what you staged.** A decoupler or fairing gives a separation line, an engine an
  ignition line, and anything else (like only parachutes) a plain "Stage!".
- **Launch countdown.** On the pad, press "Start countdown" in the window or Alt+C. Launch control counts in loñapsE,
  so it counts up: "onU, soD, serT ... zeiD", one number per second, then "¡eugepseD!" (liftoff). Staging on or after
  "zeiD" is a normal launch. Staging earlier, or pressing stop, ends the count with a yell ("¡haaaaaaA!"). With
  `countdownAutoStage` the next stage fires on the liftoff call. Chatter and throttle callouts wait during the count.
- With `useDsp = False` the voice boost and a `calloutVolume` above 1 go through the normal volume, which stops at 1.

## Hotkeys

Alt+Y mutes and unmutes and shows a short message on screen. Alt+C starts the launch countdown on the pad and stops it
while it runs. You can pick other keys with "Set key" in the window (Esc cancels, Del clears). The hotkeys do nothing
while you are typing in a text field. Some Linux window managers grab Alt combinations. If a hotkey does nothing, set
`muteModifier = None` or pick another key.

## How stress works

Stress is what makes the crew go from calm to tense to panic. Every physics tick the plugin looks at the vessel and
works out these parts, each between 0 and 1:

| Component | Rises with | Weight |
|---|---|---|
| spin | angular velocity, 1 to 5 rad/s | 1.00 |
| gforce | G load, 2.5 to 8 g | 0.85 |
| q | dynamic pressure, 15 to 45 kPa | 0.60 |
| ground | descending fast close to the ground (time to impact under 25 s) | 0.90 |
| heat | hottest part relative to its max temperature, 55 to 92% | 0.80 |
| plasma | reentry plasma, same model as the CommNet blackout | 0.35 |
| fuel | under 10% fuel while the throttle is up (see below) | 0.40 |
| impulse | short kicks from staging, liftoff and parts exploding | 1.00 |

Spin, G force and impulse are ignored while the vessel is on rails and for 3 s after loading or switching vessels,
because the physics jump around a bit then.

Fuel only counts for the propellants your running engines actually burn. Liquid fuel and oxidizer count as one group.
An empty solid booster never counts, so a spent SRB, sepratron or escape tower doesn't scare anyone. The vessel is only
low on fuel when every group that counts is low.

The parts combine as `1 - product(1 - weight * component)`. So one strong part is enough, and several medium ones add
up. The result is smoothed: it rises over about 3 s (`attackTau`, 0.35 s for sudden events), holds 1.5 s and then
slowly drops off (9 s). These are real seconds, also during physics warp.

What stress changes:

- **How often they talk**: from about once every 110 s at stress 0 to once a second at stress 1 (10.5 s at 0.5),
  with some randomness on top.
- **How the crew sounds**: calm, tense or panic lines. Under stress the crew gets louder and rougher, and the pitch
  goes up by at most half a semitone (`crewStressSemitones`, total pitch capped by `crewPitchMax`). Above personal
  stress 0.85 lines can get cut off a bit. At most 2 crew voices overlap (`maxCrewVoices`).
- **Mission Control**: gets calmer and slower the more stressed the crew is.
- **Every kerbal is different**: the name sets a fixed pitch, wobble and timbre, so Jeb always sounds like Jeb.
  Courage and stupidity change how strongly a kerbal reacts and when they move to tense or panic. Badass kerbals stay
  steadier. Kerbals that black out from G stay silent.
- **G strain**: high G makes the crew voices lower, muffled and shaky.

Beeps: slow telemetry pings in orbit and while coasting. Soft descent blips below 500 m while you come down
(`descentMaxH`), getting faster from one every 5 s to one every 1.5 s (`descentSlow`, `descentFast`). Landing and
splashdown tones exist but are off by default (`landingTones`).

Callouts: one kerbal says one word, preferably the first pilot on board. They use their own voice, and stress changes
the pitch and volume like any other line. Callouts happen:

- **on staging**, right from the launch, without waiting for the chatter to start. If there are no callout clips, or
  the radio is off, the stage tone plays instead. Every stage logs the callout, or why it was skipped.
- **on parachutes**. The first chute opening gets a callout. Full deployment gets another one only if it comes more
  than 3 s later (`chuteFullMinGap`). Chutes within 5 s of each other count as one (`chuteGroupCooldown`).
- **at the docking distances** 30, 15 and 5 m (see Docking).

Staging and parachute callouts share a 1.5 s cooldown (`calloutCooldown`), so staging that opens the chutes gives one
word, not two.

Reentry blackout: strong plasma first gives static and chopped up talk. Then Mission Control loses you completely.
The crew keeps talking on the radio, and staging and other callouts still work, but Mission Control can't hear them
and never answers. Telemetry pings stop too. When the blackout ends (after at least 3 s), you hear a static burst and a
relieved crew. A probe has nobody on board to talk, so for probes the radio goes fully silent.

## Docking

When you come in to dock, the crew makes a short call and Mission Control answers. That happens when all of these hold
for 1 s (`dockApproachHold`):

- your target (or a docking port on it) is closer than 50 m (`dockApproachDist`),
- you are slower than 2 m/s relative to it (`dockApproachSpeed`),
- you are closing in at more than 0.05 m/s (`dockClosingRate`),
- the target was further than 50 m away earlier in this approach.

So you don't get a call when you load next to your target, switch between two parked ships, drift away after undocking
or just sit still close by. The call comes once per approach. It only comes back after you go beyond 100 m again
(`dockRearmDist`), or after a new target, a vessel switch, an undock or a dock.

Close in, within 30 m (`dockCloseDist`), the crew and Mission Control talk every 8 to 15 s (`dockCloseMin`,
`dockCloseMax`). The crew is calm, or tense when you close faster than 0.4 m/s (`dockTenseClosing`). Crossing 30, 15
and 5 m gives a short callout. This ends when you dock (with a relieved call) or back out beyond 40 m
(`dockCloseExit`).

A gentle docking doesn't stress anyone. While the docking ports are closer than 3 m (`dockGuardDist`) at low speed,
spin, G and small bumps are ignored, for at most 5 s per approach (`dockGuardMax`). After contact this lasts another
2 s (`dockQuietTime`). Parts breaking always count, and they end the guard right away.

## Stations and bases

When you set a vessel's type to Station or Base (in the rename dialog, this works for ground bases too):

- The crew talks half as often (`stationRateFactor`).
- The crew is more relaxed: below high stress, a line is one band calmer 60% of the time (`stationCalmBias`).
- Mission Control checks in on its own every 150 to 300 s (`stationMcMin`, `stationMcMax`), and the crew answers.
- Telemetry pings come half as often (`stationPingFactor`).

The debug overlay shows when station mode is on.

## Crew intercom chats

With at least two kerbals awake on board, two of them sometimes chat over the cabin intercom: 2 to 4 lines back and
forth, without Mission Control. It sounds different from the radio on purpose. The radio has a narrow band of about
300 Hz to 4 kHz, plus cabin echo and some distortion under stress. The intercom has no quindar tones and no static. It
has a wide band of 100 Hz to 8 kHz and a small room reverb (`intercomRoomAtten`, `intercomReverbAtten`). A short push
to talk click plays at the start of every line and once at the end. If two kerbals sound too much alike (base pitches
closer than `intercomMinPitchGap`), the second voice is moved a bit so you can tell them apart.

- On a normal ship at low stress they chat about every 300 s (`intercomInterval`). On long quiet coasts it is twice
  as often (`intercomQuietBoost`), and on stations and bases 2.5x as often (`intercomStationBoost`).
- They talk more as stress rises, down to every 25 s at stress 0.6 (`intercomStressPeak`). On a ship that is 300 s at
  stress 0, about 87 s at 0.3 and 25 s at 0.6.
- The radio amount slider doesn't change the intercom. It has its own setting.
- Every kerbal speaks in their own stress band. So a brave kerbal can stay calm while a nervous one is already
  panicking.
- Above stress 0.7 (`intercomMaxStress`) no new chats start and the radio takes over. If stress goes above 0.78
  (`intercomAbortStress`) during a chat, or one of the two blacks out or leaves, the rest of the chat is dropped. Chats
  also pause during a reentry blackout.

## Tuning tips

Turn on the debug overlay first. It shows every stress part, the total stress, the plasma values, the blackout state,
each kerbal's own stress and band, the time to the next call and what is playing right now.

- Too chatty in quiet flight: raise `tQuiet` in TUNING, or lower the radio amount.
- Not panicky enough when spinning: lower `spinHi` (for example 1.8).
- Stays stressed too long after something happens: lower `decayTau` or `holdTime`.
- Blackout starts too early or never: watch `p`, `qv` and `shockTemp` in the overlay during a reentry and change
  `blackoutEnter` and `blackoutExit`.
- Landings feel too stressful: raise `groundVsLo` (the descent speed where it starts to count).
- Crackle on the voices: set `useDsp = False`.
- Docking call too early or too late: change `dockApproachDist` and `dockApproachSpeed`.
- Intercom chats too rare or too often: change `intercomInterval`, or turn them off in the window.
- Intercom reverb too strong or too weak: change `intercomRoomAtten` and `intercomReverbAtten` (higher is quieter).
- Crew radio voices too thin: lower `crewHp` or raise `crewLpCalm`.

## Tests

`tests/run.sh` compiles the logic that doesn't need the game and runs it with plain Mono. You don't need KSP or Unity
for it. It covers the stress model, the kerbal profiles, the settings and their migration, the fuel rules, docking,
warp, the intercom, the phrase table, probe mode, the subtitles and more.

## What is untested

The offline tests pass and the plugin compiles against the KSP 1.12.5 DLLs. I play-test in game, but I haven't tried
these yet:

- RemoteTech.

If you try any of these, I would love to hear how it went!
