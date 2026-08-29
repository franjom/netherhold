# The simulation loop and the determinism contract

Milestone M2. This is the first code in the project that is genuinely ours
rather than a reading of DK2's data, so it is worth being explicit about what
it does and does not claim.

## What it claims

> Same database + same initial world + same seed + same input frames, in the
> same order, produce the same `StateHash()` at every tick — on any machine, in
> any process.

Nothing more. It is **not** a claim about how DK2 orders its own updates, how
fast its tick runs, or what its systems do. Those are still open questions
(Q1 in `docs/research/open-questions.md`).

## Why it has to exist before any mechanic

Every fidelity claim from M3 onwards has the shape "with this input, our
simulation does X, and so does DK2". If our own simulation does not reliably do
X twice in a row, the comparison is meaningless and the regression corpus
records noise. The gate is cheap now and impossible to retrofit later.

## The pieces

| Type | Role |
|---|---|
| `GameSimulation` | Owns the loop. `Tick(in InputFrame)` is the only way time moves. |
| `WorldState` | All mutable state: tiles, players, current tick. |
| `ISimulationSystem` | One unit of per-tick work. Runs in explicit registration order. |
| `SimulationContext` | The only things a system may touch: database, world, input, RNG, event sink. |
| `InputFrame` / `PlayerCommand` | Everything entering the simulation on a tick. |
| `SimEvent` / `ISimulationEventSink` | Write-only observation. Never read back. |
| `WorldSnapshot` | Binary save/load, including RNG position. |
| `Hash64` | FNV-1a structural hash, stable across processes and machines. |

Tick N executes with `context.Tick == N`; afterwards `World.CurrentTick` is
N+1. That ordering is our convention, chosen for legibility. The original's
internal update order is UNKNOWN and becomes a research question when it starts
to matter (M4).

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
asserts an exact literal. Its job is to catch the silent killers of
cross-machine reproducibility — string hashing, dictionary iteration order,
struct layout drift — which every other determinism test would happily pass,
because they compare two runs *in the same process*. CI runs it on Linux and
Windows, which is what turns the local property into a cross-platform one.

Changing simulation state deliberately will break it. That is correct: bump
`GameSimulation.HashVersion` and update the literal in the same commit. Breaking
it any other way means determinism is broken and the change must not ship.

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

**A test-only chaos system keeps the gate honest.** No real systems are
registered yet, so an empty world never changes and "both runs agree" would be
trivially true — the gate would pass with a broken loop, a broken hash or a
broken snapshot. `ChaosSystem` (in the test assembly, and never anywhere else)
consumes randomness and mutates state every tick so that agreement is only
possible if everything underneath really is deterministic. It implements no DK2
mechanic and never will.

## What M2 deliberately does not contain

No gameplay. `GameSimulation.Systems` is empty in every non-test configuration,
and the scenario runner says so in its report rather than producing output that
looks like a finished experiment. Digging, claiming and the first Imp are M3 —
and the numbers they need are already extracted
(`docs/research/kwd-terrain-rooms.md`, `kwd-variables.md`).

`TileState` and `PlayerState` carry only fields justified by parsed data today.
Room membership, dig tagging, gold in seams, creature rosters and the rest
arrive with the mechanics that read them, not before.
