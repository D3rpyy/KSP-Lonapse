# Changelog

## 0.8.2

### New

- **Airlock sounds.** Docking and undocking have an airlock sound now. It plays on contact when you dock and when you undock. 
- **Undocking talks.** About five seconds after an undock, when the airlock sound is done, a kerbal says a line over the radio. A probe beeps does this too with beeps.
- **A calm docking is no longer silent.** A slow close approach, under 0.4 m/s, used to get no lines at all. Now a kerbal calls the distance and Mission Control answers. A fast, tense approach still plays the recorded scenes.
- **Distance callouts.** At 500, 250 and 100 m, and again at 30, 15 and 5 m, a kerbal calls the distance and Mission Control answers. The same goes for the probes Before, a probe was silent until the dock itself.
- **Other chatter waits during a docking approach.** From the approach call until the dock, an undock, a lost target or at most 240 s, no other chatter, chat, report or event call starts. Crew lost and parts lost still come through, and so does everything once the crew gets stressed.
- **The mod checks that you mean to dock.** Before the docking calls start, you must target another ship. The mod looks for three signs: the target has a free port of the same size, you fly from one of your ports (Control From Here) or target a port, and the two ports face each other. With two of them the docking call always comes, so a normal docking needs no Control From Here. With one it often comes. Going up on debris or an asteroid now rarely gets a docking call.
- Added new settings under Sounds to toggle the new sound effects.
- **Flag sound.** Planting a flag has its own sound.
- **Enter/exit sound** Entering and exiting the ship now have sounds too.
- **Jetpack sounds.** Switching the EVA jetpack on or off has a click and a hiss. While the thrusters fire, a soft hiss loops under the radio and fades out when you let go.
- **Ground thuds.** A kerbal who falls over on the ground gets a heavy thud, at the same moment as the fall lines. Landing after a jump or a jetpack flight gets a lighter one.
- **Footsteps.** An EVA kerbal makes footsteps. Walking, running, backwards and sideways follow the playing walking animation. I
- **Volume sliders for the sound effects.** Hatch, airlock, jetpack (switch and thrusters together), helmet breathing, ground thud and footsteps each have a slider to adjust the volume.
- **Science talk is spoken live now.**  The lines can say what really was tested by the player. Every stock experiment has 28 to 31 talks of its own, and 29 more fit any experiment. There are 535 total talks. If you do multiple experiments at the same time, they will be sent as 1 batched radio message instead of X separate radio messages
- **More probe talk.** Every probe core has 10 new scenes with Mission Control: chats, hails and sleepy moments, in the character of that core. These are done on runtime.

### Changed

- **The settings window is wider.** The name column is wider too, so long names fit and the value at the right of a slider stays in view.

### Fixed

- **Fall lines wait until the kerbal is calm.** After a fall, the talk starts when the kerbal has not been tumbling for 1.5 s, and at the latest 12 s after the first fall. Before, it could start in the middle of a second fall.

- **Kerbals no longer sometimes sound very deep and slow.** Each kerbal plays at their own pitch, pushed apart from the rest of the crew and lowered a little under G. For a low-voiced kerbal this could undo the whole Kerbal shift. It happened most on stations with many kerbals, in intercom chats and under high G. The pitch now has a floor, so every kerbal keeps the Kerbal sound. On a big station the voices sit a bit closer together because of this. G strain lowers the voice by at most 6% instead of 12%.

- **Tense and panicking kerbals no longer sound like chipmunks.** Under stress the crew voice went up in pitch, in the recordings and again in game. That is gone. A tense or panicking kerbal now has their normal voice and only talks a bit faster.

- **Reset data gets no report.** The talk waits until the results dialog is closed. An experiment you reset or discard there is left out. Data you keep or transmit still gets its report.

### For modders

