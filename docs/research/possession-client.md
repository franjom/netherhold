---
systems: [camera-lighting, keeper-spells, presentation-unity]
---
# Possession on the client: camera, movement and keys

DKII.EXE v1.70, headless Ghidra reads on 2026-11-14 (runner and pyghidra-mcp). The
simulation side is [keeper-spell-possession.md](keeper-spell-possession.md) and
[possession-arms-and-inputs.md](possession-arms-and-inputs.md); the camera's scripted
requests are [camera-possession-guards.md](camera-possession-guards.md). Each function's
record in `functions/` holds its read's scope. Everything here is **STRONGLY INFERRED**
(read, not seen running) unless marked; the reviewer re-read it on 2026-11-14. Callees
named but not read are taken by name: the world slots `+0x1CC`, `+0x1D0` and `+0x1D4`
and the key test `0x004097E0`. Built: the creature
fields of §5a, creep (ADR-0802), and the view (`PossessionView`, `PossessionWorld`,
`PossessionControls`, `PossessionCamera`, `PossessionFlight`), driven by `PossessionSession`:
a host reads its input into a `PossessionInput` and applies the camera pose, pointer
lock, hidden creature, commands and sounds it answers (ADR-0805). Unity's side is
`DungeonRunner.Possession`. It compiles and is unit-tested but has not been seen running.

OpenKeeper (`view/Possession*.java`, `Settings.java`, pull requests #782 and #1193) was
used for leads only: which creature fields a first-person view reads and the default
keys. Its camera maths is its own and none of it is taken.

## 1. The camera is the authority

While the local keeper possesses a creature the camera is in mode 2. The creature does
not move by itself: the client moves the **camera** every frame, and once a game turn
posts the camera's place as the creature's pose.

- Camera fields: focus `+0xD68/+0xD6C/+0xD70`, an offset vector `+0xD74/+0xD78/+0xD7C`,
  yaw `+0xD84`, pitch `+0xD86`, roll `+0xD88` (11-bit angles, 0x800 a full turn), lock
  flags `+0xD92`, mode `+0xDA8`. The point every possession routine uses is focus plus
  offset.
- **The pose packet.** `0x004039A0`, at `0x00403BAE`–`0x00403C65`: in camera mode 2,
  with a live possessed creature, when the game turn `[[interface+4]+4]` differs from
  the interface's `+0x10B4` (which it then sets), it posts game action 25 with
  X = focus X + offset X, Y = focus Y + offset Y, and the word
  `(yaw << 16) | def+0x65C`. `interface+4` is the game container, whose `+4` is the turn
  ([0x00517490](functions/00517490.md)), so this is one pose a turn; the height sent is
  the definition's eye
  height, never the camera's own Z; the heading is the camera's yaw. The handler's
  treatment of that word is `GameAction_PossessionMovement` (built, ADR-0728).
- **The view listens to action 25 too** (`0x00442870`): only for a packet whose word is
  zero and whose possessed thing is a live creature, it sets the focus to (X, Y, floor
  height + `def+0x65C`). The per-turn packet's word is never zero when the eye height is
  not, so this arm is for some other sender (UNREAD which).
- View actions are records the session carries like game actions and the view handles
  from its own table at `0x0066CE30`. The ones possession uses: 2 sets the focus
  (`0x00441F10` → `Camera_SetFocus`), 8 adds relative angles (`0x004422F0`), `0x7F` and
  `0x80` drive the roll (§3), `0x16` returns the view's own player to mode 3
  (`0x00442920`), `0x15` is the leave handler `0x00442820`.

## 2. The movement step, `0x0040E910`

Called from `Hud_PollOrdinaryCameraInput` (`0x004066A0`) each frame the camera is in
mode 2, ordinary input is allowed and the possessed creature is live. It returns at once
when the creature's `+0x447` is non-zero. Binding ids are DK2's own (§5).

1. **Frame time** is the millisecond clock less the last call's stamp (`0x006CE964`).
2. **Step length.** `0x0048ABB0(mode)` gives the speed (§2a): mode 0 by default, mode 1
   while Run (binding 5) is held, mode 2 while the creature's Creeping bit (`+0x3E6` bit
   12) is set, which wins. The forward unit is speed × `[interface+4]+8` × frame ms ÷ 1000 (the
   `0x10624DD3` multiply, high dword, then >> 6), truncated. The container's `+8` is the
   turns a second, 4 ([0x00526020](functions/00526020.md)), so the per-turn speed
   becomes a distance for the frame.
