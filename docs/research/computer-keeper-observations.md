# Computer keeper: stock Level 10 capture

The owner left Level 10 ("Smash", Woodsong) running with player 4 Asmodeus, then
saved `Data/Save/woodsong_ai.SAV` after roughly ten minutes (2026-09-27).
The save header identifies `level10`, game tick 2408, and the supported
`May 12 200012:28:22` build. `Level10Players.kld` identifies player 4 as the
computer keeper and player 3 as the human. These are CONFIRMED data facts.

The tile-grid writer in `DKII.EXE` at `0x0044F3F0` writes an eight-byte map
header and calls `0x00452B90` for ten bytes per tile, starting at the live
tile's `+2`. The map header in this save starts at `0xE7D2`; the tile records
start at `0xE7DA`. `Dk2SaveReader` now finds the true record start and reads
room, health, owner and terrain from the same tile (Q436). This layout
is STRONGLY INFERRED from the binary and confirmed by the map header's 58×100
dimensions and complete agreement with the design map's terrain lane.

| Saved state versus `level10Map.kld` | Count |
|---|---:|
| Terrain bytes changed | 0 / 5,800 |
| Ownership bytes changed | 16 / 5,800 |
| Neutral portal tiles at (26–28, 4–6) now owned by player 4 | 9 |
| Player 3 rock tiles now neutral | 7 |

The nine portal tiles share runtime room id 1125. The save does not record
which actor or trigger changed their owner. A modern control run with no keeper
AI also transfers those nine portal tiles, so the transfer is no evidence of
an AI order; the original actor remains UNKNOWN. No terrain change means this capture gives no evidence of digging
or construction. It also cannot prove the absence of recruiting, combat,
orders, traps or spells: entity sections and the AI command stream have not
been decoded (Q182). A same-map modern run can compare these saved tile facts,
but a full behavioural comparison needs more original observations.

## Modern run on the same stock map

A temporary headless run before the availability fix used `level10Map.kld`, the level's VARIABLES, PLAYERS,
THINGS and TRIGGERS, simulation seed 12345, keeper seed `12345 xor 4`, and
`KeeperAI` as player 4's input source after each tick. Its initial state held
10 player-4 creatures, 10,000 player-4 gold and 100 mana. At tick 2408 it had
changed 47 terrain tiles and held 471 tiles, versus 0 changed terrain and 427
owned tiles in the original save. The keeper emitted 18 `BuildRoomCommand`s,
9 `TagTilesCommand`s, 67 `CreateImpCommand`s and 6 `SlapCreatureCommand`s;
emission does not establish that every command succeeded. This is a
CONFIRMED result of the implementation before the availability fix.

The two captures share the stock map and elapsed game ticks, but their random
seeds, starting entities and resources have not been proved equivalent. The
modern model also has a one-tick AI input delay (§12.1 of `KEEPER_AI.md`).
The former modern room-building difference has a verified cause: `KeeperAI` defaulted
its room-availability predicate to true. Level 10's player-4 room entries are
state 4 (unavailable room) for its listed ids 1–4, 6–12 and 15, and state 1 (unavailable) for
13, 14 and 16. The original room chooser `0x004F93B0` accepts state 3. The
modern keeper then emitted nine Prison (11) and nine Torture Chamber
(12) builds, all on state-4 rooms, by tick 541. Campaign trigger action 6 can
change availability during play, so the room chooser must query current state
each tick. The fix removed all 47 terrain changes in the same-map run.
The permanent capture is `scenarios/keepers/level10_asmodeus_idle.yaml`;
`DK2.Harness` writes each emitted command's input tick and stable hash to
`keeper-commands.tsv`. The original exploratory run is ignored under
`reverse-engineering/.cache/m12save/Sim.cs`.

After the fix, the keeper's default checks current room availability (`state == 3`).
At tick 2408 the modern run emitted no room build or dig commands and matched
the original save on every terrain byte
and all but the same seven player-3 rock ownership fields as the no-AI control.
It still emitted 67 Create Imp commands, so the tile match is not proof of
equivalent AI behaviour or successful spell casts.

The reviewed planner/message revision reproduces these counts and mismatches.
Modern harness artifacts: `build/m12-review/` (ignored); the existing original
save remains the comparison source, not a new original run.

A control with the same modern initial state, random seed, triggers and 2408
ticks but no AI input ended with zero terrain changes and 427 player-4 tiles.
It matched the original save on all 5,800 terrain bytes and all but seven owner
fields. Those seven are player-3 rock tiles that became neutral in the original
but remain player 3 in the modern control. After the availability fix, the
modern AI run and this control have the same terrain and tile ownership at
tick 2408. Whether the original keeper issued other orders remains UNKNOWN.

## Unaided keeper on another stock map

