# Research: how the imp work states move and end

Source: `DKII.EXE` v1.70 (GOG), read with Ghidra 11.0.1 on 2026-09-24.
Addresses and control-flow descriptions are facts about the binary written in our
own words; no decompiler output enters this repository (ADR-0046). Binary-only
findings are **STRONGLY INFERRED**. State labels, groups and categories come from
`creature-state-table.md`; route sites and arguments from
`travel-state-call-sites.md`. `imp-task-priority.md` owns task selection.

This note reads states 10–48 and 76, their direct transition helpers, and the
worker failure path in the per-creature dispatcher at `0x00476FC0`.

## 1. Notation and the failure rule

- `S(n)` is `SetState(n, 0)` at `0x00476D30`.
- `W(n, options, noTeleport)` is the Imp wrapper `0x004651A0`: normally state 6
  with resume `n`; when its teleport branch accepts, states 51 → 52 resume `n`.
- `G` is the gold-store chooser `0x00464400`, which calls `W(28, E8, 0)`.
- `O` is the object-destination helper `0x0046E480`, which calls
  `W(31|32|33, E8, 1)` according to object kind.
- `B` is the casualty-destination helper `0x0046E610`, which calls
  `W(45, E0, 1)` for its keeper's creature or `W(43, E0, 1)` for an enemy.

**A handler return of 0 always leads to state 1.** The dispatcher does not give
workers a special failure state: after the state handler returns 0 it calls
`S(1)` and returns 0. Thus an imp does not fall directly to state 10. State 1's
own worker path can subsequently choose worker states, but that is a later tick.

## 2. Every state

All states below except 76 have group 8, category 3. State 76 has group 3,
category 7. “Failure → 1” means the handler can return 0 and the dispatcher then
does the transition established above.

