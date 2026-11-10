# Campaign player stages 1-2 implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow AGENTS.md's reviewer/documenter passes before commits.

**Status:** execution plan, 2026-11-07. Design approved by the owner; inline execution
in a worktree on `feature/campaign-player` (not merged). Tasks 1-7 (stages 1-2) are built on the branch; task 7's commit and integration are pending the owner. No later stage is built.
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
   The post-win finale window must not admit controller actions or change the Won verdict (tasks 4, 6).
5. A stale DLL, dirty tree, partial journal or skipped installed test must not receive an acceptance claim (tasks 4, 6-7).

## Entry points, dependencies and file ownership

Current source baseline is `49f6305c` (rechecked 2026-11-09: since `022cedf1` only
`WorldView.Objects.cs` changed in the touched projects); recheck HEAD before execution.
`ScenarioRunner.RunOnLevel` owns the only tick loop and queues controllers after
completed ticks for the next input frame. `RunOptions` is in `RunMonitor.cs`.
`WorldView.CanShowTile`, `Tiles`, `Creatures`, `Objects`, `PlayerInputSnapshot`
and the presentation HUD models are the visibility/input references.
`HumanCommandPolicy` is in DK2.Harness, not DK2.Presentation.

| Owner | New or changed files | Responsibility |
|---|---|---|
| Task 1 | `src/DK2.Simulation/Players/PlayerContracts.cs`, `PlayerObservation.cs`, `PlayerAction.cs`; `src/DK2.AI/Players/IPlayerController.cs`; `src/DK2.Data.Model/ObjectTypeId.cs`; `tests/DK2.AI.Tests/Players/PlayerDecisionTests.cs` | Detached contract and controller boundary |
| Task 2 | `src/DK2.Presentation/PlayerObservationBuilder.cs`, `WorldView.Player.cs`; `tests/DK2.Behavior.Tests/PlayerObservationTests.cs` | Visibility-safe projection and capabilities |
| Task 3 | `src/DK2.Harness/PlayerSession.cs`; `src/DK2.Presentation/PlayerActionAdapter.cs`; `tests/DK2.Behavior.Tests/PlayerSessionTests.cs`; revision comparison operators in `PlayerContracts.cs` | Action admission, deduplication, handles and receipts |
| Task 4 | `src/DK2.Harness/PlayerRunCoordinator.cs`, `PlayerRunManifest.cs`; existing `ScenarioRunner.cs`, `RunMonitor.cs`, `JournalHeader.cs`; `tests/DK2.Behavior.Tests/PlayerRunTests.cs` | Loop integration, bounded advancement, identity and replay |
| Task 5 | `src/DK2.AI/Players/CampaignPlayer.cs`, `PlayerActionRoutines.cs`, `CampaignPlayerOptions.cs`; `tests/DK2.AI.Tests/Players/CampaignPlayerTests.cs`; `PlayerDecision.Limited` in `IPlayerController.cs`, its mapping in `src/DK2.Harness/PlayerRunCoordinator.cs`, `tests/DK2.Behavior.Tests/PlayerRunTests.cs` | Observation-driven strategy and recovery |
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
verification list for the final gate. Only the tests of tasks 1 and 2 have been executed.

### Task 1: detached player protocol

