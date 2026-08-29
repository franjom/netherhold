# Architecture Decision Records

## ADR-0001 — Integer/fixed-point gameplay math
**Date:** 2026-08-29 · **Status:** accepted

DK2 v1.70 mixes integer math with x87 floats (Flame ships `fpu_control.h`).
Bit-exact x87 reproduction on modern hardware is not tractable. We use
integer/fixed-point for all gameplay-affecting quantities; floats are
presentation-only. Trades unachievable float-exactness for achievable
cross-platform determinism.

## ADR-0002 — Simulation independent of Unity, enforced at compile time
**Date:** 2026-08-29 · **Status:** accepted

Simulation-side assemblies target netstandard2.1 (Unity-loadable) and are
forbidden from referencing Unity. Enforcement: `ArchitectureTests` fails the
test run if any simulation assembly references an assembly named `Unity*`.

## ADR-0003 — MIT license; clean-room use of reference projects
**Date:** 2026-08-29 · **Status:** accepted (owner decision)

This project is MIT-licensed. Consequences:
- **OpenKeeper (GPLv3):** no code may be copied or transliterated. Facts
  learned from it (format layouts, enum values, record ids) may be used.
- **Flame (no license):** same rule, stricter reason — unlicensed means no
  reuse rights at all. Its symbol maps are consulted as research material.
- Every research note in `docs/research/` records provenance so any borrowed
  *fact* can be traced to its source.

## ADR-0004 — xoshiro256** as the simulation RNG
**Date:** 2026-08-29 · **Status:** accepted

Public-domain, fast, trivially serialisable, platform-independent. Deliberately
NOT DK2's original generator (which is UNKNOWN). If fidelity work later shows
the original RNG's sequence matters observably, a second generator goes behind
the same `SimRandom` surface.

## ADR-0005 — .slnx solution format
**Date:** 2026-08-29 · **Status:** accepted

.NET 10 default. No tooling in this project requires the legacy `.sln`.
