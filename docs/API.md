# Lonapse API and dialogue format

If you want Lonapse to talk about something your mod does, copy [LonapseWrapper.cs](api/LonapseWrapper.cs)
into your mod and change its namespace. You can then call it when something happens. Your mod does not need
`Lonapse.dll` as a reference. If Lonapse is missing, the wrapper returns false and your mod keeps working.

The API version is still **1**. For probe parts, voices and Module Manager patches, see
[ModuleLonapse](ModuleLonapse.md).

## Calling the API

The calls below work with [example_mod.tsv](api/example_mod.tsv). Copy that file to
`GameData/YourMod/Lonapse/example_mod.tsv` and import `System.Collections.Generic` in your code.
The [examples README](api/README.md) has the setup notes for each file.

```csharp
LonapseWrapper.Trigger("examplemod.repair_done", vessel, kerbal,
    names: new Dictionary<string, string> { { "x_part", "la antena|the antenna" } });

LonapseWrapper.Trigger("examplemod.supplies_low", vessel, kerbal,
    numbers: new Dictionary<string, double> { { "x_days", 4 } });
```

The first call reports a repair and gives the part a Spanish and English name. The second reports how many days
of supplies are left. Replace `examplemod` in both the code and the table when you use this for your own mod.

An event name has the form `yourmod.event`, in lowercase, with letters, digits and underscores on either side
of the dot. `vessel` is where it happened. Pass null to use the active vessel. `kerbal` is the
`ProtoCrewMember` who did it. Pass null if Lonapse should choose someone.

Numbers and names are optional. Their keys start with `x_`, like `x_days` and `x_part`. The TSV uses those values
as `{x_days}` and `{x_part}`. Numbers are spoken in Spanish and shown as digits. A name can be `"English"` or
`"Spanish|English"`. The pipe here separates the two languages in an API value. Each dialogue alternative in a
TSV still needs its own row.

Call the wrapper from the main thread. `Trigger` returns true when Lonapse accepts the event. It can then wait
for a free radio turn, up to 90 seconds. A false result means Lonapse cannot take it, for example because it is
unavailable, the lines are missing, the event is muted or cooling down, or the vessel is too far away.

Only the active vessel and vessels or crew within 2.3 km take part. If you give a specific kerbal and that kerbal
becomes unavailable, another kerbal does not take over their conversation. Mission Control can still answer.
Experiments, deployed devices and contracts registered in a table play through their own events. Do not call
`Trigger` for those registered topics.

The wrapper also gives you these checks:

- `Installed` tells you whether Lonapse is installed.
- `Available` tells you whether it is running in flight and enabled.
- `ApiVersion` returns 1, or 0 when Lonapse is missing.
- `HasLines(eventId)` checks whether lines for that event have loaded. Use it after the first flight scene loads.
- `Cancel(eventId)` removes an event that is still waiting. It does not cut off speech or start a cooldown.

You can find accepted and dropped events in `KSP.log` under `[Lonapse] Mod event`.

## File locations

All dialogue uses the same TSV format. Put your files in the folder that fits them:

- `GameData/Lonapse/Phrases/Crew/*.tsv` contains the shipped crew and Mission Control lines.
- `GameData/Lonapse/Phrases/Probe/*.tsv` contains the shipped probe lines and crew conversations with probes.
- `GameData/YourMod/Lonapse/*.tsv` is for your mod's events, conversations, replies, registrations and settings.
- `GameData/YourMod/Lonapse/Probe/*.tsv` is for your mod's probe dialogue.

The filename helps you find the lines. The fields inside decide when and how they play. The shipped files use
`intercom_chatter.tsv` and `radio_chatter.tsv` for routine conversations, `events.tsv` for event conversations,
and `docking.tsv` and `landing.tsv` for those parts of the flight. Replies sit beside the reports they answer.
Shared replies are in `reports.tsv` and still use `trigger=reply`.

## One alternative per row

Use a UTF-8 file with tabs between the columns and a header on the first data row. A line starting with `#` is a
comment. The reader finds columns by name. Your mod can leave out optional columns, but it needs `id`, `line`,
`scene`, `seq`, `speaker`, `trigger`, `when`, `spanish` and `english`.

