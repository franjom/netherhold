# Campaign player stages 1-2 implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow AGENTS.md's reviewer/documenter passes before commits.

**Status:** proposed execution plan, 2026-11-07. Design approved by the owner;
implementation and execution-method selection have not started.
**Goal:** attempt Level 1 unattended through a player-visible interface, preserve
the input journal and produce a truthful, replayable completion/failure report.
**Architecture:** extend the existing scenario loop. Put engine-neutral player
contracts in DK2.Simulation, visibility projection in DK2.Presentation, reusable
controller logic in DK2.AI, and scheduling/reporting in DK2.Harness. Do not change
the existing privileged `ScenarioController`/Classic keeper contract.
**Tech Stack:** existing C#, Unity-loadable netstandard2.1 assemblies, .NET 10
harness, xUnit 2/VSTest, existing YAML scenarios and input journal. No packages,
external transport, model provider, editor automation or Unity dependency added.
**Spec:** [approved design](2026-11-07-agentic-campaign-playtesting-design.md).

## Global constraints

- "Do not create a competing scenario runner, journal format or simulation loop."
- "No controller writes `WorldState`, teleports things or invokes private rule APIs."
- "Agent output is not assumed deterministic." Reproduce applied input frames.
- "A controller limitation is a valid diagnostic deliverable, not Level 1 acceptance."
- "Declare stage 2 accepted only once an unattended win and its journal replay are demonstrated."
- No simulation behavior/hash change is intended. A changed determinism pin is
  an investigation, not permission to update it. Mechanic fixes are separate tasks.
- Shared assemblies remain Unity-loadable. Invoke the repository's .NET API and
  refactoring skills when an implementation task changes a covered interface.
- First scope is headless command play. Desktop isolation, virtual UI and actual
  visual/audio output remain mandatory later design stages, not covered here.

## .NET skills and optimization gate

Every implementing/reviewing agent must read the applicable skills, not merely
cite their names. Use the [project guidance](../../.agents/skills/README.md)
and Context7 for current library/API documentation as required by AGENTS.md.
Optimization is an explicit priority throughout implementation and review.

| Skill | Required use when its trigger applies |
|---|---|
| `analyzing-dotnet-performance` | Review new observation, capability, scheduling, strategy and serialization hot paths before each owning task's gate; prioritize measured CPU/allocation costs. |
| `run-tests` | Before executing or prescribing .NET test commands/filters. |
| `test-value-evidence` | Establish meaningful revision-bound RED/GREEN evidence for the identified behavior/regression change. |
| `csharp-refactoring` | Before behavior-preserving C# extraction, consolidation, renaming or other refactoring; separate behavior changes. |
| `dotnet-breaking-changes` | When changing covered public/friend/generated contracts; check overloads, defaults, nullability and Unity consumers. |
| `msbuild` | For project/import/target changes or build/restore failures and supported build-performance investigation. |
| `microbenchmarking` | When BenchmarkDotNet is used or an isolated .NET microbenchmark is justified; keep full-run profiling separate. |
| `vectorization` | Only for measured SIMD work, with contract/tail/fallback equivalence and performance evidence. |

Measure before optimizing. Record first-capture and steady-state observation
latency, bytes allocated per decision/tick, observation size, GC counts and total
run overhead against the same applied-input replay without player observation
or diagnostics. Include map/population/visibility, decision cadence, source/build
identity, settings and sample scope. Run comparisons serially; noisy timing checks
remain opt-in, while stable allocation/complexity regressions get focused guards.
Derive numeric budgets from a reviewed baseline before claiming an improvement.

Prioritize avoiding repeated full-map scans, repeated sorting, boxing, hot-path
LINQ/closures, per-tick JSON/strings, unnecessary hashes and redundant snapshots.
Capture on decision boundaries, bound batches and buffers, and keep invariant
identity/file hashing out of the tick loop. Profile measured algorithmic costs
as well as the skill's API-pattern findings; a clean scan is not proof of speed.
Retain immutable published observations: cached/pooled scratch must not escape
or be reused while a controller still owns a snapshot. Do not trade away typed
values, visibility, current capability checks, evidence or thread safety for speed.
Use only APIs supported by the actual target framework/runtime; a .NET 10 harness
optimization must not leak unsupported APIs into Unity-loadable netstandard2.1.

