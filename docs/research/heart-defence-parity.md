# Level 5 Heart defence parity

Binary-first audit, 2026-11-01, DKII.EXE v1.70 through PyGhidra 12.1.4.
Baseline: master `38978009`, hash/save 360. Binary readings are STRONGLY
INFERRED behavior; captured transitions are CONFIRMED observations.

## Branch inventory before changing behavior

| DK2 branch | Remake comparison | Verdict |
|---|---|---|
| `004D96A4`, room ruin bit | `HeartDefenceSystem.Execute` uses the owner's ruined Heart | Equivalent for Level 5's single Heart; room/owner distinction remains |
| `004D96DC–004D96E7`, four-turn room-ID phase | Hard-coded phase zero | General mismatch; Level 5's captured room slot 1124 also has phase zero |
| `004D96F0–004D9834`, enemy search | First attackable hostile within strict reporting radius, room position | Position/radius match; (17,26) differs from Heart object (16,25). Ours evaluates attackability; native reads its cached bit (`0045A8B0`) |
| `004D9867–004D98D0`, gathering radius, Horny | Strict gathering radius, definition Horny exclusion | Same rule; no worker exclusion or head count |
| `004D98D8–004D9906`, Call to Arms, resolved Combat category, may-look gate | Same gates in `Gather` | Same ordinary Imp eligibility; physical-state approximations remain in the shared may-look predicate |
| `004D990C–004D9967`, health | Greater than level-scaled maximum / 4 | Same rule |
| `004D9976–004D99A2`, group 3, dropped job, existing walk to 254 | Same exclusions | Same rule; angry and group 7 creatures are eligible |
| `004D99C9`, route existence, then `004D9A1A`, actual route | One `TryRouteTo`, then `BeginTravel` | **Defect:** same-tile shortcut drops the point route; native `004D0F60` / `004CF490` retain it. Use `TryRouteToStance`; separate existence probe remains an adapter boundary |
| `004D99D2–004D99F7`, willing torturee kick to 135 | Omitted | Existing deviation ADR-0400; ordinary Level 5 Imps do not exercise it |
| `004D9A23–004D9A39`, run speed and reporting-counter reset | `Running = true`, `ReportTicks = 0` | Counter matches: creature +2CC is state +7E; run-speed override representation remains approximate |
| `0045BE80–0045BE86`, NonFighter near-Heart test | `NonFighter`: worker position to own Heart, each squared axis shifted by 12, unsigned distance < D000 | Same ordinary coordinate/radius rule; radius is sqrt(13) tiles, no Heart-damage trigger |
| `0045BF59` and hero path in `0045BDF0` | Enemy Imp candidates use shared selection anywhere; otherwise heroes withdraw/alarm outside near-Heart | Existing rules retained; [selector record](functions/0045BDF0.md) owns staging and release limits |
| `0045BFC9–0045C031` and `0045C1F5–0045C25A`, candidate Creeping weight | NonFighter omits this candidate weight | Pre-existing boundary, unexercised in the checked Level 5 native controls; bounded evidence and both paths: [record](functions/0045BDF0.md), Q713 |
| `0045D69D–0045D701`, chosen creature's score handoff | `Choose` retains strategy penalties/alignment discounts, except follower override | **Defect:** replace score with raw squared distance before route recheck and Heart scan; [record](functions/0045CB00.md) |
| `0045D907–0045D931`, Heart replacement eligibility | Existing `heartMayEnter` admits no candidate or an IsWorker candidate | Same rule |
| `004759A0–004759C2`, state 254 update | Wait matches; `Reporting` clears its counter before idle | Counter-retention boundary remains; no ordinary Heart decision difference established. See [record](functions/004759A0.md), Q713; no direct fight/flee choice in this handler |
| `004752A0`, category-26 leave | Clears RebelActive only | Same rule; no forced 199 exit |
| `0045EB20` → `0045F030`, acquired target | Scan enters 199; its handler runs that update and applies ordinary fear | Same ordinary dispatch order |
| `0045EC10` / `0045EC80`, NonFighter flee | A live non-Imp in either attacker array forces counter 3 | Same rule; a hero merely nearby is insufficient |
| `00460C50`, escape-tile test | `Cornered` calls shared length-tested reach with Imp 80 / other 0 flags | Native uses route existence and 80 for every type; separate known approximation, no demonstrated Level 5 causal verdict |

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

