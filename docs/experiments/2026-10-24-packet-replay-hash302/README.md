# Packet replay remeasurement, hash 302 (2026-10-24)

This measures our simulation with the owner's recorded inputs, against recorded
native outcomes. It makes no new claim about DKII.EXE. Simulation seeds 1–20
sample our RNG; they are not paired native RNG streams. A win is the replay's
`player 3 status=2`, within the recording's turn limit.

## Build and preserved inputs

The main checkout was observed clean on `master` at
`108e187e11a0a62a9569441438ae80cd669b99bc` before the Release build, with
HashVersion **302** (`src/DK2.Simulation/GameSimulation.cs`). One build succeeded
with zero warnings and errors. No simulation changes, test suite or profiler
were run. Two replay workers were used.

The set's `build-provenance.json` records
`3042e50bcc184833564e8919ec6bc3f741d8fe73`, the HEAD when that metadata was
written, plus the build command and SHA-256s of 18 tool, packet and native-map
files. Its only difference from the initially checked commit is `.gitignore`;
both commits contain identical simulation and tool sources. The metadata is
unchanged from its sealed artifact hash. The same pinned binaries are reused
for the Level 2/3 follow-up; no second build or Level 1/4 sweep is needed.

All paths below are absolute; no replay outputs are stored in the checkout.

| Item | Path |
|---|---|
| Checkout | `D:\DungeonKeeperRemake` |
| Original build path (mutable) | `D:\DungeonKeeperRemake\src\DK2.Tools\bin\Release\net10.0\DK2.Tools.exe` |
| Archived hash-302 tool | `D:\DK2Evidence\tools\hash302-before-portal-crystal-rollback-2026-10-25\DK2.Tools.exe` |
| Preserved rig | `D:\DK2Evidence\rig-2026-10-24` |
| Initial set | `D:\DK2Evidence\runs\packet-replay-hash302` |
| Level 2/3 follow-up | `D:\DK2Evidence\runs\packet-replay-hash302\recaptured-level2-level3` |
| Current combined results | Follow-up directory's `combined-results.json` (all 80 seeds) |
| Native Level 4 map inputs | `D:\DK2Evidence\dk2runs\level4-live1\seed01\{creature-map.csv,object-map.csv}` |
| Native second comparator | `D:\DK2Evidence\dk2runs\level4-live1-seed2\seed02\creature-map.csv` |
| Packets | `E:\GogGames\Dungeon Keeper 2\PacketRecord\{l1live,l2live2,l3live,l4live}.sav` |

The preserved rig contains the original `runset4.py`, all of `sets\P2`,
`l1m\map.csv`, `l2map-any.py`, `l3map.py`, and the recovered `lmap.py`,
`ours-objects.csv` and `pktdump.py`. `manifest.csv` records source path, copy path
and SHA-256 for **368 files**; all source/copy hashes were verified after copying.
Scratchpad originals and prior evidence were not changed.

Sources for that preservation:

- Driver and P2: `C:\Users\franj\AppData\Local\Temp\claude\d--DungeonKeeperRemake\ec762c7a-940e-4ac0-b16e-928afe1df4fc\scratchpad`.
- Static map and old builders: `C:\Users\franj\AppData\Local\Temp\claude\d--DungeonKeeperRemake\d869276a-99ca-491a-9cf5-0342488c1277\scratchpad`.
- Fixed builder, objects and packet reader: `D:\DK2Evidence\recovered-rig-2026-10-19\reverse-engineering\dk2runs\level4-live1\rig`.

The initial driver is `rig-2026-10-24\measure302.py`; its parameter source,
`runset4.py`, remains unchanged. `execution-manifest.csv` pins that driver and
the unedited fixed builder. The initial Level 2/3 P2-map attempts were invalid;
their logs and results remain in the initial set, excluded from current rates.

With native inputs restored, `measure302-recaptured23.py` runs only Levels 2
and 3, rebuilding each seed's map with the same fixed `lmap.py`. It reuses the
verified binaries and writes exclusively into the new follow-up directory.
Its `results.json` contains 40 fresh runs; `combined-results.json` combines
these with the unchanged Level 1/4 results from the initial set. Earlier evidence
is preserved. Builder remapping passes replace working traces only inside each
newly created seed directory; the stored outputs are the final pass.

