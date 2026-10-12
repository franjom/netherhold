# How DK2 draws a placed object

**Status (2026-10-10, claude):** draft. Read from DKII.EXE by decompile and disassembly;
each claim names its function. Confidence words as in AGENTS.md. The object thing's
field names are in `object-thing-object.md`; the definition loader is `0x0049DD60`.

## 1. The draw path

**CONFIRMED.** The per-tile presentation pass `0x0043CC30` walks both thing chains of the
tile and, for each thing whose vtable `+0x18` and `+0x34` tests pass and which is in sight
(or has thing `+0x90` bit `0x10`), calls the render sink's slot `+0xEC` (sink vtable
`0x00670574`, so `0x00599C10`) with the tile and the thing.

`0x00599C10` asks the thing for two pointers through its vtable — slot `+0x54`
(`0x004B5D50`, returns `&thing+0x102`) and slot `+0x2C` (`0x004B5D40`, returns
`&thing+0xF6`) — and calls `0x0059B880` on the thing's RenderInfo (`thing+0x44`) with
`&+0xF6`, `&+0x1A`, `&+0x102`, `&+0xF4` and the thing. It then calls `0x005953D0` (not read).

`0x0059B880` is the draw dispatcher. It draws nothing unless the installed runtime art
(RenderInfo `+0x28`) has runtime flag bit 0 (loaded) **and** RenderInfo `+0xAF` bit 0 is set
(the draw bit; thing `+0xF3`). It then switches on the art's type byte (`+0xA`):

| type | routine |
|---|---|
| 5 (static mesh) | `0x0059CCF0` |
| 6 (animated mesh) | `0x0059C880`, then `0x004B4550` if flag 4 |
| 4 | `0x0059D010` (not read) |
| 1, 2, 3 with runtime flag `0x100` | `0x0059C720` (not read) |
| 1, 2, 3 otherwise | `0x0059C360` (not read) |
| 7 | `0x0059C2A0` then `0x005A01A0` (not read) |

An art with runtime flag `0x40000` is skipped entirely while the global `0x00759ADC` is 0.

## 2. The art slots

The file record has nine 84-byte ArtResources at `+0x20` (name 64, flags u32 at `+0x40`,
12 value bytes at `+0x44`, type at `+0x50`, bytes `+0x51`/`+0x52`). The runtime definition
has **eight** 27-byte slots, filled by `0x00504D80` (`0x0049DD60`, **CONFIRMED**):

| runtime slot (def offset) | file art | use found |
|---|---|---|
| 0 (`+0x00`) | #1 | GUI icon by its data (`gui\rooms\Treasury`); use not traced |
| 1 (`+0x1B`) | #4 | not traced |
| 2 (`+0x36`) | #3 | **the in-hand mesh** (§3) |
| 3 (`+0x51`) | #0 | **the floor art**, installed by `Object_Init` |
| 4–7 (`+0x6C`, `+0x87`, `+0xA2`, `+0xBD`) | #5–#8 | state art, `0x0049BD00(n)` = slot n+4 |

File art #2 (`ObjectIcons#N`) is not loaded into any slot by `0x0049DD60`. When a slot's
flags have bit 8 or bit `0x10`, the loader also loads `<name>Start` / `<name>End` (strings
`%sStart`, `%sEnd` in the binary) into slot `+0x13` / `+0x17`. For ObjectId 8 (Torture
Wheel) and 61 (Electric Chair) the floor art's name gets a `g_` prefix unless
`0x00759ADC` is 1.

## 3. Which art is drawn

- **Default: file art #0.** `Object_Init` (`0x0049B1C0`) installs def `+0x51` into the
  RenderInfo through `0x004B37D0` (scalar `0x1000`, animation id 0, immediate). **CONFIRMED.**
- **Gold on the floor (ids 1 Gold, 2 Gold Bag): one mesh, scaled by GoldHeld** (§5). No art
  switch. **CONFIRMED** (`0x0049C760`).
- **Treasury and Heart gold (Gold Pile 1): art by fill.** `0x004F4440` (it requires def
  `+0xF4` bit `0x40`, which Gold, Gold Bag and Gold Pile 1 all carry), called by the three
  store/deposit routines `0x004F3C60`,
  `0x004F4090`, `0x004F4510`, takes the tile cap — `MaxGoldPerDungeonHeartTile` (1000) for a
  room of type 5 with `+0x9A` bit 0 clear, else `MaxGoldPerTreasuryTile` (3000) — and
  installs: GoldHeld < cap/3 → file art #5 (`Gold Urn1`); < 2·cap/3 → #6 (`Gold Urn2`);
  else #0 (`Gold Chest`), each only if that slot is loaded. Treasury: 0–999, 1000–1999,
  2000+; Heart: 0–332, 333–665, 666+. #7 `Gold Chest2` is not chosen here. **CONFIRMED.**
