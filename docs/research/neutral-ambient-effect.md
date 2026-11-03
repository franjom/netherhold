# The neutral player's ambient tile (0x004BB130)

Binary readings of DKII.EXE v1.70, 2026-11-03. The decision is ADR-0646 (hash/save 395).
Function-level facts are in the records [004BB130](functions/004BB130.md) and
[004B9EC0](functions/004B9EC0.md).

## Rule (CONFIRMED in the disassembly, 0x004BB130..0x004BB293)

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
about 4 in ours. The neutral frame (ADR-0646) is the first missing consumer built, and the loose
chicken, object process 10 (`0x004A26D0`; ObjectState.cpp lines 2851, 2914, 2915; 2421 and 46
draws over the 500 turns), the second (ADR-0647, hash/save 396). Whether ours has as many loose
birds as DK2's Level 5 at turn 1400 is not yet checked. Still missing, in the order they were found:

1. `EffectGenerator_Update` (`0x004B1B60`; lines 173, 179, 180, 188).
2. Object states 2523, 2558 and 2559, whose draw sites are not yet mapped.

Q738 (`0x004B9740`'s periodic draws) is separate.
