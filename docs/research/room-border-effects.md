# Room and neutral-Portal border flames

DKII.EXE v1.70, primary Ghidra reads and shipped Effects/EffectElements records,
2026-10-17. The owner's screenshot/recollection prompted the investigation; the
mechanism below comes from the binary and data, not that recollection.

## Border producer — CONFIRMED

[Room_BorderEffectTick](functions/004E7CD0.md) runs in the common room tick
([Room_Tick](functions/004E4B00.md)), after the type-specific handler permits it.
It requires definition byte`+0x43E` mask`0x10` (the room's HasFlame flag) and a
nonzero owner tag at room`+0x1D`. The byte is file flag bit 12 (flags sit at`+0x43D`
in file order): every shipped room carries it except the Dungeon Heart, the hero gates, the
Hero Stone Bridge and the Mercenary Gate (2026-10-21); it is not a Portal-only effect.

The room has **one** contour cursor: phase`+0x81`, signed step`+0x85`, tile
coordinates`+0x87/+0x88`, orientation`+0x89`.
[Its initializer](functions/004E7CA0.md), called by
[Room_StartWithTile](functions/004EC5A0.md), starts at the first slab, orientation3,
phase0 and step800. Each room tick advances the phase. Crossing4096 subtracts
4096 and follows the16 five-byte entries at`0x006A22F8`, testing candidate tiles
for both the same room id and owner. It skips through matching interior tiles
until the contour is found; this is not a bounding-rectangle animation.

On odd game turns it requests **effect20** at the cursor, heading0 and the room
owner. Its point uses native Z4096; the travelled edge coordinate is clamped to
512..3584 within the tile and the perpendicular coordinate is512 or3584.
At nominal four game turns/second, births are0.5seconds apart and phase travel
is800/4096 tile per turn, before contour turns and edge clamping.

## Art and the two tongues

CONFIRMED shipped data:

| Definition | Art / flags | Lifetime | Linked output |
|---|---|---|---|
| Effect20, Flame Round Room | additive sprite `gtorch`, art`0x67`; effect`0xA0`, generation0 | HP20 | NextEffect118 |
| Effect118, Room Flame Data | no parent art; effect`0x280`, generation4 | HP1 | three element265 particles per update |
| Element265, Flame round room element0 | additive `gsparkle`, art`0x84` | HP24 | authored white RGB255/255/255 |

Effect20 has zero vertical speed and scale range0..1228. Element265 has
horizontal speed−1228..1228 and upward speed1638..3072 in native units.
Next-effect chaining creates118 at the same point and owner; the existing
chain/lifetime research is in [heart-intro.md](heart-intro.md#3-how-one-instance-begets-another-second-reader-2026-10-14).

STRONGLY INFERRED: the observed pair is successive persistent flame instances,
not two independent contour runners. HP20 consumes nominal20×50ms=1second,
while the room requests a flame every0.5seconds. Thus roughly two overlap in
steady motion. Exact visible overlap still needs a timed original-game capture.

## Keeper colour and multicoloured unclaimed Portals — CONFIRMED

Effect20's art mask`0x2` is set. [ViewEffect_Initialize](functions/004347E0.md)
selects the owner palette when that mask is set; an owner matching the neutral
selector takes the cycling getter instead. The getter
[World_CyclingPlayerColor](functions/0050F880.md) selects palette Number
`floor(wallMilliseconds/100)%7+1`. The neutral selector and palette RGB are
already established in [construction effects section6](room-construction-effects.md#6-claim-capture-and-bed-creation).

Each flame samples colour **at birth**, so differently coloured flames coexist
around a neutral-owned Portal. These are the same border effect and owner-colour
path, not a separate all-colours Portal effect. The cycle includes palette
Numbers1 and2 as well as keepers3..7; it does not test who may claim the Portal.
White element265 sparks are additional to the coloured flame.

## Built (ADR-0524, 2026-10-21)

`RoomBorderFlameSystem` keeps one cursor per HasFlame room in `WorldState` (hash 291) and asks
for effect 20 on odd turns. A room's new cursor starts at its lowest tile index in place of the
first slab (INFERRED). The view draws effect 20's own sprite: `0x004347E0` flag 0x20 sets its
scale from MinScale0 toward MaxScale1228 over HP/2 and sets the return bit, and `0x004B3B40`
turns it back to 0 at the same step (122): it peaks at update 11 and dies at 20 a fifth its
size. Art flag 2 tints it with the palette colour without element clamping. The chained 118
emits three element265 sparks per update. Not built: the type handler's veto of the tick and
the parent animation's start phase. Claim mist's separate pale-colour investigation is
[section6.1](room-construction-effects.md#61-pale-claim-mist-investigation).