Each row is one Spanish line with its English subtitle. If you want another way to say the same thing, give it
another row with the same `line`, `scene` and `seq`, and a different `id`. The conditions in `when` belong to
that alternative. Do not put several alternatives in one cell with pipes.

This Mission Control report has three ways to say it. The example includes all 28 columns, in the same order
as the files in `docs/api`. Keep the tabs for empty cells when copying it into a TSV:

```tsv
id	line	scene	seq	speaker	trigger	channel	on	to	when	prefer	spanish	english	band	type	gender	phase	cause	job	trait	topic	core	beep	body	who	cooldown	priority	registration
home_soon	home_report	home_exchange	1	mission_control	report	radio				0	Pronto. Más o menos.	Soon. Roughly.	calm	chatter	both	any	any	any	any	any	any						
home_here	home_report	home_exchange	1	mission_control	report	radio			kerbin	1	Sigues en Kerbin. Paciencia.	You're still at Kerbin. Patience.	calm	chatter	both	any	any	any	any	any	any						
home_ground	home_report	home_exchange	1	mission_control	report	radio			landed	1	Primero tienes que despegar.	You have to take off first.	calm	chatter	both	any	any	any	any	any	any						
```

Lonapse chooses one of these rows for `home_report`. A longer conversation has more lines, played in `seq` order.
The order starts at 1 and has no gaps.

These are the fields you can use:

- `id` is the unique name of this alternative.
- `line` is the name of the line it belongs to. Replies and repetition memory use this name, so keep it stable.
- `scene` groups the lines into a conversation.
- `seq` is the position in that conversation. Alternatives of the same line have the same position.
- `speaker` is `kerbal_a`, `kerbal_b`, `mission_control` or `probe`. Settings and registration rows leave it empty.
- `trigger` says when Lonapse should ask for the conversation. The triggers are listed below.
- `channel` is `intercom`, `radio` or `auto`. The default is `auto`, which chooses from the participants.
  Mission Control uses the radio.
- `on` lists the events or topics the line is for, separated by spaces. Write `examplemod.repair_done` here,
  without `on=`.
- `to` lists the line names a reply can answer. A name ending in `*` matches that prefix. Leave it empty for a
  shared reply. Write the names themselves, without `to=`.
- `when` holds the conditions for this alternative. Empty means there are no conditions.
- `prefer` defaults to 0. A higher value wins when several alternatives fit. Equal values can all be picked.
- `spanish` is the spoken text and `english` is its subtitle. Both use the same variables. Probe beep rows leave
  Spanish empty and use an English caption.
- `band` controls how the voice sounds. It is `calm` by default, with `tense`, `panic` and `none` also available.
- `type` is `chatter`, `callout` or `warning`. The default is `chatter`.
- `gender` is `m`, `f` or `both`. The default is `both`.
- `phase` limits a flight scene to a stage of flight or a group such as `idle` or `serious`. The default is `any`.
- `cause` limits a flight scene to a stress cause. The default is `any`.
- `job` limits a flight scene by profession, such as `pilot` or `scientist`. The default is `any`.
- `trait` limits a flight scene by personality, such as `brave` or `nervous`. The default is `any`.
- `topic` limits a flight scene to a topic, such as an experiment. The default is `any`.
- `core` selects the probe lineset. The default is `any`. It does not choose the beep sound.
- `beep` gives a probe row its mood, such as `happy` or `offended`.
- `body` limits a flight scene to one body. Leave it empty when the same line can use `{body}` for its name.
- `who`, `cooldown` and `priority` set how a mod event is handled. See Mod event settings below.
- `registration` holds the KSP experiment ID, part name or contract class for a registration row.

The first row of a scene sets its shared fields. Empty cells in later rows inherit `trigger`, `channel`, `on`,
`band`, `type`, `phase`, `cause`, `job`, `trait`, `topic`, `core`, `who`, `cooldown` and `priority`. A different
scene starts with the defaults again. Each body-specific scene has its own first row.

The speaker, order and routing must agree across alternatives of one line. Keep each chat, science talk or
contextual probe conversation in one file. Flight scenes can share body-independent lines across files, but
each file needs the scene's shared fields on its first row.

