---
systems: [rooms, combat]
---
# Mechanic: the Combat Pit — states 137 to 141

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25: the handlers of states
  137 (`0x00457A00`), 138 (`0x00457A70`), 139 (`0x00457B40`), 140 (`0x00457CF0`) and 141
  (`0x00458270`), all in `CombatPitState.cpp`; the pit room's handlers `0x004D7A30`,
  `0x004D7A60`, `0x004D7E00` and `0x004D8000` and its helpers `0x004D8030`, `0x004D8100`,
  `0x004D8430`, `0x004D8870`; the hand-drop landing (state 76, `0x0046EAB0`) and its room
  dispatch `0x004E8DA0`; idle (state 1, `0x004773D0`); `SelectAVictim` (`0x0045CB00`),
  `spFighting` (`0x0045F030`), the health routine `0x004B2CE0`, the level increment
  `0x0048B120` and the experience adder `0x00457880`; the floor-height routine `0x004545C0`
  and the room-neighbour picker `0x004E50F0`. Addresses are facts about the binary in our
  own words; no decompiler output enters this repository (ADR-0046)
- A sweep of every reader of `creature+0x3E6` bit 15 (`CombatPitFighter`), by immediate
  and by high-byte test (`TEST AH,0x80` after a load of the word): 30 functions
- Whole-image `CallScanAll.py`, `SetStateLits.py`, `StoreAt.py` and `ReadsAtMulti.py`
  sweeps on 2026-09-25, covering all 13 initialised blocks, including `.text`
  (`0x00401000`–`0x00652BFF`) and `cseg` (`0x00653000`–`0x0066BFFF`); variable 19 at
  `0x006F2590` was the positive control and produced its expected 226 hits
- The creature save path: creature vtable `0x0066D524` slot `+4` → `0x00488520`, state
  vtable `0x0066D404` slot `+4` → `0x00476CB0`

Original version: DK2 v1.70 (GOG)

Questions: advances ADR-0135; Q310 (the loser is knocked out) is the owner's and stands;
Q364's `+0x16` is answered in §6; Q427's bit-15 call in the level increment is §7.

Confidence: **STRONGLY INFERRED (binary)** throughout. Owner observations: Q310 (the loser is
knocked out), and §4's end of a fight with the mentor's announcement (**CONFIRMED**, owner
2026-09-25).

---

## 1. The ways in

**A hand drop on the pit's floor.** The landing state 76 tests the tile it came down on.
On a Combat Pit tile that `0x00454110` accepts — an interior tile, whose map byte `+0x0C`
is `0xFF` — it scatters the creature inside the tile by `random(0x1000)` on each axis.
Then, after its generic landing work:
- **An enemy's creature** (tile owner neither the creature's owner nor the neutral player)
  on an interior tile goes to the room record's `+0x1D` state, 137, and `+0x3CB` (the
  `PrisonOwner` word) takes the room's owner. This is how a captive is put in the pit.
- **An own creature** that is not a worker, or any creature when the keeper's `Number`
  (`player+0x10FC`) is below 3, goes through `0x004E8DA0(0)`, which reads the room
  record's `+0x19` state, 137, asks the job-refusal test `0x00494480` about the record's
  job slot `+0x25` (none for the pit), sets 1 and then 137, and writes the job slot into
  `DroppedToDoJobId` (`+0x2BC`).

`0x004E8DA0(1)` is the predicate behind both: it is 0 — "this tile takes the creature
over" — for a Portal's centre, a Prison or Temple interior, a free torture device and a
Combat Pit interior, and 1 elsewhere.

**The room records** (`portals-room-handler-table.md`): the Combat Pit's are `0x004D7A30`
(construct), `0x004D8000` at `+0x05` (the room freed, `0x004ECD00`) and at `+0x0D`
(ownership change), `0x004D7E00` at `+0x09` (after any of the three rebuilds that change a
room's tiles: `0x004EC900`, `0x004ED1A0`, `0x004ED440`), `0x004D7A60` at `+0x15` (the
per-turn update), and 137 in both state slots.