3. **Creep** (binding 4): held with the bit clear posts game action `0x22` with 1;
   released with the bit set posts it with 0. The handler `0x00517310` does one thing:
   when the sender's possessed-thing word is non-zero it sets that thing's `+0x3E6` bit
   12 to the record's bit 0. No class, liveness or definition test. Built as
   `SetCreepingCommand`; its readers are combat-target-selection.md §2.
4. **Keys.** `+0x3E6` bit 13 skips steps 4 to 6 whole; a requested claim bob (`+0x389`
   non-zero) skips only the keys. Two axis values, strafe and forward, start at zero:
   ScrollLeft (18) subtracts two thirds of the unit from strafe and ScrollRight (19)
   adds it; ScrollDown (17) **sets** forward to half the unit; ScrollUp (16) then
   subtracts one unit. So forward is negative when walking forward, which is the sign
   step 8's formulas need, and both held gives minus half a unit.
5. **Walk cycle.** Standing still on the ground zeroes the counter `0x006CE968` and
   stamps `0x006CE970`; otherwise the counter is elapsed ms ÷ 50. A flier
   (vtable `+0x88` non-zero) always counts.
6. **Waddle.** On the ground, when the counter changed, the strafe gains
   sin(`def+0x86B` × counter, 64 steps a cycle) × `def+0x863`, shifted right 9 then 12.
7. Each axis is clamped to ±0x1000 (one tile a frame).
8. **Rotate by yaw.** dx = strafe·cos − forward·sin, dy = forward·cos + strafe·sin, each
   product shifted right 16; a flier's dz = forward × cos(pitch) >> 16.
9. **Body width.** The target is the start plus the delta plus `def+0x63A` on each axis
   in the direction of travel, floored at 0 and capped at the map's width and height in
   fixed point.
10. **Slide.** `Possession_CanStepToTile` (`0x0040F6B0`) is asked for the X-only tile,
    the Y-only tile and the diagonal. If any refuses, the refused axis is snapped back to
    its tile edge (the far edge when moving positive). With both free and only the
    diagonal blocked, the axis with the **larger** travel is snapped: X when
    |dx| ≥ |dy|, else Y (`0x0040EFB3`–`0x0040EFF5`).
11. The width is taken off again and `0x004D1BC0` resolves the point against things
    (§2b), with the creature's `+0x1A8/+0x1AC` temporarily set to the start × 16.
12. The move is dropped if the point's navigation byte is solid (world slot `+0x20C`,
    `0x005090C0`) or its tile holds a built, closed door (slot `+0x240` → `0x00450F70`:
    a thing of class 4 whose construction word `+0x109` and open word `+0x10D` are 0).
13. **Height.** Ground: floor height (world `+0x204`) + `def+0x65C` + bob, where bob is
    sin(`def+0x86B` × counter, 32 steps a cycle) × `def+0x867`, shifted right 7 then 12,
    halved when backing. Flier: when the camera's Z is below floor + eye height the
    shortfall is added to dz first, then Z + dz, plus the bob; so a flier below eye
    height lands at floor + eye + its own dz. When the creature is not live the start
    Z is posted unchanged.
14. On the ground it posts view action `0x80` with the waddle's roll and plays footstep
    sounds 0x332 and 0x333 alternately at each half cycle (0x337 and 0x338 on terrain
    whose `+0x17B` bit 7 is set), placed to the left and right of the heading by an
    offset of spread × sin >> 16, then ÷ 16, with a spread of 0x30000, or 0x2000 when Run
    is held.
15. It posts view action 2 with (x, y, height): the camera's new focus.
16. **Look**, always: view action 8 with yaw delta = mouse X × 2; view action `0x7F` with
    mouse X when `def+0x686` bit 28 is set; view action 8 with pitch delta =
    −mouse Y × 2, or + when Invert Mouse (`0x00759C19`) is set.

