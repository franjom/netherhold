---
systems: [tooling-process, rooms, creatures, doors-traps, computer-keeper]
---
# OpenKeeper PR relevance: 1366 to 1400

Reviewed 2026-11-25. This continues [the 1296-1363 audit](openkeeper-pr-audit-1296-1363.md).
It covers every OpenKeeper PR numbered 1366 to 1400 that exists (26 PRs: 17 merged, 9 open). PR
links are provenance for leads, not proof of DK2 behaviour, and no OpenKeeper code was copied. No
native experiment was played. The function records named below were updated the same day, and so
were the stale sentences in `creature-death.md`, `payday.md` and `tunnelling.md`. Corrections 1 to
3, the secret-door reveal, the snore effect and the `PositionOf` spiral were built the same day
(HashVersion 536, `hash-versions.md`); everything else below is listed, not made.

## How each verdict was reached

Four read-only reviewers split the PRs by theme (A furniture and resting spots, B animation,
colours and camera, C health and doors, D the computer player). Each verdict is tagged:

- **Ghidra**: the reviewer re-read the function in DKII.EXE this session.
- **grep**: the reviewer only checked that our docs or function records have a matching entry.
  Read those as triage, not as a check of the binary.

Confidence is on the one scale (ADR-0642). Nothing here was checked against a native capture, so
nothing is CONFIRMED: a claim read from the instructions is STRONGLY INFERRED.

## Corrections to our docs and code

