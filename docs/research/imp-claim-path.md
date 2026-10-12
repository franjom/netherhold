# Imp claim path: how DK2 chooses, takes and loses claim work (kinds 3 and 14)

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 (GOG), read 2026-09-30 through pyghidra-mcp and the headless runner on Claude's project copies; captures `level1-live1` seed01, `level1-live1-seeds` seed02–07 and `level2-fullstate-20260928/l2win928` for checks only |
| Confidence | **CONFIRMED** unless a paragraph says otherwise; every fact carries its address. Decompiler output stayed under `reverse-engineering/` and the scratchpad (ADR-0046) |
| Implementation | none yet; §11 is the list to build from. `imp-task-stealing.md` §3, §5–§8 and `imp-task-priority.md` §2.2, §4, §6 are the current readings this note corrects |
| Tests | none yet |
| Known exceptions | none |
| Open questions | Q487 is answered below (four neighbours, not eight); Q488 (drop rings are diamonds) is settled by §2; the imp's teleport walk (§4) and player byte `+0x10FC` (§5) are UNKNOWN |

Distances are DK2's: per axis `|d|² >> 12` with `d` in 4096ths of a tile, summed; a tile
centre is `tile × 0x1000 + 0x800`. "Slot" is `Worker[side×3+slot]` of a pool entry
(`0x00508110`). The state handlers are the 25-byte table at `0x006DA8B0`
(`creature-state-table.md`): 15 `0x00465BC0`, 16 `0x00465CD0`, 17 `0x00466080`,
18 `0x00466180`, 19 `0x00465E60`, 20 `0x00465F20`, 14 `0x004655D0`.

## 1. The answer in one paragraph

A claim is chosen by **position in a diamond walked outward from the imp, first
accepted tile wins**, never by comparing distances across tiles and never by preferring
a free task to a held one. Every claimable tile the walk reaches is handed to one
handler, `0x0046AB50`, which creates the kind-3 task on first sight, takes a free slot
at once and takes a held slot when the imp is strictly nearer the tile centre than the
holder, from where each stands now. There is one more taker: an imp **walking to any
job more than two tiles away claims the just-dug tile it steps on** (`0x00464170` →
`0x00469600`), dropping the job it was walking to and stealing the tile's claim from a
farther imp; it fires only once or twice a Level 1 game (§10), and the dispatcher's own
kind-14 arm is dead (§3). Far more takes by a walking imp are **losers of a steal in the
same tick**, dispatched again after the thief because updates run newest first (§9), and
they take the next tile in ring order at once. Enemy floor and enemy rooms
are the **same kind-3 task and slot** with a subkind byte that picks the state (15, 19 or
17), and an attacked floor is claimed afterwards by the same imp under the same task with
no reselection (§8). A working claimer stands on the tile and is never nearer-beaten;
every steal in eight captures took a task from an imp still walking (§10).

## 2. The ring scan `0x00468D10` (Q1)

- **Centre.** The imp's own position, `creature+0x1A`/`+0x1E >> 12`, passed by every
  caller (`0x00468520` case 3 at `0x0046855C`; state 10 at `0x00464036`, `0x004640C7`).
  A new scan starts from wherever the imp is at that evaluation; within one scan nothing
  re-centres.
- **Own tile first, no filter** (`0x00468D10`, the first callback call). Then rings only
  when the radius is at least `0x1000`.
- **Shape: diamonds.** The four sides step the tile pointer by `(width+1)×16`,
  `(width−1)×16`, `−(width+1)×16` and `(1−width)×16`: one row and one column per
  tile, so each ring is the set at one Manhattan distance. That confirms the emulator
  reading in `imp-task-priority.md` §2.2 (corners visited, sides clockwise, start side
  from the chooser). Q488's drop rings are therefore diamonds too (§3).
- **Start side.** `0x00469340` grades the four neighbours north, east, south, west
  through `0x00469460` and the byte table `0x00698628` (dig or mine task 1, room tile 3,
  rival floor 4, unowned floor 5, reinforce or repair 12, nothing 127) and keeps the
  **first** of the best: a later side replaces the choice only when strictly better
  (`0x0046938C`, `0x004693C9`, `0x00469408`, `0x00469442`). It is passed by the claim
  dispatcher arm, by the drop scan and by Tier B (`0x00464031`, `0x004640C0`); only the
  kinds 6, 7, 8 and 16 arms of `0x00468520` pass none, and then the side is a local only
  the chooser writes, read uninitialised: whatever the stack held, not a draw (ADR-0294).
