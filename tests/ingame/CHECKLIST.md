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
  "The soil analysis is in" (`rt_sci_examplemod_soil`).
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
