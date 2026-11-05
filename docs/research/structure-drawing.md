---
systems: [doors-traps, art-assets]
---
# Door and trap drawing

Read DKII.EXE v1.70 with Ghidra 12.1.4 and the shipped KWD/KMF files,
2026-10-10. STRONGLY INFERRED unless a boundary below says otherwise.

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
other linked door objects, and the blueprint's kind-1 branch (section 5).
The classic Unity view freezes door clips at the settled endpoint; the converted
GLB retains the clip and a future renderer must select that endpoint itself.

## 5. Blueprint drawing (2026-11-05)

A door or trap with construction nonzero is drawn as its own model with every material swapped
for one flat colour. STRONGLY INFERRED from DKII.EXE unless marked.

- **The override.** Each turn the door update `0x004A55F0` calls `0x004A6B70` while construction
  (`+0x109`) is nonzero; the trap twin is `0x004A9F10`. Both pass a world material to
  `0x004B4580`, which stores it at RenderInfo `+0x73`, sets `+0x4D` bit 1 and a period of 0 (every
  frame), and set the tint `+0x48..+0x4A` to 255. A built door clears the override and tint 128.
  `0x0059D4B0` turns bit 1 into draw flag 0x100, and the static draw `0x00580EC0` then draws every
  primitive of the model with that one material and its render state, in place of the model's own
  (draw `+0x50` bit 0x100 tested at `0x0058122E`, material from draw `+0x64` at `0x00581237`; the animated draw `0x00584900` does the same from its `+0x4C` and `+0x70`). The geometry, and so the silhouette, stays.
- **The material.** `0x005099A0` fills `+0xA36B` RedTexture and `+0xA36F` BlueTexture as kind 3
  (`AA_`), `+0xA373` RedTexture and `+0xA377` BlueTexture as kind 1 (`S_`, string `0x006A4AE4`
  BlueTexture). Kind 3 is additive ONE/ONE, no depth write, depth test kept, colour forced white
  and lighting bit 0x40 cleared ([0057C420](functions/0057C420.md),
  [tag-highlight.md §6](tag-highlight.md#6-the-overlay-material-and-the-blend-2026-10-10-follow-up)).
  BlueTexture is flat (63, 64, 255), RedTexture (255, 64, 65). So a plan adds a flat blue to
  whatever is behind it, with no shading of its own. Back faces are culled in software
  ([kmf-materials.md §4](kmf-materials.md#4-culling-is-in-software-for-every-material)), but the
  model's front faces that overlap each add again: two layers give (126, 128, 255), four nearly white.
  HYPOTHESIS: this is the light lavender with near-white folds the owner sees in DK2.
- **The kind-1 branch.** The global `0x00759AFC` (bss, read only by these two functions; no writer
  found by a full `.text` immediate scan) selects the kind-1 materials instead. UNKNOWN when it is
  set; it is zero at load, so the kind-3 look is the normal one (HYPOTHESIS).
- **The blink.** If the owner's player `+0x1121` is zero, the colour follows the game turn
  (`0x00517490`) parity: a door is blue on even turns and red on odd, a trap the reverse.
  `Player_Tick` (`0x004B9EC0`) sets `+0x1121` each turn to whether the player owns a room of type 10,
  the Workshop (`0x004C5FD0`, `0x004B9ECB..0x004B9EE6`), before its early return for the neutral and hero players. So a plan blinks red and blue, one turn each, until its keeper
  has a Workshop, then stays blue.

Ours: `WorldView.PlanTint` gives each blueprint's `StructureView.Plan` (Blue or Red) from the
Workshop test (any owned Workshop tile, refreshed once per turn) and the tick parity. A view
reads `CurrentTick` after the tick that ran DK2 turn `CurrentTick` (`GameSimulation` passes
`CurrentTick + 1` as the turn, then increments), so the parity is that of the last turn run. `DungeonScene` swaps the model's
materials for the shared `AA_BlueTexture` or `AA_RedTexture` array (`ClassicMeshes.Blueprint`,
the dig tag's own overlay materials) and, on a blink, swaps the array in place rather than
rebuilding the model. Not modelled: the kind-1 branch, and the second override `0x004A6B70`
gives the linked thing at door `+0x103`.

## 6. A built door's base light (Level6a, 2026-11-05)

Level6a's wooden door at (35,26) drew almost black in ours and well lit in DK2. STRONGLY INFERRED
from DKII.EXE.

- **The tint.** `0x004A6B70` with construction zero stores 0x80 at thing `+0x8C..+0x8E`, which is
  RenderInfo `+0x48..+0x4A` (RenderInfo is thing `+0x44`; the disassembly sets `EDI = ESI + 0x44`). A
  blueprint stores 0xFF; the trap twin `0x004A9F10` stores 0xFF for a plan and 0 otherwise.
- **Where it goes.** The door's art is an animated clip, drawn by `0x0059C880`, which passes the
  RenderInfo tint, each byte divided by 255, to `0x00583120`; that stores it x255 at draw `+0x40..+0x48`.
  `0x00584900` adds the global ambient to those words (flag `+0x4C` bit 2 clear) and then the flag-4
  radial light (`0x0057C080`). `0x005836A0` starts every vertex at material brightness plus those words
  and adds the sampled instance lights after. So a built door has half a channel of base light, 128 of
  255, whatever point lights reach it.
- **Why ours was black.** Ours lit the door by point lights and the Unity fill only. At Level6a's door
  the nearest lights are terrain lights 1.0 tile away at height 1.5 with radius 1.125, which the
  sampled test at the door's origin does not reach, and a closed door's own tile has no terrain light
  (`0x00452DC0` skips a door tile).
- **Built.** `StructureView.InstanceTint` is `WorldView.BuiltDoorTint` for a built door, open or closed;
  `DungeonScene.CreateStructure` adds it as emission through `ClassicMeshes.InstanceTinted`. Not
  verified against a DK2 frame: the size of the effect, and whether the door's neighbouring floor
  differs.
- **Sight is not the cause.** A creature two tiles from the closed door explores the door's own tile
  (`0x0044E7F0` tests the slots before the cell closes them), as the Level6a test shows.
- **Open divergence.** The torch and terrain-light door tests (`0x00452D80`, `0x00452DC0`) call
  `0x005092F0` with the third argument 0, the navigation-nibble test, so an open door or a blueprint
  does not exclude its tile, and ours (`WorldState.DoorAt`) excludes any door.