### Jobs, traits and speaker identities

`job` is what the kerbal does. The jobs are pilot, engineer, scientist and tourist. `trait` is their personality,
such as badass, brave, nervous, dim or veteran. The columns filter flight scenes. For different wording within a
runtime conversation, use `job_a`, `job_b`, `trait_a` and `trait_b` in `when`.

For example, the repair conversation in [example_mod.tsv](api/example_mod.tsv) gives an engineer confident
wording with `job_a=engineer`. Someone else gets a less certain line with `job_a!=engineer`. A nervous kerbal
outside that job gets another version with `trait_a=nervous` and `prefer=1`. The scene's `job` and `trait`
columns stay `any` so the conversation is available to those other kerbals too.

`kerbal_a` is the first participant and `kerbal_b` is the second. They keep those identities for the whole
conversation, even when one speaks twice in a row. `job_a=scientist` checks A's job. `job_b=tourist` checks B's.
In a science report A is the reporting kerbal. In a Mission Control report A is the kerbal being addressed.
In a reply A is the kerbal answering.

`is_tourist=1` checks that selected kerbal too. It is still used for tourist reports and the tourist reply pool.
It does not search the crew for another tourist. Use `job_b=tourist` to check the second participant.
The older intercom flight-scene values `job=to_tourist` and `job=tourist_pair` are also supported. They mean
working crew talking to a tourist, and two tourists talking to each other.

`gender=both` allows either voice gender. It does not ask for two speakers. Flight scenes use this field when
choosing a speaker. Runtime conversations use the selected kerbal's own voice. Most lines can leave it as `both`.

Blank rows and comments are skipped. An empty `when` has no conditions. Other empty cells use the inheritance
or defaults described above. They do not add a silent turn. Spoken rows need their Spanish and English text.
Probe beep rows only need the caption.

The old `audio/lonapse/phrases/intercom.tsv` is an archived recording source. Empty pieces in its `spanish_rt`
column reused the main Spanish line. New dialogue goes in `GameData/Lonapse/Phrases/Crew/intercom_chatter.tsv`.

### Phase and band

`phase` describes when a flight scene fits. `band` describes how the voice sounds. For example, `phase=idle`
with `band=calm` gives you a calm voice during the quieter parts of the flight.

The `idle` phase group covers prelaunch, orbit, coast and landed. The `serious` group covers launch, ascent,
reentry and descent. Blackout is separate from both. You can also use an individual phase, such as `orbit`.

`when=orbit` checks the vessel's orbital situation for one alternative. Use `phase` for the flight scene's stage
and `when` for conditions on the wording, including live numbers. Older tables with `phase=calm` or
`phase=goofy` still load as `idle`. The voice band is still called `calm`.

### Conditions and preference

You can check numbers with conditions like `fuel<20`, `ec>=25`, `x_repeat=0` or `exp_count>=2`. You can check the
vessel's situation with `orbit`, `flight`, `suborbital`, `landed`, `escape` or `other`. Several situation names
mean any of those situations can match. Other conditions must all match.

Use `job_a=scientist` or `job_b=pilot` to check a participant's job. Use `trait_a=nervous` for a personality
trait. `job_a!=scientist` checks that A has another job. A missing participant does not pass either job check.
These checks also work for the selected kerbal in reports and replies.

Flight and part tags include `prelaunch`, `launch`, `ascent`, `coast`, `reentry`, `descent`, `blackout`, `kerbin`,
`away`, `solar`, `antenna`, `legs`, `ladder`, `docking`, `heatshield`, `chute`, `engine`, `strut`, `probe`, `rcs`,
`goo`, `sciencejr`, `thermometer`, `barometer`, `seismic`, `gravity` and `magnetometer`. `kerbin` means at Kerbin,
including orbit. It does not mean landed or at a modded home world.

Lonapse first checks the conditions and whether the variables are available. It then picks from the matching
rows with the highest `prefer`. If a preferred row needs a missing value, another row can still play. Equal
preferences share the choice, with repetition memory helping avoid lines you just heard. `prefer` does not
change the event's `priority`.