| state | handler | category · group | what one tick does | every state/route call | return 0 |
|---|---|---|---|---|---|
| 10 | `0x00463DF0` | 3 · 8 | Runs the two-tier task selection; if no task is taken, starts the no-work delay. | Task handlers call `W` with resumes 13, 25, 15/19/17, 21, 23, 27, 30, 34/36, 38, 40 or 42; otherwise `S(12)`. | Only if `S(12)` is refused → 1. |
| 11 | `0x00463DB0` | 3 · 8 | Clears the worker's arrival flag and marks it freshly dropped. | `S(10)`. | If refused → 1. |
| 12 | `0x00464240` | 3 · 8 | Waits for the idle deadline, occasionally plays an idle interaction, then reselects; while waiting it may start wandering. | Deadline: `S(10)`; waiting: one ignored `S(87)`. | Only refused `S(10)` → 1. |
| 13 | `0x004654F0` | 3 · 8 | Revalidates a dig task and adjacency; waits if its stance is occupied. | `S(14)` when ready, else `S(10)`. | Refusal → 1. |
| 14 | `0x004655D0` | 3 · 8 | Holds while the dig action runs; on completion applies the dig, releases the task and may seed the adjacent follow-up. | `S(10)` after completion or invalidation. | Refusal, or failed follow-up allocation after `S(10)`, → 1. |
| 15 | `0x00465BC0` | 3 · 8 | Revalidates the plain-floor claim and current tile; waits if obstructed. | `S(16)` when ready, else `S(10)`. | Refusal → 1. |
| 16 | `0x00465CD0` | 3 · 8 | Holds while claim action 4 runs; on completion removes competing claim tasks, claims the tile and records task kind 3. | `S(10)`. | Refusal → 1. |
| 17 | `0x00466080` | 3 · 8 | Revalidates an enemy-room claim and, when attackable, begins the room-destruction action. | `S(18)` when ready, else `S(10)`. | Refusal → 1. |
| 18 | `0x00466180` | 3 · 8 | Holds while action 10 runs; on completion clears the room-tile task chain and releases the task. | `S(10)`. | Refusal → 1. |
| 19 | `0x00465E60` | 3 · 8 | Revalidates an enemy-floor claim. | `S(20)` when ready, else `S(10)`. | Refusal → 1. |
| 20 | `0x00465F20` | 3 · 8 | Holds while claim action 5 runs; on completion applies ownership/cost side effects. | One **ignored** `S(15)` after completion. | No explicit 0 path. |
| 21 | `0x004662E0` | 3 · 8 | Revalidates the reinforce task; waits if its stance is occupied. | `S(22)` when ready, else `S(10)`. | Refusal → 1. |
| 22 | `0x00466390` | 3 · 8 | Holds while reinforce action 7 runs; applies reinforcement and neighbour visual updates, then releases the task. | `S(10)`. | Refusal → 1. |
| 23 | `0x00466660` | 3 · 8 | Revalidates the wall-repair task; waits if its stance is occupied. | `S(24)` when ready, else `S(10)`. | Refusal → 1. |
| 24 | `0x00466710` | 3 · 8 | Holds while repair action 8 runs; applies the repair and releases the task. | `S(10)`. | Refusal → 1. |
| 25 | `0x00465790` | 3 · 8 | Revalidates a mining task and adjacency; waits if its stance is occupied. | `S(26)` when ready, else `S(10)`. | Refusal → 1. |
| 26 | `0x00465870` | 3 · 8 | Holds while mining action 2 runs. Before another strike, a nearly full imp tries `G`; otherwise it applies the strike, releases an exhausted seam and accounts for loose gold. | `G` → state 6/51 with resume 28; `S(10)` after invalidation or the completed strike. | Refusal or a failed post-strike follow-up → 1. |
| 27 | `0x00466850` | 3 · 8 | Removes the floor-gold task and takes matching nearby piles up to the level-scaled carrying limit. | With gold, `G` → resume 28; otherwise `S(10)`. | If both `G` and `S(10)` fail, or final `S(10)` fails → 1. |
| 28 | `0x00466A40` | 3 · 8 | Waits for a free standing point at the chosen store tile. | `S(29)` when free. | Refusal → 1. |
| 29 | `0x00466AC0` | 3 · 8 | Rechecks that the tile stores this keeper's gold, deposits what fits and emits the deposit effect. | `S(10)` after a deposit. | Invalid store, or refused `S(10)`, → 1. |
| 30 | `0x00466BC0` | 3 · 8 | Removes the loose-object task, picks an eligible object at the tile and chooses its destination. | `O` → state 6/51 with resume 31, 32 or 33; else `S(10)`. | Refusal → 1. |
| 31 | `0x00466CB0` | 3 · 8 | Validates a carried spell book; if this tile is no longer suitable it chooses another destination, otherwise archives it. | `O` to retry; `S(10)` after storage. | Invalid carried id/type or refused call → 1. |
| 32 | `0x00466DF0` | 3 · 8 | As 31 for the generic carried-item kind. | `O` to retry; `S(10)` after storage. | Invalid carried id/type or refused call → 1. |
| 33 | `0x00466F30` | 3 · 8 | As 31 for a Special delivered to a Library. | `O` to retry; `S(10)` after storage. | Invalid carried id/type or refused call → 1. |
| 34 | `0x00467040` | 3 · 8 | Finds the matching loose trap crate at the collection tile, routes to the placed trap, then takes the crate. | `W(35, E8, 1)`; if none, `S(10)`. | Refusal → 1. |
| 35 | `0x004671B0` | 3 · 8 | Consumes the carried crate at the matching placed trap and completes its construction. | `S(10)` after completion or no matching trap. | Missing carried object → 1. |
| 36 | `0x004672F0` | 3 · 8 | Door counterpart of 34. | `W(37, E8, 1)`; if none, `S(10)`. | Refusal → 1. |
| 37 | `0x00467460` | 3 · 8 | Door counterpart of 35; completes the door and may emit its terrain update. | `S(10)` after completion or no matching door. | Missing carried object → 1. |
| 38 | `0x00467650` | 3 · 8 | Revalidates task kind 11 and adjacency; waits if its stance is occupied. | `S(39)` when ready, else `S(10)`. | Refusal → 1. |
| 39 | `0x00467790` | 3 · 8 | After its action ends, clears a tile flag, emits a map update and releases the task. | No transition call. | Its normal completion returns 0 → 1. |
| 40 | `0x004678A0` | 3 · 8 | Removes the corpse task, finds the corpse on this tile and a graveyard with capacity, then takes the corpse. | `W(41, E8, 1)`; otherwise `S(10)`. | Refusal → 1. |
| 41 | `0x00467AA0` | 3 · 8 | Rechecks the carried corpse. At a valid graveyard it buries it; otherwise it searches and reroutes to another graveyard. | Successful burial `S(10)`; retry `W(41, E8, 0)`. | Bad carried id/type or failed reroute → 1. |
| 42 | `0x00467D00` | 3 · 8 | Removes the casualty task, finds the unconscious creature on this tile and chooses Lair versus Prison by ownership. | `B` → resume 45 (own) or 43 (enemy); else `S(10)`. | Refusal → 1. |
| 43 | `0x00467DD0` | 3 · 8 | Carries an enemy casualty to a Prison. It keeps resume 43 while approaching another prison/its entrance; once at the chosen admission point it starts the inner leg. | `W(43, E0, 1)` for approach/retry; `W(44, E4, 1)` for the inner leg. | No valid prison or failed route → 1. |
| 44 | `0x00468010` | 3 · 8 | Decides admission at the Prison. If admitted, it releases the body, puts it into prisoner state 114 and walks the imp away; it can retry another Prison. If refused, it starts the put-down path. | Admitted `W(46, EC, 1)`; retry `W(43, E0, 1)`; refused `W(47, EC, 1)`; terminal cleanup `S(10)`. | Invalid casualty → 1. |
| 45 | `0x00468420` | 3 · 8 | Delivers its keeper's casualty to the Lair (a worker casualty is released directly); if the destination changed it runs the casualty chooser again. | Success `S(10)`; retry `B` (normally resume 45). The helper-failure path makes one **ignored** `S(10)` then returns 0. | → 1, including after that ignored call. |
| 46 | `0x004683F0` | 3 · 8 | Finishes the admitted-prisoner carrier's walk-away leg and clears movement modes. | `S(10)`. | Refusal → 1. |
| 47 | `0x00468270` | 3 · 8 | Clears dragging modes, chooses an unoccupied put-down tile in the Prison and walks there. | `W(48, EC, 1)`; if no point/route, **ignored `S(1)` then `S(48)`**. | Refused final `S(48)` → 1. |
| 48 | `0x00468330` | 3 · 8 | Places the refused casualty at the selected point, restores its body placement, clears the drag and releases the imp. | `S(10)`. | Refusal → 1. |
| 76 | `0x0046EAB0` | 7 · 3 | Generic dropped/arrival idle processing. For an Imp it may deposit gold or fetch a trap/door crate; otherwise it handles the tile's room/job interaction and falls to worker arrival state 11. | Imp helpers: `G` → 28 and crate helper → 34/36. It also has ignored `S(1)` before a room-derived state, direct `S(71)`, and a second `S(1)` → runtime room-job state; fallback `S(11)`. Non-Imp-only arms target 130 and 197. | Refused final target → 1. |