- **In the hand:** the hand's pick-up visual `0x0040D670` installs def `+0x36` (file art #3)
  into the hand's own RenderInfo for an object without def flag `0x40`; a creature gets its
  definition's art 11 instead (`0x00494860(0xB)`). The floor mesh is not what the hand
  draws. **CONFIRMED.** An object *with* flag `0x40` (the three gold objects among them)
  gets no art from this routine; what the hand shows for gold: not read.
- **Chicken, coop, egg, book case, training dummy, beds:** §8.
- **Call To Arms (id 10):** `0x0049C6A0` and `0x0049BC00` test type 10 and install file art
  #7 or #5–#7 by process state; the process-state handlers `0x0049FBA0` and `0x0049FDC0`
  install #5 and #6. Live selectors are state12 floor0, states13/14 art5,
  state15 art7, and states20/19 art6. Post-load restoration differs for19/20
  (art7). The activation and removal handlers wait for ended/wrapped art flags;
  state14 keeps its loop while recalling. **CONFIRMED**, 2026-10-11.
- **Dungeon Heart (id 13):** its art #0 is `Dungeon centre`, type 6, 70 frames at 30 fps,
  flags `0x201`. `Object_Init` installs the floor resource with scalar 4096;
  `0x004B37D0` stops looping only for flag `0x80`, absent here. Rechecked in the
  binary on 2026-10-11. The classic Heart now plays this loop (ADR-0437), for both
  room decorations and represented objects; arches and steps remain static.
  Its real decoded vertices differ between phases 0 and 0.5. No health-dependent
  rate or decay selection is implemented. The binary also has the string
  `Unable to allocate 'Dungeon Heart Object Decay' mesh number %d`: **not followed**.

## 4. Position and rotation

**Position: CONFIRMED.** `0x0059C2A0` builds two points, one from thing `+0xF6/+0xFA/+0xFE`
and one from the position `+0x1A/+0x1E/+0x22`, each plus the RenderInfo offset words
`+0x51/+0x53/+0x55` (zeroed on install), divided by 4096, the third component negated. The
object is drawn at **its own coordinates, not the tile centre**, in 12.12 fixed point
(`0x1000` = one tile). `Object_Init` copies `+0x1A..+0x24` into `+0xF6..+0x100`.
STRONGLY INFERRED: `+0xF6` and `+0x102` are the previous turn's position and heading, and
the engine interpolates between the two.

Height is whatever `+0x22` holds; the draw adds nothing. A resting object sits at its
tile's floor height (`Tile_FloorHeight` `0x004545C0`: `0x1000` on ordinary floor, Hatchery
`0xE00`, Temple 0, Graveyard/Crypt `0xF00`, Combat Pit `0x400`, bridges `0x1199`) because
`Object_FallAndLand` (`0x0049EF90`) drops it until it reaches that height. STRONGLY INFERRED.

**Rotation: CONFIRMED.** Yaw comes from the heading word thing `+0xF4` (and `+0x102`):
radians = −(h + 0x400) · π / 1024, so **2048 units per turn** with a half-turn offset.
Under the row-vector matrices below and the `(x, −z, −y)` frame conversion, Unity yaw is
**+(h + 0x400)**, pitch is negative and roll positive. The half-turn is required: at Prison
heading 0x400 the shipped door and bar face the south cell entrance with identity rotation.
Two more RenderInfo words, `+0x20` and `+0x24`, are passed as angles × −π/1024; `+0x22`
is not used by the mesh draw. The hand's hover jitter `0x004B4430` keeps all three within
`0x7FF`. `Object_Init` zeroes `+0xF4` and `+0x102`.

**Axes and order: CONFIRMED** (2026-10-10). RenderInfo is thing `+0x44`, so thing `+0x64` is
`+0x20`, `+0x68` is `+0x24`, and thing `+0x95/+0x97/+0x99` are the offsets `+0x51/+0x53/+0x55`.
`0x00580480` builds per-axis matrices with `0x0041C580` (axis 0 X, 1 Y, 2 Z), each angle
interpolated between previous and current, and multiplies them with `0x00594CB0`
(out = B·A) into M = S · Rx(`+0x20`) · Ry(`+0x24`) · Rz(yaw), S = diag(`+4`, `+8`, `+4`)/4096.
Vertices are row vectors (`0x00594DB0`: v·M, used by `0x00580EC0`). So a mesh is scaled, turned
about its own X by `+0x20`, then about Y by `+0x24`, then yawed about the vertical Z. The engine
frame has Z down (height negated); a model's floor is z 0 and its top negative z. The
`+0x51..+0x55` offsets are added to the point in world axes, unrotated, height before the
negation (so a positive `+0x99` lifts).

