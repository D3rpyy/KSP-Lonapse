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

The full example is `docs/api/example_mod.tsv`. It is a made-up mod, `examplemod`, with three events.

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

KSP.log says why, on a line that starts with `[Lonapse] Mod event`.

Other calls:
- `LonapseWrapper.Installed`: Lonapse is installed.
- `LonapseWrapper.Available`: Lonapse is running in flight.
- `LonapseWrapper.ApiVersion`: 1, or 0 without Lonapse.
- `LonapseWrapper.HasLines(eventId)`: lines for the event are loaded. This only works after the first flight scene.

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

The `kind` column says what a row is and when it plays. There are 16 kinds.

### Kinds for your events

These 4 kinds play when your mod calls `Trigger`.

| Kind | What it is | Needs |
|---|---|---|
| `chat` | An event chat. The kerbal who did it says line 1, and another kerbal nearby answers. A chat of one line is for a kerbal alone. | `on=yourmod.event` on line 1 |
| `event` | A Mission Control line about the event. `{kerbal}` is the kerbal who did it. | `on=yourmod.event` |
| `reply` | The kerbal answers your `event` line, right after it. `to=rt_yourmod_*` answers every line whose id starts with that. | `to=<id of your event line>` |
| `meta` | The settings of one event. It has no text. | `on=yourmod.event` |

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
report. They play on those moments, never on your event. A `science` or `science_pts` row with `on=yourmod.event` is
an error, because it could never play: use `event` for that. Your `{x_...}` slots never have a value there, so a row of
these kinds cannot use them.

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
- The Debug tab has a "Mod events" section. It shows the loaded mod files, a "Test mod event" button and the last
  five mod events with what became of them.

## Testing your lines

1. Put your file in `GameData/<YourMod>/Lonapse/` and start a flight.
2. KSP.log shows `[Lonapse] Lonapse: N mod templates from M file(s): YourMod (N)`, and any bad rows.
3. Open the Lonapse window, go to the Debug tab, pick your event under "Mod events" and press "Test mod event".
   The test fills a slot that is used in a check with the number 3, and any other slot with the name "Test".
