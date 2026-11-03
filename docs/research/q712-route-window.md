# Q712: the next matched route-window divergence

Source: retail DKII.EXE v1.70, ghidra-queries, 2026-11-01. The complete body
reader [004D5C00](functions/004D5C00.md), route loader [004D5100](functions/004D5100.md)
and position/velocity setter [004D59A0](functions/004D59A0.md) identify the
captured inputs. [Follow_Path](functions/004D3C40.md) refills an exhausted
window before selecting its new aim.

## Confirmed observations

The historical exact-prefix comparisons below use legacy capture headers,
not established executed-turn parity; see the [clock boundary](packet-playback-clock.md).

The seed 2/generator 101 control retains the earlier captured pool/level/scan
inputs, phase pulse and Library goal; these are partial interventions, not a
fully restored native world. The ordinary-room fix is [Q721](room-position-parity.md).
All 81 native frames 1535-1615 are untorn and tick-aligned, for two actors.
Both new body-observer modes reproduce all four prior main replay outputs.

For Imp 455, every shared body field and heading matches at 1544. Through
1565 only the body's low position bits differ: the native body retains
16.16 fractions, while our movement (through hash 391) committed its 1/4096
entity point back into the body. Entity position/state/target/health still match.
Hash 392 keeps the body's fractions (ADR-0644; effect and rule in
[steering-body-retention.md](steering-body-retention.md)).

At 1566 both engines refill the first five-point window. The new active
native window is (11.5,31.5), (8.5,30.5), (8,30.5), (5.5,29.5). Ours omits
the first point. Both total octile distances are 158 sixteenths, but the
window distance is 112 versus 56. Heading and velocity then differ in that
update. Unused raw window/segment slots are excluded from this interpretation.

## Guarded input substitution

A scratch-only hook retains normal ConstrainTarget and world.Route, then
replaces only the returned point list before unchanged Load. Guards require
the exact body, completed tick 1566, start, constrained target, flags and
original three points. Native total/window count four and zero tail prove
all remaining points are available. One queue and one consumption are allowed;
the application record and loaded active window are checked.

Both runs exit 0; the off-control reproduces all four main files. Supplying
the captured points extends Imp 455's compared-field prefix through 1569;
the next movement difference is 1570. Hero 439 still first differs at 1611.
The substitution isolates a route-output input; it neither explains why the
queries differ nor proves equal navigation/steering inputs afterward.

## Available-map input controls

Three additional resyncs compare an unchanged observer with terrain/owner and
terrain/owner/health supplied at 1565. All exit 0; the observer reproduces four
main files. Supplied fields match exactly at application. The full bundle
retains equal terrain/owner at 1566 and 1570, while four health fields diverge.
Neither intervention removes the Imp divergence at 1566 or the hero divergence
at 1611. Existing Navigation is retained/refreshed by WorldResync; this does not
import the native navigation cache or mesh. No production steering fix follows.

## Mismatch inventory before any further production change

| Address | Observation / scope |
|---|---|
| 004D5100 / 004D3C40 | The 1566 refill returns four versus three points. Router input/mesh equality is unestablished; no production fix selected. |
| 00470C80 / 004D5AC0 | Native post-step body fractions survive in the capture; ours kept them from hash 392 (ADR-0644, [rule and outcomes](steering-body-retention.md)). |
| 004D5C00 | The logger reads five window vectors. capture_state.body exported eight, treating subsequent scalar fields as coordinates; corrected to five with a failing old-source regression. Decoder only; no simulation-score explanation. |

## Evidence

Add-only roots under `F:/DK2Evidence/studies/`:
- `q712-route-window-control--2026-11-01--001`: native bodies, observer controls and body comparison.
- `q712-native-window-control--2026-11-01--001`: guarded input, application, active-window check and comparisons.
- `q712-steering-context--2026-11-01--001`: next actor/map/object input comparison.
- `q712-map-input-control--2026-11-01--001`: available-map substitutions and observer control.
- `q712-body-exporter-proof--2026-11-01--001`: committed old exporter fails one test; two health controls pass. Fixed exporter passes all three. Original source is unchanged and checked against the committed blob.

The ordinary full-state capture lacks the navigation-header bytes in every
checked frame; the pointer is outside all recorded regions/thing records.
Do not synthesize the missing cache or infer a router/integrator error from
unmatched inputs. Q712/Q713/Q719 remain open.

## Native query control and pre-edit inventory

A new bounded Level 5 seed-2 replay captured worker refill 004D44F3 at native
clock 1574, before/after 004D5100. This is a different trajectory from the old
1566 case: health, position and goal differ. Both complete memory images have
zero short reads. The first attempt is excluded because its wrong map-size
filter never applied the seed; the successful attempt uses 51x55.

The imported 1720-triangle mesh, 863 points, native locate cache, positions,
six mover globals and class byte 1 reproduce the same endpoint triangles
705 and 545. Native returns (4224,3840), (4138,3626) in 1/256 tiles; ours returns
(4864,3968), (4608,3712), (4138,3626). Clearing the imported locate cache does
not change ours. Executing native 004D5100 on the captured before image
reproduces the real after image's two active points.

