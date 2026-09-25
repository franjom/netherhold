# Tunnelling: states 186, 223 and 224

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25.
- **Built:** ADR-0222, `TunnelRules`.
- Neighbours: `travel-states.md` §2.3 (the fallback branches), `hero-party-walk.md` §2 (223 and 224), `tile-work.md` (the dig itself).

## Result in one sentence

When a hero's route fails with option bit 0, a hero worker (the Dwarf) digs its own way. It aims a straight line at the target and follows walls round rock it may not dig, and it digs each tile from beside it. A party leader lends the errand to its Dwarf and walks behind it until the Dwarf hands back.

## 1. Entry: the route helpers' failure path — CONFIRMED

This is `0x00471890` at `0x0047193A`–`0x00471A56`, and `0x00471A70` alike.
- **Without option bit 0**, a failed route returns 0.
- **A hero worker** (definition `+0x686` bit 0, owner `[0x006F254C]`) calls `0x00485B80(point, resume)`, or `0x00485AB0(room, resume)` for a room walk.
- **A party leader** (`PartyId != 0xFF`, `Flags & 2`, `PositionInParty != 0`) scans its owner's list for a same-party follower (`Flags & 4`) that is a worker.
  - The first such follower whose tunnel request succeeds gets it with resume **224**. The leader takes the worker's tag into `MyLeadersId` and enters **223**.
  - Workers whose request fails are passed over. With none found it returns 0.
- **The feasibility test** `0x00471740` has the same branches. There it asks only `0x00485B50`: is the creature a worker, and may it enter 186 (`0x00477310`)?

## 2. The tunnel request, `0x00485B80` / `0x00485AB0` — CONFIRMED

1. `SetState(186)` must be taken; sound `0x100` plays.
2. **The target tile** `+0xB7/+0xB9` is the point's tile, or the room's first tile (`+0x2D/+0x2E`).
3. **The dig point** `+0xAF/+0xB3` and **the stand point** `+0xA7/+0xAB` both start as the creature's position.
4. The resume state goes in `+0xD3`, and the phase `+0xBB` is 0.

## 3. State 186, `0x00485C10` — CONFIRMED

1. **An action still playing** (`0x00496D00`): wait.
2. **The dig tile is clear** (world slot `+0x208` false). Try the route to the target tile's centre, resuming `+0xD3`, options 0: done.
   - Otherwise plan the next dig tile (§4).
     - **Found:** find a stand point (§5) and walk there resuming 186. If either fails: `SetState(4)`, return 0.
     - **Not found:** with the reach flag `+0xCB` set, walk to the target resuming `+0xD3`. Otherwise `SetState(86)`, wandering.
3. **The dig tile is blocked.**
   - **More than `0x555` from the stand point on either axis:** find a stand point and walk there resuming 186, or fail.
   - **At it:** turn toward the dig point (`0x004B2AE0`, one turn-rate step), waiting while any angle is left. Then start action 1, or 2 when the tile's terrain has a `GoldValue` (`+0x185`), aimed at the tile (`0x00496980`, target at `+0x38D`). Failure returns 0.

**`+0x208` is the route map's solid bit.** DK2's own `CWorld` vtable, found through RTTI, is `0x0066E3EC`. Slot `+0x208` is `0x00509200`, bit 3 of the per-tile byte map at `world+0x9F2`. The route helper's own target check `0x004B35C0` refuses the same bit.

**`0x004B2AE0` turns; it is not a region test.** It steps the heading toward a point by at most the definition's turn rate (`+0x6A0 & 0x7FF`) and returns what is left. Ours has it as `Bearing.TurnToward`. That answers half of Q371: the fight state's pre-swing test is a turn to face the target. Payday's 188 likewise waits while it turns to face the tile (`payday.md` §4.3).

## 4. The next dig tile, `0x00485EF0` — CONFIRMED

It sets the reach flag `+0xCB` to 1 first.

