# The per-room-type handler table at `0x006a1e9f`

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70, initialised `.data` at base `0x006a1e9f`, read 2026-09-07 under ADR-0046 |
| Evidence | Every record's leading type byte equals its own id, which re-confirms the base and the 41-byte stride independently of how they were found. All 26 records are present and in id order |
| Confidence | **CONFIRMED**: base, stride, the ten slots, all twelve indexing sites, and slot `+0x25` as a `JobType`. The three integers at `+0x19`, `+0x1d` and `+0x21` are **UNKNOWN** |
| Implementation | `JobRules.RoomFor` (`Confidence.Confirmed` because of this table); `PortalRules.OnOwnerChanged` for the portal's reset. ADR-0086 amendment 1, **ADR-0088** |
| Open questions | what the three integers are (Q210 (b) is closed; the integers are not) |

Opened while chasing the portal's tally reset, which is why it lives beside
`portals.md`. It is a table about **rooms**, not about portals.

DK2 addresses a record as `base + roomTypeId * 41`, guarded by
`id != 0 && id <= 26`. Each record is a type-id byte followed by ten packed
unaligned dwords: six function pointers and four small integers, three of which
have their own accessor functions. **Portal (3) and Hero Portal (25) carry
byte-identical handler sets.**

The addressing is always the same three instructions — bounds-check the type
byte at `room+0x18` against `0 < id <= 26`, `LEA ECX,[EAX + EAX*4]` for `id*5`,
then `LEA EAX,[EAX + ECX*8 + 0x6a1e9f]` for `id*41 + base` — so the record base
is computed identically everywhere and **the displacement on the next load is
the slot**. Thirteen indexing sites in twelve callers:

```
004e3993 FUN_004e3800    004e39ea FUN_004e39c0    004e3e00 FUN_004e3dd0
004e44be FUN_004e4480    004e4b23 FUN_004e4b00    004e585a FUN_004e5650
004e6cac FUN_004e6c90    004e6cec FUN_004e6cd0    004e6d2c FUN_004e6d10
004e8f48 FUN_004e8da0    004e9003 FUN_004e8da0    004e905d FUN_004e8da0
```

## The slots

| slot | caller(s) | what it is |
|---|---|---|
| `+0x01` | `FUN_004E3800` | **the room constructor** — zeroes the struct field by field, writes the type id, links the room into its owner's list |
| `+0x05` | `FUN_004E39C0` | null for portals |
| `+0x09` | `FUN_004E4480` | a tile/effect pass |
| `+0x0d` | `FUN_004E5650` | **the room ownership change** — fires the portal's reset (`portals-interval.md`) |
| `+0x11` | `FUN_004E3DD0` | null for portals |
| `+0x15` | `FUN_004E4B00` | **the per-tick handler**, `0x004DBBE0` for a portal |
| `+0x19` | `FUN_004E6C90` (accessor), `FUN_004E8DA0` | a creature state id, below |
| `+0x1d` | `FUN_004E6D10` (accessor) | a creature state id, below |
| `+0x21` | `FUN_004E6CD0` (accessor) | integer, **UNKNOWN** |
| `+0x25` | `FUN_004E8DA0` (twice) | **the job this room offers** |

`FUN_004E4B00` reads `+0x15`, calls it `thiscall` on the room, and **treats the
return as a gate** — a zero return ends the routine, a non-zero return (or a
null slot) continues into a tick stamp at `room+0x73` and further work. The
three accessors are trivial getters: bounds-check, load one integer, return it,
and return 0 when the id is out of range.

## `+0x25` is a `JobType`, and eight rooms prove it at once

`FUN_004E8DA0` loads the slot, passes it to a check taking the *creature
definition* (`creature+0x370`), and on the other path writes it into
**`creature+0x2bc`** — so it is a job the room hands to a creature after asking
the creature's record whether it may do it.

| room | `+0x25` | `JobType` |
|---|---|---|
| Hatchery (4) | 2 | `Eat` |
| Library (6) | 3 | `Research` |
| Training Room (7) | 4 | `Train` |
| Workshop (10) | 5 | `Manufacture` |
| Guard Room (9) | 6 | `Guard` |
| Torture Chamber (12) | 7 | `Torture` |
| Temple (13) | 8 | `Praying` |
| Casino (15) | 9 | `Drink` |
| Prison (11) | 22 | `Wait` |

Every one matches `JobType` as this project already had it, **including the two
that are not sequential** — `Drink` is 9 and `Wait` is 22. That is what turned
`JobRules.RoomFor` from strongly inferred into confirmed (ADR-0088). The Prison
row is a ninth entry we did not have, and it is inert: no creature's job tables
list `Wait`.

**The Lair and Combat Pit records carry no job id**, so `Sleep`, `Sulk` and
`CombatPitSpectate` are reached some other way and are neither confirmed nor
contradicted here.
**`CombatPitSpectate` answered (2026-09-25, ADR-0227):** its only caller is state 89's
fallback `0x00478680`, after the mood chooser fails (`combat-pit-spectators.md` §1). This table is **one dispatch path, not the whole map**; a
room with no job slot is not a room with no purpose (`job-selection.md`).

## The three unidentified integers, for all 26 rooms

| id | room | `+0x19` | `+0x1d` | `+0x21` | `+0x25` |
|---|---|---|---|---|---|
| 1 | Treasury | 188 | — | 5 | — |
| 2 | Lair | 79 | — | — | — |
| 3 | **Portal** | **197** | **197** | **52** | — |
| 4 | Hatchery | 92 | — | 6 | 2 |
| 5 | Dungeon Heart | 71 | 71 | 5 | — |
| 6 | Library | 53 | — | — | 3 |
| 7 | Training Room | 108 | — | — | 4 |
| 8 | Wooden Bridge | — | — | — | — |
| 9 | Guard Room | 97 | — | — | 6 |
| 10 | Workshop | 63 | — | — | 5 |
| 11 | Prison | 114 | 114 | 3 | 22 |
| 12 | Torture Chamber | 130 | 130 | — | 7 |
| 13 | Temple | 101 | 101 | 4 | 8 |
| 14 | Graveyard | — | — | — | — |
| 15 | Casino | 145 | — | — | 9 |
| 16 | Combat Pit | 137 | 137 | — | — |
| 17 | Stone Bridge | — | — | — | — |
| 18–24 | the hero gates, Mercenary Gate | — | — | — | — |
| 21 | Hero Gate Front End | — | — | — | — |
| **25** | **Hero Portal** | **197** | **197** | **52** | — |
| 26 | Crypt | 274 | — | — | — |

Two patterns, with no reading attached. **`+0x1d`, wherever it is non-zero, is
an exact copy of `+0x19`** — six rooms (Portal, Dungeon Heart, Prison, Torture
Chamber, Temple, Combat Pit, plus Hero Portal as Portal's twin). And `+0x21` is
small (3–6) everywhere except the two portals, which carry **52**.
**`+0x19` and `+0x1d` are creature state ids — CONFIRMED** (2026-09-23): both
accessors' results go straight to `SetState` (`0x0046EF7F`, `0x0046F127`,
`0x004E9035`), and the values name each room's working state (Hatchery 92
*eating*, Library 53 *researching*, Temple 101). What separates the two slots is
UNKNOWN. `+0x21`, read by `FUN_004E6CD0`, is still **UNKNOWN**.

**Rows 18–24 being empty is consistent with the shipped data** rather than a gap
in the dump: those are the hero gates and the Mercenary Gate, two of which the
data itself marks *"not used"*.
