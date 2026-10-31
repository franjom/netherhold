# Keeper management and battle selection audit

Independent Ghidra 12.1.4/PyGhidra read of the read-only `DKII.EXE` v1.70
project, 2026-09-28; §§6–7 re-read 2026-09-30. Executable MD5:
`2f3bbc21eae8c3aa468136baa303036d`. All behavioural claims here are
**STRONGLY INFERRED**, without a matching live sequence. This note records
selection and allowance rules, not proof that every requested game action succeeds.

## 1. Prisoners — `0x005006D0`

Select one random owned Prison through `0x004C6460(11)`. An empty selected
Prison ends the routine; it does not search the other Prisons. Walk its occupant
list. The first non-enemy occupant is selected immediately, with no random
test. Each enemy occupant instead gets a `random(100) < 5` selection test;
stop at the first success. At most one occupant is selected per invocation.

Selecting an occupant consumes one allowance even if its later move is refused.
Non-enemies require a Lair. Enemies use `ImprisonedCreatureAction`: 0 Torture,
1 no move, 2 Combat Pit, 3 Temple. Mode 1 therefore still spends allowance when
an enemy is selected. There is no alternate destination when the requested
room is absent.

Every move checks the dropped-counter guard, the hand, state 76, the
209/255/256/257 exclusion helper and interaction permission. See
`computer-keeper-threat.md` for these common predicates. Lair, Pit and Temple
drops use the room's stored tile plus fractional offset `0x900`. Torture asks
`0x004E8210(1,1)` for a floor or wall furniture slot and refuses a tile
containing an active creature in state 136 or effective category 15
(`0x00485210`): a torturer or torture victim. Slot selection is documented in
`workshop-crates.md` §4.

## 2. Training moves — `0x004FF5C0`

Re-read 2026-10-02 (Ghidra 12.1.4, Claude's clone; disassembly for receivers,
argument counts and branch order). **BUILT** as `KeeperTrainingStep` (ADR-0317).
Every row is STRONGLY INFERRED from the static read unless marked.

