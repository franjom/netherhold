---
systems: [keeper-spells, creatures]
---
# Where a dropped creature lands: states 76, 255, 257, 250 and 77

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-09-25; 257 and 250 re-read by disassembly on 2026-10-03.
- **Built:** ADR-0226, `DropLandingRules`; 257's hold and 250's stamp ADR-0345.
- Corrects:
  - ADR-0193 and `state-category-hooks.md` §3.2: a hand drop enters 76, not 77.
  - Q201's "the only site pushing a stunned state is the flee routine".
  - ADR-0215, where a job room's drop acted at once.

## Result in one sentence

The keeper's drop enters **76**. A non-worker that no room takes over is **stunned** (255), whoever dropped it and a freed prisoner included (ADR-0616), for its definition's `StunDuration`. It gets up in **257**, steps along its facing and attempts combat before asking the room underfoot: the room's drop state, or with none, **250**, where it stands until 50 turns past the state word `+0x7E`. **77** is the slap's reaction.

## 1. The drop enters 76 — STRONGLY INFERRED

- **The keeper's drop.** `0x004BC710` clears the in-hand bit (`+0x3E6` bit 0), stamps `+0x4BF` with now and calls `SetState(76)` (`0x004BC7CD`–`0x004BC7D7`).
- **Level-placed creatures (STRONGLY INFERRED, binary 2026-10-10).** After rebuilding rooms,
  `0x0050B5B0` kicks THINGS flag 0x40 creatures to 76; the loader copies that flag through
  `0x0048E000`. `LevelPlacedCreatureRules` runs their room take-over after furniture exists:
  Prison 114→115 with the room owner as jailer, retaining starting health; the independent
  non-worker/hero/neutral fallback also admits neutral creatures on neutral-owned cells.
  Torture 130→131,
  with own creatures kept distinct from captives. Level 8 has two neutral Dark Elves in cells
  and three neutral Mistresses on devices, including the wall wheel at (12,10).
  Deviation: initialization finishes on the first simulation tick, rather than before it.
- **77 is the slap's.** Its only way in is the slap handler `0x0048B8A0` (`0x0048BA06`), which deals `SlapDamage`, fires creature event 5 and player event `0x3C`.
- **What 77 does** (`0x0046F1F0`). It plays action 19, then sends a creature to the state of the room it is attached to (`+0x3B9`): Workshop 63, Prison 115, Torture Chamber 130. A Horned Reaper goes to 238, a worker to 10, anyone else to 1.

## 2. State 76, `0x0046EAB0` — STRONGLY INFERRED (the arms used here)

**Vertical gate and sound (binary reread 2026-10-29; Q680).** At
`0x0046EE1A..4A`, virtual `+0x88` is the creature flying test
(`0x0048ED40`): a flying creature has its Z lowered by `0x800` on this
state update. A non-flyer instead uses the shared gravity pass
(`0x004B2030`), already described in imp-create-position.md. The gate
`0x004B1FA0` predicts Z plus the signed, toward-zero quotient of
(persistent vertical velocity + temporary vertical velocity) / 16 and
compares it with world floor height (virtual `+0x204`). Above it, 76
returns success without the room/stun transition. On contact it requests
sound event `0xF6` from creature-definition sound category `+0x6E3`, except
on a Temple floor accepted by `0x00454110`. These operations are STRONGLY INFERRED;
there is no fixed descent duration in this handler. The requested position
is supplied unchanged to `0x004BC710`.

**The fall takes one turn (CONFIRMED, Level 5 seed-1 capture, 2026-11-10).**
All twelve hand drops in frames 1400 to 1900 (eleven Warlocks, one type 6)
show the creature at the drop packet's exact x and y in state 76 at height
4352 in the frame of the drop turn, then 255 from the next turn with action
52 stamped that turn. The mechanism is a reading (STRONGLY INFERRED): 4352 is 8192 less 3840,
one turn of gravity (variable 130 = 240, times 256, over 16) from the 0x2000
the stored drop passes (`0x004C0F68`); the handler returns before its arms
while the predicted height is above the floor, so the second update lands. The packet's third word differs between drops
(0x3E80000, 0x860000) with the same result.

