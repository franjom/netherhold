# Hand cursor and hover feedback

## Sprite resources and state (CONFIRMED, binary and shipped data)

Read 2026-10-18: cursor loader `0x0040C710`, state update `0x0040AAE0`,
transition `0x0040ABC0`, software-cursor publication `0x0040ACD0`, and table
`0x0068F638` (52-byte records). The loader reads named PNGs from the sprite
archive. The reference install stores these uncompressed in `Data/Sprite.WAD`.
Frames are stacked vertically, in this order from the top:

| State | PNG | Frames | Frame width × height | Hotspot x,y | Frames/second |
|---|---|---:|---|---|---:|
| 0 | none | 1 | 0×0 | 0,0 | 1 |
| 1 | Point.png | 41 | 82×53 | 2,17 | 30 |
| 2 | SmallPoint.png | 1 | 42×20 | 2,2 | 1 |
| 3 | Idle.png | 1 | 83×39 | 2,4 | 30 |
| 4 | PickAxeHold.png | 1 | 84×86 | 4,51 | 1 |
| 5 | PickAxeTag.png | 1 | 88×65 | 7,30 | 30 |
| 6 | PickAxeHoldTagging.png | 1 | 88×65 | 7,30 | 1 |
| 7 | HoldThing.png | 1 | 74×70 | 8,42 | 15 |
| 8 | DropThing.png | 14 | 88×103 | 2,64 | 30 |
| 9 | HoldGold.png | 1 | 63×43 | -18,7 | 15 |
| 10 | DropGold.png | 16 | 87×95 | 5,59 | 30 |
| 11 | Slap.png | 15 | 126×99 | 2,64 | 30 |

Width/hotspot-X scale by physical screen width/640; height/hotspot-Y by
screen height/480, with integer truncation. The loader resamples the complete
sheet to scaled width × (scaled frame height × frame count). The draw source
rectangle is `(0, frame*scaledHeight, scaledWidth, (frame+1)*scaledHeight)`;
the scaled hotspot is submitted separately. Native sprite presentation is
therefore screen-relative, not a world mesh. The height-based modern HUD may
choose a uniform scale, but that differs from DK2's separate axis scaling.

The loader sets a green colour key `0xFF00FF00`. Checked Point, Idle and
PickAxeHold PNGs are RGBA; their pure-green pixels already have zero alpha.
Preserve the supplied transparency through an asset abstraction.

The ordinary timer uses `floor((nowMs-startMs)*rate/1000)`. On reaching the
frame count it invokes the completion callback, resets start time to now and
frame to zero, then invokes the entry callback. Generic `0x0040E3C0` adopts the
requested state and resets time/frame when leaving a state. `0x0040E300`
completes state 5 into state 6; it is not a continuously swinging dig cursor.
The optional random-frame mode exists but is not the ordinary timer.

## Where the hotspot lands (CONFIRMED, 2026-10-23)

Read for the owner's report that our Hand feels "too far down and right" of the mouse.

- **DK2 (CONFIRMED, binary).** `0x0040ACD0` submits surface, frame rectangle and the scaled hotspot through `0x005B2BD0` -> `0x005BB2A0` -> `0x005DBAC0` -> `0x005DC120`. That function stores `-hotspot` as the draw offset (`+0x40/+0x44`). `0x005DC580` places the sprite at `pointer + offset`. So the top-left is `pointer - scaledHotspot`, and the hotspot pixel sits on the pointer.
- **The pointer source (LIKELY).** The point comes from a mouse source read through a vtable in `0x005DBB10`. That source is unread, so "it is the same point the world picker uses" is LIKELY, not CONFIRMED.
- **The art (CONFIRMED, shipped data).** The hand is drawn down and right of its hotspot on purpose. Point.png frame 0 has its hotspot (2,17) on a transparent pixel; its claw tip is at (6,33), which is (+4,+16) frame pixels from the hotspot. The claw reaches the hotspot only around frames 24-32 of the 41-frame loop (leftmost pixels (3,19), (0,12), (0,14)).

**Ours (CONFIRMED, code).** `HandCursor.Place` uses the same rule, with uniform `H/480` scaling (ART_DIRECTION §13). The Unity UI Toolkit panel (ScaleWithScreenSize, 1024×768, match height) resolves its scale as `H/768` from the game view or display size (Unity 6000.6 `PanelSettingsUtility.ResolveScale`). `ClassicHandCursor.Draw` divides by the same factor, so it adds no offset. `ScreenToTile` picks at `Input.mousePosition`, which is the hotspot, as in DK2.