- **Radius per call site.** Step (a) and Tier A pass `0x7FFFFFFF` (§3); the drop scan
  passes `0x2000` (`0x0046402C`), which scans rings 1, 2 and 3 because the bound is
  tested after a ring with `>`; Tier B passes `0x7FFFFFFF` (`0x004640BB`). A whole-map
  scan ends when all four sides of a ring are clipped empty (`iVar11 == 4`).
- **Stop.** The scan returns at the **first** tile whose handler returns 1. It never
  keeps looking for a nearer tile, and it has no notion of free-before-held: a held task
  in ring 2 beats a free task in ring 3, and inside a ring the walk order decides.
- **Per-tile filter** (`0x00468E99` and its three siblings): the handler runs when tile
  byte `+0xB` bit `0x20` is clear, or a kind-1 or kind-2 task is at the tile. No worker
  slot is read here.
- **Bit `0x20` (Q487, closed).** Its one recompute is `0x004502D0(x, y)`, and its
  neighbour table `0x006BC350` holds **four** offsets — (0,−1), (1,0), (0,1), (−1,0) —
  not eight (`imp-task-stealing.md` §3 said eight). For tile T it clears the bit on each
  orthogonal neighbour that is not liquid when T is owned and not solid, and sets T's
  own bit unless something in T or its four neighbours is owned, T is not liquid and T
  or a neighbour is passable. It runs on the tile itself from the tile setter
  `0x0044FC40` (after its neighbour loop, skipped only under a global editor flag,
  `[world+8]+0x7239` bit `0x40`) and on the four neighbours from the change notifier
  `0x00455030`. So a freshly claimed tile clears its four neighbours' bits the tick it
  changes, and for claims the filter only anticipates the handler's own adjacency test.

## 3. Every path to a kind-3 take, in state 10's order (Q2)

| path | where | radius | steals? | kick vs own walk |
|---|---|---|---|---|
| step (a) after a claim | `0x00463FC3`: `+0x98 == 3` and record dword 5 of kind 3 set (`0x006986A0 + 3×36`, dwords 5, 6, 7 are 1); or the aimed kind `+0x9D == 3` (`0x00463ED0`) | `0x7FFFFFFF`, the ring scan | yes | walk first |
| the drop scan | `0x0046402C`–`0x0046406B`, chain `0x00469240` step 2 (`0x0046926F`) | `0x2000` → rings 1–3 | yes | walk first |
| Tier A | table `0x00698658` = 14, 8, 9, 1, 3, 13, 12; loop `0x00464099` | `0x7FFFFFFF` | yes | walk first |
| just-dug, kind 14 | `0x00464170` → `0x004687C0(14)` → `0x00469600` on the imp's own tile | own tile only | yes | **kick first** |

- **Step (a) for kind 3 is the ring scan, not a task-list walk.** `0x00468520` case 3
  (`0x0046855C`) is the only way kind 3 is searched; nothing walks the kind-3 list.
- **Kind 14 through the dispatcher is dead.** `0x00468520`'s jump table at
  `0x00468770` sends index 14 to the default arm `0x00468763`, which returns 0
  (indices 0, 4, 5 and 14 all do). So Tier A's first entry is a no-op, and the kind-14
  dword (index 0) of every record in `0x006986A0` is 0 anyway. `0x004687C0` is the only
  reader of `0x00469600`, and `0x00464170` its only caller (`0x00464210`,
  `0x00489076`). `imp-task-priority.md` §4(c) and §6 describe the table, not the effect.
- **Tier B** (`0x004640BB`) runs `0x00469580` per tile, which has no claim step.

## 4. The claim handler `0x0046AB50`

Gates, in order, each a return 0: the tile's owner is the imp's; terrain
flags `0x81` or `0x100` set; the door nibble `+0x21C(x, y, 0)` non-zero, which is
`0x005092F0` → `0x004528E0` → `0x004D1A20`: the navigation byte's high nibble, so a tile
with a door is never claimed (re-read); the owner is neither neutral (`0x006F2548`) nor
non-allied (`0x004BF3D0`); no orthogonal neighbour is the imp's own non-solid Ownable
floor (`0x00453B30` reads the four neighbours at `−stride`, `+stride`, `−16`, `+16`).

