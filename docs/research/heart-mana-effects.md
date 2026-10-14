# Dungeon Heart mana particles

DKII.EXE v1.70, Ghidra 12.1.4 primary reads and shipped Editor KWD records,
2026-10-13. Record values are **CONFIRMED data**; motion and emission are
**CONFIRMED binary readings**, awaiting a matched original visual comparison.

## 1. Shipped definitions

Effect 59, Mana To Heart, has no own art, flags `0x2280`, HP 2, generation
type 4, five passes per update, and generated slots `[98,100,98,0,0,0,0,0]`.
It has zero velocity/mass/friction, origin radius 0..12, height 200..200,
orientation 255, spin range 78, whirlpool 32, circular path 12, fade duration
25, and next effect 242. All death/collision effect ids are zero.
The type-4 conversions give radius 0..275 and height 3200 above the request
point, in 4096 units per tile. At an ordinary delta-one view update this
requests fifteen particles per wave, ten id 98 and five id 100, for two waves.
See [room-construction-effects.md](room-construction-effects.md) for view
clock, type-4 origin, inclusive speed/HP and exclusive scale sampling.
The shipped Dungeon Heart room's CreatedObjectEffectId is 59 (file `0x384`,
runtime `+0x423`; loader mapping in room-construction-effects.md section 6).

| Element | 98 | 100 | 99 | 101 |
|---|---|---|---|---|
| HP | 24 | 24 | 24..32 | 24..32 |
| Mass | 0 | 0 | 819 | 819 |
| Friction | 393 | 393 | 524 | 524 |
| XY speed | 819 | 819 | 3276..8192 | 3276..8192 |
| Vertical speed | 14336..16384 | 14336..16384 | -4096..8192 | -4096..8192 |
| Scale | 819..2457 | 819..2457 | 1228..2048 | 1228..2048 |
| Scale ratio | 0 | 0 | 819 | 819 |
| Flags | 0 | 0 | `0x12` | `0x10` |
| Death element | 99 | 101 | 0 | 0 |
| RGB | 10,255,200 | 200,240,200 | 10,255,200 | 200,240,200 |

All four use static `Sparkle-03g`, type 3 additive sprite, width/height 4096,
fade percentage 100, zero elasticity and zero next/collision ids. Id 98 has
art flags 1; the others have zero. None has texture animation flag 4.
The shipped texture directory contains `Sparkle-03gMM0`, `MM1` and `MM2`;
there is no need to invent a flake texture or texture animation.
Effect 242, Mana2heart Light, has HP 60, vertical speed 14745, friction 327,
flags `0x9480`, fade duration 40, and no generated elements. Its exact next
effect transition timing and light lifetime have not been audited here.
Its light has offset `(0,0,1228)`, radius 8192, flags `0x3000`, RGB 60,94,39
and alpha 0 in the shipped descriptor.

## 2. Rise, replacement and falling motion

`0x00435600` updates each linked element before checking its remaining HP.
`0x00436430` subtracts elapsed HP, advances animation and moves even on the
expiry call. Removal `0x00435420`, with its ordinary argument zero, creates
the definition's death element at that final position and heading before
unlinking the expired particle. It copies the three spin increments. The
new element otherwise receives fresh authored HP, velocity, scale and RGB
through `0x00435F20`; it does not inherit old velocity or sample a new origin.
This is the authored rising sparkle followed by a distinct falling flake.

Element flag `0x10` sets runtime motion flag 8 in `0x00435F20`. Above the floor,
`0x004476E0` adds two sine-based XY sway displacements, in addition to normal
friction/mass motion. The first uses its full 2048-heading phase; the second
uses twice its phase masked to 1023. Their phases advance by 32 per view call.
A third phase advances by 64 and changes height by approximately
`-32 + 64*sin(phase)^2 - 32*cos(phase)` in position units, with native fixed
point rounding. At floor contact flag `0x10` stops motion, as established in
[room-construction-effects.md](room-construction-effects.md). Element 99's
flag 2 also shrinks scale toward initial scale times 819/4096 over its HP.
The view-particle constructor `0x004468E0` independently draws all three
initial phases from 0..2047; the phases are not initially zero or shared.

## 3. Parent movement modifiers