**The Training Room at its cap does not send anyone.** Training's job handler
(`0x00485240`), for a creature at or above `TrainingRoomMaxExperienceLevel` and below
`CombatPitMaxExperienceLevel`, calls the pit finder `0x004D8870` and returns 0 whatever it
says: the finder's result is discarded, and only its messages reach the player (§8).

**No ordinary gameplay entry to 140 was found; loading can restore it.** `CallScanAll.py`
found 329 direct `SetState` calls, 145 calls to `0x0048AD30` and four to the stun wrapper
`0x00491830`, all in `.text`; `cseg` has none, and neither setter address occurs as data,
excluding a stored vtable or function-pointer target. No literal argument is `0x8C`.
The 25 computed arguments resolve to `+0xDF`, the current state, 1, fixed non-140 ids, or
the room table; every `+0xDF` writer and route-helper resume argument excludes 140, and
the complete room table has no 140. The stun wrapper's four callers pass only 255 or 256.
No exact `0x8C` scalar compares with the current-state field, and its state-table record
has no specific reader; dispatch is only through the generic current-state index.

The exception is save loading. `0x00488520` invokes the state object's virtual
serialiser, and `0x00476CB0` transfers `0x111` bytes beginning at state-object `+0x10`.
That block includes the current id and `+0xDF`, so a save containing 140 restores it
directly without `SetState` or transition validation. A textual store sweep's only
apparent direct write to `creature+0x25E`, `0x0056EEDF`, is an unrelated property-copy
structure, not a creature. Thus 140 is load-restorable, but Q431 remains the live-game
check for an endogenous Combat Pit transition.

## 2. The states

Every handler runs once a turn; the dispatcher counts `Age` (`state+0x1C`) up before the
handler, and `SetState` zeroes it.

**137** (`0x00457A00`), the arrival. On a Combat Pit tile whose floor height
(`0x004545C0`) is exactly `0x400` — the sunken interior, §5 — `0x004D8030` resets every
creature on the room's interior tiles that is on the map (`+0x12 == 0`) and has
`CombatPitFighter`: each is set to 1 and then 138. Then 138, returning 1.

**138** (`0x00457A70`), placing. At `Age` 1 the creature stops (`0x0048ECE0`). On a Combat
Pit tile:
- at height `0x400` it gains `CombatPitFighter`, sets 139 and, if that is taken, copies the
  room's owner (`room+0x1D`) into `+0x3CB`;
