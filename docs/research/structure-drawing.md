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

## 4. Door lock attachments

`0x004A7100` copies file byte `0x247` to runtime definition `+0xB2`:
the lock object type. Shipped Wooden, Steel, Braced, Magic, Secret and Hero
Portcullis doors select objects 17, 104, 105, 106, 107 and 137 respectively;
both barricades select zero. Those objects' primary arts are `Door_ChainWooden`,
`Door_ChainPortcullis`, `Door_ChainRound`, `Door_ChainMagic`, `Door_ChainSecret`
and `Door_Chain_HeroPortcullis`. The chain and padlock are one authored mesh.

In `0x004A55F0`, construction and living barricades return before the marker
work. Mode 3 with no picked-lock countdown forces closed and requests the
object; a running countdown removes it. The object factory `0x004B7A90`
receives the door's position and owner. The update copies the door heading
to the object's current and retained headings; there is no added offset or
scale. The object initializer `0x0049B1C0` installs its primary art.
Ordinary completed foreign doors have no owner-only marker gate.

The classic view requests the lock through a door asset variant and renders
it as a child at the same origin and yaw. It requires completed, alive, closed,
mode 3 and no picked-lock countdown. Secret-door marker creation clears visibility
when tile byte `+0xB` bit 0 is set; that disguise flag is not modelled here, so
Secret attachments are conservatively omitted. Modes 1/2 have creature-dependent
marker requests in the update but no reachable lock writer (see
[doors-and-traps.md](doors-and-traps.md#door-runtime-behavior)); they remain unpresented.

Remaining: door transition/death clips, trap firing clips, secret-door disguises,
other linked door objects and construction tint/blinking.
The classic Unity view freezes door clips at the settled endpoint; the converted
GLB retains the clip and a future renderer must select that endpoint itself.
