# Lonapse API for other mods

With this API your mod can give its own events a voice! Your code tells Lonapse that something happened. Lonapse
picks who talks, queues it with the rest of the radio, and keeps the priorities and cooldowns. Your mod ships the
lines as a text file. A kerbal can say it, two kerbals can chat about it, or Mission Control can say it and the
kerbal can answer.

Neither mod depends on the other. Without Lonapse your mod runs as normal, and the call does nothing. 

The API is new in 0.8.0 and has API version 1. I have tested it with the example below, not with a real mod yet. It is in an experimental phase now, please provide feedback if you find any bugs!

## What you need

1. A line file in `GameData/<YourMod>/Lonapse/`, for example `GameData/YourMod/Lonapse/lines.tsv`.
2. `LonapseWrapper.cs` copied into your mod (from `docs/api/` in the Lonapse repository).
3. One call where your event happens.

The full example is `docs/api/example_mod.tsv`. It is a made-up mod, `examplemod`, with three events, an experiment, a Breaking Ground part and a contract type of its
own.

## The call

```csharp
using System.Collections.Generic;

// in your code, where it happens
LonapseWrapper.Trigger("yourmod.repair_done", vessel, kerbal,
    new Dictionary<string, double> { { "x_parts", 3 } },
    new Dictionary<string, string> { { "x_part", "la antena|the antenna" } });
```

- `eventId` is `yourmod.event`: a-z, 0-9 and `_`, with one dot. Start it with your mod's name, so two mods
  never use the same id.
- `vessel` is what vessel it happened on. Pass null for the active vessel.
- `kerbal` is the kerbal who did it, a `ProtoCrewMember`. Pass null and Lonapse picks one.
- `numbers` and `names` are your own values, for the `{x_...}` slots in your lines. Both may be null.
  - A number becomes Spanish words in the voice and digits in the subtitle.
  - A name is `"english"` or `"spanish|english"`. The voice says the Spanish, the subtitle shows the English.

`Trigger` returns true when Lonapse accepted the event. It then plays when the radio is free, usually within a few
seconds. It is dropped when it waited 90 s. `Trigger` returns false when:
- Lonapse is not installed or not running
- no lines for the event are loaded
- the vessel or kerbal is not the active one or close to it
- the event is still in its cooldown
- the player muted the lines of your mod
- the id is the topic of an `experiment`, `device` or `contract` row (those play on their own moment)

KSP.log says why, on a line that starts with `[Lonapse] Mod event`.

Other calls:
- `LonapseWrapper.Installed`: Lonapse is installed.
- `LonapseWrapper.Available`: Lonapse is running in flight.
- `LonapseWrapper.ApiVersion`: 1, or 0 without Lonapse.
- `LonapseWrapper.HasLines(eventId)`: lines for the event are loaded. This only works after the first flight scene.
- `LonapseWrapper.Cancel(eventId)`: drops your event while it still waits for the radio. Use it when the problem is
  solved before Lonapse said it, for example the supplies were refilled. A line that already plays is not cut. It
  returns true when the event was waiting. A cancelled event does not start its cooldown.

Call it from the main thread, like any KSP API. None of the calls throw.

Only events on the active vessel and the vessels near it play: the crew aboard, kerbals on EVA nearby, and the
crew of other vessels within 2.3 km. Other events return false.

## The line file

It is a tab separated text file, UTF-8, with the columns `id kind when spanish english`. The format is the same as
the Lonapse runtime lines. It is explained at the top of `GameData/Lonapse/Phrases/Runtime/reports.tsv`. Lonapse reads
every `.tsv` in your `Lonapse` folder at the first flight scene. KSP itself ignores `.tsv` files.

```
id	kind	when	spanish	english
rt_chat_yourmod_repair_1	chat	on=yourmod.repair_done	{other}, ya está arreglado: {x_part}.	{other}, it's fixed: {x_part}.
rt_chat_yourmod_repair_2	chat		Buen trabajo. ¿Has usado cinta?	Good work. Did you use tape?
rt_chat_yourmod_repairsolo_1	chat	on=yourmod.repair_done	Reparación terminada.	Repair done.
rt_yourmod_repair	event	on=yourmod.repair_done	{kerbal}, buen trabajo.	{kerbal}, good work.
rt_re_yourmod_repair	reply	to=rt_yourmod_repair	Gracias.	Thanks.
rt_yourmod_repair_meta	meta	on=yourmod.repair_done cooldown=180 priority=normal who=any
```

## Kinds

The `kind` column says what a row is and when it plays. There are 19 kinds.

### Kinds for your events

These 4 kinds play when your mod calls `Trigger`.

