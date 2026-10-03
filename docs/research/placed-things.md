# Placed things: what a level's THINGS file puts in the world

How one campaign level's THINGS records become world contents, what each field
means, and which fields are read from the shipped data rather than transcribed
from an external reader.

Everything below is re-checkable with `dk2tools kwd things [file]` — with a file
it dumps one level's records with the fields the projection reads and the map
terrain under each, and with no argument it sweeps all 66 shipped THINGS files
and prints the distribution, the coordinate range and the ROOM cross-check.

## 1. Scope of the corpus

66 THINGS files, **6279 top-level records**:

| tag | record | count |
|---|---|---:|
| 197 | ActionPointThing | 1152 |
| 199 | GoodCreatureThing | 1025 |
| 195 | TrapThing | 959 |
| 196 | DoorThing | 940 |
| 194 | ObjectThing | 788 |
| 200 | KeeperCreatureThing | 656 |
| 204 | RoomThing | 275 |
| 198 | NeutralCreatureThing | 263 |
| 201 | HeroPartyThing | 220 |
| 203 | EffectGeneratorThing | 1 |

**No CameraThing (205) and no DeadBodyThing (202) is placed anywhere.** That is
the same absence that made the DeadBody layout a hypothesis rather than a
reading in Q221: a record type with no instances cannot be checked against the
data at all.

## 2. Coordinates are tile indices

**A THING's `X` and `Y` are bare tile indices.**

This had to be checked rather than assumed, and the assumption was tempting: the
fields are 32-bit ints, and the simulation carries positions in fixed-point
subtile units at 4096 to the tile (`Fp32`), so "the file stores what the
simulation stores" reads naturally, type-checks, and runs.

It is wrong, and the corpus settles it in one line. Across all 66 levels the
coordinates run **x 0..118, y 0..102**, against a largest shipped map of
**121x105**, and **not one of the 6279 records lands outside its own level's
grid**. Under the subtile reading every single one would — the whole corpus
would sit inside tile (0,0).

Hero-party members are excluded from that count deliberately: their `X` and `Y`
are zero in every shipped record, because a party is placed by the trigger that
spawns it at an action point rather than by coordinates in the file. (0,0) is in
range on every map, so counting them would not have failed — it would have
passed without meaning anything.

## 3. `InitialHealth` is a percentage

**`InitialHealth` on a creature or a room is a percentage of that entity's
maximum, not an absolute hit-point count.**

Of 2891 placed creatures, **2851 carry exactly 100**. The rest carry 5, 10, 15,
20, 25, 30, 45, 50, 60, 70 or 80. **Nothing anywhere carries more than 100**, and
every value is a round percentage rather than the ragged number an absolute
health field would hold.

The absolute reading is the dangerous one because it is *plausible*: it would
spawn every creature in the game at 100 health regardless of its record, which
for a Goblin looks like a slightly weak Goblin rather than like a bug, and could
have survived a long time unnoticed.

Rooms are all at 100 but two, and both of those are **dungeon hearts** — one
placed at 1% and one at 0%. A heart that starts destroyed is a scripted level
opening, not a typo.

Health is applied **after** the level, because maximum health is the record's
`Hp` scaled by the level curve. A projection that wrote health first would leave
a level-10 creature standing at a level-1 maximum, which looks like a wounded
creature rather than like an ordering bug.

There is **no floor** on the result and deliberately so. A creature that rounded
to zero would die on the tick it arrived, so clamping to 1 is the obvious move —
but the clamp could never fire: the smallest `Hp` in the shipped creature table
is **240**, the smallest placed percentage is **5**, and the level curve only
scales health up, so the worst case in the game is 12. A branch that cannot run
is a branch nobody maintains, so the guarantee is asserted in
`PlacedThingsOnRealDataTests` instead of being defended against in the code.

## 4. A ROOM record annotates the grid; it does not place a room

`RoomThing` is **not a placement**. The MAP grid already carries the room's
terrain at that tile, and `WorldState.FromMap` has already laid it down.

Checked, not assumed: **all 275 ROOM records carry the terrain id the grid holds
under them, with no exceptions.** Projecting one as a placement would build every
dungeon heart and hero gate in the campaign twice.

### The field named `RoomType` is a terrain id

The name comes from the external field list and is misleading. Only four values
occur — **14, 33, 34 and 37** — and they are the **terrain** ids of the Dungeon
Heart and the three Hero Gates:

| value | terrain of | room |
|---:|---|---|
| 14 | Dungeon Heart | 5 |
| 33 | Hero Gate [ 2x2 ] | 20 |
| 34 | Hero Gate [ FrontEnd ] | 21 |
| 37 | Hero Gate [ 3x1 ] | 23 |

