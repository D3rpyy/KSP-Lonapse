# In-game test of the mod API

This tests the API the way another mod uses it. The test mod is `ExampleMod`. It has the example lines from
`docs/api/example_mod.tsv` and a DLL that calls Lonapse only through `LonapseWrapper.cs`. It also adds a test
experiment, a soil probe part and a drill rig part through Module Manager. Nothing of it is shipped with Lonapse.

Use a test save. Test 8 to 10 send contract events for contracts that are not in the contract list. Lonapse
handles that, but I don't know how every other mod reacts to it.

## Setup

1. `./build.sh` installs Lonapse.
2. `tests/ingame/install.sh` installs `GameData/ExampleMod`. It refuses while KSP runs.
3. Start KSP. In KSP.log:
   - `[Lonapse] Lonapse: 32 mod templates from 1 file(s): ExampleMod (32)`
   - no `[Lonapse] Lonapse runtime:` line about `ExampleMod`
4. Lonapse settings, General tab: "Speak names and new lines at runtime" and "Science points and contract funds" on.
5. Lonapse window, Mods tab: `ExampleMod (32 lines)` with its toggle on. Press "Show the tools for modders" once.
   The tools stay open after a restart. The event picker goes forward with a left click and back with a right click.

Remove it all afterwards with `tests/ingame/install.sh --remove`.

## A. The test window

Launch a crewed vessel with a link to Mission Control. The ExampleMod window shows in flight. Alt+F10 hides and
shows it. Each button writes PASS or FAIL lines. They also go to KSP.log as `[ExampleMod]`. What then plays is listed
in the Mods tab under "Last mod events".

| Step | Button | Expect |
|---|---|---|
| 1 | Status | All PASS. |
| 2 | Repair done, sent twice | Two PASS. Then an event chat about the antenna, or Mission Control with a reply. |
| 3 | Repair again, after it played | PASS: refused by the cooldown. |
| 4 | Supplies low: Trigger, then Cancel | Three PASS. Nothing about supplies plays. KSP.log: `Mod event examplemod.supplies_low cancelled by its mod`. |
| 5 | Supplies low, Days 4 | PASS. A line about supplies for 4 days, also in a busy moment (high priority). |
| 6 | Experiment done | PASS. Mission Control about plant growth, then a reply. KSP.log: `value days ignored`. It is low priority, so it can wait up to 30 s after the last mod event. |
| 7 | Ids that Trigger must refuse | All PASS. KSP.log names the reason for each id once. |

Supplies low has a cooldown of 600 s. For the line with Days 1 ("one day of food"), wait 10 minutes, or restart
the flight scene.

## B. Contracts

| Step | Button | Expect |
|---|---|---|
| 8 | Salvage done | Usually (70%) a salvage line: "Done: the salvage job". In sandbox the funds can be 0, then it is the `contract_done` line. |
| 9 | Salvage failed | Usually "We lost the salvage job". |
| 10 | Other type done | A general contract line. Never the words "salvage job". |

Press each one a few times. The 70% means a general line now and then is correct.

## C. Reload

| Step | Action | Expect |
|---|---|---|
| 11 | Write file | PASS: no lines yet. |
| | Mods tab, Reload lines | The status says `Reloaded: ... mod templates, 0 bad row(s)`. |
| 12 | Check after reload | Two PASS, then "The reload works." |
| 13 | Remove file, then Reload lines | 12 now fails "HasLines after the reload". That is the expected result. |

Also try a broken row: put a typo in a kind in `GameData/ExampleMod/Lonapse/example_mod.tsv` and press Reload lines.
The status counts 1 bad row, and KSP.log names the row. Undo the typo and reload again.

## D. Mute

1. Mods tab: turn ExampleMod off.
2. Step 14: two PASS.
3. The Mods tab lists no ExampleMod events in the event picker.
4. Turn it on again. Step 1 is all PASS again. Restart KSP: the toggle keeps its state (`mutedMods` in settings.cfg).