Before the hash-303 portal-collision rollback, all 21 Release-tool files were
archived at the path above and source/copy SHA-256s verified in its `manifest.csv`.
For historical tool verification, resolve the original tool-directory paths in
`build-provenance.json` to that archive; packet/native input paths are unchanged.
The commands below record the original invocations. These results do not measure
the hash-303 rollback.

## Commands and validity

From `D:\DungeonKeeperRemake`, build once:

```powershell
git -c safe.directory=D:/DungeonKeeperRemake status --short
git -c safe.directory=D:/DungeonKeeperRemake rev-parse HEAD
dotnet build src/DK2.Tools/DK2.Tools.csproj -c Release --no-incremental -p:NuGetAudit=false
```

Build output is in the set's `build.log`. The per-command Git trust exception
handles the managed worker's different Windows SID. The audit property applies
only to this build and avoids the sandbox's unavailable NuGet audit endpoint.

Initial invocation, with stdout/stderr captured in a newly created `sweep.log`:

```powershell
python D:/DK2Evidence/rig-2026-10-24/measure302.py D:/DungeonKeeperRemake D:/DK2Evidence/runs/packet-replay-hash302 fixed 1,2,3,4 1-20
```

After the owner restored native inputs, run only the missing levels; stdout and
stderr are captured in the initial set's new `recaptured23-sweep.log`:

```powershell
python D:/DK2Evidence/rig-2026-10-24/measure302-recaptured23.py
```

Each attempted seed stores `command.json` (exact argv, working directory and
replay environment), `replay.csv`, `events.tsv`, `ours-creatures.csv`, `run.log`
and `entity-map.csv`. The set has `results.json` and add-only `progress-NN.json`
snapshots. `MINE`/`OLD` resolve to the preserved rig; `RUNS` resolves to
`D:\DK2Evidence\dk2runs`; `OUT` is the set above.

Direct replay command, with the substitutions in the table:

```powershell
$env:DK2_DIR = 'E:/GogGames/Dungeon Keeper 2'
& D:/DungeonKeeperRemake/src/DK2.Tools/bin/Release/net10.0/DK2.Tools.exe replay levelN - DEST/replay.csv --packet 'E:/GogGames/Dungeon Keeper 2/PacketRecord/PACKET' --packet-offset 69 --turns TURNS --entity-map DEST/entity-map.csv --events DEST/events.tsv --creatures DEST/ours-creatures.csv --seed SEED
```

| Level | PACKET | TURNS | Entity map source |
|---|---|---:|---|
| 1 | `l1live.sav` | 2044 | Preserved `l1m\map.csv`, copied into each seed directory |
| 2 | `l2live2.sav` | 3657 | Fixed builder, new native `level2-live2\seed01` maps |
| 3 | `l3live.sav` | 3704 | Fixed builder, new native `level3-live1\seed01` maps |
| 4 | `l4live.sav` | 6804 | Rebuilt by the fixed builder for each seed |

Levels 2–4 use the fixed builder. Level 4's environment is shown below;
the follow-up substitutes `L2LEVEL=level2/level3`, `L2WIDTH=40/50`, the turn
limits and packets above, the corresponding native directory, and its fresh
output directory. `L2OBJECTS` is omitted for Levels 2 and 3. Exact environments
are saved in every seed's `command.json`.

Level 4 uses `DEST=D:/DK2Evidence/runs/packet-replay-hash302/level4/seedNN`:

```powershell
$env:DK2_DIR = 'E:/GogGames/Dungeon Keeper 2'
$env:L2TOOL = 'D:/DungeonKeeperRemake/src/DK2.Tools/bin/Release/net10.0/DK2.Tools.exe'
$env:L2OUT = 'DEST'
$env:L2EVENTS = 'DEST/events.tsv'
$env:L2EXTRA = '--seed SEED'
$env:L2LEVEL = 'level4'
$env:L2WIDTH = '50'
$env:L2LAST = '6804'
$env:L2RUN = 'D:/DK2Evidence/dk2runs/level4-live1/seed01'
$env:L2PACKET = 'E:/GogGames/Dungeon Keeper 2/PacketRecord/l4live.sav'
$env:L2OBJECTS = 'D:/DK2Evidence/rig-2026-10-24/ours-objects.csv'
python D:/DK2Evidence/rig-2026-10-24/lmap.py
```

