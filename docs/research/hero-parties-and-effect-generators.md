# Hero parties and effect generators: verbatim placed state

This note records the data-side evidence needed to stop discarding the last two
parsed THINGS record types, and then — in the last section, added later — how
action type 14 raises a party, read to its last instruction on 2026-09-24.

## Result in one sentence

The 48-level campaign contains 220 hero parties, and the shipped corpus contains
one effect generator; every parsed field now enters ordered, hashed and
snapshotted world state verbatim; the spawn is read (last section) and generation remains UNKNOWN.
Across the complete 66-level shipped sweep, 35 levels contain a party.

## Record shapes

| record | fields stored | confidence |
|---|---|---|
| hero party | `Name`, `TriggerId`, `PartyId`, `Unknown_0023`, `Unknown_0027`, `Members` | **CONFIRMED from parser and shipped data** |
| party member | all `GoodCreatureThing` fields, including the two-byte `Unknown_001D` tail | **CONFIRMED from parser and tests** |
| effect generator | `X`, `Y`, four scalar unknowns, variable `EffectIds`, `Frequency`, `GeneratorId`, six-byte `Unknown_001E` | **CONFIRMED from parser and tests** |

Projection copies values without coordinate conversion, validation, flag masks
or scaling. Arrays are copied into world-owned storage so parser-side mutation
cannot alter state. List counts enter both hash and snapshot. Fixed tails are
written without lengths, so their state setters enforce the parser's two- and
six-byte widths; accepting another width would desynchronise every later field.

## Party members are templates

A party has sixteen `GoodCreatureThing` slots in shipped records. They describe
an unspawned party, not creatures standing in the level. Projection stores those
records under `HeroPartyState.Members`; it does not call `SpawnCreature`, and the
projection's creature count remains unchanged. Action type 14 is the route that
would spawn a party, but its behaviour is not implemented (Q254).

`TriggerTree` still independently builds its 24 reduced party slots from the
source `HeroPartyThing` sequence. The same records therefore have two homes for
now. Refactoring that ownership without the spawn path would be speculation, so
Q256 records it rather than this change rewiring the tree.

## Behaviour

Parties spawn (ADR-0197; `HeroPartyRules`, the last section here is its brief).
Effect generators do not generate or choose effects; their fields, cadence and
consumption are UNKNOWN (Q255, Q259). Verbatim storage of the generator loses no
information; its conservative risk is hashing a value or bit DK2 discards.

Adding both collections changes the state shape even in an empty world, so
`HashVersion` and snapshot `FormatVersion` are 47. The pin is recorded in
`hash-versions.md`.

## Corpus gate

`PlacedThingsOnRealDataTests` walks all shipped levels, compares every projected
party, member and generator field with its parsed source, asserts 220 parties,
one generator and zero `NotYetProjected`, and proves party members do not inflate
the placed-creature count.

## Action type 14, read from the binary (2026-09-10, completed 2026-09-24)

The handler is `0x00519F90(world; PartyId, ActionPointId, PartyType)`, reached
from the world action dispatcher's arm `0x00519182` with the record's bytes
`00`, `04`, `01` — our `PartyId`, `ActionPointId`, `PartyType`
(`trigger-semantics.md`). Everything below is **CONFIRMED from the
disassembly** unless marked; the shipped-data facts come from all 947 occupied
member slots of the 220 campaign parties
(`PlacedThingsOnRealDataTests.APartyMembersTrailingBytesAreItsWanderRadiusAndItsPartyId`).

### The guard, when `PartyType` is non-zero

The hero player is the first record of the player array at `world+0x84`
(stride `0x1125`, `TagId` at `+4`). Its creature list (head `+6`, link `+0x10`)
is walked for a creature whose `PartyId` (`+0x417`) is the action's, whose
prison-owner word `+0x3CB` is zero and whose `CreatureDying` bit
(`+0x3E6` bit 21) is **clear**. One such creature and the handler **returns 1
having done nothing** — the party is alive, so it is not raised again. An
earlier draft of this section had the bit's polarity backwards; `trigger-tree.md`
had it right. With `PartyType` zero (244 of 259 shipped records) the guard is
skipped.

### Where the party appears

The action point is found from `world+0x12` (link `+8`) by its `+0x37` id. The
centre tile is `((x1+x2)/2, (y1+y2)/2)` with signed halving, and the position is
that tile times 4096 — the compiler adds and subtracts `0x800`, so the point is
the tile's **corner**, not its centre. No action point: the position stays (0,0).

