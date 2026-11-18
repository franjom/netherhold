# Possession view: first-person camera and input

**Status:** built to the point of the first editor run, 2026-11-15. Tasks 1 to 6 are on master,
compiled and unit-tested, not seen running in an editor, no owner play acceptance. What is not
built is listed under "Left after the first build".
**Goal:** a keeper who casts Possession sees through the creature and controls it as in
DK2: walk, strafe, run, creep, look, attack, cast, and leave.
**Research:** [possession-client.md](../research/possession-client.md) (read today), with
[keeper-spell-possession.md](../research/keeper-spell-possession.md) and
[possession-arms-and-inputs.md](../research/possession-arms-and-inputs.md) for the
simulation side, which is built.
**Branch and worktree:** `unity/possession-view`, `.claude/worktrees/possession-view`.

## What the read decided

- **The camera is the authority.** DK2 moves the camera every frame and posts its place
  as the creature's pose once a turn. So the movement step is presentation code that
  reads the world and emits one `MovePossessedCreatureCommand` a tick. It does not
  belong in the simulation, and it moves no hash.
- **Bindings exist.** `GameAction` already carries DK2's ids 1–13 and 16–19 with their
  defaults, so no new key table is needed.
- **The first-person preset is data** we already parse (camera preset 2).

## Constraints

- Rules of the step live in `DK2.Presentation` (Unity-free, tested with a hand-built
  database); Unity only reads keys and the mouse, places its camera and plays sounds.
- Integer and fixed-point arithmetic as read, so a test can assert DK2's numbers.
- Every task: tests first, reviewer, documenter, gates, one commit. A task that changes
  simulation state takes the next free HashVersion and ADR number at commit time.
- No Unity editor run is needed to build or test tasks 1–5. Task 6 needs one.

## Tasks

1. **Creature fields.** Parse the first-person fields the step reads: body width
   `0x63A`, eye height `0x65C` (present as `EyeHeight`), waddle `0x863`, bob `0x867`,
   walk-cycle scale `0x86B`, and flag bit 28 of `0x686`. Their file offsets are not
   read yet: find them in the creature loader `0x00492320` first. Round-trip test.
2. **Creep.** Game action `0x22` (handler `0x00517310`) sets and clears the Creeping bit
   (`+0x3E6` bit 12), and `0x0048ABB0` picks walk, run or shuffle speed. Read both, then
   add the flag and a `SetCreepingCommand`. This is simulation state: hash and save
   version, ADR, function records. The sneak watcher test already names the bit and
   currently has nothing to read.
3. **The step.** `PossessionView` in `DK2.Presentation`: focus, yaw, pitch, roll and the
   walk-cycle counter; `Step(frameMs, keys, mouseDelta, world)` as possession-client.md
   §2 and §3 (speed, keys, waddle, clamp, rotate, body width, slide, height and bob,
   look, pitch limit, roll lean and decay); `PoseCommand()` once a tick. Needs the tile
   test `0x0040F6B0` mirrored (its room-type refusals are HYPOTHESIS: re-read) and the
   thing resolver `0x004D1BC0` read. Footsteps become `SoundRequests`.
4. **Acting.** Selection state (melee, spell slot, the two specials) from bindings 6–12;
   Fire and the left button build `ActPossessedCreatureCommand` or `HypnotiseCommand`
   from the camera pose; Sniper, RemoveAlly and leave map to the existing commands.
   Needs `Possession_SelectAbility`, `0x0040FA90`, `0x0040FDA0` and `0x00494700` read.
5. **Entering and leaving.** Read who requests mode 2 when the spell lands and the
   timed moves of modes 10 and 11, then drive `ClassicCamera`'s mode from them. Built as
   `PossessionFlight`: the 2000 ms move in and out; the death view (camera mode 11) is not.
6. **Unity.** `DungeonRunner.Possession.cs` feeds `PossessionSession` (which drives `PossessionView`) while a creature is possessed:
   captured cursor, raw mouse delta, the preset's lens and view distance on the camera,
   the possessed creature's own model hidden. One editor run to see it; owner play
   acceptance after.

Later work: the [possession HUD subset](../research/possession-hud.md) is built; the third-person
view (action 20) is dropped (below). Colour ramps: Q745, retained developer-only and off by default (ADR-0822); cast effect and current entry-capture limits: [Q749](../research/possession-entry-effect.md).

