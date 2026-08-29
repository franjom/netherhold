# Scenarios

A scenario is a declarative experiment. The intent (plan §6.2) is that one file
eventually drives **both** our simulation and the original game, so that the
answers can be compared rather than asserted.

Run them with the headless harness:

```
dotnet run --project src/DK2.Harness -- list
dotnet run --project src/DK2.Harness -- run scenarios
dotnet run --project src/DK2.Harness -- run scenarios/smoke/determinism_flat_map.yaml
```

Each run writes to `build/scenario-runs/<id>/`:

| File | Contents |
|---|---|
| `report.md` | Summary, the question asked, and **what the run could not do** |
| `hashes.tsv` | State hash after every tick |
| `events.log` | Everything the simulation emitted |

## Format

```yaml
id: job-selection-001            # required, unique — names the output directory
question: >                      # what this experiment is trying to find out
  Prose, for humans. Never parsed.
map: flat_24x20                  # required: flat_WxH, or campaign:<level>
seed: 12345                      # simulation RNG seed
duration_ticks: 600              # required, positive
setup:
  players:
    - { id: 1, gold: 20000, mana: 1000 }
  rooms:                         # parsed, not applied yet (M3)
    - { type: Lair, at: [4, 4], size: [3, 3] }
  creatures:                     # parsed, not applied yet (M4)
    - { type: Goblin, at: [6, 6] }
observe:
  - state_hash
  - events
```

`map: flat_WxH` builds a synthetic dungeon — impenetrable border, dirt path
inside. It is a testing convenience and **not** a DK2 map layout.
`map: campaign:level1` loads a shipped map and therefore needs a Dungeon Keeper 2
installation (`DK2_DIR`).

## The honesty rule

The loader reports any key it does not recognise, and the runner reports
anything it recognises but cannot yet act on — unapplied `setup` entries,
observations nothing produces, and the fact that no simulation systems are
registered at all. All of it lands in `report.md` under **"Not answered by this
run"**.

This is deliberate. The regression corpus is the artifact this project relies on
to tell it when it is wrong; a scenario that silently does less than it says
would quietly poison it. Asserted by
`DK2.Behavior.Tests.ScenarioTests.Run_ReportsThatNoSystemsAreRegisteredYet` and
its neighbours.

## Current limits (M2)

No simulation systems exist yet, so a run advances the tick and nothing else.
Scenarios can be *written* now — that is useful, and the questions in them are
real — but none of them are answered yet. The first ones that will be are the
M3 dig/claim scenarios, whose numbers are already extracted in
`docs/research/kwd-terrain-rooms.md` and `kwd-variables.md`.
