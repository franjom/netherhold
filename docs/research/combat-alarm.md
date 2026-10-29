# Mechanic: the NonFighter arm and the alarm — a worker's reluctance, and the room that answers

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23. Addresses are
  **facts about the binary in our own words**; no decompiler output enters this
  repository (ADR-0046). State names are the executable's own `Text.str` labels
  (creature-state-table.md); field names its creature dumper's (creature-object.md).

Original version: DK2 v1.70 (GOG)

Questions: answers Q374 and Q390; opens Q391; corrects combat-target-selection.md §3.2,
which read the alarm's call as a commit to a fight. `combat-fear.md` owns the
head-count and the withdraw state this shares; `combat-target-selection.md` §3.1 the
near-the-heart path, restated in §1 only as far as the build needs.

**What this settles:** the Imp's arm **never fights a hero**. Away from its Guard Rooms it
withdraws; near one it counts heads, and when its side is outnumbered a `Scout`
runs to the nearest Guard Room and **raises the alarm**: that room's creatures are sent
running to where the hero stood, until their summed `Threat` covers the deficit,
and stand there thirty ticks looking for a fight the ordinary way. Only within
about three and a half tiles of its own heart does a worker fight, and then
anything hostile within a tile.

---

## 1. The NonFighter arm (`0x0045BDF0`), as built

**Setup.** `nearHeart` = my distance² to my owner's `DungeonHeartPosition` (tile
centre) `< 0xD000`, 3.6 tiles (§3.1 of the selection doc). Sight² and hearing² as
every arm, except an Imp's sight is forced to `0x1000` (one tile²) and its hearing
to nothing. My side is the Turncoat player while `TurncoatTimer` runs.

**Per hostile candidate**, over every player's creatures:

1. `nearHeart`, **or** the candidate's base creature type id read by `0x00494360`
   equal to 1 — an Imp candidate (§6) — takes the **shared template**: distance with the alignment
   discount (7/8), `IsAttackable`, not my `+0x4A3`, seen within sight or reached
   within hearing, lowest wins. The winner goes to the acquirer's common tail (route,
   the `d/3` gate, the fight-starter) and **the worker fights**.
2. Otherwise the candidate must be **owned by the Good player**; anyone else is
   skipped. Then the **hero path**: `d < sight²` and a line of sight (no hearing),
   `d < best`, the hero has no target or targets me, `IsAttackable`. Then:
   - my `Attacking` is set to the hero, tentatively, and the old tag stashed;
   - **hero not within `GuardRoomReportingDistanceTiles` of a Guard Room of mine**
     (`0x00460B50`, combat-fear.md §4 step 9) → the withdraw state; success returns
     `0xEA60`, "I left", which the acquirer returns as 1; failure restores the tag
     and the scan continues;
   - **near a Guard Room:** `theirs` = the hero's owner's strength within five tiles of
     me, `ours` = my owner's × 3/4 (`0x004FAC40`, summed level-scaled `Threat`).
     `ours > theirs` → restore the tag, **keep scanning** — nothing happens;
   - else `deficit = (theirs − ours) × 3/2` and **the alarm** (§2); failing that
     the withdraw state; failing that restore and continue.

So on the hero path `best` is never lowered and no tag is ever kept: the arm's
three `0xEA60` exits are the disengagement the earlier reading called a commit.

## 2. Raising the alarm (`0x004752B0(state, &threatPos, deficit)`)

