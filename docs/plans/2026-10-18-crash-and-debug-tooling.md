# Crash and debug tooling — plan for later

**Status: not started.** Written 2026-10-18, parked until there is time for it.
It is listed as gap 10 in `docs/GOAL_ALIGNMENT.md`. Item 1 is built as Stage B of
`2026-10-27-autonomous-playtesting.md` (ADR-0552), which also covers parts of items 2 and 5
for headless runs; change the two plans together.

## Why

DKII.EXE ships its own debug tools. It has ten field-named dumpers and a
call-count profiler (`docs/research/debug-dumpers.md`), a world log (`0x0050EA70`) and a route log
(`nav.log`), both behind globals. It has nine per-subsystem `Data Checksums`, an
out-of-sync handler (`0x00526590`) gated by `-LOGOOS`, and `-PSAVE`/`-PLOAD`
session recording. We use these as oracles. Our own game has none of its own for
the day the owner hits a crash or a wrong behaviour in Unity.

We should not copy DK2 one to one. Our simulation is deterministic, has a pinned
`StateHash()`, and keeps a snapshot invariant (restore, then identical hashes
forever after: `WorldSnapshot`). All input enters as `InputFrame`s. So **snapshot
plus input journal is a complete crash dump**: any failure can be replayed
headlessly, as many times as needed. DK2 could not do that, and its log-heavy
tools make up for it.

## What exists today

- `GameSimulation.StateHash()`, a single hash covering everything, with a pinned
  `HashVersion`.
- `WorldSnapshot` save and load, verified by continuation (`SnapshotTests`).
- `DungeonRunner` (Unity) collects `PlayerCommand`s into `InputFrame`s.
- `dk2tools` replay commands. They are driven by **DK2 captures**, not by our own
  play.
- `RunTimings` in the harness.
- Nothing hooks unhandled exceptions in Unity. Nothing records our own play.

## Items, in priority order

### 1. Input journal (highest value)

Write a compact binary file during play:

- Header: build id, `HashVersion`, level id, seed, the presentation settings
  that affect input, and the start tick.
- Body: `(tick, InputFrame)` for each tick that has commands. Every N ticks
  (for example 256), add a `StateHash` checkpoint so a replay can tell exactly
  where it diverged.
- Writer in `DK2.Simulation` or a new small engine-neutral assembly. Unity only
  calls it from `DungeonRunner` where `InputFrame`s are built.
- Reader: `dk2tools replay-journal <file> [--until <tick>] [--dump-at <tick>]`
  on the headless harness. Fail loudly on a `HashVersion` mismatch rather than
  replaying into a different simulation.
- Needs a `PlayerCommand` serializer. `KeeperViewSnapshot` and
  `WorldSnapshot` have binary writers to follow.
- Test: run a scenario with scripted commands, journal it, replay the journal,
  and assert the hash series is identical.

### 2. Rolling snapshot and crash capture

- Keep the last snapshot in memory (`WorldSnapshot`), taken every few minutes
  of game time. Cost to measure: the snapshot size and the time to take one.
- In Unity, hook `Application.logMessageReceivedThreaded` for errors and
  exceptions, `AppDomain.CurrentDomain.UnhandledException` and
  `TaskScheduler.UnobservedTaskException`. On a hit, write a folder with:
  the last snapshot, the journal from that snapshot's tick, the exception and
  stack, the build id, and a copy or path of `Player.log`.
- Native crashes: Unity already writes `Player.log` and a crash folder. Point
  the owner at it, or collect it on the next start if a "session did not end
  cleanly" marker is still present.
- A simulation exception inside the harness should produce the same bundle, so
  a headless failure and a Unity failure are handled the same way.

### 3. Per-subsystem hashes (needed before M14 multiplayer)

DK2's world log header has nine `Data Checksums` (Terrain, Room, Creature, Door,
Trap, Object, CreatureSpell, KeeperSpell, Shot). Split our `StateHash` the same
way: one hash per subsystem, combined into the existing total, so the pinned
total does not need a separate scheme.

Uses:
- A journal replay says *which* subsystem diverged first, not just that a
  divergence happened.
- M14 lockstep sends them for out-of-sync detection, playing the role of DK2's
  `-LOGOOS` handler. On a mismatch both peers write item 4's dump.
- Regression triage: a change that moves only the Creature hash is one kind of
  change.

Watch the cost: hashing per tick is already timed in `RunTimings`.

### 4. World dump on demand

