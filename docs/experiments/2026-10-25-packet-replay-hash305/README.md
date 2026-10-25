# Packet replay with persistent combat room-list order, hash305

One sweep of Levels 1–4 over simulation seeds 1–20, using the recorded packets and
the same fixed-map protocol as [hash303](../2026-10-25-packet-replay-hash303/README.md).
This measures our simulation against recorded outcomes; it adds no native outcome
measurement. The adaptive replay can relocate rejected recorded creature drops;
[the existing audit](../../research/decision-parity-audit.md#recorded-drop-boundary-a-replay-adaptation-changes-the-encounter)
documents why these are not strict coordinate-preserving packet comparisons.

## Code and provenance

Measured master commit: `ae14f3b3f55e2a21b3acec7a8592f0db3f156d9a`,
`integrate verified native room ordering`. HashVersion and FormatVersion: **305**.
The implementation commit is `1554db1e`; [ADR-0541](../../DECISIONS.md#adr-0541--retain-player-room-list-order-for-combat-destinations)
and [the canonical evidence](../../research/room-list.md#persistent-player-list-order-hash305)
describe its scope and limits. The captured withdrawal now matches through
production room lifecycle restoration without overriding the combat cache.

The preceding full rate is hash303. Its comparison with305 includes the Secret
Door job changes in304 as well as room ordering in305; no full hash304 sweep
isolates their individual campaign effects. Master advanced separately to306
after this measurement was pinned; these results do not measure that later code.
The historical P2 set predates hash254 and has no established exact build commit.

| Item | Absolute path |
|---|---|
| Clean detached source checkout, retained | `D:/DungeonKeeperRemake/build/room-list-order-2026-10-25` |
| Archived executable actually run | `D:/DK2Evidence/tools/hash305-ae14f3b3-2026-10-25/DK2.Tools.exe` |
| Frozen actual rig | `D:/DK2Evidence/rig-2026-10-25-hash305` |
| Measurement outputs | `D:/DK2Evidence/runs/packet-replay-hash305` |
| Immediate baseline, copied into rig | `baseline-hash303-results.json` |
| Preserved historical P2 | `D:/DK2Evidence/rig-2026-10-24/sets/P2` |
| Native mapping inputs | `D:/DK2Evidence/dk2runs/{level2-live2,level3-live1,level4-live1}/seed01/{creature-map.csv,object-map.csv}` |
| Recorded packets | `E:/GogGames/Dungeon Keeper 2/PacketRecord/{l1live,l2live2,l3live,l4live}.sav` |

Implementation validation and independent review are recorded in the linked
canonical evidence. The measurement uses one clean-source Release build and one 80-seed sweep,
with two workers. No profiler or second sweep runs as part of this measurement.

## Exact commands and validity

The implementation worktree was retained and detached at the merged master
commit. The main checkout's unrelated owner edits remained in place.
At checkout time `master` resolved to the measured commit above. For reproduction,
substitute that exact commit for `master` and use fresh rig/output paths; the
preserved driver refuses existing seed directories.

```powershell
git -C D:/DungeonKeeperRemake/build/room-list-order-2026-10-25 switch --detach master
Set-Location D:/DungeonKeeperRemake/build/room-list-order-2026-10-25
$env:DOTNET_PROCESSOR_COUNT='2'
dotnet build src/DK2.Tools/DK2.Tools.csproj -c Release --no-incremental -p:NuGetAudit=false -m:1
python -B D:/DK2Evidence/rig-2026-10-25-hash305/measure305.py
```

`build.log` records the build. Archived tool files and seven frozen rig files
were SHA-256 compared with their source copies. Preflight checks the clean source
commit, hash/save version, archived copies and the sealed hash303 inputs/results.
The Level 1 static map, `lmap.py`, `pktdump.py` and placed-object map are byte-identical
to the hash303 rig. Native Level 2/3 recordings permit fresh per-seed mapping.

| Level | Packet | Offset | Turns | Map protocol |
|---|---|---:|---:|---|
|1|`l1live.sav`|69|2044|Static `l1m/map.csv`|
|2|`l2live2.sav`|69|3657|Fixed builder, native Level2 seed01, width40|
|3|`l3live.sav`|69|3704|Fixed builder, native Level3 seed01, width50|
|4|`l4live.sav`|69|6804|Fixed builder, native Level4 seed01, width50, placed-object map|

Each replay passes `--seed N`, `--packet`, `--packet-offset 69`, `--turns`,
`--entity-map`, `--events` and `--creatures`. Each seed's `command.json` records
the default RNG protocol without a `--rng` override, matching hash303, and
the exact argv, cwd and environment. For Levels 2–4, `stable_map.py` invokes the
unchanged fixed mapper functions and writes every full/partial attempt under
`iterations/`. It exclusively copies the stable final files to the seed root and
checks their SHA-256s. It stops an invalid no-progress attempt rather than repeat
the old driver's final partial run. No existing evidence file is overwritten.

Validity retains the owner-authorized historical criteria: exit0, every expected
turn, stable final map for Levels 2–4 and no placed-type mismatch. Placeholder rows
are disclosed rather than rejected; commands targeting them may miss. Invalid
runs are excluded from wins and are not classified as gameplay losses.
`replay.csv`, `events.tsv`, `ours-creatures.csv`, `entity-map.csv` and `run.log`
remain per seed; intermediate commands and full child logs are also retained.
Top-level input/provenance/progress/result records identify the actual measurement.

## Results

All 80 runs are valid under the criteria above. Baseline validity is taken from
each saved results file. Win ticks are min / median / max over wins only.

|Level|305 valid /20|305 wins /20|305 win ticks|303 valid /20|303 wins /20|303 win ticks|P2 valid /20|P2 wins /20|P2 win ticks|
|---|---:|---:|---|---:|---:|---|---:|---:|---|
|1|20/20|18/20|1772 / 1788 / 1912|20/20|18/20|1776 / 1792 / 1900|20/20|20/20|1768 / 1808 / 1832|
|2|20/20|9/20|3080 / 3472 / 3656|20/20|7/20|3128 / 3536 / 3628|20/20|6/20|3136 / 3546 / 3648|
|3|20/20|17/20|3556 / 3608 / 3648|20/20|18/20|3540 / 3584 / 3652|20/20|14/20|3556 / 3588 / 3644|
|4|20/20|1/20|6748 / 6748 / 6748|20/20|3/20|6736 / 6752 / 6752|20/20|2/20|6732 / 6740 / 6748|

Final placeholder-map row ranges are 0 for Level 1, 5–23 for Level 2, 9–13 for
Level 3 and 2–13 for Level 4. Their per-seed values remain in `results.json` and
`comparison.json`. No invalid run was classified as a loss.

### Changed outcomes against hash303

“No win” means no win by the recording's turn limit.

|Level|No win → win|Win → no win|Winning tick changed among seeds winning in both sets|
|---|---|---|---|
|1|—|—|2, 5, 8, 9, 10, 11, 12, 14, 15, 16, 18, 19, 20|
|2|3, 10, 12|6|1, 2, 8, 11, 16, 20|
|3|20|5, 10|1, 2, 3, 4, 6, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19|
|4|3|6, 16, 19|—|

### Changed outcomes against P2

|Level|No win → win|Win → no win|
|---|---|---|
|1|—|3, 7|
|2|1, 2, 3, 8, 10, 11, 16|6, 9, 14, 15|
|3|9, 12, 16, 17, 18|7, 10|
|4|—|19|

## Level 4 Imp deaths

Counts use player 3 `creature_died` events of type Imp through turn 6804.
P2 counts are recomputed from its preserved events, not inferred from its wins.

|Simulation seed|Hash305|Hash303|P2|
|---|---:|---:|---:|
|1|14|19|1|
|2|1|36|5|
|3|3|0|4|
|4|4|1|6|
|5|2|6|37|
|6|0|6|2|
|7|4|8|39|
|8|18|28|39|
|9|10|4|6|
|10|35|58|4|
|11|18|17|7|
|12|1|22|16|
|13|26|22|16|
|14|42|19|35|
|15|47|38|38|
|16|10|3|6|
|17|4|1|41|
|18|1|7|29|
|19|0|0|4|
|20|10|9|2|
|**Median**|**7**|**8.5**|**6.5**|

The saved native seed 1/2 reference counts remain **0/19**, including an Imp
slot becoming an object as a death, as recorded in the
[previous comparison](../2026-10-25-packet-replay-hash303/README.md#level-4-imp-deaths).
Those two captures do not define a 20-seed native distribution.

## Interpretation and verification

The verified room-order correction does not improve every campaign rate.
Level 2 gains two wins against303; Level 3 loses one and Level 4 loses two.
The narrower captured withdrawal still matches its native result. Broader
combat/scheduling parity remains open, and the303-to305 comparison includes304.

Level 1's non-winning seeds remain3 and7. Their current event logs both record
`RoomBuild build_refused player=3 room=Hatchery drag_cost=0` at turn466 and no
later Hatchery build. The prior [failure analysis](../2026-10-25-packet-replay-hash303/analysis.md)
owns the upstream explanation; this correction does not close that blocker.

All requested levels and seeds ran. No hash304 baseline, hash306 sweep or new
native outcome sweep ran; the comparison and placeholder limits above remain.
No simulation changes were made after freezing the measured commit.

Post-analysis scripts are preserved under `outputs/analysis-scripts/`, where
`outputs` is the absolute measurement-output directory in the path table.

```powershell
python -B D:/DK2Evidence/runs/packet-replay-hash305/analysis-scripts/analyze305.py
python -B D:/DK2Evidence/runs/packet-replay-hash305/analysis-scripts/checkP2.py
python -B D:/DK2Evidence/runs/packet-replay-hash305/analysis-scripts/verify305.py
```

The analyzer's original run creates `comparison.json` exclusively; rerunning it
requires a copied script with fresh output paths. The two verifiers are read-only
without `--seal`. `checkP2.py` verifies21 reporting inputs against their original
preservation manifest. `verify305.py --seal` verified all80 final full logs,
consecutive turns, placeholder counts, promoted file hashes and pinned inputs,
then sealed **2071 artifacts**. Subsequent verification omits `--seal`.
Independent read-only review passed both verifiers and recomputed all tables,
changed-seed lists and Imp-death counts without further replays.