The tile under it (world slot `+0x1D4` = `0x00508FE0`: `tiles + (y>>12 × width
+ x>>12) × 16`, unchecked) decides a **gate flag**:

| tile's terrain (`+0x0A`) | what happens |
|---|---|
| 33, Hero Gate 2×2 | position moves (+4096, +4096) to the gate's centre point; effect `0x137` spawned there for the hero player; gate flag set |
| 37, Hero Gate 3×1 | gate flag set, position unchanged |
| anything else | gate flag clear |

Then every thing on that tile whose class dword `+0x12` is 3 or 4 is finished
through its vtable slot `+0x20` (a creature's is the health-zeroing
`0x004B2AD0`, `creature-leaving.md`) and effect 1 is spawned at the position with
the thing's `+0x28` word. Which two classes 3 and 4 are is UNKNOWN (class 2 is an
object, `win-and-defeat.md` §3.1). The party's own subtree is armed
(`0x00519A20(word[world+0x4030+PartyId×0x208], 1)`).

### One creature per occupied slot

The runtime party record is 8 bytes of header (the trigger id first) and sixteen
32-byte members at `+8`, copied from the THINGS record: member `+0x1E` is the
party's own id in every shipped slot and `+0x1D` is 0 in every shipped slot, so
`Unknown_001D` is `{WanderRadius, PartyId}` — the dumper's names for the creature
fields `+0x418` and `+0x417` they are copied to (Q258). A slot whose `CreatureId`
(`+0x1C`) is 0 is skipped. For each other slot, in this order:

1. **Type.** `PartyType` 0: the slot's `CreatureId`. Otherwise slot 0 is always
   creature **14, the Dwarf**, and every other slot draws `RNG(count) + 1`
   (`0x005558D0`, `WorldTrigger.cpp` line 1586) until a definition exists whose
   flag dword `+0x686` has bit 30, `MpdRandomInvader`. The range `count` comes
   from world slot `+0xC4` — the number of creature definitions, STRONGLY
   INFERRED from that use.
2. **Creation** through `0x00519B20(heroTagId, type, &position, &out)` →
   `0x004B78D0` → `0x004B7900`: a unique creature (flag bit 27) is given the
   owner at `def+0x5B3` instead; the owner must exist and the world must hold
   fewer than `0x1C0` = 448 creatures, else the handler **returns 0 at once**,
   keeping the members already made. **The gate step-off** (read 2026-09-26, not
   built): for a Good creature made on a room tile, `0x00519B20` first moves the
   point by room type, each time with effect `0xF`:
   - **19:** a random open neighbour, N, E, S or W (world slot `+0x208`, a bit
     mask re-rolled with `random(4)` until an open side comes up), halfway from
     the tile centre to that neighbour's centre; none open, it returns 0;
   - **20:** the tile named by the room's bytes `+0x2D`, `+0x2E`, plus one;
   - **23:** the room's centre tile (world slot `+0x1D8`).

   After creation it sets `GoodJob 19` (`0x0048BE20`), which the party loop
   overwrites below. If the point moved, it calls `0x004D0F60`. That is not a
   tile clear: it is a movement helper that six walk routines also call
   (`0x00471890`, `0x00471A70` and others), and it sets the creature's
   movement flags.
3. **King Reginald** (type 21): every player in the world's list (`world+0x7A`,
   link `+0x3A`) is sent notification type **5, variant 1** with the new
   creature's id — speech 1297 *"The King has entered your realm"*, 20 s
   (variants 2–4 are 1298–1300: *"killing your creatures"*, *"dying!"*,
   *"slain by your minions"*; `mentor-speech-and-notifications.md` §3).
4. **Level-ups to buy.** `n = (Level − 1 + bonus) mod 10` in byte arithmetic,
   where `bonus` is the party's **spawn count** `byte[world+0x70F0+PartyId]`
   (read by `0x0050FAD0`) when `PartyType` is non-zero and 0 otherwise. A random
   party therefore comes back one level higher each time it is raised, wrapping.
5. **Gold.** `0x0048AE70(creature, ftol(GoldHeld × 0.01 × MaxGoldHeld scaled to
   Level))`: the member's `GoldHeld` is a **percentage**; the setter caps at the
   creature's *current* level's maximum, and the creature is still level 1 here.
6. **Health.** `target = ftol(InitialHealth × 0.01 × Hp scaled to Level)`, applied
   as `0x004B2CE0(creature, target − current, heroTagId, 0)`; the creature arm
   clamps a positive delta at the current level's maximum, again level 1.
