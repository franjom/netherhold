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
advances by 256 each animation update. With animated art flag `0x80`,
`0x004B37D0` sets render-block `+0x4C` bit 2; `0x004B3AD0` then clamps
texture phase to its endpoint and zeroes its step instead of wrapping.
The reset `0x004B3610` initializes that endpoint (`+0x3C`) to 4096;
the ordinary element initializer retains it.
Without that bit it wraps. Static art without flag 4 does not advance.
This is independent of the particle's remaining HP: Portal capture elements
145/146 hold the last texture while alive, rather than replaying their cloud.

The paused Unity particle submission retains a positive lifetime fraction at
that endpoint, so texture completion does not remove a still-living particle.
Native material selection clamps phase 4096 to its final stored texture.

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

## 6. Claim, capture and bed creation

**CONFIRMED binary and shipped data (2026-10-12).** These requests use the
same view-effect interface and type-4 generator as construction. A work
strike is not an effect trigger: only the completed changes below emit.

| Change | Request source | Effect | Original position and heading |
|---|---|---|---|
| Dirt Path becomes Claimed Path | `0x00452F60`, after max-health terrain conversion | source terrain's max-health effect; shipped 5 | tile centre, Z 4096, heading 0, working player |
| Rock becomes Reinforced Wall (terrain 9) | `0x00452F60`, at max-health conversion | source terrain's max-health effect; shipped 11 | west/north/east/south adjacent tile centres, Z 0, heading 0, working player |
| Whole room changes owner, including Portal capture | `0x004E5650` | 8 on each slab in its existing chain | each tile centre, Z 0, heading 0, new owner |
| Creature creates its bed | `0x0048AD50`, after object creation succeeds; caller `0x004E03F0` | Lair definition's effect, shipped 17 | tile centre, Z 0, newly drawn bed heading, creature owner |

Terrain loader `0x004F66E0` maps file `+0x1A4` through intermediate `+0x25C`
to runtime `+0x1C7`. Room loader `0x004EACC0` maps file `+0x384` through
intermediate `+0x458` to runtime `+0x423`. The Lair's file value is 17.
The bed request precedes room registration: failed object creation emits
nothing; a later registration failure does not undo its already requested effect.
Tile-owner setter `0x004559D0` emits no effect. Claim action `0x004974C0`
emits completion sound, while the terrain applier owns the visual request.

Reinforcement was rechecked on 2026-10-13. Action 7 (`0x00497960`) applies
work but does not request a per-strike effect. In the applier, destination
terrain 9 selects four calls at `0x00453445`, `0x004534A7`, `0x00453509`
and `0x00453567`. Their XY points are the four adjacent tile centres and
height is zero; non-wall max-health conversion retains its centre/Z4096 call.
Same-terrain health repair does not enter this conversion-effect branch.

Shipped effect 11, Reinforce Wall, is type 4, flags `0x280`, HP1, twelve
passes over elements `[144,145,146,147]`: 48 particles per admitted request,
192 over four requests. It has zero origin radius and height offset512.
Elements144/147 use grey rockchip sprites (type2, RGB125); 145/146 use
sm/SM sprites (type3): 145 is keeper-coloured (`0x86`), while146
uses authored RGB210/126/60 (`0x84`). Particle lifetime
is 24..32 for 144..146 and 32..48 for147. Art80 completion follows section4.
The shared particle consumer already supports these resources; the omitted
reinforcement producer is restored in TileWorkSystem. Visual parity is pending.

The three parent effects below have no art, zero mass/friction/elasticity and
zero horizontal/vertical speed. Their nominal scale is 1024. Parent initial
HP is inclusive; fixed bounds below introduce no random draw.

| Effect | Flags | HP | Per-update emission | Origin radius; height offset |
|---|---|---|---|---|
| 5, Claim Land | `0x480` | 2 | 16 elements, each chooses uniformly between 142 and 143 | 0..735; 0 |
| 8, Claim Room | `0x280` | 1 | 16 of 145 and 16 of 146 | 0..2207; 0..1023 |
| 17, Lair Bed | `0x80` | 4 | 32 of 162 and 32 of 163 | 0..367; 0 |

