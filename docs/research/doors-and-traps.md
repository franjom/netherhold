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
presentation and bypasses the normal open/close work. A completed door derives
its open state from nearby eligible creatures, its lock mode, and a lock-pick
timer; a transition refreshes navigation and presentation.

The lock transition at `0x004A6800` refuses to act while the door is under
construction. Once constructed it changes the saved lock field and the
corresponding navigation-grid bit. The navigation writer at `0x004A66F0`
installs ownership and blocked/locked bits for completed doors, and removes the
door contribution while it is under construction.

This establishes that DK2 doors open, close, lock and affect navigation. It
does not yet establish every creature eligibility partition, the exact timing
of each transition, or how the pathfinder consumes those grid bits. Those parts
remain **UNKNOWN** and are not implemented. The consumer is now located, which
is where that last part should be picked up: the bits feed a family of world
tile predicates, and DK2 routes over a navigation mesh rather than a grid
([navigation-and-steering.md](navigation-and-steering.md), ADR-0155).

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