| # | Claim | Source |
|---|---|---|
| 1 | The tick draws `random(100)` from the world stream after specials; `< 50` with allowance calls readiness `0x004FAB10`, and only a not-ready army runs the moves, whose returned allowance replaces the tick's. No allowance skips readiness too. | `0x00500DF4`–`0x00500E38` |
| 2 | `0x004C6460(7)` then `(16)`: each draws `random(count)` among the player's rooms of the type (room `+0x18`) with `+0x9A` bit 0 clear, and only when the count is nonzero. That bit's meaning for these two types is unread; ours excludes only a ruined heart (ADR-0298). | `0x004FF5E1`, `0x004FF5F0`, `0x004C6460` |
| 3 | A branch is off while the keeper's `CurrentRoomToBuild` (`+0x98`) masked with `0x7F` is its room type: 16 for the Pit, 7 for Training. It is a type test, not a room test; the routine returns at once when neither branch is on. | `0x004FF5FB`–`0x004FF61D`, `0x004FF71F`, `0x004FF87B` |
| 4 | Allowance is the byte argument, tested before every creature, the first included. | `0x004FF63B` |
| 5 | The owner's list from `player+0x06`, next `+0x10`, the retreat's order; a worker (definition `+0x686` bit 0) is skipped. | `0x004FF628`, `0x004FF64D`, `0x004FFA25` |
| 6 | **Pit branch** when experience `+0x3FF` is above `3T/4` **unsigned and strictly**, `T` being `ExpForNextLevel` (`def+0x6B5`) raw at level 1, otherwise scaled by stat row 13 at the current level and clamped to 0–`0xFFFE`; **or** level `≥` variable 126 (`0x006F28FC`, u8) **and** `<` `IACreatureSkillLevel` (`+0x4B2` bits 19–22). Otherwise **Training**. Experience equal to the bar, or level equal to the skill level, is Training; level equal to variable 126 goes on to the skill test. | `0x004FF65A`–`0x004FF709` |
| 7 | A Pit-branch creature never falls back to Training: a missing or excluded Pit skips it. | `0x004FF86B`–`0x004FF887` |
| 8 | Pit only: health `+0x40` signed above `(3 × max) >> 2` (unsigned shift), max health level-scaled from `def+0x692` as `CreatureLevelRules.MaxHealth`. | `0x004FF88D`–`0x004FF8E1` |
| 9 | Both branches, in order: the dropped-counter guard (`+0x46D` above 300, or game turn at most 300); resolved category (`0x00478050`) not 13 (Training) / 16 (Pit); `+0x2BC` zero; current state's group tag (state record `+0x16`) not 7, the needs group; resolved category not 22; not in this keeper's hand (`0x004BCAE0`); state not 76; not 209/255/256/257 (`0x0048F350`). | `0x004FF731`–`0x004FF7C9`, `0x004FF8E7`–`0x004FF983` |
| 10 | `+0x2BC` is `DroppedToDoJobId` (ADR-0215): `Room_DropCreature` writes the room record's job `+0x25` or 0 (`0x004E906F`, `0x004E9084`); through the state sub-object (`creature+0x24E`, so `+0x6E`) chooser 89 clears it when the job is refused (`0x00477A57`), as do state 121's handler (`0x004739E0`, three sites) and `0x0047B532`. A creature keeps its dropped job, and is not moved, until one of these clears it. A scan for `+0x2BC` alone misses every clear. | StoreAt `0x2BC` and `0x6E`, `0x004E8DA0`, `0x004778E0` |
| 11 | Training asks permission `0x0048E6A0(keeper id)` before the slot; the Pit asks for the slot before permission. | `0x004FF7E5`/`0x004FF803`, `0x004FF9A8`/`0x004FF9BF` |
| 12 | Then `0x004BFBD0` (state 75), `0x004BC500` (lift), and only if `0x004BCAE0` finds it held, `0x004BC710` drops it at the slot. | `0x004FF810`–`0x004FF866`, `0x004FF9C8`–`0x004FFA1A` |
| 13 | One allowance is spent at `0x004FFA1F` by every path past the predicates: refused permission, no slot, a refused lift, or a drop. | `0x004FFA1F` |
| 14 | `0x004E8210(1, 0)` with ECX the room walks its slab chain from `FirstSlab` (`+0x2D/+0x2E`, tile `+0x04` next), counts tiles `Room_IsFloorSlot` (`0x00454110`) accepts, draws `random(count)` from the world stream and returns that tile's centre (`tile × 0x1000 + 0x800`, z 0). No slot: 0, no draw. For Training and the Pit the slot is an interior cell. | `0x004E8210`–`0x004E8427` |
| 15 | The drop adds `0x100` to x and y: `tile × 0x1000 + 0x900`, as the retreat's. | `0x004FF843`, `0x004FF9F7` |

Shipped data (CONFIRMED): variable 126 is 4; campaign keepers' `IACreatureSkillLevel` runs
0 to 10 (Level 17's Harkan 4, its player 5 5, Level 10's Asmodeus 10). A Pit fighter at or above
the skill level is lifted out by the retreat's fifth branch (`computer-keeper-threat.md` §4)
one step before this one, so the two alternate when the experience bound sends it back. No original
run (Q509); stock-map runs: `computer-keeper-observations.md`.

## 3. Specials — `0x00500520`

Walk owned objects and retain the last object whose descriptor Special flag
is set, whose tile is owned by the keeper, and whose virtual predicate at
`+0x74` returns zero. Only **after** the complete scan, check its object type.
The accepted ids are 8, 117–126, 128 and 129; id 127 is excluded. Submit action
88 for that retained tag if accepted. An excluded last object suppresses an
earlier accepted one; this is not a last-activatable-object search. The tick
charges no allowance for this routine. RTTI resolves the object's primary vtable
to `0x0066D654`; slot `+0x74` calls `0x004B7250`, which returns bit 1 of the
object dword at `+0x146`. The gate is therefore a concrete bit test; its wider
state meaning remains **UNKNOWN**, and must not be replaced by guessed ownership
or pickup eligibility.

## 4. Battle spells — `0x00503060`