Review must include the chosen skills, prioritized findings, before/after
measurements and replay/hash equivalence for every optimization. Report unresolved
costs without claiming they are fixed. Existing Unity frame targets remain in
[PERFORMANCE.md](../PERFORMANCE.md); headless figures do not certify those targets.

## Review focus

1. Hidden-state changes must not change serialized observations, offers or refusals (task 2).
2. Reused native entity slots must not let an old handle target a different thing (task 3).
3. Retried requests and end-of-run queued actions must never execute twice or disappear silently (tasks 3-4).
4. Long legitimate work must not be called an engine bug; no progress must still terminate (tasks 5-6).
5. A stale DLL, dirty tree, partial journal or skipped installed test must not receive an acceptance claim (tasks 4, 6-7).

## Entry points, dependencies and file ownership

Current source baseline is `022cedf1`; recheck HEAD before execution.
`ScenarioRunner.RunOnLevel` owns the only tick loop and queues controllers after
completed ticks for the next input frame. `RunOptions` is in `RunMonitor.cs`.
`WorldView.CanShowTile`, `Tiles`, `Creatures`, `Objects`, `PlayerInputSnapshot`
and the presentation HUD models are the visibility/input references.
`HumanCommandPolicy` is in DK2.Harness, not DK2.Presentation.

| Owner | New or changed files | Responsibility |
|---|---|---|
| Task 1 | `src/DK2.Simulation/Players/PlayerContracts.cs`, `PlayerObservation.cs`, `PlayerAction.cs`; `src/DK2.AI/Players/IPlayerController.cs` | Detached contract and controller boundary |
| Task 2 | `src/DK2.Presentation/PlayerObservationBuilder.cs`; `tests/DK2.Presentation.Tests/PlayerObservationTests.cs` | Visibility-safe projection and capabilities |
| Task 3 | `src/DK2.Harness/PlayerSession.cs`; `src/DK2.Presentation/PlayerActionAdapter.cs`; `tests/DK2.Behavior.Tests/PlayerSessionTests.cs` | Action admission, deduplication, handles and receipts |
| Task 4 | `src/DK2.Harness/PlayerRunCoordinator.cs`, `PlayerRunManifest.cs`; existing `ScenarioRunner.cs`, `RunMonitor.cs`, `JournalHeader.cs`; `tests/DK2.Behavior.Tests/PlayerRunTests.cs` | Loop integration, bounded advancement, identity and replay |
| Task 5 | `src/DK2.AI/Players/CampaignPlayer.cs`, `PlayerActionRoutines.cs`, `CampaignPlayerOptions.cs`; `tests/DK2.AI.Tests/CampaignPlayerTests.cs` | Observation-driven strategy and recovery |
| Task 6 | `src/DK2.Harness/PlayerRunReport.cs`; existing `HarnessCommandLine.cs`, `Program.cs`, `ScenarioReportWriter.cs`; `tests/DK2.Behavior.Tests/PlayerRunReportTests.cs` | CLI, limits, evidence and truthful outcomes |
| Task 7 | `scenarios/campaign-player/level1.yaml`, `tests/DK2.Behavior.Tests/CampaignPlayerLevel1Tests.cs`, harness README | Installed acceptance and delivery |

No project dependency changes are needed: Presentation and AI already reference
Simulation; Harness already references both. Avoid adding AI to Presentation or
Presentation to AI. Tests may add small private fixtures inside the owning test
file; do not turn the existing data-only TestSupport into a dependency hub.

## Shared verification and preparation

Use an isolated execution worktree under the repository's worktree workflow;
do not reuse the active Q712 or performance checkout or switch shared master.
Preserve the owner's Dungeon scene. Planning itself changes documentation only.