The builder invokes the direct replay command while resolving its map;
partial mapping passes are not independent measurements. A valid final replay
must exit zero, write exactly turns 1 through TURNS and have no placed-type
mismatch. Levels 2–4 must also report `full replay ran, map stable`.

For the fresh Level 2/3 sweep the owner authorized Level 4's historical criteria,
superseding the initial zero-placeholder requirement for reused P2 maps.
Placeholders are therefore disclosed rather than rejected on all three rebuilt
levels. Commands targeting those IDs can miss: validity establishes complete
execution and stable mapping, not representation of every recorded entity target.
The initial invalid P2-map attempts are not counted as losses.

## Results

Tick statistics include wins only, in min / median / max order. Current data
come from the follow-up's `combined-results.json`; P2 columns come from the
preserved `sets\P2\results.json`, including its historical validity flags.
P2's exact build commit is not established.

| Level | Hash 302 valid / 20 | Hash 302 wins / 20 | Hash 302 win ticks | P2 valid / 20 | P2 wins / 20 | P2 win ticks |
|---|---:|---:|---|---:|---:|---|
| 1 | 20/20 | 18/20 | 1776 / 1788 / 1900 | 20/20 | 20/20 | 1768 / 1808 / 1832 |
| 2 | 20/20 | 4/20 | 3028 / 3374 / 3648 | 20/20 | 6/20 | 3136 / 3546 / 3648 |
| 3 | 20/20 | 0/20 | — | 20/20 | 14/20 | 3556 / 3588 / 3644 |
| 4 | 20/20 | 0/20 | — | 20/20 | 2/20 | 6732 / 6740 / 6748 |

Changed win outcomes against P2, within each recording's turn limit:

| Level | No win → win | Win → no win |
|---|---|---|
| 1 | — | 3, 7 |
| 2 | 1, 8 | 9, 12, 14, 15 |
| 3 | — | 1, 2, 3, 4, 6, 7, 8, 10, 11, 13, 14, 15, 19, 20 |
| 4 | — | 3, 19 |

This measures accumulated changes through hash 302; it does not attribute an
outcome change to a particular intervening commit. Level 1/4 results are reused;
no second sweep of those levels was run.

### Level 3 progression diagnosis

Read-only comparison of saved traces: P2 destroys a gold wall at one of
`(25,18)`, `(26,18)`, `(27,18)` in all 20 seeds by turn 3361. Hash 302 does so
in only one seed by then; seven open later and twelve never open by 3704.
The Lord ends at full 6000 health in 18 current seeds, versus none in P2.
In seed 1, P2 opens at 3288 and kills him at 3556; current never mines that
seam and leaves him at `(26,5)` with his original action-point job.

This identifies delayed progression before the Lord's fight as a concrete
failure pattern. Why mining falls behind, and which intervening change causes
it, remain unresolved; no causal bisection or additional replay was run.
The next check is the work selection and movement leading to this seam.
Evidence: initial set's `level3-progression-diagnosis/{findings.json,input-manifest.csv}`;
reader: preserved rig's `diagnose-level3-hash302.py`. These are observations of
our simulation, not new claims about DKII.EXE.

### Level 2/3 map placeholders

All 40 fresh runs completed with stable maps and no placed-type mismatch.
Placeholder counts are 5–23 for Level 2 and 9–13 for Level 3. A dash in the win
tick column means no win within the recording, under the agreed validity rule.

| Simulation seed | Level 2 win tick | Level 2 placeholders | Level 3 placeholders |
|---|---:|---:|---:|
| 1 | 3420 | 5 | 12 |
| 2 | — | 5 | 11 |
| 3 | — | 5 | 11 |
| 4 | — | 23 | 10 |
| 5 | — | 23 | 9 |
| 6 | 3328 | 5 | 11 |
| 7 | — | 23 | 10 |
| 8 | 3648 | 7 | 11 |
| 9 | — | 5 | 11 |
| 10 | — | 23 | 10 |
| 11 | — | 11 | 9 |
| 12 | — | 23 | 12 |
| 13 | — | 23 | 11 |
| 14 | — | 23 | 11 |
| 15 | — | 23 | 10 |
| 16 | — | 5 | 11 |
| 17 | — | 23 | 13 |
| 18 | — | 22 | 10 |
| 19 | — | 22 | 12 |
| 20 | 3028 | 5 | 12 |

