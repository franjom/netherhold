# Category 0 states, part 1: 5, 69, 71, 86, 113, 126, 135, 211

- `DKII.EXE` v1.70 (GOG), read with Ghidra 12.1.4 on 2026-10-07.
- **Built:** ADR-0400 (5, 71, 86, 113, 126, 135, 211); 69 unreachable.
- Each state's entries come from a scan of every call to `SetState` (`0x00476D30`), `0x0048AD30`,
  `0x00491830` and the two walks, **and** a raw scan of `.text` for the push of the id with a jump
  to a shared tail (`docs/TRAPS.md`), the `+0xDF` stores (`travel-states.md` §2.4) and the room
  table's `+0x19`/`+0x1D` (`portals-room-handler-table.md`).

## 1. 5, a neutral creature's idle `0x004776B0` — CONFIRMED

A bare jump to `0x004776C0`, the inner body of state 3 (`idle-fallback.md` §3), without 3's wait
for a running action or its `+0x7E` deadline. Every turn: with `+0xA7` zero, `SetState(87)` and
`+0xA7` = now + 132; otherwise `random(100)` (simulation stream): under 25 wander as above, 25–74
action 43, 75–99 action 44 (each if `0x004966C0` allows it, else wander), clearing `+0xA7`. After an
action a creature whose `+0x36F` is 3 (the Bile Demon) rolls `random(100)` again (`0x004777D7`) and
under 10 makes shot `0x35` at its own position through `0x004B7CD0` (`0x00477806`, owner `+0x28`,
heading `+0xF4`). Shot 53 is the *Heal Instant Shot*: process 34, health 1, death shot 4 (*Modify
Health*), data words 0 and set by no one here, nor caster or target. With no target its first
pre-move turn kills it (`shots.md` §8), and the death shot heals by its inherited 0. It changes no
health, but makes two shot entities.

**Entry.** State 1's handler `0x004773D0`, at `0x004775BE`–`0x004775DA`: after its fight-return and
hero arms, every creature owned by the neutral player takes `+0x7E` = now + 12 and `SetState(5)`.
Nothing else enters 5.

**Ours.** `CreatureJobSystem` sends every neutral creature in 1 to 5, not only a fleeing one, and
5 runs `IdleBody`, which 3 now shares with the Bile Demon's draw and shot (missing from 3 too).

## 2. 69 is unreachable — CONFIRMED

`0x00470BC0`: with an action running 1; otherwise with an order (player `+0xA1C`) `SetState(63)`
and 1, else 0. Its one setter is 68 (`0x00470BA2`), which sets 1, then 69, and returns 0; the
dispatcher then sets 1 (`0x004772B4`–`0x004772B7`). Both moves are admitted (category 0's entry
mask is 0, category 1's names category 0), so 69 is entered and left within the one call and its
handler never runs. Ours ends 68 idle, which is the same.

## 3. 71, the Dungeon Heart's drop state `0x00459180` — CONFIRMED (ours' inner 3×3 anchor STRONGLY INFERRED)

1. Not on a Heart (`0x00453A20(5)`): 0, idle.
2. Not a worker (`def+0x686` bit 0): `SetState(188)`, the wage collection (`payday.md`).
3. A worker off the Heart's inner 3×3 (`0x004DA150` returns 1 unless the tile is the room's
   `+0x2D`/`+0x2E` plus 1 to 3 on each axis), or with no Heart object (`+0x12C` = 13) on the room's
   object list (`+0x0E`, link `+0x12A`) within `(dx² >> 12) + (dy² >> 12) < 0x1000000`, which is 64
   tiles and so any of the room's: `SetState(11)`.
4. Otherwise it is given back: `Player_AddMana(tile owner, Create Imp's mana cost)` (spell 4's
   `+0x58` through `0x0050DD60`), `DeadBodyOnDestroy` cleared, its gold lost (`Purse_Spend`), and it
   is destroyed (vtable `+0x20`), the tail of state 196 (`0x004592C3`–`0x00459309`).

**Entries.** The room drop `0x004E8DA0(0)` reads a Heart's `+0x19` = 71 for any Heart, own or not,
and kicks to 1 then 71: from 257 after the drop stun (`0x00481351`) and from 76 (`0x0046F17D`).
76's worker arm also sets 71 at `0x0046F0BE` for a keeper's worker dropped on the Heart's centre
(the builder `0x004EFBE0` marks only (2,2) with cell byte `0x40`). The Heart's `+0x1D` = 71 is
never read for a Heart.

**Ours.** `DropLandingRules.HeartDrop`, after the get-up: ours now sends a worker getting up on a
Heart there too (it idled every worker). Not built: 76's worker arm (`0x00481400`, ADR-0226). The
Heart object is ours' living Heart (`HeartRuined` false), and the inner 3×3 is measured from the
room's bounding box: STRONGLY INFERRED that `+0x2D`/`+0x2E` is that corner, the first slab of a 5×5
Heart (Q515). The mana is Create Imp's cost, CONFIRMED: `0x0050DD60` reads keeper spell 4's `+0x58`. 71's handler runs in the turn
it is entered.

## 4. 113, back into a Training Room `0x00485A60` — CONFIRMED

At `Age` 1 or less, 1. Then on a Training Room tile of its own keeper's (`0x004539B0(7, owner, 0)`),
`SetState(108)` and 1; otherwise 0.

**Entries.** Each walks the room's place-holders (`+0x0A`, next `+0x238`) and kicks each to 113:
the Training Room's slots `+0x09` (`0x004F2EA0`, tiles changed) and `+0x05`/`+0x0D` (`0x004F2F20`,
freed and owner changed), and `0x004E44E0` at `0x004E4527`, which `Tile_SetTerrain` calls for each
neighbouring room tile of a room whose definition has `SpecialWalls` (skipped while a save loads).

