# Door and trap drawing

Read DKII.EXE v1.70 with Ghidra 12.1.4 and the shipped KWD/KMF files,
2026-10-10. CONFIRMED unless a boundary below says otherwise.

## 1. Art selection and facing

The door definition loader `0x004A7100` maps these file resources to runtime arts:

| file slot | runtime offset | use |
|---|---|---|
| 0 | `+0x36` | the barricade's static model |
| 4 | `+0x51` | opening clip |
| 5 | `+0x6C` | closing clip |

The constructor `0x004A5070` takes `+0x36` for flag `0x40`, otherwise
`+0x6C`. Orientation zero stores heading `0x200`; other orientations store zero.
The update `0x004A55F0` selects `+0x51` to open and `+0x6C` to close.
Shipped barricades have static art 0 and empty slots 4/5; ordinary doors have
empty art 0 and named opening/closing clips. The view uses each clip's final
frame for its settled pose; the static barricade uses its one mesh in both states.

The trap definition loader `0x004AA440` puts file art 0 at `+0x36`.
`0x004AA310` returns that art; constructor `0x004A7940` supplies it to the model
setter, through definition getter `0x004A7B10`. The trap's stored heading is
already exposed by the simulation. Model yaw conversion is
[object-drawing.md §4](object-drawing.md#4-position-and-rotation).

## 2. Placement and visibility

The authored loaders `0x00520F40` (door) and `0x00520E50` (trap) supply the tile
centre with height zero. The constructors call `0x004B5560`, the thunk to
`0x004B5020`, which raises the position to the tile floor. Floor heights are
documented in [004545C0](functions/004545C0.md); the view reuses its furniture
floor-height selector.

`0x004A6B70` draws completed doors for everyone and construction only for the
local owner. Trap reveal is [traps.md §11](traps.md#11-disarm-and-reveal-q451-q455).
Visibility follows `0x004A9F10`: a foreign hidden trap is omitted, and construction
is owner-only. Fog still gates both structure classes.

## 3. Presentation boundary

Door/trap registries allocate independent ids, so scene identities include the
class. Snapdragon's barricades are door THINGS, not trap THINGS. Its rendered
structures now request the actual shipped models; presenting does not mutate
the simulation. No map-specific decorations substitute for them.

Remaining: door transition/death clips, trap firing clips, secret-door disguises,
linked door objects, construction tint/blinking and locks' visual markers.
The classic Unity view freezes door clips at the settled endpoint; the converted
GLB retains the clip and a future renderer must select that endpoint itself.
