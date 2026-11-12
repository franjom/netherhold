---
systems: [combat, creatures, simulation-core]
---
# A speed a handler sets is not modified that turn

## Observation

Level 5 seed 1 (`level5.dk2cap`): hero 434 stands in water (terrain id 4) and
enters its fight on 1728. That turn creature `+0x3BB` is 1, `+0x3BF` is 1, the
body cap `+0x206` is 17680 (the definition's run speed, not halved) and its
velocity is 17772. On 1729 `+0x3BF` is 0 and the cap 8870, the run speed halved
by water. Hero 435 does the same on 1731 (17856, then about 8940). The
re-entries into state 8 from state 199 on 1733 and 1735 have `+0x3BF` 0 and
about 8900. CONFIRMED in the capture, for the state-199 entry-turn run in water
only.

## Reading

STRONGLY INFERRED, from reads of [Creature_Update](functions/00488930.md) on
2026-11-10. The update clears `+0x3BF` at `0x00489619` and keeps the speed the
getter returns at `0x00489623`. It then applies its modifiers in this order:

1. low health, half speed, `0x0048962C..0x004896C2`
2. water, half speed, `0x0048970B..0x00489749`
3. lava, 150/100, `0x0048984C..0x00489881`
4. player bonus, `0x004898B0..0x004898E6`
5. possession and stat, `0x004898EC..0x0048994F`

The action runner `0x00489A68`, ProcessFight `0x00489AC7` and the state
dispatcher `0x0048A030` follow, then the physical pass `0x0048A204`, the
steering gate and the commit `0x0048A457`. The restore at `0x0048A45C..0x0048A47A`
sets the getter's speed back when `+0x3BF` is set, else the kept speed.

[Creature_SetSpeedMode](functions/004887D0.md) is unconditional: it stores the
mode, calls the setter [Creature_SetSpeed](functions/0048D010.md), which has no
equality test, and sets `+0x3BF` in every case (STRONGLY INFERRED). So a call
from a handler gives that turn's move the definition's speed, and the next
update modifies it again. Possession and stat is a pre-handler modifier like
the others (STRONGLY INFERRED). `+0x3BF` therefore protects only writes made
inside the update. A Haste or Slow speed persists through the kept read at
`0x00489623`, the speed the previous update restored, not because it sets
`+0x3BF` ([creature-status-effects.md](creature-status-effects.md) section 2).

The five calls that carry the mark in ours are
[state 199's](functions/0045EB20.md) entry-turn run `0x0045EBC9` (only this one
is capture-backed), [spFighting's](functions/0045F030.md) step-back shuffle
`0x0045FFC9` and run `0x0045FFF6`, and
[the flee state's](functions/004602E0.md) shuffle `0x00460806` and run
`0x00460880`. The sites and modes are STRONGLY INFERRED.

## Built (ADR-0737, hash/save 464)

`CreatureState.SpeedSetTick` (hashed, saved, copied) and `SetSpeedMode(mode,
tick)` store the turn of the last handler call. `CreatureMovementSystem.Sync`
returns after the status cap and before `WaterSpeedRules.SpeedCap` and
`PossessionRules.ApplySpeedModifier` when the mark is this turn. The five sites
above call `SetSpeedMode`.

## Low health: half speed and slower recharge (ADR-0756, hash/save 481)

STRONGLY INFERRED (reviewer re-read the disassembly of `0x0048962C..0x00489705`). A worker
(definition `+0x686` bit 0; `0x00489632..0x00489639` jumps past the branch) is skipped. Otherwise
the maximum health is definition `+0x692` (`Hp`), raw at level 1 and else scaled by the Health stat
row at `[0x006E4154] + 0xE1 + level * 2`, divided by 100. When `max * 20 / 100` is above the health
(`+0x40`, signed, strict) the getter's speed `* 50 / 100` goes through the setter `+0x8C`, before
water. On odd turns (`0x00517490 & 1`) `+0x41B`, `+0x41F`, `+0x423` (the three spell stamps) and
`+0x427` (the melee stamp) each go up one, so a weak caster recharges at about half rate. Natively
there is no zero guard; ours leaves its `NeverUsed` sentinel alone (DK2's 0 to 1 still reads as long
ready).

Capture: Level 5 seed 1, Thief 434 (150 of 1500) in state 8 from 1851. Native velocity is exactly
half ours. The emulated `Body_Integrate` (`0x004D5A40`) on the 1850 image gives ours' (759,-3363)
and native's (379,-1682) with body `+0xE2` = 4912. Study
`F:/DK2Evidence/studies/q712-thief-run--2026-11-12--001`.

Built: `LowHealthSpeedRules.IsWeak` / `SpeedCap` in `CreatureMovementSystem.Sync` before
`WaterSpeedRules`, under the `SpeedSetTick` guard, and `DelayRecharge` at the end of
`CreatureStatusSystem.Update`, before the state handler. Tests: `LowHealthSpeedTests`.
`scenarios/keepers/level17_player5_retreat.yaml` runs 200 turns because its 60-of-4000 Black Knight
walks to its bed at half speed.

## Deviations

- 17 of DK2's 22 call sites of `0x004887D0` are unmarked in ours:
  `0x004617BC` Fight_WithdrawState, `0x00461857` State202_Withdrawn,
  `0x00475482` RaiseAlarm, `0x00475886`, `0x004759B5`, `0x0047A992`
  CombatCategory_Leave, `0x00455E77`, `0x00455E97`, `0x0049AF4A`, `0x004A8A20`,
  `0x004D9A34`, `0x004874B2`, `0x0051A6FC`, `0x005172F3`, `0x00517302`, and
  `0x00461B0F` and `0x00461B25` in Creature_FollowPossessedState. That function
  calls it every run, so an influenced follower's modifier is overwritten
  natively and not in ours.
- Creature action 33's call (`0x0049AF4A`, ADR-0739) keeps the mode (`CreatureState.Running`)
  and not the mark. The creature's own root walk is at `0x0048A49D`, after the restore at
  `0x0048A45C..0x0048A47A`, so a mark set there is cleared by the next update's
  `0x00489619` before it shields a move; the one reader of `+0x3BF` found is the restore
  ([record](functions/004887D0.md), STRONGLY INFERRED). A tree fired from an event inside
  a handler would mark before the restore; that case is unread (one Level9 record under
  event 6).
- Haste and Slow (Q399) are unchanged, with one numerically different case: a
  hasted creature in water on a marked turn is 1.5 x base in ours and base in
  DK2.
- The lava modifier remains unbuilt.
- Not settled: reuse of the kept-speed slot in `0x00489F98..0x00489FFF`;
  whether the action runner or ProcessFight reach `0x004887D0` indirectly; the
  state id of Creature_FollowPossessedState's handler.

## Control

Level 5 seed-1 replay, controlled466-v3 (verifier scripts466c, study
`F:/DK2Evidence/studies/q712-water-speed-turn--2026-11-10--001`): 6764 ordered
draws match through turn 1788, from 6120 through 1755. The Workshop built on
1782 needed its draw sites mapped in the verifier; tooling only.

Next boundary: turn 1789 draw 4, native `State.cpp:1700` where ours draws
`Room.cpp:1509` for Warlock 438's Lair search. In DK2 438 already stands in the
Lair (state 80 on 1789, bed 1611 on 1793). Its path first differs on 1737 through
crowding with Warlocks 437 and 439 walking beside it; 439's first difference is
one unit on 1726, its third step in state 7 (DK2 64396,144462; ours
64395,144463).