- The runtime tables have three new kinds, `undock`, `dock_close` and `dock_callout`, and three new events for `on=`, `undock`, `dockclose` and `dockcallout`. The lines are in `Phrases/Runtime/undock.tsv` and `dock_close.tsv`.
- New runtime kind `science_talk`: a kerbal and Mission Control after an experiment, in `Phrases/Runtime/science_talk.tsv`. New values `{temp_c}`, `{press_kpa}`, `{grav_g}`, `{sci_flight}` and `{exp_runs}`. See docs/API.md.

## 0.8.1

**Tourists get their own voice, DeepFreeze is supported and the runtime voice no longer cuts the audio or crashes the game.** Most of this version comes from your feedback on 0.8.0. Thanks for the reports!

### New

- **Tourists.** Tourists have their own lines. They chat with the crew, with each other and on the radio, and they sound like what they are: passengers who never trained. Under stress they panic and ask silly questions instead of reporting like a pilot. Mission Control no longer talks to them about a career they don't have. 60 new chats: a tourist talking to the crew, the crew talking to a tourist and two tourists together.
- **Tourist ships with a probe core.** A ship with only tourists and a probe core runs in probe mode, with the probe as the crew. Now and then a tourist calls a stage and the probe answers.
- **DeepFreeze.** Freezing a kerbal no longer makes Mission Control think they died. The crew and Mission Control talk about it instead: a freeze, a thaw (the thawed kerbal speaks first) and a kerbal who came out comatose after a power failure. Two kerbals get one line with both names, three or more a line for the whole group. If everyone aboard is frozen, Mission Control still calls the freezer. A kerbal who dies frozen is still reported. The lines only load when DeepFreeze is installed, and they have their own switch in the Mods tab. Tested with DeepFreeze Updated 1.33.0.3.

### Changed

- **Voices load with the save.** The Piper voices load once when a save opens and stay loaded for the whole session. The flight start no longer cuts the audio.
- **Panic sounds less harsh.** Panic clips are never cut off, get no extra stress pitch or distortion and play a little softer. Mission Control stays steady.
- **Faces follow the stress.** Faces get more scared step by step, at each kerbal's own stress levels. A kerbal who listens shows no more fear than they feel themselves.
- **Lines fit the ship.** Lines about solar panels, RCS, landing legs, chutes and the heat shield only play when the ship has that part, in the right state.
- **More answers.** 14 Mission Control lines that had no reply now have one, and a kerbal that Mission Control calls by name answers more often.
- **Planet packs.** The mission day follows the game clock, so packs with their own day length (JNSQ, RSS, Kronometer) get the right day. KSP.log says which day length is used. Sunrises use the star the planet goes around, for packs with more than one star.
- **The runtime voice runs below the game** in priority, so it takes less from the game.

### Fixed

- A crash in flight: a log line from the runtime voice reached the KSP debug console from another thread.
- A cabin with a tourist no longer goes quiet when the tourist has nothing to say. The rest of the crew chats instead.

### For modders

- **Compat lines.** Lonapse can now ship lines for another mod in `GameData/Lonapse/Compat/<Assembly>/`. They load only when that plugin is loaded, and they count as lines of that mod in the Mods tab. A mod's own row with the same id wins.
- **The named kerbal.** When `Trigger` names a kerbal and that kerbal is gone or out cold by the time the line plays, nobody else says their chat. Only the Mission Control line can still play.

## 0.8.0

**Your kerbals talk more, and with each other.** They answer Mission Control, talk over the radio between ships and on EVA, keep talking without a signal, and chat during time warp. Additionally, I spent some time trying to improve the talking animations the kerbals have. Most new lines need the runtime voice, so "Speak names and new lines at runtime" in the General tab should be on.

### New