Claw-tip offset from the pointer in screen pixels (Point frame 0):

| Screen | DK2 rule | Ours |
|---|---|---|
| 2543×974 | (+17, +33) | (+8, +32) |
| 2560×1440 | (+16, +48) | (+12, +48) |
| 1024×768 (DK2) | (+6, +26) | (+6, +26) |

The DK2 column is ±1 px: the resampler's pixel centres are unread, and `0x0040C710`'s
hotspot division is a signed `idiv`, which truncates toward zero. The art was re-checked on
2026-10-23 by decoding Point.png (Adam7-interlaced RGBA): frame 0 has alpha 0 at (2,17) and the
claw tip at (6,33) (the finger column is 6-7); frame 28's finger reaches (1,12). The hotspot pixel itself is at offset 0 in both. Vertically ours matches DK2. Horizontally ours is the same or closer, because DK2 stretches the sprite by `W/640`. No placement defect was found.

**Open (owner choice, not decided).** Keep DK2's click point (the hotspot, up-left of the claw), or move the click point to the claw tip as a deliberate deviation from DK2. No code was changed.

## Channel selection and hover eligibility (CONFIRMED subset)

Constructor `0x00402670` initializes three channels through `0x0040AAC0`:
base at interface `+0x12DA` starts at state 1, middle `+0x12F6` and contextual
`+0x1312` start at zero. `0x0040ACD0` selects nonzero contextual, then nonzero
middle, then base. Thus contextual zero falls back to the animated Point.
Hidden-HUD/presentation gates can publish a null cursor; modal semantics remain
separate from the supported ordinary dungeon view.

**The GUI pointer (2026-10-19, LIKELY).** The middle channel takes tool icon ids
(0xC, 0xD), not hand states. State 2 `SmallPoint` goes into the contextual
channel at `0x0040BCB6` and `0x0040BEFE`, when interface `+0x12B2` is non-zero
and `+0x10C` is zero, behind gates `0x0044D620` and player `+0x19A` == 0.

`+0x12B2` is written as follows:
- set to 1 at `0x00403D1C` for event 7 (unidentified);
- cleared when interface `+0xF5` is 0;
- otherwise set to 1 when `0x0052BFC0` on `+0x10C6` hit-tests the pointer against the visible widget list (screen coordinates scaled by 0xA00/0x780);
- failing that, set to 1 when `0x0041FDF0` hits one of the `+0x4E7D` icon rectangles (26-byte entries, count `+0x4F09`), and cleared otherwise;
- also cleared at `0x0040588B`.

That makes it a pointer-over-GUI flag. The widget list's coverage of the whole HUD is unchecked. Whether a held item overrides state 2 over the HUD in DK2 is unread; ours lets the held item win.
The owner sees the small pointing finger over the minimap corners and tabs; the
adapter uses it over the whole HUD in Hand mode.

For ordinary Hand mode, `0x0040AE80` retains transient states 5 and 11.
Otherwise `0x0040C1E0` tests a live creature/object through pickup permission:
success selects state 3. If it fails, target kind 1 with target-flags bit 0
selects state 4 and clears interface flag `+0x132E & 8`; otherwise it selects
contextual zero. This distinguishes dig-hover from the one-frame tag states.
`0x0040C290`'s solid-terrain hover validation is a separate predicate, not
the complete dig-cursor eligibility rule.

Picker `0x005992B0` sets tile target flag bit 0 from resolved terrain Taggable
bit 3, or an eligible secret-door branch. It intersects different native
planes, applies presentation visibility/terrain gates, first tries rendered
hit helper `0x00572820`, then calls `0x0044DEC0` as a fallback. Some implicit
register arguments in the picker remain unresolved; do not turn this reading
into an invented complete fog or ray-depth policy.

