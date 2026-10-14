# Dungeon Heart health visuals

Read from DKII.EXE on 2026-10-13. CONFIRMED describes the binary's arithmetic;
the complete damaged appearance still needs a matched original-game comparison.

## 1. Health selects pulse and height

`0x004DA200` changes Heart health and recomputes its presentation unless the
keeper's DungeonHeartIsInvulnerable bit is set. The signed health percentage is
truncated and clamped to 0..100 against variable 174. Stage is `(100-p)/5`,
integer division: 21 stages, not ten-point steps.

| stage | health boundary | table 0x006A1930 | vertical percent 0x006A1948 |
|---|---|---|---|
| 0..10 | 100,95,90,85,80,75,70,65,60,55,50 | 50,55,60,65,70,75,80,85,90,95,100 | 100,105,110,115,120,125,130,135,140,145,150 |
| 11..20 | 45,40,35,30,25,20,15,10,5,0 | 90,80,70,60,50,40,30,20,10,0 | 138,126,114,102,90,78,66,54,42,30 |

Effective FPS is `max(1, table*3/5)`, truncated: 30 at full health, 60 at
half health, then falling toward 1 at zero. There is no attack-duration latch
in this selector. Negative damage also plays sound 0x329 and posts the attack
notification; regeneration runs this same visual recalculation.

The phase increment written to thing +0xDC (RenderInfo +0x98) is the 12.12
quotient of 1 by `(70*ticksPerSecond)/FPS`, retaining both fixed-point divisions,
capped at 4095. The setter immediately calls `0x004B3AD0` once with the Heart's
RenderInfo: this adds the new rate to its phase. The ordinary object update
`0x0049BD20` independently advances once per tick, so each non-invulnerable
health-setter call contributes an extra advance, including map setup, damage
and regeneration; merely changing the presentation rate omits that advance.
It changes the vertical
scale (thing +0x4C / RenderInfo +8) to `verticalPercent*4096/100` and clears the
two scale ramp steps. The scale axis is model Y; see object-drawing.md section 5.

Evidence: `reverse-engineering/.cache/heart-health-asm.txt`,
`heart-tables-full.txt`. The owner's observation that attack speeds the pulse
agrees with the upper-half health response; further health loss slows it again.

## 2. Health selects authored material textures

The health setter writes both RenderInfo +0x30 and +0x34 to
`min(4095, (100-p)*4096/100)`. It does not write the RGB tint bytes.
`0x0043C240` returns +0x34; `0x0059C880` passes that value divided by 4096 to
`0x00583120`, which stores it at animated draw +0x58.

`0x00584900` passes draw +0x58 to material texture accessor `0x00581B80`.
That accessor multiplies phase by the material's frame count (+0x15), adds
1/1024, subtracts the authored float 0.49999, adds 12582912, stores to float,
extracts its low 23 mantissa bits and subtracts 0x400000. It clamps the index
to 0..count-1. This implements the engine's biased floor through float rounding;
retain the bit conversion for exact boundary comparisons. Texture access is
`MIP + 4*(frameCount*variation + frame)`, with four MIP handles per frame.

Colour loss therefore uses the authored texture sequence, not an invented
desaturation or tint curve. The shipped `Dungeon centre.kmf` has one material, `DHeartTopLftDHeart`, flags
0xC0, with five textures: `DHeart`, `DHeartDecay1`, `DHeartDecay2`, `DHeartDecay3`,
`DHeartDecay4`. They select at health boundaries 80, 60, 40 and 20 percent,
subject to the accessor's rounding. The WAD decompressed length and MAT2 reader
layout were independently checked. A matched damaged original-game comparison
remains outstanding.

Evidence: `reverse-engineering/.cache/heart-prep.txt`,
`heart-texture-select.txt`; the ordinary animation draw and constructor were
independently re-read via the live Ghidra server.

## 3. Separately loaded decay resources

`0x005099A0`, at 0x00509F00, loads `DungeonCentre_DECAY0` and
`DungeonCentre_DECAY1` as meshes and wraps them as type-6 resources. The wrappers
describe respectively 100 and 70 frames, both at 30 FPS. A full-section raw
offset census finds their base +0x9E76 only in construction and loading.
Their live selector remains UNKNOWN; loading does not prove the ordinary Heart
draw uses either. Health texture selection above is independently established.

Evidence: `reverse-engineering/.cache/heart-load.txt`,
`heart-decay-offsetraw2.txt`. No source or identifiers from Flame were reused.
