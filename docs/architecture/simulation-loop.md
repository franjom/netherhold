# The simulation loop and the determinism contract

Milestone M2, and still the contract every later milestone rests on. It is code
that is ours rather than a reading of DK2's data, so it says what it does and does
not claim. The layering and the world's contents are in `docs/ARCHITECTURE.md`.

## What it claims

> Same database + same initial world + same seed + same input frames, in the
> same order, produce the same `StateHash()` at every tick — on any machine, in
> any process.

Nothing more. It is **not** a claim about how DK2 orders its own updates or what
its systems do. DK2's top-level frame (things, then room ticks, then players) is
read and ours keeps the room-tick slot (`docs/ARCHITECTURE.md` §System order,
ADR-0479); the order within it is declared rather than measured (ADR-0013,
`SimulationSystems`). DK2's tick *rate* is no longer open:
it is 4 ticks a game second, a tick of 250 ms, read from the binary and observed
in a running process (ADR-0089), and `SimulationTiming` is the one place that
relates ticks to seconds (ADR-0011).

## Why it has to exist before any mechanic

Every fidelity claim from M3 onwards has the shape "with this input, our
simulation does X, and so does DK2". If our own simulation does not reliably do
X twice in a row, the comparison is meaningless and the regression corpus
records noise. The gate is cheap now and impossible to retrofit later.

## The pieces

| Type | Role |
|---|---|
| `GameSimulation` | Owns the loop. `Tick(in InputFrame)` is the only way time moves. Holds `HashVersion`. |
| `WorldState` | All mutable state, and `HashInto` is the list of it (`docs/ARCHITECTURE.md` §World state). |
| `ISimulationSystem` | One unit of per-tick work. Runs in explicit registration order. |
| `SimulationContext` | The only things a system may touch: database, world, input, RNG, event sink, navigation, and the trigger hooks. |
| `InputFrame` / `PlayerCommand` | Everything entering the simulation on a tick. |
| `SimEvent` / `ISimulationEventSink` | Write-only observation. Never read back. |
| `WorldSnapshot` | Binary save/load, including RNG position. Carries `FormatVersion`, kept equal to `HashVersion`. |
| `Hash64` | FNV-1a structural hash, stable across processes and machines. |

Tick N executes with `context.Tick == N`; afterwards `World.CurrentTick` is
N+1. That ordering is our convention, chosen for legibility. Within `Tick`: player
messages are acknowledged and swept (ADR-0245), the trigger executor runs for DK2
turn N+1 (ADR-0152) and the level time limit counts down, the registered systems
run in order, the trigger emitter records the turn, and the clock advances.

`SimulationSystems.Default()` is the one declaration of which systems run and in
what order (ADR-0013). It is what the scenario runner, the Unity `DungeonRunner`
and the real-data tests all construct, so a scenario and the test suite can never
disagree about ordering. A test that needs one system absent removes it from that
array rather than building its own.

## Design decisions worth knowing

**The RNG's position is part of the state hash.** Two runs can hold identical
world state while having consumed a different number of random draws. Without
this they would compare equal now and diverge later — precisely the failure the
gate exists to catch early, arriving at the point where it is hardest to
diagnose.

**The hash is FNV-1a, never `GetHashCode`.** .NET randomises string hash codes
per process, so a hash built from `GetHashCode` would be stable within a run and
worthless across runs — a gate that passes locally and cannot be reproduced on
CI or on a contributor's machine.

**One pinned hash value.** `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines`
asserts an exact literal (its pin history: `docs/research/hash-versions.md`). Its job is to catch the silent killers of
cross-machine reproducibility — string hashing, dictionary iteration order,
struct layout drift — which every other determinism test would happily pass,
because they compare two runs *in the same process*. `.github/workflows/ci.yml` runs it
on Linux and Windows for every push to master and every pull request (GitHub, since 2026-10-24),
so the pin holds on both platforms. CI has no game install, so campaign-level determinism
still rests on local runs (`docs/ARCHITECTURE.md`).

Changing simulation state deliberately will break it. That is correct: bump
`GameSimulation.HashVersion`, bump `WorldSnapshot.FormatVersion` to match, and
update the literal, all in the same commit. Breaking it any other way means
determinism is broken and the change must not ship.

**Events cannot affect behaviour.** Systems write to an `ISimulationEventSink`
and never read from one. `DeterminismTests.EventRecording_DoesNotAffectState`
asserts that attaching a recorder leaves the state hash untouched, so the
observation apparatus can never become part of the thing being observed.

**Save/load is verified by continuation, not by byte equality.** Snapshotting a
run, restoring it and checking the hashes match only proves the *visible* state
survived. `SnapshotTests.RestoredRun_ContinuesIdentically` runs both forward 500
ticks and requires they never diverge, which proves the snapshot captured
everything the simulation actually depends on. Our save format is our own; DK2's
is not a target (plan risk R13).

**A test-only chaos system keeps the gate honest.** `ChaosSystem` (in
`tests/DK2.Simulation.Tests`, and never anywhere else) consumes randomness and
mutates state every tick, so agreement between two runs is only possible if the
loop, the hash and the snapshot really are deterministic. It was written when no
real systems existed and an empty world made "both runs agree" trivially true;
it stays because the gate still registers no real system: the two creatures now
in its world (Q206) are moved by nothing else, so without it the pin would be
watching a world at rest.

It implements no DK2 mechanic and never will.

## What the loop deliberately does not decide

`TileState` and `PlayerState` carry only fields a built mechanic reads. Room
membership is derived rather than stored (`RoomInstance`, ADR-0027); state
arrives with the mechanic that reads it, not before. `TriggerState` shows the rule
working in the other direction: a flag bank only a trigger *reader* would need was
still added, because an action record writes a flag a later condition reads, so two
worlds disagreeing about one will diverge (ADR-0102); `TriggerExecutor` walks the
roots at the top of every `Tick` (ADR-0152).