### 2a. The speed, `0x0048ABB0`

The definition's walk, run or shuffle speed (`def+0x5B4/+0x5B8/+0x5BC`), then in order:

- Not a worker (`def+0x686` bit 0 clear) and health below 20% of its maximum (the
  level-scaled `def+0x692`, each step truncated): × 20 ÷ 100.
- Not flying, by the terrain under it: `+0x17B` bit 7 gives × 50 ÷ 100, else bit 8 gives
  × 150 ÷ 100.
- Possessed: × (100 + first-person stat 21) ÷ 100. Otherwise, influenced
  (`+0x3EA` bit 3): the same product ÷ 200.

Every division truncates. The low-health and possession arms repeat what the creature
update applies to the body speed (`LowHealthSpeedRules`, keeper-spell-possession.md
"Modifiers"); the terrain arm's counterpart in `src/` was not looked for.

### 2b. The thing resolver, `0x004D1BC0`

Arguments: the wanted X and Y, the creature, two outputs; the caller stacks a sixth
value the callee never reads. It is the steering layer's
sweep, run once for the step:

1. The direction is the wanted point × 16 less the body point `+0x1A8/+0x1AC`,
   normalised; its length plus the body radius `+0x200` is the reach.
2. It asks the two steering enumerators within that reach: obstacles (`[0x006ED400]`,
   callback `0x004D1B40`) and creatures (`[0x006ED3FC]`, callback `0x004D1AB0`). Both
   are described in [steering-behaviours.md](steering-behaviours.md).
