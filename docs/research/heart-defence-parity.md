---
systems: [heroes, combat]
---
# Level 5 Heart defence parity

Binary-first audit, 2026-11-01, DKII.EXE v1.70 through PyGhidra 12.1.4.
Baseline: master `38978009`, hash/save 360. Binary readings are STRONGLY
INFERRED behavior; captured transitions are CONFIRMED observations.

## Current branch inventory

| DK2 branch | Remake comparison | Verdict |
|---|---|---|
| `004D96A4`, room ruin bit | `HeartDefenceSystem.Execute` tests the room's ruin (`RoomRules.IsRuined`, ADR-0662) | Equivalent |
| `004D96DC–004D96E7`, four-turn room-ID phase | Hard-coded phase zero | General mismatch; Level 5's captured room slot 1124 also has phase zero |
| `004D96F0–004D9834`, enemy search | First attackable hostile within strict reporting radius, room position | Dispatch uses the room position; see the coordinate note below. Ours evaluates attackability; native reads its cached bit (`0045A8B0`) |
| `004D9867–004D98D0`, gathering radius, Horny | Strict gathering radius, definition Horny exclusion | Same rule; no worker exclusion or head count |
| `004D98D8–004D9906`, Call to Arms, resolved Combat category, may-look gate | Same gates in `Gather` | Same ordinary Imp eligibility; physical-state approximations remain in the shared may-look predicate |
| `004D990C–004D9967`, health | Greater than level-scaled maximum / 4 | Same rule |
| `004D9976–004D99A2`, group 3, dropped job, existing walk to 254 | Same exclusions | Same rule; angry and group 7 creatures are eligible |
| `004D99C9`, route existence, then `004D9A1A`, actual route | One `TryRouteToStance`, then `BeginTravel` | Built at hash/save 361 (ADR-0617): retain the point within a shared tile. `004CF490` guarantees one destination for a shared triangle; preprobe side effects and tile/triangle equivalence remain boundaries; [dispatch record](functions/004D9690.md) |
| `0047189E–004718A9` via `004B35C0`, destination guard | Heart responders check raw navigation mask `0x08` before the point-route shortcut | Built for Heart responders at hash/save 362 (ADR-0619); alarm/trap guards remain omitted. [Mapping and scope](functions/00471890.md) |
| `004D99D2–004D99F7`, willing torturee kick to 135 | Omitted | Existing deviation ADR-0400; ordinary Level 5 Imps do not exercise it |
| `004D9A23–004D9A39`, run speed and reporting-counter reset | `Running = true`, `ReportTicks = 0` | Counter matches: creature +2CC is state +7E; run-speed override representation remains approximate |
| `0045BE80–0045BE86`, NonFighter near-Heart test | `NonFighter`: worker position to own Heart, each squared axis shifted by 12, unsigned distance < D000 | Same ordinary coordinate/radius rule; radius is sqrt(13) tiles, no Heart-damage trigger |
| `0045BF59` and hero path in `0045BDF0` | Enemy Imp candidates use shared selection anywhere; otherwise heroes withdraw/alarm outside near-Heart | Existing rules retained; [selector record](functions/0045BDF0.md) owns staging and release limits |
| `0045BFC9–0045C031` and `0045C1F5–0045C25A`, candidate Creeping weight | NonFighter omits this candidate weight | Pre-existing boundary, unexercised in the checked Level 5 native controls; bounded evidence and both paths: [record](functions/0045BDF0.md), Q713 |
| `0045D69D–0045D701`, chosen creature's score handoff | `Choose` hands off raw squared distance before route recheck and Heart scan | Built at hash/save 361 (ADR-0617); [record](functions/0045CB00.md) |
| `0045D907–0045D931`, Heart replacement eligibility | Existing `heartMayEnter` admits no candidate or an IsWorker candidate | Same rule |
| `004759A0–004759C2`, state 254 update | `Reporting` retains the wrapping counter on expiry and compares unsigned | Built at hash/save 362 (ADR-0619); [record](functions/004759A0.md); no direct fight/flee choice in this handler |
| `004752A0`, category-26 leave | Clears RebelActive only | Same rule; no forced 199 exit |
| `0045EB20` → `0045F030`, acquired target | Scan enters 199; its handler runs that update and applies ordinary fear | Same ordinary dispatch order |
| `0045EC10` / `0045EC80`, NonFighter flee | A live non-Imp in either attacker array forces counter 3 | Same rule; a hero merely nearby is insufficient |
| `0045EC8C–0045EC8F`, pit-fighter fear counter | Ours clamps then clears it before returning false | Existing mismatch: native returns false without writes; no demonstrated responder case. [Record](functions/0045EC80.md), Q713 |
| `00460C50`, escape-tile test | `Cornered` calls shared length-tested reach with Imp 80 / other 0 flags | Native uses route existence and 80 for every type; separate known approximation, no demonstrated Level 5 causal verdict |

