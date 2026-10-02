# Doors and traps: authored projection and runtime construction

This note is the canonical evidence for placed doors and traps. Addresses refer
to DKII.EXE v1.70. Binary findings are **STRONGLY INFERRED**; only corpus facts
read from shipped data are **CONFIRMED**.

## Result

The THINGS loaders use one door flag as an initial lock request, another as an
initial construction marker, and the trap field named `NumberOfShots` only as a
Boolean initial-construction marker. The runtime later maintains construction,
open, lock, disarm, recharge and firing state independently of the authored
records. Construction, door opening and navigation costs are built. Physical
door contact and damage are researched and built in [door-blocking.md](door-blocking.md);
trap activation and its implementation limits are in [traps.md](traps.md).

## Shipped records

`PlacedThingsOnRealDataTests` walks every shipped THINGS file and compares each
projected state with its source.

| record | authored fields retained | corpus finding | confidence |
|---|---|---|---|
| door | `X`, `Y`, `Unknown_0008`, `TriggerId`, `DoorId`, `PlayerId`, `Flags`, three bytes at `Unknown_0011` | 940 records; every `TriggerId` is zero; 3 have flag bit 1 set | **CONFIRMED from shipped data** |
| trap | `X`, `Y`, `Unknown_0008`, `NumberOfShots`, `TrapId`, `PlayerId`, `Unknown_000F` | 959 records; none has `NumberOfShots == 0` | **CONFIRMED from shipped data** |

The records occur on 48 of the 66 levels with THINGS files. Their file order and
all authored bytes remain hashed and snapshotted as decided by ADR-0106. The
runtime construction predicate is derived state and is also hashed and saved.

## Class boundaries and persistence

RTTI confines the primary door and trap vtables to `0x0066D6EC` and
`0x0066D754`. This was used to audit each field within its class rather than
assigning meaning from unrelated things that share an offset.

Door save at `0x004A54D0` writes the common thing state and the contiguous door
range `+0x101..+0x11D`; load at `0x004A5560` restores it and refreshes derived
presentation. Trap save at `0x004A7BD0` and load at `0x004A7C60` likewise retain
`+0xFF..+0x11E`. Construction, door open/lock state, trap recharge and the other
runtime counters in those ranges therefore survive a DK2 save.

## Door loader and fields

The authored loader at `0x00520F40` copies X, Y, type and owner. It interprets
exactly two bits of `DoorThing.Flags`:

- bit 1 seeds runtime `+0x109`; nonzero means **UnderConstruction**;
- bit 0 requests lock mode 3 through the door lock transition.

No higher flag bit is read by this loader. The authored `Unknown_0008` and the
three distinct bytes at `Unknown_0011` are included in the raw-record checksum,
but are not copied into the door instance or its saved runtime range. Their
meaning remains **UNKNOWN**; the established narrower result is that DK2's
runtime door does not preserve them.

Door initialization at `0x004A5070` starts `+0x109` nonzero and clears the open,
lock and lock-pick timer fields. The loader then applies the authored flags.
The placement-completion path at `0x00467460` clears `+0x109` through the same
setter used by the loader. The setter refreshes both navigation and visual
state. Together with the debug-state labels in the binary, these accesses make
**UnderConstruction** STRONGLY INFERRED rather than a name inferred merely from
condition 27's zero/nonzero partition.

## Door runtime behavior

The ordinary update at `0x004A55F0` separates construction from completed-door
behavior. While construction is nonzero it refreshes the construction
presentation and bypasses the normal open/close work. Barricades return before
the opening decision, including its picked-lock arm. An ordinary completed door's
**open** state (`+0x10D`) is recomputed every update, in this order
(`0x004A5D52`): a **running lock-pick timer** (`+0x115`) forces it open ahead
of everything; failing that **lock mode 3** (`+0x111`) shuts it; otherwise it
is open while an **eligible** creature stands on one of three tiles: the door's
own, and the two beside it along stored orientation (`+0x119`): zero selects
`(x−1, y)` and `(x+1, y)`, otherwise `(x, y−1)` and `(x, y+1)`. The world
predicate `+0x208` checks supporting walls; it does not reorient these probes
(`0x004A5A4B`–`0x004A5B02`). The three chains are
walked in turn and the first eligible creature ends the search. Eligible:
allied with the owner (`0x004BF3D0`), not the neutral player (`[0x006F2548]`),
not `CreatureDying` (creature flag `+0x3E6` bit 21, named by the debug dumper),
and by lock mode — 0 any, 1 no worker (definition flag bit 0), 2 workers only.

**The read lock writers use modes 0 and 3.** Direct callers of transition
`0x004A6800` include the loader (`0x00521130`, mode 3 from authored flag bit 0), the
player's lock command handler, and an effect handler `0x00474F80` registered
in a descriptor table at `0x0047F972` that, over the four neighbours of an
entity's tile, destroys traps, unlocks the entity owner's locked doors (mode 0)
and hands foreign doors to `0x004A6DF0`. Which ability owns that descriptor is
UNKNOWN.

**The lock command** `0x00513800` reads a thing id from the packet and refuses
unless the thing is live in `[0x006ED540]`, its type word `+0x12` is 4, and its
owner `+0x28` equals the issuing player. It takes **no mode**: `0x0051382D`
computes `3` from a zero lock mode and `0` from any other, so the command is a
toggle; this path does not request modes 1 or 2.

