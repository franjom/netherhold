# Mechanic: the fight approach and victory pause

Source(s):
- `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-23. Addresses are
  facts about the binary in our own words; no decompiler output enters this
  repository (ADR-0046)
- `creature-state-table.md` and `creature-action-table.md` for the independently
  recovered state and action records
- `balance-variables-in-the-binary.md` for variables 213 and 214

Original version: DK2 v1.70 (GOG)

Questions: answers Q368 and Q369; opens Q376 and, with the build, Q400. `combat-fight-state.md` owns the
fight state around these two branches, `combat-fear.md` the fear routines, and
`combat-acquisition.md` attackability and the per-tick fight scan.

**What this settles:** state 8 reselects an ordinary creature target every tick,
tests combat reach every tick, gives up a retreating target after 3 or 7 seconds,
and refreshes its route every ten handler calls. A kill normally starts action 55
in state 260; the pause lasts for that action's animation instance, not a fixed
number of game turns. `NeedBodyForFightIdle` instead walks to a nearby neutral
dead body and starts the same pause there.

---

## 1. Entering state 8 (`0x00471D10`)

The approach has one caller, `spFighting` `0x0045F030`. Its three stack
arguments are the target, `AttackRangeSquared`, and the caller's current state
id, **199**. The last was previously mistaken for route options. The function's
`RET 0x0C` confirms three stack arguments.

It copies the target's position, requires `0x004B35C0` to accept it, and requests
a route with `0x004D0F60(..., 2)`. If an exact route to a door fails, it tests the
four adjacent tiles with `0x004D0E60(..., 0)` and installs the first successful
one with `0x004D0F60(..., 0)`, offset a quarter tile back from the door.

On success it enters state 8 and writes:

| state field | value |
|---|---|
| `+0xDF`, `+0xEB` | next/resume state **199** |
| `+0xE3`, `+0xE7` | installed destination x/y |
| `+0xEF` | target tag |
| `+0xF1` | attack range squared |
| `+0xF5` | **10**, the route-refresh countdown |

This entry calls the route request and state transition directly. It does **not**
call the generic route-and-enter helper `0x00471890`.

## 2. State 8 before movement (`0x00470E30`)

The handler runs these checks in order.

1. A target absent from the live-tag table is unlinked and the approach ends via
   state 199.
2. For an ordinary creature target — target kind 0 and
   `AttackAbnormalTargets` clear — it unlinks and calls `SelectAVictim` every
   tick. It continues this approach only if selection returns the same tag. A
   different target or no target ends the route and resumes state 199, where the
   new engagement is processed.
3. When `0x00460C50` finds an escape tile, `0x0045EC80` advances the same
   `OverThreatCounter` used by `spFighting`. Reaching the threshold computes the
   same `BackOffTimer` and enters state 203. Unlike `spFighting` step 6, state 8
   does **not** call the low-health withdrawal test `0x0045EDA0`, and it zeroes the
   timer only for an `AlwaysFlee` definition (`combat-fear.md` §3.4).
4. For a creature target walking toward state 202 or 251, a chase counter at
   `creature+0x53D` advances. It is otherwise cleared. Exceeding variable 213
   `GoodCreaturesChaseEnemyDurationSeconds` (3) for the Good player, or variable
   214 `EvilCreaturesChaseEnemyDurationSeconds` (7) for anyone else, multiplied
   by the level tick rate, records the target as `RetreatingCreature`, unlinks,
   ends the route, and resumes state 199.
5. It measures fixed-point squared distance to the target's current position.
   Strictly inside `+0xF1` and with line of sight, it ends the route and resumes
   state 199 immediately. The sight call uses its door flag only for target kind
   4.

The ordinary target re-selection is broader than `spFighting`'s Support-only
in-reach re-selection: state 8 does it for every non-abnormal creature target.

## 3. The ten-call route refresh

If the reach test did not finish the approach, `+0xF5` is decremented. A nonzero
value delegates immediately to the shared movement handler `0x00470C80`.

At zero, the handler builds a fresh destination from the target's current
position. A Flanker (`def+0x67E` = 3) chasing a creature (kind 0) uses a point
behind the target's heading `h` (target `+0xF4`, 2048 to a turn): `d` = `0x2000`
(two tiles) when the attack nibble (`+0x529 & 0xF`) is 0, else `0x400` (a quarter
tile), and the point is `(tx − (d·S[h])>>16, ty + (d·C[h])>>16)` with 64-bit
products. `S` (`0x006C1488`) and `C` (`0x006C1C88`, `S` shifted 512 entries) are
2048-entry 16.16 sine and cosine tables (`S[1]` = 201, `C[0]` = `0x10000`; the
other entries inferred from that shape). The heading's forward vector is
`(sin h, −cos h)` (0 is −y, `0x200` +x), so the point is `d` behind the target's
facing, moving or not. A point `0x004B35C0` rejects is replaced by the target's
position. Other fight styles use the exact target position.

Unless the stored destination already equals the attacker's current position,
it requests a new route with `0x004D0F60(..., 0)`. Success stores the fresh
destination, resets `+0xF5` to 10, and continues through `0x00470C80`. This is a
periodic refresh even when the target has not changed tile. An invalid
destination or failed route ends the route and resumes state 199.

The handler never calls `0x00471890`: entry and refresh both use direct route
requests, and every exit uses `SetState(state+0xDF)` directly.

## 4. Arrival and state 9

State 8 can return to state 199 in two ways: its own strict range-squared plus
line-of-sight test fires before movement, or `0x00470C80` reports the installed
route complete and enters the state held at `+0xDF`. `+0xF5` does not measure
arrival; it only schedules route refreshes.

State 9 (`0x00471430`) is not a second combat approach. Its only extra rule over
the shared movement handler is a countdown at `+0xEF`: while nonzero it
decrements and delegates to `0x00470C80`; at zero it ends the route and returns
false, so the dispatcher sends the creature idle. It has none of state 8's
target selection, fear, chase limit, reach, or rerouting logic, and no entry site
was found for it (travel-states.md §7, which owns states 6, 7, 9 and 88).

## 5. Choosing the victory branch (`0x00460FE0`)

The victory arm is reached when `spFighting` has no target and the `AttackType`
nibble is `0xF`.

Without `NeedBodyForFightIdle` (`def+0x686` bit 20), it enters state 260 and
starts action 55 at the creature's own position. If the state transition is
refused, it returns false.

With the flag, it scans the neutral player's dead-body list and keeps the nearest
eligible live entry whose fixed-point squared distance is strictly below
`0x2000`. It calls `0x00471890(body position, 259, 0)` and, on success, stores
the body's tag at `state+0xA7`. This branch **does** use the generic
route-and-enter helper: the creature travels in state 6 and resumes in state
259.

## 6. Arrival at the body (state 259, `0x00461130`)

State 259 first requires the remembered tag to remain live and to identify a
dead-body thing (kind 6). Failure clears `+0xA7` and ends the state. It then
calls the turn step `0x004B2AE0` toward the body: the heading `+0xF4` turns by at
most the turn rate (`def+0x6A0`) and the angle still left is returned. While that
is nonzero it keeps waiting. There is no distance test: state 6 hands over to 259
on route arrival or its other exits (STRONGLY INFERRED from the route helper).

When the creature faces the body, it enters state 260 and starts action 55 at the
creature's own position, exactly like the ordinary victory branch. It neither
destroys, picks up, nor otherwise changes the body in this handler: the body is
where the creature goes to perform the victory pause.

## 7. State 260 and action 55

State 260's handler `0x00458680` only asks whether a current action exists
(`0x00496D00`) and returns that result. Its state record is group 4, category 22
Combat, flag 2.

Action 55's record at `0x006E47D3` has a null handler, mode 0, duration 0, and
selects animation slot 35 through the action table's `+0x14` byte (`0x004950C0`).
Both starts (`0x00460FE0`, `0x00461130`) use `0x00496A30(55, 1, …)`: count 2 and
the loop bit cleared, so the clip plays once. The action start stamps the game turn
at `creature+0x3A5`, but the generic action updater does not compare that stamp
for this record. Instead it keeps the action while the creature's animation
advance rate at `+0xDC` is nonzero and clears the entire action block when that
rate reaches zero. The embedded animation object begins at body +0x44;
see [Tremor's clock](keeper-spell-tremor.md#4-build-boundary-and-next-read).

On the same creature tick, state 260 then observes no action, returns false, and
the state dispatcher enters state 1, idle. Thus the celebration lasts **one
animation instance**, with no fixed simulation-turn timer in the victory code.

**Per creature (Q376).** `0x00494880` falls back from slot 35 to slot 8 when slot
35's runtime bit 0 is clear; `0x00504D80` sets that bit for any nonzero type, so a
type-6 slot 35 is used whatever its file flag bit 0. A type-0 clip still starts the
action and ends at the next poll. Applying Tremor's clock (`ActionHoldTicks`) to the
shipped `Creatures.kwd`, a creature that starts the pause on turn T is idle from
T + H:

| H | creatures (clip slot) |
|---|---|
| 22 | Thief, Rogue, Unique Rogue (35) |
| 19 | Bile Demon, Unique Bile Demon (8) |
| 18 | Horny (35); Salamander, Unique Salamander (8) |
| 17 | Mistress, Unique Mistress, Maiden of the Nest (8) |
| 15 | Vampire, Unique Vampire (35) |
| 14 | Skeleton, Unique Skeleton (8) |
| 13 | Guard, Royal Guard (8) |
| 12 | Elven Archer, Dark Angel, Unique Dark Angel (8) |
| 11 | Goblin, Unique Goblin, Fairy (8) |
| 9 | Giant (8) |
| 8 | Firefly, Unique Firefly, Princes Tristran, Felix and Balder (8) |
| 7 | Knight (35); Warlock, Unique Warlock, Wizard, Black Knight, Unique Black Knight, Lord of the Land (8) |
| 6 | Monk (8) |
| 5 | Imp (35); Troll, Unique Troll (8) |
| 4 | Dwarf, Dark Elf, Unique Dark Elf (8) |
| 1 | King Reginald, Stone Knight, Jack (slot 8 has type 0) |

No clip used has start or end segments. The same computation reproduces the six
Q527 get-up holds.

`ProcessFight` treats category 22 as already fighting and therefore does not run
`SelectAVictim` during state 260. The later attackability recomputation does not
exclude state 260, so a celebrating creature remains attackable subject to the
ordinary attackability gates. Being attacked may register a target, but the
group-22 retaliation path does not switch it from state 260; the animation still
controls when the pause ends.

## 8. Built and not built

**Built (ADR-0169, `HashVersion` 74).** `ApproachRules` is state 8, a walk resuming 199
(ADR-0483; until then a flag on 199, `Approaching`, now derived from the state): entry with a plain route and the countdown at ten (§1); each tick
a target gone ends it, an ordinary creature target is chosen afresh and a changed choice
ends it, the fear counter runs without the withdrawal test, a target fleeing or
withdrawing counts the chase against variable 213 or 214 and is given up as
`RetreatingCreature`, strict reach with line of sight ends it, and every tenth call the
route is refreshed to the target's position — a Flanker two tiles or a quarter tile
behind a moving target (§2—§3). `spFighting` (`CombatSystem.Fight`) enters it out of reach
and runs the tick it hands back. `VictoryRules`: the death leaves the `0xF` nibble
(`TargetDied`); the victory arm celebrates in `Celebrating` (state 260) or, for
`NeedBodyForFightIdle`, walks to the nearest corpse strictly within `0x2000` squared
(`GoingToBody`, state 259) and celebrates there; the celebrant is attackable, does not scan
and does not turn on a blow (§5—§7). On state ids (ADR-0208): the fight is 199, the walk
to a body state 6 resuming 259, the celebration 260, then state 1.

**Deviations, named (Q400):** the pause lasts a fixed **8 ticks** (`VictoryRules.PauseTicks`)
where DK2's lasts 1 to 22 turns by creature (§7, Q376); behind the target is read from its
velocity, not its heading, and a standing target gives its own position (§3); freeze, stun and a
changed choice let the engagement go where DK2 leaves the attack word; the body is reached at
half a tile squared with no turn, where DK2 has no distance test and turns to face it (§6); a corpse is looked for under any
owner; a failed route to a body celebrates at once; `AttackAbnormalTargets` is not
modelled.

## Confidence

**CONFIRMED (binary):** every branch and ordering in `0x00470E30`,
`0x00471430`, `0x00460FE0`, `0x00461130`, and `0x00458680`; the three-argument
shape and `RET 0x0C` of `0x00471D10` and `0x00471890`; the state fields written;
the direct route calls; the 10-call refresh; variables 213/214; action 55's null
handler; the animation-instance end condition; state 1 after a false handler;
category 22 suppressing a new fight scan; state 260 not being excluded from
attackability; action 55's mode 0 and (55, 1) start, the 35→8 fallback and its
availability bit; the Flanker formula and the tables' sampled entries; the turn step
in state 259.

**STRONGLY INFERRED:** the neutral-player list searched by `0x00460FE0` is the
dead-body list — state 259 requires kind 6 before using the selected entry; the
per-creature turn counts in §7 (Tremor's clock, validated by six get-up captures; no
capture of action 55); state 259 being reached on route arrival or state 6's other exits.