3. Each callback asks `0x004D2230` for the distance along the direction at which the
   two circles touch (the radii summed). A candidate is kept when that distance lies
   within ±(reach − own radius), which is ± the step's length; an obstacle whose flag
   value 4 is set is skipped (only the class-3 things the enumerator adds while this
   routine runs carry it; the Dungeon Heart's centre, flags 3, is kept), and so is the
   creature itself. A kept candidate **replaces** the last one: the callbacks do
   not compare distances, so the last one enumerated wins.
4. With none kept, the outputs are the wanted point. Otherwise the outputs are the kept
   thing's centre plus (its radius + the body radius) along the normalised vector from
   that centre to the wanted point × 16, then ÷ 16 (`0x004D1D8D`–`0x004D1D9C`, read by
   the reviewer). A kept creature replaces a kept obstacle: both share one slot.
5. `0x004D2230(point, direction, centre, radius)` works in doubles, every input times
   2⁻¹⁶: with Δ = point − centre, a = D·D, b = 2 Δ·D, c = Δ·Δ − radius², it answers
   0x7FFFFFFF when b² − 4ac ≤ 0, else the smaller of the quadratic's two roots, each
   times 65536 and truncated (q/a first, then c/q, with q = (√disc − b)/2).

## 3. Angles and roll

- **The preset is data.** Camera preset 2 in `GlobalVariables.kwd` (the 84-byte camera
  record, [classic-camera.md](classic-camera.md) §1) has zoom 0, so the offset vector is
  zero and the camera sits on its focus; lens 4096, half the ordinary view's, so twice
  its field of view; view distance 49152 (12 tiles); position Z 6144 with clip Z
  4096..8192; flags 0x2A. Presets 10 and 11 differ only in view distance (32768 for 10).
  A level's own camera records can override a preset; whether any shipped level
  overrides preset 2 was not checked.
- **The starting pitch is a constant**, not data: entering mode 2, `Camera_SetMode`
  stores 0x200, the horizon, as the pitch target (`0x0044B5B6` for a creature,
  `0x0044B62A` for an object) and takes the roll target from the preset's `AnglePitch`
  word, 0 for preset 2 (`0x0044B5C6`). The 512 in the preset's `AngleRoll` field is not
  read for this mode.
- `Camera_AdjustAngles` (`0x00449D40`) adds each delta modulo 0x800 unless its lock bit
  in `+0xD92` is set (1 yaw, 2 roll, 4 pitch) or a timed transition (`+0xDD8`) runs. In
  modes 1, 2, 10 and 11 it applies no range clamp itself; flags 0x2A carry only the roll
  lock, so action 8 turns and pitches freely.
- **Pitch is limited in `Camera_Update`** (`0x0044BED0`, mode-2 arm, live creature): each
  update, while camera `+0xE38` is zero, pitch is held to 0x100..0x300, which is 45
  degrees either side of the horizon at 0x200. A
  non-zero `+0xE38` instead drives the pitch itself, stepping by 0x20 an update (its
  writers are UNREAD). The same arm calls `Camera_ClaimBob`, and while `+0xE3C` bit 0 is
  set lifts the focus height by a 500 ms triangular bump of 0x800 (0x400 with bit 2).
- `0x0044DD40` (view `0x7F`, modes 1, 2 and 10): adds the argument to the roll, limits a
  positive roll to 0x40, then halves it toward zero on every call: a lean into the turn
  that decays. The negative side (0x400 and above) has no limit: its test compares the
  roll with 0x40 from the wrong side and never fires (`0x0044DD7C`–`0x0044DD8E`). A
  suspected defect of the original, built as it is (ADR-0783).
- `0x0044DE20` (view `0x80`, same modes): sets the roll outright, the waddle.
- `Camera_ClaimBob` (`0x0044DCB0`) is the 16-frame rise and return a possessed claim
  starts; its first call clears the request `+0x389`.

## 4. Attacking and abilities

The interface byte `+0x13D9` holds the selection: bit 0 melee, bits 1–2 a special
(2 or 4), bits 3–7 the spell slot.

- **Keys** (`Input_HandleGameKey`, `0x004058C6`–`0x00405BC2`, only with a live possessed
  creature whose `+0x447` is zero): Melee (6) sets bit 0 and clears bits 1–2; Spell1–3
  (7, 8, 9) call `Possession_SelectAbility` with 0, 1, 2 and SelectAlly (12) with 3, then
  clear bits 1–2; PickLock (2) is the Prison exception or the inert action 36; the two
  abilities (10, 11) are below; RemoveAlly (13) posts action 38; Sniper (1) is
  `0x0040FDF0`; Fire (3) calls `Possession_SubmitAttack`.
- **The two abilities.** `0x00494700(n)` returns the definition byte `def+0x673+n`, the
  kind of special ability n (0 or 1). PrimaryAbility (`0x0040FA90`) switches on kind 0's
  value: 1 sets the selection byte to 2, so Fire posts action `0x1C`; 3 posts action
  `0x1E` at once; 6 posts `0x1D`, the sneak toggle, with the camera's tile; 7 posts
  `0x1F`; 9 asks world slot `+0x334`, the melee pick, for a target at the camera pose
  and posts `0x89` with (target, possessed thing, 2000). Kinds 3, 6, 7 and 9 then start
  the presentation `0x005075A0` with `def+0x682` and `def+0x791`. SecondaryAbility
  (`0x0040FDA0`) acts only on kind 5: it sets the selection byte to 4, so Fire posts the
  hypnosis action `0x1B`. Definition mapping, built arms and remaining carrying/foreground
  limits: [special abilities](possession-special-abilities.md).
- The left mouse button reaches the same `Possession_SubmitAttack` from `Hand_Click`
  (`0x00407666`).
- **`Possession_SubmitAttack`** (`0x004100F0`) does nothing when the creature's `+0x447`
  is non-zero or `+0x3E6` bit 11 is set. Melee (bit 0) is tested first and wins over the
  specials. Without it, bits 1–2 = 2 post action `0x1C` when ability 0 is kind 1, and
  bits 1–2 = 4 post action `0x1B` (hypnosis) when ability 1 is kind 5.
- **Action 26 is the fall-through as well as the normal case**: a melee whose recharge
  gate fails, a special whose kind test fails and spell slot 3 all post it anyway, and
  the handler's own gates decide. Dword 0 = X | yaw << 20, dword 1 = Y | pitch << 20,
  dword 2 = Z | roll << 14 | melee << 27 | slot << 28, from the camera's focus plus
  offset and its three angles; the slot keeps four bits. That is the layout the handler
  reads (keeper-spell-possession.md, "Action26 packet"); the roll is the field no arm
  reads. The swing or cast presentation (`0x005075A0`) starts first only when the melee
  recharge gate or `Spell_CanCast` passes.

## 5. Bindings

The binding ids `Input_IsBindingDown` and `Input_MatchesBinding` take are the ids
`GameAction` already carries (`src/DK2.Data/Settings/GameKeyBinding.cs`, defaults read
from `0x005604C0`): 1 Sniper, 2 PickLock, 3 Fire, 4 Creep, 5 Run, 6 Melee, 7–9 Spell1–3,
10–11 the abilities, 12 SelectAlly, 13 RemoveAlly, and 16–19 the scroll keys, which are
forward, back and the strafes here.

## 5a. Creature fields

`CreatureDef_BuildFromRecord` (`0x00492320`, staging offset = file offset + 0x3F0) fills
the fields the step reads: body width `def+0x63A` from file `0xEA0` (`FootprintRadius`),
eye height `def+0x65C` from `0xEB0` (`EyeHeight`), waddle `def+0x863` from the dword at
`0xDBC`, bob `def+0x867` from the dword at `0xDC0`, and the walk-cycle scale `def+0x86B`
from the byte at `0xD3D`, zero-extended. The lean flag is `def+0x686` bit 28, file
`0xEFC`.

## 6. Entering, leaving and the other possession modes

**The timed move (STRONGLY INFERRED).** `Camera_SetMode` (`0x0044ACE0`) sorts modes into
classes (1, 2, 10 and 11; 3, 4, 5, 9, 13, 14 and 15; 7; 18; the rest) and gives a change
500 ms inside a class and 2000 ms across classes, or none when the old mode is 7 and the
new one is not, or camera flag `+0xE3C` bit 6 is set; it refuses the change when flag
byte `+0xD92` has bit 7. It fills the target view (`+0xDB6` X, Y, Z, `+0xDC2`
zoom, `+0xDC6` yaw, `+0xDC8` pitch, `+0xDCA` roll, `+0xDCC` from the preset's file
`0x24` and `+0xDD0` from its file `0x3C` (our `ViewDistance` and `Lens`; the commit
copies them to `+0xD8A`, the renderer's view distance, and `+0xD8E`, the lens), `+0xDD4` mode) and `Camera_CommitMode` (`0x0044BD70`) stores the start `+0xDDC` and the
end `+0xDD8`. Each frame `Camera_Update` (`0x0044C875`-`0x0044C9DB`) moves every value by
`(target - value) * (now - start) / (end - start)` in whole numbers and then makes
`start = now`; an angle's gap is the 11-bit difference with its sign, so it turns the
short way. When `end <= now`, `Camera_CommitMode(0)` copies the target in.

**Entering (STRONGLY INFERRED).** The view handler `0x00448440` (table entry
`0x0066D12C`) takes a thing id and a player; for the view's own player and a creature or
an object (class 0 or 2) it requests mode 2 with the thing id. A neighbouring routine
ending at `0x00448438` plays the definition's sound `def+0x6E3` and requests mode 2 the
same way (its head is UNREAD). `Camera_RequestMode` (`0x0044A5D0`) keeps the ordinary mode
in `+0xDE4` and sets flag bit 7. For a creature the target is its point, its `+0x22` plus
`def+0x65C`, its heading, pitch `0x200` and preset 2's zoom and its two `+0xDCC` and
`+0xDD0` values (the second halved when creature byte `+0x36F` is `0x2D`). The mode word `+0xDA8` stays the
ordinary one for the 2000 ms, so the first-person step, the look and the once-a-turn pose
(§1, §2) do not run. On landing the commit sets `+0xDA8 = 2`, the latch `0x00759AE8` and
flag bit 8, and clears bit 0 of the thing's byte `+0xF3`: the possessed creature's own
model stops being drawn only then. In the last 256 ms, with flag bit 7 set, `0x005976D0`
starts a 256 ms colour change from the creature's `def+0x86F` (Q745).

**Leaving (STRONGLY INFERRED).** Game action 21 (`0x15`) is also in the view table, at
`0x00442820`. With a first word of 0 it requests mode 3; `Camera_RequestMode`, leaving a
class-1 mode for an ordinary one, sets bit 0 of the thing's `+0xF3` again, swaps in the
mode kept at `+0xDE4` and starts colour change 0 over 256 ms. `Camera_SetMode`'s ordinary
arm targets the camera's own X and Y, the preset's height (file `Z`, offset 8), the zoom
the ordinary mode was left with (`+0xDE0`; the preset's when flag bit 6 is set), the yaw
rounded to a multiple of 8 (`(yaw + 4) & 0xFFF8`, which can be `0x800`), and the preset's
pitch, roll and two `+0xDCC`/`+0xDD0` values. `+0xDA8` becomes the ordinary mode at once
and the move takes 2000 ms. Posters of action 21 with zeros: the upkeep failure
(`0x004C13BA`), a death that leaves no thing behind (`0x004880D5`), creature code at
`0x00489F50`, `0x00489F9D` and `0x00489FDD`, and `Player_EndLevelStatistics`
(`0x004BEA98`, `0x004BEEAB`). Which of them, if any, is the player's own leave is UNREAD;
the remake takes that leave to be the same record.

**The mode-11 view (STRONGLY INFERRED).** A first word of 1 requests mode 11,
and only from mode 2. `Creature_DeathBookkeeping` posts it with the id of the thing the
dead creature leaves behind (`0x00488095`), and `Object_OnDelete` with id 0
(`0x0049B702`). The move takes 3000 ms with the `+0xDCC` target 0 and colour change 9
over 1500 ms. For a class-6 thing the target is an eye height from it along its heading
tables, at floor height plus `0x100`, with random yaw, roll and pitch, and the thing's
draw bit is cleared at the request; otherwise the camera's own point at floor height
plus `0x100`, roll 200, pitch 400. 2000 ms after landing `Camera_Update` zeroes the pitch
and requests the kept ordinary mode.

**Other modes.** `Hud_PollOrdinaryCameraInput` sends mode 1 to `0x0040F820`: the scroll
keys post actions `0x18` (∓0x23) and `0x17` (∓0x100) instead of moving a camera. Neither
id is in the view table, the second view table at `0x0066D0D8` or the game-action table,
so the mode may be inert; what it was for is UNKNOWN. Mode 10's input is `0x0040E670`:
bindings 16 to 19 post action 3 with steps of 0x100 (0x800 with binding 21 held) and the
mouse posts action 8 with twice its delta. No request for mode 10 exists (every caller of
`Camera_RequestMode` and every sender of action 15 found, read 2026-11-18: their records), nor a
sender of action 20; the owner knows no such view. Treated as unreachable, not to be built.

**The model (STRONGLY INFERRED) and the footsteps (HYPOTHESIS).** The draw bit is the
only thing found that hides the possessed creature: `Thing_DrawRenderInfo` (`0x0059B880`) builds a body draw only with it
set and tests neither the camera nor the possessed flag. The shadow goes with the body
(`0x00584900` queues it from the same draw object) and so does the status flower
(`0x0048E830` tests the bit); no write was found for a carried object or an attached
effect. Only the possessor's machine clears the bit, so other players see the creature.
The ordinary footstep arm (`0x0043CC30` at `0x0043CD52`) tests none of the bit, the
possessed flag or the camera, and it asks on the creature's own sound handle (`+0x2E`)
while the view's step asks on handle 0, so neither cuts the other: a creature with
authored marks should sound its `0x314` and `0x315` steps beside the view's `0x332` and
`0x333`. Unread: whether the tile pass runs in mode 2 and visits the possessed creature's
tile, and whether the sound manager takes both.

**The remake.** `PossessionFlight` runs the 2000 ms move both ways with the formula
above; the picture comes from the ordinary camera's pose formula, and only the view
axis's place on the screen is blended by the move's progress. The model is hidden from
the landing to the leave, with its shadow, and both kinds of footstep sound. A possessed
creature still in the world at zero health when the possession ends in first person gets
the mode-11 view beside its body (`PossessionSession`): the 3000 ms move, the 2000 ms
hold, then the pitch zeroed and the move back. Its three angles come from our own
generator seeded by the body's place, and that the point lies behind the body is a
HYPOTHESIS. Any other end flies out the ordinary way. The explicit object-death API is built,
but its acquisition/deletion bridge remains unbuilt ([client completion](possession-client-completion.md)).
Colour changes are built ([scope](possession-colour-rendering.md)). The return focus height is the camera's
own, which is the preset's unless something moved it. The view cannot be left until the
move in has landed, as in the original (§6b).

**The colour changes** (what `0x005976D0` starts, its 16 kinds, timing and each creature's kind) are read in [screen-colour-changes.md](screen-colour-changes.md).

## 6a. The owner's recollection, 2026-11-14

Three things the reads above do not settle, as the owner remembers the original (not
re-observed that day, so not CONFIRMED):

- The right mouse button leaves possession.
- The view starts facing the way the creature faced. Since read: `Camera_SetMode` targets
  the creature's heading (`0x0044B638`).
- A possession starts with melee selected.

The remake builds all three that way. The binary's sender of the leave action and the
initial value of the selection byte `+0x13D9` are still unread.

## 6b. The owner's play of the original, 2026-11-15

The tint and red-flash answer from that day was mistaken. CONFIRMED corrected play
on 2026-11-18 and registered evidence: [Q745 section 1](screen-colour-changes.md#1-verdict).

Seen and heard by the owner in the original that day, possessing an Imp (CONFIRMED as
observations; what causes each is not read):

- The view cannot be left while the camera is flying in. The remake's wait for the
  landing (§6) matches.
- The possessed Imp's footsteps are heard. Which of the two pairs of §6 was heard, or
  both, is not told apart, so the footstep verdict there stays HYPOTHESIS.
- No possessed creature died, so the mode-11 view is still unobserved.
- There is a ceiling over door tiles and over water and lava, as the read of the roof
  says (`kwd-terrain-rooms.md`).

Nineteen screenshots of the original at 1600 by 1200, one as a possessed Imp and
eighteen as a possessed Warlock, are in
`F:\DK2Evidence\captures\visual\possession-first-person--2026-11-15--001`. They show:

- **A picture that looks warped.** Straight edges of ceilings, walls and floors look
  curved, more toward the screen's edges, in every shot. The binary has no warp
  ([possession-warp.md](possession-warp.md)): one plain projection serves every camera,
  with a focal length of lens x 320 pixels whatever the screen, so at 1600 by 1200 the
  first-person view is 136 by 124 degrees. The curves are then the vaulted roof and the
  bent walls seen very wide (HYPOTHESIS: not fitted to a shot). The remake drew 90 by 74
  degrees until 2026-11-15; it now uses the same rule as the ordinary camera.
- **Near things drawn very large.** A wall, pillar, door frame or crystal beside the
  camera fills a side of the screen, stretched. The large pieces at the edge of the
  Imp's shot are probably the same (HYPOTHESIS), not a first-person model.
- **A ceiling**, vaulted over rooms and corridors, which the ordinary view does not
  draw, fading to black with distance, as the far floor and walls do. Its mechanism is read
  (a per-tile height-field roof drawn only in the possession modes): `kwd-terrain-rooms.md`,
  *Ceiling: the possession-view roof*.
- **A HUD of its own.** Along the top: the mana counter, a portrait, a health bar, the
  gold carried, then icons that differ by creature (the Imp a pickaxe and a boot, the
  Warlock three). At the left three counters and the minimap; at the bottom left one
  creature icon. The [possession HUD subset](possession-hud.md) is built.
- **No part of the possessed creature** in any shot, as §6's draw bit says.

## 7. Not read yet

- The writers of camera `+0xE38` and `+0xE3C`.
- Who raises the view event `0x00448440` handles, and who posts the player's own leave.
- Carry/throw dependency closure and the foreground mesh transform; the supported
  ability arms and sniper gates are built ([scope](possession-special-abilities.md)).
- The map and cell accessors the step calls, taken by name: world slots `+0x1CC` and
  `+0x1D0` (`0x00508FA0`, `0x00508FB0`, width and height) and `+0x1D4` (`0x00508FE0`,
  the cell at a position).
- Q749 has a built shot-creation-effect subset ([shot effects](shot-effects.md#7-ours));
  the [possession HUD reading and built subset](possession-hud.md) have their own note.
- The mouse profile/device link and Godot calibration remain UNREAD after the
  bounded device-chain read ([client completion](possession-client-completion.md#mouse-units-and-sensitivity)).