Applied (mesh bounds read from `Meshes.WAD`): `Torture_Wheel_Wall` is a disc of radius 0.364 in
model X–Z, 0.036..0.11 thick along +Y. With `+0x64 = 0x600` (−1.5π, row-vector y' = z, z' = −y)
the floor wheel lies **flat**, 0.04..0.11 tile above its point, and its heading spins it about
the vertical. A wall wheel (`+0x64 = 0`) stands upright, lifted half a tile by `+0x99 = 0x800`,
and state `0x1E` turns `+0x68`, the disc's own axle.

## 5. Scale

**CONFIRMED.** The mesh draw uses RenderInfo `+0x04` and `+0x08` (thing `+0x48`, `+0x4C`)
divided by 4096, as (`+4`, `+8`, `+4`) — 4096 is 1.0. Which world axis gets `+8`: UNKNOWN.

- Installing a type-5 art (`0x004B37D0`) sets both to the art's value at file `+0x44`
  (runtime `+0`) unless RenderInfo `+0x4E` bit `0x40` is set. Every type-5 object art read
  (Gold Pile, Gold Bag1, Gold Chest, the Urns, Anvil, Skull, …) holds **4096**.
- **Gold and Gold Bag:** `Object_AddGold` sets both to `0x800 + floor(GoldHeld / 2)` while
  GoldHeld < 5000, else `0x11C4` (4548) — a scale of 0.5 at zero rising to 1.11 at 5000,
  continuous at the cap — and zeroes the ramp steps `+0x14/+0x18`.
- **Chicken:** `Object_Init` sets both to `0x1000 + random(0x800)` — 1.0 to 1.5 — before
  installing its type-6 art, which does not reset them.
- `0x004B3B40` (every animation tick) ramps `+4`/`+8` toward `+0xC`/`+0x10` by the steps
  `+0x14`/`+0x18`; who sets a ramp for an object: not traced.
- The definition's Width/Height (`+0xF8`/`+0xFA`) are **not** read by the draw path above.

Tint and alpha: bytes RenderInfo `+0x48..+0x4A` (RGB) and `+0x4B` (alpha) are passed ÷255.

## 6. Resource flags the draw tests

Runtime flags = file flags | (type ≠ 0). Proved by use (names only where the binary has them):

| bit | where | effect |
|---|---|---|
| `0x1` | `0x0059B880` | art loaded; not drawn without it |
| `0x4` | `0x004B37D0`, `0x004B3AD0`, `0x0059CCF0` | texture animation: phase `+0x34` advances by `0x1000/frames` (type 5: frames = file `+0x48`) and is passed to the mesh draw |
| `0x8` | loader, `0x004B37D0`, `0x004B3C10` | start segment `<name>Start` played first |
| `0x10` | loader, `0x004B3C10` | end segment `<name>End` played after the main loop |
| `0x20` | `0x004B37D0` | random starting texture phase, `random(frames) · 0x1000/frames` |
| `0x80` | `0x004B37D0` | with type 6 or flag 4, sets RenderInfo `+0xAD` bit 3, which the dumper names **StopLooping** |
| `0x100` | `0x0059B880` | types 1–3 drawn by `0x0059C720` instead of `0x0059C360` |
| `0x200` | `0x0059CCF0`, `0x0059C880` | ORs `0x4000` into the engine flags |
| `0x400` | `0x004B37D0` | 50% chance to swap in an art from a creature's definition (`+0x370`, `+0x46E`) |
| `0x40000` | `0x0059B880` | skipped while `0x00759ADC` is 0 |

Bits `0x2` and `0x40` are **not tested** anywhere on the object draw path read here;
whether something else tests them: UNKNOWN.

## 7. Animated objects on the floor

