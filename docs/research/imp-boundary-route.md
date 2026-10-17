# First digging choice at an actual selection boundary

2026-10-17: Level 4 packet `l4live`, keeper 3, Imp slot 452. Gameplay source
is master `fbfc838e`. The [boundary recorder](../../tools/oracle/decision-boundary.md)
captured the native chooser's entry and return at stored clock 133. The whole-turn
capture had first observed this choice at frame 134. These are different boundaries.
This is one controlled comparison, not a campaign fidelity verdict.

## Native capture — CONFIRMED

At entry the Imp is in state 10, position `(88182,223342)` in 1/4096 units,
with no task, aim or gold. The captured pool contains one task. Entry RNG is
1475718508. At return it is in state 6, assigned kind 1 at `(28,54)`, and
RNG is 2257439195. Position and clock are unchanged. Both memory images
hold 317108224 selected bytes with no short read. The replay was closed after
capture; the owner's Unity sessions were not controlled.

The body's route target in 16.16 is `(1824064,3549856)`, flags 13, three
points: `(1605632,3473408)`, `(1703936,3538944)`, `(1824000,3549696)`.
The target preserves the full requested point; the last window point is
quantized. The [body route record](functions/004D5100.md) identifies the fields.

## Controlled modern reconstruction — CONFIRMED results

A local copy of `DecisionCheck` loads tiles, marks, worker poses/task fields,
the task list and gold objects from the actual entry image, without the old
extractor's earlier-worker patch. It uses `Dk2Lcg` with the captured seed and
sets the diagnostic world's clock to 133. It runs only the selected worker's
chooser. Rooms and other world state remain subject to the
[checker's limits](imp-decision-checker.md); this is not a restored simulation save.

| Input to modern chooser/router | Task and ending RNG | Target / flags | Route window |
|---|---|---|---|
| Captured selection fields, rebuilt navigation | Same as native | Same | Five different points |
| Also restore all 3200 native navigation bytes | Same as native | Same | Same five-point discrepancy |
| Also import native mesh vertices, triangles and adjacency | Same as native | Same | All three native points exactly |

Before the navigation-byte control, four cache cells differ: `(27,5)`,
`(24,9)`, `(24,27)` and Heart centre `(24,54)`. Afterwards all 3200 match.
The native-mesh control uses the existing meshreplay winding/edge remapping
and a zero-initialized locate cache; it does not claim to restore native
locate-cache history. No source routing algorithm was changed.

The fresh route goes through tile-space points `(22.5,55.5)`, `(23.5,56.5)`,
`(26.5,56.5)`, `(27,54.5)`, then the same final quantized point. Its
per-segment-truncated octile length is 147 sixteenths, versus native's 121.
That is a route-length observation, not a measured arrival-time difference.

## Interpretation — STRONGLY INFERRED boundary, unresolved cause

This choice does not expose a task-priority or RNG-consumption mismatch.
The route difference survives matching the navigation cache and disappears
when the captured mesh replaces the rebuilt one. Investigate the mesh inputs,
initial construction, Heart routing mask and partial-update history before
altering the router or the worker. The live Unity mesh has not been captured
in this comparison; a freshly reconstructed mesh need not be its current mesh.
No claim is made that this single case explains the later reinforcement streak.

Next comparison: initialize the modern level normally, compare its mesh with
the native entry image, then isolate the first differing initialization or
update. Preserve the matching task-selection and same-mesh route results.

## Local artifacts (ignored)

All paths are relative to `build/imp-acceptance/reverse-engineering/`:

- `imp-boundary-native-dx-20261017/boundaries.jsonl` and the two `.mem.z` files:
  actual hardware-breakpoint pair, installed DX executable, child-only
  `RunAsInvoker` environment. The first capture predates the manifest's new
  compatibility-environment field; the invocation supplies that provenance.
- `imp-boundary-replay-20261017/export.py`: rebuilds a snapshot through the
  existing raw recorder/decoder, exports decision fields and native mesh.
  Snapshot reconstruction reports no missing reads. It imports the decoder
  from the main checkout's ignored `reverse-engineering/`.
- In that replay directory, `decision.jsonl`, `native-route.json`,
  `native-navigation.json`, `native-mesh.bin`, and `modern{,-nav,-native-mesh}.tsv`
  with `.route.json` and `.navigation.json` sidecars reproduce the controls.
- `Probe.csproj`, `Program.cs`, `DecisionCheck.cs` are throwaway diagnostics:
  reflection sets the captured clock/stamps and injects the imported mesh.
  `IMP_MATCH_NAV=1`, `IMP_IMPORT_MESH=1`, `IMP_MESH_FILE=.../native-mesh.bin`
  select controls. Neither reflection nor the mesh import enters production.
