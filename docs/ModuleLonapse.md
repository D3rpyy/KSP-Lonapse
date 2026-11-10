# ModuleLonapse and Module Manager

`ModuleLonapse` assigns a probe core its beep voice, dialogue lineset, selection priority and display name.
It is a KSP `PartModule`; Module Manager (MM) can add or edit it in a part's config. No plugin code or speech
recordings are needed for this integration. Event-triggered dialogue from another plugin uses the
[reflection API](API.md) instead.

Lonapse considers parts with a `ModuleCommand` whose `minimumCrew` is `0`. Adding `ModuleLonapse` to a part
without an eligible command module does not make that part a probe core. Keep one `ModuleLonapse` per part;
Lonapse reads the first one.

## Add the module to a part

Place a `.cfg` under `GameData/YourMod/`:

```cfg
@PART[myProbeCore]:HAS[!MODULE[ModuleLonapse]]:NEEDS[Lonapse]:AFTER[Lonapse]
{
    MODULE
    {
        name = ModuleLonapse
        voice = okto
        lines = okto
        priority = 0
        displayName = My Probe
    }
}
```

Replace `myProbeCore` with the part's internal `name`. The `HAS` guard prevents a duplicate module.
`NEEDS[Lonapse]` makes the patch conditional on Lonapse. `AFTER[Lonapse]` runs after Lonapse's stock-core patches.
Use your own mod's pass for your other patches; do not mark a third-party integration `FOR[Lonapse]`.

A part author can include the same `MODULE` block directly inside `PART`, without MM. MM is required to apply
patch syntax such as `@PART` to an existing config, but is not required for Lonapse's own config-node readers.

## Edit an existing module

Stock cores already receive the module from `Lonapse/Patches/Lonapse_StockCores.cfg` when MM is installed.
Edit that module instead of adding another:

```cfg
@PART[probeCoreOcto]:HAS[@MODULE[ModuleLonapse]]:NEEDS[Lonapse]:AFTER[Lonapse]
{
    @MODULE[ModuleLonapse]
    {
        %voice = mymod_probe
        %lines = mymod_lines
        %priority = 10
        %displayName = Surveyor
    }
}
```

The voice and lineset names must exist; see the registration examples below. Without MM, Lonapse still
recognizes stock cores and supplies their stock voices from its built-in part-name list.

## Fields and selection

| Field | Default | Meaning |
| --- | --- | --- |
| `voice` | Empty | Stock voice ID or a registered `LONAPSE_PROBE_VOICE` name. Controls beep sound. |
| `lines` | Empty | Stock dialogue ID or registered `LONAPSE_PROBE_LINES` name. Overrides the selected voice's dialogue on the deciding core, except for a player-selected voice. |
| `priority` | `0` | Higher wins among cores whose module names a valid voice. Lonapse's stock MM patches use `-1`. |
| `displayName` | Empty | Name shown for the deciding core and used by `{probe}`. Supports a localization tag such as `#LOC_YourMod_ProbeName`. Falls back to the voice's display name when absent. |

Stock voice/lines IDs are `stayputnik`, `qbe`, `okto`, `hecs`, `rovemate`, `snob`, `mk2drone`, `mpo`, `mtm`,
and `generic`.

On a vessel with several eligible cores, the deciding core is selected in this order:

1. The first core with a valid player voice override.
2. The module with the highest `priority` that names a valid voice; ties keep the first core in root-first order.
3. The first recognized stock core with a valid stock voice.
4. The first eligible core, using the generic voice.

Unknown voice names are skipped. Setting only `lines` or `displayName` does not make a core win the voice
selection; priority applies to a valid module `voice`.

The deciding core's valid `lines` value wins over the selected voice's `lines`. An empty or invalid value falls
back to the voice's lineset. A player-selected voice keeps its own lineset, so it overrides the module's `lines`.
The deciding part's `displayName` still applies when the player changes its voice.

## Register a voice and dialogue lineset

These are top-level config nodes, outside `PART`. KSP reads them from any `.cfg` in GameData; MM is not required:

```cfg
LONAPSE_PROBE_LINES
{
    name = mymod_lines
    displayName = Surveyor
}

LONAPSE_PROBE_VOICE
{
    name = mymod_probe
    displayName = Surveyor
    lines = mymod_lines
    baseHz = 900
    wave = triangle
    rhythm = swing
}
```

A lineset ID uses lowercase `a-z`, `0-9` and `_`, up to 32 characters. It cannot be `any` or an existing stock
core ID. Register it before referring to it from a voice or dialogue table. The `displayName` on a lineset
registration labels the lineset; set the voice/module `displayName` to control the probe's displayed name.

A custom voice uses synthesized beeps and can define additional sound parameters and `MOOD` patterns.
See [Probe voices](../README.md#probe-voices) for the sound parameters. The voice's `lines` controls dialogue
independently of its beep sound; registering a new sound does not require a new lineset.

## Add the dialogue

Put a UTF-8 TSV in `GameData/YourMod/Lonapse/Probe/`, for example `personalities.tsv`. Crew and Probe now use
the same alternative-row schema. Each row contains one bilingual alternative; `line` and `scene` associate it
with an ordered exchange. There are no pipe-separated wordings or parallel condition cells.

```tsv
id	line	scene	seq	role	trigger	channel	core	beep	when	spanish	english
mymod_hello	mymod_hello	mymod_chat	1	mc	flight.probe_chat	radio	mymod_lines			Hola, {probe}.	Hello, {probe}.
mymod_answer	mymod_answer	mymod_chat	2	probe	flight.probe_chat	radio	mymod_lines	happy			[beeps happily]
```

`core` selects the registered lineset. `seq` orders the logical lines. Additional alternatives repeat the same
`line`/`seq`, have their own unique `id`, and carry their own `when` condition and optional numeric `prefer`.
Shared scene metadata can be written on the first row and inherited. Use `{body}`, `{probe}`, `{vessel}` and
available telemetry variables rather than copying a line once per planet or craft.

Contextual exchanges use `trigger=probe`, with `probe.<topic>` in `on` and numeric checks in `when`. Beep rows
use `role=probe`, empty Spanish, a bracketed subtitle and the selected mood in `beep`. They are not Piper speech.
The same selector checks conditions and variable availability for Crew speech, MC speech and probe exchanges.

See [the unified format](API.md#one-alternative-per-row), [probe dialogue](API.md#probe-dialogue), and the
[contextual example](api/example_probe_lineset.tsv). The [lineset config](api/example_lineset.cfg),
[scene table](api/example_lineset.tsv) and contextual example share the `pip` registration. Legacy mod formats
and `rt_` IDs remain supported through compatibility readers.

## Reloading and troubleshooting

Use **Reload lines** in the Mods tab after editing category TSVs. Restart KSP after adding or changing voice or
lineset registrations or MM patches. MM changes part prefabs; check existing craft/save modules separately
because KSP serializes part-module fields.

If a patch appears ineffective, check the target part name, eligible `ModuleCommand`, duplicate modules,
registered voice/lineset names, and saved player overrides. On multi-core vessels, check which core wins and its
priority. Invalid registrations and TSV rows are logged to `KSP.log` with `[Lonapse]`.

Piper is required for MC/crew speech on Windows or Linux. Probe beeps are a separate sound system. MM changes
probe configuration; it does not install Piper or create missing dialogue.
