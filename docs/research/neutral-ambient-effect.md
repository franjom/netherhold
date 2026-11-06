---
systems: [simulation-core]
---
# The neutral player's ambient tile (0x004BB130)

Binary readings of DKII.EXE v1.70, 2026-11-03. The decision is ADR-0646 (hash/save 395).
Function-level facts are in the records [004BB130](functions/004BB130.md) and
[004B9EC0](functions/004B9EC0.md).

## Rule (STRONGLY INFERRED, the disassembly 0x004BB130..0x004BB293; the pair CONFIRMED in the draw log)

`Player_Tick` (`0x004B9EC0`) returns through `Player_GenerateAmbientEffect` for the neutral
tag (`0x006F2548`, `0x004B9F22`), after the player's trigger root and `0x004B9740`. The body:

1. `x = random(world+0x9E6 - 2) + 1` (`0x004BB156`, Player.cpp line 0x7FD) and
   `y = random(world+0x9EA - 2) + 1` (`0x004BB181`, line 0x7FE) on the world seed
   (`world+0x15A0`): an interior tile of the map (width `+0x9E6`, height `+0x9EA`).
2. The tile's terrain byte (`+0xA` of the vt `+0x1D8` tile) is compared with world vt
   `+0x2C0`, the Lava terrain id ([room-floors-and-liquids.md](room-floors-and-liquids.md) §2).
   Any other terrain returns 1 with no further draw.
3. On Lava: `random(1000)` (`0x004BB1F1`, line 0x806). 0, 1, 2 and 3 pick effects 316, 317,
   318 and 319 at height 0; anything else picks effect 302 at height 0x1000.
4. `random(0x800) & 0x7FF` (`0x004BB250`, line 0x822) is the heading word.
5. World vt `+0x38C` requests the effect at the tile centre (`(x << 12) + 0x800`,
   `(y << 12) + 0x800`) for the neutral tag. That slot forwards (`0x00509580`) to `+0x388`
   (`0x0050E490`), the presentation interface; its `0x00440420` only gates and calls the
   allocator `0x00440350`.

That the effect's creation draws nothing from the world stream is STRONGLY INFERRED, not
confirmed. The allocator's initializer uses the presentation seed `0x006D6450`
([keeper-spell-tremor.md](keeper-spell-tremor.md) §3), and the one world draw reachable below it
(`0x004B37D0`) needs an owner thing, which an effect's animation lacks
([workshop-timers.md](workshop-timers.md) §5). The helpers below `0x00440350` are not all read,
and no capture has a Lava hit. On that reading the world stream pays two draws a turn, four
when the tile is Lava. The heroes' frame (`0x004B9F3A`) returns before any draw.

Before the neutral return, `Player_Tick` runs the player's trigger root and `0x004B9740`.
`0x004B9740` draws on the world seed (Player.cpp lines 1048 and 1071) on turns divisible by
9600 and 6000, behind world `+0x72A1` bit 4 and player `+0x19A == 0`. Nothing read so far keeps
the neutral player out of them, and ours makes neither draw for any player (Q738).

## Native draw log: Level 5 seed 1, turns 1401-1900