1. **An unhappy or angry creature is never idle for the computer player
   ([#1400](https://github.com/tonihele/OpenKeeper/pull/1400)).** Ghidra, STRONGLY INFERRED.
   [0x00478A20] tests the creature's `+0x3C` first: 1 returns activity code `0x1A`, 2 returns
   `0x19`, for workers too, before the class table. [0x00478B80] and [0x00478C30] post message 17
   when that value moves to 1 and message 18 plus creature event `0xB` when it moves to 2, which
   are our CreatureUnhappy and CreatureAngry messages, so `+0x3C` is the mood band (records
   0x00476FC0 and 0x00487030 already say `+0x3C` = creature `+0x28A`). `CreatureActivityRules.Code`
   (`src/DK2.Simulation/CreatureActivityRules.cs`) said the override's writers were unread and
   skipped it, so our computer keeper slapped unhappy idle creatures
   (`src/DK2.AI/KeeperAI.cs`) and picked them up for jobs (`KeeperResearchStep.cs` through
   `KeeperView.cs`). Native-captured views already carry DK2's value. Built: `Code` answers the
   mood first; the creature panel keeps the mood-free `JobCode`.
2. **Payday state 188 does not turn to the tile centre
   ([#1396](https://github.com/tonihele/OpenKeeper/pull/1396)).** Ghidra, STRONGLY INFERRED.
   [0x00472690] at `0x4726F0..0x472707` builds the turn target as the creature's 12.12 position
   shifted left 12 plus `0x800`, with no shift right by 12 first; Imp state 28 (`0x466A58`) writes
   the same tile-centre idiom correctly. So the creature turns toward a bearing that depends on its
   absolute map position, waits while the angle left is non-zero (at most 3 ticks at a Warlock's
   341 a tick), and only then enters 189. It also re-checks that the tile is the keeper's room type
   1 or 5. The PR and our `payday.md` section 4.3, `tunnelling.md` and the 0x00472690 record all
   said "turns to the tile centre"; the docs and the record are corrected. `PaydaySystem.Arrived` now
   does both, with the point in `PaydayRules.ArrivalFacingPoint`. The 8-spot table at `0x698C20` was read byte for byte and `PaydaySystem.cs:243-248`
   matches it.
3. **Terrain health reaches a dying creature in ours
   ([#1391](https://github.com/tonihele/OpenKeeper/pull/1391)).** Ghidra, STRONGLY INFERRED.
   [0x004B2CE0] returns health unchanged for a creature when the mode argument is 0 and `+0x3E6`
   bit 21 is set, for either sign of the delta. The terrain call at `0x48A640..0x48A789` passes
   mode 0, and the Hero Lair heal at `0x488A8F` is jumped over when bit 21 is set (`0x488A73`).
   `CreatureHealthSystem` applied terrain health before its dying `continue`, and its comment said
   terrain "applies to the dying". A negative delta is harmless there; the +67
   of Hero Lair terrain lifted a dying creature's health above 0, which DK2 does not. Built: terrain
   health is refused for a creature that is down and the source is still stamped
   (`CreatureHealthRules.RefuseForTheDying`, tested in `CreatureNeedsTests`). Every other heal and damage path the reviewer traced is guarded, so
   OpenKeeper's banked-heal bug has no counterpart in ours. Not traced for a dying target:
   `CreatureLevelRules.cs:119`, `EatingRules.cs:326`, `PossessionRules.Objects.cs:51`,
   `CreatureJobSystem.cs:793`, `TortureSystem.cs:129-130`.
4. **Stale prose, code right (#1391).** `creature-death.md` said lair healing is not suppressed for
   a dying creature. The binary refuses every mode-0 delta while bit 21 is set, lair heal included;
   the lair heal acts only after state 45 clears the bit (`0x468483`, `0x4684B5`). Our code already
   behaves this way. Sentence corrected.
5. **Withdrawn (#1400).** This said the [0x004764A0] record wrongly had Tremor set the fleeing
   state. The record's "Fleeing" is the flag `+0x3EA` bit 20 (set at `0x4769AB`), not state 220, and
   its body already gave resume state 1. Nothing was wrong; the reviewer of the build caught it.
6. **The walk-pace tick rate is hard-coded
   ([#1380](https://github.com/tonihele/OpenKeeper/pull/1380)).** Ghidra, STRONGLY INFERRED. [0x004B41E0] divides the animation's `+0` word by the ticks-per-second global
   (`0x6F2590`, variable 19); `src/DK2.Art/RenderAnimationClock.cs:175,180` writes 4. Every shipped
   level sets 4, so nothing changes today. `animation-clock.md` and the record now say the divisor
   is the level's variable; the code keeps the constant 4.

## Gaps in our records (the binary agrees with the PR)

- **Secret-door reveal ([#1372](https://github.com/tonihele/OpenKeeper/pull/1372), open).** Ghidra,
  STRONGLY INFERRED. The arm at `0x4A64A3..0x4A6661` of [0x004A55F0] runs when the door is open
  (`+0x10D != 0`), built (`+0x109 == 0`) and secret (`def+0x8B` bit 5), and does not test the tile
  flag. For each creature of each player that is an enemy of the door owner ([0x004BF390]) it needs
  a horizontal squared distance strictly below `0x10000` (exactly 4 tiles is excluded), a resolved
  state class other than `0x16`, `0x45A6D0(0)` true and a clear sight line (`0x4512F0`). It then
  forces the creature's target onto the door ([0x0048F390]), posts command `0x11`, clears the tile
  marker (`0x450FF0(x, y, 0)`) and rewrites the nav byte, on every tick, already discovered or not.
  Our record had only the guards and the nav write. The simulation had no reveal;
  it is built as `DoorSystem.SeenOpen` (`SecretDoorSeenTests`), with the sight line that only solid
  tiles stop (the arm passes flags 1 and 1). The nav-byte rewrite `0x4A66F0(1)`, which
  makes the open door read as shut until it next changes state, is `DoorState.SeenWhileOpen`
  (HashVersion 537). Not built: command `0x11`.
- **A hidden secret door is not drawn for a non-owner (#1372).** Ghidra, STRONGLY INFERRED for this
  branch. [0x004A6E10] returns 0 for a secret door whose owner is not the local viewer while the
  tile marker is set, with no alliance test (`0x4A6E95..0x4A6ED3`). `src/DK2.Presentation` does not
  read the marker when drawing doors (only `WorldHoverText.cs:91` does).
- **Snore effect ([#1394](https://github.com/tonihele/OpenKeeper/pull/1394)).** Ghidra, STRONGLY
  INFERRED. State 83 ([0x0046FA60], `0x46FDF3..0x46FE4B`) loads byte `+0xA7` with `0x30` on entry,
  decrements it each call, and when the old value was 0 requests the room definition's `+0x425`
  effect at the creature's position and heading, then reloads. Shipped Rooms.kwd gives the Lair 67
  there. The period is 49 calls, not the PR's 48, if the handler runs every tick (not checked).
  Presentation only: effects are view objects on their own random stream. Built as a view-effect
  request in `CreatureSleepSystem.Snore`.
- **Player colour table ([#1388](https://github.com/tonihele/OpenKeeper/pull/1388), open).** Ghidra,
  STRONGLY INFERRED. [0x004C04C0] fills 8 numbers by 3 variants at `0x6E5058`: 0 black,
  1 (191,213,224), 2 white, 3 red (230,50,50), 4 blue (50,78,230), 5 green (50,230,51), 6 yellow
  (226,230,50), 7 purple (230,50,212). [0x0050F830] indexes it by the player's byte at `+0x10FC`
  and falls back to number 1. `ClassicKeeperPalette.cs` is exactly this table, so OpenKeeper's
  keeper 3 and 4 mix-up is not ours. New to our records: the values, and that the lobby list
  (`0x5445F0`), chat (`0x544FA0`, which adds `0x20` a channel) and loading bars (`0x510950`) read
  entry (slot + 3). We build none of those screens. Our minimap markers read a shipped palette
  (`17 + owner`), not this table; whether rows 20 and 21 come out red and blue was not checked.
- **Camera installer ([#1386](https://github.com/tonihele/OpenKeeper/pull/1386), open).** Ghidra,
  STRONGLY INFERRED. [0x00449A30] accepts mode bytes 1 to 18 and copies `0x98` bytes into the
  mode's slot with no overwrite guard, which `GameDatabaseLoader.cs:21-31` already matches. One arm
  our records lack: with `[0x759AEC] < 5` (graphics detail) it sets intermediate `+0x20` to 1 and
  pulls `+0x7C` a third of the way from `+0x78`. The fields were not mapped to names.
- **Small record gaps (#1400).** [0x004BCB20] had no record: it sums byte `+0x787` of the creature
  type data over the player's creatures. That byte is `CreatureDef.Unknown_0F4C`; its meaning is
  still unknown to us too. [0x00476390] had no record: state 220's handler tries up to 10 random
  points within 2 tiles with a clear sight line and travels there, resuming state 221. Who sets
  state 220 is still unread (Q698 stays open). [0x004872A0] zeroes the dropped counter `+0x46D`.

## Approximations of ours the PRs confirm

All known and documented; listed because #1400 re-derives the exact rule ([0x004E4590], Ghidra,
STRONGLY INFERRED). The first was built the same day.

- `KeeperView.PositionOf` took the nearest room tile when an ordinary room's centroid is outside
  it; it now walks the spiral, as DK2 and `RoomIndex.FindOrdinaryCentre` do.
- `KeeperView.FirstSlabOf` (line 256) uses the lowest tile index, not the chain head that
  `SlabChain.FirstSlab` models. The two agree for the fixed 3x3 and 5x5 rooms.
- `RoomInstance.cs:36-45` uses the bounding-box centre for the hero gates (types 18 to 24), where
  DK2 uses the first slab (ADR-0625).
- Ours counts the dropped counter from the drop and reads zero in the hand; DK2 increments in the
  hand and resets in state 76 (Q768). Equivalent wherever the hand test also applies.

## Where the PR is wrong per the binary

- **#1372: a possessed creature does not reveal a secret door.** [0x0045A6D0] with argument 0
  rejects `+0x3E6` bit 7 (`0x45A7C3`), which our records tie to possession. Ghidra, STRONGLY
  INFERRED; the meaning of bit 7 is taken from our records.
- **#1372: the forced target is unconditional.** The PR reveals and then leaves the creature to its
  normal door-attack choice. DK2 forces every qualifying creature onto the door each tick.
- **#1366: an ally gets no route through the owner test.** [0x004C8BD0] compares the door's owner
  nibble with the mover and never reads alliances; the "respect doors" option refuses any foreign
  door, ally included. Opening does accept allies, and `DoorSystem.Eligible` already does.
- **#1367: a second pin is ignored, not swapped in.** [0x00416C20] at `0x416CC5` skips to the
  re-layout when a pin exists and Shift is held on another category. Our record and
  `BottomPanelTabs` already have one pin at most.
- **#1400: "a route fails only if an end is solid or the ends are in different parts".** After the
  region test, mode -2 of [0x004D0320] still runs corridor selection and corridor-to-points, and
  the count stays 0 unless both succeed. `navigation-mesh-and-search.md` sections 5-6 say so.
- **#1400: "state 0x3E" for knocked out is action 62.** The state is 216.
- **#1380: "the longer axis plus half the shorter".** The binary computes
  `|x| + |y| - (min >> 1)`, which rounds the half up. `WorldView.cs:1878-1888` has the exact form.
- **#1395: "angle 0 to the north" does not hold for the Casino.** Library and Workshop use
  `(sin, -cos)`; the Casino stand angle uses `(cos, sin)`, so 0 is +x.

## Covered, or no DK2 claim

Covered by our records and code, with the binary re-read this session (Ghidra, STRONGLY INFERRED
unless noted): #1394 bed heading, the snap onto the bed, the hidden bed except the Hero bed, the
restarted sleep action and the wake offset; #1395 the stand-off distance and the Library, Workshop
and Casino stand points, one gambler per object, and the alcove term that masks to zero (our Q572);
#1396 the 8-spot pay table (the bytes at `0x698C20` were read); #1380 the walk and run scaling and the
unscaled drunk and stealth walks (slots 34 and 38); #1371 the 20 Hz gate, integer phase steps,
random start and play-once flags; #1391 the Prison admission heal of a tenth and the 17-tick wither;
#1366 allied opening; #1400 heal targeting, the Call to Arms gate order and heart route, the Portal
drops, the dropped counter and its 300-tick gate, the state table groups and classes, the
reinforcement tests and the room reference tile.

Covered by grep only: #1392 (where DK2 stands a trainee, `training-actions.md`); in #1400 the
handlers 0x4F9520, 0x4F9710, 0x4FF260, 0x4FF5C0, 0x5006D0, 0x503690 and 0x502F00, the scared fight
state 203, and the nav bytes of 0x44FC40.

No DK2 claim: #1369, #1370, #1373, #1376, #1378, #1382, #1383, #1384, #1387, #1393, #1397, #1398,
#1399. Our records have nothing on when DK2 creatures re-pick jobs after a room is built or sold
(#1376 raises the topic without claiming a rule); the binary was not read for it.

## Suspected original defects

- **Payday state 188's turn target** (correction 2). No case for intent: a bearing that depends on
  map position cannot be meant. Effect: the heading and a wait of 0 to 3 ticks before taking pay.
- **Casino alcove angle** ([0x00456690]): already filed as Q572.
- **Snore counter** (period 49) and the **secret-door reveal** repeating every tick were both
  reviewed as candidates and not filed: an ordinary countdown, and idempotent steps.
- The payday turn target is filed as DK2-050.

## Owner questions

- Does a DK2 computer keeper slap, or pick up for a job, an idle creature that is unhappy or angry?
  The binary says no; ours does both.
- At pay time, which way does a creature face just before it takes the gold, and does that change
  with where on the map the Treasury is?
- Does an enemy walking past an open secret door reveal it, and does a possessed creature? The
  binary says yes and no.
- Does a knocked-out creature on Hero Lair terrain recover health? The binary says no.
- Do sleepers in the Lair show the snore effect about every 12 seconds?
- Does a second Shift-click on another HUD tab do nothing while one is pinned?
- What puts a creature in state 220? Tremor does not.

## Limits

Not read: the body of 0x468010 (only its call order), the consumers of the forced-target bit, the
removal-path marker clear at `0x4C37E9`, whether 0x4599D0 is the only non-zero-mode caller of
0x4B2CE0, the load order of level camera records against GlobalVariables.kwd, the parent effect's
sprite install in 0x4347E0, and the claimed sequence of the 39 shipped camera records. The PRs' own
measurements and probes were not checked. The build moved the pinned simulation hash (HashVersion
536, `hash-versions.md`); no native replay or gameplay acceptance was run. `tools/openkeeper-review/last-reviewed.txt` moves to 1400.
