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
Training Room's two interior cells. On that build a Black Knight over the Training cap
stayed in the room's state 108 once dropped; since ADR-0393 it is refused and lifted again, as
in DK2 (Q509, the Level 17 comparison below). Portal recruitment occurred independently
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

The two same-map comparisons below were run to this protocol; it is written for the owner, who
runs DK2, and for whoever compares.

**Map and keeper.** Stock Level 17, Harkan (player 4), with the shipped PLAYERS record
and availability: the modern counterpart is `scenarios/keepers/level17_harkan_unassisted.yaml`
extended to 2,400 ticks (ten minutes at four turns a second, the Level 10 save's length).
Level 17 is chosen because its Prison is in state 3, ours lifts its Black Knights into
Training from the first ticks (Q509), and its portal feeds the room and mood messages. Q497's attack wants a
second run on Level 11A (Drako, player 5) under the same protocol.

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
(fidelity by outcomes). Two of ours to check first: Harkan's Training drops (Q509, checked
below) and,
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

**Training drops (Q509, 2026-10-06).** The capture answers Q509: Harkan lifts his three level-7
Black Knights onto the Training Room's interior cells (12,20) and (13,20) at ticks 6, 7 and 11.
Each lands (76), gets up (255, 257), enters the room's drop state 108, is refused at the cap
(110) and idles, and is lifted again within 4 ticks; the cycle repeats about every 27 ticks to
220, and again at 586-597 and 888-899. Ours lifted at 5, 6 and 7 onto the same cells, then left
the knights in 108 until 676; with states 108 and 110 built (ADR-0393) they are refused at 26-28
and lifted again, about every 25 ticks: each knight's first re-lift 45, 57 or 73 ticks after its
refusal, later ones mostly 4 to 6, as DK2's.

## Second same-map comparison: Level 11A, Drako, 2,400 ticks (2026-10-06)

Captured as Level 17 was, with the owner idle (`m12-level11A-20261006-105700`, local). Ours is
Level 11A on the PLAYERS record's own gold and mana, every placed thing, 2,400 ticks, seed 12345.
DK2's ticks are capture ticks (ADR-0119). The first two columns of ours ran Drako's AI alone; DK2
runs every computer keeper, as the last column does.

**Same route to Morgana.** Both dig north from the Lair, bridge (64,40) and (64,39) and meet
Morgana's reinforced (64,38). DK2 puts the call to arms on it; ours, since ADR-0387, two tiles
the other side of her heart, (64,34), the other end of the same drawn diagonal.

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

**Recruits alike.** Drako's mana in the capture (`player+0x82`) drops by 1,500, 3,000, 4,500,
6,000 and 7,500 at ticks 23, 59, 115, 199 and 307: Create Imp's rising cost. Ours buys its five
imps at 20, 56, 112, 192 and 292 for the same sums.

**Morgana repairs her wall (ADR-0387).** DK2's three digging imps take 2 each a tick off
(64,38); from 101 one of Morgana's imps, which had been digging her own rock beside it, stands at
(64,37) in states 23 and 24 and puts 3 back each tick, so the wall loses 3 a tick and falls at 387.
Ours had no repair job, and ran Morgana's AI not at all, so her imps were reinforcing elsewhere.
With both the wall falls at 362. What remains (Q563): Drako claims the hole at 375 where DK2's imps
fight Morgana's in it until 638, and our call goes to (64,34) at 544, DK2's to (64,38) at 700.

**The hole is fought over (ADR-0388; numbers as at that ADR).** Ours let Drako's imp take Morgana's imp's claim of the
hole, though each keeper has its own task pool, and two imps on one tile out of reach never closed
in. With both fixed they fight from 368 (DK2's from 389), but our Morgana lifts her wounded imp out at 391 and the
hole is Drako's at 399; DK2's wounded imps show the slap reaction and fight on to 638 (Q563). The owner confirms
the imps fight in such a hole and a wounded one stays; whether its keeper slaps or lifts it they
do not know (2026-10-06).

**Not scripted.** Level 11A's triggers give Drako no dig, build or attack order: they set
alliances, give Raksha three creatures and drive speech and the camera. The tunnels, bridges
and calls are the keeper's own.

**Ten seeds (2026-10-06, after ADR-0393).** Level 11A with keepers 4 to 7, seeds 1001-1010,
1,800 ticks. The wall falls at 361-403 (DK2 387, inside the spread); everything after it is
early in every seed: Drako claims the hole at 374-448 (638), calls at 417-602 (700), first
strikes Morgana's heart at 381-641 (Drako's army crosses only after its call, from 707) and
she falls at 803-997 (1,378). So the gap is systematic. In DK2, 11 imps fight in the hole
(6 of Drako's, 5 of Morgana's), against 3 a side in ours, and Drako's mana drops 5,000 three
times before the call; ours casts Heal before 700 in one seed of ten (Q563).