Radius is in 4096-per-tile position units with uniform radius, as in section 3.
`0x004347E0` immediately invokes `0x00446C70`: below the queried native
floor, these zero-elasticity parents settle exactly at floor height.
Generation uses that current position; Z 0 is not an instruction to place
bed or capture particles below the floor. Element birth uses the same floor helper.

`0x004386F0` uses parent flag `0x400` to select one generation slot per
particle; `0x00435E30` draws inclusively through the last nonzero slot index
cached by loader `0x004358C0`. Without it, all nonzero slots emit in reverse
order. Parent flag `0x200` is not established here as a color control.
Element flag `0x200` bypasses its allocation visibility test (`0x004352E0`).

The initializer does not emit these type-4 waves. `0x00434E80` generates
before subtracting elapsed 50ms quanta from parent HP (section 5). At delta 1,
effects 5/8/17 therefore request 32/32/256 particles over 2/1/4 updates.
An intact 3x3 Portal requests nine independent effect-8 instances, 288 particles.
Late scheduled updates can consume several HP units and reduce wave count.
Effect 81 retains one wave of 64 particles. Its immediate modern birth emission
boundary is recorded in section 5 and is not evidence of original birth timing.

| Element | Sprite; raw art flags | HP | RGB source |
|---|---|---|---|
| 142 | SM; `0x86` | 16..24 | keeper color |
| 143 | sparkle-02g; 0 | 24..32 | 255,255,255 |
| 145 | sm; `0x86` | 24..32 | keeper color |
| 146 | SM; `0x84` | 24..32 | 210,126,60 |
| 162 | SM; `0x84` | 16..32 | 80,20,60 |
| 163 | SM; 4 | 16 | 70,80,130 |

All are additive sprites (type 3). Raw art flag `0x2` selects keeper color
in `0x00435F20` (`0x00436332`), replacing authored RGB and
clamping each component to at least 48; authored RGB bypasses that clamp.
`0x00504D80` preserves file flags, ORing only the loaded-resource bit0.
Art flag`0x80` controls play-once animation in a separate runtime field; it
does not select colour. Plug dust381 (`0x85`) and382 (`0x24`) retain authored
RGB100/100/100. Alpha starts at255. Elements
142/145/162/163 carry `0xD` (destroy on contact, scale up, dim RGB), while
143/146 carry `0xC`. Fade percentage 100 makes lifetime alpha threshold zero.
Physics, scale and normalized sprite animation follow sections 3-5.

Keeper RGB is variant 0 of the palette initialized by `0x004C04C0`, whose
four-byte entries are written by `0x004C06D0`. `0x0050F830` looks up the
player's Number (`+0x10FC`), not its thing tag, and indexes `0x006E5058`
with 12 bytes per number (three variants). Invalid player/number returns
number 1, variant 0.

| Player Number | Native RGB before minimum-48 clamp |
|---|---|
| 0 | 0,0,0 |
| 1 | 191,213,224 |
| 2 | 255,255,255 |
| 3 | 230,50,50 |
| 4 | 50,78,230 |
| 5 | 50,230,51 |
| 6 | 226,230,50 |
| 7 | 230,50,212 |

`0x00435F20` also compares the element owner against component slot `+0xC4`.
Equality selects `0x0050F880` instead: palette number
`floor(wallMilliseconds/100)%7+1`. CONFIRMED startup writer (2026-10-16):
`0x00447D87` and`0x00525702` pass the neutral-player global through slot`+0xC8`;
`0x00440F90` forwards to renderer`+0x9E`, whose setter`0x00443110` writes`+0xE`.
Its getter`0x00443100` is shared by the renderer interfaces. Thus neutral-owned
elements with art flag`0x2` select the cycling palette after startup initialization.
Elements without that bit, including the plug dust, bypass both palette getters.
The Heart startup closure contains no palette-enabled elements; see
heart-intro.md, Startup element colours (Q605).
Live Level1 sampled the selector as2 and the96-byte palette matched the table;
evidence is ignored`reverse-engineering/step5-native-level1-palette.json`.
Color is sampled at birth; this later sample alone does not prove birth-time colour.
