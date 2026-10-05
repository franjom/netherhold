# Scenarios

A scenario is a declarative experiment. The intent (`DK2_REIMPLEMENTATION_PLAN.md`
§6.2) is that one file eventually drives **both** our simulation and the original
game, so that the answers can be compared rather than asserted.

Run them with the headless harness:

```
dotnet run --project src/DK2.Harness -- list
dotnet run --project src/DK2.Harness -- run scenarios
dotnet run --project src/DK2.Harness -- run scenarios/smoke/determinism_flat_map.yaml
```

Each run writes to `build/scenario-runs/<id>/`:

| File | Contents |
|---|---|
| `report.md` | Summary, the question asked, the balance values used, and **what the run could not do** |
| `hashes.tsv` | State hash after every tick |
| `events.log` | Everything the simulation emitted |
| `world.txt` | The final world as `TextRenderer` draws it for `viewing_player` |

## Format

The loader is `src/DK2.Harness/Scenario.cs`; it is the authority on every key, and
it reports by name any key it does not recognise.

```yaml
id: job-selection-001            # required, unique — names the output directory
question: >                      # what this experiment is trying to find out
  Prose, for humans. Never parsed.
map: rock_24x20                  # required: flat_WxH, rock_WxH, chamber_WxH, campaign:<level>
seed: 12345                      # simulation RNG seed
duration_ticks: 600              # required, positive
fog_of_war: false                # optional: per-keeper discovery (ADR-0076)
viewing_player: 1                # optional: the keeper world.txt is drawn for
setup:
  placed_things: false           # optional: place the shipped level's own THINGS
  players:
    - { id: 3, gold: 20000, mana: 1000 }
  rooms:                         # laid down before tick 0 — see below
    - { type: Treasury, at: [4, 4], size: [3, 3], player: 3 }
  creatures:                     # placed before tick 0 — see below
    - { type: Imp, at: [6, 6], player: 3 }
    - { type: Goblin, at: [7, 6], player: 3, health: 0, hunger: 2 }
  objects:                       # loose things on the floor before tick 0
    - { type: Chicken, at: [8, 6], player: 3 }
commands:                        # real player input, scheduled by tick
  - { at_tick: 0, player: 3, tag: [[5, 5], [6, 5]] }
  - { at_tick: 4, player: 3, untag: [[6, 5]] }
  - at_tick: 6                   # builds and pays, through the ground rules
    player: 3
    build: { type: Lair, tiles: [[7, 7], [8, 7]] }
  - { at_tick: 40, player: 3, sell: [[7, 7]] }
  - { at_tick: 50, player: 3, create_imp: true }
  - { at_tick: 60, player: 3, pick_up: 1 }
  - { at_tick: 62, player: 3, pick_up_object: 1 }
  - { at_tick: 64, player: 3, drop: [9, 9] }
  - { at_tick: 70, player: 3, slap: 1 }
work:                            # harness-injected tile work — see below
  - { at_tick: 8, player: 3, kind: Dig, at: [5, 5] }
  - { at_tick: 12, until_tick: 25, player: 3, kind: Reinforce, at: [5, 4], repeat: 1 }
observe:
  - state_hash
  - events
```

`observe` takes `state_hash` and `events`; anything else is reported as an
observation nothing produces.

`map: flat_WxH` builds a synthetic dungeon — impenetrable border, dirt path inside;
`rock_WxH` is the same thing filled with diggable rock, which is what a dig
experiment needs; `chamber_WxH` (minimum 7x7) is solid rock with a 3x3 opening at the
centre, which is what an experiment with workers needs, since a worker has to stand
somewhere. All three are testing conveniences and **none** is a DK2 map layout.
`map: campaign:level1` loads a shipped map and therefore needs a Dungeon Keeper 2
installation (`DK2_DIR`).

### `setup.placed_things`

Places the shipped level's own THINGS records — its creatures, objects and rooms —
before tick 0, through `ThingProjection` (ADR-0101). **Opt-in and off by default**,
because turning it on for an existing scenario changes what that scenario is
measuring. Only meaningful with a `campaign:` map; a synthetic map has no THINGS
file. Anything the projection cannot place yet is counted in the report.
`scenarios/smoke/campaign_level1_placed_things.yaml` is the example.

### `setup.players[].gold`

A keeper's gold is not a counter: it is stored in treasury and dungeon-heart tiles
and nowhere else (ADR-0015). Starting gold is *deposited*, so a player with no room
to keep it in gets a report line saying how much had nowhere to go rather than a
silent number. Give the player a `Treasury` or a `Dungeon Heart` in `setup.rooms`
first. Mana is a player-level pool and needs no room.

With `placed_things: true` the level's PLAYERS record already pays each keeper's starting
gold into its heart. A positive `gold` here **replaces** that amount rather than adding to
it; `0` leaves the level's own. (Until 2026-10-05 it was added on top, which gave Level 17's
Harkan 20,000 where DK2 gives 10,000.)

### `setup.rooms`

Lays room terrain down before tick 0, owned by `player` (default 0). `size` is
`[w, h]` and defaults to one tile; tiles the room cannot sit on are skipped and
counted in the report.

