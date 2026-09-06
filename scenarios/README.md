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
fog_of_war: false               # optional: enable per-keeper discovery
viewing_player: 1               # optional: keeper used for world.txt
seed: 12345                      # simulation RNG seed
duration_ticks: 600              # required, positive
setup:
  players:
    - { id: 3, gold: 20000, mana: 1000 }
  rooms:                         # laid down before tick 0 — see below
    - { type: Treasury, at: [4, 4], size: [3, 3], player: 3 }
  creatures:                     # placed before tick 0 — see below
    - { type: Imp, at: [6, 6], player: 3 }
commands:                        # real player input, scheduled by tick
  - { at_tick: 0, player: 3, tag: [[5, 5], [6, 5]] }
  - { at_tick: 4, player: 3, untag: [[6, 5]] }
  - at_tick: 6                   # builds and pays, through the ground rules
    player: 3
    build: { type: Lair, tiles: [[7, 7], [8, 7]] }
work:                            # harness-injected tile work — see below
  - { at_tick: 8, player: 3, kind: Dig, at: [5, 5] }
  - { at_tick: 12, until_tick: 25, player: 3, kind: Reinforce, at: [5, 4], repeat: 1 }
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

### `setup.players[].gold`

A keeper's gold is not a counter: it is stored in treasury and dungeon-heart
tiles and nowhere else (ADR-0015). Starting gold is *deposited*, so a player with
no room to keep it in gets a report line saying how much had nowhere to go rather
than a silent number. Give the player a `Treasury` or a `Dungeon Heart` in
`setup.rooms` first. Mana is a player-level pool and needs no room.

### `setup.rooms`

Lays room terrain down before tick 0, owned by `player` (default 0). `size` is
`[w, h]` and defaults to one tile; tiles the room cannot sit on are skipped and
counted in the report.

**This is the level author placing a room, not a keeper building one.** Nothing
is charged, no imp constructs it, and the only rule applied is the room's own
placeable-on flags. Use a `build` command to exercise the ground rules and the
treasury.

**Where the tiles are already that room, only the owner changes** — which is how
a scenario says "claimed portal". A portal ships owned by the neutral player
(`PlayerIds`, ADR-0043) and there is no work kind for room conversion (Q92), so
an imp cannot take one; re-owning is the stand-in, and the report says so as a
shortfall rather than passing it off as setup.

And note what most rooms still are not. A treasury and a heart hold gold because
a variable says how much per tile. **Three rooms now do more**: a **portal**
produces creatures; a **lair** attracts them *and* holds one bed per tile, which
a creature claims, walks to, sleeps in and is healed by; and a **hatchery** knows
how many coops its size earns, how big a flock it holds, and — since ADR-0066 —
**it lays on its own clock**: an egg every few seconds depending on its size, a
chicken two seconds later, stopping at one bird per tile — and **since ADR-0067
hungry creatures come and eat those birds**, which starts a full hatchery laying
again. A prison holds the enemies an imp drags in and withers them into
Skeletons. Every other room is inert terrain that somebody owns.

### `commands[].build`

A real player command: it goes through the input frame, the ground rules
(`RoomRules`) and the treasury (`GoldStore`), exactly as the tag command does.
Tiles are charged in the order they are listed, so a keeper who can afford four
of six gets the first four and a `built` event naming the reason for the rest.

### `setup.creatures`

Places a creature at a tile before tick 0. **This is not how DK2 makes
creatures**: imps come from a dungeon heart, from the Create Imp spell at a mana
cost, and from level specials, none of which exists yet. Any run that places one
says so, and also says what a worker still cannot do — it only performs work the
keeper marked, and its routing is ours rather than DK2's (Q5).