- elsewhere on the pit (the rim), if the tile is its own keeper's pit (`0x004539B0(16,
  owner, 0)`), it sets 142, the spectator's state, and returns what `SetState` returned
  (`0x00457AD3`–`0x00457AE3`). **The gate refuses it** (`combat-pit-spectators.md` §1), so 138 on the rim returns 0,
  idle;
- otherwise it returns 0.

Off the pit it returns 0.

**139** (`0x00457B40`), waiting for an opponent.
1. Below `CombatPitMaxExperienceLevel` (variable 192, byte `0x006F2976`, ships 8): with no
   engagement (`+0x502 == 0`) and standing on a Combat Pit tile, `0x004D8100` finds an
   opponent (§3). Found: `0x0048F390` makes each the other's forced target
   (`AttackingId` `+0x3F9`, `AttackAbnormalTargets` bit 2), both take `CombatPitFighter`,
   return 1. With an engagement, off the pit or with nobody found, go on to step 3.
2. At or above the cap: at `Age` 1, a running action is ended and action `0x2C` started;
   started, return 1. While an action runs, return 1.
3. Anger: `angerNoWork` (`def+0x6C7`, as loaded: the file's figure over four) into bucket 0
   and again into bucket 3, through the wrapper `0x00478B80`, **every turn** this step is
   reached. Ours added the file's figure until ADR-0228.
4. On a room tile, `0x004E50F0(position, 4, 0, 0)` picks a neighbouring tile (§5); found,
   `0x00471890(point, 139, 0xE8)` walks there in state 6 and resumes in 139. Return 1.

**140** (`0x00457CF0`), load-restorable but with no ordinary gameplay entry found (§1).
While `Age` is at most variable 19 × 4 (four game seconds) it starts action `0x23` when
none runs. After that, with no action, it sets 138 and adds
`ModifyAngerOfCombatPitVictor` (variable 134, word `0x006F2908`, ships −16400) to bucket 5.

**141** (`0x00458270`), thrown out. At `Age` 1 or less, return 1. Then on a Combat Pit tile
of anyone's, 138; elsewhere 0.

**Idle** (`0x004773D0`), unless it follows a possessed creature: a creature with
`CombatPitFighter` and not `CreatureDying` sets 1 and then 138; refused, it clears
`CombatPitFighter`. Every other creature has `+0x3CB` cleared here and goes on to the
hero, neutral, worker and ordinary branches. So a fighter that ends any other state goes
back into the pit.

## 3. Finding an opponent (`0x004D8100`)

A creature with its chicken word (`+0x44B`) running finds nobody. Otherwise the room's
tiles are walked in chain order; on each interior tile, each creature standing there is a
candidate when it is on the map, has `CombatPitFighter`, is below the level cap, is not
`CreatureDying`, has no chicken word and is not the seeker. The first pass keeps the
nearest candidate by squared distance (`d = (dx² >> 12) + (dy² >> 12)`, strict `<`) whose
owner the hostility test `0x004BF390` calls hostile; with none, a second pass does the same
over candidates it does not call hostile. **An enemy is preferred to a friend, and a friend
will do.**

Nothing excludes a candidate that is already engaged. `0x0048F390` first drops the
creature's old forced target (`0x0045EF50`: the engagement `+0x502` is unlinked from its
target's attacker slots and cleared, and `0x0048F3F0` clears `AttackingId` and bit 2), so a
fighter still standing over a beaten opponent is taken by the next seeker.

## 4. The fight

The pit adds no fight of its own; the forced target does it.
- **`ProcessFight`** skips the may-I-look gate `0x0045A6D0` for a creature with
  `AttackAbnormalTargets`. The scan cadence (a new tile, or the counter at 10) is unchanged.
- **`SelectAVictim`**, with the flag set: a live `AttackingId` is the candidate, with **no
  attackability, sight or hostility test**. The route request (`0x004D0E60`, option `0x80`,
  0 for an Imp) must succeed, or, for a creature with an attack spell, the target be in
  sight; else the candidate is dropped. The distance-over-three range test is skipped. The
  commit, the withdraw check and the move to 199 are the ordinary ones. A dead
  `AttackingId` is cleared (`0x0048F3F0`) and scan A runs over every creature: attackable,
  not the seeker, in sight or hearing, no hostility test.
- **`IsAttackable`** (`0x0045A8B0`) is clear for a `CombatPitFighter`, so no ordinary scan
  picks a pit fighter, and a pit fighter is reached only as someone's forced target.
- **`spFighting`** accepts a creature target that is not attackable when **I** am a
  `CombatPitFighter`: the target must only be live and on the map (`+0x16 != 2`). A target
  that fails, for a creature with `AttackAbnormalTargets`, is unlinked, the forced target
  cleared, and the handler returns 0 — no new target is sought.
- **Damage.** A pit fighter's blow is `(100 + CombatPitMeleeDamageModifierPercentage) / 100`
  (variable 225, ships 100: double) (`combat-damage.md` arm C). A pit fighter takes
  `(128 − 128 + PitPercentageDamageTakenOfNormalCombat) / 128` of a blow (variable 239,
  word `0x006F29F6`, ships 20: 20/128), before the other terms of the health routine's
  multiplier (`creature-status-effects.md` §3).
- **The knockout** is the ordinary collapse (Q310): health at or below zero, with
  `GenerateDeadBody`, not in a Prison or Torture Chamber, sets `CreatureDying`. A dying
  creature then takes no damage from a blow (`0x004B2CE0` returns early when `CreatureDying`
  is set and its fourth argument is 0, which every swing passes).

**The knockout ends the fight, with no victory.** The loser's collapse takes it out of the
fight state, and the Combat category's leave hook `0x0047A980` calls `0x0045ADC0`. That
clears the loser's own forced target and engagement, then walks its sixteen attacker slots
(`+0x504`, `+0x514`): each attacker whose engagement (`+0x502`) is the loser has it cleared,
its forced target cleared (`0x0048F3F0`) when it has `AttackAbnormalTargets`, and the victory
nibble (`+0x529 |= 0xF`) set **only when the loser's health is below 1**. A knockout has
just set that health to the dying countdown, `var(0x006F25B0) × var19`, so the winner gets
no nibble: `spFighting` finds no target, `SelectAVictim` finds none (the flag is gone and pit
fighters are not attackable), and the state ends. Idle then returns the winner to 138 and
139, where it takes the next fighter or walks about. A loser that dies outright (no
`GenerateDeadBody`) leaves at health 0 or below, and the winner takes the victory arm.

The owner confirms it (2026-09-25): the winner does not keep swinging at the unconscious
loser; it fights another creature if one is in the pit, otherwise it walks around, and the
mentor says the fight in the Combat Pit is over — message 9 of §8.

None of those knockout, target-loss, victory or pickup paths sets 140. State 140 therefore
does not explain the normal winner's post-knockout behaviour found statically; Q431 asks
for the short live capture that can falsify that conclusion.

**The loser sinks, moves to the rim and rises.** Owner recollection (2026-11-15): the
defeated body sinks into the sand and sprouts on the rim, where an imp can collect it.
The sequence below is STRONGLY INFERRED from the binary; no new native capture was taken.
State 217 (`0x004599D0`, re-read 2026-11-15) clears `CombatPitFighter`. After the turn
passes state `+0xAB` (stamped turn + 10 by state 216), with no action running and the
entity on a Combat Pit interior, it starts phase 1 and requests effect `0x149`.
Each phase-1 call lowers Z by `0x200`; at Z <= 0 it clamps to zero and starts phase 2.
If still on that interior, `0x004E8430` selects a random tile that is neither an interior
nor a wall slot; the entity moves there at sub-tile offset `(0x400, 0x400)`, facing
`0x300`, and effect `0x149` is requested again. Each phase-2 call raises Z by `0x200`, ending
at `0x1000`. Phase start and both phase branches return before the dying countdown.
The separate steering body is not moved by this handler (see `steering-body-retention.md`).
The requests use world slot `+0x38C` (`0x00509580`), the effect wrapper, not the sound
manager (independent review, 2026-11-15). Raw setter `0x004B5570` forwards to `0x004B5350`:
it relinks the entity but neither refreshes its floor word `+0x26` nor clamps Z to it.
The later physical pass still runs. For an ordinary non-flying fighter without another
impulse, below the retained floor it makes no commit; rising above it adds gravity, and
a physical commit through the normal setter can refresh/clamp to the rim floor before
phase 2 finishes. The nominal eight rise calls therefore need not all be visible.

**Built (hash/save 522):** `DyingStateRules`, called in the per-creature recovery step,
runs both phases, the delay, raw relocation and effects. It retains the old floor for
`CreaturePhysicsRules` until a normal commit refreshes it. The dying countdown is now
per turn, paused by phase branches and carriage; a standalone health-system host has
a once-per-turn fallback. `WorldView` exposes current and previous Z instead of pinning
the dying creature to its floor. `CombatPitTransportTests` cover the sequence, effect
requests, physical clamp, snapshot/copy continuation and imp rescue to the bed.
The action-running test still uses `ActionUntilTick`; its inherited collapse timing is
a hypothesis (`death-fall-heading.md`). Native capture resync does not import these
phase/deadline/floor fields; exact continuation is tested with our own snapshots.

## 5. The floor, and where a waiting fighter walks

`0x004545C0(tile, position)` is the floor height. For a room tile `0x00454110` accepts it
returns a per-type constant: Hatchery `0xE00`, Temple 0, Graveyard and Crypt `0xF00`,
**Combat Pit `0x400`**, anything else `0x1000`. A pit rim tile is `0x1000` except in the
quarters that touch an interior neighbour, which slope; only the exact `0x400` is tested
anywhere in the pit code, so a pit fighter is a creature on an interior tile.

`0x004E50F0(position, count, rimOnly, empty)`, a room method: `start = random(count)`; for
`i` in `0..count−1`, direction `(start + i) % count` from the four-way table `0x006BC350`
(N, E, S, W) when `count` is 4, else the eight-way table `0x006BC370` (N, NE, E, SE, S,
SW, W, NW). The neighbour is taken when it is a tile of the same room, its terrain is not
`+0x17B` bit 0, its interior test equals the start tile's (or, with `rimOnly`, is false),
its map byte `0x00455000` equals the start tile's, and (with `empty`) nothing stands on it.
The point is the tile's centre at the creature's height. 139 passes `(4, 0, 0)`: one
orthogonal step, staying on the interior.

## 6. `+0x16` is the map layer (Q364)

`0x004B4A10` initializes the thing links to zero and map layer `+0x16` to 2;
`0x004B4F60` actually removes a thing from its tile list and writes 2 unless
already off-map (rechecked 2026-09-28). **Layer 2 means not on the map**.
Hand and carrier callers: `computer-keeper-threat.md` section 2. The adjacent
`+0x12` is the thing class (0 creature, 2 object), not its container state;
`0x004BC500` branches on these classes while picking up.

## 7. Experience, levels, and leaving the pit

- The adder `0x00457880` refuses every change for a `CombatPitFighter` at or above
  `CombatPitMaxExperienceLevel`.
- The level increment `0x0048B120` sets a `CombatPitFighter` that reaches the cap to 1 —
  and idle sends it back to 138 and 139, where at the cap it only wanders (§2 step 2).
  This is the bit-15 call Q427 listed.
- `CombatPitFighter` is cleared in six places, found by a sweep for the word, dword and
  byte forms of the clear: idle's refused 138; the room reset `0x004D7E00`; **the hand's
  pick-up `0x004BC500`**, which also clears the forced target (`0x0048F3F0`); **state 217**
  (`0x004599D0`), so a knocked-out loser stops being a fighter once it lies dying; the
  Spectating enter hook `0x0047A9E0` on a Combat Pit tile; and the vampire's reincarnation
  `0x0048FFC0`. A fighter that walks off the pit keeps the flag, and idle and 138 then
  pass it back and forth each turn until one of these clears it.

**The room reset** `0x004D7E00` (after any rebuild): every creature on the room's tiles
that is on the map with `CombatPitFighter` has its forced target cleared, loses the flag
and is set to 141. Then it records the room's entry point (`+0x4B`, `+0x4F`: the tile
north of the first interior tile, in chain order, whose three northern neighbours are not
interior; `+0xA3` = 1 when any tile is interior) and sets every creature on its attached
list (`room+0x0A`) to 141. **`0x004D8000`** (freed, owner changed) does only the last.

## 8. The room's own turn, and the finder's messages
The room turn (`0x004D7A60`) advances byte countdowns `+0x49` and `+0x4A`,
reloading with `random(var19 * 4)` and `random(var19 * 32)`. The room definition's
`+0x421` / `+0x423` words name **effects**, not sounds: shipped Combat Pit ids are
90 and 52. Reload draws occur even when the effect id is zero; a nonzero effect also
needs an eligible slot. The canonical handler record describes the slot and position
rules: [004D7A60](functions/004D7A60.md).

Exactly one standing floor creature, more than one standing on the previous turn and
at least one down posts message 9 to the owner (the mentor's fight-over announcement,
CONFIRMED by the owner). The standing count is stored as byte `+0x59`. This counts
on-map floor occupants, regardless of allegiance or the fighter flag.

`0x004D8870`, the finder, per keeper's Combat Pit (`player+0x36` chain) that has room
(`0x00471740(room, 0x80)`): a free spot (`0x004E8210(1, 0)`), whether the drop test
`0x004D8430` accepts it, and the nearest by distance to the room's first tile, preferring a
pit with nobody in it that `0x0045C640` objects to. With none, when the keeper may build a
Combat Pit (`0x004BAB20(16) == 3`), one of messages `0x29` (no pit), `0x2A`, `0x2B`, `0x2C`
(full, or no interior), each held off by `room+0x9F` and variable 19 * 4.

`0x004581F0`, called from destruction of a `CombatPitFighter`, requests the second
room effect at the destroyed creature's position. Its foreign-owner helper is a
state-neutral floor query: [004D83D0](functions/004D83D0.md), suspected defect DK2-040.

## 9. What is built (ADR-0213; hash/save 523)
- The hand-drop paths for own and enemy creatures, workers and the keeper `Number`
  branch, and 138's keeper-binding write. `PrisonOwnerPlayerId` represents the shared
  binding; actual Prison custody additionally requires resolved category 14 or raw
  state 126. Disposal, regeneration, rescue and the dying gate use that distinction.
- 137 through 141, idle's follower attempt before pit return or flag clearing, pairing, per-turn
  anger and one-step wandering. Load-restored 140 runs action 35 and its anger exit;
  ordinary entry remains unestablished (Q431). The cap runs action 44 on its plain clock.
- The forced-target route through combat, missing-tag scan A and the common D/E/F
  acquisition routes, including friendly-route failure. Pit fighters' targeted shots
  use the Good player's ownership. Existing scan and target-selection limits are in section 10.
- Knockout, rescue and transport (section 4), attackability, damage, experience refusal and
  the cap transition. The actual-held-place test governs the dying exception.
- `CombatPitRoomStep` runs the two effect timers and fight-over message. Message 9 uses
  the existing speech-353 presentation row, 50-second lifetime, tab and duplicate rules.
  The destruction effect and both finders' room-needed/full/unreachable/unequipped
  messages are wired (`RoomNeedRules`, ADR-0413).
- Room grow, split, capture, absorption and free callbacks migrate runtime with the
  surviving room or initialize a new room's runtime, and reset or release fighters and
  attached holders at the corresponding callbacks. See [004D7E00](functions/004D7E00.md)
  and [004D8000](functions/004D8000.md); the native refusal-loop candidate DK2-041 is deferred.
- Entry geometry, rim slopes and the full navigation-byte neighbour predicate are used.
  Spectators, their counted cheer actions and room-centre facing are described in
  [combat-pit-spectators.md](combat-pit-spectators.md).

The three room bytes and `CombatPitAction` are hashed, saved and copied. Pit timing
uses explicit miscellaneous variable 19, otherwise `LEVEL.TicksPerSecond`, otherwise
fixture value 4; see [0056C4E0](functions/0056C4E0.md).

## 10. Remaining boundaries
The selector and scan B retain their existing partial parity, as do shared geometry,
resource identity, ground occupancy and action-position payloads. The callbacks mirror
room lifecycle ordering with tile-backed runtime rather than native room-object identity.
State 140 remains classified unreachable for ordinary entry coverage despite its built
save-restored handler. Tests establish clone behavior; no new native runtime reproduction
or live visual acceptance was performed.

`KillsInCombatPit` (`creature+0x408`) has no modeled counter or dedicated pit trigger.
The bounded broad-offset audit found constructor byte-zeroing and the diagnostic byte
reader, with no increment found among seven stores and 31 references; indirect writes
are not excluded. See [00490240](functions/00490240.md) and
[004872A0](functions/004872A0.md). This does not establish that DK2 lacks an increment
or that a dedicated script event is missing. Generic Dying and Killed creature events
remain separate from mentor message 9.
## 11. The spectators

Job 26 and states 142–144 are in `combat-pit-spectators.md` (ADR-0227).