Repository tests are SDK-style xUnit 2 under VSTest, with no test overlay or
`global.json` MTP selection. For a task's class, run its named project with
`dotnet test <project> -c Release --filter FullyQualifiedName~<class>`; substitute
the exact project/class from that task. This builds by default. RED must show
the intended assertion failure once the contract compiles, not only a missing
type. GREEN must include positive controls. Retain counterfactual evidence.

After each task, run the reviewer, fix objections, run the documenter and inspect
its edits before staging named paths and committing. Use `docs/HANDOFF.md`'s
verification list for the final gate. No tests have been executed for this plan.

### Task 1: detached player protocol

**Files:** task 1 ownership above; test `tests/DK2.Simulation.Tests/PlayerContractsTests.cs`.
**Interfaces produced:** namespace `DK2.Simulation.Players`:
`PlayerRunId(string Value)`, `PlayerActionId(long Value)`,
`PlayerThingHandle(string Value)` and `PlayerRevision(long Value)` are validated
readonly value types; empty IDs/handles and nonpositive action/revision values
are rejected. `PlayerStamp` contains run ID, `PlayerId`, `GameTick` and revision.
Use typed `PlayerGold` and `PlayerMana` values, not interchangeable quantities.

`PlayerObservation` contains Stamp, Phase (Intro/Playing/Ended), own terminal
state, public resources, displayed owned-room/creature summaries, visible tiles,
opaque thing handles, visible notices, capability offers and action receipts.
DTO collections own copied, read-only storage; no world, database, deferred
enumerable, delegate or mutable engine record crosses this boundary.
Version the contract with `PlayerObservation.SchemaVersion = 1`.

`PlayerAction` is a closed discriminated record family: TagArea, BuildRoom,
SelectPanel, PickUp, DropAt, Slap, ActivateSpecial, CreateImpAt and SkipIntro.
Areas use TilePos corners, rooms use RoomTypeId, panels use GuiButtonTarget and
things use PlayerThingHandle. No raw entity slots or arbitrary command types.
`PlayerRequest(PlayerStamp BasedOn, PlayerActionId Id, PlayerAction Action)`;
`PlayerReceipt` includes the ID, status and optional scheduled input tick.
Statuses: Queued, Submitted, Refused, Cancelled; reasons are public-safe enums.
Submitted means present in an input frame, not successful work completion.

In `DK2.AI.Players`, define
`IPlayerController.Decide(PlayerObservation observation) -> PlayerDecision`;
`PlayerDecision` owns an action list, positive `AdvanceTicks` and a short reason.
Controller construction receives run/player identity; it never receives a world.

- [ ] Add tests `ObservationOwnsItsCollections` (mutating constructor inputs cannot change the snapshot), `ObservationHasNoPrivilegedReferences` (recursive field/type audit), and typed identity validation. Include these exact contract assertions:

```csharp
Assert.Throws<ArgumentException>(() => new PlayerRunId(""));
Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerActionId(0));
Assert.NotEqual(new PlayerRunId("run-a"), new PlayerRunId("run-b"));
```

- [ ] Add a minimal immutable constructor/record skeleton, then run `PlayerContractsTests` in DK2.Simulation.Tests and retain the intended RED assertions.
- [ ] Implement copying, validation and the closed action family; GREEN includes valid empty/no-action observations and distinct run identities.
- [ ] Build the solution and Unity compile project; check the architecture guards before the task review/documentation/commit cycle.

### Task 2: visibility-safe observations and capabilities

**Consumes:** task 1 types, existing presentation models.
**Produces:** `PlayerObservationBuilder(GameDatabase database, PlayerId player)`
in DK2.Presentation; `Capture(WorldState completedWorld, PlayerStamp stamp,
IReadOnlyList<PlayerReceipt> receipts) -> PlayerObservationCapture`.
Define PlayerObservationCapture in the same file: Observation is the task 1 DTO;
Targets is an owned read-only handle-to-PlayerInputTarget dictionary. The host
retains Targets; only Observation reaches IPlayerController. Every handle is
scoped to its run/revision and generated from already-admitted targets.
The host calls Capture serially at completed boundaries, never concurrently with
world mutation. Capture may use existing view caches but must not alter the hash.