**Ten seeds after the slap's reaction (2026-10-08, ADR-0405).** Seeds 1-10, 1,500 ticks. The
keepers' slaps now redirect imps, so the wall falls at 72-390: early when Morgana's keeper slaps
the imp reinforcing (64,38) before it finishes, near DK2's 387 when nothing interrupts her
repairer. Drako attacks Morgana first in 6 seeds (calls 357-601, her heart falls 689-1,098) and
turns west to Belial in 4; the owner says DK2's Drako always goes for Morgana (2026-10-08), so the 4 are a divergence (Q574). DK2's capture is one draw: its Morgana slaps once in her first 200
ticks, against 2.8 expected if the step rolls every tick (`computer-keeper-management.md` §8).

**How ours turns west (2026-10-08, Q574).** Measured in ours: every seed plans Morgana at tick 1,
stores the tunnel at (64,41) and resumes it; once it ends, the next plan finds her one tunnel used
(`BreachAtXPoints` 1) and none stored, and picks Belial. The call goes to the first attacked
player, in bit order, whose heart a walker reaches: Belial (4) before Morgana (6). Drako cannot pay
for the call before about 480 (five imps bought by 331), so the west turn needs Belial's tunnel
open by then, which happens only when Morgana's wall falls by about 90, her reinforcer slapped
before it finished. Read in DKII.EXE and compared with our code: the planner `0x004FAEF0` (a used-up
enemy's stored tunnel resumed, else skipped; the nearest heart among the rest that pass the
strength test), the front `0x004FE9C0` (bits 0-15, each player's own heart, first routable), the
slap step `0x00502F00` and the tick's allowance `0x00500C20` (a refused Create Imp spends none).
Not shown in DK2: that Belial is the planner's second pick there too. Not found: what in DK2 keeps
the wall up or the call on Morgana; the reading explains our west turn, not why DK2 differs.

**DK2 over ten seeds (2026-10-09).** The idle packet `m12l11a` replayed under DK2 seeds 1-10
(`dk2runs/level11A-seeds`, 1,500 ticks; `run.ps1 -Level level11A -Packet m12l11a -Map 90x60`).
Drako's first call, taken as a 10,000 drop of his mana (player record `+0x82`; Chicken also costs
10,000, so the army's move 80 ticks later is what identifies it), sends his army to Morgana in 8
seeds (ticks 335-939), west to Belial in seed 3 (884), and splits it in seed 8 (851). Morgana's
keeper slaps her imps early in every seed; in 3, 5, 7 and 10 (64,38) is never reinforced and is dug
at 61-91, as in ours' west seeds. Given that early fall DK2 turns west once in 4 (seed 3, which tags
Belial's first tile at 103 as ours does) where ours turns west 4 times in 4: Fisher p about 0.14,
not separable on ten seeds a side. In those four seeds DK2's Drako buys two or three imps and then
spends 5,000 at a time about every 70 ticks (seed 10: 195, 263, 323, 381), Heal or Sight of Evil
(Q563); in the other six it buys five imps as ours does (seeds 2 and 8 then spend 5,000s too). Ours
buys five imps after an early fall and casts Heal only from about 720 (seed 2).

**The heart's defenders (2026-10-09, ADR-0407).** Why DK2 spends: in its early-fall seeds Drako has
a creature fighting on 348-403 of the first 500 ticks (101 in seed 1), so the battle step runs in
place of imp buying. Within two ticks of the wall opening Morgana's imps near it walk to Drako's
digger resuming 254 and fight it in the hole: her heart's update `0x004D9690` sends every creature
within seven tiles of the heart to an enemy within four (`combat-alarm.md` §7). Built, ours over
seeds 1-10: Morgana first in 8 (6 before; seeds 2, 3, 8 and 9 turned to her, 4 and 5 turned
west); the early-fall seeds 2-4 buy three or four imps before 700 and Heal three to five times.
Seed 7 has 11 imps engaging from 360 to 640, 5 of Drako's and 6 of Morgana's (6 before; DK2's
idle capture: 6 of Drako's, 5 of hers). Seeds 4 and 5 call (416, 772) after Belial's tunnel opens and turn west.
DK2 calls after its own bridge toward Belial in most seeds (seed 2: bridge 276, call 584; seed 5:
356, 939) and still sends the army to Morgana, so an open route west does not decide DK2's front
(Q574).

**Why DK2's call stays on Morgana (2026-10-09, Q574).** DK2's front is the same first-routable
walk as ours (`0x004FE9C0`, re-read). Drako's tunnel west reaches Belial only through (40,40),
which DK2's Belial reinforces at 126 in seeds 1, 2, 4, 6, 7, 9 and 10; it falls at 659 in seed 2
(call 584) and 803 in seed 9 (call 676), after the call; the other five are not itemised. Seed 3 reinforces it at 426, it falls at 776 and the call at
884 goes west; seed 8 at 480, falling at 751 before the call at 851, which splits. In seed 5 it is
never reinforced but Drako's tunnel stalls at (45,43) and the call at 939 goes to Morgana. That
the reinforced wall is what keeps Belial unroutable is STRONGLY INFERRED (the route test's result
is not captured). Ours on master reinforces it at 339 at the earliest over seeds 1-10. The 126 is one imp's
choice: its claim of (28,33) is taken at 61 by a digger whose wall had fallen, when the pool
holds no dig (the next rectangle is tagged at 62), and it takes the reinforce beside it. Ours
differs there in two ways DKII.EXE shows. State 6 never looks at the task (`0x00470C80`; seed 2's
imps walk on to (29,33) after (28,33) fell, enter 13, then 10), where ours drops the walk at once;
and a strike lands inside the striker's update, before any later imp chooses (`0x00496E00`),
where ours applies all strikes after the imp pass. Built together on a branch, ours reinforces
(40,40) at exactly 126 in 5 of 10 seeds, and every seed then calls Morgana when its call precedes
the wall's fall and Belial when it follows. Not committed: Level 1 falls to 16/20 (the Hatchery
block claimed late) and, with the walk alone, Level 3 seed 01 is lost (rooms claimed to DK2's 25
tiles, as DK2's, but 20 % less gold mined by 1000, so the Workshop is refused); both expose a
mining shortfall still to find.

## M12 acceptance against the two captures (2026-10-06)

The plan's bar: the keeper digs, expands, builds to a need, recruits, defends and attacks
unaided, measured against DK2's own keeper on the same map, differences reported. Both captures
ran with the owner idle and every computer keeper active; ours runs the same keepers on the
record's gold. Ticks are DK2's (ours in brackets).

| | evidence | agrees | differs (open) |
|---|---|---|---|
| digs | Level 17 seam; Level 11A tunnels | same seam, same tunnel route and bridge tiles | — |
| expands | Level 17 second Portal | 164 (159) | — |
| builds to a need | Level 17 Prison; Level 11A Morgana's Lair | Harkan's Prison first, on the same 25 tiles in 6 of ten seeds (the site's origin is a random room), laid row by row from (16,22); paths dug before rectangles: Morgana's path 8, 106 (7, 100-104), Lair 202 (111-196) (ADR-0394, ADR-0395) | Prison 855 (557-868 on that site) |
| recruits | Level 11A imps | five imps at 1,500-7,500 mana, ticks 23-307 (20-292), in the Level 11A section | — |
| trains | Level 17 Training drops | the knights lifted at 6, 7, 11 (5, 6, 7) onto the same two cells, refused, lifted again | each knight's first re-lift 45-73 ticks after its refusal, later ones 4-6; DK2's every one 4 (Q509) |
| defends | Level 11A, Morgana | repairs the wall from 101, which falls at 387 (seeds 1-10: 72-88 where her slapped reinforcer left it rock, else 240-404); her heart sends her imps to fight in the hole (ADR-0407): 11 engage in seed 7, 5 of Drako's and 6 of hers (DK2's capture: 6 of Drako's, 5 of hers); both lift a wounded imp out | the hole is claimed at 420 in seed 7 (638) (Q563) |
| attacks | Level 11A, Drako | Morgana first, then Belial; same bridges; the call two tiles off her heart in a drawn diagonal | call 700 on (64,38) (seeds 1-10: Morgana first in 8, calls 192-741, on (64,38) in 2; seeds 4 and 5 turn west, Q574); Morgana falls 1,378 (666-1,028) (Q563) |