- **Chatter during time warp.** Time warp is no longer silent. The crew and Mission Control keep talking, also about the next burn or the next body. The faster the warp, the more they talk and the faster their voices sound. "Chatter during time warp" in the General tab turns it off, and two sliders under it set how much.
- **Replies on reports.** Now and then a kerbal replies to Mission Control's reports, mostly with a joke.
- **Radio between ships and on EVA.** Kerbals on EVA nearby and the crews of other vessels within 2.3 km join the radio. A kerbal outside talks with the ship, a lander talks with the mothership, and two kerbals on EVA chat over their suit radio. "Nearby kerbals join the radio" in the General tab turns it off.
- **Talk without a signal.** Without a link to Mission Control, the kerbals still talk to each other during EVA about everything EVA.
- **More to say on EVA.** New lines about the jetpack, the distance to the ship, the time outside and the place. The radio is busier on EVA, like a real spacewalk.
- **Science with the place.** EVA reports and surface samples now say where they were taken: "Control, Midlands sample in the bag." Deploying a Breaking Ground part gets a line with its name.
- **Flags.** Mission Control says something when you plant a flag, and the kerbal who planted it answers.
- **Biomes and sunrises.** Lines can say the biome below you, planet packs included. In orbit, Mission Control now and then counts the sunrises.
- **Personalities in chats.** New chats for a dim kerbal, a nervous one, a brave one and a veteran.
- **Faces and mouths.** The mouth follows the words better and closes in the pauses. A scared kerbal no longer grins while saying a calm line. Kerbals nod a little when they talk or listen.
- **Lines from other mods.** Other mods can now add their own lines to Lonapse. The new Mods tab lists them, with a switch for each mod.
- **Subtitle time.** A "Stay on screen" slider sets how long a subtitle stays after its line.

### Changed

- **More variety.** Mission Control's reports, science and contract lines and the crew chats have many more wordings and topics, so they repeat less. More recorded chats say the other kerbal's name, and the word lists are longer.
- **Real numbers in more lines.** With the runtime voice, Mission Control also says real values in ordinary lines: the battery, the fuel after a cutoff, the delta-v of a new maneuver node.
- **Fewer names.** Kerbals and Mission Control say names less often. Mission Control no longer picks a kerbal at random to talk to.
- **The right kerbal speaks.** The pilot talks while flying, the engineer about power and broken parts, the scientist about science.
- **Warp talk fits the warp.** During time warp most lines are about the warp, and a line about the warp never plays after it ends.
- **Right click goes back.** Buttons that step through a list go back with a right click.

### Fixed

- On EVA, lines about the ship now name the ship, not the kerbal outside. Lines about the hatch only play right after you leave, and only near the ship.
- Switching vessels while someone talks no longer gives the line to the wrong kerbal.
- The voice now says the same name or value as the subtitle, in every line.
- Changing the stage order in flight no longer gives a staging callout.
- The voice no longer tries to say vessel names in another alphabet. The subtitle still shows them.
- A fresh rocket no longer reports the wrong fuel percentage. Fuel lines count the next stage, and none play on the launch pad.

### For modders

- **An API for other mods.** Give the events of your mod a voice: ship lines in `GameData/<YourMod>/Lonapse/` and call `Trigger` through a small wrapper file. There is no dependency either way, and Module Manager is not needed. Your mod can also name its own experiments, Breaking Ground parts and contract types. Everything is in `docs/API.md` on GitHub, with an example in `docs/api` and a test mod in `tests/ingame`. I tested the functionality with a made-up mod, not with a real one yet. If you do, please provide feedback on what could be improved!
- **Tools in the Mods tab.** "Show the tools for modders" reloads all lines without a restart, tests each event and lists the last ones. Made so that you won't have to restart KSP every time you want to add a new line.
- **Probe cores.** The stock probe cores get `ModuleLonapse` through an optional Module Manager patch. A patch of your own can change them with `:AFTER[Lonapse]`. If MM is not installed, it falls back to the default.
- **Runtime lines.** `Phrases/runtime.tsv` is now the folder `Phrases/Runtime/`. Every `.tsv` in it is read, so your own lines can go in a file of your own. `Phrases/slots_es.tsv` is gone, the Spanish is now the `spanish_rt` column. The install removes the old files.
- **New kinds and slots.** New kinds `warp`, `sunrise`, `reply`, `event`, `meta`, `experiment`, `device` and `contract`. Chats can ask for a persona, and play on an event with `on=`. The slots are listed at the top of `Phrases/Runtime/reports.tsv`.