Use rendered/admitted tile and thing projections, own HUD/overview information
and recipient-visible message content. Do not copy KeeperView and redact it
afterwards. No enemy inventory, exact hidden statistics, task lists, future
triggers, global room ordering or diagnostic progress appears in the DTO.
Render-facing entity handles are per observation and resolved only by the host.
Own offscreen/held creatures use the ordinary owned overview/hand information;
enemy things must pass the existing visibility/selection path, not merely a
known tile check. If an observation cannot safely represent something, omit the
capability and report that coverage limit through diagnostics.

Define capabilities per action family and visible target/area. Return displayed
room/spell availability and prices through existing panel models. Include only
the player's visible speech/notification identifiers or resolved text; never raw
message payloads naming otherwise hidden objects. The host's full-state diagnostic
stream stays out of this interface, including error reasons and action hints.

- [ ] In `PlayerObservationTests`, create paired synthetic worlds differing only behind fog: enemy creature/gold, secret door, room capacity, trigger state and another player's messages. Assert field-for-field identical detached observations and capabilities.
- [ ] Add positive controls revealing a target through the existing view path, own held-creature visibility, discovery changes and empty message history. A global count or hash must not expose hidden changes.
- [ ] Run the Presentation test project/class for RED, implement Capture, then GREEN. Mutate the original world after capture and assert the old observation is unchanged.
- [ ] Assert StateHash and simulation RNG state are unchanged by repeated capture; retain existing fog/view/input tests in the focused run.
- [ ] Establish the observation performance baseline on small and large/fog-heavy synthetic worlds. Measure first and repeated capture, capability generation and allocation; apply the optimization gate and preserve the detached-snapshot mutation test after any reuse/caching change.
- [ ] Review the correctness and performance evidence, document and commit.

### Task 3: bounded action admission and receipts

**Consumes:** observations/capabilities and existing human command constructors.
**Produces:** `PlayerSession(PlayerRunId runId, PlayerId player,
PlayerActionAdapter adapter)` in Harness;
`Observe(PlayerObservationCapture capture)`;
`Submit(PlayerRequest request) -> PlayerReceipt`;
`Drain(GameTick appliesAt) -> IReadOnlyList<PlayerCommand>`;
`Cancel() -> IReadOnlyList<PlayerReceipt>`.
Expose immutable receipts for the next observation. PlayerActionAdapter is
defined in Presentation with
`Resolve(PlayerObservationCapture capture, PlayerAction action)
-> PlayerActionResolution`. Resolution holds commands or a public-safe refusal.
This adapter uses only the admitted observation and target map; it never gets a
live world. Keep handle resolution out of controller DTOs.

Only submit actions from the current observation, with matching run/player and
phase. Refuse stale revisions, expired handles, future revisions and forbidden
targets before any hidden-state lookup. Check run/player identity first, then
the deduplication ledger, then freshness for a new ID. Repeating the exact
request returns its receipt even after a later observation; same ID/different
content (including a changed stamp) is refused. Preserve request
order. Drain once and mark Submitted only for commands actually entering the
recorded input frame. Pending long-running work is tracked by observation-based
postconditions, not by the submission receipt.

Construct commands using existing player paths and validate with
`HumanCommandPolicy`. SkipIntro is allowed only in an observable intro phase;
RevealMap and other outside-play/keeper-only commands are never admitted here.
Emit GuiButtonCommand for relevant panel selections, because campaign scripts
can observe those events; this still does not test UI hit detection.
Admission validates capability, bounds, ownership and visible handles. The engine
remains authoritative for gameplay outcomes. Do not mutate resources or fabricate
a success/refusal event to compensate for a command that did no work.

- [ ] `PlayerSessionTests`: `DuplicateRequestRunsOnce`, `ConflictingIdIsRefused`, `StaleOrOtherRunDoesNotProbeWorld`, `ReusedSlotCannotResolveOldHandle`, `WrongPlayerAndDebugActionRejected`, `SelectPanelEmitsOrdinaryCommand`.
- [ ] Run Behavior tests/class for assertion RED. Implement Submit/Drain/Cancel and confirm corresponding positive actions produce the same commands as the existing player path.
- [ ] Verify repeated Drain is empty; Cancel marks queued actions cancelled, admits nothing later and does not retract a previously submitted input frame. Reject actions after terminal state.
- [ ] Verify receipt reasons do not distinguish two otherwise identical hidden states; review/document/commit.

