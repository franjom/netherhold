# Room construction particles

DKII.EXE v1.70, primary binary read 2026-10-12 through Ghidra 12.1.4.
Binary conclusions below are **STRONGLY INFERRED**, pending a controlled
original-game visual comparison. Shipped record values are **CONFIRMED data**.
No decompiler text or third-party implementation is reproduced.

## 1. Construction source

`0x004C7380` requests effect 81 after a tile passes its placement/cost test
and the call to `0x004C5C30`. It does not inspect that builder's return before
requesting the effect. Rejected tests produce no effect. Arguments to world
slot `+0x38C` are the effect id, player tag, position, zero heading and output.
Position is `(x*4096+2048, y*4096+2048, 6144)`: tile centre, height 1.5 tiles.
The builder itself contains no visual-effect creation. Its additional
presentation request is the terrain's sound after a paid build on a later turn.

Single-tile and drag input use this same wave. Its already established
geometry and input queue belong in [room-slab-chain.md](room-slab-chain.md),
section 2. `RoomBuildWaveSystem` now emits creation events on that original
diagonal timing.
Direct computer/scenario builder commands do not emit the player wave effect.

World slot `+0x38C` is `0x00509580`: it duplicates the position as both initial
and current position and forwards six arguments to slot `+0x388`.
The subsequent view bounds/solidity/visibility gates and effect allocation
are documented in [keeper-spell-tremor.md](keeper-spell-tremor.md), section 3.
These are view objects; their random stream is separate from simulation RNG.

## 2. Shipped definitions

Reference files: `Data/Editor/Effects.kwd` and `EffectElements.kwd` in the
reference install. Existing KWD readers supply field names and offsets.

Effect 81, Create Room Block Effect:

| Field | Value |
|---|---|
| Own art | none |
| Flags | `0x80` |
| Min/Max HP | 1 / 1 |
| Generate ids | 204, 144, then six zeroes |
| Generation type / elements per turn | 4 / 32 |
| Min/Max XY speed | 2048 / 2048 |
| Min/Max vertical speed | 0 / 0 |
| Inner/outer origin range | 0 / 16 |
| Lower/upper height | 40 / 80 |
| Orientation / sprite spin range | 256 / 64 |
| Mass / friction | 0 / 0 |
| Next/death/collision effect ids | all zero |

| Element field | 204, Create Room Block Element 0 | 144, Reinforce Wall Element 0 |
|---|---|---|
| Art name/type | mist / 3 | rockchip / 2 |
| Art flags | `0x24` | `0x1` |
| Sprite width/height | 4096 / 4096 | 4096 / 4096 |
| Declared texture animation count | 16 | 0 (static) |
| Min/Max HP | 12 / 16 | 24 / 32 |
| Min/Max XY speed | 0 / 6144 | 2867 / 4096 |
| Min/Max vertical speed | 0 / 819 | 8192 / 12288 |
| Mass / friction / elasticity | -122 / 130 / 6553 | 1228 / 0 / 32768 |
| Min/Max scale | 1228 / 2047 | 204 / 409 |
| Scale ratio | 10240 | 0 |
| Element flags | `0xD` | 0 |
| RGB | 160,134,110 | 125,125,125 |
| Fade percentage | 100 | 100 |
| Death/collision/next ids | all zero | all zero |

Type 2 selects an alpha sprite; type 3 selects an additive alpha sprite.
It does not mean that type 3 alone enables texture animation.

## 3. Emission and movement

`0x00434E80` advances effect age, performs motion, invokes its generation
handler, then decrements positive lifetime by the caller's delta. Dispatch
table `0x00697669`, entries five bytes apart, selects `0x004386F0` for type 4.
For effect 81's flags the handler makes 32 passes. Each pass walks all eight
generation slots backwards and requests every nonzero element: rockchip,
then mist. Thus an emission requests 32 of each, 64 total. Bounds, solidity,
view visibility and allocation can reject individual element creation in
`0x004352E0`. The handler keeps existing elements updated even after emission
stops, through `0x00435600`; the complete parent cleanup policy is unread.

The type-4 origin conversion multiplies inner/outer radius by 23, and
lower/upper height by 16. `0x00435660` draws angle in `[0,2048)`, radius in
`[inner*23,outer*23)`, height in `[lower*16,upper*16)`, using uniform radius,
not uniform disk area. It adds those offsets to the parent's current position.
For effect 81 this is radius 0..367 and height 640..1279, in 4096-per-tile units.
`0x00435720` draws heading offset in the converted orientation range about zero.

Parent initialization `0x004347E0` chooses its speeds inclusively. Heading zero
uses trig table values 0 and 65536 at `0x006C1488` and `0x006C1C88`, yielding
XY velocity `(0,-2048)`. The first motion step before emission therefore moves
the parent `(0,-128,0)` in tile-position units: minus 1/32 tile on original Y.

The definition loaders `0x004358C0` and `0x004366A0` narrow the relevant
file dwords directly to runtime signed shorts. Element initializer
`0x00435F20` selects horizontal speed, vertical speed and HP with inclusive
upper bounds; its velocity angle is uniform over 2048 headings. Scale uses
an exclusive upper bound. It installs RGB and alpha 255, and sets the
fade threshold to `HP - floor(FadePercentage*HP/100)`.

