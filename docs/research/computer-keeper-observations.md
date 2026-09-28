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
state 3. In the 1,200-tick modern run, the keeper emitted 32 tag commands,
25 room-build commands, and seven slaps. Imp work dug marked rock, and the
Prison first built at tick 524; 25 tiles were built by tick 616. Portal
recruitment occurred independently of the keeper command stream. These are
CONFIRMED modern-run facts, not an original-game comparison. The scenario
has no scripted player commands and has regression expectations for digging,
the Prison build, and portal recruitment.
