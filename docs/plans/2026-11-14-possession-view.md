# Possession view: first-person camera and input

**Status:** parked by the owner, 2026-11-14, until the prerequisites below are researched
and built. Nothing here is built; the research note and its function records are on the
branch, not yet through the reviewer pass.
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
   timed moves of modes 10 and 11, then drive `ClassicCamera`'s mode from them. Until
   read, enter and leave cut directly, stated as a deviation.
6. **Unity.** `DungeonRunner` feeds `PossessionView` while a creature is possessed:
   captured cursor, raw mouse delta, the preset's lens and view distance on the camera,
   the possessed creature's own model hidden. One editor run to see it; owner play
   acceptance after.

Deferred, each its own task later: the possession HUD (`0x00420D10`), the third-person
view (camera mode 1, action 20), the colour ramps (Q745), the cast effect (Q749).

## Prerequisites

Done (possession-client.md):

- Creature fields: `FirstPersonWaddle`, `FirstPersonBob` and `FirstPersonWalkCycleScale`
  are parsed and written; body width, eye height, the three speeds and the lean flag
  were parsed already (§5a).
- Read: the speed selection (§2a), the thing resolver (§2b), the tile test, the creep
  handler, the two ability keys and the ability kind accessor (§4), and what requests
  mode 2 (§6).

Still needed before tasks 3 and 4:

- `0x004D2230`, the resolver's touch-distance helper.
- Whether `src/` already mirrors the speed's terrain arm (§2a).
- The file offsets of the ability kinds `def+0x673/+0x674`, and the handlers of actions
  `0x1C`, `0x1E`, `0x1F` and `0x89`, for the abilities beyond melee, sneak and hypnosis.

Creep (task 2) is built (ADR-0802, hash and save 518): the flag, `SetCreepingCommand`,
the clear on leaving, `CreepingDistancePercent` and the weighting at the combat scans and
the sneak's watcher test. Its level row is not covered by a test: the fixture database
has no stat rows.

Can follow a first playable version: entering and leaving (task 5), Mouse Sensitivity,
and how the preset's lens and view distance reach the renderer.

The reviewer has re-read all three changes (the client note, the prerequisite reads and
creep) and its corrections are applied. The documenter pass has not been run.

## Owner decisions wanted

1. **Peer validation.** `PeerCommands` refuses `MovePossessedCreatureCommand` from a
   peer until the simulation validates a pose. DK2 trusts the client. Single player is
   unaffected; say whether multiplayer validation is in scope here or stays with M14.
2. **Unity editor.** Task 6 needs an isolated editor run from the worktree. The main
   checkout holds your edited `Dungeon.unity`; confirm the run, or take that step.
3. **Order.** Tasks 3 and 4 give a playable result without task 2 (no creep, walk and
   run only). Say if creep can follow, or must land first.