Read as *room* ids instead, 14 would be the Graveyard, and 33, 34 and 37 would
not exist at all — the room table stops at 26. Every one of the four resolves
through `RoomByTerrain`, and none through a room id.

That the four are exactly the Dungeon Heart and the Hero Gates is itself the
pattern: these are the structures a level *starts with* and a player cannot
build, which is why they need a record carrying rotation and health while an
ordinary treasury does not.

`Direction` takes 0, 2, 4 and 6 across the corpus — four of eight, consistent
with a rotation in 45-degree units used only at right angles. Not yet read from
the binary, so it is not yet applied to anything.

### Owner: the record cannot express it, so the grid is it (Q223)

Five of the 275 disagree, always about the owner and always a Hero Gate — two on
Level13 (*Cherish*), two on Siege, one on Onslaught. **Swept whole, the
disagreement dissolves.** All **133** hero structures in the campaign — 86 type
33, 6 type 34, 41 type 37 — carry record player **1**, without exception, while
the grid under them takes 1, 2, 4 and 5. The 142 Dungeon Hearts carry the real
owner, 3 to 7, and never disagree.

So the ROOM record's `player` is a **constant** for a hero structure and cannot
be the per-instance owner; the grid is the only side that varies. Level13 is
also the case that breaks the old wording: its grid says **2, unclaimed**, not a
keeper, which is the normal state of a hero portal (`PlayerIds.Neutral`).

**CONFIRMED (owner, 2026-09-16)**: on *Cherish* both gates read **unclaimed, and
not destroyed**, which is what the grid says and what the record cannot say. The
second half also rules out owner 2 meaning a ruined structure. Room placement,
when it is built, takes the owner from the grid.

## 5. Hero parties are a fixed array of sixteen

A `HeroPartyThing` carries **16 member slots** and most stand empty; an empty
slot is creature id 0. Counting the empty ones is what first made the percentage
claim in §3 look false, because an empty slot's `InitialHealth` is 0.

## 6. What the projection places, and what it does not

`ThingProjection.Place` places or stores every parsed THINGS record except rooms,
which it counts as already-in-the-grid. Over the whole corpus that is **1944
creatures, 788 objects, 1152 action points, 940 doors, 959 traps, 220 hero
parties and one effect generator**, with 275 rooms recognised and zero records
left not-yet-projected.

| record | status |
|---|---|
| KeeperCreatureThing | placed, with its own `PlayerId` |
| NeutralCreatureThing | placed as `PlayerIds.Neutral` — the record carries no player field |
| GoodCreatureThing | placed as `PlayerIds.Good` — likewise |
| ObjectThing | placed, `MoneyAmount` becoming the object's gold |
| RoomThing | recognised, not placed (§4) |
| ActionPointThing | placed as hashed level state (ADR-0103) |
| DoorThing, TrapThing | verbatim ordered state without behaviour (ADR-0106) |
| HeroPartyThing | verbatim ordered templates; members are not placed creatures (ADR-0108) |
| EffectGeneratorThing | verbatim ordered state without behaviour (ADR-0108) |

The count is returned rather than logged, because a projection that silently
ignores two thirds of a file looks identical to one that works. "This level has
no doors" and "this build cannot see doors" are the same output without it.

Each authored-state collection is hashed and snapshotted (ADR-0103/0106/0108).

A placed Good creature's `Objective`, target player and action point become its standing job
at placement, the triple the loader hands `0x0048BE20` (ADR-0344; `verification-2026-09-26.md` §6).

## Summary of confidence

| claim | confidence | evidence |
|---|---|---|
| THING coordinates are tile indices | **Strongly inferred** | all 6279 records in range on their own grid; none would be under the subtile reading |
| `InitialHealth` is a percentage of maximum | **Strongly inferred** | nothing in the corpus exceeds 100; all values round percentages |
| A ROOM record duplicates the grid's terrain | **Known** (from data) | 275 of 275 agree, no exceptions |
| `RoomType` is a terrain id, not a room id | **Known** (from data) | 3 of its 4 values are not room ids at all; all 4 are room terrains |
| Neutral/Good creature side comes from the record type | **Strongly inferred** | neither record carries a player field; `PlayerIds` ids are confirmed |
| Hero-party members are 16 slots, 0 meaning empty | **Known** (from data) | 220 × 16 slots, most with creature id 0 |
| `Direction` is a rotation | **Hypothesis** | only 0/2/4/6 occur; no binary reading yet |
| Which owner wins when a ROOM record and the grid disagree | **the grid** — the record's field is constant across all 133 hero structures (§4) | Q223 |