**Files:** task 1 ownership above; test `tests/DK2.Simulation.Tests/PlayerContractsTests.cs`.
**Interfaces produced:** namespace `DK2.Simulation.Players`:
`PlayerRunId(string Value)`, `PlayerActionId(long Value)`,
`PlayerThingHandle(PlayerRevision, int ordinal)` and `PlayerRevision(long Value)` are
validated readonly value types; empty IDs and nonpositive action/revision values
are rejected, and `default` reports `IsValid == false`. `PlayerStamp` contains run ID, `PlayerId`, `GameTick` and revision.
Use the existing typed `GoldAmount` and `ManaAmount` (DK2.Core), not interchangeable quantities.
`PlayerThingHandle` is (revision, ordinal in that observation's thing list) rather than a
string: no allocation per thing, revision-scoped by construction, and a transport formats it. The action family is closed by a `private protected abstract` member,
since a record's protected copy constructor cannot close it.

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

- [x] Add tests `ObservationOwnsItsCollections` (mutating constructor inputs cannot change the snapshot), `ObservationHasNoPrivilegedReferences` (recursive field/type audit), and typed identity validation. Include these exact contract assertions:

```csharp
Assert.Throws<ArgumentException>(() => new PlayerRunId(""));
Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerActionId(0));
Assert.NotEqual(new PlayerRunId("run-a"), new PlayerRunId("run-b"));
```

- [x] Add a minimal immutable constructor/record skeleton, then run `PlayerContractsTests` in DK2.Simulation.Tests and retain the intended RED assertions.
- [x] Implement copying, validation and the closed action family; GREEN includes valid empty/no-action observations and distinct run identities.
- [x] Build the solution and Unity compile project; check the architecture guards before the task review/documentation/commit cycle.

### Task 2: visibility-safe observations and capabilities

**Consumes:** task 1 types, existing presentation models.
**Produces:** `PlayerObservationBuilder(GameDatabase database, PlayerId player)`
in DK2.Presentation; `Capture(WorldState completedWorld, PlayerStamp stamp,
IReadOnlyList<PlayerReceipt> receipts) -> PlayerObservationCapture`.
Define PlayerObservationCapture in the same file: Observation is the task 1 DTO;
`TryResolve(handle, out PlayerInputTarget)` maps a handle of that revision to its admitted
target (an owned array indexed by ordinal, not a dictionary). The host retains the capture;
only Observation reaches IPlayerController. Every handle is scoped to its run/revision and
generated from already-admitted targets. As built, the tests live in DK2.Behavior.Tests to
reuse `ScenarioTests.SyntheticDatabase()` (creature, Treasury, Create Imp) instead of a copy,
and `PlayerNotice` carries the message kind and a per-viewer ordinal, never the shared queue id
(schema still 1, unshipped). Thing offers are the engine's hand rules (`HandRules.CanPickUp`,
`CanSlap`, `CanPickUpObject`, `CanPickUpStoreGold`); every own creature not in the hand has its tile.
The viewer's own tag on hidden ground is kept as the human sees it, although whether it sticks
depends on hidden terrain (TileTagSystem); `PlayerObservationBuilder.CoverageLimits` lists the rest.
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