7. **`n` level-ups** through `0x0048B120`, each: nothing at level 10 or when the
   finished predicate holds; `ratio = (current << 12) / oldMax` (12-bit fixed),
   `new = (ratio × newMax) >> 12`, applied as a delta through `0x004B2CE0` with
   the source word unchanged; `+0x3AF` adjusted; effect `0x153` and sound
   `0x349`; a creature with `+0x3E6` bit 15 at or past `byte[0x006F2976]` calls
   `0x00476D30(1, 0)`. So a member's health percentage is applied at level 1,
   clamped, then carried up by the maximum's growth. `0x0048B120` is DK2's
   **only** level increment: its twelve callers include the experience adder
   (`0x004579D4`), so an earned level rescales health the same way (ADR-0234).
   Its mirror `0x0048AFA0` lowers the level with the same rescale (three
   callers; not built). With the constructor's 1 at `0x004875C9`, these are the
   only stores to `+0x403`.
8. **Party fields.** `+0x38 := 0` (dword, role UNKNOWN), `WanderRadius :=
   member+0x1D`, `PartyId := member+0x1E`.
9. **The job.** `0x0048BE20(job, target, value)` writes the triple at `+0x409`
   (`rebellion.md` §3): `job` is the member's `Objective`, except that a
   non-zero `PartyType` with `n == 0` forces **19, KillPlayer**; `target` is the
   `TagId` of player index `ObjectiveTargetPlayerId − 1` when that byte is
   non-zero, else 0; `value` is `ObjectiveTargetActionPointId`. Shipped
   objectives are 13, 18, 19, 22, 23 — `StealGold`, `KillCreatures`,
   `KillPlayer`, `Wait`, `SendToActionPoint` in our `JobType`.
10. **Flags.** Member `Flags` bits 0, 1, 2, 3, 5, 6, 7 go to the creature's
    `Flags` byte `+0x46F` through seven setters (`0x0048DF10`…`0x0048E030`);
    **bit 4 is not copied**, though 4 of the 9 shipped values set it. Every
    shipped member has exactly one of bit 1 (**leader**) and bit 2
    (**follower**). Member `Flags2` bit 0 → `DestroysRooms` (`+0x3EA` bit 7),
    bit 1 → `IsTool` (bit 8), bit 3 → `IsMercenary` (bit 9, mirrored at `+0xF0`),
    bit 2 → `DieInstantly` (bit 11). Shipped `Flags2` values are 0, 1, 4, 5.
11. **Trigger.** `0x0049A950(creature, TriggerId)` stores `TriggerRoot` and the
    record's repeat count at `+0x4F9`; a non-zero id arms that member's subtree;
    creature event **3** is fired (`0x0049AA30(creature, 3, 0, 0)`).
12. **From a gate** the creature enters state **269**, a second idle
    (`0x004626A0`, `creature-state-table.md`), and bit 0 of `+0xF3` is cleared.

### After the loop

The spawn count `byte[world+0x70F0+PartyId]` is incremented. The hero player's
creatures with this `PartyId` are scanned for the **slowest** (`min def+0x5B4`)
and the **leader** (`Flags` bit 1, the last one found). From a gate, effect
`0x2B` is spawned at the leader's position with z `0x2000`. Then every member's
speed mode is set with `0x004887D0(0, slowest)` — the party walks at its slowest
member's pace — and each **follower** (bit 2) gets `MyLeadersId := leader` and
`PositionInParty := ++k`; finally the leader's `PositionInParty := k`, the
follower count. (No leader at all and that last write lands on address `0x3E1`;
no shipped party lacks one.) The handler returns 1.

### What this settles and what it opens

Q254 is answered above. Q257: the runtime record keeps only an 8-byte header of
the 43-byte file header and the handler reads the trigger id alone, so
`Unknown_0023`/`Unknown_0027` are not consumed at spawn. Q258 is answered. Left
UNKNOWN: thing classes 3 and 4; `+0x38`; what `0x004D0F60` does to the moved
creature beyond its movement flags; where `PositionInParty` and `MyLeadersId` are read
(`travel-states.md` §2.3 has one reader).

### Built (2026-09-24, ADR-0197)

`HeroPartyRules.Spawn` follows the list above; `CreatureLevelRules.LevelUp` is step 7's
arithmetic. Deviations, each named at its site: the creature stands at the tile's centre
(DK2: its corner); things on the spawn tile are not cleared; no party pace, event 3, gate
state 269, effects or King notification. The fields the spawn writes are hashed on the
creature under the dumper's names, and `SpawnCount` on the party.
