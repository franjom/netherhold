# A completed rock strike and the following claim boundary

2026-10-18. Research only, main gameplay hash/save 274. This replaces neither
the [campaign acceptance verdict](mining-cadence.md#isolated-gap-correction-acceptance)
nor the [whole-turn checker's limits](imp-decision-checker.md).

## Native boundaries — CONFIRMED

A session-owned Level4 `l4live` packet playback supplied two hardware entry/return
pairs from the same process and thread. The recorder changed debug registers,
not game instructions or simulation memory. The replay was closed afterwards.
Executable MD5: `b9709c6c9084d3060e4ac7ac9764c70b`, the previously verified DX
variant. Each of the four images contains 331907072 selected bytes, no short reads.

| Boundary | Entry | Return |
|---|---|---|
| Worker 452's action poll, stored turn 165 | `(28,54)` Rock, neutral, health 165 | Neutral floor, health 1; experience +20 |
| Worker 453's chooser, stored turn 171 | State 10, position `(100249,235478)`, no task/aim/gold | State 6, kind 3, target `(28,54)` |

At the strike, action 1 retains epoch 164 and phase 2726. Its flags change
from 5 to 13 while the action id remains 1: the exhausted action requests stop;
this poll does not finish the action or dispatch the worker's state.
World RNG stays 1899811412 during that call.

The chooser advances RNG from 1768782011 to 1843400869. Three other workers
already reserve the three mining slots on the accessible face of `(29,54)`.
Its route target is `(1867776,3571712)` in 16.16, flags 12, with two points:
`(1802240,3571712)` and `(1867776,3571712)`.

A separate completed poll of worker 455 on turn 165 is a negative control:
the tile is already floor at entry, remains unchanged, and flags likewise
gain stop bit 8. An action poll is not automatically another work hit.

## Modern reconstruction — CONFIRMED results

The chooser fixture restores captured terrain/health/owners, marks and ordered
task entries, worker poses, task sides/slots, hands, loose/stored gold, clock
and native RNG. It executes only the chosen worker's job choice. Holder
relocation, which the historical checker permits, is forbidden here and does
not occur. Reservations are represented through modern work assignments,
not restored native pool-id storage.

Both navigation controls match the native task, state, unchanged position,
ending RNG, route target, flags and both route points exactly:

| Navigation input | Result |
|---|---|
| Captured navigation bytes, modern mesh rebuild | Exact measured output match |
| Same bytes, imported native mesh | Same exact match |

Separately, `TileWorkRules.Apply` receives the captured Rock/owner/health and
worker rate plus bonus with the shipped Level4 data. It produces terrain 3,
owner 2, health 1, matching the completed strike's tile result. This numerical
application does not run the modern action scheduler or verify its experience
award. The native action/stop result is established by the completed live pair.

## Scope and next boundary

No new chooser or path defect is established at turn 171. The fixture is still
a bounded reconstruction: rooms and general nonworker objects are not restored,
and native pool identities and prior locate-cache history are not replayed.
Exact output agreement here does not validate those omitted systems.

The six turns between strike and selection ran in DK2; the modern engine was
not advanced through them from the first image. These are two per-boundary
comparisons, not an end-to-end tick match. The subsequent
[dig stop clock control](imp-dig-stop-clock.md) identifies a delayed action
completion that changes slot availability; its gameplay candidate fails acceptance.
The [Portal visibility control](imp-hatchery-work.md#portal-regression-a-work-visibility-boundary)
already identifies a local scheduling difference, without campaign acceptance.

## Reproduction

Ignored artifacts: `build/imp-acceptance/reverse-engineering/imp-strike-choice-20261018/`.
`capture_live.py` uses the existing `BoundaryDebugger` to capture the action
runner and then switches the entry breakpoint to the chooser. `STRIKER_SLOT=452`
and `CAPTURE_NAME=live-rock-strike` select the validated capture; the completed
negative control is `live-boundaries-retry/`. No manual play is required.

`export_live.py` extracts the boundary fixture, strike inputs and native mesh
using the existing raw decoder. `Program.cs`, `Probe.csproj` and the diagnostic
`DecisionCheck.cs` run on the ordinary hash-274 assemblies. `IMP_MATCH_NAV=1`
matches cache bytes; `IMP_IMPORT_MESH=1` plus `IMP_MESH_FILE` selects the imported
mesh. `PROBE_PREFIX` retains each control separately. `verify.py` checks the
pairs and compared outcomes. Faulted emulator runs are excluded from evidence.
