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
| `keeper-commands.tsv` | Only with `computer_keepers`: each command the computer keepers emitted, with its input tick |
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
random: modern                   # optional: modern (default) or dk2, the original's generator (ADR-0275)
duration_ticks: 600              # required, positive
fog_of_war: false                # optional: per-keeper discovery (ADR-0076)
viewing_player: 1                # optional: the keeper world.txt is drawn for
setup:
  placed_things: false           # optional: place the shipped level's own THINGS
  players:
    - { id: 3, gold: 20000, mana: 1000, spells: [1] }   # spells: keeper spell ids granted at the start
  rooms:                         # laid down before tick 0 — see below
    - { type: Treasury, at: [4, 4], size: [3, 3], player: 3 }
  creatures:                     # placed before tick 0 — see below
    - { type: Imp, at: [6, 6], player: 3 }
    - { type: Goblin, at: [7, 6], player: 3, health: 0, hunger: 2, level: 3 }
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
  - { at_tick: 80, player: 3, call_to_arms: [9, 9] }   # see the table below
  - { at_tick: 90, player: 3, remove_call_to_arms: true }
computer_keepers: [4]            # campaign maps only: keepers driven by their PLAYERS records
keeper_personalities: { 4: Psychotic }  # optional: a listed keeper takes a lobby personality instead
work:                            # harness-injected tile work — see below
  - { at_tick: 8, player: 3, kind: Dig, at: [5, 5] }
  - { at_tick: 12, until_tick: 25, player: 3, kind: Reinforce, at: [5, 4], repeat: 1 }
observe:
  - state_hash
  - events
expect:                          # claims the run must meet; a miss fails the run
  - event: picked_up             # a SimEvent kind; optional detail, at_tick, count, absent
    detail: "player=3"
    because: >                   # required: where the claim comes from
      A source, not a guess.
```

`observe` takes `state_hash` and `events`; anything else is reported as an
observation nothing produces. `expect` entries are checked after the run and listed in
`report.md`; any miss prints `EXPECTATION FAILED` and makes the harness exit 1.
`because` is mandatory (see `ScenarioExpectation`). `computer_keepers` runs a
`DK2.AI.KeeperAI` for each listed player after every tick and needs the install.
`keeper_personalities` gives a listed keeper one of the nine skirmish personalities
(`MasterKeeper`, `Conqueror`, `Psychotic`, `Stalwart`, `Greyman`, `Idiot`, `Guardian`,
`ThickSkinned`, `Paranoid`; `DK2.AI.KeeperAIPersonality`), remapped by the level's name as the
game remaps it; the slot needs a PLAYERS record but not its AI flag.

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
record's `Hp`, and every number including `0` means itself. It lets a run start a
creature wounded without staging a fight; `health: 0` is how a run watches one lie on
the floor for `CreatureDyingStateDurationSeconds` and then die (Q99). `level` (1 to 10)
starts it at that experience level, as a level's own THINGS records do.

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
| `call_to_arms: [x, y]` | `CastSpellAtPlaceCommand` | casts Call to Arms at the centre of that tile (ADR-0279); the player needs the spell, e.g. via `setup.players[].spells` |
| `remove_call_to_arms: true` | `RemoveCallToArmsCommand` | removes that player's flag |

### `commands` vs `work`

`commands` is real input. `work` is **not**: it puts applications straight onto the
world's work queue, bypassing the worker that would produce them — nothing has
decided that an imp wanted this job, could reach the tile, or was standing beside it.
Any run that uses `work` says exactly that in its report.

Since workers exist, `work` is the way to exercise the kinds an imp does not choose
on its own. To watch a worker dig or mine, use `setup.creatures` and a `tag` command
instead.

`kind` is a `TileWorkKind` name: `Dig`, `MineGold`, `DigOwnWall`, `DigEnemyWall`,
`Claim`, `AttackTile`, `Reinforce`, `RepairWall`, `ConvertRoom`, `AttackRoom`,
`RepairRoom`. (`RepairTile` and `FillIn` were removed: DK2 has no fill-in mechanism and
neither health variable has a reader — Q219.) `repeat` queues several applications on the same tick; `until_tick`
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
including the gold round trip: mining a shipped seam, carrying it into a heart, and
fetching a dropped pile. `scenarios/rooms/` builds a room, sells one back, and
watches a hatchery lay and hatch. `scenarios/mana/` is the run in which time alone
changes something. `scenarios/fog/` reveals part of a world through one creature's
eyes (ADR-0076). `scenarios/creatures/` is the largest set: portals, hunger, eating,
death, lairs, jobs, the hand, prisons, graveyards and the wage bill.
`scenarios/keepers/` runs computer keepers on shipped levels (`computer_keepers`),
and `scenarios/spells/` casts Call to Arms. `scenarios/triggers/` checks that placed
trigger roots run each game turn. `scenarios/smoke/` loads campaign level 1, with
and without its own THINGS, and pins determinism on a flat map. The research behind
each family is under `docs/research/`; start from the scenario's own header comment.

What a run cannot tell you:

- **Anything its own header says it does not establish.** Scenario headers name the
  mechanism exercised and what the run leaves open; read that before citing a run as
  DK2 behaviour.
- **What is unbuilt.** The current list of unbuilt or unconfirmed mechanisms is
  `docs/HANDOFF.md` and the status words in `docs/research/open-questions.md`, not
  this file. A run records the mechanisms the simulation has; a missing one
  produces no event and no warning unless the runner knows to say so.
- **Wall-clock timing of a `work` entry.** Tick numbers in a report are ordering and
  time (a game second is four ticks of 250 ms, ADR-0089), but nothing chose the job,
  so how often DK2 would have applied that work is not shown; the per-application
  amount is a game-second delta divided across the ticks of a second (Q19).
- **Playability of a synthetic map.** `flat_`, `rock_` and `chamber_` maps are testing
  conveniences, not DK2 layouts.