Motion helper `0x004476E0` subtracts runtime mass from vertical velocity,
then multiplies all velocity components by `(4096-friction)/4096`, truncating
toward zero. Position advances by velocity shifted right four bits. Therefore
one raw velocity unit means 1/65536 tile per update, not 1/4096 tile per update.
The helper also handles world collision. Negative mist mass supplies buoyancy.
The wall-slide helper remains unaudited; floor response is described below.

Floor placement helper `0x00446C70` queries world slot `+0x204`. At or above
that floor it continues. Below it, element flag 1 destroys the particle;
mist therefore disappears on floor or wall contact, whereas rockchip survives.
For a surviving chip, height becomes floor plus reflected penetration times
elasticity/65536; horizontal velocity multiplies by that coefficient. Vertical
velocity reverses with the same coefficient, then an upward speed below 8192
becomes its square shifted right 13; below 131072 it instead takes the 247/256
damping arm. All spin increments clear on floor bounce. Element flag 16 would
stop motion at floor height; neither construction element carries it.

`0x00436430` subtracts delta from element HP before its motion step. Flag 8
dims RGB by `(remainingHP-1)/remainingHP`, with integer truncation; alpha
fades only below a nonzero threshold. Both shipped fade percentages make
that threshold zero, so no lifetime alpha fade is established. Element flag
4 targets initial scale multiplied by ScaleRatio/4096 over HP; mist therefore
grows toward 2.5 times its initial scale. Flag 2 takes precedence over flag 4.
`0x004B4160` and `0x004B41A0` store a signed-short target and a constant
signed-short increment `(target-current)/HP`, truncated toward zero once.
`0x004B3B40` adds that increment and clamps only when crossing the target;
expiry can occur before the exact target is reached. Recomputing a floating
linear interpolation each frame does not preserve this integer stepping.

## 4. Sprite animation and stored textures

`0x00504D80` compacts sprite descriptor width, height, flags and the low
byte of its declared frame count. `0x004B37D0` uses art flag 4 to enable
texture animation and flag `0x20` to choose a random initial phase. For a
16-frame descriptor its normalized phase starts at `random(16)*256` and
advances by 256 each animation update. `0x004B3AD0` wraps looping phase at 4096.

`0x0059C360` sizes the sprite from descriptor width/height times render scale,
passes RGB/alpha and normalized texture phase to `0x0057F1E0`.
`0x0057F7E0` selects a material frame from normalized phase multiplied by
the material's own frame count (`material+0x15`), clamped to its valid range.
The descriptor count governs duration; it does not require equally many textures.

The reference `EngineTextures.dir` has `mist0` through `mist7` at MM0/MM1/MM2,
and `rockchip` at MM0/MM1. No `mist8` through `mist15` entries occur. Thus
eight stored mist images can render the 16-update cycle without inventing
images or requiring missing textures. **STRONGLY INFERRED:** use normalized
phase over the available contiguous frames. The material loader's exact
resource enumeration and missing-frame policy are not established here.

## 5. View clock and remaining boundaries

**CONFIRMED (binary, 2026-10-12):** effect lifetime uses wall-time quanta of
50 milliseconds, nominally 20 updates per second. The complete presentation
update is `0x0043B220`; its earlier Sight-only name described one branch.
At `0x0043BF3D` it obtains milliseconds from `0x005B5E90`, compares them to
saved time at view `+0x1323`, and updates only when `now > saved + 50`.
It passes `floor((now-saved)/50)` to `0x00434E80` at `0x0043BFF7` and advances
saved time by that count times 50, retaining the remainder.

The shared millisecond getter is established in
[animation-transitions.md](animation-transitions.md), section 3. Its initializer
`0x005B5D70` independently confirms the performance-counter divisor is frequency
per 1000 units: these quanta are milliseconds, independently of simulation
turns and renderer frame rate.

Type-4 generation `0x004386F0` forwards this count to `0x00435600` before
emission; `0x00436430` subtracts it from element HP. Nominal lifetimes after
creation are therefore 0.6-0.8 seconds for mist and 1.2-1.6 seconds for chips,
subject to collision, render scheduling and the strict boundary above.
Applying HP to the four-turn simulation made both five times too long.
Texture phase, scale increments, color and ordinary motion advance once per
scheduled update. A delayed frame can consume several HP units at once;
additional motion substeps are mode-dependent, so repeating every operation
for that count is not established as original lag behavior.

Built: `RoomConstructionParticles` rebases its injected millisecond clock at
creation, retains the strict 50ms gate and remainder, subtracts batched lifetime
and advances ordinary motion, texture and scale once. Unity samples unscaled
wall time and interpolates positions using this clock's own fraction. Burst
creation remains immediate on the accepted event rather than waiting for the
next global effect gate; this leaves up to one gate of emission-phase difference.

**UNKNOWN:** all visibility/collision edge cases and a measured original-game
particle count/trajectory comparison. The owner's comparison found the
four-turn implementation lasts longer, but did not measure seconds. Logical
sprite keys resolve through the asset provider; original filenames remain
inside that provider.
