---
systems: [creatures, rooms, simulation-core]
---
# Mechanic: a creature's height, its floor, and the physical pass's fall (Q795)

Source(s): DKII.EXE v1.70, disassembly of `0x004545C0`, `0x00454110`, `0x00450470`, `0x004B2030`,
`0x004B2740`, `0x004B5020`, `0x0048A457`, `0x00459560` and `0x004596D0`, read twice on 2026-11-13 (the
reviewer's Ghidra re-read agrees on every item marked STRONGLY INFERRED).
Original version: DK2 v1.70 (GOG)

Read, not captured, except where a capture is named. Level 5 seed 1 (diag controlled548) is the one replay
that depends on it.

## 1. What DK2 keeps

Every thing has an absolute height `z` (`+0x22`) and a floor word (`+0x26`). The ordinary floor is 0x1000, so
a creature on flat ground has `z` 0x1000; ours keeps `CreatureState.PosZ` above that, zero on the ordinary
floor. The setter `Thing_SetPosition` (`0x004B5020`) recomputes the floor from the tile floor function
(`0x004545C0`) at the new x and y on every position write and stores it in `+0x26`; it raises `z` to the
floor when below (and zeroes a creature's vertical vector `+0x10C`), and drops a `z` above 0x3000 to 0x2000
(record 004B5020). It never lowers `z` otherwise: a thing above its floor stays there until the physical pass
brings it down.

## 2. The floor function `0x004545C0` (STRONGLY INFERRED, whole body read)

The point's tile decides the arm.

1. A room tile first: a Portal's cell piece 4 gives 0 and a Dungeon Heart's centre piece (cell byte `+0xC` ==
   0x40) gives 0; the three bridge types (8, 17, 22) give 0x1199; a result that differs from 0x1000 returns at
   once.
2. Otherwise the floor-slot test (`0x00454110`, interior cell plus the type's rule) accepts: Hatchery 0xE00,
   Temple 0, Graveyard and Crypt 0xF00, Combat Pit 0x400, any other type 0x1000.
3. A tile that is not a slot takes a zone arm, for Hatchery, Graveyard, Crypt and Pit only. The point's
   position inside the tile picks a zone (x under 0x400 west, over 0xC00 east, y under 0x400 north, over 0xC00
   south) and each zone needs neighbour bits R: NW 0xC1, N 0x01, NE 0x07, W 0x40, E 0x04, SW 0x70, S 0x10, SE
   0x1C, centre none (bit order of table `0x006BC370`, N, NE, E, SE, S, SW, W, NW). Hatchery, Graveyard and
   Crypt give 0xE00 when the mask `M` of same-terrain, same-owner neighbours has every bit of R (`M & R == R`),
   else 0x1000. The Pit's mask holds the neighbours whose cell byte is 0xFF (interior), same terrain and owner;
   it gives 0x1000 when `M & R` is empty and 0x1200 otherwise. Every other room type gives 0x1000.
4. No room: terrain that is neither water (flag 0x80) nor lava (0x100) gives 0x1000. A liquid tile whose own
   cell byte is 0xFF (every neighbour liquid) gives 0xA66. Otherwise a bank: the octant of the point's angle
   from the tile centre, `o = (atan2 >> 8) & 7`, the dry-side mask `Dry` (bits 8 and up set, the liquid mask
   inverted), and a guard `G = ((7 << e) | (7 >> (8 - e))) & 0xFF` with `e = o & 6`. `Dry & G == 0` gives 0xA66.
   Else the distance `d` is the larger of |dx|, |dy| from the centre for an even octant whose bit `e` is dry,
   the smaller for a wet one; for an odd octant the test mask is `(4 << e) | (4 >> (8 - e))`, tested in 32
   bits. The floor is `0xA66 + (d * 0x59A >> 11)`.
5. The liquid mask `0x00450470` marks a neighbour that is off the map, has the same terrain id, or is a room
   tile whose definition (`+0x43D` & 3) and bridge-over-lava bit match.

Calibration: Thief 434 at (82525, 57099) on a lava bank is octant 4 at `d` 1803, which gives 3924 exactly.

Suspected DK2 defects in this function (ADR-0783: built as they are): the odd-octant test mask is not cut to a
byte as the guard is, so a 32-bit NOT leaves octant 7's test bit always set and its distance always the larger
one; and the Temple's rim arm (5000) is dead.

## 3. The physical pass `0x004B2030` and height (STRONGLY INFERRED)

The update calls it for every creature each turn (`0x0048A204`), in any state including the collapse states
216 and 217.

- Gravity. When the creature is not flying (vtable `+0x88` is `0x0048ED40`'s test) and `z` is above the floor
  word, the persistent vector `+0x104/+0x108/+0x10C` gains `-(GravityConstant << 8)` in z: variable 130 is 240
  in the shipped data, so -61440 a pass.
- Ground response `0x004B2740` (vtable `+0x64`) zeroes `+0x10C` when `z` is at or below the floor.
- The pass sums the persistent and the temporary vectors and clears the temporary one. A zero sum returns with
  no commit and no body write. Otherwise it takes `n = min(16, (length3D(sum) >> 14) + 1)` sub-steps; only x and
  y feed the stop tests, one commit uses the last accepted point, and z moves by a sixteenth of the vector
  (truncated). If that sixteenth truncates to no change `z` moves by the whole vector (`0x004B267C`).
- The commit is the setter, so the floor and the clamp of section 1 apply. The pass treats `|+0x104| +
  |+0x108|` in 1..0x7F as nothing (both zeroed, no commit, `z` untouched); 0 passes.
- Friction `(0x1000 - word +0x11E) / 0x1000` scales x and y only; the vertical word is undamped (the drag
  at `0x004B26B4`..`0x004B26FA` restores `+0x10C`).
- A class 0 (creature) commit writes the steering body's `+0x1A8/+0x1AC` from the entity, x and y `<< 4`,
  also on a move of z alone; the body's velocity is left alone.

The walking commit `0x0048A457` also goes through the setter (`0x004B5560`), so a step onto a higher floor
raises `z` at once and a step onto a lower one leaves `z` for the next pass. On a falling floor `z` therefore
lags one pass behind: the capture's water walks satisfy `z(t+1) = floor(t)` exactly.

## 4. The collapse entries (STRONGLY INFERRED)

`0x00459560` and `0x004596D0` never call `StopMoving` (`0x0048ECE0`), and neither does the damage call
`0x0048B770` that reaches them. They put the creature through the setter where it stands and zero
`+0x104..+0x118`. The steering body keeps its last step's 16.16 point and velocity until a physical commit
puts it back on the entity, so a neighbour's separation reads a body that is not where the entity is. A
collapse over a liquid bank leaves the creature above its new floor word, and the next pass falls: Thief 434
at turn 1864 has floor word 3924 under `z` 4096.

## 5. Ours (ADR-0788, hash 503)

- `FloorHeightRules.At` is the whole of section 2 over the tile grid. Native reads the cell's cached piece and
  neighbour masks (`+0xC`, kept by `0x00450470`); ours derives them from the neighbouring tiles. Approximated:
  a Portal's piece 4 is the tile whose four orthogonal neighbours share terrain and owner (native also asks
  the same room thing); the Heart's centre is the tile whose 5 x 5 square is all Heart; `0x00454110`'s
  per-type rule is the interior test alone (a Graveyard cell holding a crypt is not refused); the Pit's
  interior test is the eight-neighbour test.
- `CreaturePhysicsRules.Step` runs every creature every update: raise to the floor first (ours' other position
  writers do not call the setter), gravity, ground response, sub-steps with `nextZ` fed to the door test,
  `SetHeight` (the setter's height half) on commit, body resync. `OwnsHeight` excludes what keeps its own
  height rules: the imp's creation fall (state 214), a party's arrival, a hand drop's landing, a creature in
  the hand, a possessed one.
- `CreatureMovementSystem` commits through `SetHeight`; `CreatureDeathRules.Collapse` does not call `StopMoving`,
  zeroes `PhysicalVelocityX/Y` and `CreationVelocityZ` (`+0x10C`), and calls `SetHeight`.
- `PreviousPositionStep` copies z into `LastPosZ` (`+0xFE`) with x and y (record 00488930).
- `EggLaunchRules.FloorHeight` delegates to `FloorHeightRules`, so every caller gets the full function: eggs,
  `HeartSoundRules`, `ThingSoundRules`, `HandSystem`, `ImpSupplySystem`, `TortureSystem` and
  `TriggerExecutor`'s object z. The Heart's blow sound is now at z 0 at the Heart's centre.

## 6. Not built

- The flying slot's other arms (`0x0048ED40`: a worker's `+0x3E6` bit 8, five animations that answer no,
  `0x00494700(0) == 3`, a Vampire bat). Flyers have no altitude in ours, so this changes nothing today.
- The `0x004B267C` small-vector rule is built but unreachable for a creature: its gravity step is never small.
- The walking commit's blocked path does not call `SetHeight`; the next pass's raise covers it.
- The setter's layer-2 raw copy has no counterpart.
- The barricade test `z < 0x1801` (`DoorRules.BlocksAt`) is given `PosZ`, zero at the ordinary floor, where
  DK2 gives the absolute `z`, so ours blocks up to the ordinary floor's 0x1000 higher than DK2.
- The presentation draws a creature on its own ground projection (`WorldView.CreatureGroundHeight`, pinned by
  presentation tests), and reads `PosZ` as height above that: `ClassicCreatureQuery`, `CreatureHoverSound`,
  `VoiceLocator`, `WorldView.Minimap`, `ClassicLightning.cs`. Drawing a creature at
  the simulation's height is a presentation follow-up.
- `ShotSystem` damps a shot's vertical velocity by the air friction and cites `0x004B26B2`; the creature's
  pass leaves it undamped. Whether a shot's class differs is unread (record 004B2030).

## 7. Effect on the Level 5 replay

At hash 503 the controlled replay matches 10033 draws through turn 2002 (hash 501: 9828 through 1987). It
parts at turn 2003 draw 7 (Q796). The first creature position splits are Warlock 439 at 2034, 433 at 2042,
437 at 2054 and 438 at 2060; the Warlocks no longer part at 1923. Level 1's seed-1 win tick is unchanged,
2566, with the journal re-recorded. Tests: `FloorHeightTests` (each arm, the octant-7 quirk labelled read, not
captured; lava, Graveyard, Temple and Pit interiors, the Pit rim, a raise onto a bridge, the hand-drop
exclusion, the 0x3000 cap, the collapse zeroing the vectors) and `DeathFallTests`.