**Built (ADR-0730, hash/save 459).** `HandSystem.Drop` releases the creature
at the command's point (its tile's centre without one) at
`DropLandingRules.FallingHeight`, and the landing's arms run at the start of
the next tick. `LandingPending` and the dropper's player are hashed, saved
and copied. While it falls the creature does not act (`HandRules.IsHeld`), is
not in the hand (it can be hurt, healed and hit by a forced shot) and shows
the computer keeper state 76. Ours keeps the creature's own state word
through the fall; a flyer's descent (Z lowered 0x800 a turn) has no capture
and falls like any other. Deviations: the Temple pool's sacrifice is still immediate (DK2's arm waits
for the floor); a Torture Chamber tile does not snap the creature to its
centre in the air; the view shows no landing pose for the fall turn. The
keeper AI's own drops pass height 0 and the tile's centre plus 0x100
(`0x004FF589`), so DK2 gives them no fall height; ours falls them the same
turn count from the centre, which no capture confirms.

With it the four Warlocks dropped at 1688 to 1693 stand on DK2's points and
enter 255 and 257 on DK2's turns. Two then differ by one turn entering the
fight, which is Q712's next lead: 438 engages at 1701 where DK2's does at
1702, and DK2's 441 is in 199 at 1704 without the facing step, where ours
ends the full Getup hold, steps and engages at 1705.

The evil worker's landing art is already correct in our view: its `idle`
key resolves to catalogue index 19 (native slot 20). Other creatures use
slot 24 deferred; see animation-transitions.md section 1. No presentation
correction follows from the key's name.

After its landing work (the pit scatter, the fall, and the sound gate above), it:
1. **Sets 1**, and a dying creature stops there (`0x00459560`).
2. **Writes `WasLastDroppedOnARoom`** (bit 26) from the tile.
3. **An enemy room.** On a tile whose owner is neither the creature's nor the neutral player's, a room that takes the creature over (`0x004E8DA0(1)` = 0) gives its enemy state `+0x1D`, through 1. `+0x3CB` takes the room's owner. This is a captive into a cell (114) or onto a device (130).
4. **A non-worker, or a creature of player 1 or 2.**
   - **Taken over:** on a room that takes it over, `0x004E8DA0(0)`.
   - **Otherwise:** `0x00491830(255)` — `SetState(255)`, action 52, creature event 15. **This is the drop's stun.**
5. **A keeper's worker** has its own arms (`0x00481400`, the Treasury, Portal, Heart, Workshop and Temple cases), and falls back to 11. Its Torture Chamber case (`0x0046F034`) sets 130 for any type id but 1, so a keeper's own Imp dropped on a wheel is not strapped (`TortureRules.TryStrapOwn`).

**`0x004E8DA0(1)`, "does this tile take the creature over".** It returns 0 (yes) for:
- a Portal (type3 only) tile whose cell byte `+0xC` is4; Hero Portal reaches its table state after getting up,
- a Prison or Temple tile the cell test `0x00454110` refuses,
- a free torture device under the creature (`0x004F2180`),
- a Combat Pit tile the cell test refuses (its floor).

Anything else, a Library or a Lair included, returns 1.

## 3. The stun, 255 → 257 — STRONGLY INFERRED

255 (`0x004810E0`) stands while `now − state+0x18 < StunDuration`, a count of turns (the loader `0x00493B57` stores file seconds × ticks a second >> 12; `creature-status-effects.md` §2). Its first call, the turn after the stun is set (Age 1), creates effect 101 (Creature Dazed Effect) only when the duration hold arm is active; zero duration skips it. Its art has "dazed" stars and a sparkle on a circular path, tied to the creature's `0x1388` point, which `0x00434E80` follows (ADR-0480). `CreatureStunnedTimeSeconds` (2) is never read. Then 257 (`0x00481200`):
1. It returns 1 while an action is current (`0x00496D00`): the get-up, action 53, which 255's end started. Action 53 plays animation block 27, Getup (`0x006E47AD` = 27), so the hold is that animation's frames at its rate: `ceil(frames × 4 / rate) + 1` ticks, the +1 being the tick the handler sees the action end, as for the Entrance animation (`portal-creature-action.md`). That `+20` selects the block is STRONGLY INFERRED (`creature-action-table.md` §3), and the formula is fitted (STRONGLY INFERRED): it reproduces every 257 run the Level 1–3 captures measured (the longest of a type that ends in a room drop, the same in every run): Warlock (5) 51 frames, 8 ticks; Goblin (7) 69, 11; Troll (10) 29, 5; Firefly (12) 84, 13; Dwarf (14) 16, 4; Lord of the Land (29) 22, 4. A fight can cut it short (257 → 8 or 199): 257's flag byte is 2, without bit 6, so `ProcessFight` runs while it holds (`combat-acquisition.md` §2). Measured in the Level 4 seed 1 capture: DK2's dropped creatures go 257 → 199 or 8 one to eight ticks after getting up, each on the hero registered on it (ADR-0488).
2. It steps a little along its facing: the definition's vector `0x00494650(def, 2)` (`def+0x6EC+0x18`) turned by the heading `+0xF4`, taken if the terrain there is not solid (terrain table byte `+0x17B` bit 0 clear) and the cell test `0x00454110` gives the same answer for the tile there as for its own.
3. It runs `0x00481400` (`SelectAVictim`, else a neighbouring enemy door as the forced target; `door-blocking.md` §8) and returns 1 if that attempt was handled (including a forced-door selection that fails); otherwise **`0x004E8DA0(0)`**. So a shot's stun ends the same way as a drop's.

