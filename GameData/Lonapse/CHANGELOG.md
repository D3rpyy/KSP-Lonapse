# Changelog

## 0.7.0 (not released yet)

**Kerbals now say your names.** Mission Control and the crew speak the names of your kerbals, your vessel and the planet you are at, and Mission Control reads out real numbers. The voice still speaks loñapsE. Only the names are said as they are.

### New
- Runtime voice. Lonapse now ships a small speech engine (Piper) and three voices, and speaks some lines while you play. Turn it on with "Speak names and new lines at runtime" in the Crew tab. It is off by default.
  - Mission Control sometimes ends a line with the name of the kerbal who just spoke, or with the name of your vessel. On a probe it uses the vessel name.
  - In crew chats on the intercom, a kerbal sometimes says the other one's name.
  - On a planet from a mod, lines about "the planet" say its real name.
  - Mission Control reports now and then with the real values: your orbit, altitude, speed, fuel or the mission day. The numbers are spoken in loñapsE, the subtitle shows them in digits. Turn this off with "Mission Control reports with numbers".
  - After some science reactions Mission Control says the experiment, the body and the science points. A finished contract gets its funds, a failed one a short line. Turn this off with "Science points and contract funds".
  - A line that is not ready in time plays as before, without the name. Nothing waits more than a second for it.
  - You can add your own lines without rendering any audio. Put rows in `GameData/Lonapse/Phrases/Extra/*.tsv`, in the format of the tables in the repo. They are only heard with the runtime voice on.
- Subtitles with versions. Almost every line now has several English subtitles for the same audio: 6478 new versions on 3186 of the 3849 lines. You see a new wording first, the original comes later.
- Subtitle slots. 29 template scenes fill in things from your flight: the mission day, the planet, the vessel, the altitude, parts of your vessel and the names of the crew in a chat. A subtitle only names parts your vessel has, and some versions only show in the right place, like on the ground or at Kerbin.
- QBE, Mk2 Drone Core, MPO and MTM have their own probe voices and 138 scenes of their own lines.
- `ModuleLonapse` takes a `displayName`, so a probe from a mod shows its own name in the subtitles.
- "Light delay" in the Crew tab. Turn it off and Mission Control answers right away, also far from Kerbin.

### Changed
- The scene variants test from the development builds is gone. Its lines are now subtitle versions.

### Notes
- The runtime voice makes the download bigger. Piper and its voices run as a separate program next to KSP, in `GameData/Lonapse/PluginData/Runtime`. Piper is GPL-3, see the licence files in that folder.

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