**Phase 0: the straight line.**
- **The step.** From the dig point's tile, step one tile toward the target along the axis with the larger remaining distance (x when `|dx| > |dy|`, else y). The dig point follows each step.
  - Arriving at the target returns 0.
  - Once, at the first clear tile, if the target is already reachable (`0x00471630(target, 0)`), return 0 with the reach flag still set.
- **The stop.** The line stops at the first tile that is blocked (`+0x208`) or is Lava (terrain flag `0x100`).
- **A diggable tile** (`0x00453D10`: Solid and not Impenetrable) not owned by the tunneller's player is the dig tile: return 1.
- **Otherwise, wall-follow.**
  - The facing `+0xC7` is the step taken: 1 east, 3 west, 2 south, 0 north. A y step counts as south when the blocked flag was set.
  - Phase becomes 1.
  - Carry on along the line past up to 81 more blocked tiles to the first one that is neither (solid and impenetrable), nor (solid and the tunneller's own), nor Lava. That exit tile goes in `+0xBF/+0xC1` and the hit point in `+0xC3/+0xC5`. Past 81, return 0 with the reach flag set.
  - `0x004868D0` walks both hands round the obstacle from the hit point to see which reaches the exit first. Its answer goes in `+0xCF`, and the facing turns one quarter that way.

**Phase 1: follow the wall.** Starting at the dig point:
- **At the exit tile:** that is the dig point; phase 0; return 1.
- **The side cell** (a quarter turn toward the hand).
  - Diggable, not the tunneller's and not Lava: dig it and return 1.
  - Not solid and not Lava: step onto it.
- **Otherwise turn** the other way, up to four quarters, until a cell is diggable (dig it) or open (step). Four turns with neither: dig the last.
- **Back at the hit point four times:** the reach flag is cleared; return 0.

The directions (`0x0069A070`) are N (0,−1), E (1,0), S (0,1), W (−1,0).

`0x00453CC0`, used by the hand walk, is a wall of a non-neutral player that is solid, has no gold, and is at its terrain's `MaxHealth`.

## 5. The stand point, `0x00486680` — CONFIRMED

These are the dig tile's four neighbours in order:
1. the near side in x (toward the creature; to the left when level)
2. the near side in y
3. the far side in x
4. the far side in y

The first that is clear (`+0x208`) and reachable (`0x00471630`, options 2) becomes the stand point, at its tile centre.

## 6. States 223 and 224 — CONFIRMED

- **223** (`0x00462130`). The leader reads `MyLeadersId` as the tunneller.
  - A dead or removed tag, or a thing that is not a creature (`+0x12 != 0`), clears the word and returns 0.
  - Otherwise, unless the leader is already in 222, it aims one free tile behind the tunneller's facing. That is 222's search with depth 1: a blocked step turns a quarter, and five turns give up. It adds a random sub-tile offset and walks resuming 223, options `0x80`.
- **224** (`0x004620F0`), the worker's resume.
  - The worker's own `MyLeadersId` names its leader, and **the leader** is sent to state 1 (`ECX = thing[+0x3DF] + 0x24E`, its controller, at `0x00462103`–`0x00462110`).
  - Then the worker goes to 222. There is no liveness test.
  - (The decompiler hides the first call's `this`; the disassembly settles it.)

## Differences in ours

- **The dig lands as imp work.** It is one application per work period (`TileWorkRules`, Dig or MineGold, which resolves an owned wall itself), while an "action" of one period plays.
- **The solid bit is terrain `Solid`.**
- **The turn is `Bearing.TurnToward`** at the definition's `TurnRate`.
- **Not built:** sound `0x100`, and state 86 (the worker idles instead).
- **A same-tile walk finishes at once** (`WorldSteering.TryRouteTo`, DK2's `0x004D0F60` rule). A tunneller pushed more than `0x555` off its stand tile's centre therefore walks there again and again without moving. The leader test puts the leader behind the worker for that reason. Whether DK2 can loop the same way is UNKNOWN.
