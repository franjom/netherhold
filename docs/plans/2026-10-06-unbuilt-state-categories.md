# Unbuilt state categories: Casino, Research, Temple, Manufacture, Refused

Branch `states/unbuilt-categories` (worktree `D:/DungeonKeeperRemake-states`), from master
`2626d586`. Scope: 54 states that `coverage states` listed unbuilt in five categories. Each ends
built, unreachable, or unbuilt with a named open question. Milestones in order: M-A Casino, M-B
Research, M-C Temple, M-D Manufacture, M-E Refused part 1 (5, 69, 71, 86, 113, 126, 135, 211),
M-F Refused part 2 (the other 24, grouped by entry path).

## M-A Casino (2026-10-06)

- **Built:** 146, 147, 148, 149, 150, 151, 153 (`CasinoStep`), and the jackpot winner's 152, 155,
  156, 157 (`CasinoJackpotRules`). Those four are category 0 (M-F's list) but share the jackpot's
  entry path, so they were read and built here; M-F has 20 left.
- **Not in scope but read:** 154 is the eviction a Casino's loss starts (`0x004D70D0`,
  `0x004D7100`, `0x004E44E0`); left for M-F.
- ADR-0396, HashVersion and `WorldSnapshot.FormatVersion` 196, research `casino.md`, 27 new
  function records, 25 updated. Tests: 4,396 passing (3 install-gated skips); new
  `CasinoStateTests` and `CasinoOnRealDataTests` (4).
- Replays, both sides run at master `5691f56d` and again at `95daa5f6`: Level 1 20/20 → 20/20,
  Level 2 2/20 → 2/20 (seeds 14, 17), Level 3 seed 01 won at 3560 → 3560 (DK2 3683). No Casino on
  those levels.
- New open question: Q568 (owner: do drinkers only use alcoves on a Casino's north wall?).
- Reviewer, three passes. Fixed: ADR and Q numbers taken by master (now ADR-0396, Q568); the
  party's tile-effect draw on the simulation stream (now drawn); a flood fill every turn in
  `CasinoSystem` (now only on settlement or party turns); the `+0x38`/`+0x3C` stores while a
  deadline runs (recorded as unread); "of the keeper" in 155's pile search (any owner's); "side"
  alcoves in the ADR (south only is shown); 152's action 57 length (marked LIKELY); a bare -1
  sentinel, 154's row note and the lowest-tile party loss (stated); no settlement while a party is
  on (built, with a test and its positive control). Nothing rejected.

## M-B Research (2026-10-06)

- **Built:** 53, 54, 56, 57, 59 (`ResearchStep`), and the Library arm of the disturbed room
  (`0x004E88A0`) in `RescueSystem`'s contact test.
- **Unreachable:** 55 and 58 (the stub that returns 0; no SetState, wrapper, walk, `+0xDF` store or
  room-table site enters them, `library-states.md` §2), rows moved as afa9ad7f did.
- Also closed: idle-fallback.md §6's UNKNOWN (the record reader sets the field that stores
  `def+0x76A`). Two corrections land with it: a speed effect stops at the `0xFFF` word SetSpeed
  stores, for Haste too; the disturb and the Casino party's tile effect fire on DK2's turn.
- ADR-0397, HashVersion and `WorldSnapshot.FormatVersion` 197, research `library-states.md`, 16 new
  function records, 12 updated. Tests: 4,409 passing (3 install-gated skips); new
  `LibraryStateTests` (10) and `LibraryOnRealDataTests` (2), a clamp test in
  `CreatureStatusEffectTests`.
- Replays, both sides run at master `95daa5f6`: Level 1 20/20 → 20/20, Level 2 2/20 (seeds 14,
  17) → 2/20 (seeds 5, 13), Level 3 seed 01 won at 3560 → 3560 (DK2 3683).
- Not built, stated: the Library's storage gate `0x004E1AC0` inside the capacity test, for every
  caller of `RoomCapacityRules.HasRoom`; the Training Room's arm (112 to 109).
