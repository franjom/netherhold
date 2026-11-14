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
named but not read are taken by name: the world slots `+0x1CC`, `+0x1D0`, `+0x1D4`,
`+0x204`, `+0x20C` and `+0x240`, and the key test `0x004097E0`. Nothing here is built
yet.

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
  with a live possessed creature, when the counter `[[interface+4]+4]` differs from
  the interface's `+0x10B4` (which it then sets), it posts game action 25 with
  X = focus X + offset X, Y = focus Y + offset Y, and the word
  `(yaw << 16) | def+0x65C`. That the counter is the game turn is HYPOTHESIS (action 25
  is recorded once a turn in captures, ADR-0728); the height sent is the definition's eye
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
   `0x10624DD3` multiply, high dword, then >> 6), truncated.
3. **Creep** (binding 4): held with the bit clear posts game action `0x22` with 1;
   released with the bit set posts it with 0. The handler `0x00517310` does one thing:
   when the sender's possessed-thing word is non-zero it sets that thing's `+0x3E6` bit
   12 to the record's bit 0. No class, liveness or definition test.
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
12. The move is dropped if world slot `+0x20C` at the point or `+0x240` at its tile
    answers non-zero.
13. **Height.** Ground: floor height (world `+0x204`) + `def+0x65C` + bob, where bob is
    sin(`def+0x86B` × counter, 32 steps a cycle) × `def+0x867`, shifted right 7 then 12,
    halved when backing. Flier: when the camera's Z is below floor + eye height the
    shortfall is added to dz first, then Z + dz, plus the bob; so a flier below eye
    height lands at floor + eye + its own dz. When the creature is not live the start
    Z is posted unchanged.
14. On the ground it posts view action `0x80` with the waddle's roll and plays footstep
    sounds 0x332 and 0x333 alternately at each half cycle (0x337 and 0x338 on terrain
    whose `+0x17B` bit 7 is set), placed to the left and right of the heading by an
    offset scaled from 0x30000, or 0x2000 when Run is held (units UNREAD).
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

Arguments: the wanted X and Y, the creature, two outputs. It is the steering layer's
sweep, run once for the step:

1. The direction is the wanted point × 16 less the body point `+0x1A8/+0x1AC`,
   normalised; its length plus the body radius `+0x200` is the reach.
2. It asks the two steering enumerators within that reach: obstacles (`[0x006ED400]`,
   callback `0x004D1B40`) and creatures (`[0x006ED3FC]`, callback `0x004D1AB0`). Both
   are described in [steering-behaviours.md](steering-behaviours.md).
3. Each callback asks `0x004D2230` for the distance along the direction at which the
   two circles touch (the radii summed). A candidate is kept when that distance lies
   within ±(reach − own radius); an obstacle whose flag bit 2 is set is skipped, and so
   is the creature itself. A kept candidate **replaces** the last one: the callbacks do
   not compare distances, so the last one enumerated wins.
4. With none kept, the outputs are the wanted point. Otherwise the outputs are the kept
   thing's centre plus (its radius + the body radius) along a normalised vector, ÷ 16.
   Which two points that vector joins is HYPOTHESIS (wanted point less the thing's
   centre): the decompiler dropped the subtraction's left operand. `0x004D2230` is
   UNREAD.

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
  clear bits 1–2; PickLock (2) is the Library special or the inert action 36; the two
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
  hypnosis action `0x1B`. The file offsets of `def+0x673/+0x674` are UNREAD.
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

## 6. The other possession modes

**Entering.** The view handler `0x00448440` (table entry at `0x0066D12C`) takes a
record of a thing id and a player; for the view's own player, when the thing is a
creature or an object (class 0 or 2), it requests camera mode 2 with the thing id. A
neighbouring routine ending at `0x00448438` plays the definition's sound `def+0x6E3`
and requests mode 2 the same way (its head is UNREAD). What `Camera_SetMode` does with
that request between an ordinary mode and mode 2 (preset 10's fly-in) is UNREAD.

**Leaving.** `Camera_Update`'s mode 11 arm waits 2000 ms from `+0xDDC`, zeroes the
pitch and requests the saved mode.

`Hud_PollOrdinaryCameraInput` sends mode 1 to `0x0040F820`: the scroll keys post actions
`0x18` (∓0x23) and `0x17` (∓0x100) instead of moving a camera. Neither id is in the view
table, the second view table at `0x0066D0D8` or the game-action table, so the mode may be
inert; what it was for is UNKNOWN. Mode 10 goes to `0x0040E670` (UNREAD). Mode 11 is
entered from mode 2 by the leave handler `0x00442820` (its record is HYPOTHESIS).

## 7. Not read yet

- How the first-person preset's lens and view distance reach the renderer, and the
  writers of camera `+0xE38` and `+0xE3C`.
- The timed move from an ordinary mode into mode 2 (mode 10), and who raises the view
  event `0x00448440` handles.
- `Possession_SelectAbility`'s level gate in full, `0x005075A0`, `0x004D2230`, and the
  handlers of actions `0x1C`, `0x1E`, `0x1F` and `0x89`.
- The possession HUD (`0x00420D10`), the colour ramps (Q745) and the cast effect (Q749).
- The units of the mouse delta `Input_DrainMouseDelta` returns, and what Mouse
  Sensitivity does to it.