**Non-workers are no longer inert.** A creature with a lair claims a bed, walks
to it, sleeps and heals; it gets hungry; it grows angry and, if it is evil and
angry enough, walks to the nearest portal and leaves the dungeon. What it still
has no notion of is a **job**, and nothing can be picked up or carried.

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
lets them find the work themselves - including the whole gold round trip, where
one imp mines a shipped seam and carries every coin of it into a shipped dungeon
heart. `scenarios/rooms/` builds a room and pays for
it out of a dungeon heart. `scenarios/mana/` is the first run in which time alone
changes something: a keeper's land and heart pay them once a game second, and
paving three tiles of that land into a treasury lowers the rate. See
`docs/research/tile-work.md`,
`docs/research/imp-tasks.md`, `docs/research/rooms-and-gold.md` and
`docs/research/mana.md`.

`scenarios/creatures/` is where M4 starts. A keeper opens a route to level
1's neutral portal, takes it whole, and receives the level's own creatures at the
level's own interval — and a Goblin placed beside an Imp gets hungry after
exactly the 180 seconds its record gives while the Imp never does. See
`docs/research/portals.md`, `docs/research/binary-entrance.md` and
`docs/research/creature-needs.md`.

What a run still cannot tell you:

- **How many workers a keeper would have.** Imps now come from a dungeon heart
  and from Create Imp at a mana cost (ADR-0021), and everything else comes out of
  a portal. What is still missing is a level's own starting roster, because
  THINGS is unparsed — so a creature a scenario places stands in for one the
  level file would have put there.
- **What a worker does when it runs out of jobs.** The priority order is
  CONFIRMED and built - marked excavation, then claiming, then reinforcing
  (Q24, ADR-0018) - and carrying mined gold is built as the tail of a mining job
  (Q31, ADR-0019). What is absent is the fourth behaviour: DK2 has an imp wander
  the dungeon and interact with scenery and creatures, and here it stands still
  (Q41). Fleeing, dragging bodies and building rooms are absent too.
- **How it would move.** Routing is ours, not DK2's (Q5): four-connected,
  uniform cost. Travel times follow from that choice.
- **How long anything takes.** The health deltas carry no unit (Q19), and the
  tick rate is unmeasured (Q1). Tick numbers in a report are ordering, not time.
- **What a creature is.** Health, hunger, sleep in its own bed, anger with a
  measured ceiling, and levels with experience — all built. What is missing is a
  **job**: nothing decides what a non-worker does with its day. **Eating is no
  longer on this list** (ADR-0067): a hungry creature walks to the nearest
  hatchery of its own keeper, picks the nearest chicken once it is inside, and
  eats it for `min(HpFromChicken, missing)`. Going unfed still costs no health,
  because DK2 charges anger and now so do we. What eating still cannot do:
  a **prisoner** cannot be fed - not because it cannot move (it wanders its cell
  as of ADR-0073), but because nothing drops a bird into a cell and nothing
  races for it (Q172's remaining half).
- **What a room does.** A treasury and a dungeon heart hold gold; a portal
  produces creatures; a lair attracts them and holds a bed per tile that heals
  its owner **while asleep in it**, not whatever stands on it; a hatchery is
  researched and half built. Every other room is terrain its owner holds and
  nothing more; rooms cannot be sold (Q34) and are built instantly, because
  building over time is another worker job with the same unknown trigger.
- **What happens to gold nobody can hold.** Mined gold now reaches a treasury,
  carried there by the imp that freed it (Q31). What is missing is the other end:
  DK2 leaves a lump on the floor when there is nowhere to unload. **Gold on the
  floor is three of the 145 objects and there is now a list to put an instance
  in**, so what is missing is the mechanic rather than the record. The imp keeps
  mining, as it does in the original, and the overflow is reported as
  `gold_uncollected` and lost.
- **What mana is for.** Nothing spends it. A mana total is what a keeper earned,
  not what they would have left.
- **What happens when a dungeon heart falls.** Losing it is defeat in DK2; defeat
  is not implemented, so a keeper whose heart is beaten down here stops earning
  its 30 a second and plays on (Q40).
- **Anything about spells, jobs, carrying or combat.** Carrying is the largest
  single gap: no corpse reaches a graveyard, no gold is lifted off the floor, no
  unconscious creature is dragged to a prison or back to its bed.
