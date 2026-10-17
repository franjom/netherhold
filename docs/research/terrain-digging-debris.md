# Terrain destruction debris

## 1. Request and shipped definition — CONFIRMED

Primary source: DKII.EXE `0x00452F60` and `0x004F66E0`, reread on
2026-10-13, plus the installed Terrain/Effects/EffectElements KWD records.
On health reaching zero, a tile that becomes a different terrain requests
its source terrain's destruction effect. The request is at the tile centre,
Z4096, heading zero, with the working player as owner. Progress alone and
same-terrain regeneration do not request it. A zero effect id means none.

The loader copies file ushort `+0x1A6` through its shifted record `+0x25E`
to runtime `+0x1C9`. Rock (terrain 2) requests effect 6, Dig Rock; Gold (6)
requests effect 7, Dig Gold. Reinforced Wall (9) has no destruction effect;
it becomes Rock first. There is no separate Earth terrain in this database.

Dig Rock is generation type 4, flags `0x280`, parent HP1, twelve passes over
slots `[542,7,8,9,10,144]`: 72 particles, including 48 large mesh fragments.
Elements 7–10 use original Rock1–Rock4, art type 5; 542 is dust and 144 is
rockchip. The meshes have 32–64 HP, upward speed 2048–16384, mass3276 and
elasticity26214. Dig Gold similarly emits 60 particles, 48 of them meshes.

## 2. Tumbling and contact — CONFIRMED

Primary sources: `0x004386F0`, `0x00435F20`, `0x00436430`,
`0x00446C70`, `0x00435E70`, `0x004468E0` and `0x004B3610`.
The element constructor calls the particle constructor, whose render block
at `+0x26` is reset; pitch and roll begin at zero. The initializer installs
the supplied heading. Type-4 generation supplies an independent centred
orientation offset, scaling the definition's range by eight and wrapping
at 2048 units. Dig Rock's orientation range is 255.

After allocation/initialization, generation installs three independent
centred spin draws at `+0xEC`, `+0xEE`, `+0xF0`. A spin range of64 becomes
512 possible increments, `[-256,256)`, stored modulo2048. The update adds
one increment to heading `+0xE2`, pitch `+0x4A` and roll `+0x48` per view
call, while HP loses the elapsed 50ms count. Delayed calls do not multiply
the angular step. Floor bounce clears all three increments.

Initialization corrects floor contact before generation assigns spin, so
a birth contact can be followed by nonzero initial spin. Contact during
subsequent motion clears it. Existing motion and collision limits are in
[room-construction-effects.md](room-construction-effects.md).

## 3. Presentation boundary

Type-5 effect elements resolve through logical effect-element keys and the
existing install mesh provider. The renderer reuses cached KMF geometry and
materials, with per-element mesh particle groups and reusable submission
buffers. Sprite effects retain their existing path. Mesh orientation uses
the same coordinate mapping as objects; interpolation is view-only.
No original asset is copied into the repository. These requests and their
presentation RNG are neither saved nor hashed; gameplay remains version240.

## 4. Dust, progress and keeper color — CONFIRMED within the work chain

Primary binary reread 2026-10-13: whole dig action `0x00496E00`, mine
action `0x004971D0`, mining state `0x00465870`, action-point wrapper
`0x00496980`, tile-work applier and element initializer `0x00435F20`.
Neither strike handler directly requests a view effect. The applier's dust
request belongs to the different-terrain destruction arm, not health
progress. This establishes the work chain; it does not rule out particles
from an unexamined creature drawing or animation path.

The destruction call at `0x004532B9` passes the applier's working-player
argument, not the tile's previous owner. `0x004352E0` forwards the parent's
owner at `+0x12D` into element initialization. Shipped records differ:

| Destruction effect | Dust element; art | Raw art flags | Authored RGB | Upward speed |
|---|---|---|---|---|
| 6, Dig Rock | 542, Dig Rock Dust Element; sm, type 3 | `0x85` | 130,110,100 | 0..1638 |
| 7, Dig Gold | 6, Dust; sm, type 3 | `0x24` | 130,110,100 | 0..6144 |

Both dust elements have HP32..48, scale819..1638, scale ratio16384 and
element flags `0xD`: twelve rising, expanding, dimming sprites on destruction.
Both raw art flags lack mask`0x2`, so both dust elements keep authored
RGB130/110/100. The separate play-once mask`0x80` does not select keeper tint.
The canonical colour rule is in
[room-construction-effects.md section6](room-construction-effects.md#6-claim-capture-and-bed-creation).
The initializer does not consult the file's random/table color indices
in its color selection block. Claim Land's keeper-colored SM element142
is a distinct definition, not the rock dust element.

Current `TileWorkSystem` preserves destruction-only requests and the
worker owner. `RoomConstructionParticles` applies the raw art-color flag;
`ClassicConstructionEffects` submits its resulting sprite RGB and the
resolved resource. The previous red ordinary-rock cloud was caused by the
incorrect colour mask, corrected in ec92cfae; it is not native keeper colouring.
The remaining prominence comparison is Q593.
