# Door and trap observations to run

Status: pending owner observation. Binary evidence is STRONGLY INFERRED in
[traps.md](../research/traps.md) and [door-blocking.md](../research/door-blocking.md).

## Q451 — a failed disarm

Use a visible, armed hostile trap with enough owner mana and record a creature
attempting to disarm it. Capture a failure and the following few seconds.
Does the trap fire at the failure itself, or only after the creature stops
disarming? Record trap type and creature level. The binary prediction and
its timing caveat are in [the disarm read](../research/traps.md#disarm-reveal-and-destruction).
If no creature begins disarming, report the setup; acquisition remains Q455.

## Q452 — a turning Sentry's range

On flat ground with clear sight, place a Sentry with no nearby Trigger trap.
Keep one hostile creature between five and seven tile centres away, with mana
available. Record whether the Sentry turns and whether it fires before the
enemy crosses the five-tile boundary. Repeat inside five tiles as a control.
Firing outside five supports the cache interpretation; turning alone does not.
Record the level, since an override may change the global range of five.

## Q454 — contact with an open enemy door

Answered by the owner on 2026-10-02 (CONFIRMED): an enemy stops to attack a door
held open. See `door-blocking.md` §1.

## Q362 — a creature fighting a revealed trap

On open floor, give a rival keeper a Sentry (visible from the start) and drop one
Goblin of yours four tiles from it, with no enemy creature in sight. Record from the
drop until the trap breaks or the Goblin falls. The build (ADR-0340) predicts it walks
onto the trap's own tile (its reach is 0.6 of a tile), swings there, and that an Imp
attacks only a revealed enemy trap it stands within about 0.7 of a tile of. Note where
the Goblin stands when it swings and how many swings break the trap.

## Q520 — the Boulder at a wall and at a corner

Build a Boulder Trap facing down a straight corridor that ends in rock, and a second
one whose target stands diagonally past an inner corner (the two side tiles free, the
corner tile rock). Lure an enemy into each trap's sight. Record whether the boulder
breaks on reaching the wall, rolls back (the binary's reading), or turns; at the corner,
whether it clips the rock tile and rolls on. A probe capture of object 74's position and
`+0x156` (wear) per turn settles it; video settles (b) and (e) alone.

## Q524 — the Jack In The Box

No shipped level places the trap: a custom map is needed, with the trap on a tile an
enemy Imp must cross on its way to work. Record the Imp's health at the burst, where
the Jack walks, whether it reaches the enemy heart or stands and wears out, and what
its explosion hurts. Predictions are `jack-in-the-box.md`.