`0x004E8DA0` has a fourth caller at `0x00481351`, inside 257. The function-based caller list missed it, because Ghidra had no function there.

**Shipped `StunDuration`, all 46 records.** Most creatures lie 2–3 s, which matches the owner (Q201). Goblin 1, Imp 0, Bile Demon 5, Mistress 3, Knight 4, Stone Knight 5; the Dwarf, Firefly and Fairy 0.

Portal state197 entry and its direct196 call are settled in [creature-leaving.md section2](creature-leaving.md#hand-drop-entry-and-direct-call-2026-10-31).

## 4. `0x004E8DA0(0)`, the room drop, and 250 — STRONGLY INFERRED

It runs only while `CasinoBigWinnerGameTickRemaining` (`+0x3F2`) is 0.

**On a room with a drop state** (`+0x19`, `portals-room-handler-table.md`):
- A keeper's creature is refused if it hates the room's job (`0x00494480(+0x25)`).
- Otherwise it goes to 1 and then the state, and `DroppedToDoJobId` takes the job.
- Job 0 is never hated: whole `0x00494480` returns 0 before reading its list.
  The Portal and Hero Portal rows both use job 0 and state 197. A level-placed
  non-worker or non-keeper worker on a Portal centre dispatches immediately;
  on Hero Portal it is stunned first and reaches that dispatch after getting up.
  A keeper worker of a type other than 1 takes either centre directly to 197.

**With none**, for a keeper's creature (not the hero player or the neutral one):
- `+0x2CC` takes now. **This is the state word `+0x7E`:** the state record lies at `creature+0x24E`, and `0x24E + 0x7E = 0x2CC`.
- Unless the owner's `CallToArmsOn` (player `+0x76` bit 2) is set, `FightProcessCounter` (flag dword bits 22–25) becomes 10 and the creature enters **250**.

**250** (`0x00481CF0`, "guarding") returns 1 while an action is current; else, while `+0x7E + 50 > now` (unsigned), it starts action 43 if the creature has it (`0x004966C0`, `0x00496700`) and returns 1; otherwise 0, which is idle.
- So a dropped creature stands for **50 turns from getting up**, or until a fight takes it. Level 1's six Goblins at (15,14): 76 × 1, 255 × 4, 257 × 11, then 250 until the Lord comes into sight at 1728–1730.
- (Until 2026-10-03 this section said `SetState` does not write `+0x7E` and the stand could reach 62 turns; the stamp above was missed, and ours inherited the gap: ADR-0345.)

## Differences in ours

- **Kept arms.** The Combat Pit floor, the torture devices and the Prison arms stay the direct arms they were. A captive now goes through 1, where it went through 77.
- **Lair drops** run state 79; see lair-acquisition.md §9 and ADR-0450. Other
  unsupported room drop states still idle. The Combat Pit's 137 is built
  (ADR-0227); the Heart's 71 is built under ADR-0400.
- **Workers.** A worker getting up on a Heart runs state 71 (ADR-0400); an owned Portal/Hero Portal centre runs 197. Elsewhere it idles, although the native room-drop tail has no worker exclusion. The shared combat attempt runs before this fallback, and before keeper-worker landing dispatch (ADR-0698).
- **The stun's count and stars** (ADR-0480): 255 and 256 count from the turn after they are set; the first counted turn asks for effect 101 once only while the duration hold remains active (zero duration skips it), at the creature's position on the floor, tied to the creature's mesh point 5000. Point 5000 is a KMF CTRL point, the head: slot `+0x10C` is the engine interface's `0x0059D380` (that the callers' object is that interface is HYPOTHESIS), which searches the drawn mesh's tagged points (`0x0059D260`); an animated entry is (primitive, vertex) evaluated at the drawn frame (`0x005835E0`). The view keeps each drawn creature's head and moves the effect and its stars with it each update, as `0x00434E80` does; a creature drawn with a static mesh is not resolved, so its stars stay where they were asked for.
- **Not modelled:** the landing's sound and pit scatter (the fall is built, ADR-0730). 257's facing step is built (ADR-0698), with a map-boundary check. The Casino countdown gate is built for Portal/Hero Portal room entry (ADR-0603), but remains absent from other room-drop branches. The Call To Arms branch is built (ADR-0279).
- **257's hold** is `ActionUntilTick`, set at the stun's end to `DropLandingRules.GetUpTicks`, the Getup animation's length (ADR-0348, Q527). Classic presentation plays 255's knockdown once and holds its final pose, then plays 257's Getup once from its state-entry stamp. Action 52's art selector is 24 (`0x00495F36`), action 53's is 27 (`0x00495F6E`); independently checked 2026-10-13.
- **One word for `+0x7E`:** `StateDeadlineTick`, stamped with now by `AfterGettingUp` on the way into 250, as `0x004E8DA0` stamps `+0x2CC`.

## Built scope and verification (ADR-0698, hash/save 430)

`DropCombatRules` connects the shared target attempt to hand/level-start keeper-worker
landing and to state 257 after its facing step. `GetUpStepY` round-trips file `0x128B`.
Acquisition, approach, attacks and cleanup use the existing combat path. The helper is
built; state 257 remains partial because of its worker room fallback described above.
The fitted get-up hold remains a deviation.

The reviewer independently checked the binary and implementation with no remaining
blockers; the corrected focused fixtures pass 30 tests. Release solution build:
0 warnings/errors. Unity compile: 0 errors and 2 existing HUD CS0618 warnings.
Simulation: 4849 passed; Tools 164; Data 515; Presentation 1623; AI 412; Assets 68;
Core 60; Convert 188, all with no failures or skips. Oracle: 3 passed, 25 skipped.
These checks establish the bounded implementation, not native outcome parity.

The full Release solution test run passes with no failures: Behavior 526 passed,
5 skipped; the other suite counts above are unchanged. After the reviewer's final
scheduling correction, both focused Level 17 tests pass (2 passed, 0 failed/skipped).
The reviewer accepted the split between repeatable unaided play and controlled
Prison construction. These are remake regressions, not native outcome parity.

The original Prison-by-tick-1800 expectation exposed the diversion below: clean
pre-change source `9c153d0d` first builds at 1021, while hash 430 builds none by
1800 or in a diagnostic extension to 3600. Logs:
`build/drop-combat-level17-{baseline,current,extended}/keepers-level17-harkan-unassisted-001`.
The first event difference is a Troll 47 training sound at 177, after the get-up
step executes at 168 (the completed-turn trace labels that pose 169).

### Level 17 diversion controls

Measured remake runs, not original-game evidence. An owned source snapshot under
`build/drop-combat-investigation` retains the patch and adds diagnostic switches
only to its copied code; no switch or suppression was added to production.

| Probe change from hash 430 | Prison by 1800 | Other comparison |
|---|---|---|
| None | no | Final hash `22e544a2592a90cf` |
| Disable the new combat helper only | no | Same final hash and byte-identical event log as the complete patch |
| Disable only the get-up step | yes, first tile 1021 | Byte-identical events and keeper commands to pre-change code |
| Ignore only Temple room requests | yes, first tile 864 | Get-up step and combat helper retained |

The step changes Troll 47's training movement. The changed trajectory reaches a
missing-Temple message at tick 490, versus 1358 before the change.
Harkan takes the Temple request at 506 and routes toward the neutral Temple at
(39,39), from (12,20), instead of choosing the Prison. It has 19613 gold then,
above the Prison's 18750 estimate: lack of funds is not the immediate cause.
The expired gold-tag countdown removes the seam tags at 403 in both runs.

The pending-request priority is STRONGLY INFERRED from fresh instruction reads
of `0x004FA400`, `0x004F93B0` and `0x004FC690`; their existing implementations agree.
Thus the old seed-specific Prison expectation is not an invariant of this room
planner once the get-up motion is corrected. The precise intermediate RNG-call
divergence and matching native outcome remain unmeasured.

The unaided scenario retains digging, mining and recruitment expectations; its
repeatability test checks both expectation lists, hashes and keeper commands.
`Level17PrisonConstructionTests` retains the same seed, map, shipped things,
campaign definitions, systems, triggers and next-tick keeper scheduling, disabling
only player 4's Temple availability. It requires mining, an actual Prison build
and a keeper-issued Prison build command. The controlled fixture failed with
stock Temple availability and passed with that exclusion. This separates a
construction regression from a room-choice outcome that depends on the seed;
no production AI rule changed.
