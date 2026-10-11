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

Assembly `0x0049F9D5` divides by the fixed-point squared speed without a zero
guard. Nonzero runtime speeds below 64 therefore cannot drive this routine;
ours reports that invalid definition explicitly instead of changing its speed.

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
`+0x102`, used by process 8's heading changes. The object's vtable
`0x0066D654` slot `+0x58` contains `0x004B5C50`; its complete assembly
writes the fixed turn rate `0x100` (2026-10-11).

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
resume process 10. The helpers are now read (2026-10-11): `0x0049CE50` returns
Chicken `+0x14A` or zero; `0x0049CEA0` returns Chicken `+0x152`, otherwise
`+0x14E`. The owner's `+0x132` is the stamped killer. The region `+0x1A9` gate
remains unnamed after a complete process-10 read; see Q584. Loose roaming is unbuilt.

## 4. Implementation boundary

The owned Hatchery loop is built in `ChickenMovementRules` and stepped every
tick, newest first, by `ChickenMovementSystem` (ADR-0434). The shared class-list
order is established in `imp-task-stealing.md` §8. Heading, age, reaction countdown,
destination, velocity, resume process, flags and count are hashed and saved;
crossing a tile updates its occupancy link. Hatching on an owned Hatchery starts
process 6, matching `0x004A1CF0`'s owned-room branch. Egg animation timing stays
the existing two-second approximation. Object initialization retains Chicken
`+0x14A = 1`; full loose process 10 and Prison process 3 remain unbuilt.

Hand drop selects falling process 1 (`0x004BC710`). On landing without a
recipient, `0x0049EF90` selects the room's nonzero field-21 handler
(`0x004E6CD0`), without an owner check: Hatchery 6, Prison 3, Temple 4,
Treasury/Heart 5 and either Portal 52; otherwise a Chicken selects 10.
Each selection resets age. Our collapsed-height Hand landing does this
immediately, preventing a previously moving bird from resuming its old target.
Process 6 performs its separate owned-Hatchery check on its next tick.
Production worker carry accepts books, specials and crates, so chickens cannot
reach its put-down route. Recipient feeding removes the Chicken instead.

Avoidance scans the eight compass neighbours, then the current tile, taking
the newest linked creature on each tile. The original rejects workers and
creatures above their ground-height word, then attempts a half-tile escape
with doubled speed and resume 8. Our absent ground-height word is approximated
by supported flight and `PosZ <= 0x1000`; complete vertical physics remains
unbuilt. Audio's global footstep alternation, randomized object scale and the
Chicken KMF walk/peck/scared playback remain presentation work.

## 5. Persistent room membership — confirmed

Process 6 first tries `0x004E5FF0` when object room word `+0x138` is zero.
Admission consumes the definition's capacity; failure does not prevent roaming.
Successful admission calls `0x004C0010`, which clears Chicken eligibility
`+0x14A` through `0x0049CE30` and decrements the owner's nonzero counter
`+0x1C1`. Therefore registered birds cannot take the creature-escape path.
`0x004E6130` clears membership and gives capacity back; `0x004BFFD0` restores
eligibility and increments that counter. Normal movement retains membership.
Hand lift detaches a room object (`0x004C0CB0`), and removal releases it.
Room teardown (`0x004E39C0`) detaches ordinary room objects without deleting them.
Conditional decay in `0x0049BD20` tests membership zero, not physical room tiles.

Our saved/hashed Hatchery anchor identifies the current derived room containing
that tile. Northwest growth retains membership; an invalidated or differently
owned anchor releases it. Capacity counting follows the saved membership even
outside the room. This is bounded by the existing derived-room model: a sale,
split, absorbed-room merge or capture can recreate original room identities
while a still-valid anchor survives here. Full identity teardown/adoption and
the free-range census/decay remain unbuilt. Existing laying capacity continues
counting eggs as reservations; the original room list counts only chickens.