**The rendered hit (CONFIRMED, read 2026-10-23).** Only meshes enter it. The static and
animated mesh draws append themselves to a 256-entry screen list (`0x00769A78`: x, y, radius
squared, item) when they carry a thing tag ([0x00572820](functions/00572820.md)), and
[Thing_DrawRenderInfo](functions/0059B880.md) passes the tag only for: a creature whose definition
has CanBePickedUp (`+0x686` bit 1) and is not dying (`+0x3E6` bit 21), while the sink's `+0xD1` is
clear (unread); an object whose definition has CanBePickedUp, CanBeSlapped or Highlightable
(`+0xF4` & 0x400300); any door or trap. Dead bodies and sprites never enter. `0x00572820` offers
each listed item whose circle holds the pointer ([0x00588A60](functions/00588A60.md)) twice: first
to its triangle test ([0x00585AD0](functions/00585AD0.md), static twin
[0x00582D30](functions/00582D30.md)), which keeps the least mean vertex depth over the triangles
under the pointer in one winding; only when no triangle is hit, to the centre test, the nearest
mesh centre. Terrain is not in the list. The winner then becomes an entity hit only by its
owner (read 2026-10-23, [0x005992B0](functions/005992B0.md)): a creature always (kind 2 own, 3
other); a door (5) or trap (7) only the viewer's, another keeper's leaving the tile hit with no
fallback; an object (4) by an ownership test not yet built; a dead body (6) never in practice,
since none is listed. See world-hover-text.md and Q634.

Fallback `0x0044DEC0` scans the clipped 3×3 neighbourhood row-major and each
tile's thing list. Distance is `max(abs(dx),abs(dy))+min(abs(dx),abs(dy))/2`.
Ordinary mode takes the first eligible creature strictly within half a tile,
rejecting definition byte `+0x36F == 0x2E`, flag `+0x3E6 & 0x200000` and states
`+0x25E` 0xD6/0x4C/0xD1. Otherwise it keeps the nearest eligible object
strictly within a quarter tile (definition flag `+0xF4 & 0x100` or object id13).
A creature supersedes the object candidate. Object-only mode instead searches
eligible objects within half a tile. This is not arbitrary same-tile picking.
Built (2026-10-23) as `HoverPickRules` (both stages' eligibility) and `RenderedPick` (triangle hit
over centre) in src/DK2.Presentation/HoverPickRules.cs, used by `DungeonScene.PickHoverEntity`.

## Implemented cursor subset and limits

`HandCursor` and `ClassicHandCursor` use the original Point, Idle and
PickAxeHold sheets, with uniform height-based scaling under ART_DIRECTION §13
instead of native per-axis integer scaling. The runner captures hover state
before the worker tick starts; drawing reads retained presentation data only.
Pointer hiding is restored on focus loss, disable and disposal.

The adapter's rendered stage offers each eligible creature, object, door and trap whose renderer
bounds the ray crosses before the terrain plane, tests the posed mesh's triangles (either winding,
exact ray distance rather than DK2's mean vertex depth), and falls back to the nearest bounds
centre along the ray. Bounds stand in for DK2's screen circles, whose radius is unread; terrain
still occludes, where DK2's list holds no terrain. The fallback sweep applies the native
exclusions; thing-list order within one tile is not reproduced. Held-item cursor
channels and transient tag/slap states 5/11 remain unimplemented; contextual
Point/Idle/PickAxeHold selection does not change existing click commands.

## Rectangle publication and remaining outline boundary

`0x0040BBE0` resets interface rectangle `+0x132F/+0x1333/+0x1337/+0x133B`.
The ordinary Hand held-item arm in `0x0040AE80` fills it as
`(tileX,tileY,tileX+1,tileY+1)`, with invalid/drop-special flags separately.
The early dig-hover arm does not fill that rectangle. `0x0040ACD0` publishes
it through `0x0044E130` into bridge `+0x10..+0x1C`, and through `0x0044E1C0`
into `+0x4E..+0x5A`. Each call replaces the current bounds; the previous ones go
only to `+0x24..+0x30` / `+0x62..+0x6E`, through `0x005B7090` (unread) after the
first call. Both store the hover thing id
at bridge `+0x34`; bridge `+0x36` receives interface flag bit `0x10` as 0/1.
Every other caller passes a zero rectangle, so the secondary equals the primary here.
The tool arms that fill this rectangle (room/sell only while dragging; doors,
traps and spells never) and the tool icon, No Go and price overlay drawn by
`0x0040CAE0` are in [placement-cursor.md](placement-cursor.md) (2026-10-23).
With both rectangles empty, the one-tile cage under the pointer is the Hand
light `+0x33E`'s own kind3 art, which `Presentation_Update` shows (edge word
`0x1F`, or `0x3F` with `+0x36`). See placement-cursor.md §2a (2026-10-23).