**CONFIRMED.** A type-6 floor art plays on the floor: the object update calls the
RenderInfo tick `0x004B3AD0`, which runs `0x004B3C10` for type 6. Install resets the phase
`+0x94` to 0, so the main loop starts at **frame 0** (the random-start bit affects only the
texture phase, §6). The phase is a 0..`0x1000` fraction of the segment, advanced per turn by
the rate from `0x004B41E0` (frames, fps at runtime `+6`/`+4`, turns per second). At the end
it wraps if `+0xAD` bit 0 is set and StopLooping is not; otherwise it plays the end segment
(flag `0x10`) or holds the last frame and installs the queued art (`+0x2C`), if any. The
type-6 draw `0x0059C880` blends the current and next frame by the phase.

## 8. Furniture, eggs and chickens

Object process states run their handler from the table `0x0049E600` fills (state *s* at
`0x006E4B3C + s·0x14`). The art each one installs, read 2026-10-10:

**Sleeping in a bed (CONFIRMED).** Creature state 83 (`0x0046FA60`) does the presentation
for every sleeper, and only when the creature's own art 12 (`0x00494860(0xC)`) is loaded and a
creature vtable `+0x278` call on the bed (at `0x0046FAF9`, meaning unread) returns 0:
- *Ordinary bed:* the bed's draw bit is cleared (the bed is hidden) unless the creature's
  LairObjectId (`+0x771`) is 6, the Hero Creature Bed. The creature stays drawn and runs
  creature action 24 (its own sleep clip). On waking, both draw bits are set and the creature
  is put at the bed's own position plus an offset of at most one unit (a 16.16 table entry at
  angle `(−410·h) & 0x7FF` shifted down 16, no radius; `0x0046FD71..0x0046FDD9`), not in front
  of it.
- *Mistress bed (object 23):* on the first turn it installs the bed's file art #7
  `door_opens` and moves the Mistress onto the bed. When that clip ends it sets her draw
  bit, clears the bed's, and starts action 24. On waking it goes to state 84.
- **State 84 is `0x0046FE90`**: the turn handler in table entry 84 (`0x0047D15C` stores
  it; entry 83 holds `0x0046FA60`). It is not a category enter/leave hook. On its first
  turn (`+0x1C == 1`) it plays #7 again with the bed drawn and the Mistress hidden. When
  the clip ends it shows her, reinstalls the bed's #0 and sets her next state. Only the
  Mistress bed reaches 84 (`lair-acquisition.md`).
- **A bed's #5 swing:** no bed-specific installer was found. The generic "struck"
  state 18 (`0x004A13F0`) would install any object's #5. Beds also hold the swing in file
  #3, the in-hand art the hand installs (§3), so a bed in the hand swings. Whether a bed's
  #5 ever plays on the floor: UNKNOWN.

**Book Case (64): CONFIRMED.** `Room_Rebuild` (`0x004EE6A5` arm) picks n = 0..2 from the
tile-coordinate selector and installs def + (n+4)·0x1B: file art #5, #6 or #7
(`book_case1..3`). It stores n at `+0x14A` and sets the heading to `(y & 1) << 10`.
Books and research play no part. #0 and #8 `book_case4` are not chosen there.

**Coop (35) and Egg (47), CONFIRMED.** `Hatchery_LayEgg` (`0x004DE620`, `hatchery.md` §3) has two paths.
- *Coop path, the room full:* coop state 29 and egg state 24; the lay routine sets both.
  - State 29 (`0x004A1BD0`) installs the coop's #5 `Coup-Wobble(Give_Birth)` (77 frames
    at 15 fps), then #0 when it ends.
  - The egg is created hidden. State 24 (`0x004A21C0`) waits 4 s (4 × turns per second).
    It then shows the egg at z `0x13E8`, heading = the coop's + `random(0x100)` − 0x80, and
    launches it (0x6000 along the heading, 0x3000 up). In flight it adds 0x40 per turn to
    three RenderInfo angles. Landed (|vx|+|vy| ≤ 0x1000), it goes to state 32.
- *Free-tile path, capacity left:* the egg goes straight to state 32 with #0.
- State 32 (`0x004A1CF0`): while #0 `Egg-Hatch_Start` (45 frames at 30 fps) is installed
  and RenderInfo `+0xAD` bits 1–2 report it ended or wrapped, it installs #5
  `Egg-Hatch_End`. On the next turn, with any other art installed, it creates the Chicken
  and destroys the egg, with no wait for #5 to finish. CONFIRMED read; worth checking by eye.
  Whether bit 2 is ever cleared after the first wrap: not checked.

**Chicken (9).** State 8 (`0x004A24A0`) shows #5 `Eat`, or #6 `Scared` when `+0x152` is
set, for random(16)+8 counts, then #0 `Walk`. State 25 (`0x004A17E0`, no type test) flees
with #6 and returns to #0 when slowed. Nothing read installs #7 `Idle`: UNKNOWN.