### Task 4: integrate the existing tick loop and replay identity

**Consumes:** task 2 builder, task 3 session and IPlayerController.
**Produces:** `PlayerRunCoordinator` in Harness with
`AfterTick(WorldState world, GameTick completedTick) -> IReadOnlyList<PlayerCommand>`;
add optional `PlayerRunOptions? PlayerRun` to RunOptions. Define PlayerRunOptions
with Controller, PlayerId, RunId and the limits specified below. Preserve all
existing Run overloads and the no-option execution path.

The coordinator captures after a completed tick, presents updated receipts,
calls Decide when its advance count expires, and queues for the next frame.
After journal input tick t, World.CurrentTick is t+1; generated commands enter
journal input tick t+1, not t+2. The observed GameTick is not incremented again.
Validate controller advance/actions before queuing. Run serially after existing
keeper decisions, matching existing controller order. Reject a player already
owned by a keeper, old ScenarioController or another player controller.
Replay never instantiates the coordinator or calls the controller.

Do not silently drop a final decision at the duration boundary: make no new
decision when no next tick exists; cancel outstanding unsubmitted actions.
For player runs only, stop at terminal state, cancellation or a reached limit;
write the actual completed count to ScenarioResult.TicksRun and journal end, with
hashes for ticks 0..completed. Preserve the legacy no-option duration behavior.
Add `player.run.schema=1` to the header; replay of this mode executes exactly
InputJournal.TicksRun ticks, rejecting absent/out-of-range counts. Legacy replay
behavior is unchanged. Use end reason `completed` only for actual campaign wins;
record other outcomes as failure reasons so existing replay CLI cannot hide them.
Use the existing watch process for hard wall-time termination of blocked code;
a synchronous Decide callback cannot be forcibly cancelled safely in-process.

Extend journal metadata additively with contract/controller versions, visibility
policy, limits and pacing policy. Add a `player-run-manifest.json` sidecar naming
scenario/installed-data identity and SHA-256 of executing assemblies, along with
source identity. Keep journal compatibility checks; do not rename command tags.
An unverified dirty/stale build may run diagnostically but cannot qualify for
acceptance. PlayerRunManifest exposes `Write(string directory, Scenario scenario,
string installDirectory)` and `Verify(string directory, Scenario scenario,
string installDirectory)`. Hash all .kwd/.kld files beneath Data/Editor, ordered
by relative path, plus executing DK2 assemblies; record source hashes separately.
The CLI calls Verify before invoking existing Replay for player-run journals;
tests of acceptance invoke the same verification. Missing or altered sidecars
are incomplete evidence, not a best-effort replay acceptance.
Hashes alone do not prove source-to-DLL freshness. Qualifying attempts also need
a successful fresh-build receipt binding source hashes, output hashes and the
build log; absent or inconsistent receipts leave the run diagnostic only.

- [ ] `PlayerRunTests`: controller and scripted next-tick commands yield identical hash series; decisions after ticks 0/4/8 observe stamps 1/5/9; monitor on/off yields identical hashes and RNG.
- [ ] Add RED cases for keeper/controller conflicts, invalid AdvanceTicks, first/last tick handling and cancellation after queued input. Implement the single-loop integration; run GREEN plus ScenarioControllerTests and JournalReplayTests.
- [ ] Replay with a controller that throws if invoked: assert it is never called and the recorded run's hashes match. Reject mismatched scenario/data/assembly identity and truncated journals as incomplete evidence.
- [ ] Profile coordinator/observation overhead against the identical journal without observers. Keep manifest/source/data hashing at startup, measure instrumentation separately from simulation and add stable allocation/cadence guards before the task review.
- [ ] Retain complete logs; review/document/commit without updating a determinism pin merely for instrumentation.