`scenarios/keepers/level17_harkan_unassisted.yaml` uses the shipped Level 17
PLAYERS and availability records for Harkan (player 4). Prison (11) is in
state 3. In the 1,200-tick modern run on the training-moves build (2026-10-02),
the keeper emitted 32 tag commands, 25 room-build commands, nine slaps and 30
pick-up/drop pairs. Imp work dug marked rock, and the Prison first built at tick
219; 25 tiles were built by tick 335. The 30 moves are step 9's: its three placed
level-7 Black Knights four times each, and portal recruits after them, onto the
Training Room's two interior cells. A Black Knight over the Training cap is refused,
forgets the job and is lifted again (Q509). Portal recruitment occurred independently
of the keeper command stream. These are CONFIRMED modern-run facts, not an
original-game comparison. The scenario has no scripted player commands and has
regression expectations for digging, the Prison build, and portal recruitment;
`level17_harkan_training.yaml` asserts the first moves.

On Level 10 the same build has Asmodeus (skill level 10, never ready) lift its
creatures 13 times into its two Training Rooms' interior cells (22,16) and (32,16)
from input tick 2. The original save cannot confirm or refute this: its entity
sections are undecoded (Q182), so where Asmodeus's creatures stood at tick 2408
is UNKNOWN (Q509).

## Same-map comparison protocol (ADR-0353; Q497, Q509)

Nothing in this file is yet a command-by-command comparison. This is the run that would
make one; it is written for the owner, who runs DK2, and for whoever compares.