## Selection outline rendering

CONFIRMED (2026-10-18, binary): `0x0043CC30` uses the primary rectangle,
falling back to the secondary when the primary is empty. Bounds are half-open.
For each boundary tile it moves bridge handle `+0x35D` to the tile centre at
height zero, then sets edge bits north=1, south=2, west=4, east=8, plus `0x10`;
bridge `+0x36 != 0` adds `0x20`. It submits through `0x00599C60`, then
`0x0059B880`: RenderInfo type7, procedural kind3, reaches `0x005A01A0`.

Kind3 constructs a 27-point grid using `0x0059F3C0`, with half-tile XY steps
and initial Z base -2. Its height bias is -0.05. The HighWalls levels are
-2.05, -1.55, -1.05 in renderer coordinates (positive Z points downward).
With terrain deformation enabled it applies the same terrain subpoint,
lean and wibble builders; these initial levels are not final world heights.
The endpoint table initialized by `0x0059F760` draws upper and lower perimeter
edges and vertical corners, each split at its midpoint. Thus it follows the
wall shape rather than filling the ground tile. The segment's required mask
must be wholly present in the tile edge mask.

Point index is `layer*9 + row*3 + column`. Each row below lists endpoint pairs
and their required mask (hex); `0x10` enables the lower edge/corner cage.

| Pairs | Mask |
|---|---|
| 0-1, 1-2 | 01 |
| 18-19, 19-20 | 11 |
| 2-5, 5-8 | 08 |
| 20-23, 23-26 | 18 |
| 6-7, 7-8 | 02 |
| 24-25, 25-26 | 12 |
| 0-3, 3-6 | 04 |
| 18-21, 21-24 | 14 |
| 0-9, 9-18 | 15 |
| 2-11, 11-20 | 19 |
| 8-17, 17-26 | 1A |
| 6-15, 15-24 | 16 |

`0x0059DD30` emits crossed textured ribbons around each segment, using radius
0.03 tile units and both triangle windings. RGB is `(pulse,0,0)` normally,
or `(0,0,pulse*0.61328125)` with bit `0x20`. The shared wall-clock pulse
changes by 0.004 per elapsed millisecond, reverses after strictly crossing 0
or 1, and clamps at those endpoints. It is updated after the kind3 draw,
not per simulation tick.
The texture handle comes from render sink `+0x8E`; its resource and final
blend/depth state remain UNKNOWN. Keep this separate from the persistent
dig-tag mesh tint in [tag-highlight.md](tag-highlight.md). The one-tile hover
cage is most likely the Hand light's (placement-cursor.md §2a). Its visibility flag is
interface vtable slot `+0x20`, `0x004096B0` (CONFIRMED, 2026-10-23): in Hand mode it shows
over a dig-taggable tile (blue) or with a full hand and no drop rectangle, and never over
plain floor with an empty hand.

What the Hand points at is tinted red by the hover-id rule of `0x0040C290`, and a hovered
gold object shows its `_HIGHLIGHT` texture; additive and alpha-blended materials get no red
([gold-hover-and-sparkle.md](gold-hover-and-sparkle.md) �1-2).
Ours shows the pickup hand over own creatures and loose gold and the pointing cursor over
doors, traps and store piles (owner, 2026-10-23).

Implementation: `TileHoverOutline` retains the full-height rest grid, endpoint
table, radius and blue peak. `ClassicTileHoverOutline` emits one camera-facing
quad per segment in a retained mesh. Native emits two crossed textured ribbons
with both windings; native UVs run 0..1 across the ribbon. Ours blends additively
with a bright-centre profile `centre^2.15` across that width (a third wider than
the cubic first tried, owner 2026-10-19), so the 0.06-tile band reads as a thin light line; both are matched to the owner's DK2 screenshot (2026-10-19,
"too thick and too blue" against the earlier opaque band), not read. The adapter samples the
existing terrain lattice at the unbiased layer positions, then applies the
-0.05 height bias; its reuse of the terrain sampler remains an approximation
until compared against the native procedural grid builder. The pulse draws
the previous level, advances only on active draws, clamps overshoot and retains
the native strict endpoint reversal.
