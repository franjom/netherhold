# Packet replay after withdrawing portal crystal collision, hash 303

One sweep of Levels 1–4 over simulation seeds 1–20, compared with
[hash 302](../2026-10-24-packet-replay-hash302/README.md). These are our simulation's
outcomes with fixed recorded inputs, not a new measurement of DKII.EXE behavior.

## Committed code and tool

Measured commit: `e96639742fa12c87eeb1407cdc13418f1b7931fe`,
`restore movement through portal crystals`. HashVersion and FormatVersion are
**303**. The gameplay change from the hash-302 baseline is the withdrawal of
ADR-0527's crystal footprints, swept guards, target projection and predictive
contacts. Portal art, portal-hole navigation and Heart protection remain.

The source was built once in Release from a clean detached checkout. The main
checkout retained the owner's unrelated configuration changes. The executable
and dependencies were copied out before replay; all 21 source/copy SHA-256s
match its archive manifest. No full test suite, profiler or repeat sweep ran.
The earlier rollback verification covered 174 targeted tests.

| Item | Absolute path |
|---|---|
| Clean source checkout, retained | `D:\DK2Evidence\checkouts\packet-replay-hash303-2026-10-25` |
| Archived executable actually run | `D:\DK2Evidence\tools\hash303-e9663974-2026-10-25\DK2.Tools.exe` |
| Actual rig | `D:\DK2Evidence\rig-2026-10-25-hash303` |
| Outputs | `D:\DK2Evidence\runs\packet-replay-hash303` |
| Baseline records copied into rig | `baseline-hash302-results.json` |
| Original hash-302 combined records | `D:\DK2Evidence\runs\packet-replay-hash302\recaptured-level2-level3\combined-results.json` |
| Native maps | `D:\DK2Evidence\dk2runs\{level2-live2,level3-live1,level4-live1}\seed01\{creature-map.csv,object-map.csv}` |
| Packets | `E:\GogGames\Dungeon Keeper 2\PacketRecord\{l1live,l2live2,l3live,l4live}.sav` |

No evidence was removed or overwritten. The rig's `manifest.csv` verifies seven
preserved files: original driver, fixed builder, placed-object map, packet reader,
static Level 1 map, hash-302 driver reference and baseline results. The baseline
copy also matches its sealed hash-302 artifact manifest. Preflight verified the
same native CSVs, packets and static map used by hash 302. The fixed builder
`lmap.py` is byte-identical to that run's copy.

## Commands and validity

Clean checkout and build:

```powershell
git -c safe.directory=D:/DungeonKeeperRemake worktree add --detach D:/DK2Evidence/checkouts/packet-replay-hash303-2026-10-25 e96639742fa12c87eeb1407cdc13418f1b7931fe
Set-Location D:/DK2Evidence/checkouts/packet-replay-hash303-2026-10-25
dotnet build src/DK2.Tools/DK2.Tools.csproj -c Release --no-incremental -p:NuGetAudit=false -p:BuildInParallel=false
```

The process used `DOTNET_PROCESSOR_COUNT=2`. The build completed with no warnings
or errors; `build.log` records it. After archiving the tool, run the preserved
driver from its new rig:

```powershell
python D:/DK2Evidence/rig-2026-10-25-hash303/measure303.py
```

`measure303.py` uses at most two workers. Every seed stores `command.json`
(exact argv, working directory and relevant environment), `replay.csv`,
`events.tsv`, `ours-creatures.csv`, `entity-map.csv` and `run.log`. Top-level
`provenance.json` pins commit, tool, hash/save version and build command;
`input-manifest.csv` pins 16 inputs. `sweep.log`, add-only `progress-NN.json`
and final `results.json` record completion.

| Level | Packet | Offset | Turns | Entity map |
|---|---|---:|---:|---|
| 1 | `l1live.sav` | 69 | 2044 | Static `rig\l1m\map.csv` |
| 2 | `l2live2.sav` | 69 | 3657 | Rebuilt per seed, native `level2-live2\seed01`, width 40 |
| 3 | `l3live.sav` | 69 | 3704 | Rebuilt per seed, native `level3-live1\seed01`, width 50 |
| 4 | `l4live.sav` | 69 | 6804 | Rebuilt per seed, native `level4-live1\seed01`, width 50, `ours-objects.csv` |

Every replay passes `--seed N`, `--packet`, `--packet-offset 69`, `--turns`,
`--entity-map`, `--events` and `--creatures`. The fixed builder receives
`L2TOOL`, `L2OUT`, `L2EVENTS`, `L2EXTRA=--seed N`, `L2LEVEL`, `L2WIDTH`, `L2LAST`,
`L2RUN` and `L2PACKET`; only Level 4 receives `L2OBJECTS`. Builder remapping
passes replace working traces only inside the newly created seed directory.

Validity matches the owner-authorized hash-302 criteria: successful full
execution, exactly consecutive turns 1 through the limit, no placed-type
mismatch, and a stable final map for Levels 2–4. Placeholder creature IDs are
disclosed rather than rejected. Commands targeting them can miss; “valid” does
not establish that every recorded entity target had a real counterpart.

## Results

All 80 runs are valid under the criteria above. Win-tick statistics use wins
only, in min / median / max order.