### Task 5: reusable action routines and Level 1 strategy

**Consumes:** only PlayerObservation; no database/world access in the controller.
**Produces:** `CampaignPlayer(CampaignPlayerOptions options) : IPlayerController`;
`PlayerActionRoutines.Choose(PlayerObservation observation,
CampaignObjective objective) -> PlayerAction?` in DK2.AI.Players.
Objectives are controller policy: EstablishEconomy, ProvideRooms, Explore,
RespondToThreat and AwaitVerdict. They are not claims about DK2's AI priorities.
`CampaignPlayerOptions` carries typed run/player IDs and explicit strategy
parameters; observations carry the prices/availability needed to act.

First profile: skip an offered intro, select required offered tools, maintain a
Treasury and offered Lair/Hatchery rooms, explore visible diggable boundaries,
use owned creatures against visible threats through pickup/drop, activate an
offered visible Special when applicable, and wait for the real verdict.
Room candidates are 3x3 rectangles admitted by capabilities, ordered by distance
from owned accessible floor, then Y/X. Exploration searches the observed grid
only, N/E/S/W with Y/X tie-breaking. Never embed the Lord's unseen coordinates,
trigger IDs, a captured input timeline or hidden map data into the strategy.

Check an action's postcondition before requesting another: a built room exists,
the hand contents changed, territory opened or a visible threat changed. Do not
equate a queued receipt with completion. Let known ongoing work continue; after
the declared stall window, try the next candidate up to three recovery attempts,
then return a controller limitation. Preserve the reason and observed inputs.

- [ ] `CampaignPlayerTests`: table-driven detached observations require affordable-room selection, no repeat build while pending, alternate visible frontier after refusal, no targeting unseen things, valid waiting, terminal idle and recovery exhaustion.
- [ ] Add the positive control that changing only an admitted observation changes the decision, while identical observations/options yield identical actions. Run AI project/class RED.
- [ ] Implement routines and strategy; GREEN includes 3x3 candidate ties, insufficient gold, a disappearing target and no offered action. Tests use observation fixtures, never a privileged KeeperView.
- [ ] Measure strategy/candidate-search cost and allocations with representative visible populations; optimize dominant scans/sorts before considering SIMD or microbenchmarks. Verify identical decisions and hashes for unchanged observations/options.
- [ ] Review/document/commit. Unsupported strategic needs remain controller limitations; they do not authorize a mechanic change or a hidden-state shortcut.

### Task 6: CLI, limits, outcomes and evidence

**Produces:** `PlayerRunOutcome` (Completed, Defeated, ControllerLimited,
TechnicalFailure, IncompleteEvidence); `PlayerRunReport` and its writer.
Add `--campaign-player <player>` to existing run/watch options, propagated by
WatchJobs; reject combination with --fuzz or another controller on that player.
Defaults are test policy, not DK2 rules: 14,400 simulation ticks maximum,
600 wall seconds, decision interval 4 ticks, at most 4 requests per decision,
4,096 submitted actions, stall window 1,200 ticks and 3 recovery attempts.
Expose `--player-max-ticks`, `--player-wall-seconds`, `--player-interval-ticks`,
`--player-max-actions`, `--player-stall-ticks`, `--player-retries` and
`--player-output-mib`; reject zero/negative or overflowing values. Keep at most
4 requests per decision and bound AdvanceTicks to 1..40. Bound output to 64 MiB,
reserving 1 MiB for final journal/report flush before stopping at that budget.
Actual disk errors may prevent a bundle; report on stderr for the watch parent,
and classify incomplete evidence instead of promising that flush succeeded.

Use actual PlayerStatus.Won for completion; Conquered/Lost mean gameplay defeat.
Exit 0 requires a qualifying completed run, 1 a non-completed attempt, 2 invalid
configuration/incomplete evidence; watch retains its existing aggregate/limit
exit meanings. A sourced gameplay violation is a report finding, not an invented
new terminal state. Waiting/progress diagnostics alone cannot prove a defect.

