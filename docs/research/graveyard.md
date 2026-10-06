# Mechanic: the Graveyard — where a Vampire comes back, and the tally that makes one

Source(s):
- Owner observation, 2026-09-07 (Q113)
- The reference install's `Rooms.kwd`, `Objects.kwd` and `Creatures.kwd`, via `DK2.Data`
- `DKII.EXE` v1.70: the Graveyard's handlers, burial, capacity gate and Vampire
  routine, read 2026-10-04 (Q318–Q321, Q329)

Original version: DK2 v1.70

Confidence: **CONFIRMED (owner + data)** for §1's destination room, **CONFIRMED
(binary)** for §2 except where a line says otherwise.

Split from `creature-death.md` (§2.2, §2.5) on 2026-10-05; the vampire's own
reincarnation routine and its guards stay there (§2.3).

## 1. Where a vampire comes back (Q113)

The owner's first wording was *"they reincarnate at their sarcophagus"*, which
this project read as the vampire's own bed, because its `LairObjectId` had been
pinned as a coffin. The owner retracted it the same day: *"sarchopagus at the
graveyard, I made a wrong claim earlier."*

Two shipped facts back the correction:

- The **Graveyard's `CreatedCreatureId` is 8 — the Vampire.** Only two rooms in
  the table create a creature at all; the other is the Prison, whose 9 is the
  Skeleton.
- The **Graveyard's object ids are 31–34**; the Vampire's `LairObjectId` is
  **26**. Two different objects.

The executable settles the vocabulary: `Object.cpp`'s union names `Lair` and
`TombStone` among its object kinds, and the words *sarcophagus* and *coffin*
appear nowhere in its strings. A bed is a `Lair` object whatever art it wears.
The vampire's coffin-shaped bed and the graveyard's sarcophagus are two objects
that look alike, and reading one as the other was a pun rather than a finding.

So reincarnation puts the creature in a **Graveyard**, which needs a Graveyard
under the keeper's control. The DK2 wiki also gives that as a third
permanent-death condition. The binary agrees (2026-10-04, `creature-death.md` §2.3, Q329). The
destination is the `CentrePos` (`+0x27/+0x29`) of a Graveyard that
`Player_RandomRoomOfType(14)` (`0x004C6460`) draws at random, moved there as a
tile centre (×0x1000 + 0x800) through `0x004B5560`. It is not a tombstone or any
other object.

## 2. The Graveyard tally and its Vampire

Binary, 2026-10-04 (Q318–Q321). The Graveyard is room type 14. In the handler table at `0x006A1E9F` its
constructor is `0x004DD3A0` and its per-tick handler `0x004DD3F0`. The per-tick
handler is an effect countdown on byte `+0x4D`, reloaded from `0x006F2590`.
The tally lives in the room's dword `+0x49`, which the room dumper names
`DecomposeValue`.

- **A burial adds.** The burial routine `0x004DD470` is called from
  `0x004B1070` at `0x004B1139`.
  - The value it adds is the corpse's `DecomposeValue` (creature definition
    `+0x6BF`, reached through corpse `+0x129`).
  - Above level 1 (corpse byte `+0x12D`) the value is scaled by the
    level-percentage word at `[0x006E4154] + 0x225 + 2·level`, divided by 100,
    and capped at 0xFFFE.
  - The add comes before any test.
- **The gate** `0x004DDCA0` runs on the owner.
  - It sums `TotalCapacity` (`+0x67`) over all the owner's Graveyards.
  - It counts the owner's creatures that have `CanBeResurrected` (definition
    `+0x686` bit 23) and whose vtable `+0x28` test returns 0. Reading that test
    as "not dead" is STRONGLY INFERRED.
  - It passes only while that count is below the capacity sum. Otherwise it
    posts message 0x2A, if the owner has any Graveyard at all, and fails.
- **The threshold zeroes; it does not subtract** (Q319). The threshold is
  global `0x006F25AC`, variable 25 `DecomposeValueNeededForVampire`, 1000
  (written by `Variables_SetMisc` at `0x0051CE18`).
  - When the gate passes and the tally is at least the threshold (unsigned
    compare at `0x004DD515`), the routine stores **0** at `0x004DD519` and
    calls `0x004DD820` once.
  - Any excess is lost.
  - There is no loop, so one burial makes at most one Vampire, however large
    the tally (Q320).
  - While the gate fails, burials keep adding and nothing caps the tally. The
    first burial after the gate passes again makes one Vampire and zeroes it.
- **Where the Vampire appears** (Q318). `0x004DD820` works as follows.
  - It returns when the RoomDef's `CreatedCreatureId` (`+0x44F`) is 0.
  - It counts the objects of type 75 on the room's `AutoPlacedObjectList`
    (`+0x0E`, next link `+0x12A`, type byte `+0x12C`). The shipped
    `Objects.kwd` names object 75 **Graveyard Crypt**: its record's own id
    byte reads 75.
  - If it finds any, it draws `random(count)` on world `+0x15A0`. Its selection
    loop (`0x004DD8A5`–`0x004DD8C9`) has no break: once the counter reaches 0,
    every later crypt overwrites the pick. The result is therefore always the
    **last** crypt on the list, whatever the draw, but the draw still advances
    the seed.
  - The Vampire is created at that object's position (`+0x1A..+0x22`) for the
    room's owner (`0x004B78D0`). With no crypt it is created at
    `Room_RandomTilePoint(room, &pt, 1, 0)` (`0x004E4D90`).
  - Then come effect 0x138 at the creature, state 198 (0xC6) and message 0x48.
- **The tally does not outlive its room** (Q321).
  - The Graveyard's constructor writes only `+0x4D` and `+0xA3`. `Room_Construct`
    has already zeroed `+0x49` for every new room.
  - Selling any tile frees the room and rebuilds what is left as new rooms,
    copying state only for types 8, 11 and 15 (`room-object.md`). So any sale,
    partial or whole, loses the tally, and a rebuilt Graveyard starts at 0.
  - In a merge the surviving room keeps its tally and the absorbed room's is
    dropped.

All of the above is CONFIRMED (instruction level) except where a line says
otherwise. Built as ADR-0370 (`GraveyardRules`): the gate's capacity is the room's floor slots
(`Room_ComputeTotalCapacity` `0x004E3ED0`, type 14), the Vampire stands on a random tile since
no level places a crypt, and effect 0x138, state 198 and message 72 are not modelled.