Re-read 2026-09-29 (Ghidra 12.1.4, Claude's read-only project) and **BUILT** as
`KeeperBattleStep` (ADR-0283). The tick (`0x00500C20`) reaches it at step 11
only with allowance left and the fight flag (bit 3) set; otherwise step 11
makes imps. The message walk sets the flag; only this routine clears it.

Clear the fight flag and walk own creatures in list order. Effective category
22 (`0x00478050`) sets it again. A candidate for emergency Call to Arms must
have an existing target (`+0x502` through the liveness table) with an owner,
no active CTA (bit 7) and a zero attack cooldown (`+0x4F3`). Strength is
`0x004FAC40` over that target owner's creatures within 12 tiles, without
traps; other enemy players are not summed, and the 12-tile square overflows
(`computer-keeper-threat.md` §2), so the whole map counts. It must strictly
exceed `BACallToArmsValue`, and the fighter's terrain must lack flag bit 8
(`Lava`, world slot `+0x68` → terrain `+0x17B`). The stored CTA position
(`+0x4E`) is written before the later checks. Require mana **strictly
greater** than `0x004BE560(3)`'s cost (the human's own cast passes on
equality), the world turn (`0x00517490`) divisible by 40, availability 3,
and `0x00452970`'s route from two tiles east of `DungeonHeartPosition` to
the fighter for the player with options 1,0,0,0. A cast `0x004BBD30`
accepts costs one allowance, sets bit 7 and `CallToArmsCounter ←
RemoveCallToArmsAfter`, and returns at once. Every fighter before the first
that passes the spell roll gets such an attempt.

Otherwise `random(4) >= UseKeeperSpells` permits a spell-selection attempt,
and that fighter ends the walk whatever comes of it; a fighter failing the
roll is passed over. `random(4) < AttackingVsDefensiveSpellPreference`
selects the supportive arm; otherwise offensive. Both enumerate spell ids 1
to the descriptor count (slot `+0x114`: 13 shipped, so id 14 is never
reached), require availability 3, no cooldown, the corresponding descriptor
flag word bit (`+0x54` ← file `0xDC`: 8 supportive, 4 offensive), and bit
16 clear. Shipped: Thunderbolt, Inferno and Chicken are offensive, Heal is
supportive, Tremor carries 4 and 16 (CONFIRMED from `KeeperSpells.kwd`).
With `q = manaCost / 2499`:

- Supportive: retain each eligible spell **before** testing
  `random(q+1) >= 4-SpellPreference`. Stop on success; if no roll succeeds,
  the last eligible spell remains selected.
- Offensive: retain the first eligible spell whose
  `random(q+2) >= 4-SpellPreference` succeeds. If all rolls fail, no spell
  is selected.

This is ordered selection with different fallback rules, not one weighted draw.
Supportive targets are own creatures strictly within five tiles, strictly
below half health and without the dying flag. Offensive targets are creatures
of the fighter's target owner strictly within five tiles; this loop adds no
health or dying filter, and neither scan skips a held or carried creature.
Every accepted target replaces the previous target; `random(100) > 75` stops
either scan. With a spell and a target, slot `+0x250` (`0x005177B0`: byte
`0x11` of the target-validator entry, set for selectors 1, 2, 3 and 6) picks
the thing cast: `0x004BBA40`'s permission, then `0x004BBF90` with its return
ignored; selectors 4 and 5 cast at the target's position through
`0x004BB8A0` and `0x004BBD30`, each of which must return nonzero. A refused
permission returns the allowance unspent; a cast spends one. At most one
spell is attempted per call.

Built: the whole walk, with Thunderbolt (ADR-0283), Heal (ADR-0287,
`keeper-spell-casting.md` §4), Chicken and Inferno (ADR-0302,
`keeper-spell-inferno-chicken.md`) the spell arm casts the simulation executes.
Ours predicts the permission from the observation and issues the cast a tick
later; the original runs are Q485, Q491 and Q498.

## 5. CTA removal — `0x004FEFC0`

Called from step 7 while bit 7 is set and allowance remains; it spends none.
Remove when the timer is zero, or the strict-radius enemy count is below the
configured minimum, or the total own nonworker count is below its minimum.
Counts exclude map layer 2. The enemy walk takes every player in the manager
list that `0x004BF390` calls an enemy (another tag, alliance byte zero), heroes
and neutral included, and adds each such player's Heart room (`0x004C6460(5)`
with that player as receiver) if its stored centre lies within the radius of
the stored position's tile centre, `RemoveCallToArmsWithinRange`² × 4096
against the shifted squares. The own count has no distance requirement.
Clear bit 7, submit action 63 using the player's CTA tag, and set the attack
cooldown to `WaitBeforeAttackingAgain * 4`. Those AI state changes do not
depend on the command's return value. Built (ADR-0283); ours waits for the
observation to show the call before testing, since its cast lands a tick late.