**Training Dummy (14): CONFIRMED.** The training blow (`0x00498CA0`) sets state 18
(`0x004A13F0`). It installs #5 `Recoil_back` if loaded (sound 0x311 for type 14), spins the
heading with a decaying speed until below 0x40 per turn, and reinstalls #0 when the clip ends.

The same pattern elsewhere: object 109 states 44/45 (#0, #5 played once, toggled by
`0x004E35C0`); the Prison door object 116 states 46/47 (#0, toggled by `0x004E3680`);
object 95 states 48/49 (#5, #6 while playing, set by `0x004D6890`).
The classic Prison door view retains its opening stamp and plays #0 once
when the simulated door opens; its closing lifecycle is not yet modelled (ADR-0438).

**Object update order (CONFIRMED, 2026-10-11).** `0x0049BD20` runs the process,
then physical movement (`0x004B2030`), then animation (`0x004B3AD0`). The object
vtable `0x0066D654` has ground response `0x0049CD70` at +0x64 and mass test
`0x004B7230` at +0x88. Mass's runtime low signed word zero suppresses gravity.
Gravity subtracts variable 130 × 256 from persistent vertical velocity while
above the floor. An Egg on the floor with horizontal speed sum above 4096 adds
a temporary upward impulse 12288. Other ground objects clear vertical velocity.

During state 24's wait, `0x004DEA50` requires a room and a Coop on the Egg's
tile, restores the Coop point at z 5000, and adds temporary vertical velocity
`gravity × 256 − persistent vertical velocity`. Physics then cancels that
temporary velocity against gravity. On turn 16, the launch impulse is added
before the same physics pass; the persistent vertical velocity still includes
the previous gravity accumulation. This is not a conventional ballistic reset.

Physical velocity is the sum of persistent (+0x104..0x10C) and temporary
(+0x110..0x118) vectors. Motion uses that sum divided by 16, subdivided by
`min(16, (vector length >> 14) + 1)`. Solid collision reflects the blocked
horizontal component at half speed and continues the remaining substeps.
Afterward horizontal components retain `(4096 − low-word air friction)/4096`,
truncated toward zero; a negative multiplier becomes 2048. Vertical velocity
is restored without this drag. `0x004B5020` clamps below-floor position and
clears an Object's vertical velocity; positions above 12288 become 8192.

**Built:** state 29's Coop clip, state 32's two Egg arts and one-turn hatch-end
lifetime, state 24's hidden wait, launch and tumbling, state 18's recoil and
decaying heading, Casino setting clips with their final poses, and Call to Arms
activation, recall and removal. Integer
animation metadata comes from the nine file resources. Launch collisions use
the engine's terrain solids; original voxel volumes, liquid contacts and floor
geometry outside the Hatchery remain absent. The Hatchery ground zones follow
`0x004545C0`; other room floors retain the engine's existing approximation.

## 9. Torture victims on wheels

**CONFIRMED (2026-10-10, 0x00484110 and 0x00484590).** Strapping places the creature
at its device's point and starts action 36, using creature art 17. This is catalogue
slot 16, `Mis_Torture` for both Mistresses; its mesh contains the body, not the wheel.
Wall victims face inward with the device, roll about its axle, and get draw height
`0x800`; numeric mode 1 also moves their drawing `0x1C2` inward. Floor victims are
placed at the tile centre with z 0, then `0x004B5020` clamps them up to floor height.
They pitch `0x600` and spin with the wheel; mode 1 adds draw height `0x200`.
The meaning of the mode global remains UNKNOWN (§3's `g_` resource switch).

**Built:** simulation attachment uses the device's wall position; the classic view
draws the first torture frame with the wheel's transforms and mode-1 offsets. Ordinary
floor therefore draws the victim at `0x1200`, a wall wheel at `0x1800`. Full clip
playback remains classic look step 4. Chair pose/effects and device visibility in the
other numeric modes remain unbuilt. ADR-0420.

## 10. Open

- `0x0059C360`, `0x0059C720`, `0x0059D010`, `0x005A01A0` (sprites, flat, terrain mesh,
  procedural) and `0x005953D0` are not read.
- The Heart's decay meshes; the chicken's Idle art; the
  setter of RenderInfo `+0x4E` bit `0x40`; what `0x00759ADC` is.
- What writes an object's heading besides the boulder, beds (random) and book cases.