Then the kind-3 task is found or raised (`0x00508200`, `0x00507BB0`; a raise fails only
when the pool's free count at `+0x4650` is 0, and then the tile is refused). The flavour
is decided next and written to the entry's `+0x1D` (`0x005080E0`):

| test | subkind | state | meaning |
|---|---|---|---|
| `0x00453FC0`: owner neutral, terrain `+0x1CB` bit 0, not solid | 0 | 15 | plain claim |
| `0x00454000(owner)`: owner not the imp's, flags Ownable `0x4` and Attackable `0x20`, neither Solid nor Room | 2 | 19 | enemy floor |
| `0x00454040(owner)`: owner not the imp's, Room flag `0x10`; on Attackable terrain any room but a Dungeon Heart (type 5), which needs `room+0x9A` bit 0 (the ruin); on non-Attackable terrain (the five gates) only that bit (ADR-0299) | 1 | 17 | enemy room |

Terrain byte `+0x1CB` bit 0 is set by the terrain loader at `0x004F7759` when the type
is **not Ownable, its StartingHealth (`+0x1AB`) is below its MaxHealth (`+0x1AD`), and
its id differs from its MaxHealthTypeTerrainId (`+0x1C5`)**: the neutral types that heal
into an owned type (Dirt Path → Claimed Path, `tile-work.md`). STRONGLY INFERRED that
this is the only writer of bit 0 in play: the other two stores are the record clear
(`0x004F66B3`, `& 0xFC`) and bit 1 (`0x004F7799`).

The slot: `Worker[0][0]` zero → leave hook `0x004647D0`, then the walk
`0x004651A0(centre, state, 0xE0, 0)`. Held → the holder must be live (`0x006ED540[id]`),
a creature (`+0x12 == 0`), have a state machine (`+0x252`), not be in 51 or 52, and pass
`0x00477310(10)`; else return 0 (a dead or reused id blocks the tile). Then both squared
distances to the tile centre are taken from where each imp stands, and the taker needs
**strictly less**. Then the leave hook, the walk, and
only if the walk started, `0x0048AD30(10)` on the holder. On success: `+0x8A` id,
`+0x8C/+0x90` tile, `+0x94/+0x96` 0, the slot ← the imp's id (`0x00508160`), `+0x98` 3.

**The walk** (`0x004651A0`): if the imp has an ability with definition flag `0x20` at
its level (a teleport), the target is more than two tiles away on either axis and
reachable, it teleports (state 1 then 51) instead of walking; otherwise
`0x00471890(centre, state, 0xE0)`. Which imp level grants that ability is UNKNOWN here;
no Level 1 capture shows it.

## 5. The just-dug claim: `0x00464170` and `0x00469600`

`0x00464170` is called once per creature update at `0x00489076`, and only on a tick the
creature **entered a new tile** (`EBX` set at `0x00488F91` when the tile coordinates
changed), for a creature whose definition has `+0x686` bit 0, whose owner's player byte
`+0x10FC` is at least 3 (STRONGLY INFERRED a keeper, not the hero or neutral player:
`0x004B8640` treats 1 and 2 specially), and whose effective category is not 22. Inside:
nothing carried (`+0x3C3`, `+0x3C5`), effective category 3 (`0x00478050`: a walk reads
its resume `+0xDF`), the imp's tile **more than two tiles from its task tile**
`+0x8C/+0x90` on either axis, and dword 6 of the record of its current kind `+0x98`
(set for kinds 1–7 and 16). Then `0x00469600` on the tile underfoot; on success the
walk's saved caller `+0x40` is set to 10 (`0x00478080`).

`0x00469600`: the same five gates as §4; a **kind-14 task must already exist** at the
tile (no raise); if the tile fails `0x00453FC0` the kind-14 task is removed and 0
returned; any kind-1 task in the eight neighbours, tested in the order (−1,−1), (0,−1),
(1,−1), (1,0), (1,1), (0,1), (−1,1), (−1,0), returns 0 with the task kept; then the
kind-3 task is found or raised, the kind-14 task removed, subkind 0, and the slot rule of
§4 with two differences: **the kick comes before the leave hook and the walk**, and the
walk state is always 15. The leave hook releases the slot of the task the imp was walking
to (its state names it through `+0xDF`), so a digger or claimer on its way abandons that
job. State 10 then reselects with `+0x98 = 3`.

Kind-14 tasks are raised when a dig ends in state 14 (`0x00465774`) and the dug tile
passes `0x00453FC0` and `0x00453B30` at that moment, and at the end of mining
(`0x00465B57`); they are removed by `0x00469600` as above and by state 16's completion.
Nothing else reads them.

**The raise needs the dig task alive** (read 2026-10-01, ADR-0305). The wall's fall does not
remove its kind-1 or kind-2 task. The digger, when its action ends, tests the id
(`0x00508070`) and raises nothing if it is gone; any imp's dig walk `0x00469AF0` removes a
task whose tile is no longer diggable (`0x00469BC8`, `0x00469EBF`). In Level 1, seeds 1 and 6
raise nothing at (10,28) because an imp chose a dig in that window; seed 4's walk did too,
but the player's next tag reused the freed id and the digger found it alive.

## 6. Kind-3 tasks: created and removed (Q3)

- **No task exists before an imp asks.** The only raises of kind 3 are the two handlers
  (`0x0046AC51`, `0x004698B1`); nothing marks claimable tiles ahead of time. A tile that
  has been looked at and refused at the slot keeps its task.
- **Removed by id** (`0x00508040`, the only remover for kind 3; `0x00507EF0` by kind
  and tile is called by it, by the kind-1 raise for kinds 6 and 16, and by the tag
  functions `0x00512F10` and `0x00512A20`, STRONGLY INFERRED for kinds 1 and 2 only,
  not read): state 15 on arrival failure when the kind-3 task at `+0x8C/+0x90`
  is the imp's own (`0x00465C99`); states 17 and 19 on arrival failure (own id);
  state 16 on completion: the kind-14 task at the tile, the kind-3 task at the tile and
  the own id (`0x00465DD3`, `0x00465DF6`, `0x00465E28`); state 18 on completion (own
  id). **"16 removes competing claim tasks" means its own tile only**: nothing touches
  a neighbouring tile's task, and a claim in progress blocks nothing next to it. The
  tile setter `0x0044FC40` does not touch the pool, so a task on a tile that changed
  hands lingers until an imp arrives and fails the check.
- **The slot** is released only by the leave hook (states 15–20 clear `[0][0]` when
  `+0x8A` is a kind-3 or kind-14 task, `0x004647D0` cases 15–20) and overwritten by a
  taker. The dead-holder ghost of `imp-task-stealing.md` §7 stands: both handlers refuse
  it, and no removal path reaches a tile nobody arrives at.

## 7. The claim states 15–20 (Q4)

| state | on entry each tick | leaves to |
|---|---|---|
| 15 `0x00465BC0` | imp's tile must equal the task tile, else leave hook → 10; task kind 3 or 14; adjacent (`0x00453B30`); subkind 0; `0x00453FC0` | 16; else removes its own task at the tile → 10 |
| 16 `0x00465CD0` | while no action runs (`0x00496D00`): task kind 3 or 14 else → 10; adjacent and `0x00453FC0` → start action 4 at the imp's position and stay | when the tile no longer passes: remove kind-14 and kind-3 tasks at the tile and the own id, `0x004BA720` (the keeper's territory bounding box), `+0x98 = 3`, → 10 |
| 19 `0x00465E60` | kind 3, adjacent, subkind 2, `0x00454000` | 20; else remove own task → 10 |
| 20 `0x00465F20` | while no action runs: kind 3 else → 10; adjacent and `0x00454000` → action 5 and stay | tile neutral: notifier `0x00455030`, `0x004B2CE0(−value, owner)` for each thing of class 3 or 4 on it (STRONGLY INFERRED traps and doors), subkind ← 0, **→ 15 with the same task** |
| 17 `0x00466080` | kind 3, adjacent, subkind 1, `0x00454040`, tile has a room id | 18 and notification `0x22`; else remove own task → 10 |
| 18 `0x00466180` | while no action runs: kind 3; adjacent and `0x00454040` → action 10 and stay | room gone: `0x004BA720` over the room's tile chain, remove own id → 10 |