Use `{body}` for the event's planetary body, `{here}` for the vessel's body, and `{vessel}`, `{probe}` or `{target}` for
names. Values such as `{ec}`, `{fuel}`, `{height_m}`, `{sci}` and `{funds}` come from the flight. If a value is
missing, an alternative that needs it cannot play. Your mod's values use the `x_` prefix.

## Triggers

The trigger tells Lonapse when to ask for a line. It keeps that event's timing, queue rules and cooldowns.
All triggers use the same dialogue format.

- `flight.<event>` is a normal flight reaction, such as `flight.chatter`, `flight.soi_enter`, `flight.probe_chat`,
  `flight.probe_crew` or `flight.countdown`. These scenes use the phase, cause, job, trait and lineset fields.
  See [all flight triggers](#all-flight-triggers) below for the full list.
- `probe` is a contextual probe conversation. Its `on` field gives the topic, such as `probe.power`.
- `report` is a scheduled Mission Control report.
- `chat` is a crew conversation. Add `on` to make it happen for a particular event.
- `science_talk` is an experiment report and Mission Control response, with explicit speakers on each line.
- `science` and `science_pts` are science follow-ups without or with known science points.
- `contract_funds`, `contract_done` and `contract_failed` are contract follow-ups.
- `dock`, `dock_close`, `dock_callout` and `undock` are docking and undocking responses.
- `land` is descent telemetry.
- `warp`, `sunrise`, `eva` and `flag` are follow-ups for those moments.
- `event` is Mission Control's response to a mod's `Trigger` call. The `on` field names the event.
- `reply` is a crew answer. The `to` field says which lines it can answer.
- `meta` holds mod event settings and has no spoken text.
- `experiment`, `device` and `contract` register an item and give its name in both languages.

One event can have more than one kind of response. For example, `flight.science` is the normal flight reaction
and `science_pts` is the later points report. A reply without `to` can answer reports, warp, sunrise, EVA and
ordinary Mission Control chatter. With `to=orbit*` written as `orbit*` in the cell, it can answer any line whose
name starts with `orbit`. The asterisk is a match rule and is never spoken.

### All flight triggers

These are all 36 accepted `flight.*` triggers. The list comes from `PhraseTable.Events` in
[PhraseTable.cs](../src/PhraseTable.cs). Write the full value, including `flight.`, in the `trigger` column.
The related setting, flight conditions and available speakers still decide whether a line can play.

Routine dialogue:

- `flight.chatter` is ordinary crew or Mission Control chatter.
- `flight.relief` is a relief response after a tense moment.
- `flight.station_check` is a station or base check-in.
- `flight.eva_check` is an EVA radio check-in.
- `flight.eva_fall` is a response when an EVA kerbal falls over.

Flight actions:

- `flight.staging` is a staging callout.
- `flight.chute_semi` is a partially deployed parachute callout.
- `flight.chute_full` is a fully deployed parachute callout.
- `flight.throttle_full` is a full-throttle callout.
- `flight.throttle_cut` is a throttle-cutoff callout.
- `flight.plan_node` is a maneuver-node planning response.
- `flight.burn_soon` is a reminder before a maneuver burn.
- `flight.countdown` is a launch countdown line.

Docking and landing:

- `flight.dock_approach` is a docking approach callout.
- `flight.dock_close` is a close-range docking callout.
- `flight.dock_hard` is a final docking approach callout.
- `flight.docked` is a docking-complete response.
- `flight.touchdown` is a landing response.
- `flight.splashdown` is a water-landing response.

Game events:

- `flight.soi_enter` is a response to entering a body's sphere of influence.
- `flight.science` is a science experiment reaction.
- `flight.part_lost` is a response to losing a part.
- `flight.power_low` is a low-power warning.
- `flight.power_ok` is a response when power recovers.
- `flight.signal_lost` is a response to losing the signal.
- `flight.signal_back` is a response when the signal returns.
- `flight.crew_lost` is a response to losing a crew member.

Science experiments:

Use `trigger=flight.science` and put the experiment name below in `topic`. For a crew science conversation, use
`trigger=science_talk` and put the same experiment name in `on`. For example, a temperature reaction uses
`trigger=flight.science` with `topic=temperaturescan`. Its crew conversation uses `trigger=science_talk` with
`on=temperaturescan`. The `science` and `science_pts` follow-ups also use `on` for the experiment.

- `crewreport` is a crew report.
- `evareport` is an EVA report.
- `mysterygoo` is a Mystery Goo observation.
- `surfacesample` is a surface sample.
- `mobilematerialslab` is a materials study.
- `temperaturescan` is a temperature reading.
- `barometerscan` is a pressure reading.
- `seismicscan` is a seismic reading.
- `gravityscan` is a gravity reading.
- `atmosphereanalysis` is an atmosphere analysis.
- `asteroidsample` is an asteroid sample.
- `cometsample` is a comet sample.
- `infraredtelescope` is an infrared telescope observation.
- `magnetometer` is a magnetic-field reading.
- `roc` is a Breaking Ground surface-feature experiment.

These are the experiment-specific values in `PhraseTable.ScienceTopics`. Use `topic=any` for a general flight
science reaction, including unrecognized experiments. A general crew science conversation uses `on=science`
and can say `{exp}` for the experiment's name. Runtime science lines can also use `on=other` specifically for
experiments without a known or registered topic.

Probe dialogue:

- `flight.probe_chat` is routine probe and Mission Control chatter.
- `flight.probe_sleepy` is sleepy probe chatter.
- `flight.probe_hail` is a probe starting a call to Mission Control.
- `flight.probe_stress` is a probe stress response.
- `flight.probe_say_again` is a repeat request on a weak signal.
- `flight.probe_milestone` is a probe milestone response.
- `flight.probe_rover` is a probe rover-driving response.
- `flight.probe_data` is a science data transmission response.
- `flight.probe_crew` is a conversation between crew and a probe on the same vessel. It has no Mission Control
  lines and needs crew aboard.

Some flight events, such as science, power changes, docking and staging, also have probe versions. The probe-only
events are listed above. The contextual probe topics are listed directly below.

### All probe triggers

For these conversations, put `probe` in the `trigger` column and the topic below in `on`. For example,
`trigger=probe` with `on=probe.power` selects a contextual power conversation. The `flight.probe_*` values above
are the separate flight-scene triggers.

The runtime selects these topics in [ChatterDirector.ProbeContext.cs](../src/ChatterDirector.ProbeContext.cs),
[ProbeContext.cs](../src/ProbeContext.cs) and
[ChatterDirector.ProbeStress.cs](../src/ChatterDirector.ProbeStress.cs). Conditions and settings still decide
whether a conversation can play.

Routine reports:

- `probe.power` is a low-battery report.
- `probe.recovery` is a report after the battery recovers.
- `probe.spin` is a response to the probe spinning.
- `probe.node` is a report about a planned maneuver.
- `probe.node_finished` is a follow-up when the planned node is gone.
- `probe.contact` is a response when contact returns.
- `probe.relief` is a response after a stressful moment settles down.
- `probe.biome` is a report about the current biome.
- `probe.orbit` is an orbit or general mission-status report.
- `probe.anniversary` is a mission-day milestone.
- `probe.mission` appears in the shipped general-report lines. The current routine selector uses `probe.orbit`
  for general mission status, so `probe.mission` is not scheduled separately.

Other events:

- `probe.contract` is a contract follow-up.
- `probe.docking` is a docking follow-up.
- `probe.warp` is a time-warp report.

Crew and probe conversations on the same vessel:

- `probe.crew_power` is an engineer and probe talking about power.
- `probe.crew_science` is a scientist and probe talking about the biome or possible measurements.
- `probe.crew_node` is a pilot and probe talking about the next maneuver.

Science and transmission:

- `probe.science_batch` is a report about several experiments together.
- `probe.science_received` confirms that transmitted science data was received.
- `probe.science_interrupted` is a response to an interrupted transmission.

Individual experiments use `probe.science_<experiment-topic>`. These are the built-in names:

- `probe.science_crewreport` is a crew report.
- `probe.science_evareport` is an EVA report.
- `probe.science_mysterygoo` is a Mystery Goo observation.
- `probe.science_surfacesample` is a surface sample.
- `probe.science_mobilematerialslab` is a materials study.
- `probe.science_temperaturescan` is a temperature reading.
- `probe.science_barometerscan` is a pressure reading.
- `probe.science_seismicscan` is a seismic reading.
- `probe.science_gravityscan` is a gravity reading.
- `probe.science_atmosphereanalysis` is an atmosphere analysis.
- `probe.science_asteroidsample` is an asteroid sample.
- `probe.science_cometsample` is a comet sample.
- `probe.science_infraredtelescope` is an infrared telescope observation.
- `probe.science_magnetometer` is a magnetic-field reading.
- `probe.science_roc` is a Breaking Ground surface-feature experiment.
- `probe.science_other` is an experiment without a known or registered topic.

These names follow the experiment topics in [EventWatchers.cs](../src/EventWatchers.cs). They describe the data
being reported. The vessel, available instruments and experiment events decide which ones can occur.

Stress responses:

- `probe.stress_q` is a response to high dynamic pressure.
- `probe.stress_heat` is a response to heating.
- `probe.stress_spin` is a response to rotational stress.
- `probe.stress_gforce` is a response to G force.
- `probe.stress_fuel` is a response to fuel stress.
- `probe.stress_ground` is a response to ground proximity or descent stress.
- `probe.stress_plasma` is a response to plasma affecting the link.
- `probe.stress_impulse` is a response to a jolt or impact.
- `probe.stress_engine_out` is a response to an engine flameout.
- `probe.stress_any` is the response when the stress cause has no specific topic.

Use `x_panic=0` for a tense alternative and `x_panic=1` for a panic alternative. Set `band` to `tense` or `panic`
to match the delivery. The [probe dialogue notes](#probe-dialogue) below cover the available stress readings.

## Mod event settings

A `meta` row uses `on` for the event name and can set these fields:

- `cooldown` is the minimum gap before that event can repeat, in seconds. The default is 120.
- `priority` is `low`, `normal` or `high`. The default is `normal`. High priority bypasses the usual 30-second
  gap and allows the response at higher stress.
- `who` is `crew`, `mc` or `any`. The default is `any`. If both a crew conversation and Mission Control response
  fit, `any` chooses the crew conversation 60% of the time. These are event settings, separate from `speaker`.

The [example mod table](api/example_mod.tsv) has settings, conversations, Mission Control responses and replies
for repair, supplies and a custom experiment event.

## Experiments, deployed devices and contracts

These registrations let Lonapse react to the game's own events. Set `on` to a topic such as `yourmod.soil` and
put the item's ID in `registration`:

- `experiment` uses the KSP experiment ID, the part before `@` in a science subject ID. Its text fills `{exp}`.
- `device` uses the Breaking Ground deployable part name. Its text fills `{device}`.
- `contract` uses the C# contract class name. Its text fills `{contract}`.

Write one name in Spanish and English, without variables or alternatives. Match the articles to your sentences.
The example soil experiment uses `análisis del suelo` and `soil analysis`, with `el {exp}` in the Spanish lines.
Each registered topic and item ID must be unique. Stock experiments and deployed devices already have their own
names and cannot be registered again.

Add `chat`, `science`, `science_pts` or `science_talk` lines for the same topic. Contracts use `contract_funds`,
`contract_done` and `contract_failed`. Do not call `Trigger` for a registered topic.

A generic talk with `on=science` can use `{exp}` for any experiment. Lonapse uses a registered bilingual name
first, then a stock translation. For an unlisted experiment it uses KSP's display name and adjusts its spelling
for the voice. If no name is available, it says “experiment”. You only need a registration when you want your
own translation or a separate topic for that experiment.

Conditions can use `splashed=1` for water and `splashed=0` for dry ground or flight. The `landed` situation
includes splashdown. For EVA, `eva_grounded=1` requires the kerbal to be landed, off ladders and not thrusting.
Use `orbit eva_thrusting=1` for jetpack lines around any body.

Science talks can also use `{temp_c}`, `{press_kpa}`, `{grav_g}`, `{sci_flight}` and `{exp_runs}`. A talk with
`exp_count>=2` is for a batch of experiments. It can use `{exp_count}`, `{exp_list}` and `{sci}` for the total.
`data_sent=1` means the data was transmitted. `data_sent=0` means it was stored, so the line should not claim
Mission Control received it.

The Surface Scanning Module is a resource scanner. It does not fire the science experiment event. A powered,
enabled scanner on the active landed vessel provides `{resource}` and `{resource_pct}` when its reading is
usable. Those values can be used in reports and intercom lines. Resource scanning does not award science points.

Crew history values include `missions`, `flight_no`, `visits`, `landings` and `stars`. Some values are unavailable
in game modes that do not use them. Contract rewards use `{funds}`, `{con_sci}` and `{con_rep}` when available.
Several contracts from a mod can share a class, so registering that class does not distinguish their individual
contract configurations.

## Probe dialogue

Probe lines use the same fields as crew lines. These examples work together:

1. Copy [example_lineset.cfg](api/example_lineset.cfg) to `GameData/YourMod/`. Replace `myProbeCore` with your
   part's internal name. The part must have `ModuleCommand` with `minimumCrew = 0`. The patch needs Module Manager.
2. Copy [example_lineset.tsv](api/example_lineset.tsv) to `GameData/YourMod/Lonapse/Probe/lineset.tsv`. Its
   `core=pip` matches the lineset registered in the config.
3. Optionally copy [example_probe_lineset.tsv](api/example_probe_lineset.tsv) to
   `GameData/YourMod/Lonapse/Probe/power.tsv` for a contextual power conversation using the same lineset.

`LONAPSE_PROBE_LINES` registers the lineset. A voice can choose it with `lines`, or a part can choose it with
`ModuleLonapse.lines`. The deciding probe core's valid module lineset wins, unless the player has selected a
voice with its own lineset. See [ModuleLonapse](ModuleLonapse.md) for the full selection rules.

Ordinary probe scenes use `flight.*` triggers and `core`. Contextual conversations use `trigger=probe` and a
topic in `on`. The full lists are together under [All flight triggers](#all-flight-triggers) and
[All probe triggers](#all-probe-triggers).

A beep row uses `speaker=probe`, empty Spanish and a caption such as `[beeps happily]`. Put `happy`, `offended`,
`curious`, `scared`, `sleepy`, `proud` or `bored` in `beep`. The caption is text, not a condition. Every line must
have an alternative that fits before the conversation starts, including beep rows with conditions.

The voice lines are prepared before the opening sound plays. A vessel or voice change, or readings that no longer
fit, can cancel a waiting conversation. Radio beeps use the receiver effects. Cabin beeps stay local.

Stress responses use the topics listed under [All probe triggers](#all-probe-triggers). Available readings are
`x_q_kpa`, `x_spin_rpm`, `x_heat_pct`, `x_load_g`, `x_height_m`, `x_descent_ms`, `x_fuel_pct` and `x_stress`.

Probe history belongs to the save and is kept per vessel and science subject. The existing settings for pet
talk, hails, stress, transmission, science and milestones still apply. Turning off calm reports does not turn
off stress conversations. Flight scenes and contextual warnings still share their limits on repeating a cause.

## Compatibility and reload

Use lowercase letters, digits and underscores for new alternative and line IDs. New IDs do not need the old
`rt_` prefix. Keep line IDs stable once you publish them, since replies and repetition memory refer to them.
Older `rt_` names and generated body-specific names still work as aliases. When old names merge, the most recent
saved play time is kept.

Older tables with `role`, `severity`, `trait` for the job and `persona` for personality still load. New tables
use `speaker`, `type`, `job` and `trait`. Do not mix the two sets of column names, since the header decides what
`trait` means. Older `a=` and `b=` conditions, `kind` tables, five-column templates, grouped `chat_` and `st_` IDs,
and fixed probe scene files are also supported.

New tables use one alternative per row, as shown in the examples. The shipped dialogue does not need recorded
speech, `phrases.tsv` or `pools.tsv`. API mod events can still show subtitles if Piper is unavailable.

Use **Reload lines** after changing a TSV. Restart KSP after adding voice or lineset registrations or changing
Module Manager patches. The Mods tab controls API templates and contextual probe templates. Fixed flight scenes
load through the flight-scene reader.