**This is the level author placing a room, not a keeper building one.** Nothing is
charged, no imp constructs it, and the only rule applied is the room's own
placeable-on flags. Use a `build` command to exercise the ground rules and the
treasury.

**Where the tiles are already that room, only the owner changes** — one way for a
scenario to start with a claimed portal. Taking one properly is also available now:
`TileWorkKind.ConvertRoom` is what an imp applies, one tile is worked and the whole
room instance changes hands (Q92, ADR-0044), which
`scenarios/creatures/an_imp_claims_a_portal.yaml` runs end to end.

Most rooms are still terrain that somebody owns. The ones that do more: a **treasury**
and a **dungeon heart** hold gold; a **portal** produces creatures; a **lair**
attracts them and holds one bed per tile, which a creature claims, walks to, sleeps
in and is healed by; a **hatchery** knows how many coops its size earns and how big a
flock it holds, and lays on its own clock — an egg every few seconds depending on its
size, a chicken two seconds later, stopping at one bird per tile (ADR-0066) — while
hungry creatures come and eat those birds, which starts a full hatchery laying again
(ADR-0067); a **prison** holds the enemies an imp drags in and withers them into
Skeletons; a **graveyard** is where an imp carries a corpse.

### `setup.creatures`

Places a creature at a tile before tick 0. **This is not how DK2 makes creatures**:
imps come from a dungeon heart and from the Create Imp spell at a mana cost
(ADR-0021), everything else comes out of a portal, and a shipped level's own roster
comes from `setup.placed_things`. Any run that places one says so.

`health` is optional and **nullable rather than defaulting to full**: omit it for the
record's `Hp`, and every number including `0` means itself. It exists because nothing
in a scenario can hurt a creature — there is no combat — so a wounded creature has to
start wounded; `health: 0` is how a run watches one lie on the floor for
`CreatureDyingStateDurationSeconds` and then die (Q99).

`hunger` is helpings of food owed, for the same reason. It is the only way to reach
the prison's eating rule at all: a Goblin's `HungerRate` is 180 game seconds and a
cell turns it to bone in 83, so no prisoner ever gets hungry *in* a prison, but
`HungerNeed` accumulates over a creature's whole life and is paid off only by eating
(`docs/research/prison.md`).

### `setup.objects`

Puts a loose object — a chicken, a pile of gold — on the floor before tick 0. Here
because a keeper's hand can hold one (ADR-0075) and nothing in a scenario can lay
one: a hatchery lays on its own schedule, and waiting for a bird is not the thing
under test when the thing under test is what happens after it is dropped. `gold: n`
puts `n` gold in a gold object, so a pile is something an imp can carry.

### `commands`

Real input. Each entry becomes part of an `InputFrame` on `at_tick` and goes through
the same path the game would use, so every ground rule and every refusal applies.

| Key | Command | Notes |
|---|---|---|
| `tag`, `untag` | `TagTilesCommand` | `[[x, y], …]` |
| `build: { type, tiles }` | `BuildRoomCommand` | goes through `RoomRules` and `GoldStore`; tiles are charged in the order listed, so a keeper who can afford four of six gets the first four and a `built` event naming the reason for the rest |
| `sell` | `SellRoomCommand` | `[[x, y], …]`, in the order each refund is banked. 50 % of cost per tile (Q34, ADR-0029); the tile reverts to claimed floor and the keeper keeps the ground |
| `create_imp: true` | `CreateImpCommand` | buys one imp at the dungeon heart for mana — the one command that spends a resource |
| `pick_up` | `PickUpCreatureCommand` | a creature id. Ids are assigned in the order `setup.creatures` lists them, starting at 1 |
| `pick_up_object` | `PickUpObjectCommand` | an object id (ADR-0075) |
| `drop: [x, y]` | `DropCommand` | no creature is named, because the hand chooses: DK2 puts down what it picked up last, and letting a scenario name one would hide that |
| `drop_onto` | `DropCommand.TargetCreatureId` | with `drop`: the creature id under the cursor. A chicken or gold dropped onto it feeds or pays it on the spot (ADR-0217) |
| `slap` | `SlapCreatureCommand` | a creature id |

### `commands` vs `work`

`commands` is real input. `work` is **not**: it puts applications straight onto the
world's work queue, bypassing the worker that would produce them — nothing has
decided that an imp wanted this job, could reach the tile, or was standing beside it.
Any run that uses `work` says exactly that in its report.

Since workers exist, `work` is the way to exercise the kinds an imp does not choose
on its own. To watch a worker dig or mine, use `setup.creatures` and a `tag` command
instead.

`kind` is a `TileWorkKind` name: `Dig`, `MineGold`, `DigOwnWall`, `DigEnemyWall`,
`Claim`, `AttackTile`, `Reinforce`, `RepairWall`, `ConvertRoom`. (`RepairTile` and
`FillIn` were removed: DK2 has no fill-in mechanism and neither health variable has a
reader — Q219.) `repeat` queues several applications on the same tick; `until_tick`
repeats the whole entry every tick up to and including that one.

## The honesty rule