### Level 4 Imp deaths

Our count is keeper-owned `creature_died` events with `type=Imp player=3` over
turns 1–6804. P2 counts below are recomputed from its preserved event logs.
The recorded native counts are **0 / 19** for seeds 1 / 2: counting transitions
from keeper Imp to object reproduces both, including repeated deaths in reused
slots. Requiring an observed zero-health creature would miss these transitions;
see [the existing native outcome comparison](../../research/imp-fidelity-integration.md#level-4-outcomes-2026-10-19).

| Simulation seed | Hash 302 Imp deaths | P2 Imp deaths | Hash 302 placeholder map rows |
|---|---:|---:|---:|
| 1 | 28 | 1 | 14 |
| 2 | 0 | 5 | 13 |
| 3 | 0 | 4 | 12 |
| 4 | 7 | 6 | 15 |
| 5 | 19 | 37 | 10 |
| 6 | 4 | 2 | 11 |
| 7 | 36 | 39 | 5 |
| 8 | 8 | 39 | 14 |
| 9 | 10 | 6 | 11 |
| 10 | 9 | 4 | 11 |
| 11 | 8 | 7 | 16 |
| 12 | 0 | 16 | 8 |
| 13 | 12 | 16 | 12 |
| 14 | 5 | 35 | 8 |
| 15 | 36 | 38 | 7 |
| 16 | 8 | 6 | 10 |
| 17 | 6 | 41 | 11 |
| 18 | 6 | 29 | 18 |
| 19 | 15 | 4 | 10 |
| 20 | 25 | 2 | 16 |
| Median | **8** | **6.5** | — |

Every Level 4 map stabilized, with 5–18 placeholder creature rows and no
placed-type mismatch. Those missing counterparts limit what the win comparison
can establish, even though every final trace contains all 6804 turns.

Final follow-up verification and add-only manifest creation:

```powershell
python D:/DK2Evidence/rig-2026-10-24/verify302-recaptured23.py --seal
```

The verifier checks all 40 fresh traces, required files, map stability,
placeholder counts, win summaries and input/tool hashes. It checks that the
80-row combined set reuses the original Level 1/4 records exactly. The follow-up's
`artifact-manifest.csv` seals 288 files. Omit `--seal` to recompute statistics
without attempting to create an existing manifest.

The initial `analyze302.py` / `seal302.py` checks and initial set's 309-file
`artifact-manifest.csv` remain unchanged; they cover the earlier 42 attempts,
including the two invalid P2-map trials. The new follow-up replaces only those
Level 2/3 measurements in the current combined set.

The built tool's `docs` gate reports no budget violations in changed files;
`kb check` reports 2307 records with no findings. `git diff --check` passes.

## Restored Level 2 and 3 native inputs

The owner re-recorded both seed-1 packet playbacks on 2026-10-24. The fixed
builder now reads these native creature/object maps directly; no P2 entity map
is reused in the follow-up.

| Level | Native input directory | Reported frames | Torn / missed frames |
|---|---|---:|---:|
| 2 | `D:\DK2Evidence\dk2runs\level2-live2\seed01` | 3657, ticks 1–3657 | 0 / 0 |
| 3 | `D:\DK2Evidence\dk2runs\level3-live1\seed01` | 3704, ticks 1–3704 | 0 / 0 |

Each directory contains the capture, `creature-map.csv`, `object-map.csv`,
`capture-info.txt`, `seed.out` and a SHA-256 `manifest.csv`. All 12 entries in
each native manifest were verified before replay. These inputs remain unchanged.

The follow-up's `input-manifest.csv` pins the driver, fixed builder, packets,
capture manifests and consumed native CSVs. Its `provenance.json` records the
unchanged tool provenance and the owner's authorization to use the same
placeholder-permitting validity criteria as Level 4.