**The lock-pick timer.** `+0x115` is a plain tick countdown: the update
decrements it by one (`0x004A56A1`) above both the construction bypass and the
open decision, and a nonzero value sets the open accumulator without looking at
anything else. The read writer is `0x004A6DF0`, four instructions that store
`[0x006F28D3] * [0x006F2590]` — a duration times the tick rate, the shape every
timer here has. Both globals live in uninitialised data, so the duration is
**UNKNOWN**; the effect handler above calls it.

**Definition flags.** Record offset `0x228` is the flags byte, runtime
`def+0x8B` (the offset delta is STRONGLY INFERRED from the bits' owners): the
Secret Door alone carries bit 5, Barricade and Hero Barricade bit 6, Hero
Barricade and Hero Portcullis bit 7. `DoorFlags` names them.

**Alliances.** `player+0x1AE` is **eight bytes**, one per player, indexed by
the player's compact number at `+0x10FC` — `+0x1B6` is the next array, which is
what bounds it. The predicate `0x004BF3D0(otherPlayerId)` returns 1 when the id
is the player's own (`player+4`) and otherwise the byte; `0x004BF390` beside it
is its negation, an is-enemy test. Both handlers write **both** players' arrays
(`0x00513FF0` make, `0x00513F60` break; make also clears the `+0x1B6` pair), so
a one-sided alliance is unreachable, and neither checks that the issuer is one
of the pair. `0x0051DCEB` writes it from a table too, STRONGLY INFERRED to be
trigger action 18's `Available`. What `+0x1B6` holds is UNKNOWN.

**Navigation.** The writer `0x004A66F0` sets the tile's byte to
`owner << 4 | 4 if lock mode ≠ 0 or definition bit 6 (a barricade) | 2 if the
tile's flag bit 0` and, when told the door is open or under construction,
writes plain floor instead; an open door is floor for everyone. The lock
transition `0x004A6800` stores the mode and toggles bit 2 in place. How the
cost function reads the byte is navigation-mesh-and-search.md §4.

Built (ADR-0158 and ADR-0159; `DoorSystem`, `AllianceSystem`, `WorldSteering`,
`MeshRouter.TransitionCost`): open state and its order, the three tiles, lock
mode from the authored flag and from the owner's toggling command, the
alliance array and its symmetric writes, the lock-pick countdown, the byte with
the barricade arm, and the cost arms. ADR-0250 corrects the opening bypass and
stored-orientation probes. Unbuilt: the effect handler that starts a
picked lock — no ability dispatches one here — and trigger action 18.

## Trap loader and fields

Trap initialization at `0x004A7940` sets runtime `+0x10C` bit 5 (`0x20`) and
clears the firing timers and counters. The authored loader at `0x00520E50`
copies X, Y, type and owner, then tests the byte currently parsed as
`NumberOfShots`:

- zero leaves the construction bit set and creates construction work;
- nonzero clears the bit through `0x004A96D0`, producing a completed trap.

The loader never copies the byte as a count. Firing and updates do not read it,
and no firing path decrements it. `NumberOfShots` is therefore a misleading
parser name: in this record it is a Boolean initial-construction marker,
STRONGLY INFERRED. It remains verbatim in `TrapState` for source compatibility.

`TrapThing.Unknown_0008` and `Unknown_000F` participate in the raw-record
checksum but are not copied into the runtime instance or its save range. Their
meanings remain **UNKNOWN**; DK2's runtime trap does not preserve them.

## Trap runtime behavior

The construction finisher at `0x004A96D0` clears only bit `0x20` and refreshes
the trap. This full loader-to-completion trace supports the existing
**UnderConstruction** interpretation from Q281.

Readiness, target selectors, field mapping, timing, mana, disarm execution,
reveal and shot coverage are now in [traps.md](traps.md). That note owns both
the evidence and the remaining integration reads. ADR-0251 builds the parsed
fields and stateless activation arithmetic; automatic firing stays disabled.

## Trigger conditions 27 and 28

The player-condition arms at `0x004C80CE` and `0x004C8240` have parallel count
modes:

- flag bit 3 counts owned instances under construction;
- otherwise flag bit 4 counts owned completed instances;
- otherwise `TargetId == 0` counts all owned instances and a nonzero id filters
  by definition id.

Type 28 has one additional rule: asking for a trap whose definition emits shot
26 also counts owned live object type 74. These are the Boulder projectile and
Boulder object ids established independently by shipped definitions.

`Comparison == 0` asks the keeper's separate availability table rather than
world state. It remains declined. All ten shipped type-27/28 records use a
nonzero comparison, so no shipped record needs the unread availability mode.

## Confidence and remaining work

| claim | confidence |
|---|---|
| Record counts, all-zero door trigger ids, 3 unfinished doors and 0 unfinished traps | **CONFIRMED from shipped data** |
| Loader transforms, saved ranges and runtime state transitions above | **STRONGLY INFERRED from DKII.EXE v1.70** |
| Door open eligibility and its order, the three tiles, the toggling lock command, the alliance array and the lock-pick countdown | **STRONGLY INFERRED from DKII.EXE v1.70** |
| The lock-pick duration, and which ability reaches the effect handler | **UNKNOWN** |
| Trap runtime and door contact | See [traps.md](traps.md) and [door-blocking.md](door-blocking.md) |
| Any semantic meaning for either `Unknown_0008`, door trailing bytes or trap `Unknown_000F` | **UNKNOWN** |

Trap firing is built for the traps whose shot is (ADR-0321, `traps.md`); Q450 lists what is
left, with owner observations Q451, Q452 and Q513.
A field absent from the runtime licenses
only that result; it does not show that a corresponding behavior is absent.
