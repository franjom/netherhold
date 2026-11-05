# Determinism

The simulation makes one promise. Comparing it with DK2, testing it and
replaying a session all rest on that promise:

> Same database, same initial world, same seed and the same input frames in the
> same order produce the same state hash at every tick, on any machine, in any process.

It is a promise about our own simulation, not a claim about how DK2 orders its
work. The full contract is in
[`simulation-loop.md`](https://github.com/franjom/netherhold/blob/master/docs/architecture/simulation-loop.md);
the layering is in
[`ARCHITECTURE.md`](https://github.com/franjom/netherhold/blob/master/docs/ARCHITECTURE.md).

## Ticks

Time moves only through `GameSimulation.Tick(in InputFrame)`. A tick is 250 ms,
four ticks a game second, which is DK2's own rate (ADR-0089). The rate was read
from the binary and the shipped data and checked with a stopwatch against the
running game. Systems run in one declared order, `SimulationSystems.Default()`,
written in one place (ADR-0013). The
[[Simulation Read-Write Matrix|Simulation-Read-Write-Matrix]] shows what each
system reads and writes.

## Fixed point, not floats

Every gameplay quantity is an integer or fixed-point number (ADR-0001). `Fp32`
is DK2's own 32-bit fixed point, a raw integer over 4096. A fraction that has to
carry over between ticks is kept as an integer remainder. Floats appear only on
the presentation side. DK2 itself uses x87 floats in places; reproducing them
bit for bit on modern hardware is not achievable, and determinism across
machines is.

## One source of randomness

`SimRandom` is the only random source the simulation may use. It is seeded
explicitly, and its position is part of the state. The generator is pluggable
(ADR-0275): a modern default, or DK2's own linear congruential generator.

## The state hash

`GameSimulation.StateHash()` hashes the whole `WorldState` plus the random
generator's position (ADR-0006, algorithm in ADR-0576). `GetHashCode` is never
used, since .NET randomises it per process. A test pins one exact hash value,
and CI checks it on Linux and Windows.

A change that deliberately alters simulation state raises `HashVersion` and
updates the pinned value in the same commit. Any other change to the hash is a
determinism bug. New state must be reachable from `WorldState`, included in the
hash and written to snapshots, or it is silently non-deterministic.

## Snapshots

`WorldSnapshot` saves and loads the world in a format of our own, including the
random generator's position (ADR-0007). It is tested by continuation, not by
comparing bytes: a snapshot restored into a fresh simulation must produce the
same state hashes as the original run, tick after tick. That proves the
snapshot holds everything the simulation depends on.

## The CI gates

[`ci.yml`](https://github.com/franjom/netherhold/blob/master/.github/workflows/ci.yml)
runs on every push to `master` and every pull request, on Linux and Windows:

| Gate | Checks |
|---|---|
| Architecture | the simulation assemblies do not reference Unity (`ArchitectureTests`) |
| Determinism | the pinned hash and snapshot continuation (`DeterminismTests`, `SnapshotTests`) |
| Test | the whole suite; tests that need a DK2 install skip without one, never pass (ADR-0100) |
| Docs and KB | documentation budgets, the function knowledge base, commit messages |

A test-only chaos system changes state and draws random numbers on every tick,
so the gate cannot pass with a broken loop, hash or snapshot (ADR-0006).

What CI cannot check: it has no game installation, so campaign-level runs and
packet replays are compared on one machine only.