This is our equivalent of `0x0050EA70`: readable text of every player, room,
creature (state id, job and needs, steering body), object and task pool at one
tick.
- Triggers: `--dump-at <tick>` in the harness and journal replay, and a debug
  key in Unity (development builds only).
- Use DK2's field names where the dumpers give them (`debug-dumpers.md` and the
  layout pages). That makes our dump diffable against `tools/worldlog.py`
  output from a DK2 world log of the same map.
- Write it outside the simulation assembly's hot path. It reads `WorldState` and
  writes nothing back.

### 5. Debug-build invariant checks

Per-tick assertions, compiled out of release builds with `[Conditional]` or a
build symbol. Examples:
- no creature inside a solid tile,
- no task with two owners,
- gold in treasuries plus gold carried equals the player's total,
- every entity slot reference points at a live entity.

These catch corruption on the tick it happens instead of thousands of ticks
later. They must not change state or consume RNG, or the hash would differ
between debug and release.

### 6. Analysis tools, with a GUI

Items 1–5 produce files. This item is about reading them: opening a crash
bundle or a journal and seeing what happened.

**Core, engine-neutral (`src/`, a new `DK2.Diagnostics` or similar):** one
library that every front end uses.
- `ReplaySession`: load a journal (with an optional starting snapshot), then
  `SeekTo(tick)`. Seeking restores the nearest keyframe and simulates forward.
  Keyframes are snapshots taken every K ticks during the first pass, kept in
  memory or cached on disk, so scrubbing stays fast.
- Queries at any tick: the entity list, one entity's full state (item 4's
  dump, structured rather than text), the event log within a tick range, the
  per-subsystem hashes (item 3).
- `Diff(a, b)`: two runs (journal against journal, our run against a converted
  DK2 world log, before against after a code change). Gives the first tick
  where each subsystem's hash differs, then a field-by-field diff of the
  entities at that tick.
- Bisect: given two builds and one journal, find the first tick where they
  diverge. This uses the checkpoints from item 1.

**Front end A: replay mode in Unity (primary).** The classic-look renderer
already draws the world, so do not build a second one.
- Open a journal or crash bundle from a debug menu or a command-line argument.
- A timeline bar: play, pause, step one tick, scrub, speed control. Markers on
  the bar for the crash tick, hash checkpoints, a first divergence, and
  filtered events (deaths, fights, task assignment).
- Click a creature, room or tile to open an inspector panel showing its live
  fields at the current tick. Pin fields to watch them change over time.
  Draw the creature's path, target and current task in the world.
- Overlays: navigation mesh, task pool, fog per player, ownership.
- The camera is free and fog can be turned off: the debug view should be
  omniscient.
- Development builds only. The Unity layer only displays what the core
  library returns.

**Front end B: lightweight, outside Unity (secondary).** For triage without
starting Unity, and for agents.
- `dk2tools` subcommands that print the core's results: `journal info`,
  `journal hashes`, `journal diff a b`, `journal bisect`, `dump <tick>`,
  `entity <id> --from <t1> --to <t2>` (one entity's fields over time as CSV).
- A local, static HTML report generated by `dk2tools journal report`: hash
  timeline per subsystem, event histogram, the first-divergence diff table, and
  a field-over-time chart for chosen entities. It is one file and needs no
  server, so it can be attached to a bug.
- A desktop GUI framework (Avalonia, WPF) is not recommended: it would be a
  third UI to maintain next to Unity and HTML.

**Order inside this item:** the core library and the CLI first, because tests
and agents can use them straight away. Then the HTML report. The Unity replay
mode comes once journals exist from real play.

## Not planned

- DK2's call-count profiler (`0x00526292`). `RunTimings` and the Unity profiler
  cover it.
- Matching DK2's log format byte for byte. Only the field names are worth
  matching.

## Constraints

- Engine-neutral (`AGENTS.md`): items 1, 3, 4 and 5 live in `src/`. Only the
  exception hook, the crash folder and the debug key live in Unity.
- Determinism: nothing here may read wall-clock time or RNG inside the
  simulation, or write to `WorldState`.
- Journal and snapshot formats are ours (plan risk R13), versioned by
  `HashVersion`.

## Suggested order

Items 1 and 2 together first: any Unity bug becomes a headless repro. Item 3
before M14 work begins. Items 4 and 5 when a hard bug calls for them. Item 6's
core and CLI can follow item 1 straight away; the Unity replay mode after that.
