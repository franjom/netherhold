# Creature movement dust

Primary source: retail v1.70 DKII.EXE, Ghidra 12.1.4 decompilation and
instruction checks, 2026-10-13. Shipped data: the reference install's
Data/Editor/Terrain.kwd, Effects.kwd and EffectElements.kwd.

## 1. Ordinary trigger - CONFIRMED

Creature_Update (`0x00488930`) requests effect 117 at
`0x0048A06A-0x0048A0AE` when both conditions hold:

- Its previous and current positions occupy different X or Y tiles.
- Its tile at update entry has terrain id 3, the shipped Dirt Path.

The comparison at `0x00488F61-0x00488F99` uses the previous-position getter,
vtable slot +0x2C (`0x004B5D40`, returning thing +0xF6), against current
position +0x1A. XOR masked with 0xFFFFF000 discards sub-tile differences.
The tile pointer was obtained from current position at `0x00488961-0x00488970`.
Ordinary integration therefore produces a crossing which the next update
detects on the arrived tile; this is not a test of the departed tile.

The request occurs after previous position is refreshed and the state handler
runs, before this update's mover and animation chooser. It retains the entry
tile guard but takes XY from the then-current creature position. State-side
position changes must not be silently substituted for the stored crossing.

One request occurs per detected crossing, including a diagonal crossing;
there is no loop for intervening tiles. This arm has no walk/run mode,
animation-phase, foot-contact, speed, owner, flying or creature-kind test.
Within-tile movement does not emit; standing does not emit. Claimed Path id 4,
rooms, bridges and liquids do not satisfy the terrain-id test. A route or
walking animation alone is insufficient.

Request arguments: effect 117, creature owner, current XY with fixed height
4096, current heading and an output pointer. World slot +0x38C forwards this
through `0x00509580`; shared admission/allocation limits are documented in
[keeper-spell-tremor.md](keeper-spell-tremor.md), section 3.

The same effect also appears in status-timer requests, independently of this
terrain condition; their existing cadence lives in
[creature-status-effects.md](creature-status-effects.md), section 1.

## 2. Shipped dust - CONFIRMED data

Effect 117, Foot Dust Effect, has no own art, flags 0x280, HP 1/1,
generation type 4 and eight passes. Its sole generated id is element 264.
All parent motion/scale quantities are zero. Inner/outer origin ranges are
0/64, lower/upper heights 15/15, orientation range 255 and sprite spin range
32. Next/death/collision ids and generation randomness are zero.

Element 264, Foot Dust Element 0, uses mist art type 3 with flags 0x24,
sprite dimensions 4096/4096 and declared animation count 16. RGB is
128/128/128; element flags are 8 and fade percentage 100. HP is 16/24,
XY speed 1228/3686, vertical speed 0/409, scale 1228/2457. Mass and
elasticity are zero; friction is 327; scale ratio and linked ids are zero.
These are grey additive mist particles, distinct from coloured digging mist.

The type-4 generator `0x004386F0` requests eight elements per emission for
these flags; parent HP 1 makes this a single emission before expiry under
the nominal view update. Allocator rejection can reduce the visible count.
Origin radius is 0..1471 and height offset 240 in 4096-per-tile units.
Texture storage, blend semantics, integer motion and the 50ms view clock
are already established in [room-construction-effects.md](room-construction-effects.md),
sections 3-5. Nominal child lifetime is 0.8-1.2 seconds; dimming is the flag-8
integer RGB decay rather than a newly invented alpha fade or scale growth.

## 3. Current boundary and remaining comparison

Built 2026-10-14 (ADR-0477): `MovementDustSystem`, placed just before the mover,
keeps the tile it saw each creature on last tick (`CreatureState.LastDustTile`,
neither saved nor hashed) and posts the request when the tile differs and the
creature's tile is terrain 3, at its position one tile up with its heading. A
second reader's correction of §1's order (2026-10-14): DK2 computes the crossing
flag at the update's entry (`0x00488F61`), from `+0xF6`/`+0xFA` against `+0x1A`/
`+0x1E`, **before** the previous-position refresh (`0x00489FE6`) and the state
handler (`0x00476FC0`); the request (`0x0048A06A`) comes after the handler with
the then-current position and heading and the terrain of the tile found at entry.
Ours therefore matches exactly only when no state handler moves the creature
within the tick; a handler's teleport is recorded as the new last tile rather than
noticed as a crossing on the next update, and the terrain is the current tile's.
Held creatures and corpses are skipped as the other mirrors of this update do
(ADR-0206); whether the thing pass updates them is unread. The existing type-4
particle consumer and install sprite provider draw element 264.

UNKNOWN: a controlled original-game count/trajectory comparison and native
visibility rejection at tile boundaries. The owner's observation of dirt
walking/running dust supports the binary result but is not a measured experiment.
Implementation must map the original previous-position/entry-tile ordering;
adding a post-move event without checking that ordering is not established here.

## 4. Other inspected helpers

The unrelated helper interpretations found while locating the emitter live
only in their function records: [004B4550](functions/004B4550.md),
[0048B5F0](functions/0048B5F0.md), [0048EB50](functions/0048EB50.md) and
[0048FE30](functions/0048FE30.md).