## 6. Attack planner — `0x004FAEF0`, readiness `0x004FAB10`

Re-read 2026-09-30 (Ghidra 12.1.4, MCP and runner, Claude's clone) and **BUILT** as
`KeeperAttackPlanner` and `KeeperReadiness` (ADR-0298). Q497 is the original run.

| Claim | Source | Result | Status |
|---|---|---|---|
| Gates | `0x004FAEF0` | NeverAttack (`+0x4B6` bit 30), a nonzero attack wait (`+0x4F3`) or a call up (bit 7) refuses before readiness runs | STRONGLY INFERRED |
| Readiness | `0x004FAB10` | non-workers (definition bit 0 clear) strictly above `IAMinimumCreatures`; under-levelled means level below the four-bit skill; when under ≤ skill and under < count, each under-levelled one gets one `0x0048B120`, then any left under refuses. Called before the rooms test, and by the tick's training step (`random(100) < 50` with allowance) and the Train predicate | STRONGLY INFERRED |
| Rooms, research | | `IAAllRoomsPlaced` wants `AllRoomsBuilt`; `IAAllSpellsResearched` bit 0 with a spell at availability 2 among ids 1..count refuses, calling `0x004FA8D0` first when a Library exists | STRONGLY INFERRED |
| Modes | | attacker-only takes `AttackingPlayerId` (`+0x08`, a tag) when `HaveBeenAttacked`, clearing both, else nothing; otherwise ordinary; superiority is `IAThreatSuperiority` | STRONGLY INFERRED |
| Stored tunnel | | an enemy with `TunnelsLeft` 0 resumes its first stored tunnel (start X ≠ 0): task 7, next 4, `DigRoomAt` = end, `DigRoomAtCurrent` = start, the start cleared; ordinary mode writes the wait ×4 and clears it again when another of that enemy's tunnels remains, inside the player scan before any strength test; attacker-only writes no wait | STRONGLY INFERRED |
| Strength | `0x004FBDB0` | the whole owned list, workers included, level-scaled; ordinary: enemy × superiority / 100 ≤ own; attacker-only: own ≥ attacker × superiority / 100 | STRONGLY INFERRED |
| Ordinary scan | manager list, `+0x3A` | skips status 1 and 3, ids 1 and 2 and non-enemies (`0x004BF390`); the nearest `DungeonHeartPosition` (`+0xB43`, the heart thing's tile, `0x004C5F60`) by shifted squares, strict less | STRONGLY INFERRED |
| First attack | | `FirstAttackFlag` bit set; end by `IAFirstEnd`: 0 random Heart room, 1 random room by index, 2 least own strength within five tiles (strict below `0x7FFFFFFF`), 3 greatest (strict above 0); then start by `IAFirstStart & 3`: 0 `0x004FADF0` (furthest position from the heart, strict above 0), 1 random room, 2 random Heart, 3 `0x004C6010` nearest the end's first slab; a missing room refuses after the bits | STRONGLY INFERRED |
| Later attack | `0x004FB579`, `0x004FB58B` | a random own room drawn first, then a random enemy one | STRONGLY INFERRED |
| Attacker-only rooms | `0x004FB9C1`, `0x004FBA2D` | end: the attacker's non-bridge room nearest the own heart, `DigRoomAt` its first slab; start: the own non-bridge room nearest that slab, the end room itself when none | STRONGLY INFERRED |
| Start | | task 7, next 4, clock reset, wall hug off; `DigRoomAt` = end position, `DigRoomAtCurrent` = start position; `TunnelsLeft`−1; the nibble of `+0x4BA` = id + 1 | STRONGLY INFERRED |
| Staging | usage 0, tunnels used up, no call | `CallToArmsPos` = heart (x+2, y+2); the first own creature lacking definition bits 4 or 5 (else the list head) routes (`0x004D0E60`)? then the start room's position; then its slabs in chain order; the last tried stays | STRONGLY INFERRED |
| Random room | `0x004C63D0` | `random(count)` over the whole list; a bridge at that index yields nothing | STRONGLY INFERRED |
| Room position | `0x004E4590` | `CentrePos`: the slabs' centroid when it is a room tile, else a spiral from it; a Portal's or Hero Portal's first slab +1; the Heart's first slab +3; ids 18–24 the first slab | STRONGLY INFERRED |
| Random room of a type | `0x004C6460` | uniform among the player's rooms of the type whose ruin bit (`+0x9A` bit 0) is clear; the research gate draws a Library this way before its move | STRONGLY INFERRED |
| Room order | `0x004E3800` | a new room is linked at the head of its owner's list: newest first | STRONGLY INFERRED |
| Breach counts | `0x004F8993` | `BreachAtXPoints & 0xF` into all sixteen | STRONGLY INFERRED |
| Tunnel store | `0x004FDC50`, `0x004FDB90` | with the nibble set, an owner `0x004BF390` calls an enemy on any of the nine tiles around the cursor stores start = cursor, end = target in the first empty slot, clears the nibble and ends the task | STRONGLY INFERRED |
| Neutral land | `0x004BF390`, `0x0051DC90` | every shipped level allies each keeper with the neutral player (id-66 records, [alliances.md](alliances.md) §2), so `0x004BF390` calls neutral land no enemy: an attack tunnel does not stop at its first step onto unowned ground | CONFIRMED |
| Heart room extent | | the +3 rule centres a seven-by-seven room; `RoomIndex` sees five by five, so ours lands one tile inside the corner | UNKNOWN |

## 7. Front and attack management — `0x004FE9C0`, `0x004FECE0`

| Claim | Source | Result | Status |
|---|---|---|---|
| Tick order | `0x00500C20` step 7 | management when `PlayersAttackedFlags` ≠ 0, then the removal test when bit 7, then the retreat | STRONGLY INFERRED |
| Management | `0x004FECE0` | usage ≠ 0 with a call up: the removal test only; else, with no wait and a front: availability 3 admits `0x004BBD30(3, pos, 0)`, whose success sets the counter and bit; usage 1 with fewer than three events queues half the army's threat at an owned, open drop site by the room reader's score; a front costs one allowance whatever the cast did | STRONGLY INFERRED |
| Front | `0x004FE9C0` | per set bit 0..15 (the null test on the player slot's address never fires, so a bit is never cleared): a random Heart room; two `random(2)` put the point two tiles off its position on each axis; the walker must route; usage ≠ 0 stores it; usage 0 with no call returns true unchanged; with a call, only when non-workers ≤ floor(5 × those strictly within ten tiles / 4) and `0x004D0E10` (mode `0x2000`) routes: the first waypoint | STRONGLY INFERRED |
| Refused staging | `0x005179D0` | the cast at a Temple, Prison or Pit interior or the heart's inner square is refused and the allowance spent each turn; a heart room's own CentrePos is inside that square, so `IAFirstStart` 2 stages nothing | STRONGLY INFERRED |

Deviations (ADR-0298): the one-tick delay and the keeper's RNG; a room's first slab is its
first tile and its list order the capture's scan; routes run over the captured bytes, whose
router selects gate midpoints as mode `0x2000` does. Tests: `KeeperAttackPlannerTests`,
`GrantCreatureLevelTests`, `level18_nemesis_allies.yaml`, `level11a_drako_attack.yaml`.

## 8. Slapping — `0x00502F00`

The actual gates are accessor `0x00478A20 == 5`, health strictly above half
maximum, creature `u16 +0x459 == 0`, then `random(100) == 0`. Each submitted
action 62 spends one allowance. `+0x459` is `SpeedChangeTimer`, not
`LastSlapTick` or `WorkFasterTimer` (`hand-of-evil.md` §§4–5).

The accessor's 5 does **not** mean working. State-object `+0x3C` values 1/2
override it with 26/25. For nonworkers it reads the category's `+0x3C` default,
substituting the resume category during movement. Reconstructing the runtime
table from `0x004798D0..0x004805F0` confirms idle category 1 returns 5, whereas
research/manufacture return 20 and training returns 21. For workers, nonzero
creature `Idle +0x3D1` or state-object byte `+0x98` (creature `+0x2E6`) `== 6` returns
5; otherwise effective combat returns 4 and other categories return 6. **That byte is
the imp's last task kind**, CONFIRMED (2026-10-08): each task handler writes it as it takes the task
(`0x0046AEA0` writes 6 for a reinforce and `0x10` for a repair) and state 10 zeroes it before it
chooses again (`0x00464010`; state 11 on a drop). It is not a working-state test: an imp keeps kind
6 on its walk, at the wall, and in a fight it is drawn into, until its next state 10. The Level 11A
capture (2026-10-06) shows it: of 57 entries into 77 in ticks 1-1,400, 53 are imps', from 6, 10, 12,
22, 87 and 199 (Morgana's in the hole at 437 and 444); the turn before, 42 had kind 6 and 10 had
`+0x3D1` set, and the last was wandering in 12 with neither (HYPOTHESIS: set within the turn the
capture sampled before). Over
the same ticks the four keepers' imps spent 6,429 imp-ticks with kind 6 or `+0x3D1` set and
`+0x459` zero, and were slapped 53 times, 0.82 %: consistent with the 1-in-100 roll if the step
runs every tick, which is unread (the health gate and `+0x3C` are not in the count); the human
(player 3) never. So the keeper slaps an imp with no task or whose last task was a reinforce, never one whose
last was a dig, mine, claim or repair. Built 2026-10-06 as `CreatureActivityRules` (ADR-0376),
then on ours' `LastTaskKind` with kind 16 for a repair (ADR-0405; it had tested a held reinforce
target, so a fighting or repairing imp differed). The runtime table `0x006996C4`, which
`0x004798F0` fills, gives 5 for categories 1, 2, 10, 19, 23 and 26-30, 2 for 0, 0x14 for research
and manufacture, 0x15 for training, the combat pit and 31. Our slap arms `SpeedChangeTimer` as DK2's does
(`hand-of-evil.md` §5, ADR-0612), so the step tests the timer alone.

## 9. Traps and doors — `0x005000A0`, `0x004FFD80`

**BUILT** as `KeeperTrapDoorStep` (ADR-0561). Records: [005000A0](functions/005000A0.md) (the step and
its gate at `0x00500E62`), [004FFA50](functions/004FFA50.md) (site test),
[004FFC40](functions/004FFC40.md) (spacing), [004FFD80](functions/004FFD80.md) (door or trap).

The rectangle uses minima/maxima of the owned rooms' stored positions, not all
their slabs, with the random margin and map clamp described in `KEEPER_AI.md`
§5.6. The west-edge pass examines **only its first tile**: `0x0050021D` loads
the minimum Y; `0x0050025F..0x00500262` increments it and compares against that
same minimum. East, north and south have full inclusive loops. Corners can be
visited again. No original observation yet tests this asymmetry.

Selection scans type ids in ascending order and remembers each eligible candidate
before its random stop test. Thus the final eligible candidate is a fallback.
Door definitions with `+0x8B & 0x40` are skipped. Trap type 2 receives the boulder
branch only when type 2's availability is 3. With the long-corridor flag, four
further valid corridor tiles in any cardinal direction make that branch stop;
otherwise its stop test is `random(4) > preference`. Other trap types with
descriptor `+0xBD == 1` stop on `random(5) > preference`, the rest on
`random(5) < preference`. A chosen type still requires its world placement
predicate; after that passes, dispatch costs one allowance even if dispatch fails.

Level 17 (three identical idle DK2 captures, 2026-10-28). Harkan's PLAYERS record has
DoorPreference 0 and TrapVsDoor 4, so a door is laid whenever a site passes and each eligible
door stops the scan 1 time in 100. He lays a Magic Door at tick 204, a Wooden Door at 915 and a
Magic Door at 1182 (sites (19,4), (15,20), (15-16,2)); ours picks the same sites. The Wooden Door
is STRONGLY INFERRED to be one of the 1-in-100 stops: the captures are one sample and his door
availability at 915 is not captured. It leaves 12,650 gold when the Training Room expansion is
decided against 500 x 25 = 12,500, which is why DK2 grows Training 12 -> 20 tiles at 1111-1138.
Ours, 10 seeds x 3000 ticks: the usual Magic Door leaves the keeper short at that
decision and the planner drops the request, so Training grew in 0 of 10 (9 of 10 without the step)
and Harkan's Prison finished in 9 of 10 (10 of 10). Evidence paths: ADR-0561.

## 10. Room expansion — `0x004FBE00`

The slab-chain extrema are the smallest/largest `x+y`, not independent X/Y
bounds. The routine tests north, south, west, then east and retains **every**
accepted extension. It does not choose one winning strip. West/east use the
possibly extended north/south limits. Each scan excludes its high endpoint;
the final planned rectangle includes it. A one-tile extent therefore skips
that axis's validation loop, and an empty loop accepts its side untested. A
strip tile passes when its terrain is taggable and not impenetrable, has
`+0x1CB` bit 0, or is the keeper's own with no room on it (solidity untested).
The gap line two tiles out allows at most one sample that is a kind-1 or kind-2
task of the keeper (`0x00508290`) or not taggable. Bounds/terrain failures reject
that side, not earlier accepted sides. If any side survives, start task 2 over the
combined rectangle with positive X/Y stepping, `NextTask` untouched. These are
binary predictions, rechecked in instructions 2026-10-02; an irregular-room
experiment would test the extrema and excluded endpoints.

## 11. Research slot and job placement — `0x004FA8D0`, `0x00503690`

The slot selector excludes workers, counts creatures whose job list contains
Research, and keeps a non-researcher with the smallest descriptor byte `+0x787`.
Equal values replace the candidate, so the **last** minimum wins. The byte's
meaning remains **UNKNOWN**. Fewer than two researchers and a candidate are
required. Prefer a Portal; fall back to a Hero Portal only if none exists.
`0x004DC760` counts both kinds for capacity; `0x004DCCE0` counts creature
state-flag bit 1. Their comparison admits eviction at or above capacity.
There is no allowance deduction inside the eviction helper. A selected candidate
that fails the drop/state/interaction gates does not cause a second selection.

Job placement first tries the hand's last item. A failed room/job match spends
allowance while retrying that same item, rather than advancing to earlier items.
Successful drop rereads the hand count. After this loop it draws `random(100)`
even with no allowance left; only a result below 10, remaining allowance and free
hand capacity permit the owned-creature pickup scan. That scan also requires
accessor 5 (§8), the dropped-counter guard, a zero dword at creature `+0x2BC`,
and its state exclusions. A successful pick costs one allowance; dropping that
creature is left to the hand-processing path on a later invocation.

The job table initializer `0x004F8180` confirms the four room mappings in
`KEEPER_AI.md` §2.3. The Research predicate `0x00502E90` is false without a
state-2 spell. With one, it calls eviction if a Library exists and returns true.
The Training predicate `0x00502EF0` negates readiness, including that routine's
direct level-up side effect. Thus predicate evaluation is not observational.

The caller `0x00500C20` draws the training roll and the research roll even with
zero remaining allowance. The trap roll is budget-gated. The job helper's own
10-percent roll is separate from these. An implementation must preserve these
draw sites if it intends to match the original command stream.

## 12. Gold deposit retention — `0x00501C10`

The four stored entries are sorted by squared distance to the nearest owned
room's stored position, but are **not always the four nearest deposits**.
The admission cutoff starts at zero (`0x00501C1D`), increases when an admitted
distance is larger (`0x00502165..0x00502172`), and never decreases after an
eviction. After four admissions, a new distance must be strictly below that
historical cutoff (`0x0050215A..0x00502163`). Insertion starts at slot 3 and
shifts only while the predecessor is strictly farther. Equal distances retain
encounter order; a later equal-to-third candidate can overwrite the fourth.

Discriminating prediction: encounter distances `1,4,9,100,16,25` leave
`1,4,9,25`, not `1,4,9,16`. This follows from the instructions, not a live test.
`GoldScanner` implements this retention rule with discriminating regression tests.

The instruction trace `0x00501D26..0x00501F15` checks terrain `u16 +0x185 != 0`,
without a solidity test, and visits cardinal neighbours through `0x006BC350`.
The seed contributes count 1 and its X/Y; each newly visited gold tile adds one
and its coordinates. Integer division gives the unweighted centroid. The stored
dword is the **sum of terrain gold values**, not the tile count: seed load at
`0x00501D83`, accumulation at `0x00501ECB..0x00501ED8`, store at `0x005021CC`.

If the centroid is out of bounds or has terrain flag bit 1, a square spiral
starts one tile east and accepts the first in-bounds tile without that bit.
It does not require the replacement tile to contain gold. The extent limit is
twice the larger map dimension (`0x00501F40..0x00501F59`); exhaustion sets X to
zero and preserves the low word of the accumulated Y sum, not the centroid Y
(`0x00501EFE..0x00501F15`, `0x005020AF`, `0x005021C2..0x005021C7`).
These details required instructions because the decompiler conflates
stack variables during this scan. Behaviour at gold on the map border remains
**UNKNOWN**: the neighbour walk has no explicit bounds call.

Implementation limits: room distance uses the bounding-box centre rather than
the original stored room position; the flood walk explicitly bounds neighbours.

## 13. Mood remedies — make happy `0x004F9710`, dispose `0x004F9520`

Read whole 2026-10-04. **Make happy** returns success (handled) for a type other
than 17/18, a creature tag naming nothing, or band (`+0x28A`) zero. Otherwise
`0x004F9772..0x004F9786` passes creature `+0x26E` to `0x004871B0`, which scans the
six counters and replaces its index only for a strictly larger value: ties keep
the lowest index. The remedy is chosen by that **current** bucket, never by the
message's Data1 selector or a bucket cached at insertion. A discriminating case
retains a hunger message while Lair anger becomes the largest: the Lair is chosen.

- **0, 4, 5 — pay.** Needs a Treasury (`0x004C6460(1)`, a random one; the draw is
  the only use). The wage is `Pay` (def `+0x69A`) at level 1, else `Pay` times the
  level table's percentage over 100: our `PaydayRules.Wage`. `Player_ChangeGold`
  takes it with flag 0 and no tile: share = remaining / (Treasuries + 1); each
  Treasury or unruined Heart in the room list gives min(share, its gold), or up to
  the whole remainder when the share is 0, walking its slab chain from the first
  slab one gold pile per slab (`0x004F4510`); passes repeat until one takes
  nothing. It returns what it took, which may be less than the wage. Zero fails.
  Otherwise `0x004B7A90` makes a gold object holding that amount, flags `+0x146`
  bit 4, puts it in the hand, earmarks it for the creature (`+0x142`) and, when
  the hand holds it, drops it at the creature's position: the landing pays it
  (`hand-drop-earmark.md`).
- **1 — a chicken.** A random Hatchery's object-list head (`+0x0C`); none fails.
  The chicken is lifted, unlinked from the room (`0x004E6130`), earmarked and
  dropped on the creature.
- **2 — the Lair.** A random Lair; no slot or capacity test. If the creature may
  be lifted (`0x0048E6A0`), `0x004BFBD0` prepares it, the hand lifts it and, when
  held, drops it at the room's position (`+0x27`) plus `0x900` on both axes.
- **3 — a job room.** For each happy-table entry in order (def `+0x628`, count
  `+0x78B`), job ids below 10 map through `JobRooms` (`KEEPER_AI.md` §2.3; the room
  byte only, so job 7 is the Torture Chamber): a random room of that type, then
  the dropped-counter guard (`+0x46D` above 300, or game turn below 301) and
  `0x0048E6A0`; the first that passes is lifted and dropped as for the Lair.

A failed remedy returns handled only while `NumberOfEventTasks` (keeper `+0`
bits 18–21) is below 3; it never queues an event itself.

**Dispose** takes Data2's creature. With `AngryDisposalMethod` (`+0x4B6` bit 18)
and a Temple, it draws a floor slot (`0x004E8210(1, 0)`) and, when the creature
may be lifted and the hand holds it, drops it there plus `0x100`: success.
Otherwise, or if that failed, it takes a random Temple, else a Portal, else a Hero
Portal (none: returns 0, unhandled and retried) and drops at that room's position
plus `0x900`. A refused lift returns 0.

Ours: `KeeperMoodRemedies` with `MintWageCommand` and `GoldStore.Spend` (ADR-0353). The
Hatchery's list head is taken as its newest chicken (`EatingRules.NewestChicken`); our
object drop needs the keeper's own tile where DK2's does not (Q541).
