---
systems: [tooling-process, campaign-triggers, creatures, navigation, combat, rooms]
---
# OpenKeeper PR relevance: 938 through 999

Reviewed 2026-11-06 against local HEAD `5b9edfb5`. GitHub's all-state list
contained 38 PRs above the previous marker 937, all open. This is a relevance
audit of their descriptions and production patches against our code, tests
and canonical research; no gameplay fixes or new native experiments were run.
The owner's unrelated Unity scene edit was excluded.

PR links below are provenance for leads, not independent proof of DK2 behavior.
Existing behavior evidence stays in the linked canonical notes and function
records. Fresh Ghidra checks cover the injury branch and breach/party arms
identified below. No OpenKeeper code was copied.

## What merits work

1. Injury speed/recharge penalties: built (ADR-0756, hash 481) with native turn
   phase and our timestamp sentinel mapping; see [the update record](functions/00488930.md).
2. Room condition 68 and breach events: these have shipped script uses; see
   [room conditions](room-conditions.md), [tile work](functions/00452F60.md)
   and [wall action](functions/00498BB0.md). The latter record's old reading
   omitted damage and assigned the supplemental events incorrectly.
3. Remaining missing conditions/events and lava speed: useful fidelity work,
   with the exact scope in the table. Several were already documented gaps.
4. Unique Portal layouts: a skirmish/multiplayer lead; campaign generation is
   already covered. The pattern semantics remain a research task, not permission
   to adopt the PR's layout table.

## All 38 PRs

“Covered” means the identified change is represented by our implementation;
it does not assert full original-versus-modern equivalence. “Gap” identifies
missing code; native details beyond our cited evidence still require research.
Source file names below are relative to `src/DK2.Simulation/` unless qualified.