Refused unless my definition's `JobClass` is **3, `Scout`** — the Imp's — and my
owner's `CallToArmsOn` (`player+0x76` bit 2) is clear. Refused when **someone is
already responding** (`0x004754D0`): any creature of my owner in state 6 heading
for 251 or 254, or in 251 or 254, whose destination (`0x004755C0`: the state's
`+0xE3` for state 6, else its position) lies within `0x19000` — five tiles² — of
the threat. Then the **nearest Guard Room of mine** to the threat by distance² to its
`+0x27/+0x29` tile: the walk skips any room whose type byte `+0x18` is not 9
(`0x00475336`, CONFIRMED; the same filter as `0x00460B50`, ADR-0473), and with none it
raises nothing (ADR-0517). `0x004E4D90(room, &out, 0,
0)` gives a position in it: one random slab-chain step count, then advance
and wrap past floor slots (`Room_IsFloorSlot`, `0x00454110`). Portal, Heart
and bridge tiles are never floor slots, so even a blocked centre can be drawn.
Binary recheck 2026-10-11 confirms there is one point and no retry with another
room after route failure. Enter 199 if not already in the combat group;
route there with `0x00471890(&pos, 251, flags)`, `flags` `0x80` for an Imp (`+0x36F == 1`) and 0
otherwise (`0x0047542A`; ours since ADR-0332) — and stash the
threat's position at `state+0xA7/+0xAB`, the deficit at `+0xAF` and the room's tag
at `+0xB1`; **run speed**; the definition's `+0x6E3` sound. Return 1.

## 3. Arriving: state 251, "responding to a threat" (`0x00475660`)

1. If any creature of my owner **eight or more tiles from me on either axis** is in
   state 252 → return 0 (idle): another alarm has just been raised elsewhere.
2. **Dispatch**, over my owner's creatures in list order, while the deficit is not
   yet negative. A candidate is skipped when: `IsWorker` or `IsHorny`; under Call to
   Arms (`0x004888C0`: owner's `CallToArmsOn`, the creature's `+0x3E6` bit 20, or
   in states 190–192); in the combat group; the gate `0x0045A6D0(0)` refuses; its
   `RoomId` (`+0x3B9`) is not the alarm's room and it is not in state 6 heading for
   a job there (`state+0x68 == 6`, `+0x6C` the room); its health is not above a
   **quarter** of its level-scaled `Hp`; it is **angry**, `state+0x3C == 2` (the mood
   band, creature `+0x28A` of Q269: `Anger_Add` `0x00487030` writes it through the
   state's anger block at `+0x20`, `0x00478BA8`; the state object is creature `+0x24E`,
   `0x00520AE2`); its
   state's group tag (record `+0x16`, `0x00475813`) is 7 (lair, sleep, eating, payday) or
   3 (held, slapped, dying and others; `creature-state-table.md`); or it is already en
   route to 254. Otherwise it is routed to
   the threat's position with `0x00471890(&pos, 254, flags)`, the raiser's rule (`0x0047583F`), given **the
   raiser's run speed** (`def+0x5B8` of the Scout, not its own — a slip), its
   `+0x2CC` zeroed, and the deficit reduced by its level-scaled `Threat`.
3. **Stand down.** The `+0x6E3` sound; `SetState(1)` then `SetState(252)`; a
   worker starts action 79 (`0x004969D0(0x4F, 1, 0, 0, 0)`) — the alarm animation.
   State 252's handler (`0x00458680`) returns 1 only while an action is current, so
   a non-worker is idle in the 251 turn and a worker when the action ends. Action 79's record
   (`0x006E4A8B`, filled in `0x004950C0`) has no handler, animation block 29 and mode 3,
   so `CreatureAction_Run` `0x00496AA0` ends it when the animation stops, as action 55
   (`combat-approach-and-victory.md` §7); one play of slot 29 (count 1) is 4 turns for the Imp, as its captures hold 252 (ADR-0580); what the clip shows is unread (Q391).

## 4. Answering: state 6 to state 254, "reporting danger" (`0x004759A0`)

State 6 (`0x00470C80`) walks the route and, when the path is finished
(`0x004D5B20 == 0x10000`), enters the arrival state at `state+0xDF`; on the way the
fight gate allows a look (only 202 and 251 are refused), so a responder acquires
the hero as soon as it sees or hears it. Arrived, state 254 counts `state+0x7E` up
each tick and **returns 1 for thirty ticks**, then sets the walk speed and returns
0: idle. Standing there it is outside the combat group, so `ProcessFight` runs
every tenth tick and the ordinary acquisition takes over.

## 5. Built and not built (ADR-0166)