## Prerequisites

Done (possession-client.md):

- Creature fields: `FirstPersonWaddle`, `FirstPersonBob` and `FirstPersonWalkCycleScale`
  are parsed and written; body width, eye height, the three speeds and the lean flag
  were parsed already (§5a).
- Read: the speed selection (§2a), the thing resolver (§2b), the tile test, the creep
  handler, the two ability keys and the ability kind accessor (§4), and what requests
  mode 2 (§6).

The ability fields, key dispatch, form, prayer, timed fear, Prison exception and sniper
are built (ADR-0819). Carry/throw physics remain unbuilt; scope and verification:
[special abilities](../research/possession-special-abilities.md). The authored foreground
renderer and creature filters are built ([scope](../research/possession-foreground.md));
current-action and entry-pointer highlighting are built ([scope](../research/possession-target-highlight.md)).

Creep (task 2) is built (ADR-0802, hash and save 518): the flag, `SetCreepingCommand`,
the clear on leaving, `CreepingDistancePercent` and the weighting at the combat scans and
the sneak's watcher test. Its level row is not covered by a test: the fixture database
has no stat rows.

Can follow a first playable version: Mouse Sensitivity's units, and how the preset's lens
and view distance reach the renderer (possession-client.md §7).

The reviewer re-read each change and its corrections are applied.

## Owner decisions wanted

1. **Peer validation.** `PeerCommands` refuses `MovePossessedCreatureCommand` from a
   peer until the simulation validates a pose. DK2 trusts the client. Single player is
   unaffected; say whether multiplayer validation is in scope here or stays with M14.
2. **Unity editor.** Task 6 needs an isolated editor run from the worktree. The main
   checkout holds your edited `Dungeon.unity`; confirm the run, or take that step.

## Left after the first build

Built since: the 2000 ms move in and out (`PossessionFlight`) and the hidden model, both
as read in possession-client.md section 6. The creature's ordinary footsteps stay beside
the view's: no read shows DK2 silencing them (HYPOTHESIS).

For the Godot switch (ADR-0805) the wiring's rules moved out of Unity into
`PossessionSession` (`DK2.Presentation`): when a possession starts and ends, the timed
moves, key presses, the commands, the hidden creature and the pointer lock. A host only
fills a `PossessionInput` and applies the answer; `DungeonRunner.Possession.cs` is that
for Unity and `InGameHud.Possession.cs` for Godot (step 3m of the migration plan; the latch and the roof's shown rule are `PossessionScene`'s). The first-person roll is now
applied through an up vector the session computes, the same picture as before.

Remaining lighting, listener, object-death bridge and mouse-calibration boundaries:
[client completion](../research/possession-client-completion.md). Unity keeps near clip 0.3;
Godot uses `PossessionCamera.NearClipDistance`, 0.05, a remake choice (possession-warp.md section 2). The
[possession HUD subset](../research/possession-hud.md) and ordinary-shortcut gate are built in Godot.
The third-person view (camera mode 10) is dropped: nothing in the binary enters it
(possession-client.md section 6, 2026-11-18).

Seen in the original by the owner on 2026-11-15 (possession-client.md section 6b, 19
screenshots) and since built: the roof over every open tile (`TerrainCeiling`, scheduled by
`TerrainCeilingScheduler` and built by `TerrainCeilingGeometry` in `DK2.Presentation`, uploaded by Unity's
`TerrainCeilingChunks` and Godot's `WorldScene.Possession.cs`), the very wide field of view (there is no warp:
possession-warp.md), full-height walls whatever the High Walls option says, and the
death view. Godot applies the distance fade to black; scope and verification:
[possession-warp section 4](../research/possession-warp.md#4-the-possession-distance-fade-strongly-inferred-implementable). Godot now applies the possession-only visible tile set; scope and verification:
[possession-warp section 8.3](../research/possession-warp.md#83-the-enumeration-strongly-inferred-whole-disassembly-of-0x005737e0-and-0x00572f60). The [possession HUD subset](../research/possession-hud.md) includes entry/landing visibility, native 4:3 geometry and the height-fit policy (ADR-0821); final wide-fit captures are inspected and owner acceptance remains.

Remake choices to check in play: one host pixel of mouse travel is one unit of DK2's
delta; the roll's sign; the vertical field of view held at 73.7 degrees on wide screens.
