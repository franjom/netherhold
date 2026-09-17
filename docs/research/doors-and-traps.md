# Doors and traps: authored projection and runtime construction

This note is the canonical evidence for placed doors and traps. Addresses refer
to DKII.EXE v1.70. Binary findings are **STRONGLY INFERRED**; only corpus facts
read from shipped data are **CONFIRMED**.

## Result

The THINGS loaders use one door flag as an initial lock request, another as an
initial construction marker, and the trap field named `NumberOfShots` only as a
Boolean initial-construction marker. The runtime later maintains construction,
open, lock, disarm, recharge and firing state independently of the authored
records. This branch models only construction and the trigger counts that read
it; open/close, navigation blocking and firing remain unbuilt under ADR-0141.

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
presentation and bypasses the normal open/close work. A completed door's
**open** state (`+0x10D`, read 2026-09-17) is recomputed every update: lock
mode 3 (`+0x111`) keeps it shut and a running lock-pick timer (`+0x115`) forces
it open; otherwise it is open while an **eligible** creature stands on one of
three tile chains (`0x004A5E9E`–`0x004A5F1B`) — the door's tile and, STRONGLY
INFERRED, the two beside it along the passage. Eligible: allied with the owner
(`0x004BF3D0`: the same player, else the owner's alliance byte at
`player+0x1AE+id`), not the neutral player (`[0x006F2548]`), creature flag
`+0x3E6` bit 21 clear (unread), and by lock mode — 0 any, 1 no worker
(definition flag bit 0), 2 workers only.

**Navigation.** The writer `0x004A66F0` sets the tile's byte to
`owner << 4 | 4 if lock mode ≠ 0 (or definition byte +0x8B bit 6) | 2 if the
tile's flag bit 0` and, when told the door is open or under construction,
writes plain floor instead; an open door is floor for everyone. The lock
transition `0x004A6800` stores the mode and toggles bit 2 in place. How the
cost function reads the byte is navigation-mesh-and-search.md §4.

Built (ADR-0158, `DoorSystem`, `WorldSteering`, `MeshRouter.TransitionCost`):
open state, lock mode from the authored flag, the byte and the cost arms.
Unbuilt: the lock transition and who calls it, the lock-pick timer, alliances,
definition byte `+0x8B` (Q353).

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

The firing decision at `0x004A7D10` refuses an under-construction trap. A
completed trap additionally checks its recharge state, disarming state and,
where applicable, whether its keeper can afford the mana cost. The update at
`0x004A84E0` bypasses ordinary targeting while construction or disarming is
active, otherwise finds a target or proximity condition according to the trap
definition, advances a runtime charge counter, fires at the definition's
threshold, and installs a definition-derived recharge.

The firing path at `0x004A80E0` creates the shot type named by the trap
definition. No authored shot counter is consumed. Exact target selection,
charge timing, mana charging, disarming and projectile behavior remain
**UNKNOWN** or unimplemented, so this branch adds no trap firing.

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
| Exact door open eligibility and transition timing | **UNKNOWN** |
| Exact trap targeting, charge, mana, disarm and shot behavior | **UNKNOWN** |
| Any semantic meaning for either `Unknown_0008`, door trailing bytes or trap `Unknown_000F` | **UNKNOWN** |

Next work should isolate door navigation as a pathfinding change and trap
firing as a definition-driven state machine, each with original-game
experiments for observable results. A field absent from the runtime licenses
only that result; it does not show that a corresponding behavior is absent.
