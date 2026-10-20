# Wet and bloody footprints

Primary source: retail v1.70 DKII.EXE, Ghidra 12.1.4 decompile and instruction
checks, 2026-10-20; shipped `Effects.kwd`. Q610.

## Producer — CONFIRMED

The creature update [0x00488930](functions/00488930.md), before its action,
fight, state handler and movement, maintains native bytes +0x478 (blood) and
+0x479 (water). The constructor zeros both.

- Water terrain flag +0x17B bit 7, grounded (position Z at or below signed
  floor height +0x26) and not flying (virtual +0x88): clear blood and refresh
  water to ten times the tick rate, 40 in shipped levels. No wet effect on water.
- At native global `0x00759AEC >= 4`, airborne or flying clears both counters.
  Otherwise on non-water terrain, the first class-6 thing on the same tile
  refreshes blood to 40 and clears water. The tile finder
  [0x00453670](functions/00453670.md) tests class alone: no distance, health,
  blood-pool, corpse-age or creature-kind condition.
- On that dry-ground path, nonzero blood requests effect 99 and decrements
  blood; otherwise nonzero water requests effect 100 and decrements water.
  The decrement occurs even if effect allocation fails. There is no movement,
  tile-crossing, animation-contact, ownership or dry-terrain-id condition.

Instructions `0x00489821–0x0048984A` establish water refresh;
`0x00489957–0x00489A4D` establish the dry path. Requests carry current XYZ,
owner and heading. Remaining counters pause on water through refresh, rather
than measuring elapsed time since exit. Remaining dry requests last ten game
seconds, including while stationary. A corpse tile refreshes blood every tick
and emits immediately, leaving 39 after each request. Dying creatures are
still class 0; a class-6 corpse is required to supply blood.

## Appearance — CONFIRMED shipped data

| effect | art | art flags | type | scale | HP | generation |
|---|---|---|---|---|---|---|
| 99, Bloody Footprints Effect | `bloodfoot` | `0x40101` | 2, alpha | 819 | 50 | 0 |
| 100, Water Footprints Effect | `waterfoot` | `0x100` | 2, alpha | 819 | 50 | 0 |

Both have effect flags `0x480`, fixed HP/scale, 4096-square art dimensions,
zero mass, friction, speed, generation ids and linked effects. Flat art flag
`0x100` uses the existing [flat sprite draw](functions/0059C720.md): a square
with half-size 819/4096 tile, rotated by the request heading. The initializer
[0x004347E0](functions/004347E0.md) installs the parent's own art at birth.
Generation zero's dispatch entry is null; these are not generated elements.
Their base tint is RGB0, alpha255: the render reset supplies these defaults,
and neither footprint has the art flag2 palette override. Tile lights add to
that tint through the existing flat renderer; RGB255 would make prints fullbright.
The effect update [0x00434E80](functions/00434E80.md) consumes lifetime on the
independent 50 ms view clock: nominal lifetime 2.5 seconds per print.

## Build boundary and remaining observation

Built: `FootprintSystem` before fight and movement; saved/hashed byte counters;
stationary flat generation-zero parent sprites through `RoomConstructionParticles`,
the install sprite provider and Unity's existing flat meshes. Asset keys use
`effect.<name>.effect`, distinct from generated `effect_element` keys.

The implementation runs the native global >=4 path; that global's meaning
and a configurable lower setting are UNKNOWN. It reuses the existing
`WaterSpeedRules.IsFlying` subset; exceptional worker/state/animation flight
arms remain outside that subset (see [flying slot](functions/0048ED40.md)).
Simulation creature Z is relative to its floor, so grounded means Z <= 0;
the view places the decal at the queried floor height. Held creatures/corpses
and carried corpses do not participate in the tile contact pass.

STRONGLY INFERRED: each print keeps constant alpha until removal. No lifetime
alpha change was found in the initializer, updater, generation-zero dispatch,
resource installation/advance or expired-effect scheduler. The owner's
observation (2026-10-18) of oldest-first fading remains UNKNOWN: overlapping
stationary prints expiring separately could produce it, but this is only a
HYPOTHESIS. A timed native capture of one isolated moving print versus a
stationary pile would distinguish opacity fading from overlapping removal.
No invented linear fade is applied.