The action handlers are the table `0x006E4198` (`creature-action-table.md`): action 4
`0x004974C0`, 5 `0x00497670`, 10 `0x00497BF0`. Each applies one work application (action 4
repeats it every poll until the tile converts, possession-arms-and-inputs.md §3) to the
tile under the action through `0x004508D0` → `0x00452F60`: `ClaimTileHealth` at
`0x006F2554` (825, load-time ÷4) times the imp's rate `+0x3AF + +0x3B3` for a claim,
`AttackTileHealth` at `0x006F2558` for enemy floor, `ConvertRoomHealth` at `0x006F2574`
through `0x004E5300` for a room. `tile-work.md` owns the arithmetic; the applier flips
the terrain to its MaxHealthTypeTerrainId owned by the claimer at MaxHealth and to its
DestroyedTypeTerrainId, neutral, at 0.

**Durations, measured** (three captures, §10): state 15 lasts 1 tick; state 16 lasts 8
ticks, with +164 on four consecutive ticks from health 1 and +3 to 660 (Level 2 also
shows +185 and +104 for a boosted imp); state 19 lasts 1 tick and state 20 28 ticks
(−28 on 23 ticks, then −15 to 1), followed by 15 and 16 on the same task, so an enemy
floor tile costs 37 ticks against 9; state 17 1 tick, state 18 24–26 ticks.