**Group tags** (2026-10-09): §3 called group 3 "the worker jobs" and 7 "idle, eating,
joining, leaving, stunned, burning"; those are categories 3 and 7. The test reads the group
tag, and worker jobs are group 8. Ours keeps the room-job match, which no group-3 or group-7
state passes.

**On state ids** (ADR-0208): the raiser enters 199 unless already in the combat group and
walks in state 6 resuming 251; arriving, it sets 1 and 252, which a worker holds for action 79 and anyone else leaves at once; a
responder walks from its job state in 6 resuming 254, then stands thirty ticks and goes
to state 1.

**Built** (`CombatTargetRules.NonFighter`, `FearRules.RaiseAlarm`,
`RespondToThreat`, `RaisingAlarm`, `Reporting`): §1 with the Imp escape (§6, built
2026-09-23: an enemy Imp is judged by the shared template anywhere); §2 with the
room's bounding-box centre for nearest-room scoring, then the random slab-chain
point for the route; §3 steps 2–3 with
the room match as a room job whose tile lies in the alarm's room, the group-tag
exclusions as the room-job activities, and the raiser's-speed slip replaced by the
responder's own run speed; §4 with `CreatureActivity.Reporting` covering both the
walk and the thirty ticks.

**Built at hash 330** (ADR-0580): 251's handler runs the turn after the arrival, and
a worker is held in 252 for action 79, one play of art slot 29 (`CountedHoldTicks`: 4 turns
for the Imp; DK2 holds an Imp four ticks, 432 of 432); anyone else idles in the 251 turn. A
responder's 254 counts from the turn after the arrival.

**Not built, named:** step 1's eight-tile test; the Turncoat side; the angry skip (`state+0x3C`); the `+0x6E3` sounds;
Jack, the other NonFighter.

The Call to Arms alarm gate is built with ADR-0425; it reads the owner's `On`
bit, without requiring a surviving flag object.

## 6. The escape byte

`0x00494360(def)` returns a **base creature type id**: `def+0x5B3` when
`IsUnique` is set, otherwise `def+0x770`. The raw-record reader `0x00568E30`
and definition builder `0x00492320` establish the complete mapping directly:

| file read | staging store | definition store | meaning |
|---:|---:|---:|---|
| `record+0xD3B` | `+0x112B` | `def+0x5B3` | unique definition's ordinary archetype |
| `record+0xF4B` | `+0x133B` | `def+0x770` | definition's own `CreatureId` |

The builder stores those staging bytes at `0x00492391` and `0x00492380`; the
record reader fills them at `0x00568E5A` and `0x00568E4B`. Thus the earlier
`0xCE2`/`0xE9F` adjacency guess was wrong. The
`creatures_struct.bt` transcription in `kwd-creatures.md` names neither
`0xD3B` nor this relationship; `0xF4B` was already named `CreatureId`.

### Readers

`ReadsAt.py` found seven instructions for each byte after excluding three
`+0x770` false positives whose base is not a definition. The initialiser
(`0x00491DC0`), loader and copier
(`0x0051F3B0`) only move the bytes. The four other direct readers agree with
the base-id interpretation:

- `0x004B4C30` updates a player's per-archetype creature population, using the
  instance type for an ordinary creature and `def+0x5B3` for a unique one.
- `0x004B7900` resolves a requested definition to the ordinary archetype before
  its creation/availability check, substituting `def+0x5B3` only for a unique.
- `0x00488520` serialises the definition's own `def+0x770` id before the
  creature's fight, action and other state blocks.
- `0x004A3FC0` passes the definition's own `def+0x770` id as the creature type
  during an action-event branch.

The apparent `+0x770` reads at `0x004EA986`, `0x0050E37D` and `0x0050E3D8`
use a stack or indexed unrelated base and are not creature-definition readers.

`CallScanAll.py` found every call or tail-jump to the resolver:

- `0x00418080` (`0x004181CD`) selects a live creature matching a requested base
  type; `0x004185E0` (`0x0041878A`) groups creature statistics by base type.