Write `player-run.json`, `player-decisions.tsv` and `player-actions.tsv` beside
the existing journal/hashes/report. Include first terminal tick, milestones,
controller limits, applied/pending/refused/cancelled receipts, coverage, source
identity and untested areas. Separate player-visible decision evidence from
privileged diagnosis; the latter is never input to the strategy.

- [ ] `PlayerRunReportTests`: assert win/defeat/controller limitation/timeout/invalid evidence map to distinct report outcomes; no victory is inferred from exit success or an empty expectation list.
- [ ] RED tests cover parsing bounds, watch propagation, budget boundary flush, unwritable output, exhausted storage and no overwrite of an existing evidence directory. Implement and run GREEN.
- [ ] A failed installed attempt must leave journal and metadata readable through the last flushed tick. Preserve irreplaceable failure/acceptance evidence add-only under F:/DK2Evidence with a SHA-256 inventory; routine regenerable output stays under build/.
- [ ] Measure journal/report buffering and serialization under long runs; bound allocation and retained memory without per-tick formatting work or weakening flush/recovery evidence. Include these findings in the review before committing.
- [ ] Review/document/commit; update the harness README's new flags and explicitly list Unity/model/UI/audio coverage as not implemented.

### Task 7: real Level 1 attempt and acceptance

**Files:** task 7 ownership above; extend existing journal/replay tests if needed.
**Consumes:** tasks 1-6, the shipped Level 1 data and existing win/message research.
Create the campaign-player scenario with campaign:level1, random:dk2, seed:1,
fog enabled, viewing player 3, placed_things:true and duration_ticks:14400.
No setup resource grants, extra things/rooms, work injection, scripted commands,
forced verdicts or pre-revealed map. Load ordinary shipped players and triggers.

Before asserting milestones, verify their applicability against shipped Level 1
and the existing notes: `research/navigation-captures.md` (Level 1 winning packet),
`research/message-tabs.md` (tutorial notifications), `research/win-and-defeat.md`.
Those are evidence pointers, not authority for copying historical raw slot IDs.
Use current normal player capabilities and actual status. Investigate any new
DK2 behavior question under AGENTS.md before adding a behavioral assertion.

- [ ] Add InstallFact tests `AttemptUsesUnmodifiedLevelStart`, `ControllerCanReachNativeWinVerdict`, `WinningJournalReplaysWithoutController`. Initial fixture must demonstrate the tests are exercised, not vacuous skips.
- [ ] Fresh-build Release before the attempt; retain build log plus source/output hashes as the build receipt and verify no source changed before launch. Run one seed-1 attempt under watch with the limits above and a new output folder. A non-win is diagnostic, not GREEN; fix controller defects only within scope and file gameplay blockers separately.
- [ ] Once it wins, replay the same journal at the verified build/data identity and compare all recorded hashes. Confirm observations/monitoring off replay the same applied input; no model or controller participates.
- [ ] Attempt seeds 2 and 3 and disclose every outcome without tuning engine behavior or promising broader campaign acceptance. A repeat seed-1 run must reproduce the deterministic baseline's action decisions and hash sequence.
- [ ] Run final Release solution/Unity compile, relevant existing suites, full solution tests and docs/KB/coverage/status gates from HANDOFF. Installed acceptance must run with DK2_REQUIRE_INSTALL=1; absent data blocks acceptance rather than passing it.
- [ ] Publish the final headless overhead/allocation results against the reviewed baseline, including first-use cost, remaining hot spots, applied-input equivalence and skill-backed review findings. Do not call one fast run a whole-game performance acceptance.
- [ ] Reviewer checks evidence and actual Level 1 verdict; documenter reconciles status in the design/parent/handoff without marking later stages built. Inspect edits, archive receipt, commit and integrate by the owner's selected execution method. Do not remove worktrees or evidence.

## Execution handoff

Recommend inline execution for this first increment: tasks share contracts and
one scenario loop, so interface continuity matters more than parallel production.
Keep the independent reviewer/documenter gates. Owner review of this plan and
choice of inline versus task workers precede implementation; later design stages
require their own plans. No external model connection or Unity background run is
authorized or implemented by saving this plan.
