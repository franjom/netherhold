---
systems: [creatures, rooms, tooling-process]
---
# Q710: fighter decisions after the Level 4 restore

## Verdict

Q710 remains OPEN. Local bed and remembered-job controls establish particular
modern decision effects. The post-build paired control below does not measure a
production before/after improvement or explain the complete Guard-room gap;
unequal restore inputs and later birth populations remain limits.
This is a bounded continuation of the [post-Q714 comparison](resync-heart-terminal.md#level-4-recheck).
No native game was launched and no production simulation code was changed.

## Evidence and controls (2026-11-14)

The frozen hash-516 packet, enriched turn-5000 inputs and eight native captures
come from `F:/DK2Evidence/studies/q714-restore--2026-11-14--001`.
The following additive studies retain their scripts, results and SHA-256 manifests:

| Study under `F:/DK2Evidence/studies/` | What it verifies |
|---|---|
| `q710-room-decisions--2026-11-14--002` | Raw native resume fields against the carried CSV rows; all prior state totals reproduced for eight native and 48 modern runs |
| `q710-population-census--2026-11-14--001` | The same totals split by initial/retained actors and post-restore birth events |
| `q710-room-observer--2026-11-14--002` | Actual restore pairings and an immediate post-restore sidecar for native seed 7 / generator 101; observer off and on each reproduce all five archived telemetry files byte-for-byte |
| `q710-current-h518--2026-11-14--001` | Two diagnostic replays, generators 101 and 202, at examined source `7d6c318a`, hash 518 |
| `q710-decision-summary--2026-11-14--002` | Selected raw native states/bed links, paired observer rows and old/current metrics, with hashes of the preceding manifests |
| `q710-428-inventory--2026-11-14--003` | Native 428's inline state/needs fields and bed 2356's reciprocal link, type, tile and room membership |
| `q710-428-bed-control--2026-11-14--002` | Hash-519 logical bed-ownership intervention, immediate field assertions and first acquisition trace; four completed replays, including byte-identical plain/off/on observation controls |
| `q710-428-inline-control--2026-11-14--001` | Six hash-519 replays: supported needs/job field restores, a diagnostic remembered-job retry, boundary assertions and native 428's state census |

Ordinary CSV rows at turn 5000 precede `WorldResync.Apply`. The sidecar records
the actual `ResyncReport.Pairs` and the state immediately after that call; it
does not rematch creatures or re-run their decisions. The census uses native
observation labels without shifting them to presumed execution turns. All
native labels 5000–5450 used by the resume census were present and untorn.

Rejected setup attempts remain outside the accepted manifests: room-decisions
001 failed a total-count assertion, room-observer 001 failed source-provenance
validation, and decision-summary 001 stopped on a non-tick sidecar header.

## The first observed difference is already in the restored inputs

CONFIRMED in the seed-7 capture at label 5000: Dark Elf 428 is in state 6,
resume 98, with bed 2356 pointing back to creature 428. Troll 453 is already
walking in state 7, resume 80 (make a home). By label 5300, Troll 453 owns bed
3916 and walks in state 7, resume 63 (Workshop).

In the hash-516 generator-101 replay, the actual paired modern ids are 75 and
83 respectively. Immediately after restore, both are idle state 1, resume 0,
without a bed or active route. The same is true of all seven restored keeper
fighters in this sample. At replay label 5002, all seven are walking in state
7 with resume 80. In particular, actor 75 is seeking a home although native
428 already owns one.

The code accounts for the unequal inputs: `WorldResync.Apply` restores fighter
pose and health, retains some modern free states, and otherwise can reset to
idle. It does not reconstruct their native bed links or complete job/route and
needs history. This differs from saying that the same fighter, given the same
inputs, chose a different job. The existing [lair acquisition rule](lair-acquisition.md#5-what-starts-the-search-q150)
checks bed ownership before departure; a fresh binary read of
[0046F390](functions/0046F390.md) leaves that reading unchanged.

## Late home-seeking includes different actors

For labels 5300–5450, native Goblins and Trolls contribute zero state-7/resume-80
exposure. The 40 frozen default-Xoshiro replays average 39.125 such actor-turns
per run. This counts time walking toward home acquisition, not time executing
state 80 or a number of decisions.

| Modern population class | Goblin actor-turns/run | Troll actor-turns/run |
|---|---:|---:|
| Initial or retained | 13.3 | 1.325 |
| Post-restore unmatched birth | 3.25 | 21.25 |

In seed 7 / generator 101 specifically, the extra Troll home-walking exposure
belongs to new modern actors 87 (151 turns) and 88 (112 turns), both unmatched
by the existing birth-pair heuristic. It does not belong to initial Troll 453.
New Dark Elf 89 contributes another 123 home-walking turns and is paired to
native 419, with modern birth label 5320 versus recorded native CSV birth label
5274. The pair log's `native_turn=5273` is its matching convention.

An unmatched birth means the existing same-type/owner, 100-turn-window matcher
found no partner; it does not prove that native could never produce that actor.
“Initial or retained” likewise does not prove exact matched identity. The
population split reduces a confounder but does not produce matched histories.

## Examined hash-518 snapshot

The old comparison predates [Q807's portal rules](portal-creature-action.md#the-portals-type-pick-built-2026-09-28-adr-0272)
and subsequent changes. Reusing seed-7 inputs at hash 518 gives:

| Generator | Fighter actor-turns, 516 → 518 | State-7 actor-turns, 516 → 518 | Goblin/Troll Guard-target actor-turns, 516 → 518 |
|---|---:|---:|---:|
| 101 | 1621 → 1339 | 909 → 587 | 77 → 142 |
| 202 | 1490 → 1923 | 886 → 1094 | 115 → 79 |

Fighter/state exposure uses 5300–5450; target exposure uses 5000–6000 and the
prior scorer's living owner-1 Guard definition. Birth mixes also change. These
two runs describe examined source `7d6c318a`, not the latest master. They are
not a replacement cohort, an isolated Q807 intervention or evidence of a general
improvement/regression.

## Bed-ownership control at hash 519

The inventory verifies the exact untorn native label 5000: creature 428's full
`0x53F` bytes and bed 2356's full `0x189` bytes are retained. The bed is type 20,
owner 3, tile (27,59), in Lair room 1111, and points back to 428. Inline state,
resume, current/return job and needs fields are available; their meanings follow
[the job object](creature-job-object.md). The legacy recorder excludes pointed-to
path buffers and the navigation cache; see [capture coverage](../../reverse-engineering/README.md#legacy-polling-captures-dk2recordpy-dk2capturepy).
This prevents reconstructing the full native route from these frames.

The diagnostic uses copied source `d0d3d065`, hash 519, native seed 7 and default
generator 101. The unmodified build, instrumented build with observation off,
and instrumented build with observation on produce identical bytes in all five
ordinary telemetry files. Both builds pass with zero warnings/errors.

Immediately after restore, the actual pair for native 428 is modern 76, idle
state 1/resume 0, without a bed. The intervention creates one logical type-20
bed for that pair on the free owned Lair tile (27,59). All non-bed creature
fields recorded by the sidecar are asserted unchanged at that boundary, as is
the full RNG state.
Object count increases 154→155 and the next-object id 1036→1037; the linked
object is bed 1036. Creation forwards two ordinary sound requests, events 241
and 242. These allocation/linking/callback effects are part of the intervention.
It does not restore native raw object position, flags, process state, heading or
identity; the logical bed tile is the asserted placement input.

| Replay label | Ordinary restore: state / resume | Bed-ownership control: state / resume |
|---|---|---|
| 5001 | 89 / 0 | 89 / 0 |
| 5002 | 7 / 80 (walk to make a home) | 97 / 0 (Guard entry) |
| 5003 | 7 / 80 | 6 / 98 (Guard walk) |

The ordinary replay reaches the acquisition helper's existing bed test at world
tick 5001 and reads false. The control never calls the helper for this actor:
`CreatureJobSystem` checks `HasBed` before calling it. The caller-side bypass is
STRONGLY INFERRED from the unchanged source and observed ownership; no flipped
inner helper guard was observed. The native [acquisition guard](functions/0046F390.md) reading
is unchanged. Thus missing bed ownership explains this modern home-acquisition
attempt, rather than demonstrating a different native job-selection rule.

All four replays complete through label 6000. This is a single-actor decision
control, not a Guard-room outcome comparison. Native 428 already has state
6/resume 98 and current job 6 at the restore label; the control retains modern
state, chooser/needs history and reconstructed navigation. It does not establish
matched inputs or native timing.

## Inline job and needs controls at hash 519

Copied source `5dd771cf` has the same production source/configuration as the
previous control. Native seed 7/default generator 101 and the actual 428→76
pair are retained. Observation off/on reproduce the original five ordinary
telemetry files byte-for-byte; the bed-only repeat also reproduces its previous
five files. The new copied build has zero warnings/errors; all six replays
complete through label 6000.

The needs mode restores seven supported fields: last ate, hunger, last food
attempt, last slept, sleep debt, sleep retry and pay retry. It copies literal
stored timestamps; sleep retry is the captured last attempt plus four, while
pay retry is already a deadline. This is not verified clock alignment or a
polling-label shift. The jobs mode adds current job, dropped job and the three
chooser timestamps. Before/after sidecars assert every assigned value, no
other observed fighter-field changes and unchanged RNG state at the boundary.
Native state/resume, route, mood, wages and other unselected history remain
modern. Writing current job 6 does not restore the native Guard activity.

Restoring needs changes this actor's later travel exposure, while adding the
supported job fields leaves this measured window unchanged:

| Actor 428 comparison, labels 5300–5450 | State 7 / resume 97 | State 6 / resume 98 |
|---|---:|---:|
| Native capture | 97 | 49 |
| Bed only | 0 | 141 |
| Bed + supported needs | 105 | 42 |
| Bed + supported needs and jobs | 105 | 42 |

Each row covers the same 151 observation labels; other states account for the
remainder. The native pointer, type and owner stay constant, and every label is
present and untorn. Native and needs-restored 428 both begin the Hatchery walk
at label 5031. Neither this label agreement nor the closer exposure counts
establish native route/timing parity or a campaign outcome improvement.

The supported job fields omit a separate native input: `ReturnToJobId`, state
`+0x68`, captured as 6 (Guard); saved room `+0x6C` is 1116. Current job `+0x64`
is a distinct field. Binary recheck of [004778E0](functions/004778E0.md)
confirms that the remembered job is tried after the dropped job and before the
happy chooser. Copied source `5dd771cf` has no remembered-job field or retry. Its function
record is corrected to PARTIAL; the whole-function confidence is unchanged.

A final diagnostic mode dispatches captured Guard through the existing managed
backend at that priority. At label 5002, both the ordinary jobs-restored replay
and the probe enter state 97. Their decisions differ: ordinary selection logs
`roll=28 how=rolled` and changes the happy-choice timestamp to 5001; the probe
logs an accepted remembered-job attempt with `roll=-1` and retains captured
timestamp 2584. Identical state ids therefore hide a real decision-path gap.
This is a counterfactual state-89 probe: native 428 was already travelling at
restore, and no corresponding native chooser event at label 5002 is established.

This scratch probe retries the fixed captured value on later chooser calls,
without native writer/pickup lifecycle or saved-post reuse. Conclusions use
only its first eligible state-89 call; later probe outcomes are not fidelity
evidence. The subsequent bounded implementation is in [guard-job-memory.md](guard-job-memory.md). Q710 remains
OPEN: no global exposure cause or matched-input outcome has been established;
unavailable routes, later birth populations and other Q719 inputs remain limits.

## Post-build paired control at bb2a395b

The accepted study uses copied source `bb2a395b`, native seed 7 and modern
generator seed 101, with native Dark Elf 428 paired to modern 45. All four managed replays
complete from the turn-5000 restore through label 6000. Plain and observed
controls reproduce all five ordinary telemetry files byte-for-byte; the scratch
changes are in Tools only, with no Simulation source change.

Both interventions restore the same supported inline needs/jobs and retain the
existing linked bed, at tile index 2976 versus native 2977. One deliberately
clears remembered job/room; the other restores captured Guard 6 and room tag 1116,
mapped to modern reference 22, the owned eastern Guard room at (43..45,51..53).
Boundary RNG and every recorded non-memory actor field agree; only four recorded
memory components differ. This asserts sidecar coverage, not equality of every
`CreatureState` or private field. Recorded non-memory actor fields remain equal
through label 5239. At 5240, the cleared control rolls Training (`roll=94`,
`how=rolled`, happy stamp 5239); restored memory retries Guard (`roll=-1`,
`how=remembered`, happy stamp 2584).

The ordinary current baseline already has remembered Guard 6 and
western room reference 28. The cleared control is counterfactual, not the
ordinary pre-fix production path. The measured window is labels 5300..5450:

| Actor 428 observation | 7 / resume 97 | 97 | 6 / resume 98 | 98 |
|---|---:|---:|---:|---:|
| Native | 97 | 1 | 49 | 4 |
| Restored-memory control | 82 | 1 | 64 | 4 |
| Ordinary current baseline | 66 | 1 | 81 | 3 |

The cleared control spends all 151 labels in Training. All 451 native frames
from 5000..5450 are untorn, with stable pointer/type/owner and remembered job 6,
room tag 1116. Native and modern start in state 6/resume 98, but route buffers
remain unavailable. Room mapping, other actors' reservations, later births and
other restore/history gaps preclude matched-input or global outcome claims.
The broader comparison is completed in the [fighter cohort](#fighter-cohort-at-7904875f);
its hybrid inputs still limit the verdict.

Evidence: `F:/DK2Evidence/studies/q710-built-comparison--2026-11-14--002`;
`analysis.json`, `decision-path-comparison.json` and `native-memory-observations.json`.
`manifest-verified.json` is authoritative: 113 entries, excluding itself.
`scope-assessment.json` corrects the immutable setup's idle-state claim;
`manifest-assessment.json` identifies the two retained invalid self-entries.
Trial 001 remains an aborted setup because its old bed-placement precondition
failed: this baseline already has a bed.

## Fighter cohort at 7904875f

The accepted hash-521 study uses source `7904875f`, eight native turn-5000
states and modern generator 101. Each state has ordinary, history and
history-memory arms, running through managed turn 6000. All 26 replays,
including observation controls, complete with exit 0. Plain/off/on controls
match all five ordinary telemetry files byte-for-byte. Only two scratch Tools
files change; production Simulation source is unchanged.

The initial cohort is 58 living owner-3 actors of types 6/7/10. Captured memory
is Guard 6 in 21 actors, naming valid rooms 1116/1117/1120; the other 37 have
None. All 49 nonnull native bed links are reciprocally checked. The history
arm copies literal needs stamps/debt, current/dropped job and chooser history;
sleep attempt plus four remains an adapter. History-memory adds captured
None/Guard and mapped room references. All 24 arm boundary assertions pass,
including unchanged RNG; history versus history-memory differs only in four
recorded memory components. These are selected-field assertions, not complete
native-state restoration. The added field restore excludes corpse/dying actors.

Stored-target exposure counts live owner-3 Goblin/Troll rows naming a living
owner-1 Guard (type 25), requiring positive signed health and state other than
216/217. Modern rows come from the actual live world each label; native rows
come from raw captures. The target window is 5001..6000; fighter-state counts
use 5300..5450. This measures stored targets, not attacks. These filters/windows
differ from historical carried-CSV metrics: no cross-version comparison follows.
Observation labels are not aligned execution timestamps.

| Mean observations with a Guard target | Native | Ordinary | History | History-memory |
|---|---:|---:|---:|---:|
| All eligible actors | 273.75 | 242.375 | 250.625 | 195.25 |
| Initially paired cohort | 266 | 239.5 | 232.25 | 193.625 |

| Native capture seed | Native | Ordinary | History | History-memory |
|---|---:|---:|---:|---:|
| 1 | 470 | 305 | 314 | 181 |
| 2 | 342 | 276 | 438 | 438 |
| 3 | 441 | 499 | 317 | 385 |
| 4 | 274 | 235 | 213 | 135 |
| 5 | 73 | 56 | 68 | 81 |
| 6 | 196 | 81 | 147 | 147 |
| 7 | 117 | 96 | 82 | 82 |
| 8 | 277 | 391 | 426 | 113 |

Adding memory lowers exposure in three cases, raises it in two and leaves
three unchanged. One modern generator and eight capture histories establish
descriptive differences, not a population estimate, regression/improvement
verdict or causal AI-rule explanation.

At the boundary, 22/58 actors mismatch bed presence, 53/58 mismatch bed tile
or presence, and 50/58 mismatch state/resume: 46 state ids differ and four
retain the same walking state with a different resume. No actor matches both
bed and state/resume, or even bed plus active state alone. The interventions
preserve the existing bed/state/route restoration while copying selected history.
The [compatible bed/state control](#compatible-bed-and-point-controls-at-d6d9b5f7)
completes that local test; unavailable native path buffers remain explicit.
Outside-cohort dying reservations, later births and other Q719/history gaps
remain limits. These mismatches block a matched-input verdict; they do not
prove the global cause. No production restorer or AI rule is changed.

Evidence: `F:/DK2Evidence/studies/q710-fighter-cohort--2026-11-14--002`;
`analysis.json`, `boundary-assessment.json` and `scope.json`.
`manifest-reviewed-input.json` verifies 332 files. No native game was launched.

## Compatible bed and point controls at d6d9b5f7

The accepted hash-521 study uses source `d6d9b5f7`, native seed 8 and modern
Xoshiro seed 101, with Troll 440 paired to modern 27. Nine accepted managed replays complete:
six main controls, one read-only destination observer and point flag off/on.
Plain/off/on main controls match each other. The destination observer and point
flag off each reproduce the native-bed run's five telemetry files byte-for-byte.
Main instrumentation changes two scratch Tools
files; the point counterfactual adds one scratch `CreatureJobSystem` change.
No production code is changed and no native game is launched.

This actor's restored pose, health and idle state 1/resume 1 match the capture;
it has no active modern route and is not held. No native state is forced. Only its literal supported
needs, current/dropped job 5, chooser stamps and remembered None are copied.
Native bed 1753 is object type 25, owner 3, linked back to 440, at tile index
3077=(27,61), effective centre (112640,251904). The baseline bed is 853 at
2789=(39,55). Sham releases/recreates that old tile; native-bed mode instead
recreates at the captured tile. Both create id 1048, advance next id to 1049,
retain 138 objects and preserve RNG. Managed bed PosX/PosY stay zero under the
existing tile-centre convention: this matches logical position, not native raw
position, pooled identity or the complete object state.

Across 1000 labels, sham versus baseline agrees on every recorded fighter
field other than bed id; the scorer's world metrics and RNG also match.
Native-bed versus sham likewise agrees except the changed bed tile. Bed location has no observed
effect through 6000 for this actor/control; this is not global bed irrelevance.
Native and history-restored state sequences agree at label 5001 (89) and 5002
(7/resume 63). A read-only probe then finds native room 1110, Workshop type 10,
with stored destination (124928,198656)=(30,48); modern JobTile is 2480=(30,49).
The [travel fields](travel-states.md) are decoded only while state 7 is active;
stale idle union bytes are not interpreted as a destination.

The flagged point control runs after the ordinary picker, retaining its draw.
It validates Workshop membership, furniture/navigation and absence of Obstacle,
then substitutes tile 2430=(30,48) only for 440's first call at managed tick
5001/replay label 5002. Normal routing follows; no path is injected. Flag on
records exactly one substitution at 5002 and its RNG after that step matches off.

| State-63 arrival label | Value |
|---|---:|
| Ordinary point / flag off | 5018 |
| Captured-point substitution | 5025 |
| Native capture | 5026 |

Substitution shifts seven labels and leaves one label different. It establishes
neither timing parity nor correct native RNG/picker inputs, steering, full body
or world fidelity. Modern Xoshiro 101, native picker/slab-chain inputs and the
rest of the world remain unmatched. Q710 stays OPEN. Next: match room-point
selection, RNG and slab-chain inputs for this stationary continuation, retaining
body/world/clock and broader-cohort limits; do not repeat bed relocation or
infer a one-tick steering correction.

Evidence: `F:/DK2Evidence/studies/q710-compatible-bed--2026-11-15--002`;
`analysis.json`, `interpretation.json`, `destination-probe/analysis.json` and
`point-control/analysis.json`. `manifest-reviewed-input.json` verifies 223 files,
excluding itself. Trial 001's rejected raw-position assertion is retained
outside the nine accepted replays.