## 3. Direct states outside the requested range

The table above directly reaches four protocol states already read in
`travel-states.md`: state 6 performs the walk and resumes through `+0xDF`; state
51 begins the optional Imp teleport, state 52 completes it and resumes through
`+0xDF`; state 1 is the dispatcher's fallback. State 12 can also request state
87: its handler either starts a state-6 wander and resumes in 238 or 1, or stays
in 87 when no walk is selected. State 76's direct state 71 is a generic idle
handler that can choose state 188 or 11 and otherwise returns 0. These are not
additional work states.

## 4. Answers required by the slice

### Failure and transition guards

There is **no** state 10–48/76 instance of state 82's exact “try target; on
refusal go to 1; retry target” guard. State 47 deliberately forces `1 → 48` when
its route cannot be made, without trying 48 first. State 76 likewise uses
`1 → runtime target`. The Imp teleport wrapper tries state 1 before state 51,
but refusal falls back to an ordinary walk rather than retrying state 51.

The ignored single calls are state 20's `S(15)`, state 45's `S(10)` immediately
before it returns 0, state 47's leading `S(1)`, and state 76's leading `S(1)`
calls. State 12 also ignores `S(87)`. Every other listed call either returns its
result or tests it.

### Work loops

The ordinary per-application loop is not a persistent work state:

```text
10 selection → 6 walk (or 51 → 52 teleport) → odd entry → even action → 10
```

Dig is 13 → 14 → 10; mine is 25 → 26 → 10. The action state may hold across
ticks while its creature action runs. State 10 can select the same task again,
so the next strike repeats the whole loop. Mining instead branches from 26
through `G` to state 6/51 with resume 28 when the next load would not fit; then
28 → 29 → 10 deposits it. This contradicts the built `Working` activity, which
keeps one target across repeated applications (Q420).

### The body chain

State 42 is the pickup fork. Own casualty: state 6 → 45, the Lair delivery.
Enemy casualty: state 6 → **43, the carrying walk to a Prison**. State 43 keeps
resume 43 while finding/reaching the prison and uses resume 44 for its inner
admission leg. **State 44 is the drop/admission decision**: admission changes
the body to prisoner state 114 before the imp walks away to 46; another prison
returns to 43; refusal goes 47 → 48, where 48 physically puts the still-collapsed
body down and releases the imp. Thus 46 is carrier cleanup after admission, and
48 is the refused drop, not the walk to the Prison.

### Our activities against DK2

| our activity | DK2 states over the same span | boundary with no DK2 counterpart |
|---|---|---|
| `MovingToJob` | 10 chooses; 6 walks (or 51 → 52 teleports); resumes 13/25/15/19/17/21/23/38. | Ours begins after selection; DK2's state 10 is separate. |
| `Working` | 13→14 dig, 25→26 mine, 15→16 claim, 19→20 enemy claim, 17→18 room destruction, 21→22 reinforce, 23→24 repair, 38→39 kind 11; each completed application returns to 10 (39 via dispatcher state 1). | Ours persists across applications; DK2 reselects between them (Q420). |
| `FetchingGold` | 10 → 6/51 → 27. | None beyond folded state 10 and travel protocol. |
| `DeliveringGold` | `G`: 6/51 → 28 → 29 → 10, entered from 10, 26, 27 or 76. | Ours has no separate arrival/deposit boundary 28 → 29. |
| `FetchingBody` | Corpse: 10 → 6/51 → 40. Casualty: 10 → 6/51 → 42. | Ours folds the pickup handler (40/42) into the activity's arrival tick. |
| `CarryingBody` | Corpse 6 → 41; own casualty 6 → 45; enemy 6 → 43 → 44 → 46 or 47 → 48. | Ours has one carry state and no prison admission/walk-away/refused-drop states. |
| `FetchingObject` | 10 → 6/51 → 30; trap/door crates instead resume 34/36. | Ours does not represent crate installation as object fetching. |
| `CarryingObject` | `O`: 6/51 → 31 (book), 32 (other) or 33 (Special), then 10. | Ours folds the three destination-specific arrival handlers into one activity. |

## 5. Open consequence

Q420 records the built work-loop mismatch. No behavior, source, test, hash or
format version is changed by this research.