## 0.7.0 (2026-11-06)

**Kerbals now say your names, and Mission Control reads your real numbers.** Until now every line was a recording made before release, so the voice could never say the name of your kerbal, your vessel or the place you are at. This version ships a small speech engine (Piper) with the same three voices, and it speaks new lines while you play. That adds 53 Mission Control lines and 22 crew chats (56 lines) with real values, on top of 6478 new subtitle versions, 29 scenes with subtitle slots and 138 new probe scenes. The voice still speaks loñapsE. Only names are said as they are.

### New

- **Runtime voice.** It is on by default. Turn it off with "Speak names and new lines at runtime" in the Crew tab, then only the recorded lines play. Piper runs as a separate program next to KSP. A line that is not ready within a second plays as before, without the name, so nothing ever waits for it.
  - **Names.** Mission Control sometimes ends a line with the name of the kerbal who just spoke, or with the name of your vessel. On a probe it uses the vessel name. In crew chats on the intercom, a kerbal sometimes says the other one's name. On a planet from a mod, lines about "the planet" say its real name.
  - **Reports with real numbers.** Now and then Mission Control reports your orbit, altitude, speed, fuel of the burning stage, battery, the next burn, the mission day, funds, science or reputation. Low over the ground it reads your height and descent speed, and on a docking approach the distance to the target. Big numbers are said short, in thousands, millions and billions. The numbers are spoken in loñapsE, the subtitle shows them in digits. Turn this off with "Mission Control reports with numbers".
  - **Science and contracts.** After some science reactions Mission Control says the experiment, the body and the science points. A finished contract gets its funds, a failed one a short line. Turn this off with "Science points and contract funds".
  - **Crew history.** Mission Control knows your crew's career. It can tell a kerbal that this is their first flight or their tenth, their first time at Duna, that they landed here before, or what level they are. Two kerbals in a chat can talk about it too.
  - **Crew chats with values.** 22 intercom chats use real values and the names of the two kerbals. Some only play with the right jobs, like a pilot and an engineer talking about the fuel.
  - **The voice says what the subtitle shows.** For 65 lines with subtitle versions and slots, every version has its own Spanish. The voice speaks the version you see, with the word that was filled in: the thing that is lost, the food, the part, the vessel or the name of the other kerbal. Before, these lines always played the same recording, whatever the subtitle said. The Spanish is in `GameData/Lonapse/Phrases/slots_es.tsv` (65 lines and 172 values). The other subtitle versions still play their recorded clip.
  - **Same sound as the recordings.** Runtime lines use the same voice, pitch and delivery as the recorded lines around them. A kerbal keeps their own voice, and a tense or panicked line is as fast and high as the recorded ones.
- **Write your own lines, no audio needed.** The runtime lines are templates in `GameData/Lonapse/Phrases/runtime.tsv`. Add a row and restart KSP to give Mission Control something new to say. The file explains the columns, the values you can use and the checks (like `fuel<25` or `orbit`). A chat can ask for jobs: `a=pilot` for the kerbal who starts, `b=scientist` for the one who answers. You can also put rows in `GameData/Lonapse/Phrases/Extra/*.tsv` in the format of the tables in the repo. These lines are only heard with the runtime voice on.
- **Subtitles with versions.** Most lines now have several English subtitles for the same audio: 6478 new versions on 3186 of the 3849 lines. You see a new wording first, the original comes later. So a line you heard before can still read differently.
- **Subtitle slots.** 29 template scenes fill in things from your flight: the mission day, the planet, the vessel, the altitude, parts of your vessel and the names of the crew in a chat. A subtitle only names parts your vessel has, and some versions only show in the right place, like on the ground or at Kerbin.
- **More probe voices.** QBE, Mk2 Drone Core, MPO and MTM have their own probe voices and 138 scenes of their own lines.
- `ModuleLonapse` takes a `displayName`, so a probe from a mod shows its own name in the subtitles.
- **"Light delay"** in the Crew tab. Turn it off and Mission Control answers right away, also far from Kerbin.