Before E and F: ExampleMod must be on again in the Mods tab (after D), and step 1 must be all PASS.

## E. The soil probe (kind experiment)

Put "Example Soil Probe (Lonapse test)" on a crewed vessel. Run "Analyse Soil". With a link, Mission Control only
adds its science line 60% of the time. Between two runs, wait 60 s (the science cooldown of one topic), and change the
biome or delete the data.

- With a link, in sandbox: the recorded science scene, then Mission Control says "We have the soil analysis" or
  "The soil analysis is in" (`sci_examplemod_soil`).
- With a link, in career: a general science points line. If it has `{exp}`, it says "soil analysis", never "the
  experiment".
- Without a link: the kerbals have the soil chat ("the soil analysis is done. It's dirt."), or alone "I have the soil
  analysis". KSP.log: `Runtime voice: science examplemod.soil chat queued, no link`.
- The Mods tab "Test mod event" list does not show `examplemod.soil`.

No link: CommNet on, extra ground stations off, then go behind the Mun or to the far side of Kerbin. Lonapse has no
setting that cuts the link.

## F. The drill rig (kind device, needs Breaking Ground)

Take "Example Drill Rig (Lonapse test)" in an inventory. On EVA, place and deploy it.

- KSP.log: `Event deploy: exampleDrillRig (experiment)`, then `Runtime voice: deployed the drill rig (examplemod.drill), chat queued`.
- Usually (70%) a drill chat: "the drill rig is set up" with a kerbal nearby, or "Done, the drill rig is set up" alone.
  Otherwise one of the general deploy chats.
- With a link, after it: Mission Control "Data is coming in: the drill rig works", or a general deploy line.
- A stock seismometer still says "the seismometer", never "the drill rig".

## G. Without the runtime voice

Turn "Speak names and new lines at runtime" off. Repeat step 6 after its cooldown (120 s). The line shows as a
subtitle with the radio click and no voice. The Mods tab says `(subtitles, the runtime voice is off)`.

## What this does not test

- A real mod. ExampleMod is made up, but it calls the API from its own DLL, the way a real mod would.
- Contracts from a cfg based contract mod. They share one class, see docs/API.md.
- The crew of another vessel nearby as the kerbal of an event.

## Contextual probes and radio

- Fly a stock probe with runtime reports on. Check battery, orbit, biome and maneuver exchanges; compare OKTO,
  QBE and Drone Core personalities. Leave a topic unchanged: it should not repeat within four minutes.
- Drain then recharge a probe; lose then restore contact; induce spin then recover. Each recovery should receive
  a relevant follow-up, with no reply while asleep or disconnected. Repeat a recovery cycle.
- Change vessel, ModuleLonapse selection, display name or voice while Piper renders. No old exchange should play.
  Change battery rapidly while a line waits: stale power readings should be skipped.
- Run temperature, pressure, gravity, Goo and materials experiments, including a mod-registered experiment.
  Run several from one action group: one batch exchange, with bounded experiment names. Compare first/repeat
  observations in the same full subject and a different biome. Move before closing results: use captured readings.
- Keep, discard, transmit, interrupt and retry science data. Collection must not claim receipt; acknowledgments
  should follow successful transmission events and combine closely spaced results. Test an outage and later upload.
- Save/reload and restart KSP: mission history stays per vessel and save, and landing milestones do not repeat.
- Crew one engineer/scientist/pilot with a probe. Check job-specific cabin reports and the probe's selected beep voice.
- Compare full and weak signal during a long beep/data stream: filtering, grit and dropouts respond live.
  Listen for a short carrier opening, hiss and tail. Check blackout cutoff, mute and vessel changes stop the carrier.
- Compare cabin beeps with intercom radio FX on/off: neither should have CommNet dropouts. Preview voices and
  play ordinary stage tones: no inherited receiver degradation or lingering static.
