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
map: rock_24x20                  # required: flat_WxH, rock_WxH, chamber_WxH, campaign:<level>
seed: 12345                      # simulation RNG seed
duration_ticks: 600              # required, positive
setup:
  players:
    - { id: 1, gold: 20000, mana: 1000 }
  rooms:                         # parsed, not applied yet
    - { type: Lair, at: [4, 4], size: [3, 3] }
  creatures:                     # placed before tick 0 — see below
    - { type: Imp, at: [6, 6], player: 1 }
commands:                        # real player input, scheduled by tick
  - { at_tick: 0, player: 1, tag: [[5, 5], [6, 5]] }
  - { at_tick: 4, player: 1, untag: [[6, 5]] }
work:                            # harness-injected tile work — see below
  - { at_tick: 8, player: 1, kind: Dig, at: [5, 5] }
  - { at_tick: 12, until_tick: 25, player: 1, kind: Reinforce, at: [5, 4], repeat: 1 }
observe:
  - state_hash
  - events
```

`map: flat_WxH` builds a synthetic dungeon — impenetrable border, dirt path
inside; `rock_WxH` is the same thing filled with diggable rock, which is what a
dig experiment needs; `chamber_WxH` (minimum 7x7) is solid rock with a 3x3
opening at the centre, which is what an experiment with workers needs, since a
worker has to stand somewhere. All three are testing conveniences and **none** is
a DK2 map layout. `map: campaign:level1` loads a shipped map and therefore needs
a Dungeon Keeper 2 installation (`DK2_DIR`).

### `setup.creatures`

Places a creature at a tile before tick 0. **This is not how DK2 makes
creatures**: imps come from a dungeon heart, from the Create Imp spell at a mana
cost, and from level specials, none of which exists yet. Any run that places one
says so, and also says what a worker still cannot do — it only performs work the
keeper marked, and its routing is ours rather than DK2's (Q5).

Only workers (the Imp and the Dwarf) do anything at all so far. Placing anything
else is reported as inert.

### `commands` vs `work`

`commands` is real input. It becomes an `InputFrame` and goes through the same
path the game would use.

`work` is **not**. It puts applications straight onto the world's work queue,
bypassing the worker that would produce them — nothing has decided that an imp
wanted this job, could reach the tile, or was standing beside it. Any run that
uses `work` says exactly that in its report, and the tick numbers in it are not
seconds, because how often work is applied is UNKNOWN (Q19/Q1).

Since workers exist, `work` is the way to exercise the kinds an imp does not do
on its own — claim, reinforce, repair, attack, fill in. To watch a worker dig or
mine, use `setup.creatures` and a `tag` command instead.

`kind` is a `TileWorkKind` name: `Dig`, `MineGold`, `DigOwnWall`, `DigEnemyWall`,
`Claim`, `AttackTile`, `RepairTile`, `Reinforce`, `RepairWall`, `FillIn`.
`repeat` queues several applications on the same tick; `until_tick` repeats the
whole entry every tick up to and including that one.

## The honesty rule

The loader reports any key it does not recognise, and the runner reports
anything it recognises but cannot yet act on — unapplied `setup` entries,
observations nothing produces, and the fact that no simulation systems are
registered at all. All of it lands in `report.md` under **"Not answered by this
run"**.

This is deliberate. The regression corpus is the artifact this project relies on
to tell it when it is wrong; a scenario that silently does less than it says
would quietly poison it. Asserted by
`DK2.Behavior.Tests.ScenarioTests.Run_ReportsWhatItsWorkersStillCannotDo` and its
neighbours.

## Current limits (M3)

Tile mechanics run for real: `scenarios/tiles/` digs, claims, reinforces and
mines against DK2's own numbers. `scenarios/imps/` puts workers on the map and
lets them find the work themselves. See `docs/research/tile-work.md` and
`docs/research/imp-tasks.md`.

What a run still cannot tell you:

- **How many workers a keeper would have.** Nothing generates imps; the scenario
  file places them.
- **What a worker does when nothing is marked.** Claiming, reinforcing, carrying
  gold and fleeing are not implemented, and their priority order is UNKNOWN
  (Q24).
- **How it would move.** Routing is ours, not DK2's (Q5): four-connected,
  uniform cost. Travel times follow from that choice.
- **How long anything takes.** The health deltas carry no unit (Q19), and the
  tick rate is unmeasured (Q1). Tick numbers in a report are ordering, not time.
- **Anything about rooms, spells or the economy.** Gold released by mining is
  reported and dropped — there is no treasury to put it in.
