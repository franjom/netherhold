# Q712: the next matched route-window divergence

Source: retail DKII.EXE v1.70, ghidra-queries, 2026-11-01. The complete body
reader [004D5C00](functions/004D5C00.md), route loader [004D5100](functions/004D5100.md)
and position/velocity setter [004D59A0](functions/004D59A0.md) identify the
captured inputs. [Follow_Path](functions/004D3C40.md) refills an exhausted
window before selecting its new aim.

## Confirmed observations

The seed 2/generator 101 control retains the earlier captured pool/level/scan
inputs, phase pulse and Library goal; these are partial interventions, not a
fully restored native world. The ordinary-room fix is [Q721](room-position-parity.md).
All 81 native frames 1535-1615 are untorn and tick-aligned, for two actors.
Both new body-observer modes reproduce all four prior main replay outputs.

For Imp 455, every shared body field and heading matches at 1544. Through
1565 only the body's low position bits differ: the native body retains
16.16 fractions, while our movement commits its 1/4096 entity point back into
the body. Entity position/state/target/health still match. This observation
does not establish the effect of that representation difference.

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
| 00470C80 / 004D5AC0 | Native post-step body fractions survive in the capture; ours rounds back to entity precision. Its behavioral effect is untested. |
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