**Stealable while working?** By the rule, yes: no state or handler reads `+0x18`,
`+0x1C` or `+0x44`, and `0x00477310(10)` passes from 15–20 (`imp-task-stealing.md` §6).
In practice no: the claimer stands on the tile, and a taker must be strictly nearer its
centre. In eight captures no steal took a task from an imp in 15–20 (§10).

## 8. Enemy and hero tiles (Q5)

There is no separate kind. An enemy or hero floor tile and an enemy room tile are kind-3
tasks in the same pool list with the same `[0][0]` slot and the same nearer-to-centre
steal rule; the subkind byte (`+0x1D`) chosen at take time picks 19 or 17, and the
handler's distance measure is the tile centre for all three. The only extra gates are
`0x00454000` and `0x00454040` (§4). An attacked floor becomes neutral Dirt Path at
health 0 and the same imp goes 20 → 15 → 16 under the same task id and slot, with no
pass through state 10, so **no second selection and no second steal window exist**; a
claim counter that keys on the task id sees one take. Time: 28 ticks of state 20 plus 9
of claim (§7). So our `AttackTile` work, selected and stolen as a kind of its own, has no
counterpart, and its 2.8 steals a run belong to the same kind-3 count as claims.

## 9. Update order and the per-tick cap (Q6)

- **Newest first, confirmed.** `0x004B4A10(kind)` links a new thing at the head of list
  `kind` (`thing+8 = head; head+6 = thing; head = thing`, heads at `[0x006E504C] +
  kind×2`; kind 9 is not linked), and `0x004B7830` walks each of the eight lists through
  `+8`. `imp-task-stealing.md` §8 holds.
- **The cap counts every evaluation.** `player+0x7A` is zeroed at the top of the player
  update `0x004B9EC0` (and at player init `0x004B8640`) and incremented at `0x00463E9C`
  once per state-10 dispatch that is not just-dropped (`+0x99`), before any search and
  whatever the outcome. A thief counted when it chose; a loser kicked to 10 counts again
  when it is dispatched, the same tick if it is older than the thief. At 4 or more the
  imp goes to 12 for `20 + rand(80)` ticks (`0x00463E47`–`0x00463E8D`). The bypass at
  `0x00463E3D` (`LastEvaluate +0x9E >= now + 40`) can never hold.

## 10. Capture checks

`dk2steals.py` over the eight captures reproduces the owner's table (Level 1: 3, 4, 5,
2, 7, 7, 8 kind-3 steals; Level 2: 31). A scratch script re-derived each kind-3 steal
with both imps' squared distances to the tile centre from the frame before:

| check | result |
|---|---|
| taker strictly nearer the tile centre than the holder | 67 of 67 steals (36 Level 1, 31 Level 2) |
| holder's state when it lost | 6 (walking) in all 67; none in 15–20 |
| taker's state the tick before | 10 in 60; 6 in 7. A 6→6 taker is a loser kicked earlier in the same frame and dispatched again (§9), or a walker on the just-dug tile it stepped on (§5); classified below |
| kind-3 takes per Level 1 game | 88–100, of which 77–87 free slots, 1–14 held, and 5–8 by an imp in state 6 at both frames. Of those, **another imp held the old task at t in all but 0–2**: same-tick losers. The just-dug path (a kind-14 task at the new tile at t−1, the imp standing on it, nobody on its old task) fires 0–2 times a Level 1 game (0, 1, 1, 0, 1, 2, 1) and once in Level 2; Level 2: 227 takes, 43 held, 16 by a walker of which 15 are losers |

## 11. Where an engine that follows `imp-task-stealing.md` §3 diverges

1. **Choice order.** DK2 takes the first accepted tile in diamond ring order from the
   imp, start side by the grade table; it does not rank tiles by distance and does not
   prefer a free task to a held one. A held tile one ring nearer wins.
2. **The just-dug path is a walk interruption**, not Tier A's first step: on entering a
   tile more than two tiles from its task, a walking imp with a job of kind 1–7 or 16
   claims the kind-14 tile underfoot when no neighbour still carries a kind-1 task,
   releases the job it was walking to, and kicks a farther holder first. Kind 14 never
   runs through `0x00468520`. Rare: 0–2 takes a Level 1 game (§10).
2a. **Same-tick reselection.** A loser older than its thief is dispatched again in the
   same tick, counts against the cap again, and takes the next tile in ring order from
   where it stands, held or free; this is where most Level 2 walking takes come from.
3. **Enemy floor and rooms are kind 3** with a subkind; after the attack the same imp
   claims under the same task without a reselection. There is no `AttackTile` kind.
4. **The drop scan** is diamond rings 1–3 (`0x2000`), chain step 2 is the claim handler
   with the same steal rule, and it uses the claim start-side chooser.
5. **Step (a)** after a claim is the whole-map diamond scan for kind 3 before anything
   else; a loser searches kind 3 again immediately and finds its next tile in ring order.
6. **Task creation** happens at first sight by the handler; removal only on arrival
   failure or completion, own tile only; a tile that changed hands keeps a stale task; a
   dead holder's task is untakeable for good.
7. **Bit `0x20`** is four-neighbour ownership, recomputed on the tile by the setter and
   on the four neighbours by the notifier; for claims it duplicates the adjacency gate.
8. **Kick order:** the claim handler walks first and kicks only if the walk started;
   the just-dug handler kicks first.
9. **A door tile is never claimed**; the claim needs one orthogonal own floor neighbour.
10. **The cap** counts thief and loser alike, once per state-10 dispatch; updates run
    newest thing first.
11. **Durations:** 1 tick in 15, 8 in 16 (four applications); enemy floor 28 in 20 then
    the claim; room 24–26 in 18.
12. **The teleporting walk** for an imp with the ability, target over two tiles away.

## 12. Addresses read in full

`0x00463DF0`–`0x00464232`, `0x00464170`, `0x004647D0`, `0x004651A0`, `0x004655D0`,
`0x00465BC0`, `0x00465CD0`, `0x00465E60`, `0x00465F20`, `0x00466080`, `0x00466180`,
`0x00468520` (and its jump table `0x00468770`), `0x004687C0` (callers only),
`0x00468D10`, `0x00469340`, `0x00469460`, `0x00469600`, `0x0046AB50`, `0x00476FC0`,
`0x00478050`, `0x00478080`, `0x004974C0`, `0x00497670`, `0x00497BF0`, `0x004502D0`,
`0x004508D0`, `0x00452F60`, `0x004528E0`, `0x00453B30`, `0x00453FC0`, `0x00454000`,
`0x00454040`, `0x00455030`, `0x0044FC40`, `0x004B4A10`, `0x004B7830`, `0x004B8640`,
`0x004B9EC0`, `0x004BA720`, `0x004D1A20`, `0x004F66E0` (the `+0x1CB` stores),
`0x00507BB0`, `0x00507EF0`, `0x00508040`, `0x00508070`, `0x005080B0`, `0x005080E0`,
`0x00508110`, `0x00508160`, `0x00508200`, `0x00488930` (`0x00488F40`–`0x0048907B`),
`0x00496D00`, the tables `0x006986A0`, `0x00698628`, `0x006BC350`. Not re-read:
`0x00469240` beyond its call sites, `0x0048AD30`, `0x00477310`, `0x00471890`.