In Level 5, the room's position words `+0x27/+0x29` give tile `(17,26)`;
reporting and gathering distances use that tile's centre `(17.5,26.5)`. The Heart
object occupies tile `(16,25)`; the player's `DungeonHeartPosition` and the
NonFighter near-Heart test use that tile's centre `(16.5,25.5)`. The remake
uses `CentreFieldX/Y` for the former and `HeartSites` for the latter.

The hero fight-style score chooses a creature first; it must not become that
creature's distance when comparing with Heart pillars. Flanker/Support strategy
offsets and alignment discounts can move the result in opposite directions.
No radius, damage, flee threshold or RNG distribution is adjusted.

## Native transition controls

Source: `F:/DK2Evidence/captures/native/native-dk2runs--unknown-date--001/payload/level5-live1-seed2/seed02/level5.dk2cap`.
SHA-256-pinned extracts: `F:/DK2Evidence/studies/heart-defence-baseline-l5--2026-11-01--001/native-controls.json`.
At capture 2708, Imp 444 is in 254 with spell attacker 439 and melee attacker
437; at 2709 it is in 203 targeting 437. At 3043 it is in 254 with melee
attacker 434; at 3044 it is in 203 targeting 434. The normal scan and immediate
NonFighter fear check explain these exits without a special state-254 flee arm.

## Verification and measurements

At hash361, the seven new cases fail against unchanged master and pass with ADR-0617:

| Test | Cases |
|---|---:|
| `SelectAVictim_0045D69D_ReplacesStrategyScoreWithDistanceBeforeHeartScan` | 3 |
| `SelectAVictim_0045D69D_RemovesAlignmentDiscountBeforeHeartScan` | 2 |
| `SelectAVictim_0045D69D_HeroTakesNearerImpAndReportingImpFlees_0045EC80` | 1 |
| `HeartDispatch_004D9A1A_RetainsTheIntrudersPointWithinTheRespondersTile_004CF490` | 1 |

The existing near-Heart Imp test now checks the newest attacker's swing start
before the Imp's acquisition and flight, and no swing by the Imp. Its earlier
assertion depended on the attacker's erroneous Heart choice.

Hash361 verification: 4,241 Simulation cases pass; the final focused run passes 14 cases,
including the determinism pin. The other solution projects retain the same one
Level 18 `Level18NemesisGrantsLevelsAndNeverChoosesItsAllies` failure as master
(expected `(39,13)`, actual `(36,12)`). DK2.Data passes 502; Behavior passes 508,
skips 5 and has that one failure. The reported Q520 install failures did not recur
on either build. Solution and Unity adapter compile with zero warnings/errors.
Preserved logs: `F:/DK2Evidence/builds/heart-defence-fixed-h361--g38978009-wip--2026-11-01--001/verification/`
(`red-tests.log`, `simulation-final.log`, `review-tests.log`, `final-tests.log`,
`final-build.log`, `adapter-build.log`); source logs are retained.

