# Imp selection: binary evidence and matched boundaries

2026-10-25. Research only; no gameplay changes or new rate sweep. Modern target:
`e96639742fa12c87eeb1407cdc13418f1b7931fe`, HashVersion 303. This extends the
[decision audit](decision-parity-audit.md), with a live capture of the previously
missing Level 1 choice. It establishes bounded equivalence, not complete Imp AI parity.

## Evidence and coverage

All raw evidence, diagnostic source and outputs are under
`D:/DK2Evidence/imp-selection-evidence-2026-10-25/` (R below), add only.
`artifact-manifest.csv` seals the files; `verify_evidence.py` verifies the seal
and result assertions without starting either game. Binary output stays outside git.
Native executable: `E:/GogGames/Dungeon Keeper 2/DKII-DX.exe`, MD5
`b9709c6c9084d3060e4ac7ac9764c70b`. Packet: existing `l1live.sav`.

| Boundary | Binary evidence | Execution coverage | Limit |
|---|---|---|---|
| State-10 choice, `00463DF0` | Full fresh decompile, existing instruction reading | Three live entry/return pairs and original-byte replay; 61 ordering controls | Controls replace search success, state setter and rest RNG |
| Kind dispatch, `00468520` | Fresh full decompile | Real claim dispatch in all three pairs; mining/reinforce in counterfactuals | Other eligibility arms not exhaustively executed |
| Ring scan, `00468D10` | Fresh full decompile and hooked original bytes | Three exact live inputs, candidate and RNG trace | Not every radius/navigation/teleport condition |
| Claim start side, `00469340`, grade `00469460` | Full start-side disassembly; both fresh decompiles | Actual N/E/S/W grades and selected side at each pair | No exhaustive terrain/rank permutation matrix |
| Drop chain, `00469240` | Full decompile, instruction call sequence | 46 controls: none, nine singles, every pair | Handler eligibility stubbed |
| Secondary chain, `00469580` | Full fresh decompile | 11 controls: none, four singles, every pair | Handler eligibility stubbed |
| Dig/mine finder, `00469AF0` | Fresh decompile; mining blockage instruction check | Real mining choice in last-kind-cleared control | No new exhaustive slot/holder fixture matrix |
| Claim handler, `0046AB50` | Fresh decompile, entry instruction check | All candidate branches in three choices | These choices do not cover all stealing/alliance/room branches |
| Reinforce, `0046AEA0`; on-way `00464170`; fresh-floor `00469600` | Fresh decompiles | Reinforce in controlled input; other two not separately exercised here | Existing records retain their evidence and scope |

`binary-review/decompiled-functions.json` preserves those 12 functions;
`leaf-functions.json` adds 18 relevant handlers, scanners and pool functions.
The latter inventory includes the tile dig handler, gold, objects, blueprint
scanner/tile handler, secret-door scanner/tile handler, corpse, rescue scanner/tile
handler, gold-in-hand/store selection, and pool raise/first/next/find/holder access.
Querying interior address `00508190` resolves to function `00508160`.
Fresh decompilation is available for follow-up inspection; it does not confer
execution coverage or upgrade existing LIKELY verdicts for dig, corpse or rescue.

The canonical rules remain in [task priority](imp-task-priority.md),
[task stealing](imp-task-stealing.md), [claim eligibility](imp-claim-path.md),
[objects and secret doors](imp-task-objects-and-walks.md), and
[task-pool structure](player-task-pool.md). Function records own individual verdicts.

## What the priority evidence establishes

**CONFIRMED from the binary:** ordinary priorities are not the first thing an
Imp tries. State 10 first applies the special-owner/throttle branches, then an
aimed retry and the shipped last-kind retry table. A failed dig/mine retry can
try depositing before ordinary priorities. A just-dropped Imp has a local scan
before the ordinary kind list; the secondary search runs only after that list
fails. Exact lists and predicates are in the canonical task-priority sections.

`priority-matrix.json` contains 118 passing original-byte controls: 61 state-10
controls, 46 dropped-chain controls and 11 secondary-chain controls. These cover
single/pair competition in the ordinary effective list, successful/failed
last-kind retries, aimed retries and deposit-before-object precedence. Kind 14
is attempted but the real dispatcher has no successful arm for it; controls
never allow it to succeed. Kinds 4/5 likewise cannot be synthetic successes.