### Fixed

- The same lines came back too often for kerbals with a personality or a job. Their own scenes are a small list, and now they wait like every other scene before they come back.
- Mission Control said the name of one kerbal and another one answered. It now talks to the kerbal who answers.
- A subtitle showed the vessel name, but nobody said it. With the runtime voice on, the name is now spoken too.
- Some lines made no sense at Kerbin, like "Remember: you can't take Kerbin home." They no longer play there.
- Without a link the crew kept calling Control and got no answer. Now they only talk to each other until the link is back, like in a radio blackout.
- Two kerbals of the same gender aboard could sound almost the same. Their voices are now kept at least 1.5 semitones apart.
- Vessel names from KSP's localization (like `#autoLOC_...`) are shown and spoken as the real name.

### Notes

- The download is about 80 MB bigger: Piper for Windows and Linux and the three voices, in `GameData/Lonapse/PluginData/Runtime`. The voices are smaller copies of the build voices (int8 weights, decoder kept at full precision), and they sound the same.
- There is no runtime voice on macOS yet. The macOS build of Piper is missing its libraries. On a Mac only the recorded lines play. The Linux build is not tested in the game.
- Piper, espeak-ng, ONNX Runtime and the voices come with their own licences, see the licence files in that folder and "Runtime voice files" in the readme. The readme also lists the sha256 of every file, if your virus scanner asks.
- The scene variants test from the development builds is gone. Its lines are now subtitle versions.

## 0.6.0 (2026-11-05)

**738 new interactions with 1514 new lines.** That is 233 radio chats with Mission Control, 76 crew chats on the intercom, 141 scenes for tense moments, launch and reentry, 121 for flight events like staging, landing, docking and EVA, 99 chats between a probe and the crew and 68 probe events. Lonapse now has 1729 interactions in total.

### New

- Probes and crew together. A crewed vessel with a probe core now gets small cabin chats. The probe beeps and a kerbal answers, often in their own way. This also works with only one kerbal aboard. Turn it off with "Probe talks with the crew" in the Probes tab.
- Probe voices you can change. Fly a probe, open the Probes tab, pick another voice for that core part, change its sound with the sliders and press Play to hear a mood. This is saved in `settings.cfg` and needs no config files.
- Mods can add probe voices. A voice is only text: a few values like pitch, wave and rhythm. Lonapse makes the sound in the game, so a mod ships one `.cfg` file and no audio. The voice borrows the lines and subtitles of a stock core or of the generic probe (`lines = okto`).
  - A `LONAPSE_PROBE_VOICE` block in any `.cfg` in GameData adds a voice. A block named after a stock core (like `okto`) replaces that core's voice.
  - To give a part a voice, add `ModuleLonapse` to it. A part author can put it in the part's own config. Module Manager is only needed to add it to a part from another mod, or to add it only when Lonapse is installed (`:NEEDS[Lonapse]`).
  - See Probe voices in the readme for an example.
- Mission Control sounds livelier. The voice is a bit higher, calm lines are a bit faster and less flat, and there are many more excited and silly Mission Control lines.
- Lines remember when they were last played, also between game sessions (`PluginData/played.txt`). A scene doesn't come back until most of the other scenes that fit have played. Delete the file to start fresh.
- A real countdown from 10 if you want one. Pick Down in the General tab.
- Radio static under every radio transmission, also with a full signal. It is on by default at 50 %. Set it with the Radio static slider in the Sound tab, 0 % turns it off.
- "Radio sound effects on the intercom" makes the crew chats sound like the radio instead of a voice in the cabin.
- Subtitle width slider.