Hash362 verification: all 4,249 Simulation cases pass. Four regressions fail against
`d04eaa58` production sources in an isolated scratch copy (16 pass of 20); the
working tree was not reverted. The full solution and Unity adapter build with
zero warnings/errors. Data passes 502; Behavior passes 508, skips 5 and retains
the same one Level 18 failure as master. Oracle passes 3/skips 25; Core 60,
Assets 66, Convert 175, Presentation 1,396, AI 412 and Tools 102 pass.
The final focused run passes 21 cases, including the pin. The reported Q520
failures do not recur on master or this build. SHA-256-verified log copies:
`F:/DK2Evidence/builds/q712-reporting-h362--gd04eaa58-wip--2026-11-01--001/verification/`
(build, adapter, red-final, scratch-red, final-tests, focused-final logs and scratch
provenance); source logs are retained.

### Outcome resyncs

Hashes 360 and 361 each ran 75 Level 5 and 40 Level 4 resync processes, all exit 0, with two workers,
`DOTNET_PROCESSOR_COUNT=2`, five generators/state and `--pickups stored`. Process
success does not establish a complete state restore; Level 4 has the Q714 limit below.

| Window and deaths per run | DK2 | Master 360 | Fixed 361 | Fixed 362 |
|---|---:|---:|---:|---:|
| Level 5, 1401-3400: all keeper creatures | 13.38 | 6.40 | 9.90 | 9.90 |
| Level 5, 1401-3400: keeper Imps | 11.12 | 4.30 | 7.35 | 7.35 |
| Level 5, 1401-3400: heroes | 3.75 | 6.23 | 5.10 | 5.10 |
| Level 5, 3401-5000: all keeper creatures | 8.60 | 4.84 | 9.16 | 9.16 |
| Level 5, 3401-5000: keeper Imps | 5.80 | 2.60 | 7.20 | 7.20 |
| Level 5, 3401-5000: heroes | 5.00 | 5.44 | 4.44 | 4.44 |
| Level 4, 5001-6000: keeper Imps, all 40 runs | 6.50 | 10.07 | 11.93 | 11.93 |

All 75 hash362 Level 5 runs exit 0. Their 150 `creatures.csv`/`events.tsv` files
are SHA-256 byte-identical to the archived hash361 controls. Scores and comparison:
`F:/DK2Evidence/studies/heart-defence-q712-h362-l5--2026-11-01--001/`
(`score-r1400.out`, `score-r3400.out`, `matched-output-comparison.json`).
The Level 5 first-window Imp and hero differences narrow against hash360;
ADR-0619 changes no output in these bounded controls. Outcome parity remains Q712.
DK2-versus-fixed paired per-state sign-flip p values are 0.289/0.156 (Imps/heroes)
and 0.250/0.625 in the second window. The earlier hash 359 stored-pickup figures
were 4.33 Imp deaths and 6.27 hero deaths; the table uses a fresh master 360 control.