The seven new cases fail against unchanged master and pass with ADR-0617:

| Test | Cases |
|---|---:|
| `SelectAVictim_0045D69D_ReplacesStrategyScoreWithDistanceBeforeHeartScan` | 3 |
| `SelectAVictim_0045D69D_RemovesAlignmentDiscountBeforeHeartScan` | 2 |
| `SelectAVictim_0045D69D_HeroTakesNearerImpAndReportingImpFlees_0045EC80` | 1 |
| `HeartDispatch_004D9A1A_RetainsTheIntrudersPointWithinTheRespondersTile_004CF490` | 1 |

The existing near-Heart Imp test now checks the newest attacker's swing start
before the Imp's acquisition and flight, and no swing by the Imp. Its earlier
assertion depended on the attacker's erroneous Heart choice.

Verification: 4,241 Simulation cases pass; the final focused run passes 14 cases,
including the determinism pin. The other solution projects retain the same one
Level 18 `Level18NemesisGrantsLevelsAndNeverChoosesItsAllies` failure as master
(expected `(39,13)`, actual `(36,12)`). DK2.Data passes 502; Behavior passes 508,
skips 5 and has that one failure. The reported Q520 install failures did not recur
on either build. Solution and Unity adapter compile with zero warnings/errors.
Preserved logs: `F:/DK2Evidence/builds/heart-defence-fixed-h361--g38978009-wip--2026-11-01--001/verification/`
(`red-tests.log`, `simulation-final.log`, `review-tests.log`, `final-tests.log`,
`final-build.log`, `adapter-build.log`); source logs are retained.

### Outcome resyncs

Both builds ran 75 Level 5 and 40 Level 4 resync processes, all exit 0, with two workers,
`DOTNET_PROCESSOR_COUNT=2`, five generators/state and `--pickups stored`. Process
success does not establish a complete state restore; Level 4 has the Q714 limit below.

| Window and deaths per run | DK2 | Master 360 | Fixed 361 |
|---|---:|---:|---:|
| Level 5, 1401-3400: all keeper creatures | 13.38 | 6.40 | 9.90 |
| Level 5, 1401-3400: keeper Imps | 11.12 | 4.30 | 7.35 |
| Level 5, 1401-3400: heroes | 3.75 | 6.23 | 5.10 |
| Level 5, 3401-5000: all keeper creatures | 8.60 | 4.84 | 9.16 |
| Level 5, 3401-5000: keeper Imps | 5.80 | 2.60 | 7.20 |
| Level 5, 3401-5000: heroes | 5.00 | 5.44 | 4.44 |
| Level 4, 5001-6000: keeper Imps, all 40 runs | 6.50 | 10.07 | 11.93 |

The Level 5 first-window Imp and hero differences narrow; outcome parity remains Q712.
DK2-versus-fixed paired per-state sign-flip p values are 0.289/0.156 (Imps/heroes)
and 0.250/0.625 in the second window. The earlier hash 359 stored-pickup figures
were 4.33 Imp deaths and 6.27 hero deaths; the table uses a fresh master 360 control.

Level 4's raw increase remains visible. Its interpretation is confounded by the
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

| Transition | DK2 | Master 360 | Fixed 361 |
|---|---:|---:|---:|
| Entries into 254 | 15.00 | 70.80 | 12.87 |
| Exit to 199 | 3.67 | 49.00 | 0.60 |
| Exit to 203 | 5.00 | 2.33 | 2.60 |
| Exit to 1 | 3.00 | 4.93 | 4.67 |

The corrected entry count is closer to native; this does not establish outcome parity.
The earlier stored-pickup baseline is in
`F:/DK2Evidence/studies/l05-resync--2026-11-01--001/payload/README.md`.
Seeds 5/8 have 1400 snapshots but native captures end near 1463;
their remake resyncs can run, but cannot score a complete 1401–3400 window.