- NonFighter `0x0045BDF0` (`0x0045BF59`) tests the candidate's result against
  1. This is both the escape to the shared target template and, consequently,
  the escape from the Good-owner filter: the candidate is an Imp.
- The shot processor `0x004AD7A0` (`0x004ADA16`) special-cases base type 23,
  Dark Angel, when shot kind 9 creates its collision effect.
- Definition-table accessors `0x0050D220`, `0x0050D240`, `0x0050D2E0`,
  `0x0050D390`, `0x0050D420`, `0x0050D460` and `0x0050D4D0` use the result to
  return the first id, step forward/backward with wraparound, find by id, get an
  index's id, map another definition byte to an id, or test whether an id exists.
  Their call sites are `0x0050D230`, `0x0050D260`, `0x0050D2AA`, `0x0050D2BC`,
  `0x0050D304`, `0x0050D350`, `0x0050D37B`, `0x0050D3D0`, `0x0050D448`,
  `0x0050D4AF` and `0x0050D510`.
- The definition inserter `0x0051F240` (`0x0051F308`) orders the definition
  table by the resolved base id before copying entries upward.

### Shipped roster

The table is the raw `Creatures.kwd` pair, not the resolver's output. `0xD3B`
is zero for every ordinary definition; every unique carries its ordinary
archetype's id. `0xF4B` is the contiguous definition id.

| creature | `0xD3B` | `0xF4B` |
|---|---:|---:|
| Imp | 0 | 1 |
| Prince Tristran | 0 | 2 |
| Bile Demon | 0 | 3 |
| Mistress | 0 | 4 |
| Warlock | 0 | 5 |
| Dark Elf | 0 | 6 |
| Goblin | 0 | 7 |
| Vampire | 0 | 8 |
| Skeleton | 0 | 9 |
| Troll | 0 | 10 |
| Salamander | 0 | 11 |
| Firefly | 0 | 12 |
| Knight | 0 | 13 |
| Dwarf | 0 | 14 |
| Giant | 0 | 15 |
| Wizard | 0 | 16 |
| Elven Archer | 0 | 17 |
| Thief | 0 | 18 |
| Monk | 0 | 19 |
| Fairy | 0 | 20 |
| King Reginald | 0 | 21 |
| Black Knight | 0 | 22 |
| Dark Angel | 0 | 23 |
| Rogue | 0 | 24 |
| Guard | 0 | 25 |
| Prince Felix | 0 | 26 |
| Horny | 0 | 27 |
| Stone Knight | 0 | 28 |
| Lord Of The Land | 0 | 29 |
| Royal Guard | 0 | 30 |
| Prince Balder | 0 | 31 |
| Unique Firefly | 12 | 32 |
| Unique Goblin | 7 | 33 |
| Unique Warlock | 5 | 34 |
| Unique Troll | 10 | 35 |
| Unique Dark Elf | 6 | 36 |
| Unique Skeleton | 9 | 37 |
| Unique Mistress | 4 | 38 |
| Unique Salamander | 11 | 39 |
| Unique Rogue | 24 | 40 |
| Unique Bile Demon | 3 | 41 |
| Unique Vampire | 8 | 42 |
| Unique Black Knight | 22 | 43 |
| Unique Dark Angel | 23 | 44 |
| Maiden Of The Nest | 0 | 45 |
| Jack | 0 | 46 |

No shipped unique maps to 1. Therefore the resolver returns 1 only for the Imp,
and the NonFighter escape is an **Imp-candidate test**. The parsed raw field is
named `UniqueBaseCreatureId`; the resolver's concept is the base creature type
id.

**Confidence: CONFIRMED (binary and shipped data).** The loader fixes both file
offsets, all readers use the result as a creature type id, and all 13 unique
records map exactly to their ordinary archetypes.

## 7. The heart's dispatch (`0x004D9690`, 2026-10-09)