These controls run original caller bytes with fixed machine-code stubs reading
variable availability data. They verify the attempt sequence and short circuit;
they do not verify whether a real task is available, routing, handler side
effects or RNG parity. Rest RNG is explicitly forced to zero in this matrix.
The separate live-input traces below retain the actual searchers, router and RNG.

One stale prose claim was corrected in task-priority §2.2: the mining slot
obstruction is a gold-pile object above a threshold, not any creature occupying
that third. Fresh instructions `0046A13E`–`0046A1A4` check class 2, object type 1,
strict interior position in the `0x555` cell, and gold greater than `[006F25CD]`.
The existing finder record and modern `MiningSlots` already described that rule.

## Live Level 1 worker 455

`capture_native.py` observes hardware execution breakpoints at chooser entry and
its return address. It captures six stopped-process memory images, never patches
game instructions or simulation fields, detaches, and closes only its own game.
All pairs have the same process/thread and unchanged execution clock across the
call; all six images report 334,848,000 selected bytes and zero short reads.
The packet was played once for this capture. No recorded packet was changed.

| Pair | Execution clock | Previous claim | Chosen claim | RNG range/result | Seed before → after |
|---:|---:|---|---|---|---|
| 1 | 310 | (13,21) | (15,23) | 4 / 0 | 1016825632 → 2051772415 |
| 2 | 327 | (15,23) | (14,32) | 4 / 1 | 1433018614 → 335193053 |
| 3 | 360 | (14,32) | (15,32) | 4 / 1 | 2421696086 → 1855269309 |

All enter in state 10, last kind 3, aimed kind 0, not just dropped, zero task id
and keeper selection counter zero. Each returns state 6, claim kind 3, task id
22, counter one and last-evaluation stamp equal to its execution clock.
Each keeps its position and 1,092 carried gold. The actual search sequence is
`[3]`: the last-kind retry succeeds before ordinary priorities or depositing.

The claim first observed in the earlier full-state snapshot at header 328 is
therefore selected at execution clock 327. This is an observed relation for this
boundary; [snapshot clocks](imp-observation-clocks.md) forbid a universal shift.

### Why (14,32) wins this choice

At execution 327 the ring scanner consumes `random(4) = 1`; its start-side
callback also selects east (side 1). It grades north/east/south/west as
6/1/0/0, ranked 12/1/127/127. The native grading result is a byte: upper EAX
bits can be stale and must not be treated as part of the class.

The trace records 102 claim-handler invocations, including repeated ring corners:

| Count | Result | Last decisive instruction |
|---:|---|---|
| 63 | Already owned by this keeper | `0046AB98` |
| 34 | Terrain flags intersect `0x81` | `0046ABA7` |
| 4 | No adjacent owned floor | `0046ABFE` |
| 1 | Accept (14,32) | Successful task/route path |

The four adjacency rejections are (10,26), (10,27), and (15,33) twice.
They are not all unique tiles. Earlier/later pairs invoke the claim handler
32 and two times respectively. This explains these native choices from their
actual inputs; it does not explain how every losing modern seed reached its inputs.

Two emulator-only counterfactuals change input job fields, not the live capture:

| Pair-2 input control | Search attempts | Result |
|---|---|---|
| Clear only last kind | 14, 8, 9, 1 | Mine (16,23) |
| Set last and aimed kind to 6 | 6 | Reinforce (15,22) |

Both consume the same one range-4 draw and finish at seed 335193053.
The controls establish that previous/aimed work changes selection in this state;
they are not campaign outcomes or proposed changes to the rule.

## Hash303 comparison

`export_fixtures.py` reconstructs selected captured inputs at the three exact
entry boundaries: terrain/ownership,
ordered pool data and holders, worker poses and job fields, native names/blood,
gold piles, captured navigation bytes and native mesh. It reports no missing reads.
`ModernProbe-v3` references the archived hash303 DLLs directly and invokes the
real private `UpdateIdle` through reflection, including the cap/counter path.
It does not use the timer/cap-omitting public `ChooseJob` diagnostic entry.

| Compared output | Rebuilt modern mesh | Captured native mesh + navigation bytes |
|---|---|---|
| Kind/tile, state, pose, carried gold | All three match | All three match |
| Single RNG call, result and final seed | All three match | All three match |
| Keeper selection counter | All three match | All three match |
| Route target and flags | All three match | All three match |
| Route points/count | Pair 1: 2 vs native 3; pair 2: 3 vs native 4; pair 3 matches | All three match exactly |