Before updating existing elements, `0x004386F0` calls whirlpool `0x00438520`
and circular-path `0x00438620` when their authored rates are nonzero.
For relative position `(dx,dy)` from the parent, the whirlpool's angle is
`(signedRate*16*128) / ((dx>>5)^2+(dy>>5)^2+128)`, clamped to -1024..1023.
The circular helper uses the signed rate itself, likewise clamped. Both
rotate that relative position and add sixteen times the difference to the
element's transient XY displacement accumulator. `0x004476E0` applies and
clears that displacement before applying the ordinary friction velocity.
They do not rotate or permanently change the element's velocity.
Parent linkage therefore remains necessary after the two emission waves
while its rising and replacement elements survive.

## 4. Presentation boundary

**Heights, 2026-10-14 (pending owner comparison).** The Heart object sits at its
tile's floor, 0 for the Heart's centre piece (`Tile_FloorHeight` `0x004545C0`), one
tile below the ring floor at `0x1000`. Element 98/100 therefore starts 3200 units
(0.78 tile) above the pit floor, 0.24 above the `Dungeon centre` mesh's top (0.54),
and with vertical speed 14336..16384, friction 393, mass 0 and 24 updates it climbs
about 2.0 tiles to peak at 2.66..2.92 tiles; a death element 99/101 with vertical
speed up to 8192 peaks at 2.98. The `DHeart Arches` mesh spans 0.54..2.73 above
the same origin, so the sparkles reach the arches' apex. A re-read of
`0x004366A0`, `0x00435F20`, `0x00436430`, `0x00435660`, `0x004386F0` and
`0x004476E0` (2026-10-14) found no scale, clamp or quantum that would start or
peak them lower; one 0x004476E0 step is taken per view call whatever the delta,
as ours does. An expired parent (HP 0 after two quanta) stays in the view list
and still updates its children through `0x00435600`: the cleanup loop `0x0043C041`
removes it through `0x00440530` → `0x004354F0` only once its child list is empty
(heart-intro.md §3), so no path shortens a sparkle's life.
**The owner then checked DK2 (2026-10-14): the sparkles originate down at the
Heart and dissolve at about the Arch Pillars' height, well below the arches'
apex.** The reading above does not explain that. A one- to three-step sparkle
life would (handoff at 0.99..1.36 tiles, flakes peaking 1.0..1.65 and settling on
the pit floor); so would a vertical scale or offset on the sprite draw side.
Candidates still unread: the vtable `+0x90` request callee (could adjust the
spawn Z) and the sprite draw path `0x0059C360`. The owner's second observation (same day): the cloud rises
steadily and pale flakes drift back down, so the lifecycle (24-step rise, then the
death flakes) is DK2's and only the height of the rise differs. Built as an owner
adjustment: `HeartSparkleStyle.PillarHeight` starts a rising sparkle inside the
Heart mesh (1024 over the Heart, not 3200) and scales its vertical speed to 622/1000
(ADR-0474, Q594); Native stays the default. The owner judged the first build's
origin "where the stairs end" and the second's still too high, and asked for it
closer to the Heart.


The shared construction particle model implements death-element replacement,
parent linkage after emission, the two parent modifiers and falling sway,
using the shipped definitions and additive sprite resources. Its view RNG
remains independent from simulation RNG. Effect 242's dynamic light and its
transition timing remain outside the current particle renderer.

`0x0043CC30`, the per-tile presentation pass, contains the original source.
For a class-2 object of type 13 with object byte `+0x92` bit `0x20` set, it
checks the shared wall-clock stamp at `0x006D632C`, initialized on the first
pending beat. Strictly more than 1000 ms since initialization or consumption
plays sound `0x150`, clears the object's bit and stamps now.
Then a nonzero room tag `+0x138`, nonzero RoomDef effect `+0x423`, and game turn
strictly above seven times ticks per second permit the effect request through
view-world slot `+0x90`. Source and target are both the Heart object's position
at `+0x1A`, owner is its `+0x28` tag, and heading is zero.
The shared timer means rendering multiple Hearts can arbitrate the one-second
gate. Animation helper `0x004B3C10` sets that bit when normalized phase reaches
4096, before wrapping the main segment. The Heart has no start/end segments,
so this is authored loop completion, not a distinct mana-payment callback.

This branch contains no positive-income or mana-cap test. The mana payment
`0x004C10E0` has no visual-effect creation; room update `0x004D9A60` requests
intro effects 223, 225 and 314 (the opening sequence, [heart-intro.md](heart-intro.md)). Room-type response `0x004E7FE0` has no Heart
type-5 arm. Do not attach effect 59 directly to mana payment.