The Dungeon Heart's room update ends in a third sender to 254 (`0x004D96F0`-`0x004D9A1A`). When
`(turn + room+4) & 3` is zero, unless the room is ruined: over the players in list order that
`Player_IsEnemy` (`0x004BF390`) names enemies of the heart's owner, and their creatures in list
order, the first whose distance² to the heart's position word (`+0x27`, tile centre) is below
`DungeonHeartReportingDistanceTiles`² (`0x006F2964`, 4) and whose `IsAttackable` bit is set gives
the threat's position. Then every creature of the owner within
`DungeonHeartCreatureGatheringDistanceTiles`² (`0x006F2973`, 7) of the heart is sent to it, unless
Horny (`def+0x686` bit 14), under Call to Arms (`0x004888C0`), in the Combat category, refused by
`0x0045A6D0(0)`, at or below a quarter of its level-scaled health, in group tag 3 (`0x004D9976`),
dropped to a job (`+0x2BC`) or already walking to 254 (state 6, resume `0xFE`). A route
(`Creature_CanRouteTo`, `0x80` for an Imp) is required; a willing torturee is kicked to 135 first;
`Travel_RouteToPoint(&pos, 254, flags)`, its own run speed (`def+0x5B8`), `+0x2CC` zeroed. No worker
is refused and no deficit is counted: every eligible creature goes.

**Seen in DK2** (Level 11A, packet `m12l11a`, seeds 1, 3, 5, 7 and 10): only Morgana's heart
sends, always on capture rows 1 mod 4, game turns 0 mod 4 (a row is the turn plus one, ADR-0119):
her heart's room id (`+0x04`) is 1100 in every seed. The other hearts are 1097 (Drako), 1101
(Belial), 1105 (player 3) and 1107 (Raksha). In seed 5 Drako's imp digs (64,38) through at 63 from
(64,39), about four tiles from her heart; at 65 her three imps nearby switch to state 6 for
(64,39) resuming 254 and fight it in the hole in 199 until about 120. Drako has a creature in
the Combat category on 348-403 of the first 500 ticks in the four seeds whose wall falls before
100 (3, 5, 7, 10), and on 101 in seed 1.

**Built** (ADR-0407): `HeartDefenceSystem`, after the store sweep. Deviates: ours has no room ids,
so every heart takes Morgana's phase (1100); the ruin gate reads the player's heart, not the
room's `+0x9A`; the torturee's kick and `+0x2CC` are not modelled; the job is let go as the
alarm's responders' is (ours; DK2's `0x00471890` is not read for it).

### Level7 owner report (2026-10-11)

The modern seed-1 idle probe first hit player 3's Heart at tick1096 from
(5.77,12.44), outside the four-tile radius around room position (6.5,16.5);
the first rally was1131. Combat targeted a phantom outer-ring tile rather
than an Arch Pillar. Exact pillar targets now use furniture-placement.md §6.
The final228 run first rallies at495, before the first hit at519 from
(7.12,15.13), inside the radius. At519 the working Imps are outside the
seven-tile gathering radius. Both tested
working states interrupt when an eligible enemy enters the radius
(`HeartDefenceTests`). These are modern measurements, not original timing
parity; the owner's differing rally behavior remains open in Q586.

## Confidence

**CONFIRMED (binary):** §1's order, the Good-owner filter, the hero path's three
exits and their `0xEA60` return; §2's conditions, the five-tile check and the
state fields; §3's every skip, the quarter-health test, the raiser's run speed,
the deficit countdown, the two `SetState`s and action 79; §4's thirty ticks and
the walk speed; state 6's arrival.

**CONFIRMED (binary, 2026-10-14):** the 9 that `0x00460B50` compares at `0x00460BC8` is the
room type byte `+0x18` (the Guard Room), not a thing class: `0x004C61C0` walks the same list
and compares that byte with the requested room type (3 and 25 for portals).

**STRONGLY INFERRED:** that `RoomId` is the room
a creature works in.

**UNKNOWN:** Q391 (what a worker's animation block 29 shows; its length for the Imp, 4 turns, is STRONGLY INFERRED from the clip clock and the captures).
