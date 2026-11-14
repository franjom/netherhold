# Campaign player stage 4: agent supervision implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Follow AGENTS.md's reviewer/documenter passes before commits.

**Status:** execution plan, 2026-11-09, awaiting owner review. Nothing here is built. On 2026-11-14
the owner moved agent supervision to stage 4, after stage 3's capable bot: the bot plays the
campaign without a model first, and this plan starts only once stage 3 wins the levels it is
tried on (stage 3's driver level here). The file name keeps its old stage number so existing links hold.
**Goal:** let any MCP-capable AI agent (Claude Code, Codex or another client) supervise the
campaign player: at bounded checkpoints it reads a player-visible summary and changes the
strategy the existing action routines follow, while every run stays bounded, recorded and
replayable without the agent.
**Architecture:** the routines stay in `DK2.AI` and gain a `PlayerStrategy` they read instead
of fixed options. A `SupervisedPlayer` controller wraps them and, at checkpoints, asks an
`ISupervisor` for a strategy change. A new executable, `DK2.PlayerMcp`, is a local stdio MCP
server: it runs one player run on a worker thread through the existing `PlayerRunCommand` path
and bridges checkpoints to MCP tool calls. No transport or provider code enters the simulation,
the routines or the harness.
**Tech Stack:** .NET 10 (`DK2.PlayerMcp` only), the official C# MCP SDK `ModelContextProtocol`
1.0.0 (stdio transport, already in the local NuGet cache), existing netstandard2.1 assemblies,
xUnit 2/VSTest. No model provider SDK, network listener, API key or Unity dependency.
**Spec:** [design](2026-11-07-agentic-campaign-playtesting-design.md), stage 4 row; stages 1-2
as built in [their plan](2026-11-07-campaign-player-stages1-2.md).

## Owner decisions (2026-11-09)

- The supervisor is an AI agent session connected through a local stdio MCP server. The owner
  chose this over a provider API called from the harness or a local-only model.
- The server is provider-neutral: usable by Claude Code, Codex and any client that speaks MCP
  tools over stdio. Nothing in it may depend on one client.
- The second level for the routine-reuse check was Level 2 (2026-11-09). The default bot now wins
  it every time, so the trials use stage 3's driver level, one it does not always win.

## Global constraints

- Design: "It does not issue a model call per frame or replace reliable input routines." The
  agent chooses strategy at checkpoints; routines issue every command.
- Design: "Record decisions and issued actions separately; make malformed responses and
  exhausted decision budgets explicit controller outcomes."
- Design: "Agent output is not assumed deterministic. Reproduction feeds the recorded merged
  input frames ... without calling the controller again." The journal format is unchanged.
- Design: "Keep provider/network code outside simulation and controller logic."
- The agent sees only what the player sees: checkpoint summaries are built from
  `PlayerObservation`, never from `WorldState`, the trigger tree or the database.
- No simulation behavior or hash change. With no supervisor reply, a run must reproduce stage 3's
  bot exactly: the `baselines.tsv` rows recorded at the commit this plan starts from reproduce
  byte for byte (stage 3's Task 2 re-records them on purpose, so the revision is named then).
- One run per server process. The server writes only under its output root
  (default `build/player-runs/`), reads scenarios only under `scenarios/`, opens no
  network port, and stops every run it started when the client disconnects.

### Provider neutrality (owner requirement)

- Transport: stdio only (JSON-RPC over stdin/stdout). Logs go to stderr.
- Tools only. No dependence on sampling, elicitation, roots, prompts or resources, which
  clients support unevenly. The play instructions are also the `dk2_rules` tool, which a
  client calls like any other tool; an MCP prompt with the same text is optional extra.
- Every tool has a JSON input schema with descriptions, returns text content that is valid
  JSON (plus structured content where the SDK supports it), and stays under a byte bound.
- No client-specific names, wording or behaviors in tool text. The run records the client's
  `clientInfo` name and version from MCP `initialize`, so evidence says which agent played.
- Registration is documented for Claude Code (`.mcp.json`, as for `dk2kb`) and Codex
  (`~/.codex/config.toml` `[mcp_servers.dk2player]`). The self-test uses the SDK's generic
  client, not any vendor's.

## .NET skills

Read and apply `run-tests`, `test-value-evidence`, `csharp-refactoring` (task 1),
`dotnet-breaking-changes` (task 1's options change), `msbuild` (task 4's new project) and
`analyzing-dotnet-performance` (checkpoint summary and serialization). Use Context7 for the
`ModelContextProtocol` 1.0.0 API before writing task 4; do not code it from memory.

## Entry points and file ownership

| Task | Owns |
|---|---|
| 1 | `src/DK2.AI/Players/PlayerStrategy.cs` (new), `CampaignPlayer.cs`, `CampaignPlayerOptions.cs`, `PlayerActionRoutines.cs`; `tests/DK2.AI.Tests/Players/PlayerStrategyTests.cs` |
| 2 | `src/DK2.AI/Players/SupervisedPlayer.cs`, `ISupervisor.cs`, `PlayerCheckpoint.cs` (new); `tests/DK2.AI.Tests/Players/SupervisedPlayerTests.cs` |
| 3 | `src/DK2.Harness/PlayerRunCommand.cs`, `PlayerRunReport.cs`, `SupervisorLog.cs` (new); `tests/DK2.Behavior.Tests/SupervisedRunTests.cs` |
| 4 | `src/DK2.PlayerMcp/` (new project, added to `DK2.slnx`); `tests/DK2.PlayerMcp.Tests/` (new) |
| 5 | `.mcp.json`, `src/DK2.PlayerMcp/README.md`, `docs/HANDOFF.md`, harness and scenarios READMEs |
| 6 | evidence under `F:/DK2Evidence/studies/`; plan as-built text |

## Task 1: a strategy the routines read

**Moved (2026-11-14)** to Task 1 of the [stage 3 plan](2026-11-14-campaign-bot-stage3.md), which
builds `PlayerStrategy` and `Apply` first; this plan uses them as built there.

## Task 2: checkpoints and the supervisor contract

**Produces** (in `DK2.AI.Players`, netstandard2.1, no I/O):

- `PlayerCheckpoint`: `Tick`, `Reason`, `Summary` (a `CheckpointSummary`), `Strategy` (current),
  `Budget` (checkpoints and malformed replies left), `Sequence` (1, 2, ...).
- `CheckpointReason`: `Start` (first playable observation), `Interval` (every `CheckInTicks`,
  default 600), `FirstEnemySeen`, `RoomBuilt`, `Stalled` (the routines would have reported a
  limitation), `CreatureLost`, `Ended` (a result; informational, no reply changes the run).
- **Precedence with playbooks (stage 3, Task 5):** a supervisor's strategy change holds until the
  playbook's next phase change, which wins.
- `CheckpointSummary`, built only from `PlayerObservation` and the bot's own state: gold, mana,
  game phase; the playbook phase (name and index) that is active; own creatures by
  type with counts held, fighting and idle; own rooms by type and size; panel offers; visible
  enemies (type, owner, tile, count by tile); explored and owned tile counts; notices since the
  last checkpoint (kind and order only, as the observation carries); receipts refused since the
  last checkpoint, grouped by reason.
- `ISupervisor.Consult(PlayerCheckpoint) -> SupervisorReply`. A reply is `Continue`,
  `Change(PlayerStrategy)`, or `GiveUp(reason)`. `Malformed(text)` is how a transport reports an
  unparseable answer.
- `SupervisedPlayer(CampaignPlayer routines, ISupervisor supervisor, SupervisionLimits limits)`
  implements `IPlayerController`. At each checkpoint it consults the supervisor before the routines
  decide, applies a valid change, and records `(checkpoint, reply, outcome)` through an
  `ISupervisionRecorder`. Limits: `MaxCheckpoints` (default 60), `MaxMalformed` (default 3),
  `CheckInTicks` (default 600). Exhausted checkpoints mean the run continues on the last strategy
  with no more consultations, recorded once. Too many malformed replies, or `GiveUp`, is a
  `PlayerDecision.Limited` naming why. `Stalled` replaces the routines' own limitation: the
  supervisor may change strategy; `Continue` at a stall ends the run as the routines would have.

- [ ] Write `SupervisedPlayerTests` with a scripted `ISupervisor`: checkpoints arrive in order
      with the right reasons; a change takes effect on the same decision; malformed replies count
      and the third ends the run as a limitation; `GiveUp` ends it; the checkpoint budget stops
      consultations but not the run; the summary contains no thing the observation does not.
- [ ] Write a test that `SupervisedPlayer` with a supervisor that always answers `Continue`
      makes the same decisions as the bare `CampaignPlayer` (no consultation side effects).
- [ ] Implement; measure the summary's size and build time on a Level 20 observation
      (`analyzing-dotnet-performance`); budget it from that measurement.
- [ ] Reviewer, documenter, commit.

## Task 3: supervised runs in the harness, with evidence

**Produces:** `PlayerRunCommand.Run(..., ISupervisor? supervisor)` builds a `SupervisedPlayer`
when given one; the CLI path without a supervisor is unchanged. Evidence in the run folder:

- `supervisor.jsonl`, one line per checkpoint: sequence, tick, reason, SHA-256 of the summary
  sent, its byte count, the raw reply text, the parsed reply, the validation result, the strategy
  applied, and wall milliseconds waited.
- `player-run.json` gains `supervision`: client name and version, checkpoints used, malformed
  replies, budget exhausted or not, total wait seconds.
- Journal header keys `player.supervisor` (client name/version, or `none`) and
  `player.pacing` = "headless: the simulation waits at completed ticks for each decision and
  checkpoint".
- Acceptance: a supervised run may claim acceptance under the same conditions as today. The
  claim line adds the supervisor's name; the replay still needs no supervisor.

- [ ] Write `SupervisedRunTests` (synthetic data, scripted supervisor): the folder holds
      `supervisor.jsonl` with one line per checkpoint; the journal replays without the
      supervisor and matches every hash; a malformed-reply run ends `ControllerLimited` with the
      reason in the verdict; a supervisor that throws is a recorded limitation, not a crash.
- [ ] Implement; keep the per-tick path free of JSON (serialize only at checkpoints).
- [ ] Reviewer, documenter, commit.

## Task 4: the MCP server

**Produces:** `src/DK2.PlayerMcp` (net10.0 executable, references `DK2.Harness`, package
`ModelContextProtocol` 1.0.0). `Program` hosts the stdio server. One `RunHost` owns at most one
run on a worker thread. An `McpSupervisor : ISupervisor` blocks the worker on a checkpoint until
the client answers through `dk2_decide`, or until `ReplyTimeoutSeconds` (default 600) runs out;
a timeout is recorded and the run ends as a limitation. The simulation waits meanwhile.

Tools (names are stable API; every result is one JSON object under `MaxResponseBytes`, default
16 KiB):

| Tool | Input | Result |
|---|---|---|
| `dk2_rules` | none | how a run works: the checkpoint loop, the strategy schema with every field and allowed value, limits, what the agent can and cannot see, how to end |
| `dk2_start_run` | `scenario` (path under `scenarios/`), `player` (keeper id), optional `run_id`, optional limits | run id, output folder, and the first checkpoint (blocks until it arrives or the run ends) |
| `dk2_checkpoint` | none | the pending checkpoint, or `running` (with tick) or the final verdict |
| `dk2_observe` | `section`: `tiles` (with an optional window), `creatures`, `rooms`, `things`, `offers` or `notices` | that part of the current observation, player-visible only, bounded |
| `dk2_decide` | `action`: `continue`, `change` (with a `strategy` object, fields as in `dk2_rules`) or `give_up` (with `reason`); `note` (≤ 256 chars, logged) | the validation result, then the next checkpoint or the final verdict (blocks up to `WaitSeconds`, default 120, returning `running` if none yet) |
| `dk2_cancel` | `reason` | the verdict of the stopped run (`IncompleteEvidence`, as a wall stop is) |
| `dk2_verdict` | none | the finished run's `player-run.json` summary and folder |

A `change` that fails validation is returned to the agent with every problem and counts as a
malformed reply. `dk2_start_run` refuses a second run while one is active. A client disconnect
cancels the active run and writes its verdict.

- [ ] Confirm the SDK 1.0.0 server and client APIs through Context7; record the calls used.
- [ ] Create the project (`msbuild` skill): `DK2.slnx` entry, `Directory.Build.props`
      inheritance, nullable on, warnings as errors as for the harness.
- [ ] Write `tests/DK2.PlayerMcp.Tests` against the real executable over stdio with the SDK's
      generic client:
      - `ListTools` returns exactly the seven tools, each with an input schema;
      - a synthetic scenario played by a scripted client reaches a verdict, and its run folder
        replays without the server;
      - malformed `change`, an unknown tool argument and an oversized observation request are
        refused with explicit errors;
      - a path outside `scenarios/` is refused;
      - a disconnect mid-run leaves a verdict file;
      - two `dk2_start_run` calls give a refusal for the second.
- [ ] Implement `RunHost`, `McpSupervisor` and the tools; JSON with System.Text.Json source
      generation; no reflection-based serialization in the server's hot path.
- [ ] Reviewer, documenter, commit.

## Task 5: registration and documentation

- [ ] `.mcp.json`: a `dk2player` stdio entry running the built server
      (`dotnet src/DK2.PlayerMcp/bin/Release/net10.0/DK2.PlayerMcp.dll`), like `dk2kb`.
- [ ] `src/DK2.PlayerMcp/README.md`: what it is, the tools, the loop, limits, evidence, the
      Claude Code and Codex registration snippets, and what the agent cannot see.
- [ ] HANDOFF, harness and scenarios READMEs: entry points and limits. No claim that the agent
      "knows" a level; it chooses among the routines' strategies.
- [ ] Reviewer, documenter, commit.

## Task 6: live trials and disclosure

- [ ] Baseline: stage 3's unsupervised `run --campaign-player 3` on stage 3's driver level, seed 1, using
      stage 3's scenario for that level. Disclose the outcome, whatever it is.
- [ ] The driver level, seed 1, supervised from a Claude Code session through `dk2player`. Disclose the
      outcome, checkpoints used, malformed replies and wall time.
- [ ] The same from a Codex session where Codex is installed; otherwise record that the second
      client was the SDK test client only.
- [ ] Level 1, seed 1, supervised, to show supervision does not break the known win.
- [ ] Replay every journal without any supervisor; all hashes must match.
- [ ] Preserve receipts, run folders and `supervisor.jsonl` files add-only under
      `F:/DK2Evidence/studies/` with a SHA-256 inventory. A supervised driver-level loss is a
      finding, not a failure of this stage; tuning simulation behavior to win is out of scope.
- [ ] Full gates with `DK2_REQUIRE_INSTALL=1`; reviewer; documenter; commit; merge by the
      owner's instruction.

## Out of scope

Rendered or Unity play (stage 5), the real UI (stage 6), skirmish maps (stage 8), per-frame model control, model
training, a remote provider API, network transports, and changing DK2 behavior to help the
agent win.
