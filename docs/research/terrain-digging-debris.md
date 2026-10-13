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
