# Changelog

## 0.8.0

### New
- **Chatter during time warp.** Time warp on rails is no longer dead silent. The calm radio and intercom talk goes on at its normal pace. Events wait until the warp ends, as before. With the runtime voice on, Mission Control also talks about the warp: the hours to the next burn and the hours to the next body. 10 new Mission Control lines and 4 crew chats only play in warp (`Phrases/Runtime/warp.tsv`). Turn it off with "Chatter during time warp" in the General tab.
- **The crew answers Mission Control.** With the runtime voice on, a kerbal now and then replies to a Mission Control report, a warp line, a science or a contract line, mostly with a joke. The kerbal the report talks to is the one who answers. 43 replies with 88 wordings (`Phrases/Runtime/replies.tsv`), some for any report, most for one kind of report. Recorded lines get answers too: after a calm chatter scene that ends with a Mission Control statement, a kerbal sometimes replies with one of the general replies.
- **Sunrise in orbit.** The mod counts the sunrises of a flight. Now and then Mission Control says one: "Sunrise number 12 this flight. We're still counting." A crew chat can use the count too.
- **Biome names.** Reports and crew chats can say the biome under the vessel: "You're passing over the Highlands." "Welcome to the Midlands. Population: us." Biomes of planet packs work too. A name the voice cannot say is left out: an untranslated tag (`#LOC_...`), a name in another alphabet, or one that is too long.
- **Personality in runtime chats.** A runtime chat can ask for a persona, like a dim kerbal and a brave one. 6 new chats use it: the dim kerbal and the red button, a nervous kerbal and a strange noise, a veteran who still shakes, and more.
- **Subtitle time on screen.** A new "Stay on screen" slider in the subtitle settings sets how long a subtitle stays after its line is spoken, from 0 to 10 s. The default is 0.6 s, as before. The countdown numbers keep the short time.

### Changed
- **The voice says what the subtitle shows, for every line with a name or a value.** All 289 subtitle lines that fill in a word now have their own Spanish. Before, 228 of them played the general recording while the subtitle showed the other kerbal's name, your vessel, the body or the mission day.
- **More names and words in the recorded chats.** 61 more intercom lines have a version with the other kerbal's name, so 382 recorded lines now fill in a word or a name. The word pools got 70 new values (things, spots, food, chores, rumors, songs, games and more). Every line that uses a pool now has more to pick from.
- **Fewer names, and no one singled out.** After a name is said, the lines of the next 30 seconds prefer a version without one. A chat or scene says at most one name when its lines allow it. Crew replies never say a name when a version without one exists, and the "{self} here" reply is gone. With two or more kerbals aboard, Mission Control no longer names one of them at random. It names the kerbal it answers, the vessel, or the whole team.
- **The right job speaks up.** At staging, burns, docking and landing the pilot talks most of the time, at low power or a lost part the engineer, at science the scientist. Before, any kerbal aboard was picked at random, so a crew of one pilot and two scientists gave the scientists two of every three flying lines. The others still speak up now and then. Crew replies to Mission Control's fuel, node, power and science reports work the same way.
- **Mission Control reads numbers in ordinary lines too.** With the runtime voice on, 32 event lines have a version with a real value: the battery when power runs low or comes back, the delta-v and time of a new maneuver node, the fuel left after a cutoff, the speed at staging, the parts and crew after docking, the science in the lab. Without the runtime voice these versions never show.
- **Much more variety in the runtime reports.** Runtime lines can have several wordings, split by `|`. Lines with the same meaning are one row now. The reports, the crew history and the docking and landing calls went from 57 to 186 wordings, with 35 new topics: a polar or retrograde orbit, a fast or slow orbit, flying low, almost full or empty tanks, a long mission, a crew of one, a burn that is coming up, a first landing somewhere, a second visit, drifting away from the docking target, a gentle descent and more. Docking and landing calls can now also say your vessel name. The runtime crew chats went from 64 to 162 wordings: most lines have two or three now, so the same chat sounds different each time, and there are 9 new chats (the ship already has a name, said with your vessel's real name, and a joke from the kerbal who picked it, monoprop, a polar orbit, a long mission, a far landing, a full battery, a big burn, a veteran). The warp lines went from 29 to 42 wordings and the crew replies from 63 to 88, with replies to the new reports.
- **Many more science and contract lines.** Mission Control now has 47 new wordings after science and contracts (it had 14). A science line can say that this is the first data of its kind from a body, how much science is still left there, or how much the lab has banked. A finished contract can mention its science and reputation, the agency's funds and how many contracts are still open. A failed one says what it costs. There are 4 new crew chats about contracts, reputation, science and funds.
- **Names the voice cannot say.** Vessel and docking target names in another alphabet are no longer said by the runtime voice. The subtitle still shows them, and the line plays as recorded.

- **One zip per release.** A version release on GitHub now only has `Lonapse-<version>.zip`, so CKAN picks the right file. The source of the runtime voice (Piper, piper-phonemize, espeak-ng) is linked at its exact commits in `PluginData/Runtime/LICENSES` instead of attached as a second zip.

- **Fuel numbers.** Before any engine has fired, the fuel value counted every tank of the vessel, so a line could say "92 percent fuel left" on a fresh rocket. Now it uses the tanks of the next stage with a liquid engine. On the launch pad no line says the fuel at all, and a vessel without a liquid engine gets no fuel line.

### For modders
- **The stock probe cores get `ModuleLonapse`** through a Module Manager patch, `Patches/Lonapse_StockCores.cfg`, with their own voice and priority -1. A patch of your own can run `:AFTER[Lonapse]` to change them. Module Manager stays optional: without it the patch does nothing and the stock cores sound the same as before.
- `Phrases/runtime.tsv` is now the folder `Phrases/Runtime/` (reports, crew, docking and landing, science and contracts, chats). Every `.tsv` in it is read, so your own lines can go in a file of your own. The columns are found by name. The format is at the top of `reports.tsv`.
- `Phrases/slots_es.tsv` is gone. The runtime Spanish of a line is the `spanish_rt` column of its row, and the Spanish of a pool value is the `spanish` column of `pools.tsv`. An English edit can no longer silently switch it off.
- The install removes the old `runtime.tsv` and `slots_es.tsv`.
- New runtime kind `warp`, new number slots `in_warp` (1 in time warp, for checks), `node_h` and `soi_h`, and the name slot `next` (the body after `soi_h`).
- New runtime kind `sunrise`, number slot `sunrises` and name slot `biome`. Chats can check a persona with `a=` and `b=` (badass brave nervous dim veteran), several per kerbal.
- New runtime kind `reply`: a crew answer to a Mission Control line. `to=<id>` in the when column says which lines it answers, a prefix with `*` works too.
- New number slots for science and contract lines: `sci_left`, `sci_first`, `con_sci` and `con_rep`. Science and contract lines now also get the flight values (`vessel`, `day`, `sci_total`, `funds_total`, `contracts` ...). For a failed contract, `funds` and `con_rep` are what it costs.

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