**Map and keeper.** Stock Level 17, Harkan (player 4), with the shipped PLAYERS record
and availability: the modern counterpart is `scenarios/keepers/level17_harkan_unassisted.yaml`
extended to 2,400 ticks (ten minutes at four turns a second, the Level 10 save's length).
Level 17 is chosen because its Prison is in state 3, ours lifts its Black Knights into
Training from the first ticks (Q509), and its portal feeds the room and mood messages. Q497's attack wants a
second run on Level 11A (Drako, player 4) under the same protocol.

**In DK2.** v1.70 (`May 12 2000`), started from the command line with
`-level Level17 -PSAVE m12l17`, so the packet recorder (`navigation-captures.md`) writes
`PacketRecord\m12l17.sav`. Then remove the input: no tagging, building, hand, spells or
possession for ten minutes; the camera is free. Take world logs (`0x0050EA70`, the Cheat
Engine recipe in `debug-dumpers.md`) about every two and a half minutes and one at the end;
each records its `GameTick`, every creature's position, state and `Anger:` block, every
room, and the `PlayerMessageQueue:` with each record's type, recipient, Data1/Data2 and
handled bit (`computer-keeper-messages.md` §1; the pool is world `+0x2E54`). Save at the end
for the tile grid (`Dk2SaveReader`). Note, with the clock, any room appearing and any
creature the keeper lifts.

**What to compare,** per minute of game time, with differences reported, not tuned away:

| Quantity | DK2 source | Ours |
|---|---|---|
| keeper commands by kind: tag, room tile (type, tile), Create Imp, slap, pick-up/drop (creature, tile), cast | the packet record, if player 4's actions travel in it; else inferred from consecutive world logs | `keeper-commands.tsv` |
| rooms by type, tile count and first tile; tiles dug; tiles owned | world logs, the final save | `world.txt`, `events.log` |
| creatures by type and level; Training and Lair occupants | world logs | `events.log` |
| player 4's messages: type, Data, handled, when first seen | `PlayerMessageQueue:` | room messages in `events.log`; the others are not logged yet |

The first record settles the command column: with the human idle, any command it holds
for player 4 is the keeper's; none means the keeper's actions bypass the packet stream and
the world logs are the only source. The seeds differ, so compare when and where, not
equality: rooms at the same spots, tiles dug at nearly the same time, the same rooms built
(fidelity by outcomes). Two of ours to check first: Harkan's Training drops (Q509) and,
where a creature turns unhappy, whether he pays it or drops it in its Lair (ADR-0353).

## First same-map comparison: Level 17, Harkan, 2,400 ticks (2026-10-05)

Captured with the owner idle (full-state recorder plus `-PSAVE m12l17`; the driver and the
decoder are local, gitignored tools). Ours is `level17_harkan_unassisted.yaml` run for 2,400
ticks. The captures are not in the repository (about 26 MB); the numbers below are the record.

**The packet record holds no action.** 2,403 turns, 0 actions, with the human idle (parsed with
a port of `PacketLog`'s framing). So Harkan's actions bypass the packet stream: STRONGLY
INFERRED, and the world state and the message queue are the only source for what he does.

**Same start.** Player 4's gold at tick 1 is 10,000, as the scenario sets. Both sides convert
their two Portals at nearly the same time (DK2 ticks 40 and 164; ours 36 and 149).

**Different order, and a starved keeper on our side.**

| | DK2 | ours |
|---|---|---|
| first gold tagged / mined | `GoldMined` 3,000 by tick 160, 30,000 by tick 700 (seam at (16..22, 2..3)) | footprint tag at tick 5 is a 25-tile Prison at (10,23)-(14,27); the seam is first tagged at tick 1,693 and mined at 1,970 |
| gold | 10,000 -> 26,250 (tick 480) -> 31,410 (tick 855) | spent on Prison tiles (750 each) and out of gold from about tick 448 |
| Prison | first tile at tick 855, centre (16,22), 25 tiles | first tile at tick 232, centre (12,23) |
| after the Prison | gold 31,410 -> 0 by tick 1,920 on a Training Room growth (12 -> 20 tiles, about tick 1,200) and a stone bridge (tick 1,414) | about 1,180 `BuildRoomCommand`s refused `NotEnoughGold` (ticks 448-1,690), no Training growth, no bridge |

DK2's Harkan digs gold first and builds when he has it; ours built first and starved, and the
cause is the scenario, not the keeper: `placed_things` pays the PLAYERS record's 10,000 into the
heart and `setup.players` deposits another 10,000, so ours held 20,000 and passed the room
planner's `750 x DefaultRoomSize 25 = 18,750` test that DK2's 10,000 fails (KEEPER_AI.md §5.5).
With `setup.players` gold at 0, ours tags the same seam from tick 3 and mines from tick 91,
and the refused-command loop is gone. Ten keeper scenarios on campaign maps added gold this
way; since 2026-10-05 a scenario's gold replaces the record's (`scenarios/README.md`).

**What remains (Q546).** Ours mines four seam tiles by tick 284 (DK2's Harkan: eight of
(16..22, 2..3) by tick 480, two more by 855), then an untag at tick 286 clears the seam, and
from tick 158 ours tags a one-tile path along the map edge and builds no Prison; DK2 builds
it at 855.

**Why the path (traced 2026-10-05).** Harkan's room passes alternate the unaffordable Prison
(-> the seam) with a neutral-Portal search. Ours claims its second Portal at tick 149 and, at
tick 153, picks the neutral Portal at (53,6), about 40 tiles east: Harkan's
`NeutralRoomRangeFromHeart` is 100, so the range test passes as KEEPER_AI.md §5.5 reads it.
Player 5 claims that Portal at tick 161; a Task 7 bound for Task 4 does not abort, so the dig
continues. Undiggable rock at (32..42, 7) starts a wall hug that follows the map edge: with
`LineWalker` rebuilt to `0x00502230`/`0x005027D0` and re-measured, ours still tags about
700 tiles from tick 158 to 2,236 (untags at 286 and 1,242). Whether one of the walker's
listed deviations (computer-keeper-tasks.md §5) lengthens that hug is open. In DK2 (capture ticks, one behind the game's turn, ADR-0119) Harkan claims his
second Portal at 164 and player 5 takes (53,6) at 165, and Harkan never digs east.
The race reading first offered here was wrong; *What it was* below gives the cause.

**Owner, a second game (2026-10-05).** Played normally for over ten minutes with the map
revealed (the cheat, `game-actions.md` §3.1): Harkan digs east only along the straight gold
row by his base, never on toward the top-right keeper, and then southeast toward the hero
fortification in the map's centre. OBSERVED, one game with a playing human; it agrees with
the idle capture, where Harkan owned no tile in x 24-51 of the top rows (y 0-13), sampled
from tick 1 to 2,400.

**Why our Portal claims run early (2026-10-06).** A slap, not the cadence. Harkan's
second Portal is reached over neutral path (2..5, 2), claimed one tile at a time by one imp.
DK2 takes 13 ticks a tile ((3,2) 105, (4,2) 118, (5,2) 131): state 10 for 1 tick, 3 walking,
state 15 for 1, state 16 with the tile flipping on its 7th tick and holding 1 more, then 10
again. Ours takes 11 (99, 110, 121), and 26 for the Portal against 30 (149 against 164).
Walk, entry and application ticks match (first at E+2, then every tick); ours adds 185 an
application where DK2 adds 164, so its tile flips on the 4th, not the 5th, and its imp
leaves a tick sooner. Our Harkan slapped that imp at tick 86 (90 % for 100 ticks); DK2's
slaps only an imp with no task or one reinforcing (`computer-keeper-management.md` §8), so
its claimer stays at 80 %. Ours slaps any working creature: that is the early claim.

**What it was (2026-10-06, ADR-0376).** Not the race: DK2's Harkan claims his second Portal
before player 5 too. His record's `MaxBuildArea` is (1,1)-(40,64), and the room planner
(`0x004FC690`) refuses a neutral Portal outside it before any range test, so (53,6) and (16,70)
are never candidates; ours planned over the whole map. With the area and the slap gate built,
ours tags nothing outside the area, claims the second Portal at 159 and builds its Prison at
476 centred on (12,25), against DK2's 855 on (16,22) (Q555).

## Second same-map comparison: Level 11A, Drako, 2,400 ticks (2026-10-06)

Captured as Level 17 was, with the owner idle (`m12-level11A-20261006-105700`, local). Ours is
Level 11A on the PLAYERS record's own gold and mana, every placed thing, 2,400 ticks, seed 12345.
DK2's ticks are capture ticks (ADR-0119). The first two columns of ours ran Drako's AI alone; DK2
runs every computer keeper, as the last column does.

**Same route to Morgana.** Both dig north from the Lair, bridge (64,40) and (64,39), meet
Morgana's reinforced (64,38) and put the call to arms on it.

| | DK2 | ours (ADR-0377) | ours (ADR-0384) | ours (ADR-0387, keepers 4–7) | ours (ADR-0388) |
|---|---|---|---|---|---|
| (64,41) dug | 52 | 47 | 47 | 47 | 47 |
| Morgana reinforces (64,38) | 64 | 62 | 62 | 62 | 62 |
| bridges (64,40), (64,39) | 66, 70 | 62, 63 | 62, 63 | 62, 63 | 62, 63 |
| Morgana repairs (64,38) from | 101 | — | — | 92 | 92 |
| (64,38) dug through | 387 | 229 | 229 | 362 | 362 |
| (64,38) claimed by Drako | 638 | 273 (Morgana retakes it at 325) | 241 | 375 (Morgana retakes it at 472; Drako again at 724) | 399, after a fight from 368 |
| Belial's first tile (48,43) | dug 443 | tagged 68 | dug 281 | dug 433 | dug 433 |
| bridge (47,43), (46,43) | 457, 461 | 99, 100 | 297, 299 | 446, 448 | 446, 448 |
| call to arms | 700 at (64,38) | 418 | 308 | 544 at (64,34) | 540 at (64,34) |
| bridge (64,46) | 1,155 | 420 | 707 | 855 | 832 |
| Morgana defeated | 1,378 (status 1) | 847 (heart ruined) | 730 | 1,004 (heart ruined) | 887 (heart ruined) |

**Raksha was an ally.** Before ADR-0377 ours attacked every keeper in turn: Morgana at tick 1,
Raksha at 66 (a tunnel north from (50,43) with eight bridge tiles), Belial at 273 and the human
at 318. Level 11A's first turn runs trigger action 18 for 7-6, 7-4 and 7-5, which allies Raksha
with the other keepers (`trigger-action-arms.md`); the capture shows the bytes set on turn 2,
and DK2's Drako never digs toward her. Ours left the action unperformed.

**The tunnel ended at its last tag (ADR-0384).** Ours tagged (64,38) at 64 and ended Morgana's
tunnel there: a flood from the heart through its own tags reached her heart. DK2's task 7 waits
on a tagged tile until it is dug and ends only when the target routes to the cursor, so its
Belial tunnel follows Morgana's wall. Since ADR-0384 ours does the same, and the bridges at
(57,38) and (57,39) move from 164 to 871 (DK2 has none in 2,400 ticks).

**Morgana repairs her wall (ADR-0387).** DK2's three digging imps take 2 each a tick off
(64,38); from 101 one of Morgana's imps, which had been digging her own rock beside it, stands at
(64,37) in states 23 and 24 and puts 3 back each tick, so the wall loses 3 a tick and falls at 387.
Ours had no repair job, and ran Morgana's AI not at all, so her imps were reinforcing elsewhere.
With both the wall falls at 362. What remains (Q563): Drako claims the hole at 375 where DK2's imps
fight Morgana's in it until 638, and our call goes to (64,34) at 544, DK2's to (64,38) at 700.

**The hole is fought over (ADR-0388).** Ours let Drako's imp take Morgana's imp's claim of the
hole, though each keeper has its own task pool, and two imps on one tile out of reach never closed
in. With both fixed they fight from 368, but our Morgana lifts her wounded imp out at 391 and the
hole is Drako's at 399; DK2's wounded imps show the slap reaction and fight on to 638 (Q563). The owner confirms
the imps fight in such a hole and a wounded one stays; whether its keeper slaps or lifts it they
do not know (2026-10-06).

**Not scripted.** Level 11A's triggers give Drako no dig, build or attack order: they set
alliances, give Raksha three creatures and drive speech and the camera. The tunnels, bridges
and calls are the keeper's own.
