# Combat body integration

Source: DKII.EXE v1.70 GOG, Ghidra 12.1.4, 2026-10-12. Primary
instruction reads and field-store census; no decompiler output is retained.

## Route-less fighting

CONFIRMED: the creature update's mover gate at `0x0048A232`–`0x0048A248`
tests state-table bit 0, independently of whether a path is loaded. The
recovered table gives states 199 and 200 flags 3 (creature-state-table.md).
Fighting therefore integrates without a route. The other gates exclude
sidelined, possessed and Imp creatures before this test.

Ours skipped every empty path, freezing ordinary neighbour contact after
combat stopped its route. `CreatureMovementSystem` now includes route-less
199/200; other route-less state dispatch remains unbuilt. No-route
`Follow_Path` brakes velocity without turning heading. Separation is soft,
with the original overflow arithmetic and zero-distance exemption
(navigation-and-steering.md section 3.2); it guarantees no rigid clearance.
The fixture regression establishes contact integration and retained facing.
Natural Level8 bridge distances and original timing remain unmeasured.

A controlled shipped-data Level8 Stone Bridge encounter forced Guard, Dwarf
and Salamander targets. After 16 ticks, Guard–Dwarf spacing was 100 raw units
with the old gate and 648 with contact integration. Both stayed on the bridge;
the lava-capable Salamander entered lava. One initial contact tightened from
100 to 6 units. This confirms restored motion, not guaranteed model clearance
or natural campaign behaviour.

## Remembered entity

CONFIRMED census: `ReadsAt`/`StoreAt` for `+0x537/+0x539`, with constructor,
selector and update positive controls, finds constructor zeroing, selector
failure zeroing, update decrement and expiry clearing. The complete selector
and fight-starter do not arm it. The earlier claim that the victim selector
stores the enemy here was unsupported. Activation remains UNKNOWN: the
census does not exclude indirect writes.

The body reader `0x00460F60` resolves only a live class-0 thing. Reach reader
`0x00460F90` supplies one tile in state 8, otherwise the remembered creature's
sight range. The separation reader is `0x004D2DF0`. The current fight target
is a distinct field; substituting it would invent behaviour. The modern
null supplier remains. Update expiry checks zero before decrement, clearing
the id on the following tick (`0x00489A6D`–`0x00489A83`).

## Verification

Release verification passed 5,046 tests with zero skips, including shipped
Guard/Dwarf/Salamander facing and post-impact holds, plus both route-less
fight states. Unity assembly sync and adapter compilation passed.
All 60 Level1–3 replay traces completed: modern wins 20/20, 2/20 and 6/20.
Artifacts are under `build/combat-tests237-final.txt` and
`build/classic-step3-replays/combat-followup237/`. This is modern regression
evidence; original bridge distances and visual equivalence remain Q345.
