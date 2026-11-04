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

The consumers found from the log are all accounted for. Two ADR-0649 fixes (hash/save 398) came from
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
to those states: the owner decided on 2026-11-04 to keep building rules backed by the binary or a capture and
to stop steering Q712 by outcome averages (see [Outcomes at hash 399](#outcomes-at-hash-399)). Q738
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
- still missing: Hatchery.cpp:187 (`0x004DE220`, a coop's placement `random(4)` per coop, which needs the coops
  rebuilt on a room change, Q733) and ManufactureState.cpp:469 (`0x00470B20`, state 68 on a crate made,
  `random(2)`, its gate flag unread, no function record).

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
`0x004DEB70` was built afterwards, ADR-0652), and the scratch restore adapter stops here.

Evidence: `F:/DK2Evidence/builds/q712-final399-measurement-h399--2026-11-04--001/` and the studies
`heart-defence-q712-h399-l5--2026-11-04--001/` and its `l4` sibling.