| Kind | What it is | Needs |
|---|---|---|
| `chat` | An event chat. The kerbal who did it says line 1, and another kerbal nearby answers. A chat of one line is for a kerbal alone. | `on=yourmod.event` on line 1 |
| `event` | A Mission Control line about the event. `{kerbal}` is the kerbal who did it. | `on=yourmod.event` |
| `reply` | The kerbal answers your `event` line, right after it. `to=rt_yourmod_*` answers every line whose id starts with that. | `to=<id of your event line>` |
| `meta` | The settings of one event. It has no text. | `on=yourmod.event` |

3 more kinds register a thing of your mod that Lonapse already notices itself. They do not need `Trigger`, see
[Your own experiments, parts and contracts](#your-own-experiments-parts-and-contracts). Each has one version of text
without slots: the words for its slot.

| Kind | What it registers | Its slot | Needs |
|---|---|---|---|
| `experiment` | An experiment that runs through KSP science | `{exp}` | `on=yourmod.topic id=<experimentID>` |
| `device` | A Breaking Ground part you can deploy | `{device}` | `on=yourmod.topic id=<part name>` |
| `contract` | A contract type | `{contract}` | `on=yourmod.topic id=<contract class name>` |

Chat ids are `rt_chat_<name>_<seq>`, with seq 1, 2, 3 and so on, one row per line. Only line 1 has a `when`. All
lines of a chat go in one file. Only replies with your `to=` answer your `event` lines.

The settings of `meta` are all optional:

| Setting | What it does | Default |
|---|---|---|
| `cooldown=<seconds>` | The event plays at most once in that time. | 120 |
| `priority=low\|normal\|high` | High skips the 30 s gap between two mod events, and still plays when the crew is stressed. | normal |
| `who=crew\|mc\|any` | Only the event chats, only Mission Control, or both. With `any` and both kinds present, the chat plays 60 % of the time. | any |

### Kinds Lonapse triggers itself

These 12 kinds play on moments that Lonapse finds itself. Your file may add rows of them too, for example an extra
report. They play on those moments, never on your event. A `science` or `science_pts` row may only have
`on=yourmod.topic` when an `experiment` or `device` row registers that topic. A `contract_funds`, `contract_done` or
`contract_failed` row may only have it when a `contract` row registers it. Otherwise it is an error, because it could
never play: use `event` for that. Your `{x_...}` slots never have a value there, so a row of these kinds cannot use them.

| Kind | When it plays |
|---|---|
| `report` | Now and then: Mission Control reports on the vessel (orbit, fuel, power, crew and more). |
| `dock` | After a docking approach call: the distance and closing speed to the target. |
| `land` | Once per descent, low and coming down: the height and the speed. |
| `science` | After an experiment, when its science value is not known. |
| `science_pts` | After an experiment, with its science points. |
| `contract_funds` | A contract is done and pays funds. |
| `contract_done` | A contract is done without funds. |
| `contract_failed` | A contract failed. |
| `warp` | Now and then in time warp on rails. |
| `sunrise` | Now and then at a sunrise in orbit. |
| `eva` | Now and then: Mission Control to a kerbal on EVA. |
| `flag` | Right after a flag is planted. |

`chat` and `reply` also work without your event:
- A `chat` without `on=` is a normal chat. It plays now and then on the intercom in one cabin. With `on_eva=1` it
  plays on the suit radio between two EVA kerbals, and with `apart=1` on the radio between two vessels nearby.
- A `reply` without `to=` can answer a `report`, `warp`, `sunrise` or `eva` line, or a recorded Mission Control line.

## Your own experiments, parts and contracts

### Experiments

Does your mod add an experiment that runs through the KSP science system, a `ModuleScienceExperiment` with its own
`experimentID`? Then Lonapse sees it like a stock experiment. You do not call `Trigger` for it. Without help Lonapse
calls it "the experiment", and only the general science lines play. An `experiment` row fixes that:

```
rt_yourmod_soil_exp	experiment	on=yourmod.soil id=yourSoilScan	análisis del suelo	soil analysis
rt_sci_yourmod_soil	science	on=yourmod.soil	Recibimos el {exp}.	We have the {exp}.
rt_chat_yourmod_soilsolo_1	chat	on=yourmod.soil	Ya tengo el {exp}.	I have the {exp}.
```

- `id=` is the `experimentID` of your experiment, the part of the subject id before the `@`. Case does not matter.
  A stock id is an error, Lonapse has its own words for those.
- `on=` names its topic. Each topic and each id has one `experiment` row.
- `{exp}` now says "soil analysis" in the subtitle and "análisis del suelo" in the voice.
- `science` and `science_pts` lines with `on=yourmod.soil` play only after this experiment, most of the time when
  there is one. The general science lines can still play for it too.
- Event chats with `on=yourmod.soil` play when the kerbals talk about it themselves: without a link to Mission
  Control. Event chats with `on=science` can play for it too.

Do not call `Trigger` for an experiment that already goes through the KSP science system. Lonapse would play two
lines for one result. `Trigger` refuses the topic of an `experiment` row, and KSP.log says why. Use `Trigger` and kind
`event` for an experiment that does not use the KSP science system, like `examplemod.experiment_done` in the example.

### Breaking Ground parts

Does your mod add a part with a `ModuleGroundSciencePart`, one that a kerbal on EVA deploys? Lonapse notices the
deploy itself. Without help it calls the part by what it does ("the antenna", "the generator", "the gadget"). A
`device` row gives it its own words and a topic:

```
rt_yourmod_drill_dev	device	on=yourmod.drill id=yourDrillRig	el taladro	the drill rig
rt_chat_yourmod_drillsolo_1	chat	on=yourmod.drill	{device} ya está montado.	Done, {device} is set up.
```

- `id=` is the part name, the `name` in the part's cfg. A stock Breaking Ground part is an error.
- `{device}` says "the drill rig". The words include the article, because the slot stands for the whole thing.
- Event chats with `on=yourmod.drill` play after it is deployed. Event chats with `on=deploy` play for it too.
- A `science` line with `on=yourmod.drill` is what Mission Control says after it, when there is a link.

### Contract types

Does your mod add contracts? Lonapse already says something when a contract is done or failed. A `contract` row lets
your contract type have its own lines:

```
rt_yourmod_salvage_con	contract	on=yourmod.salvage id=YourSalvageContract	el rescate de chatarra	the salvage job
rt_con_yourmod_salvage	contract_funds	on=yourmod.salvage	Listo: {contract}. Os pagan {funds} fondos.	Done: {contract}. It pays {funds} funds.
```

- `id=` is the class name of your contract, the C# type that derives from `Contracts.Contract`.
- `{contract}` says "the salvage job". A line with `{contract}` only plays for a registered contract type.
- `contract_funds`, `contract_done` and `contract_failed` lines with `on=yourmod.salvage` play only for that type. The
  general contract lines can still play for it too.
- Contracts made from a cfg by a contract mod all share the class of that mod, not a class of their own. Then the
  row registers all of them at once. I have not tested this with a contract mod.

## Rules

- Ids start with `rt_` and are unique over all files, Lonapse's own too. Start yours with `rt_<yourmod>_`. A row
  with a used id is skipped and named in KSP.log.
- `spanish` is the text the voice speaks. `english` is the subtitle. Several versions go in one cell, split by `|`,
  in the same order in both columns. One version is picked each time.
- Each version uses the same slots in both languages. Your own slots start with `x_`: `{x_part}`, `{x_days}`.
- The Lonapse slots work too: `{other}` and `{self}` in a chat, `{kerbal}`, `{vessel}`, `{here}` (the body),
  `{biome}`, `{day}` and the numbers of the vessel. The list is in the header of `reports.tsv`.
- `when` takes the checks of Lonapse (`landed`, `orbit`, `link=0`, `a=scientist` in a chat) and checks on your own
  numbers: `x_days>=2`. A line only plays when all its slots have a value and its checks hold.
- Avoid a plural noun after a value that can be 1. Give the line a check like `x_days>=2`, and write another
  line for 1.
- A line that says "Control" is dropped when the vessel has no link to Mission Control. Without a link only the
  event chats play, so write a chat with `link=0` if your event should be heard there too.
- A bad row is skipped and logged as `[Lonapse] Lonapse runtime: <file>:<line>`. The rest of your file still works.

## What players see

- With the runtime voice on, the kerbals and Mission Control speak your lines in loñapsE, the made-up language of
  Lonapse, with an English subtitle.
- With the runtime voice off, the same lines show as subtitles, with the radio click around them. Nothing is spoken.
- Your event waits for the radio like any other line. It never cuts into a line that is playing, a chat or a
  callout.
- In a busy moment, with high crew stress, low and normal events wait. High events still play, unless the stress
  is very high.
- The Lonapse window has a Mods tab. It lists every mod with lines and how many, with a toggle each. A player can
  turn off the lines of your mod there. Then none of its rows play, and `Trigger` returns false.
- The same tab has the tools for you, behind "Show the tools for modders": "Reload lines", "Test mod event" and the
  last five mod events with what became of them. The window remembers that you opened them.

## Testing your lines

1. Put your file in `GameData/<YourMod>/Lonapse/` and start a flight.
2. KSP.log shows `[Lonapse] Lonapse: N mod templates from M file(s): YourMod (N)`, and any bad rows.
3. Open the Lonapse window, go to the Mods tab and press "Show the tools for modders". Pick your event (left click
   goes forward, right click back) and press "Test mod event".
   The test fills a slot that is used in a check with the number 3, and any other slot with the name "Test".
4. Changed your file? Press "Reload lines" in the same tab. Lonapse reads every line file again, its own and
   those of all mods. KSP.log lists the bad rows again. You do not need to restart KSP.