- Test ModuleLonapse priority, explicit lines/displayName, player override and LONAPSE_PROBE_VOICE definitions.
  Add a contextual event row with x_core_<custom_lineset>=1; verify it wins only for that lineset and respects mod mute.
- Disable reports/science, each probe feature, and DSP in turn. Verify quiet science,
  volume controls and the existing reflection API Trigger/Cancel behavior.

- During tense/panic probe flight, check `[Lonapse] Runtime probe exchange stress_<cause>` in KSP.log.
  Test sustained dynamic pressure, heat, spin, G-load, fuel shortage and ground approach. MC should prefer
  telemetry-bearing runtime lines in launch/ascent as well as other phases. Panic uses the controlled MC delivery.
  A single pressure warning must not loop every four seconds; contextual and ordinary scene alternatives share a cause
  cooldown. Disable Runtime reports while leaving speech available and stress talk on: stress exchanges still work.
  Make Piper unavailable: no recording fallback should play. Change cause, band,
  vessel or telemetry during rendering and verify obsolete speech is skipped.

## Runtime-only dialogue migration

- Install the release into a clean test copy. Confirm speech plays without `Sounds/crew`, `Sounds/control`,
  `Phrases/phrases.tsv` or `Phrases/pools.tsv`.
- Exercise ordinary crew radio, cabin chats, panic, direct staging/chute callouts and the final crew-loss MC call.
  Check delivery, matching subtitles, bounded synthesis waits and cancellation after a vessel/scene switch.
- On the pad, wait for countdown speech preparation. Count in both directions, stop it, and confirm the prepared
  "Launch cancelled" call. Preparation must finish before timing/auto-staging begins.
- Test stock probe personalities, a custom LONAPSE_PROBE_LINES lineset, ModuleLonapse.lines/priority/displayName,
  a custom beep voice, and a player voice override. Check the existing precedence and each beep's mood/order.
- Trigger probe telemetry, science, warp, docking and stress exchanges. Change the selected voice or vessel while
  synthesis is pending; stale exchanges must not play.
- Edit a category file and use Reload lines. Verify both ordinary scenes and contextual templates
  reload. Rename a category file; its filename must not change selection behavior.
- Try missing/unavailable Piper. Speech availability must be explained; no unrelated recording fallback should play.

- Check prefix-free IDs and separate `on`/`to` routing with an existing mod that still uses `rt_` IDs. Reply
  matching, priorities and saved repetition history should behave as before.
- Trigger a `{body}` scene on the Mun and on a modded body. Check paired speech/subtitles use the actual body,
  and the old generated per-body IDs resolve without creating duplicate dialogue. Home-world lines should also
  use the current body; intentional MPO/Moho personality jokes remain specific.

## Unified Crew and Probe tables

- Give one line a fallback (`prefer=0`) and separate `kerbin` and `landed` alternatives (`prefer=1`). On Kerbin's
  surface either contextual alternative may play; in Kerbin orbit only the Kerbin one may play. Away in orbit,
  the fallback must play. Speech and subtitle must use the same alternative.
- Put a numeric guard on a later conversation line, then on a probe beep row. The entire exchange must wait
  until every line has an eligible alternative. Give a preferred alternative an unavailable variable and confirm
  its fallback still works. Test both Crew and Probe categories.
- Set two consecutive chat lines to `speaker=kerbal_a`; the same primary kerbal should speak both. Set the second to
  `kerbal_b` and confirm the other kerbal speaks it. Check explicit intercom/radio routing and MC radio delivery.
- Reload a malformed row with a pipe-separated alternative or unknown variable/condition. Check the filename
  and error in KSP.log. Existing mod tables using the legacy `kind` header should still load alongside new files.
- Reload countdown alternatives and switch vessels while speech is preparing. Old clips and subtitles must
  never be reused for the new vessel; starting the count must wait for the current preparation to finish.

## Dialogue jobs, traits, experiment names and resource scanners

