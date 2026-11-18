---
systems: [creatures, camera-lighting, presentation-godot]
---
# Possession foreground attacks and creature filters

## Observations and resources

CONFIRMED owner play, 2026-11-18: the Vampire shows orange claw streaks, the
Skeleton and Black Knight a cyan blade streak, and the Mistress four red claw
streaks. Registered native evidence is under `F:\DK2Evidence\captures\visual\`:
`possession-vampire--2026-11-18--002`, `possession-skeleton--2026-11-18--002`,
`possession-mistress-party--2026-11-18--001` and
`possession-black-knight--2026-11-18--001`. The mixed-creature Vampire/Skeleton
sets ending `--001` are excluded.

STRONGLY INFERRED, binary and shipped-data reread: the table at `0x006A4668`
has an empty slot and 22 named resources loaded by [005074D0](functions/005074D0.md).
These are animated KMF meshes, not a persistent first-person hand or weapon rig.
The shipped Vampire (type8) uses Claw_Slash (2), Skeleton (9) and Black Knight (22)
use Sword_Slash (13), and Mistress (4) uses Mistress_Slash (10), all at75fps.
The per-level creature parser preserves overrides instead of hardcoding types.

[00492320](functions/00492320.md) and getters
[0056A1F0](functions/0056A1F0.md)/[0056A230](functions/0056A230.md) establish:

| compact definition | file offset | meaning |
|---|---|---|
| 67F,680,681 | F42,F43,F44 | three melee foreground indices; caller reads the first two |
| 682,683 | 1025,1026 | primary and secondary foreground indices |
| 684 | F45 | spell foreground index |
| 791 | CEC | foreground frame rate, ushort |

## Selection and time

STRONGLY INFERRED: [004100F0](functions/004100F0.md) checks the usual live,
frozen and action-exclusion conditions. A ready melee starts the first resource,
or alternates between the first two when the second is nonzero (the second is
chosen first). A ready ordinary spell starts the spell resource when nonzero;
group selection does not. Hypnosis Fire uses the secondary resource.
[0040FA90](functions/0040FA90.md) starts the primary resource for immediate
ability kinds3,6,7,9. The interface suppression flag still has no counterpart;
the remake's supported session input gate bounds delivery.

[005075A0](functions/005075A0.md) starts at phase0 with white colour, sets a
hold deadline of `now + 22000/fps`, and stores setup count22 independently of
the actual KMF frame count. [00507710](functions/00507710.md) draws the old
phase, then updates the phase for the next frame. It starts fading only after
the strict deadline, holds phase4095/4096, and lowers all four colour bytes over
500ms; elapsed501 clears the resource. [00507830](functions/00507830.md) stores
the same new phase in current and previous fields. Resource0 clears the block.
`PossessionForeground` mirrors this presentation-only clock and selection.

## Closing the screen transform

STRONGLY INFERRED: [005982A0](functions/005982A0.md)'s renderer vtable is
`0x00670574`; its slot+5C at `0x006705D0` points to
[0059B0C0](functions/0059B0C0.md). The type6 foreground arm calls
[00579A10](functions/00579A10.md), constructing a screen animated-mesh node via
[00577E60](functions/00577E60.md). [00577F10](functions/00577F10.md) transforms
a sampled vertex by the stored rotation, then adds screen origin to scaledXY;
Z has its own scale and is not used for perspective division.
The foreground's rotation is identity, origin is integer half screen width/height,
XY scale is integer `screenWidth*1000/640`, and layer is9. Therefore the ordinary
world camera, possession warp and distance fade do not transform the streaks.
The existing KMF decoder/sampler and HUD triangle list retain texture UVs and
additive materials. The Godot adapter only submits that draw list.

The remake draws the foreground before the creature filter and HUD, using full
screen width for the native scale at every aspect ratio. Native widescreen shape,
layer9 interaction with all overlay kinds, and animated texture phase last-bit
rounding remain unverified; no centered 4:3 restriction is invented for this mesh.

## Creature filter

STRONGLY INFERRED: [00410580](functions/00410580.md) selects type12->110,
13/28->107,22->108,25/30->109; type0 retains the previous resource and other types
clear it. [00410630](functions/00410630.md) submits the selected filter at origin.
CONFIRMED native Black Knight files show a curved black visor with a soft edge,
and the HUD over it. The draw-list and Godot panel order now both put filters
before the portrait/health panel, minimap and remaining HUD.
The existing mask image fills1600x1200 for a4:3 viewport. At16:9 it stretches
independently over the entire viewport, a remake fitting choice; these native
files establish no widescreen policy. The adapter comparison is recorded below.

## Adapter capture comparison

The parent inspected five clean D3D12 Movie Maker recordings under
`F:\DK2Evidence\captures\possession-review-20261118-001`:
`black-knight-4x3-d3d12/f00000162.png`, `black-knight-wide-d3d12/f00000162.png`,
`vampire-4x3-d3d12/f00000160.png`, `skeleton-4x3-d3d12/f00000162.png` and
`mistress-4x3-d3d12/f00000160.png`. The wide run is 1920x1080; the others are
1600x1200. Each has 200 frames at 30fps, exit0, no runtime/shader errors and
matching pre/post DLL hashes; adjacent metadata records exact commands and hashes.
They show one bright red target, cyan Black Knight/Skeleton slashes, four orange/yellow
Vampire claws and four red Mistress claws. The Black Knight visor has a soft edge
and sits behind the HUD. Compared with the registered native frames above, effect
types and shapes agree; phases, sizes and brightness differ. The selected Vampire
frame looks brighter/yellower. The staged campaign corridor differs from native
My Pet Dungeon; no pixel, phase or brightness parity is claimed. The earlier Vulkan
pilots are excluded from clean validation; see [the tool trap](../TRAPS.md).

## Verification boundary

Focused tests cover resource families in shipped data, hold/fade endpoints,
resource0 clearing, screen coordinate projection and HUD/filter ordering. Parser
round-trip tests cover the seven new fields at both record strides. Full project
suite counts, final adapter captures and their remaining visual limits belong to
the branch review report. No simulation state or snapshot field is added.
