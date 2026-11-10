# API examples

These examples use the current dialogue format. The TSV files start with their column names so they also open
properly in table view. Every row has the same 28 columns. Each spoken alternative pairs its Spanish text with
its English subtitle.
The full field descriptions are in the [API guide](../API.md).

## Mod events

Copy [example_mod.tsv](example_mod.tsv) to `GameData/YourMod/Lonapse/example_mod.tsv`. Copy
[LonapseWrapper.cs](LonapseWrapper.cs) into your mod and change its namespace. The calls in the wrapper's example
match the event names and variables in the table. Replace `examplemod` in both places when adapting it.

The table has examples for repairs, supplies, a custom event, and registered experiments, devices and contracts.
Replies are beside the responses they answer. The `to` field gives the line name they can answer.

The repair conversation shows the difference between jobs and personality traits. An engineer gets the
`job_a=engineer` wording. Another profession gets `job_a!=engineer`. A nervous kerbal outside that job gets the
`trait_a=nervous` alternative with `prefer=1`.

`kerbal_a` and `kerbal_b` are the two participants. They keep those identities through the conversation.
The `job` and `trait` columns stay `any` in this example so they do not block the alternatives for other kerbals.

## Probe linesets

Copy [example_lineset.cfg](example_lineset.cfg) to `GameData/YourMod/`. Replace `myProbeCore` with your part's
internal name. It needs `ModuleCommand` with `minimumCrew = 0`. The part patch needs Module Manager.

The config registers the `pip` lineset and the `pipvoice` voice. Both probe tables use `core=pip` and share that
registration.

- Copy [example_lineset.tsv](example_lineset.tsv) to `GameData/YourMod/Lonapse/Probe/lineset.tsv` for ordinary
  probe flight scenes. These use `trigger=flight.probe_chat`.
- Optionally copy [example_probe_lineset.tsv](example_probe_lineset.tsv) to
  `GameData/YourMod/Lonapse/Probe/power.tsv` for a contextual power conversation. This uses `trigger=probe` and
  `on=probe.power`.

Probe beep rows leave Spanish empty. Their English text is a caption, and `beep` supplies the mood.
`phase=idle` describes the quieter flight stages. `band=calm` describes how the voice sounds.

After changing a TSV, use **Reload lines**. Restart KSP after adding the config or changing its patch.