- Load a scientist/pilot pair. Trigger `chat_scipts` and the Mystery Goo science talk. The scientist should use the
  specialist wording; the pilot should use the uncertain alternative. Reverse the pair and check the names and
  voices still follow `kerbal_a`/`kerbal_b`. Repeat a personality condition with a nervous/brave kerbal.
- Trigger orbit, crew-history, warp and science reports. Replies must still use their `to` targets after moving
  into the report category files. Tourist replies must stay in the tourist pool. Upgrade an existing install and
  confirm the old built-in `Crew/replies.tsv` is removed without duplicate reply IDs.
- Run an unregistered mod experiment with a display title. Check `{exp}` says that title, paired speech/subtitles
  stay consistent, and a mixed batch names each experiment. A registered experiment must keep its bilingual name.
- Land a powered vessel with a Surface Scanning Module. Routine reports and intercom should use its resource and
  percentage. Check a zero reading works; disable the scanner, drain power, lift off, or switch to a vessel without
  one and confirm scanner lines become ineligible. Resource scanning must not announce science points.

## Playtest regressions

- In orbit, use EVA while holding a ladder, then release it. Reports say "outside the ship" and never give walking instructions. Land and walk to confirm the walking lines still play.
- Fire the EVA jetpack in orbit around Kerbin and another body. Jetpack reports mention the current body. Coast, grab a ladder or board before a rendered report plays and confirm the outdated report is skipped.
- Splash down, do an EVA report and take a surface sample. Expect water dialogue, including when keeping the data or working without a link. Dry ground and rock exchanges must stay out of the selection.
- Listen through a crew radio exchange and an intercom chat. Regular telemetry and warp-end telemetry wait for the whole exchange, including pauses between speakers. An arriving conversation takes priority over a pending telemetry ping.
- Listen to engine and ambient audio as speech starts and ends. Their volume should stay steady. Check whether the reported micro cuts are gone.
- In IVA, thrust at different levels, shut down, run out of fuel and fire a solid motor. The rattle follows actual thrust and fades after shutdown. Leaving IVA, pausing or turning Cabin sound off silences it. Cabin thrust noise (IVA) controls its volume independently of Cabin fan noise (IVA). Muting either slider leaves the other sound audible.
- Watch portraits below 0.2 vessel or personal stress, both speaking and idle. Frightened stock expressions and fearful line moods must stay neutral at worst. Raise stress through 0.2 and back down to check the transition.

## Docking and empty stations

- Undock from a station with a probe core on the active vessel. Expect undocking dialogue and no ignition, separation or staging tone from the stage-number change. Repeat with crew aboard and from each side of the docking connection.
- Activate an actual stage immediately after undocking. Its callout should still play. Dock again and check that merging stage lists does not trigger staging dialogue.
- Leave a healthy uncrewed station active with the station style set to Auto or Check. Routine probe talk should come only through station check-ins, normally 120 seconds apart, including the first wait. There should be no independent hail between those calls.
- Repeat with probe chatter off and hails on, then both off. Neither combination should initiate station hails. Exchange style retains Mission Control initiated probe conversation. Low power, loss of signal, science and staging still get their own reactions.

- In General, move MC check-in gap to 15 and 180 seconds. Changing it restarts the wait. Save and reload to confirm the selected gap is retained. Both crewed and uncrewed station check-ins use it.

## Volume headroom

- Existing saved volumes should sound as before at lower displayed percentages. Set each audio slider to 100% and confirm additional gain, including voices, probe beeps, static, keys and individual effects.
- Raise master and a category together to check that gain above Unity's source-volume limit still reaches the audio. Mute an individual slider and confirm it stays silent. Save and reload the higher settings.
- Fixed signal and probe/crew chat share should keep their original 0 to 100% behaviour.

- In IVA, activate a stage with a decoupler or fairing and expect the cabin separation clunk. An ignition-only stage, undocking and staging in the external view must not play it. The Cabin separation noise (IVA) slider controls it independently of fans, thrust and radio callouts. Check it with radio disabled and during a blackout too.