- [x] In `PlayerObservationTests`, create paired synthetic worlds differing only behind fog: enemy creature, object and gold, a hidden room, a scanned tile's owner, rival mana and another player's messages (posted before the viewer's). Assert field-for-field identical detached observations and capabilities.
- [ ] Not yet in the pair: a secret door and trigger state (a later task's fixture can add them).
- [x] Pin builder invariants: `Things[i].Handle` is (stamp revision, i), creature-summary handles point into Things, hidden tiles carry only the viewer's own tag (otherwise all-default), and no default(PlayerVisibleThing) is emitted.
- [x] Add positive controls revealing a target through the existing view path, own held-creature visibility, discovery changes and empty message history. A global count or hash must not expose hidden changes.
- [x] Run the test class for RED, implement Capture, then GREEN. Mutate the original world after capture and assert the old observation is unchanged.
- [x] Assert StateHash and simulation RNG state are unchanged by repeated capture; retain existing fog/view/input tests in the focused run.
- [x] Establish the observation performance baseline on small and large/fog-heavy synthetic worlds. Measure first and repeated capture, capability generation and allocation; apply the optimization gate and preserve the detached-snapshot mutation test after any reuse/caching change.
- [x] Review the correctness and performance evidence, document and commit (`38e8b9e8`).

Result (headless, shipped data, fog on, steady state, tick 1200): Level 1 (30x38) 114 us and
5 KB per capture; Level 20 (99x99) 589 us and 32 KB, against 1.2-3.3 ms per simulation tick.
The hot spot left is `PlayerInputSnapshot.Capture` building full `CreatureView`s (180-230 us on
Level 20); a per-terrain lookup table gave no measurable gain and was reverted.

Human-UI questions, answered from code and research (the builder follows the human path in each):
- Own tag on hidden ground: a fog tile takes the tag and loses it on reveal as impenetrable rock
  or a room (owner, Q193; `FogOfWarSystem`); a secret door or scanned non-taggable ground refuses
  it at once (`TileTagSystem`), and `TileAt` draws the same marker, so a human sees it too.
- Own prisoner in unseen cells: the creature panel's Focus (`0x00417F60`) excludes only state
  193 and held creatures, so a human can jump to it; the observation keeps its tile.
- Mid-level camera paths: pointer routing (`0x0044D590`) refuses panel and hand input in path
  mode 0x12; only Escape/Space skip (`0x00405362`). Intro offering only SkipIntro is right.
- A disguised enemy: the hover permission admits it, but action 57's path unmasks and refuses it
  (creature-pickup.md); the offer matches the cursor. How a disguise is drawn is unread.

### Task 3: bounded action admission and receipts

**Consumes:** observations/capabilities and existing human command constructors.
**Produces:** `PlayerSession(PlayerRunId runId, PlayerId player,
PlayerActionAdapter adapter)` in Harness;
`Observe(PlayerObservationCapture capture)`;
`Submit(PlayerRequest request) -> PlayerReceipt`;
`Drain(GameTick appliesAt) -> IReadOnlyList<PlayerCommand>`;
`Cancel() -> IReadOnlyList<PlayerReceipt>`.
Expose immutable receipts for the next observation (as built: `TakeReceipts()`, the receipts
issued or changed since the last call; optional `PlayerSessionLimits` bounds requests per
observation and admitted actions). Only requests judged on the current observation are
ledgered; transient refusals (identity, stale, future, too many, cancelled) may be retried under
the same id. The next `Observe` cancels still-queued requests as stale, since their commands
carry that observation's entity ids, so the coordinator drains before observing. A drop names
the creature under the tile, as the hand does. PlayerActionAdapter is
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

- [x] `PlayerSessionTests`: `DuplicateRequestRunsOnce`, `ConflictingIdIsRefused`, `SelectPanelEmitsOrdinaryCommand`, and as built `StaleFutureOrOtherRunRequestsAreRefusedBeforeAnyHandleIsResolved`, `AHandleFromAnEarlierRevisionNeverResolves`, `WrongPlayerWrongPhaseAndUnofferedActionsAreRejected` for the planned stale, reused-slot and wrong-player cases.
- [x] Implement Submit/Drain/Cancel. `AdmittedActionsBuildTheHumanPathsCommands` and `ADropNamesTheCreatureUnderItAsTheHandDoes` pin the commands against the existing player path. The assertion-RED run was replaced by 16 paired counterfactuals, each detected by its named test (K1-K12 on the first version were not re-run after the session rewrite).
- [x] Repeated Drain is empty; Cancel marks queued actions cancelled, admits nothing later and does not retract a submitted request (`DrainIsOnceAndCancelWithdrawsOnlyWhatIsQueued`); nothing is admitted after the game ends (`NothingIsAdmittedAfterTheGameEnds`).
- [x] Receipt reasons and commands do not distinguish two otherwise identical hidden states (`ReceiptsDoNotDistinguishHiddenStates`).
- [x] Commit.

A drop onto a creature feeds or pays it as the hand's does (`DropDeliveryRules.RecipientOf`,
`0x004BC710`: chicken by hunger fill, gold by pay, no ownership test). Queued actions never cross
an observation: the next `Observe` cancels them as stale (host design, not a DK2 question).

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
For player runs only, stop at terminal state, cancellation or a reached limit.
Status 2 is written before the Horned Reaper finale starts (`0x004BE630` →
`0x004BF410`, [campaign-win-sequence.md](../research/campaign-win-sequence.md)),
so after Won the coordinator keeps simulating a bounded post-win window with no
decisions and every new request refused as terminal. The window ends when
`PlayerFinale.PickupTick` is set (Reaper in `PickUpGemState` 233) or its tick limit
expires; it is simulation, not controller play, and records the pickup tick.
Defeat stops at once. Write the actual completed count, including any post-win
window, to ScenarioResult.TicksRun and journal end, with
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

- [x] `PlayerRunTests`: controller and scripted next-tick commands yield identical hash series; decisions after ticks 0/4/8 observe stamps 1/5/9; an observed run's hash series equals the uncontrolled run's.
- [x] Monitor on/off yields identical hashes and RNG with a player attached (`TheMonitorOnOrOffLeavesTheHashSeriesAndItsRandomStateAlone`: coverage and progress sampling on against off; the state hash folds in the generator's state).
- [x] RED cases for keeper/controller conflicts (`APlayerOwnedByAKeeperOrAControllerIsRefused`), invalid AdvanceTicks, first/last tick handling and cancellation after queued input. Post-win window, at coordinator level: Decide is never called after Won, the session is cancelled so requests are refused as terminal, the run ends at the gem pickup or the window limit, and `EndReason` is "completed". Single-loop integration built; the full suite, which includes ScenarioControllerTests and JournalReplayTests, is green (8,714 passed, 0 failed, 30 skipped).
- [ ] Journal end and `TicksRun` including the finale window, through `ScenarioRunner`. No synthetic scenario can win; task 7's install attempt must assert it.
- [x] Replay of a player-run journal executes exactly the recorded ticks and matches the recorded hashes (`APlayerRunJournalReplaysItsRecordedTicksWithoutTheController`). `Replay` takes no controller and never builds the coordinator, which is structurally stronger than the planned throw-if-invoked controller, so no such controller exists. Truncated journals are refused (`ATruncatedPlayerRunJournalIsRefused`); mismatched scenario, data or assembly identity is reported by `PlayerRunManifest.Verify` (`TheManifestVerifiesItsRunAndNamesEveryDifference`); the CLI's call to it is task 6.
- [x] Coordinator and observation overhead measured against the identical run without observers (figures under "As built"); a stable allocation and cadence guard is `ADecisionCostsAFewKilobytesAndTheCadenceIsTheAdvance`. Manifest, source and data hashing at startup is the CLI's, task 6.
- [x] Retain complete logs; review/document/commit without updating a determinism pin merely for instrumentation.

As built: `AfterTick(world, completedTick, anotherTick)`; on the last tick it records the result
without deciding. `PlayerRunOptions` carries MaxTicks, MaxAdvanceTicks, FinaleTicks and the
session limits; `PlayerRunStop` and `PlayerFinaleCoverage` (ReachedPickup, NotReached,
NotApplicable when no gem was recorded at the win, CutShort when the tick budget closes the
window, Unchecked) feed task 6's outcomes; a win stands whatever the finale does. A controller
that throws or answers nothing is ControllerLimited; a scenario scripting commands for the
controlled player is refused. A journal that ended in an exception replays the throwing tick too.
The decision log reads each receipt back from the session (`TryGetReceipt`).
`PlayerRunManifest.Write/Verify(directory, scenario, install, runId)` cover the run id, scenario,
`Data/editor` KWD/KLD and `Data/Paths.WAD`, every `DK2.*.dll` beside the harness, and the source
tree with root build files; `Verify` returns blocking and diagnostic lists (source only
diagnostic). The CLI calls it in task 6; the fresh-build receipt is `BuildReceipt` (task 7). No synthetic
scenario can win, so a win through the runner (the last-tick result, the finale window, the
journal's "completed") is first exercised by task 7's install attempt, which must assert it. Measured (shipped Level 1, fog, 1,200 ticks, decision every 4 ticks):
549-560 ms against 525-536 ms without the player (about 4.5%), 2.5 MB more allocation (about
8 KB a decision), no extra gen0 collections, and an identical 1,201-hash series.

### Task 5: reusable action routines and Level 1 strategy

**Consumes:** only PlayerObservation; no database/world access in the controller.
**Produces:** `CampaignPlayer(CampaignPlayerOptions options) : IPlayerController`;
`PlayerActionRoutines`, pure routines over one `PlayerObservation`, in DK2.AI.Players.
The strategy's order (intro, hand, rooms, specials, frontier) is controller policy, not a
claim about DK2's AI priorities. `CampaignPlayerOptions` carries typed run/player IDs and explicit strategy
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

- [x] `CampaignPlayerTests` (in `tests/DK2.AI.Tests/Players/`): detached observations require affordable-room selection, no repeat build while pending, alternate visible frontier after refusal, no targeting unseen things, valid waiting, terminal idle and recovery exhaustion.
- [x] Add the positive control that changing only an admitted observation changes the decision, while identical observations/options yield identical actions (`IdenticalObservationsGiveIdenticalDecisionsAndAChangedOneChangesIt`). The assertion-RED run was replaced by 21 paired counterfactuals over the build and its two review rounds, each detected by a named test.
- [x] Implement routines and strategy; GREEN includes 3x3 candidate ties, insufficient gold, a disappearing target (a special that is gone, a held fighter whose enemy left) and no offered action. Tests use observation fixtures, never a privileged KeeperView.
- [x] Measure strategy/candidate-search cost and allocations (figures under "As built": about 1% of a Level 1 run, so no scan or sort was optimised). Identical decisions for unchanged observations are pinned by the determinism test, identical hashes by the Level 1 replay and repeat run.
- [x] Review/document/commit. Unsupported strategic needs remain controller limitations; they do not authorize a mechanic change or a hidden-state shortcut.

As built: `CampaignPlayerOptions(runId, player, workerTypes)` takes the digging creature types
from the host (public definition data) and validates its policy values; `PlayerDecision.Limited`
ends a run as a controller limitation, its reason naming pending rooms, marks, frontier, hand and
gold. `PlayerActionRoutines` holds pure routines over one observation (frontier, room squares,
nearest enemy, free fighter, specials, own floor); the plan's `Choose`/`CampaignObjective` were
dropped as an unused second path. Postconditions: a room is done when it covers its square, and a
build not shown after `PendingRoomTicks` moves to another square; a drop is done when the hand
empties, else that tile is skipped; a held fighter with no enemy goes down near home; a fighter
near the enemy is left to fight, with a cooldown after each drop. Progress counts gains only; a
gain resets recoveries; recovery untags and skips the standing marks. Receipts are matched by
action id (the host numbers 1, 2, ... in decision order, pinned by a run test); only a refusal
about the square (NotOffered, OutOfBounds) marks it tried. `DK2.AI/Players` is scanned for
engine and host type names, with a negative control (`DK2.AI` cannot reference Presentation or
Harness at all). Measured on Level 1: `Decide` 29 us and 1.3 KB a decision once warm.

An early attempt with this controller (seed 1, fog, placed things, no setup changes) won Level 1
at tick 2360 in 590 decisions (2 rooms, 155 marks, 15 pickups and drops, 1 special). The Horned
Reaper was created at the win but took no gem: the Portal Gem had no position (ADR-0725). Task 7
records the acceptance runs after that fix.

### Task 6: CLI, limits, outcomes and evidence

**Produces:** `PlayerRunOutcome` (Completed, Defeated, ControllerLimited,
TechnicalFailure, IncompleteEvidence; a refusal before any tick is recorded as the verdict string `Refused`); `PlayerRunReport` and its writer.
Add `--campaign-player <player>` to existing run/watch options, propagated by
WatchJobs; reject combination with --fuzz or another controller on that player.
Defaults are test policy, not DK2 rules: 14,400 simulation ticks maximum,
600 wall seconds, decision interval 4 ticks, at most 4 requests per decision,
4,096 submitted actions, stall window 1,200 ticks, 3 recovery attempts and a
400-tick post-win finale window (100 s at 4 TPS; derive the final value from the
first headless Level 1 finale and record it, do not tune it to pass).
Expose `--player-max-ticks`, `--player-wall-seconds`, `--player-interval-ticks`,
`--player-max-actions`, `--player-stall-ticks`, `--player-retries`,
`--player-finale-ticks` and `--player-output-mib` (finale window may be 0 to skip it); reject zero/negative or overflowing values. Keep at most
4 requests per decision and bound AdvanceTicks to 1..40. Bound output to 64 MiB,
reserving 1 MiB for final journal/report flush before stopping at that budget.
Actual disk errors may prevent a bundle; report on stderr for the watch parent,
and classify incomplete evidence instead of promising that flush succeeded.

Use actual PlayerStatus.Won for completion; Conquered/Lost mean gameplay defeat.
Report finale coverage separately: reached gem pickup (tick), not reached within
the window (a suspected anomaly, not a defeat), not applicable (no gem on the
level) or skipped (window 0, listed as unchecked coverage). The finale outcome
never changes the Won verdict. Headless camera holds in states 229/230 never
wait, so headless finale timing does not certify rendered timing (stage 4).
Exit 0 requires a qualifying completed run, 1 a non-completed attempt, 2 invalid
configuration/incomplete evidence; watch retains its existing aggregate/limit
exit meanings. A sourced gameplay violation is a report finding, not an invented
new terminal state. Waiting/progress diagnostics alone cannot prove a defect.

Write `player-run.json`, `player-decisions.tsv` and `player-actions.tsv` beside
the existing journal/hashes/report. Include first terminal tick, milestones,
controller limits, applied/pending/refused/cancelled receipts, coverage, source
identity and untested areas. Separate player-visible decision evidence from
privileged diagnosis; the latter is never input to the strategy.

- [ ] `PlayerRunReportTests`: assert win/defeat/controller limitation/timeout/invalid evidence map to distinct report outcomes; no victory is inferred from exit success or an empty expectation list. Each finale coverage value is reported distinctly and none alters the Won outcome.
- [x] RED tests cover parsing bounds, watch propagation, budget boundary flush, an unwritable output folder, the budget stop and no overwrite of an existing evidence directory. Implement and run GREEN.
- [ ] Untested branches: the over-budget downgrade of a finished folder, a real `EvidenceWriteException` from the disk, Defeated through the CLI and an IO error while writing the report files.
- [ ] A failed installed attempt must leave journal and metadata readable through the last flushed tick. Preserve irreplaceable failure/acceptance evidence add-only under F:/DK2Evidence with a SHA-256 inventory; routine regenerable output stays under build/.
- [x] Bound allocation and retained memory without per-tick formatting work or weakening flush/recovery evidence: the per-tick cost is one budget comparison and one delegate, the reports are written once, and `BudgetedStream` forwards writes without allocating.
- [ ] Measure journal/report buffering and serialization under a long run. Not timed separately; the figures above come from reading the code, not a measurement.
- [x] Update the harness README's new flags and explicitly list Unity/model/UI/audio coverage as not implemented.
- [x] Commit.

As built: `HarnessCommandLine` parses `--campaign-player` and the `--player-*` limits (a limit
without a player, a misspelt `--player-*` flag, a non-keeper, `--fuzz` on the same player, an
advance above 40 or an output budget under 2 MiB are refused). `watch` passes them to each run,
refuses `--player-run-id`, and sets the wall limit 30 s below `--timeout-seconds` unless given (it
must stay below), so a run stops itself before the watchdog kills it; watch reads a player run's
journal from its run folder. `PlayerRunCommand` makes a new `<out>/<scenario>/<run id>/` folder
(default id: the UTC time; never an existing folder; run ids of only dots are refused), writes the
manifest before the first tick, and journals through a `BudgetedStream` whose 1 MiB reserve keeps
room for the final records; wall time and output budget stop the run as cancelled. The whole folder
counts against the budget, checked before the verdict file is written last. Verdicts: a win is
Completed, with the finale and any suspected anomaly beside it and `acceptance` saying whether it
is claimed (only with task 7's build receipt and replay proof, see below); a defeat Defeated; no result in the ticks or a controller's
own limit ControllerLimited; a stopped run or evidence that did not reach disk IncompleteEvidence
(exit 2); a refusal before any tick Refused (exit 2); an engine or host exception TechnicalFailure
(exit 1) with its journal, manifest and verdict. `replay` verifies a player-run journal's manifest
first. The budget check is one comparison a tick and the reports are written once; `BudgetedStream`
forwards byte and span writes without allocating. The installed Level 1 runs through the CLI are
recorded under task 7.

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

- [x] Add InstallFact tests `AttemptUsesUnmodifiedLevelStart`, `ControllerCanReachNativeWinVerdict`, `WinningJournalReplaysWithoutController`, `WinningRunReachesFinaleGemPickup` (Level 1 hands a gem over by creature action 20; the replay reproduces the pickup tick). Initial fixture must demonstrate the tests are exercised, not vacuous skips.
- [x] Fresh-build Release before the attempt; retain build log plus source/output hashes as the build receipt and verify no source changed before launch. Run one seed-1 attempt under watch with the limits above and a new output folder. A non-win is diagnostic, not GREEN; fix controller defects only within scope and file gameplay blockers separately.
- [x] Once it wins, replay the same journal at the verified build/data identity and compare all recorded hashes. Confirm observations/monitoring off replay the same applied input; no model or controller participates.
- [x] Attempt seeds 2 and 3 and disclose every outcome without tuning engine behavior or promising broader campaign acceptance. A repeat seed-1 run must reproduce the deterministic baseline's action decisions and hash sequence.
- [x] Run final Release solution/Unity compile, relevant existing suites, full solution tests and docs/KB/coverage/status gates from HANDOFF. Installed acceptance must run with DK2_REQUIRE_INSTALL=1; absent data blocks acceptance rather than passing it.
- [x] Publish the final headless overhead/allocation results against the reviewed baseline, including first-use cost, remaining hot spots, applied-input equivalence and skill-backed review findings. Do not call one fast run a whole-game performance acceptance.
- [x] Reviewer checks evidence and actual Level 1 verdict; documenter reconciles status in the design/parent/handoff without marking later stages built.
- [ ] Inspect edits, commit and integrate by the owner's selected execution method. Do not remove worktrees or evidence.

As built: `scenarios/campaign-player/level1.yaml` (seed 1), `level1-seed2.yaml` and `level1-seed3.yaml`
are shipped Level 1 (`random: dk2`, fog on, viewing player 3, `placed_things`, 14,400 ticks) and grant
nothing; they play only under `run --campaign-player 3`. `CampaignPlayerLevel1Tests` has four
InstallFacts sharing one seed-1 attempt. `AttemptUsesUnmodifiedLevelStart` asserts every
Scenario/Setup field, that the file's start hash equals a bare in-memory scenario's, and that every
applied journal command is player 3's and as many as the submitted receipts.
`ControllerCanReachNativeWinVerdict` reads `PlayerState.Status` through `RunOptions.AfterTick` and
checks it against the coordinator's `ResultTick`; nobody else wins, the journal ends `completed`.
`WinningJournalReplaysWithoutController` replays with no controller, monitors off then on, all
hashes equal. `WinningRunReachesFinaleGemPickup` checks that the gem exists at the win, stands on the
tile where the Lord of the Land died (at its centre), that the finale is `ReachedPickup` within
`FinaleTicks`, `reaper_created` on the win tick and `gem_taken` on the pickup tick, and that the
replay reproduces `gem_taken`. `PlayerRunCommand.PlayerRunFor` was extracted so the tests and the CLI
build the same controller and limits. The Task 7 attempt follows ADR-0725 (hash 456), merged into
this branch from `fix/reaper-gem-pickup`; without it the finale was `NotReached`.

Result (study `F:/DK2Evidence/studies/campaign-player-l01-acceptance--2026-11-09--002`, which
supersedes `--001`; fresh Release build, 0 warnings, 1,661 source and 11 output hashes verified
before launch). `watch scenarios/campaign-player --campaign-player 3`, all exit 0, all
Completed/Won with the finale `ReachedPickup`:

| Seed | Win tick | Gem pickup | Ticks run | Decisions |
|---|---|---|---|---|
| 1 | 2360 | 2490 | 2491 | 590 |
| 2 | 2244 | 2346 | 2347 | 561 |
| 3 | 2216 | 2316 | 2317 | 554 |

Seed 1's final hash is `8f8750679404da55`; a repeat seed-1 run is byte-identical in decisions,
actions, hashes and events. Each journal replayed through `replay`, exit 0, all hashes matching
(2,492 / 2,348 / 2,318 ticks; the repeat 2,492). With `DK2_REQUIRE_INSTALL=1` the full tests (Behavior
600 passed, 5 skipped: the opt-in SpeedBudget tests and the TriggerCapture comparison, which needs
captures), the Unity compile, verification-ledger, kb, coverage, docs, status and diff-check gates
exit 0. Counterfactuals, each fault alone and the sources restored by SHA-256: reverting the gem drop
is caught by `WinningRunReachesFinaleGemPickup`, `ReaperFinaleOnRealDataTests` and both fix unit
tests; reverting the gem creation is caught only by
`FinaleTests.TheReaperTakesAGemItsDeadCarrierDropped`, because on Level 1 the Lord drops the gem
before the win, so that half is not observable there; both together, a replay one tick late, a
misread win tick, a start that grants gold and a foreign keeper's command in the journal are caught.

Headless overhead (seed 1, the player run against a replay of the same journal, both writing a
journal, order alternating, 15 steady rounds, assemblies hash-checked against the receipt): small,
within noise. The paired median is +49 ms a run against a 1,165 ms replay median, but the spread is
-63..+153 ms and play was slower in 11 of 15 rounds. Allocation is deterministic: +8.15 MB a run,
13.5 KB a decision; gen0 15 against 14; `Decide` 17 us a decision. First use in a fresh process costs
4.9 s against about 1.2 s steady (`Decide` 185 ms on first use). This is not a whole-game
performance acceptance. The remaining hot spot, from task 2's measurement, is
`PlayerInputSnapshot.Capture` building `CreatureViews`. These are remake measurements, not DK2 facts.

Build receipt and the claim: `tools/build_receipt.py <new dir>` writes the receipt (head, status,
tracked patch and its hash, source hashes taken before and after the build, build log, output hashes
without `.pdb`), and `run --campaign-player N --player-receipt <dir>` (also under `watch`) has
`BuildReceipt.Verify` check outputs, sources, unlisted build inputs under `src/` and that every loaded
`DK2.*` assembly comes from the harness folder before the run folder is made; any problem is exit 2.
`PlayerRunCommand.ClaimGaps` then requires a receipt, an empty `PlayerRunReport.ModifiedStart`, no
`--fuzz`, `computer_keepers` equal to the level's PLAYERS records, a Completed outcome, a finale of
`ReachedPickup` or `NotApplicable`, the evidence within the budget and an in-process controller-free
replay that matched every recorded hash and the tick count. `player-run.json` gains
`acceptance_claimed` and `receipt`; otherwise the line is `not claimed: ` and every reason. The claim
is headless Level 1 only. `BuildReceiptTests`, `PlayerRunReportTests` and `CampaignPlayerLevel1Tests`
(`AMatchingReceiptLetsTheWinClaimAcceptance`, which writes its receipt from the bytes it runs on and
so proves the claim path, not freshness) passed 52 with `DK2_REQUIRE_INSTALL=1`; each counterfactual
fault tried (receipt hashes not compared, unlisted outputs ignored, a mismatch not refused, setup
grants passing as unmodified, no receipt allowed to claim, fuzz ignored, computer keepers not compared,
replay result ignored, replay never run, replay proof not required, a dirty tree reported clean) was
caught. A real run of `tools/build_receipt.py` then the CLI on `scenarios/campaign-player/level1.yaml`
exited 0 with `acceptance_claimed` true, before the review fixes. The harness and scenarios READMEs
carry the procedure.

## Execution handoff

Recommend inline execution for this first increment: tasks share contracts and
one scenario loop, so interface continuity matters more than parallel production.
Keep the independent reviewer/documenter gates. Owner review of this plan and
choice of inline versus task workers precede implementation; later design stages
require their own plans. No external model connection or Unity background run is
authorized or implemented by saving this plan.
