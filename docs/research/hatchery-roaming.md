---
systems: [rooms]
---
# Chicken object roaming

Binary reads on 2026-10-10: object process table `0x0049E600`, Hatchery
process 6 `0x004A1F30`, movement starter `0x0049F880`, movement process 9
`0x0049FA50`, reaction process 8 `0x004A24A0`, and loose-object process 10
`0x004A26D0`. Whole bodies were decompiled; process 6 and movement-start
arithmetic were also checked in assembly. These are object handlers, not the
creature chicken-spell states. Captures remain under `reverse-engineering/`.

## 1. Owned Hatchery loop — STRONGLY INFERRED

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

## 2. Movement timing — STRONGLY INFERRED

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
the creature-avoidance helper. Chicken footstep audio (`0x110`) alternates through a
global counter when the movement count is 1; built, the listener keeps the counter
([sound-effects-objects.md](sound-effects-objects.md#1-objects-and-specials)).

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
four-neighbour search that checks matching owner and the raw navigation byte (`0x00455000`). It avoids
room tiles on the first three attempts, traps, doors and obstacle objects,
then chooses each sub-tile coordinate as `0x555 + random(0x555)` and moves with
resume process 10. The helpers are now read (2026-10-11): `0x0049CE50` returns
Chicken `+0x14A` or zero; `0x0049CEA0` returns Chicken `+0x152`, otherwise
`+0x14E`. The owner's `+0x132` is the stamped killer. The `+0x1A9` gate reads
the terrain record's Damage field (`0x00509480`, Q584): any nonzero value, harmful
or healing, kills a loose bird like a fallen owner or a held bird off a Hatchery.
A Treasury or Heart tile holding gold has a Gold Pile object, an obstacle. The loose roam is built (ADR-0647, hash/save 396; ADR-0649, 398). ADR-0697/hash-save 429 adds the virtual Heart pillars to its obstacle check; see the [bounded Chicken probe](neutral-ambient-effect.md#chicken-draw-4-at-turn-1402-adr-0697).

## 4. Implementation boundary

The owned Hatchery loop is built in `ChickenMovementRules` and stepped every
tick by `ObjectMovementSystem`, together with Rats, Eggs and Coops newest first (ADR-0699)
([prison-rats.md](prison-rats.md#3-life-and-presentation)). The shared class-list
order is established in `imp-task-stealing.md` §8. Heading, age, reaction countdown,
destination, velocity, resume process, flags and count are hashed and saved;
crossing a tile updates its occupancy link. ADR-0700/hash-save 432 adds the [virtual Hatchery pillar
obstacle check](q712-early-draw-continuation.md#built-at432-adr-0700) to the owned-room selector.
The [creature steering query](hatchery-pillar-steering.md) also includes virtual
Hatchery corner geometry at 441, with its ordering and lifecycle limits kept separate.
Hatching on an owned Hatchery starts
process 6, matching `0x004A1CF0`'s owned-room branch. The [Egg art clock and birth draws](chicken-birth-clock.md)
are built at 439; broader hatch-destination checks remain partial. Object initialization retains Chicken
`+0x14A = 1`. Loose process 10 is built (ADR-0647) and so is free-range decay (§5); Prison process 3 remains unbuilt.

Hand drop selects falling process 1 (`0x004BC710`). On landing without a
recipient, `0x0049EF90` selects the room's nonzero field-21 handler
(`0x004E6CD0`), without an owner check: Hatchery 6, Prison 3, Temple 4,
Treasury/Heart 5 and either Portal 52; otherwise a Chicken selects 10.
Each selection resets age. Our collapsed-height Hand landing does this
immediately, preventing a previously moving bird from resuming its old target.
Process 6 performs its separate owned-Hatchery check on its next tick.
Production worker carry accepts books, specials and crates, so chickens cannot
reach its put-down route. Recipient feeding removes the Chicken instead (health zeroed in its own update, §6).

Avoidance scans the eight compass neighbours, then the current tile, taking
the newest linked creature on each tile. The original rejects workers and
creatures above their ground-height word, then attempts a half-tile escape
with doubled speed and resume 8. Our absent ground-height word is approximated
by supported flight and `PosZ <= 0x1000`; complete vertical physics remains
unbuilt. Audio's global footstep alternation, the existing presentation size
approximation and Chicken KMF Walk/Eat/Scared playback and render interpolation are built;
see kmf-animation-playback.md §7.

## 5. Persistent room membership — STRONGLY INFERRED

Process 6 first tries `0x004E5FF0` when object room word `+0x138` is zero.
Admission consumes the definition's capacity; failure does not prevent roaming.
Successful admission calls `0x004C0010`, which clears Chicken eligibility
`+0x14A` through `0x0049CE30` and decrements the owner's nonzero counter
`+0x1C1`. Therefore registered birds cannot take the creature-escape path.
`0x004E6130` clears membership and gives capacity back; `0x004BFFD0` restores
eligibility and increments that counter. Normal movement retains membership.
Hand lift detaches a room object (`0x004C0CB0`), and removal releases it.
Room teardown (`0x004E39C0`) detaches ordinary room objects without deleting them.
Conditional decay in `0x0049BD20` tests membership zero, not physical room tiles: a bird no
room holds loses one health a turn and at none requests its death effect and is removed. The
decrement is CONFIRMED by the seed-1 Level 5 capture (frames 1396-1462: free-range birds -1 a
frame, registered ones stay 1000); the death is read, not captured. Built (ADR-0650).
The decay skips an object whose `+0x146` bit 0x10 is set: the hand's lift (`0x004C0CB0`, store
`0x004C0D17`) sets it on every object and landing (`0x0049EF90`, `0x0049EFB7`) clears it, so a bird in
the hand does not decay (STRONGLY INFERRED, binary only, no capture). Ours returns early for it.

Our saved/hashed Hatchery anchor identifies the current derived room containing
that tile. Northwest growth retains membership; an invalidated or differently
owned anchor releases it. Capacity counting follows the saved membership even
outside the room. A tile add, a merge and a removal release the birds of every room they free
(`0x004E39C0`: the absorbed rooms, or the whole old room) and then adopt the birds standing on the
Hatchery's tiles (`Hatchery_AdoptChickens`, `0x004DEB70`; `ChickenMovementRules.AdoptOnChain`,
ADR-0652, STRONGLY INFERRED, no capture covers a change). An adopted bird stops decaying. A capture
conversion (`Room_MergeNeighbours`, `0x004EC900`, and the owner-change slot `0x004DE5D0`) runs through
ours' removal and re-add; a partial conversion is unread. Unbuilt: `Object_SetOwner`'s owner-list relink,
the object trigger event 18, adoption at level load (process 6 registers such birds on their first
turn). Existing laying capacity continues
counting eggs as reservations; the original room list counts only chickens.

## 6. How a meal removes the bird — STRONGLY INFERRED (Q796)

A creature's meal does not delete the bird. The Hatchery meal (state 93, `0x00463760`,
`0x00463996`-`0x004639A8`) clears the bird's `+0x146` bit 0, the death-effect permission that
`Object_Init` (`0x0049B378`) sets for every object, and calls its vtable `+0x20`. The object vtable
starts at `0x0066D654`; slot `+0x20` (`0x0066D674`) is `0x004B2AD0`, which only writes health
`+0x40` = 0. `World_UpdateThings` (`0x004B7830`) updates creatures (list 0) before objects (list 2),
so the bird's `Object_Update` (`0x0049BD20`) still runs its process (`0x0049EED0`) that turn, then
its health <= 0 tail (a death effect only with bit 0 and definition `+0x119`: none for a meal), and
the second walk (vtable `+0x28` `0x004B5C30`, `+0x30` `0x0049BEC0`, then `Thing_Delete`
`0x004B7B10`) removes it the same turn. The searches (`0x004537E0`, state 92's ranking, the
walk-over chain) never test health, so a health-0 bird is still visible to a creature updated
later that turn. Hatchery generation runs after the second walk and never counts it. The prison's
eat (`0x00473860` vtable `+0x20`, bit cleared at `0x00473863`-`0x0047386B`, any TypeFood object),
the creature update's walk-over arm (`0x004892EF`-`0x00489318`, both branches, chain walk
`0x00489278`-`0x004892C7`) and the possessed arm (`0x00514830`: the meal clears bit 0 at
`0x00514BF2`-`0x00514C02`; the kill, `0x005149DD`-`0x005149F2`, leaves it set, so the bird's update
asks the death effect) do the same. Reviewer re-read, not captured.

Evidence: Level 5 seed 1, native turn 2003 has two reaction draws (`ObjectState.cpp:2782`, birds
1269 and 1279); ours' bird 110 (1279) was eaten at 2003 and removed, so it never drew.

Ours (ADR-0789, hash 504): `ObjectState.DeathEffectCleared` is hashed and saved. `CreatureEatSystem.Swallow`
and `PossessionRules.EatChicken` zero the bird's health (a meal also sets the flag; the possessed kill
keeps the effect) and `ChickenMovementRules.Decay` removes it in its own update, asking the death
effect unless the flag is set. `WalkOverGoldSystem` and `PrisonerEatSystem` still remove at once, on
purpose: ours run them after the object update, so deferring would give the bird a second update
next turn. Recorded deviation: a second walker the same turn finds no bird, and prison food that is
not a chicken likewise. Residuals: a held bird's `Tick` returns before `Decay` (native runs the tail
for every object), reachable only if a health-0 bird is lifted before its update; two meals on one
tile the same turn are not pinned by a test; the `+0x278` refusal call at `0x004637B3` and whether
`Thing_Delete` asks the `0xF4` sound are unread (ours' `RemoveObject` asks it).