### Changed

- Subtitles are on by default.
- The crew chats on the intercom more often: about every minute on a calm flight instead of every 5. Stations and quiet coasts no longer get extra chats on top, so it is about a minute everywhere.
- Full throttle and throttle cut callouts can come 5 s apart instead of 20 s.
- The Intercom gap slider goes up to 15 minutes instead of 30.
- Probes talk as often as crews. Before, the gap between probe calls was 1.5 times as long.
- When you update, these settings move to the new defaults only if you never changed them. Your own values are kept. Subtitles were off by default before, so they get turned on. You can turn them off again in the window.

### Fixed

- Science experiments run right after each other no longer lose their lines. They wait in a short queue (up to 3) and the crew talks about them one by one, 30 s apart. The same experiment type gets a new line after 60 s instead of 5 minutes, so a crew report in a new biome is mentioned.
- A science line during launch, ascent, reentry or descent now waits until things calm down (up to 10 minutes) instead of being dropped after 90 s.
- A waiting science line is no longer lost when a kerbal goes out on EVA or climbs back in.
- A calm chat with two or more lines doesn't come back within 10 minutes.
- A generic tense line no longer repeats as often when nothing else fits.
- Removed lines where kerbals talk about their helmet inside the cabin. Kerbals don't wear one in IVA.
- Lines about floating around the cabin now only play in orbit or while coasting, not on the pad or after landing.

## 0.5.3 (2026-11-03)

- Mission Control no longer keeps saying "Keep an eye on the data" while the craft spins or pulls heavy g's. Its short answer to a crew line now fits the crew line, like a spin answer to a spin line.
- Tense and panic radio lines don't repeat within 45 s. A generic line that fits any situation plays at most once in that time. When nothing new fits, the radio stays quiet.
- Radio and intercom now take turns. Once the crew starts a chat on the intercom, radio chatter and events wait until the chat is over instead of cutting in between two lines. A new chat doesn't start when the radio is about to talk. Urgent calls (a lost part or kerbal) still end the chat, and short callouts like staging still come in between two lines.

## 0.5.2 (2026-11-03)

- On EVA, falling over or lying on the ground no longer counts as spinning. The kerbal also gets about 3 s to get up after a fall (`evaSettleTime`).
- On EVA, hops and bumps no longer count as something hitting the kerbal.
- Mission Control questions like "Station check. Please confirm." now get an answer from the crew. Mission Control no longer acknowledges a crew line with a question nobody answers, and "Confirm status, please." is gone.
- Every crew and Mission Control line now shows its subtitle. When no line from the phrase table fits, the radio stays quiet instead of playing a random clip without a subtitle.

## 0.5.1 (2026-11-03)

- The crew keeps talking during a reentry blackout. Staging and other callouts still work. Only Mission Control drops out and doesn't answer.
- The crew no longer repeats the same stress topic (like "this is shaking a lot") over and over. Each cause comes up at most once every 45 s, at any radio amount.
- The master alarm only plays at very high stress (0.9), not as soon as one nervous kerbal panics.
- Removed chatter that claimed things that may not have happened, like "Booster separation", "Fairing away", "Main engine cutoff" and altitude callouts.
- Mission Control no longer says "Throttle down now" during heavy vibration, and the crew no longer claims to throttle down or open chutes.

## 0.5.0 (2026-11-03)

First public release. The mod was called Quindar during development.

- Crew and Mission Control radio in Lonapse, with stress per kerbal, callouts, cabin intercom and English subtitles.
- Probe mode with a beep voice per probe core, moods and Mission Control talking to the probe.
- EVA suit radio with breathing that speeds up while running, a relay through a nearby ship and lines for falls.
- Signal from CommNet or RemoteTech: static, blackout, answer delay and dropouts.
- Talking kerbals in IVA and in the portraits, with head turns to the speaker.
- Launch countdown, station and base mode, master alarm during panic.
- Old Quindar settings are picked up automatically.