| Level | Hash 303 valid / 20 | Hash 303 wins / 20 | Hash 303 win ticks | Hash 302 valid / 20 | Hash 302 wins / 20 | Hash 302 win ticks |
|---|---:|---:|---|---:|---:|---|
| 1 | 20/20 | 18/20 | 1776 / 1792 / 1900 | 20/20 | 18/20 | 1776 / 1788 / 1900 |
| 2 | 20/20 | 7/20 | 3128 / 3536 / 3628 | 20/20 | 4/20 | 3028 / 3374 / 3648 |
| 3 | 20/20 | 18/20 | 3540 / 3584 / 3652 | 20/20 | 0/20 | — |
| 4 | 20/20 | 3/20 | 6736 / 6752 / 6752 | 20/20 | 0/20 | — |

### Which seeds changed outcome

| Level | No win → win | Win → no win | Winning tick changed in seeds that win in both sets |
|---|---|---|---|
| 1 | — | — | 2, 5, 9, 11, 16, 18 |
| 2 | 2, 11, 16 | — | 1, 6, 8, 20 |
| 3 | 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 | — | — |
| 4 | 6, 16, 19 | — | — |

Level 1 keeps its win count, but its timing and traces are not wholly unchanged.
The saved trigger and event traces differ in the following numbers of seeds;
`comparison.json` contains the exact seed lists.

| Level | Changed `replay.csv` / 20 | Changed `events.tsv` / 20 |
|---|---:|---:|
| 1 | 8/20 | 10/20 |
| 2 | 20/20 | 20/20 |
| 3 | 20/20 | 20/20 |
| 4 | 20/20 | 20/20 |

### Placeholder map rows

These are counted from the final entity maps, not inferred from the outcome.
Levels 2/3 have ranges 5–23 / 9–13; Level 4's range is 2–13.

| Seed | Level 2 | Level 3 | Level 4 |
|---|---:|---:|---:|
| 1 | 5 | 12 | 6 |
| 2 | 5 | 13 | 2 |
| 3 | 5 | 12 | 10 |
| 4 | 23 | 10 | 13 |
| 5 | 23 | 10 | 11 |
| 6 | 5 | 10 | 11 |
| 7 | 23 | 12 | 8 |
| 8 | 7 | 10 | 4 |
| 9 | 6 | 11 | 10 |
| 10 | 12 | 12 | 6 |
| 11 | 6 | 9 | 11 |
| 12 | 11 | 11 | 6 |
| 13 | 23 | 11 | 4 |
| 14 | 23 | 11 | 8 |
| 15 | 23 | 10 | 3 |
| 16 | 5 | 9 | 8 |
| 17 | 23 | 10 | 8 |
| 18 | 22 | 11 | 8 |
| 19 | 22 | 10 | 7 |
| 20 | 6 | 10 | 10 |

### Level 4 Imp deaths

Count keeper-owned `creature_died` events with `type=Imp player=3` over all
6804 turns, using the same method as hash 302. Native comparison and its
Imp-to-object counting rule remain in
[the hash-302 report](../2026-10-24-packet-replay-hash302/README.md#level-4-imp-deaths).

| Seed | Hash 303 Imp deaths | Hash 302 Imp deaths | Hash 303 win tick |
|---|---:|---:|---:|
| 1 | 19 | 28 | — |
| 2 | 36 | 0 | — |
| 3 | 0 | 0 | — |
| 4 | 1 | 7 | — |
| 5 | 6 | 19 | — |
| 6 | 6 | 4 | 6752 |
| 7 | 8 | 36 | — |
| 8 | 28 | 8 | — |
| 9 | 4 | 10 | — |
| 10 | 58 | 9 | — |
| 11 | 17 | 8 | — |
| 12 | 22 | 0 | — |
| 13 | 22 | 12 | — |
| 14 | 19 | 5 | — |
| 15 | 38 | 36 | — |
| 16 | 3 | 8 | 6736 |
| 17 | 1 | 6 | — |
| 18 | 7 | 6 | — |
| 19 | 0 | 15 | 6752 |
| 20 | 9 | 25 | — |
| Median | **8.5** | **8** | — |

The rollback increases Level 4 wins while its median Imp deaths rise slightly.
The win-rate improvement is not a uniform improvement across measured metrics.

### Level 3 progression

The first opening means a `destroyed` / `MineGold` event at any of `(25,18)`,
`(26,18)`, `(27,18)`, as in the earlier saved-trace diagnosis.

| Measure, 20 seeds | Hash 303 | Hash 302 |
|---|---:|---:|
| Gold passage opened by turn 3361 | 19/20 | 1/20 |
| Gold passage opened by turn 3704 | 19/20 | 8/20 |
| Lord ends at full 6000 health | 1/20 | 18/20 |

With the same rig and verified recording inputs, withdrawing the crystal-collision
feature restores timely passage opening in most Level 3 seeds and changes its
win count from 0 to 18. This implicates the withdrawn feature in this replay
regression. It does not isolate which guard/contact effect or subsequent RNG
trajectory caused each delay, and it is not a new claim about native collision.
The restored steering's two observed near-hole route limitations remain in
[the rollback note](../../research/portal-crystal-collision.md#verification-and-limits).

## Verification

The read-only verifier recomputes winning ticks, changed outcomes, placeholder
counts, Level 4 Imp deaths and Level 3 progression from saved files:

```powershell
python D:/DK2Evidence/rig-2026-10-25-hash303/verify303.py --seal
```

The completed verification checked all 80 turn sequences, map stability,
placeholder counts, win summaries, Imp deaths and pinned inputs. With `--seal`,
it created new `comparison.json` and `artifact-manifest.csv`, sealing **598 files**.
Omit the flag for subsequent read-only checks. The source checkout is retained;
its removal requires the owner's separate cleanup approval.