| Address | Binary against pre-edit code |
|---|---|
| 004CD000 / 004CD490 | Both search loops read neighbour words +6, +8, +A. In our reversed triangle winding these are slots 2,1,0; MeshRouter.Expand visits 0,1,2. Equal heap keys therefore pick a different predecessor/corridor. |
| 004CD000 / 004CD490 | Native pre-visits the mesh hull ring at 006E86F0/count 006ECA4C, rebuilt by 004CE770; ours has no explicit hull premark. The captured list is [1,1,2,3]. Broader effects remain Q723. |
| 004D0320 | Native region/clearance prechecks are not fully mirrored. Native succeeds in this case; no rejected-route claim follows. |

A scratch-only change to neighbour order reproduces the exact native two
points; no other production source is changed. A synthetic 4x4 floor fan
with a point inserted at (2,2) isolates the tie: both original search functions
return corridor bottom,left,top (10,9,11); pre-edit managed search returns
bottom,right,top (10,4,11). These are binary-backed expectations, not score pins.

Evidence: `F:/DK2Evidence/studies/q712-native-refill-boundary--2026-11-01--002/`
and `q712-native-refill-route-comparison--2026-11-01--001/`; compiled source
provenance is in the matching `q712-native-refill-route-probe-h369` build.
Allocation free lists in the imported mesh are synthesized for query-only
use. No future-edit or old-timeline equivalence is claimed.

## Historical refill isolation

A separate old-369 observer captures the modern mesh immediately after normal
Refresh/ConstrainTarget and before the guarded Imp-455 query at 1566. All four
main files reproduce the old trajectory. Querying that same mesh/profile/start/
target under old and corrected neighbour order returns the same three points;
the original native observation has four. The producer's expected-positive
assertion failed, and this negative result is retained. The neighbour-order
fix explains the new 1574 case, while the historical 1566 mesh/query difference
remains open under Q719. No old native mesh is imported from the new trajectory.
Evidence: `q712-historical-refill-order-control--2026-11-01--001/`, supplemental
negative-result summary and manifest; earlier partial input controls still apply.

## Built scope and verification (hash 370)

ADR-0626 changes only the shared neighbour expansion order. Hash/save 370 has
pin `0xB4927C1D9318B10F`. Both native-backed fan cases pass; archived 198e291a
source fails both, with all 730 production source files unchanged. Release
build has no warnings or errors; Simulation 4306 and Presentation 1413 pass.
The solution retains only the known master Level 18 Behavior failure; the
custom-Q520 failures did not recur. A fresh neighbour-order/determinism run
passes all 11 cases. Independent review approves the bounded Q722 rule.

The frozen build and 115 successful stored-pickup resyncs retain 744 unchanged
source/config hashes and four matching observation-control output hashes.
[Exposure, reporting exits and death scores](fight-scan-initial-phase.md#matched-outcomes-366-through-370) leave Q712 open; Q714 restoration is unchanged.
The full native 1574 control and historical 1566 negative result are distinct.

Verification provenance: `F:/DK2Evidence/builds/q712-search-order-measurement-h370--g198e291a-wip--2026-11-01--001/`,
the corresponding `heart-defence-q712-h370-l5` and `l4` studies, and
`q712-search-order-old-source-proof--2026-11-01--001/`. The latter
`sha256-corrected-20261102.json` preserves and identifies the original
manifest's invalid self-entry; its six other original entries verify.

## Reporting return walks

The visible 254-to-6 exits all resume 254 in native (six total; mean 2.00)
and hash 370 (117 total; mean 7.80). At 369, mean 3.20 splits into resume 254
at 3.1333 and resume 202 at 0.0667. Counts match the original transition totals;
the native extraction checks all 6003 frames for tearing and tick agreement.
The saved resume alone does not identify the sender. This strengthens the
redispatch/encounter-exposure lead without proving another behavior mismatch.
Evidence: `F:/DK2Evidence/studies/q712-reporting-walk-destinations--2026-11-02--001/`.

## Hull premark control

On the retained 1574 mesh, 33 endpoint pairs cover the captured query, 16
near-Heart and 16 map-wide floor-centroid pairs. Paired execution of both native
searches with and without the captured hull list changes none of 66 directional
results. The corrected managed search agrees on all 63 executed directions;
three backwards calls are excluded because the managed driver first fails its
forward search. The class-zero width table and original six mover globals are
explicit. No hull fix follows from this bounded control; Q723 remains open for
other meshes/profiles. Heap sift signed comparisons and tie rules also match
the existing implementation (004CCF00/004CCF70).

Evidence: `F:/DK2Evidence/studies/q712-hull-search-control--2026-11-02--001/`.
Use `results-corrected-v3.json` and `sha256-analysis-v3.json`; earlier parser
analyses are preserved and excluded. The historical 1566 mesh/history remains
unmatched. [Atomic input controls](q712-atomic-inputs.md) now capture a new
trajectory with its retained mesh/cache. They expose the [packet playback-clock
mismatch](packet-playback-clock.md); no old/new trajectory equivalence follows.
