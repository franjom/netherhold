---
systems: [creatures, navigation, simulation-core]
---
# Neutral idle continuation: captured heading

## Cause of the 1453 boundary

CONFIRMED in the Level5 seed1 capture: neutral creatures447 and449 both have
heading0 at completed1400. The439 continuation instead retains prehistory
headings566 and1833. Their restored action clocks hold those headings until
the next wander; position equality during the hold does not establish equal
movement inputs.

For447, native and modern both create the route at1419. Native moves to
190524/207679 then, while the old control spends that turn rotating and barely
moves. The ensuing walk is one turn late: native reaches state1 at1451 and
state5 at1452; modern reaches them at1452/1453. Native therefore makes the
State.cpp:1025 draw at1453 before any object draw, while modern cannot yet do so.

STRONGLY INFERRED from fresh whole-body reads: [idle inner](functions/004776C0.md)
draws100 for a nonzero wander stamp; [the state3/4 wrapper](functions/00477680.md)
adds action/deadline gates that neutral state5 bypasses. [Follow Path](functions/004D3C40.md)
turns before returning seek force, braking while an angle remains.
[The turn helper](functions/004B2AE0.md) reads/writes heading+F4 and returns
the remaining angle. These existing rules explain the delayed draw; no
new gameplay rule is needed for this boundary.

## Controlled comparison, production439 unchanged

The new diagnostic input Q712_NEUTRAL_HEADING_INPUT restores only headings
from completed1400, guarded by tag, type, owner and XY. Its two rows cover
all native neutral creatures in that snapshot. It adds one key to the prior
21; it does not inject a later route, action, deadline or draw.

| Control | Ordered sites/preseeds through complete turns | Next difference |
|---|---|---|
| Read/validate22inputs, no assignment | 716 through1452 | 1453 draw1: State1025 vs Chicken ObjectState2851 |
| Restore447's1400 heading only | 876 through1464 | 1465 draw1: the same sites, now for449 |
| Restore both neutral headings | 1041 through1474 | 1475 draw1: native ObjectState2851 vs modern ResearchState215 |

The read-only/two-heading pair uses identical dictionaries of22 declared
input keys and values. The one-creature run uses its earlier one-row file.
Frozen439 runtime sources are unchanged; only the diagnostic reader and
telemetry differ. Two inherited
observation-only files, ChickenMovementRules and ObjectMovementSystem, already
differ from the main checkout; this is not a claim of byte identity to main.
`source-input-proof.json` records this scope; `unchanged-runtime-assemblies.json`
records the read-only/applied runtime DLL comparison. The next boundary
has preseed2513196491; modern Warlock439 draws7 at its shelf before DK2's
first object draw. It needs a fresh caller/guard and captured-state check.

This is bounded draw continuation, not full-state, rendered playback or
Heart-outcome parity. Other captured headings and restore fields remain open.
No production hash/save bump or general restore-adapter implementation follows.

Evidence: `F:/DK2Evidence/studies/q712-neutral-heading--2026-11-07--001`.
Native headings come from `captured1400-neutral-headings.json`; later frames
in `native-heading-boundary.json` only validate the diagnosis. Run names are
`neutral-read-only`, `heading-restored` and `neutral-headings-restored`.
The matching build archive retains `source.zip`, payload and
`frozen439-source.zip`. The read-only source/payload control is under
`heading-read-only/`. Final manifests and the external integration receipt
record delivery verification.

## Verification and scope

`delivered439-neutral-headings-verification.json` verifies the final 1041-draw
complete-turn prefix. `delivered439-neutral-headings-components.json` verifies
150 neutral state/XY samples (75 completed turns, 1400 through 1474, for two
actors) and 75 Imp455 state/resume/XY samples. These checks end at the prefix;
no later component, full-state or Heart-outcome parity is established.

This is research-only: no production source, tests, scenarios, hash/save
version or rule count changes. The prior [production439 verification](chicken-birth-clock.md#verification-at-439)
retains its original scope; the full .NET suite is not rerun for this control.

Study delivery is recorded by `delivered-change-v2.zip`, `final-scripts/`,
`verification/summary.json`, `manifest-final.json` and `integration-receipt.json`
at the external study path above. The matching build path is
`F:/DK2Evidence/builds/q712-neutral-heading--2026-11-07--001`.
