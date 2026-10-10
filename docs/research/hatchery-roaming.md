# Chicken object roaming

Binary reads on 2026-10-10: object process table `0x0049E600`, Hatchery
process 6 `0x004A1F30`, movement starter `0x0049F880`, movement process 9
`0x0049FA50`, reaction process 8 `0x004A24A0`, and loose-object process 10
`0x004A26D0`. Whole bodies were decompiled; process 6 and movement-start
arithmetic were also checked in assembly. These are object handlers, not the
creature chicken-spell states. Captures remain under `reverse-engineering/`.

## 1. Owned Hatchery loop — CONFIRMED

Process 6 acts on Chicken object 9 in its owner's Hatchery 4. An unregistered
room object (`+0x138 = 0`) attempts room registration before moving. It rolls
`random(4)` and examines the four cardinal neighbours cyclically from that
index: `(0,-1)`, `(1,0)`, `(0,1)`, `(-1,0)` (table `0x006BC350`). The first
same-owner Hatchery tile with no obstacle from `0x00453600` becomes the target;
otherwise the current tile remains the target. This excludes obstacle objects,
not every object on the tile.

The destination is a random sub-tile point. For `missing = ~tileMask`, narrowed
to a byte from tile `+0x0C`, the exclusive random bounds on each axis are:

| Axis | Lower | Upper |
|---|---|---|
| X | `0x100 + ((missing & 0xE0) != 0 ? 0x400 : 0)` | `0xF00 - ((missing & 0x0E) != 0 ? 0x400 : 0)` |
| Y | `0x100 + ((missing & 0xC1) != 0 ? 0x400 : 0)` | `0xF00 - ((missing & 0x38) != 0 ? 0x400 : 0)` |

Each coordinate is tile base plus lower bound plus `random(upper-lower)`.
The handler starts movement with resume process 8, extra speed 0 and
interruptible flag 1. Outside an owned Hatchery, a room tile routes to process
10; otherwise it returns 0 to the process dispatcher.

## 2. Movement timing — CONFIRMED

`0x0049F880` checks destination bounds, then stores process 9, process age 0,
resume state `+0x15C`, destination X/Y `+0x160/+0x164` and interruptible flag
`+0x172` bit 1. Speed is the signed 16-bit sum of definition `+0x100` and the
extra-speed argument; zero returns failure after those state writes.

For bearing `theta = atan2(dx,dy) & 0x7FF`, velocity X/Y at `+0x168/+0x16C`
is `(sin(theta)*speed)>>16`, `(-cos(theta)*speed)>>16`, with signed 32-bit
products. Let `D = narrow32(dx*dx>>12) + narrow32(dy*dy>>12)` and
`S = narrow32(speed*speed>>12)`. The signed-square products use 64-bit
intermediates. The stored 16-bit movement count at `+0x170` is
`max(1, isqrt(unsigned(D)/unsigned(S))-1) + (unsigned(D)%unsigned(S) != 0)`.
The helper sets `+0x172` bit 0 to require turning first. This is not a rounded
distance divided by speed.

Process 9 calls `0x004B2AE0` while that turning flag is set. A nonzero angle
remaining consumes a tick without movement or countdown decrement. Once
facing, it clears the flag, decrements the count, and applies the velocity on
nonzero counts. At zero it snaps to the destination, preserving Z, restores
the resume process and resets age. Each intermediate Chicken step also calls
the creature-avoidance helper. Chicken footstep audio alternates through a
global counter when the pre-decrement movement count is 1.

The definition loader `0x0049DD60` divides file speed `+0x338` by global
ticks/second `0x006F2590`, narrowing to runtime word `+0x100`; a nonzero source
that narrows to zero is raised to 1. It copies file word `+0x34A` to runtime
`+0x102`, used by process 8's heading changes. The object turning getter behind
vtable `+0x58` is not resolved by this investigation.

## 3. Reaction and loose movement

Process 8's countdown, art and Horny marker are already recorded in
[object-drawing.md](object-drawing.md#8-furniture-eggs-and-chickens) and
[special-role-idle.md](special-role-idle.md). The normal loop returns to process
6 after its `random(16)+8` countdown. `0x0049CEC0` can instead start a
half-tile escape from a grounded creature, resuming process 8 and adding
definition speed again; see its function record for the eligibility gates.

Process 10 is broader than an owned-room wander. It has Chicken/free-range
eligibility and destruction paths, owner-state and world-region gates, and a
four-neighbour search that checks matching owner and floor height. It avoids
room tiles on the first three attempts, traps, doors and obstacle objects,
then chooses each sub-tile coordinate as `0x555 + random(0x555)` and moves with
resume process 10. Helpers `0x0049CE50` and `0x0049CEA0` and the meanings of
the owner/region gates remain unresolved; their addresses identify the next
reads, not established semantics.

## 4. Implementation boundary

Roaming is unbuilt. The existing position header can carry sub-tile coordinates,
but a faithful owned-room loop also needs persistent heading, process age,
reaction/countdown state, destination, velocity, resume state, movement flags
and movement count. Per-tick stepping must update tile occupancy when crossing
a tile boundary. The mask bounds, turn getter and avoidance path must be
represented; an arbitrary wander interval or teleport does not mirror this
loop. Full loose movement additionally needs the unresolved process-10 gates.
The egg placement fix in [hatchery.md](hatchery.md#3-where-an-egg-appears) is independent.