- Reviewer, two passes. Fixed: 59's hurry used the definition speed, not the intruder's current word,
  and the 0xFFF store clamp was missing; the disturb's turn phase; the contact call's gates on the
  Library arm; `FindRoomWithPlace` excluded its own place; confidence tags; per-tile slot scans; a
  stranded doc comment and a dead `case`. Recorded, not changed: tie and kick order, the party-speed
  word. Nothing rejected.

## M-C Temple (2026-10-07)

- **Built:** 101, 103, 104, 105, 106, 107 (`TempleStep`). 107 is category 0 (M-F's list) but is the
  Temple's own return path, so it was read and built here; M-F has 19 left.
- **Unreachable:** 102 (`SetState(101)`; nothing enters it, `temple-states.md` §2).
- **Found while building:** ours walked every job to its room's centre, a Temple's pool. DK2's room
  walk ends at `0x004E4C10`'s point (§9); `RoomWalkRules.ArrivalTile` now serves every job room.
  The owner chose the general fix over a Temple-only one (2026-10-07).
- ADR-0398, HashVersion and `WorldSnapshot.FormatVersion` 198, research `temple-states.md`, 13 new
  function records, 11 updated. New `TempleStateTests` (11), `TempleOnRealDataTests` (1); job,
  torture and payday fixtures given a tile the room point accepts.
- Replays, both sides run at the branch's `71d1a0dc`: Level 1 20/20 → 20/20, Level 2 2/20 (seeds 5,
  13) → 5/20 (seeds 3, 5, 10, 13, 14), Level 3 seed 01 won at 3560 → 3608 (DK2 3683). Level 2
  moved past the one-seed bar; the owner accepted it.
- Pre-existing divergence found, not changed (Hand system): ours sacrifices a creature dropped on
  any tile of the keeper's Temple; DK2 only in the pool, after the landing (§8).
- Reviewer, two passes. Fixed: 105's floor-slot test compares the creature's own tile with itself,
  so the step is always taken (was refused into the pool; record 00472E90 now LIKELY); "floor height
  0" is exactly "floor slot" for a Temple tile (a deviation that did not exist); 0048AD30's body.
  Declared: corpses not counted on a tile, 103's skipped nav and solid tests, a draw per failed
  candidate room. Still open, not this branch's: master's `DBG t=` stderr dump in
  `CreatureJobSystem.TrySetOut`.

## M-D Manufacture (2026-10-07)

- **Built:** 63 (its handler, which ours only entered), 64, 65 (`WorkshopStep`).
- **Unreachable:** 66 (`workshop-states.md` §2).
- **Blocked:** 67, the slap's state for a Workshop holder (`0x0048B9DB`). Ours makes none of the
  slap's state moves (67, 77, 120), and building them is Hand code, outside this task: Q569.
- With no order, 63 plays action 43 and the manufacturer goes idle, as the binary's `player+0xA1C`
  test does; `ManufactureTests` pinned the old stay-and-credit and was rewritten.
- ADR-0399, HashVersion and `WorldSnapshot.FormatVersion` 199, research `workshop-states.md`, 7
  new function records, 1 rewritten (`00470110`, was GUESS), 4 updated. New `WorkshopStateTests`
  (6), `WorkshopOnRealDataTests` (2).
- Replays, both sides run at the branch's `e6925731`: Level 1 20/20 → 20/20, Level 2 5/20 → 5/20
  (same seeds), Level 3 seed 01 won at 3608 → 3600 (DK2 3683).
- Reviewer, two passes. Fixed: 67 is entered by the slap through a push and a jump to a shared
  `SetState` tail, which the call scan missed (now blocked on Q569, and a trap in `docs/TRAPS.md`);
  `0x00470C5C` is state 70's, not 67's; the work's length (LIKELY), the machines' presentation
  (STRONGLY INFERRED), the object-list and null-room deviations declared; the drop test's pin
  restored and two tests added. The scans for 102 and 66 were redone as raw byte scans: still none.