`F:/DK2Evidence/studies/q712-native-drawlog-level5-seed1--2026-11-03--001` (DK2's own
world-seed draw log, every draw attributed to its turn against the frames' seeds, no gaps):
Player.cpp:2045 and 2046 are drawn 500 times each, once per turn, and they are the last two
draws of every turn. Line 2054 (the Lava roll) is never drawn in that window, so the Lava branch rests on the
disassembly alone. In 4 turns a keeper's mana-tail draw (Player.cpp:2254, `0x004BB2A0`) comes
just before the pair: the neutral frame follows at least one keeper's. The draw before
them is a Treasury's (Treasure.cpp:132 or 162) in 486 turns; the rest are keeper-frame or
creature draws.

## The remake

`NeutralAmbientEffectSystem` runs these draws when the neutral player is in the active list,
right after `DungeonHeartSystem`'s keeper verdicts and before the Tremor's world records. DK2
walks the active list, which is the PLAYERS records reversed. No shipped level lists a keeper
before the neutral player (`NeutralAmbientOnRealDataTests`), so the neutral frame always comes
after every keeper's. Ours runs the keepers' trigger roots all together at the start of the
player phase (`PlayerTriggerSystem`); DK2 runs each player's root in its own frame, so the
neutral root runs after the keepers' frames. That difference is older than this rule and
unchanged by it.

## The remaining draw gap

Over the same 500 turns the log holds 8480 draws, about 15-19 world-seed numbers a turn, against
about 4 in ours before these consumers. Built, in the order found: the neutral frame (ADR-0646,
hash/save 395); the loose chicken, object process 10 (`0x004A26D0`; ObjectState.cpp lines 2851,
2914, 2915; 2421 and 46 draws over the 500 turns; ADR-0647, hash/save 396); and the effect
generators, `EffectGenerator_Update` (`0x004B1B60`; lines 173, 179, 180, 188; ADR-0648, hash/save
397, [evidence](hero-parties-and-effect-generators.md#effect-generators-in-dk2-read-2026-10-05-q255-q259)).

The draw sites logged as object states 2523, 2558 and 2559 belong to an owned Hatchery's object
process 6 (`0x004A1F30`): line 0x9DB, `random(4)`, at its side search, and lines 0x9FE and 0x9FF,
its point draws at `0x004A2097` and `0x004A212B`. Ours already mirrors them in
`ChickenMovementRules.ChooseRoomPoint`; they were never a missing consumer, only unmapped.

The later [turn-1403 probe](#egg-collision-at-turn-1403) identifies an Egg collision consumer built at431
in this log. Two ADR-0649 fixes (hash/save 398) came from
comparing the birds: a loose bird's side test compares the raw navigation bytes, so the Heart's centre (3)
refuses every floor side, and a Treasury or Heart tile holding gold holds a Gold Pile object, an obstacle.

Measured 2026-11-04 (one modern Level 5 run restored from the seed-1 capture at 1400, a scratch
adapter; studies under `F:/DK2Evidence/studies/q712-seed1-draws-h397--2026-11-04--*`), turns 1401-1900
against the log's 8466 draws:

| restored at 1400 | study | ours draws |
|---|---|---|
| creatures only | `--002` | 6543 |
| and DK2's 19 Chickens | `--004` | 7790 |
| and the Treasuries' gold, at hash 398 | `--006` | 8091 |

(`--005` is excluded: stale payload.) With the Chickens and store gold restored, turn 1401 matches DK2's
log site for site, and 39 turns have equal counts. The next difference is turn 1402: three
`RoomWalkRules.ArrivalTile` draws by our three Warlocks, which the creature restore leaves choosing
lairs and jobs, where DK2's were in states 7/80 (making a home), 6/54 (going to research) and 56
(researching). That is restore fidelity, like Q719's partial inputs, not a rule. The scratch restore adapter is not extended
to those states in that measurement. The owner decided on 2026-11-04 to keep building rules backed by the binary or a capture and
to stop steering Q712 by outcome averages (see [Outcomes at hash 399](#outcomes-at-hash-399)); the later
[bounded restore comparison](#bounded-restore-comparison-2026-11-06) reopens only the first-divergence probe. Q738
(`0x004B9740`'s periodic draws) is separate.

## Native draw log: Level 4 seed 1, turns 1401-1900

`F:/DK2Evidence/studies/q712-native-drawlog-level4-seed1--2026-11-04--001`, the same kind of log on the
`l4live` natural seed: 10206 draws over the 500 turns, every one attributed to its turn (median 21 a turn),
from 57 sites, 13 of which the Level 5 window never draws. The run's MESSAGE.LOG shows "Gone Out Of Sync
(Checksum) GT 622", yet all 501 frame seeds occur in the owner's `level4-live1/seed01` capture, offset one
tick, as with Level 5. Of the 13:

- already mirrored: PatrolState 72 and 73 (`0x00471EB0`), Training.cpp:188 (`0x004F2FF0`), Hatchery.cpp:289,
  306 and 307 (`0x004DE620`), WaitState 54 and 60 (`0x00486CF0`), TrainState 101 (`0x00485310`), ObjectState
  2030 (`0x004A13F0`), ManufactureState 188 (`0x00470470`), ShotProcess 189 (`0x004AD7A0`);
- built from this log: State.cpp:2100 and 2101 with PlayerRooms.cpp:644, an idle creature's Heart walk
  (`0x00478680`; ADR-0656, [idle-fallback.md](idle-fallback.md#2-0x00478680--strongly-inferred)), 10 times
  each for a keeper with one Heart, always in the order State.cpp:1700, PlayerRooms.cpp:644, 2100, 2101
  (CONFIRMED);
- built from this log: ManufactureState.cpp:469 (`0x00470B20`, state 68 on a crate made, `random(2)` when art
  record 8 resolves present; ADR-0671, hash 415);
- built from this log: Hatchery.cpp:187 (`0x004DE220`, a coop's placement `random(4)`, 4 consecutive draws in each
  of 6 clusters at the first index of a turn; the coops are rebuilt on a room change; ADR-0672, hash 416). No site
  of the census is unbuilt.

## Native draw log: Level 3 seed 1, turns 3-3700

`F:/DK2Evidence/studies/q712-native-drawlog-level3-seed1--2026-11-05--001`, the same draw log on the `l3live`
natural seed over the whole replay, with the world seed read at every `World_UpdateTurn` return (`seeds.jsonl`)
instead of frames. 61153 draws over 3698 turns, every one attributed to its turn (median 15 a turn), from 97
sites. MESSAGE.LOG reports a checksum desync from GT 18 (low bits only), yet all 3699 per-turn seeds occur in the
owner's `level3-live1/seed01` capture at offset one tick, as with Levels 4 and 5. The 25 sites neither the
Level 4 nor the Level 5 log draws, read in the binary (STRONGLY INFERRED where not stated):

- already mirrored: PatrolState 148 and 165 (`0x004721C0`, hero roaming state 175), Creature.cpp 5642-5644
  (`0x0048E160`, burning), ImpState 233 and 384 (`0x00463DF0`), ImpState 522 (`0x00464240`), Training.cpp 148
  and 289 (`0x004F2FF0`, `0x004F3310`), Creature.cpp 6651 and 6652 (`0x0048F930`), Player.cpp 4701 and 4702
  (`0x004BF410`), LairState 170 (`0x0046F4F0`);
- built from this log: ExploreState 28/29 (`0x0045A290`, job 24: up to 20 tries of `random(width)`,
  `random(height)`) and 98/99 (`0x0045A3E0`, state 225: up to 5 tries of `random(5) - 2` per axis), 280 and 50
  draws each (ADR-0674, hash 418); Creature.cpp 1555 (`0x00488F13`, `random(20)` a turn for a Horny creature
  once its keeper's finale bit `player+0x76 & 0x40` is set), 18 draws, one a turn from 3683 (ADR-0675, hash 419);
- built: Creature.cpp 2098, 2105 and 2106 (`Creature_Update`'s flight from the thing at +0x533, from traps and
  the Boulder), 2 and 32 each (ADR-0678, hash 422);
- built: PayState 197 (`0x00472390`, state 187's `random(8)` point, 13 draws, each after its `0x004E4D90` start
  draw, Room.cpp 1563; ADR-0677, hash 421);
- partial: Room.cpp 3994 (`0x004E8430`, 27 draws): 25 inside the wander's run (StateUtils 325, 367, 3994, 338), whose
  room branch and the Prison release and escape now draw with DK2's count (ADR-0676, hash 420); the one at
  turn 1985 is unattributed, and state 217's Graveyard point and the dead body's `+0x134` move are unbuilt.

## Native draw log: Level 5 seed 1, turns 1901-3400

`F:/DK2Evidence/studies/q712-native-drawlog-level5-seed1-fight--2026-11-06--001`, the same draw log on the
`l5live` natural seed over the Heart fight the Q712 outcomes measure (1401-3400; the 2026-11-03 log holds
1400-1900). 28468 draws over 1500 turns, every one attributed to its turn (median 16 a turn), from 89 sites.
All 500 per-turn seeds of 1400-1899 occur in the 2026-11-03 log and the seed after 1900 is its last draw
stepped once, establishing world-seed continuity across the overlap, not call-site parity. The 14 sites none of the Level 3, 4 or 5 (1400-1900)
logs draws, mapped by the file and line pushed at each of the 479 calls of `0x005558D0`
(STRONGLY INFERRED for site attribution; each function record retains its rule-confidence level):

- already mirrored: PrisonState 179 and 185 (`0x004734D8`, `0x0047350C`, state 116's `random(100)` and
  flourish `random(3)`), Room.cpp 1717 (`0x004E5127` in `0x004E50F0`, called here by state 115 `0x00473303`),
  ObjectState 1132, 1171 and 1172 (`0x0049FE60`, the rat's wander) with Player.cpp 1246 (`0x004BA254`, the
  ambient-animal spawn), State.cpp 1838 (`0x00478425`, the unhappy chooser's roll), GuardState 210 and 297
  (`0x00462B57`, `0x00462D8D`: states 97 and 98's deadlines), ResearchState 421 (`0x00476248`, state 60's
  cheer), ShotProcess 1858 (`0x004AFFEE`, a made creature's heading) and PlayerRooms.cpp 1122 (`0x004C6D55`,
  the eligible-room pick);
- unbuilt: ObjectState 2287 (`0x004A1AAA`, 6 draws), object process 28, the spin after a prisoner's
  prop kick. Ours has no Skull or Bone props ([objects.md](objects.md)). The binary's selection,
  velocity addition and spin rules are recorded in [00473350](functions/00473350.md) and
  [004A1A60](functions/004A1A60.md).

The fight census identifies one unbuilt prop consumer; its six world-seed draws can shift later decisions,
so their effect on Q712 remains untested. A turn-by-turn comparison with matched restored inputs is needed
to distinguish that omission from differences in draw order and count inside built rules. The owner
authorized the bounded comparison below on 2026-11-06; a general restore adapter remains outside its scope.

## Bounded restore comparison (2026-11-06)

Owner scope: restore the three Warlocks at completed turn 1400 and explain the next draw divergence;
no campaign sweep or outcome tuning. The scratch build uses production rules at `2fd5f277`, hash/save
428, with the archived diagnostic adapter. Source, executable and provenance are preserved under
`F:/DK2Evidence/builds/q712-warlock-restore-seed1--2026-11-06--001`; inputs, runs, verification script
and `validation.json` are in its same-named `studies` directory. Both manifests were SHA-256 checked.

Only turn-1400 snapshot fields feed the new restore: Warlocks 438/439/441 regain state/resume, heading,
state age, Library targets/holds, waiting positions and steering bodies; 441's reading hold is projected
from its captured animation fields. On completed turns 1401 and 1402, all three match native state,
resume and XY (six checks, CONFIRMED); this does not validate their later action completion times.

The current source has an earlier restore omission than the historical hash-398 control: its effect
generator retains deadline 1403, while native tag 1228 holds 1395. Supplying native `+0x2E` makes all
17 draws of turn 1401 match in ordered sites and pre-draw seeds. The due gate is
[004B1B60](functions/004B1B60.md), STRONGLY INFERRED; the deadline contrast and control are CONFIRMED.

| Control | Draws at 1401 | Draws at 1402 |
|---|---:|---:|
| Native | 17 | 12 |
| Existing adapter, current rules | 9 | 18 |
| Warlock restore | 9 | 13 |
| And captured generator deadline | 17 | 15 |
| And neutral Troll 449's state/action | 17 | 14 |

After the Warlock and generator restore, the first turn-1402 draw is modern Troll 449's idle-body
`random(100)`, in state 5. Native 449 stays in state 87 with action 44 running throughout 1400-1402.
The restore had put it in 1, which dispatches to 5. State 87 returns before any random draw while an
action runs ([004808E0](functions/004808E0.md), STRONGLY INFERRED). Supplying 449's captured state and
action removes that extra draw (CONFIRMED). This is a restore-input difference, not an established rule defect.

That control matches the first three Chicken draws of 1402. Its fourth draw is ObjectState.cpp:2914
where native draws 2851: that probe stopped at the Chicken boundary. The [bounded follow-up](#chicken-draw-4-at-turn-1402-adr-0697) resolves it; later action endpoints and any effect on Heart-defence outcomes remain unestablished.

## Chicken draw 4 at turn 1402 (ADR-0697)

The next bounded probe identifies native Chicken 1414 (modern object 84) at (68703,107090), tile
(16,26), in process 10. Its starting-side draw is the third draw of 1402. Modern takes the last side,
(17,26), and draws the point's X/Y; native rejects it and proceeds to the next bird's side draw.
Its captured position is unchanged; process 10 clears to 0 after no side is accepted (CONFIRMED).

Native object 1221 is an Arch Pillar (94) on that side, at (72089,106906), already present at 1400.
Objects.kwd gives it Obstacle. `Tile_FirstObstacle` tests that flag on tile objects, and process 10
rejects the side before its point draws ([00453600](functions/00453600.md),
[004A26D0](functions/004A26D0.md), STRONGLY INFERRED). The remake's pillars are virtual combat
targets; its object-list scan saw none. Supplying that one captured object, with the other probe
inputs fixed, removes the two extra draws and matches both turns' ordered sites and seeds (CONFIRMED).

ADR-0697/hash-save 429 adds the existing virtual-pillar eligibility and definition flag to the loose
Chicken's side check. It creates no objects. Regression cases cover all three pillars, a non-pillar tile,
a cleared Obstacle flag, a ruined Heart and zero Heart health: the three live-pillar cases fail before
the fix (two extra draws), with all four controls passing; all seven pass afterwards. The Simulation
suite passes 4823 tests. The new determinism pin is in [hash-versions.md](hash-versions.md).

| Two-turn probe | 1401 draws | 1402 draws |
|---|---:|---:|
| Native | 17 | 12 |
| Hash 428, prior restored inputs | 17 | 14 |
| Hash 428, one captured pillar added | 17 | 12 |
| Hash 429, prior restored inputs | 17 | 13 |
| Hash 429, also restoring Troll 447's captured state/action | 17 | 12 |

The fixed rule changes the replay's pre-1400 history: Troll 447 now reaches the restore in a different
state. Native already holds state 87/action 43 at 1400; supplying that snapshot removes its extra
idle-body draw, as the earlier 449 control did. With that declared input, the fixed replay matches all
29 ordered draws over 1401-1402. Neither full-state parity nor any Heart-defence outcome is established.
This fix's validation ends at 1402; the [next probe](#egg-collision-at-turn-1403) extends the diagnostic comparison.

Evidence and replay source: `F:/DK2Evidence/studies/q712-chicken-draw1402--2026-11-06--001` and its
same-named `builds` directory. Native inputs use completed turn 1400 only; projected action endpoints
remain outside the validation claim.

## Egg collision at turn 1403

The unchanged hash-429 matched-input replay retains all 29 ordered matches at 1401-1402, then draws
9 numbers against native's 10 at 1403. Native's first is Object.cpp:1330, `random(2)` in
[0049CD00](functions/0049CD00.md): an Egg's blocking-collision callback, before reflection in
[004B2030](functions/004B2030.md). Zero requests effect301 and marks the Egg dead (STRONGLY INFERRED).
At hash429, `EggLaunchRules.Move` has no such draw or break arm.

Native Egg1421 already exists at completed1400 in process24/age37, position (47440,98616,4096),
persistent velocity (-2705,-1877,0). It reaches (47113,98390,4096) at1402 and is absent at1403
(CONFIRMED). Its next projected step enters wall tile (11,23) (STRONGLY INFERRED). The existing restore restores
Chickens, not this Egg. Supplying only its completed1400 snapshot matches its position, velocity,
process and age through1402, but leaves the 1403 draw missing.

Adding the binary's collision draw in the diagnostic build gives10 draws, but the Egg draws seventh,
after six Chicken draws. It receives1 and survives. Hash429's `ObjectMovementSystem` runs the
Chickens before `HatcherySystem` runs Eggs. DK2 instead completes each object's update in list order
([004B7830](functions/004B7830.md), STRONGLY INFERRED). Interleaving the diagnostic state24 Egg step
with the Chicken/Rat walk makes this Egg draw first, receive0 and disappear as native does (CONFIRMED).

| Bounded diagnostic control | 1401 | 1402 | 1403 | Ordered sites/seeds at1403 |
|---|---:|---:|---:|---|
| Unchanged rules, prior restore | 17 | 12 | 9 | Missing Egg draw |
| And captured Egg1421 | 17 | 12 | 9 | Missing Egg draw |
| And collision check | 17 | 12 | 10 | Egg draw seventh |
| And state24 object interleaving | 17 | 12 | 10 | All10 match |

The final control matches all39 ordered world draws over1401-1403. This is diagnostic evidence,
not a production fix, full-state parity or a Heart-outcome result. Effect301 playback and later turns
are untested in that control. The restored Egg is appended as newest; the full native mixed-object order
is not restored. Restoring only the draw count changes which Egg survives. No general restore adapter is built.

Evidence, controls, validator and diagnostic source/build: `F:/DK2Evidence/studies/q712-draw1403--2026-11-06--001`
and its same-named `builds` directory. Only the corrected `egg1421-input-v2.json` is used. The earlier
ungated `collision-control` changes pre1400 history and is excluded; bounded controls enable the new
rules only after the completed1400 restore. No production source or hash/save version changes.

### Built at431 (ADR-0699)

`EggLaunchRules.Move` now draws before reflection on a blocked candidate point. Zero requests301
at the Egg's pre-commit position, owner and heading, clears velocity/health, and removes it; one
retains the bounce. A clear diagonal candidate is not blocked by an unused axis projection, matching
the candidate-first physical test in [004B2030](functions/004B2030.md) (STRONGLY INFERRED).
`ObjectMovementSystem` interleaves Egg/Coop processes with Chicken/Rat handlers; the default
Hatchery pass does not repeat them. Standalone Hatchery hosts retain their own object updates.

The branch-only production probe uses these six changed simulation files byte-for-byte, with the existing
diagnostic host supplying the captured inputs. It matches all39 consecutive native pre-draw seeds
and ordered sites over1401-1403, the Egg's pose/velocity/process through1402, and its removal at1403.
It requests301 at1403, owner3, height4096. The corrected snapshot includes captured heading word403.
There is no diagnostic collision or ordering switch in this control (CONFIRMED comparison).

The new regressions cover break/bounce, effect payload, held/carried Eggs, clear motion and diagonals,
mixed Egg/Chicken order in both directions, newborns, Coop aging and state24-to32 single stepping.
The initial nine-case test revision fails five cases before the fix; adding the diagonal case fails
that case before the candidate-test correction. Source hashes and reconstructed unchanged tests
bind those results to the saved traces. All twelve final cases pass. The integrated solution has
8427 passed and 30 skipped across ten suites. The Release build has no warnings or errors;
Unity compiles with zero errors and two existing obsolete-API warnings. Both affected scenario CLI
runs pass. Fixed-seed scenario counts and incidental bird IDs are remake
regression pins; those affected by the new draws are updated without changing meal or laying clocks.

Native door/voxel/liquid collision volumes, deferred unlinking, the full captured mixed-object list,
effect playback and Heart outcomes remain outside this validation. The engine unlinks the broken Egg
immediately, whereas native marks it for the later removal sweep. This 431 validation ends at 1403;
the [early continuation](q712-early-draw-continuation.md) follows the next boundaries through 1412.
Hash/save431 adds no fields; the new pin is in [hash-versions.md](hash-versions.md).
Original regression evidence/source/build: `F:/DK2Evidence/{studies,builds}/q712-egg-collision-h431--2026-11-06--001`.
The composition with master `b4cc4e84` retains the same 39 ordered native seeds/sites at 1401-1403
in a new production probe; this is the same bounded prefix, not a new native or outcome measurement.
Combined evidence/source/build: `F:/DK2Evidence/{studies,builds}/q712-egg-collision-integration--2026-11-06--001`;
39 study files and 53 build files are SHA-256 verified, and its archived validator passes.

## Outcomes at hash 399

Measured 2026-11-04: 115 bounded stored-pickup resyncs (75 Level 5, 40 Level 4) at hash/save 399
(ADR-0650, on top of ADR-0646 to 0649), same scorer and cohorts as
[hash 392](steering-body-retention.md#outcomes-at-hash-392). Means per run:

| Measure | DK2 | 392 | 399 |
|---|---:|---:|---:|
| L5 state-254 entries | 15.00 | 15.33 | 13.27 |
| Exits to 199 | 3.67 | 1.00 | 0.80 |
| Exits to 203 | 5.00 | 2.27 | 2.07 |
| Exits to 1 | 3.00 | 4.40 | 5.40 |
| Exits to 6 | 2.00 | 6.87 | 4.47 |
| Exits to 8 | 1.00 | 0.67 | 0.53 |
| Hero ticks near Heart, targeting pillar | 3298.67 | 1679.53 | 1618.33 |
| Keeper Imp deaths, L5 resync 1400 | 11.12 | 5.90 | 7.27 |
| Keeper Imp deaths, L5 resync 3400 | 5.80 | 5.80 | 9.32 |
| Keeper Imp deaths, L4, all 40 | 6.50 | 9.93 | 8.00 |

The outcome averages neither confirm nor refute the rule fixes at this sample size: the sign-flip p of
399 against DK2 is 0.20 (1400), 0.19 (3400) and 0.25 (L4); the per-state paired change from 392 to 399 has
p 0.43, 0.06 and 0.19. The Heart still sees the pillar targeted about half as long as native. Q712 is
therefore no longer steered by these averages: new rules need a binary reading or a capture (a draw-log
comparison on other levels, which needs native captures; a held bird's decay, read afterwards as none and already ours, Q584; `Hatchery_AdoptChickens`
`0x004DEB70` was built afterwards, ADR-0652), and the scratch restore adapter stopped for that measurement. The [bounded 6 Nov comparison](#bounded-restore-comparison-2026-11-06) is separate.

Evidence: `F:/DK2Evidence/builds/q712-final399-measurement-h399--2026-11-04--001/` and the studies
`heart-defence-q712-h399-l5--2026-11-04--001/` and its `l4` sibling.