The loader reports any key it does not recognise, and the runner reports anything it
recognises but cannot yet act on — unapplied `setup` entries, work it cannot place,
and observations nothing produces. All of it lands in `report.md` under **"Not
answered by this run"**, together with the standing caveats: what a mana total is
short of, and what a dungeon heart on the map is not.

This is deliberate. The regression corpus is the artifact this project relies on to
tell it when it is wrong; a scenario that silently does less than it says would
quietly poison it. Asserted by `DK2.Behavior.Tests.ScenarioTests`:
`Run_ReportsWhatItsWorkersStillCannotDo`, `Run_ReportsWorkItCannotPlace`,
`Run_ReportsSetupItWillNotApply`, `Run_ReportsUnknownKeysRatherThanIgnoringThem` and
`Run_ReportsThatInjectedWorkHasNoWorkerAndNoKnownRate`.

## The corpus, and what a run still cannot tell you

`scenarios/tiles/` digs, claims, reinforces and mines against DK2's own numbers.
`scenarios/imps/` puts workers on the map and lets them find the work themselves,
including the whole gold round trip, where one imp mines a shipped seam and carries
every coin of it into a shipped dungeon heart, and the return leg where another imp
fetches a pile off the floor. `scenarios/rooms/` builds a room out of a dungeon
heart, sells one back, and watches a hatchery lay and hatch. `scenarios/mana/` is the
run in which time alone changes something: a keeper's land and heart pay them once a
game second, and paving three tiles of that land into a treasury lowers the rate.
`scenarios/fog/` reveals part of a world through one creature's eyes (ADR-0076).
`scenarios/creatures/` is the largest set: a portal fills a dungeon, a keeper takes a
neutral portal and receives the level's own creatures at the level's own interval, a
Goblin gets hungry after exactly the 180 seconds its record gives while an Imp never
does, eats a chicken, collapses and dies, walks out for want of a lair, gets found
work, gets picked up and slapped, gets dragged to a prison, gets buried; and a keeper
meets its wage bill. `scenarios/smoke/` loads campaign level 1, with and without its
own THINGS, and pins determinism on a flat map. See `docs/research/tile-work.md`,
`imp-tasks.md`, `rooms-and-gold.md`, `mana.md`, `fog-of-war.md`, `portals.md`,
`creature-needs.md`, `job-selection.md`, `prison.md` and `placed-things.md`.

What a run still cannot tell you:

- **How a creature would move.** Routing is ours, not DK2's (Q5): four-connected,
  uniform cost. Travel times follow from that choice.
- **What a fight looks like.** There is no combat: nothing damages a creature, so
  nothing kills one except a prison or the dying timer, and `health` on a placed
  creature is the only wound a scenario can inflict. Defeat is not implemented either
  — a keeper whose dungeon heart is beaten down would stop earning its heart's 30 a
  second and play on (Q40).
- **What most spells do.** Create Imp is the only one. Mana has both halves of its
  economy and not all of either: income is land and a dungeon heart, outgoings are
  Create Imp's cast cost and the 7 a second every imp charges its keeper (Q53,
  ADR-0022), and every other keeper spell is missing — a summoned Horned Reaper costs
  2000 a second and Call To Arms 90.
- **How long a room takes to build.** Building is instant and paid in full: an imp
  does not walk to the site, so a `build` command is the ground rules and the
  treasury and nothing else.
- **What a worker does when it has no job.** The priority order is CONFIRMED and
  built — marked excavation, then claiming, then reinforcing (Q24, ADR-0018) — and
  carrying is built on top of it (`ImpTaskKind`: `FetchGold`, `CarryObject`,
  `CarryCorpse`, `CarryUnconscious`). What is absent is DK2's fourth behaviour: an
  imp wandering the dungeon and interacting with scenery and creatures. Here it
  stands still (Q41). Fleeing and building rooms are absent too.
- **What every job pays.** Health, hunger, sleep in its own bed, eating, anger with a
  measured ceiling, levels with experience, and a job chosen the way DK2's mood
  choosers choose one (ADR-0082, ADR-0088) are all built, and three jobs pay:
  training grants experience and charges its bill (ADR-0083), research credits the
  keeper, praying runs once a game second. Manufacture pays nothing, and Guard, Drink
  and the roomless jobs (Leave, Rebel, Explore, StealGold) are chosen, logged and
  occupy the creature without doing anything else. How long a job is kept is ours: all
  six of DK2's duration variables have no reader (ADR-0082).
- **Where uncollectable gold goes.** Mined gold reaches a treasury, carried by the
  imp that freed it (Q31), and a destroyed treasury leaves piles on the floor
  (ADR-0055) that an imp will fetch. What is still lost is the overflow when there is
  nowhere at all to unload: the imp keeps mining, as in the original, and the run
  reports `gold_uncollected`.

Tick numbers in a report are ordering **and** time: a game second is four ticks of
250 ms, read from the binary and stopwatched by the owner (Q1, ADR-0089). What the
tick numbers in a `work` entry still do not tell you is how often DK2 would have
applied that work, because nothing chose the job — the per-application amount is a
game-second delta divided across the ticks of a second (Q19).