| PR | Finding for this project | Evidence / next action |
|---|---|---|
| [938](https://github.com/tonihele/OpenKeeper/pull/938) | Partial: water covered; lava speed missing | `WaterSpeedRules.SpeedCap`; [steering section 7.5](steering-behaviours.md#75-the-creatures-tick-0x004d5a40-and-its-callers) already records the unbuilt lava arm. |
| [939](https://github.com/tonihele/OpenKeeper/pull/939) | Covered: party minimum walking speed | `HeroPartyRules`, `HeroWalkTests`; [party walk](hero-party-walk.md). |
| [940](https://github.com/tonihele/OpenKeeper/pull/940) | Covered: injured attack recharge | Covered since ADR-0756: `LowHealthSpeedRules.DelayRecharge`; [injury reading](functions/00488930.md). |
| [941](https://github.com/tonihele/OpenKeeper/pull/941) | Covered since ADR-0756: injured walking speed | `LowHealthSpeedRules.SpeedCap` in `CreatureMovementSystem.Sync`; same [injury reading](functions/00488930.md). |
| [942](https://github.com/tonihele/OpenKeeper/pull/942) | Covered: room hover text | `DK2.Presentation/WorldHoverText`, `WorldHoverTextTests`; [hover text](world-hover-text.md). |
| [943](https://github.com/tonihele/OpenKeeper/pull/943) | Covered: enemy terrain/door hover text | Same presenter and tests; [hover text](world-hover-text.md). |
| [944](https://github.com/tonihele/OpenKeeper/pull/944) | Covered: carried dying clock pause | `CreatureHealthSystem`, `CarryingTests`. |
| [945](https://github.com/tonihele/OpenKeeper/pull/945) | Covered: remaining health on Prison admission | `PrisonRules`, `PrisonTests`; [Prison](prison.md). |
| [946](https://github.com/tonihele/OpenKeeper/pull/946) | Covered: reject unreachable worker tasks | `ImpTaskSystem.Reaches/Flooded`, `ReachableTiles`, ADR-0453. |
| [947](https://github.com/tonihele/OpenKeeper/pull/947) | OpenKeeper console feature | Optional console spawn arguments do not expose a native rule gap. |
| [949](https://github.com/tonihele/OpenKeeper/pull/949) | Covered: action-point congregation comparison | `TriggerAreaConditions`, `TriggerAreaConditionsTests`; ours also counts objects. |
| [951](https://github.com/tonihele/OpenKeeper/pull/951) | Gap: captured-party condition 45 | `TriggerConditionEvaluator` declines it; condition 64 is different. [World evaluator record](functions/00518270.md); membership transitions need separate caller checks. |
| [952](https://github.com/tonihele/OpenKeeper/pull/952) | OpenKeeper transport fix | jME game-session UDP/TCP channel selection; our network milestone is M14. No matching transport code to correct. |
| [954](https://github.com/tonihele/OpenKeeper/pull/954) | Gap: global creature count, condition 35 | `TriggerConditionEvaluator` declines it; [world evaluator](functions/00518270.md). |
| [956](https://github.com/tonihele/OpenKeeper/pull/956) | Gap: dying-creature count, condition 74 | No `TriggerExecutor.PlayerCounter` arm; [dispatch coverage](trigger-dispatch-tables.md). |
| [958](https://github.com/tonihele/OpenKeeper/pull/958) | Gap: largest single room, condition 26 | No `PlayerCounter` arm; total room size 24 is a different condition. [Dispatch coverage](trigger-dispatch-tables.md). |
| [960](https://github.com/tonihele/OpenKeeper/pull/960) | Covered: angry-creature count | `TriggerExecutor.MoodCount`, `TriggerExecutorTests`. |
| [962](https://github.com/tonihele/OpenKeeper/pull/962) | Gap: angry event 11 producer | `CreatureAngerRules` explicitly records omission; enum/filter existence is insufficient. [Event sites](trigger-walkers.md#who-fires-what-2026-09-25-every-call-site-of-both-filters). |
| [964](https://github.com/tonihele/OpenKeeper/pull/964) | Deliberate OpenKeeper deviation | Do not adopt its destruction-count policy. Our condition 33 is still declined; the PR describes a native unconditional-true stub, which needs our own reread before changing that result. |
| [965](https://github.com/tonihele/OpenKeeper/pull/965) | OpenKeeper render-queue fix | Its jME queue repeatedly schedules camera initialization in the same frame. Our camera path uses `CameraPathPlayback` and Unity; no matching queue loop. |
| [966](https://github.com/tonihele/OpenKeeper/pull/966) | Covered: same-turn creature counting | `TriggerExecutor.CreatureCount` reads the current world list; `TriggerExecutorTests` cover ownership/type changes. No new Level19 playthrough was run. |
| [968](https://github.com/tonihele/OpenKeeper/pull/968) | Gap: player drop event 59 producer | Successful `HandSystem` releases do not fire it; [event sites](trigger-walkers.md#who-fires-what-2026-09-25-every-call-site-of-both-filters). Carrier-release routing remains a native lead. |
| [970](https://github.com/tonihele/OpenKeeper/pull/970) | Deliberate intended-behavior implementation | PR explicitly acknowledges native ownership-order suppression. Do not adopt it as faithful behavior; [object root / Q285](trigger-walkers.md). |
| [972](https://github.com/tonihele/OpenKeeper/pull/972) | Gap: level history condition 67 | `TriggerConditionEvaluator` declines it; [world evaluator](functions/00518270.md). PR's defeats-versus-attempts interpretation needs its own native read before implementation. |
| [975](https://github.com/tonihele/OpenKeeper/pull/975) | Gap: ordinary breach events | `TileWorkSystem.PostThreat` omits them; [tile applier](functions/00452F60.md) reread confirms the existing finding. |
| [976](https://github.com/tonihele/OpenKeeper/pull/976) | Gap: wall-action supplemental breach events | [Action 9](functions/00498BB0.md) is READ, not mirrored; fresh instruction check corrects post-damage ownership and recipients. |
| [978](https://github.com/tonihele/OpenKeeper/pull/978) | Covered for implemented creature slaps | `HandSystem` credits target owner; `TriggerExecutor` uses fixed numeric operand. Object/trap interactions remain separate scope. |
| [980](https://github.com/tonihele/OpenKeeper/pull/980) | Covered: happy-creature count | `TriggerExecutor.MoodCount`, `CreatureAngerRules.MoodOf`; no new conclusion about native roster edge cases. |
| [982](https://github.com/tonihele/OpenKeeper/pull/982) | Partial: door numeric modes covered; NONE unavailable | `TriggerExecutor.DoorCount` implements mode precedence. [Availability mode](trigger-dispatch-tables.md) remains declined. |
| [983](https://github.com/tonihele/OpenKeeper/pull/983) | Covered: research stand-off | `ResearchStep.StandPoint/StandOff` uses widths and heading; [Library states](library-states.md). PR's half-tile approximation adds no fidelity here. |
| [984](https://github.com/tonihele/OpenKeeper/pull/984) | Covered: due-roll Portal clock | `PortalSystem` resets every due roll; scripted arrival changes tally without clock reset. [Portal interval](portals-interval.md). |
| [985](https://github.com/tonihele/OpenKeeper/pull/985) | Partial: ordinary choice covered; unique layouts missing | `PortalChoiceRules`, `CreatureAttraction`, `PortalRules.FreeLairSpace`; [special arrival](functions/004DCF50.md) and [pattern predicate](functions/004DD130.md) remain READ. |
| [989](https://github.com/tonihele/OpenKeeper/pull/989) | Partial: trap counts/boulder covered; NONE unavailable | `TriggerExecutor.TrapCount`; same [availability gap](trigger-dispatch-tables.md) as doors. |
| [991](https://github.com/tonihele/OpenKeeper/pull/991) | Covered: creatures at/above a level | `TriggerExecutor.LeveledCreatureCount`, corrected byte-03 layout and tests; [semantics](trigger-semantics.md). |
| [993](https://github.com/tonihele/OpenKeeper/pull/993) | Covered: afraid event | `Combat/ThreatFleeRules`, `ThreatFleeTests`; [update record](functions/00488930.md). |
| [995](https://github.com/tonihele/OpenKeeper/pull/995) | Gap: room furniture/capacity condition 68 | No `PlayerCounter` arm. [Room conditions](room-conditions.md) and [capacity producer](functions/004E3ED0.md); do not substitute gameplay storage capacity. |
| [997](https://github.com/tonihele/OpenKeeper/pull/997) | Covered for existing HUD producers | `GuiButtonCommand`, `TriggerExecutor`, Unity bottom panel/HUD, `TriggerEventTests`. Networking additions are OpenKeeper-specific. |
| [999](https://github.com/tonihele/OpenKeeper/pull/999) | Gap: hunger-sated event 54 producer | `EatingRules.Swallow` omits it; [hunger record](functions/00477AD0.md). Already-zero dispatch remains a lead, not newly established evidence. |

## Limits that matter before implementation

OpenKeeper's new creature/button event patches decrement finite repeat counts.
Our [event-walker reading](trigger-walkers.md) and `TriggerExecutor` deliberately
do not; agreement on the event name does not establish the same execution rules.
Likewise, #995's spatial furniture query does not settle native room-list
membership or permission to relax our auto-placed/owner predicates.

The PRs generally report the extra global/party/history conditions and anger,
drop and hunger-sated events unused in their installed campaign corpus. That
is their corpus claim, not a new exhaustive scan here. Prioritize ordinary
injury behavior and known shipped room/breach scripts ahead of those extensions.
No replay outcomes, simulation hash version or gameplay acceptance changed.