All 40 hash362 Level 4 runs exit 0; all 80 creature/event files are SHA-256
byte-identical to the archived hash361 controls. Scores and comparison:
`F:/DK2Evidence/studies/heart-defence-q712-h362-l4--2026-11-01--001/`
(`score-r5000.out`, `matched-output-comparison.json`,
`resync-contamination-analysis.json`; native control inherited from hash361).
The sensitivity recheck identifies the same eight generator-404 terminal cases;
hash361/362 means are both 11.925 for all 40 runs and 10.03125 for the 32-pair
subset. Q714 and the prior-history
terminal states persist unchanged. Level 4's raw increase against hash360 remains visible. Its interpretation is confounded by the
[Heart/terminal-state restore omission](resync-replay.md#heart-and-terminal-state-restoration-q714).
The loss before 5000 itself is unexplained and may be a regression. A descriptive
sensitivity analysis retains 32 pairs without detected pre-resync terminal state:
master 9.91, fixed 10.03 Imp deaths/run (exact 9.90625/10.03125), mean change 0.125,
paired sign-flip p = 0.875. This outcome-selected subset is not an unbiased regression
check. The full 40 paired change is 1.85, p = 0.1796875; primary scorer files exclude
nothing. Q710/Q714 remain open; no regression-free verdict follows.

Level 5 score files (`score-r1400.out`, `score-r3400.out`) are in the
`F:/DK2Evidence/studies/heart-defence-baseline-l5--2026-11-01--001/` and
`F:/DK2Evidence/studies/heart-defence-fixed-l5--2026-11-01--001/` directories.
Level 4's `score-r5000.out` is in the corresponding baseline/fixed-l4 directories;
its secondary analysis is `resync-contamination-analysis.json` in the fixed-l4 directory.

### Reporting-state transitions

State-254 transition counts in 1401-3400, seeds 2/3/7: means per native state
and per remake run (five generators/state). Source with CSV hashes:
`F:/DK2Evidence/studies/heart-defence-fixed-l5--2026-11-01--001/state-transitions.json`.

| Transition | DK2 | Master 360 | Fixed 361 | Fixed 362 |
|---|---:|---:|---:|---:|
| Entries into 254 | 15.00 | 70.80 | 12.87 | 12.87 |
| Exit to 199 | 3.67 | 49.00 | 0.60 | 0.60 |
| Exit to 203 | 5.00 | 2.33 | 2.60 | 2.60 |
| Exit to 1 | 3.00 | 4.93 | 4.67 | 4.67 |
| Exit to 6 | 2.00 | not reported | 4.40 | 4.40 |
| Exit to 8 | 1.00 | not reported | 0.53 | 0.53 |

Hash362 transition source: `state-transitions.json` in the hash362 Level 5 study
linked above. Native has 45 entries, 44 completed exits and one censored stand.
The corrected entry count is closer to native; this does not establish outcome parity.
The earlier stored-pickup baseline is in
`F:/DK2Evidence/studies/l05-resync--2026-11-01--001/payload/README.md`.
Seeds 5/8 have 1400 snapshots but native captures end near 1463;
their remake resyncs can run, but cannot score a complete 1401–3400 window.

## Q712 state-254 audit (2026-11-01)

The current inventory above includes the reviewed hash362 corrections and
remaining adapter boundaries.

The mask is value `0x08` (bit index 3), not mask `0x100`.
`0044FE02-0044FE26` selects 8 for Solid terrain; doors and room geometry
use other cache values (see [structure-blocks](structure-blocks.md#54-the-navigation-cache-and-all-known-writers-q209)).
The Heart platform is not rejected by this mask merely because it is a platform.

Category 26 has no enter or update hook; its leave hook `004752A0` only clears
RebelActive. The state-table registration and category initializer establish
this; the unused per-state pointer fields are not enter/exit callbacks.
`004759A0` has no target or fear branch. `00476FC0` converts its zero return
to state 1. Acquisition by `0045CB00` / `0045A550` enters 199 before dispatch;
`0045EB20` tails into `0045F030` in the same update. The escape check
`0045F387` must find an escape before `0045EC80` can force a NonFighter with
a live non-Imp attacker to 203. Otherwise a valid acquired target stays 199
or takes the ordinary approach (state 8). No received-hit or swing gate exists
in the acquisition throttle or the reporting handler.

Heart dispatch enters state 6 resuming 254, after the eligibility and route
gates in the current inventory; arrival (`00470C80`) enters 254. Alarm and
trap responders can also resume 254. A standing reporter remains eligible
for another Heart dispatch; a creature already walking to 254 is excluded.
The sender clears the reporting counter only after a successful point route.


The complete native exit control is
`F:/DK2Evidence/studies/q712-native-controls--2026-11-01--001/native-exits.json`
(capture SHA-256 values included). Across seeds 2/3/7, all nine exits to 1
increment 30 to 31. All fifteen exits to 203 have incoming attackers in the
prior frame and counter 3 afterward. All eleven exits to 199 finish with counter
0; ten have empty prior-frame attacker arrays. Seed 3 Imp 455 at 2600 instead
replaces prior spell attacker 431 with melee attacker 432 by the end of the tick.
These are CONFIRMED observations, not a reconstruction of within-tick ordering.
The binary's ordinary acquisition/fear paths explain the outcome classes;
which same-update event supplied or cleared an attacker needs a matched control.

The same 6,000 frames contain 13,807 observations of hero types 14/16/18 within
the reporting radius, none on Solid terrain. This is a terrain-derived bounded
check, not a captured raw navigation cache. All three exit-control Heart room tags are 1124
at `(17,26)`. All ten Level 5 tick-1400 exported states also have Heart room slot
1124, phase zero; input SHA-256 values and results are in
`F:/DK2Evidence/studies/q712-native-controls--2026-11-01--001/level5-phases.json`.
The destination gate is therefore not established as the cause of
the Level 5 target-acquisition gap, and entry-count parity remains unestablished.


## Redispatch follow-up (Q712, 2026-11-01)

Measured capture/output counts for ticks 1401-3400, native seeds 2/3/7 and
five remake generators per native state. Fifteen hash362 resyncs add only
`--jobs`; all 30 creature/event outputs match their prior controls by SHA-256.
The resume state splits the visible 254-to6 exits:

| Resume state | DK2 total / 3 runs | Remake total / 15 runs | DK2 mean | Remake mean |
|---|---:|---:|---:|---:|
| 254, redispatch | 6 | 64 | 2.00 | 4.27 |
| 202, withdrawal | 0 | 2 | 0.00 | 0.13 |
| 251, alarm | 0 | 0 | 0.00 | 0.00 |

The excess is chiefly redispatch, but the nine native timeouts do not establish
that a standing responder fails a route or eligibility test. Across 69 room
phases during those stands, neither adjacent captured frame contains an active,
attackable hostile inside the strict reporting radius. The check uses active
slots, the captured alliance byte, cached attackability and exact positions;
all six successful native redispatches are positive controls with an intruder
present and the counter reset to zero.

The wider phase check freezes the previous frame's fields before advancing:
88 phases begin with an observed standing Imp. Six redispatch, 68 remain in 254,
and two become idle; the latter 70 have no intruder in either adjacent frame.
The other 12 leave for 199 (4), 203 (6), or 8 (2). No still-standing case with an
observed qualifying intruder demonstrates refusal by `004D0E60`, `0045A6D0`,
or `004888C0`. These snapshots do not expose every within-tick intermediate.
Fresh arrival and redispatch within one frame are outside this visible-exit count.

Binary reread: `004888C0` is the existing Call to Arms predicate, with no special
254 clause; `0045A6D0` has no special standing-254 clause either. The preprobe
`004D0E60` uses entity endpoints/options; this does not establish downstream
route equivalence. No additional behavior change follows. The subsequent
[hero-exposure comparison](#hero-exposure-and-earliest-comparable-divergence-q712-2026-11-01)
follows this lead; general adapter boundaries remain Q713.

Evidence: `F:/DK2Evidence/studies/q712-redispatch-split--2026-11-01--001/`:
`split.json`, `native-opportunities.json`, `native-positive-controls.json`,
`all-native-standing-phases-v2.json`, `verification.json`, `inputs.json`,
verified scripts and per-run commands.
The first `all-native-standing-phases.json` is invalid: it retained live decoder
snapshots. Use v2, whose six redispatches match the positive control.

## Hero exposure and earliest comparable divergence (Q712, 2026-11-01)

Measured: native seeds 2/3/7 and five hash 362 generators per seed,
ticks 1401-3400, exact positions about room tile (17, 26), centre (17.5, 26.5).
This is distinct from the player Heart-position tile (16, 25). Each squared axis
displacement is shifted by 12 before addition, strict sum < 65536, as 004D9690.
All 45 original creature/event/replay outputs are SHA-256 identical under a
scratch CLI tracer; no simulation source changed. Complete per-run state/target
counts, visit lengths, retargets and end causes are in the add-only study:
`F:/DK2Evidence/studies/q712-hero-exposure--2026-11-01--001/` (`README.md`,
`summary.csv`, `visits.csv`, `exposure.json`, commands, scripts and hashes).

| Native seed | Native hero-ticks / qualified | Remake hero-ticks, generators 101/202/303/404/505 |
|---|---:|---|
| 2 | 4964 / 4905 | 4289, 1583, 4800, 1997, 1469 |
| 3 | 4273 / 4221 | 1320, 1362, 3292, 3757, 2719 |
| 7 | 3744 / 3661 | 4824, 3281, 2194, 1174, 2753 |

Mean presence is 4327.00 native vs 2720.93 remake; qualified presence is 4262.33
vs 2683.73. Mean target-ticks are 3298.67 pillar/790.00 Imp/66.67 other/171.67
none native, vs 1607.00/790.60/90.60/232.73 remake. Thus excess *total*
remake exposure is not the redispatch explanation. Exposure conditional on a
standing responder remains open. Spatial visits end on leaving, collapse or
the window boundary; retargets are recorded inside visits, not called deaths.
Collapse is not a proved permanent death. State 199 dominates inside on both
sides; complete joint state/target distributions are in exposure.json.

Tick 1400 already has non-worker state/route differences under the documented
partial restore (resync-replay.md). A new party supplies a cleaner comparison:
seed 2 hero 439 is born 1514 at (17, 42), job 19/player 3 on both sides. DK2 enters 269,
then 210 at 1522, 263 at 1523 and 264; ours enters idle then fights Imp 440 at 1515.
Ours first enters the radius at 1666; DK2's first entry is hero 438 at 1750, with 439
still fighting 440 outside. The existing gate-arrival omission precedes this
arrival gap; its contribution to Q712 has not been isolated from later RNG,
combat/action history and route differences.

STRONGLY INFERRED binary/source comparison, before any production change:

| Address / branch | Comparison and remaining mismatch |
|---|---|
| 00519F90 / 004626A0 | The subsequent full flag read corrects the gate interpretation and builds the non-gate party lifecycle; see [party arrival](party-arrival-lifecycle.md), ADR-0622. |
| 0045CB00, 0045D907 / 0045DAB7 | No candidate or worker admits Heart scan; strictly nearer pillar replaces running plain-distance best. Current Choose/ScanHearts agrees on these branches; no matched selector error found. |
| 00462490 / 00484940 | Job 19 routes to a random Heart-room point, resume 178; 178 returns 0. Current HeroJobRules agrees on this dispatch; existing random-point/route adapters remain as documented in hero-party-walk.md. |
| 00498E30 | Successful ordinary pillar blow ends its action without a pillar-specific hero-state transition; ours likewise retains the combat target. No new mismatch established. |
| 0045E9F0 / 004B5D40 / 00489FE6 | Previous pose +F6 is refreshed after scan and before state dispatch/mover. Ours stamps after combat dispatch, before ordinary movement. Ordinary arrival is scanned next update in both; the [Portal relocation control](q712-atomic-inputs.md#portal-relocation-scan-reference-q731) isolates the separate Q731 write, now built at hash 388 (ADR-0637); [paired outcome verification](q712-atomic-inputs.md#paired-outcomes-387-and-388) is complete; Q712 remains open. |
| 00460C50 | Native class 0 enemy targeting self can hold a neighbouring tile without a health/state test. HeldByAnAttacker additionally skips IsDown; the pre-existing route existence/options mismatch remains Q713. No collapsed hero retaining an Imp target was found in these captures. |

This measurement pass made no production change; 362 remains its baseline.
The follow-up [party-arrival lifecycle and matched results](party-arrival-lifecycle.md)
records ADR-0622/provisional hash 363, the corrected non-gate selection and remaining Q712,
Q713/Q714 and Q718 boundaries. No additional owner-play question arose.