**Ours.** `RoomEvictRules` (shared with the Temple's 107 and the Prison's 126) kicks a trainee at
its next turn when the room was rebuilt, taken, freed or beside a changed tile since its claim
(ADR-0406, `room-return-states.md` §1).

## 5. 126, back into a Prison `0x00473920` — CONFIRMED

At `Age` 1 or less, 1. Then on any Prison tile (`0x00453A20(11)`, whoever owns it): a neutral
creature first takes the tile's owner (vtable `+0x24`, `0x0048D050`); then `SetState(114)` and its
result. Off a Prison, 0. `0x00473990` counts 126 as a captive with category 14.

**Entries.** The Prison's slots `+0x05` (`0x004E2C00`), `+0x09` (`0x004E2B30`) and `+0x0D`
(`0x004E2C30`) kick every place-holder to 126 (`0x004E2C11`, `0x004E2B57`, `0x004E2C41`).

**Ours.** `RoomEvictRules` and `PrisonRules.Return` (in `PrisonerWanderSystem`): 114 then 115, as
the admission does, or let out and idle off a Prison; the freed and ownership slots kick as a rebuild
does (ADR-0406). Not built: the neutral captive's owner change. A neighbour's change does not kick a
Prison: `0x004E44E0` has no arm for it.

## 6. 86, a tunneller's wander `0x00480750` — CONFIRMED

No wait for an action. If state `+0xB3` is not after now (unsigned): 0. Otherwise from
`random(4)` round N, E, S, W (`0x006BC350`) of its tile: skip a neighbour whose
`max(|n.x − [+0xA7]|, |[+0xAB] − n.y|)` exceeds `[+0xAF]` (unsigned) or that is nav-solid (world
vtable `+0x208`); otherwise `random(9)` picks one of nine points (`0x00698EC8`: x and y each of
`0x2AA`, `0x7FF`, `0xD54`) and a walk there resuming 86 (`0x00471890`, options `0x80`). All four
refused: 0.

`+0xA7`–`+0xB3` are 186's fields as `0x00485B80` writes them, positions: the stand point and the dig
point. So 86 compares a tile index against a position and reads the dig point's y as a turn, a
near-constant range and a deadline that passes only for small y. Built as read.

**Entries.** 186 at `0x00485CD8`, when its dig tile is clear, the walk to the target fails, the
planner `0x00485EF0` finds no next dig and the reach flag `+0xCB` is clear (the wall-follow came
back to its hit point four times, `tunnelling.md`); and 86's own walk.

## 7. 135, a willing torturee's way off `0x00484CC0` — CONFIRMED

Zeroes `+0x64`, `+0x66`, `+0x68` (animation) and returns 0, idle.

**Entries.** Each is a kick taken just before another move in the same call, so 135's handler
runs only when that move is refused:
- the Call to Arms recruit `0x004BDDA0` (`0x004BDF0E`), for a creature with `TortureVoluntary`
  (`+0x3EA` bit 14), which also lifts the recruit's refusals (`0x004BDEA4`–`0x004BDEE0`), then 190;
- the Heart's responder `0x004D9690` (`0x004D99E4`), then 254;
- the owner change `0x0048D050` for a creature in category 15, which a torture conversion follows
  with `KickToState(1)`.

The kick's leave from category 15 frees the device. In the recruit a taken kick also zeroes `+0x64`,
`+0x66` and `+0x68` (`0x004BDF17`–`0x004BDF1F`), the animation words 135's handler zeroes: presentation.

**Ours.** `CallToArmsRules.Recruit` kicks a willing torturee to 135 before 190 (idle if 190 is
refused). Not built: the Heart's responder. A conversion goes 131 → 1, the same leave.

## 8. 211, the gold celebration `0x00458680` — CONFIRMED

`0x00458680` returns whether an action runs; 0 is idle. Its one setter is `0x00481610`: not in 193,
`SetState(211)`; then with art slot 8 present (`0x00494860(8)`, `+0xB` bit 0) a running action
ended (`0x00496D00`, `0x00496D20`), action 35 for `random(3) + 2` plays (simulation stream, line
`0x3E2`) through `0x00496A30` and, started, the walk stopped (1); without the art, or the action
refused, back to the state it was in (`SetState(prev)`) and 0. Ours starts every action, so the
refused-action arm is not modelled.

**Entries.** Gold landing on a creature (`Object_FallAndLand` `0x0049EF90`, object kinds 1 to 3 with
a recipient at `+0x142`): `Purse_Receive(gold >> 1)`, then `0x00481610` only when the creature holds
no place (`+0x3B9` zero, `0x0049F14E`), then the debt cleared, the `+0x3EA` bit `0x20`, the pay
grievance forgotten and the treatment calm. And 206's failed disarm (`0x00459107`), which nothing
reaches (Q455).

The transition table refuses 211 from a category 0 state (2–5, 60–62, 68–70, 99, 107, 113, 126,
136, 141, 154, 211–213, 215, 245, 258, 262): same group, target category 0. So a creature idling in
3 does not cheer; one in 1, at work or walking does.

**Ours.** `DropDeliveryRules.Celebrate` at the Hand's delivery (Q570, owner 2026-10-07: a creature
cheers when gold lands on it), and 211's turn in `CreatureJobSystem`: held while the action runs,
then idle. The action's length is `AnimationTimingRules.CountedHoldTicks` for art slot 8 (LIKELY,
as for the Casino's).