**CONFIRMED, bounded:** these three selected jobs match through their RNG calls.
Supplying the native navigation inputs removes the observed route discrepancy.
The [navigation follow-up](navigation-partial-rebuild.md#6-level-1-same-input-navigation-controls-2026-10-25)
separates the missing fixture byte from mesh history: a native fresh build itself
produces the changed route. Actual campaign update history remains unverified.

The fixture omits full room state and general non-worker objects, uses modern
reservation representations, and does not advance the other creatures or the
intervening turns. Holder relocation is forbidden. Matching a selected task or
an imported route cannot establish the fidelity of omitted inputs, mesh updates,
full scheduling, task completion, interruptions, or every other task kind.

## Reproduction

Tools: Python `D:/Python/python.exe`; Unicorn DLL
`D:/Tools/unicorn-2.1.4/unicorn.dll`; Ghidra 12.1.4 via the configured
PyGhidra service. Raw inspection targets are listed in the coverage table and
leaf inventory. Modern assemblies come from
`D:/DK2Evidence/tools/hash303-e9663974-2026-10-25/`.
Copied helpers and their hashes are in `rig/` and the manifests; scratchpads
are not needed. `ModernProbe/` and `ModernProbe-v2/` preserve failed compilation
drafts; neither was executed. Only v3 produced the final comparison outputs.

Commands actually executed (paths abbreviated by R; expand it to the evidence root):

```powershell
python -B R/capture_native.py --out live-level1-worker455 --from-tick 300 --pairs 3 --timeout 240
python -B R/export_fixtures.py
python -B R/trace_native.py --pair 1 --variant base
python -B R/trace_native.py --pair 2 --variant base
python -B R/trace_native.py --pair 3 --variant base
python -B R/trace_native.py --pair 2 --variant last-zero
python -B R/trace_native.py --pair 2 --variant aim-reinforce
python -B R/priority_matrix.py
dotnet build R/ModernProbe-v3/ModernProbe.csproj -c Release --nologo
# Each pair NN=01,02,03, first with these variables absent:
dotnet R/ModernProbe-v3/bin/Release/net10.0/ModernProbe.dll R/fixtures/pairNN/decision.jsonl R/fixtures/pairNN/modern.tsv
# Then each pair again, using new output names:
$env:IMP_IMPORT_MESH='1'; $env:IMP_MATCH_NAV='1'
$env:IMP_MESH_FILE='R/fixtures/pairNN/native-mesh.bin'
dotnet R/ModernProbe-v3/bin/Release/net10.0/ModernProbe.dll R/fixtures/pairNN/decision.jsonl R/fixtures/pairNN/modern-native-mesh.tsv
python -B R/verify_evidence.py --seal
```

Capture/export/trace/result paths refuse existing outputs. Use a new evidence
root for reruns; do not overwrite this set. The capture uses the documented
`RunAsInvoker` hardware-debugger workflow and refuses a pre-existing native game.
Diagnostic builds compile only the probe, not the simulation or DK2.Tools.

Independent review rechecked the native return comparisons, route distinction,
claim branches, counterfactuals and priority-control scope. The saved verifier
passes all result assertions and verifies 141 SHA-256 entries. Repository gates:
`dk2tools docs` reports no changed-file budget violations; `kb check` validates
2,315 records without findings; `git diff --check` reports no whitespace errors.
No production code was changed by this study.

## What remains unconfirmed

**UNKNOWN as complete equivalence:** every possible task-selection input.
Existing LIKELY handler verdicts remain so; all room/blueprint/rescue/corpse,
secret-door, capacity, stale-holder, stealing, state-refusal and route-failure
branches need dedicated native-input controls before claiming complete coverage.
The 118 ordering controls do not close those predicates.

For the measured Level 1 failures, follow the first divergence in task-pool,
pose, reservations and navigation history upstream of choosing; preserve the
same-input chooser comparison as a regression boundary. For Level 4, capture
the first worker interruption/acquisition/withdrawal boundary with room data and
RNG. The existing [audit](decision-parity-audit.md) retains the confirmed combat
and scheduling gaps; this study neither fixes them nor attributes every loss to RNG.
