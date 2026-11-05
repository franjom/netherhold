# Charter

What this project is for, what it is not for, and what "faithful" means here.
The long form is the twelve goals in
[`AGENTS.md`](https://github.com/franjom/netherhold/blob/master/AGENTS.md#goal-1--modern-dungeon-keeper-2-engine)
and where each one stands in
[`GOAL_ALIGNMENT.md`](https://github.com/franjom/netherhold/blob/master/docs/GOAL_ALIGNMENT.md).

## The mission

A behaviourally faithful reimplementation of **Dungeon Keeper 2** in C#, with
Unity as the presentation layer. What is preserved is the game's **playable
behaviour and simulation**, so that it outlives the operating systems, graphics
APIs and hardware the original was written for (goal 12).

## Goals

1. **A modern DK2 engine**: the game's rules, run by a simulation of our own.
2. **Campaign compatibility**: the original levels load and play, from your own installation.
3. **Modern rendering** of the original game, its design preserved.
4. **Replaceable assets**: everything is reached through an asset layer, so original art can be swapped for new.
5. **High behavioural fidelity**: the same inputs give what the original gives.
6. **Original-versus-ours testing**: runs of our simulation compared with runs of DK2 itself.
7. **A level editor.**
8. **A modding framework.**
9. **Deterministic, fast simulation**: the same inputs give the same state on every machine, faster than real time (see [[Determinism]]).
10. **An AI and bot interface**: anything that can send commands can play a keeper.
11. **Several presentation modes**: headless, text and Unity over one view of the world.
12. **Preservation.**

These are end states. They do not dictate what is built first:
[`HANDOFF.md`](https://github.com/franjom/netherhold/blob/master/docs/HANDOFF.md)
says what is being built now, and the
[plan](https://github.com/franjom/netherhold/blob/master/DK2_REIMPLEMENTATION_PLAN.md)
gives the milestones.

## Non-goals

- **Shipping DK2's files.** The repository holds no original game data. The
  game reads everything from your own installation at run time (see
  [[Legal and Clean Room|Legal-and-Clean-Room]]).
- **Loading DK2's saves.** We have our own save format; the original's layout
  buys nothing behavioural (ADR-0007).
- **Redesigning the game.** A modern asset re-expresses DK2's design at higher
  fidelity and never changes it; photorealism is out of scope (ADR-0242,
  [`ART_DIRECTION.md`](https://github.com/franjom/netherhold/blob/master/docs/ART_DIRECTION.md)).
- **Fixing DK2.** A strange original behaviour is kept. Where we do differ, the
  difference is written down as an ADR.
- **Bit-exact floating point.** DK2 mixes integer and x87 float maths. We use
  integer and fixed-point maths instead, and trade float-exactness, which can't
  be reproduced, for determinism across machines, which can (ADR-0001).
- **Multiplayer**, for now. Determinism makes it possible later; it is out of
  scope until after milestone M10 (plan risk R20).

## What "faithful" means

**Behaviour comes from the original, not from memory of it.** The binary,
`DKII.EXE`, is the primary source. It is read in Ghidra, together with the
shipped data files, before a mechanic is built. Community sources come after
(AGENTS.md, ADR-0046). Every balance number comes from the game database; a
constant written into simulation code is a bug.

**Every claim says how it is known.** One scale is used in research notes,
function records and code (ADR-0642):

| Level | Means |
|---|---|
| CONFIRMED | seen in the running game: an owner observation, a stopwatch, a native capture |
| STRONGLY INFERRED | read in the binary or the shipped data |
| HYPOTHESIS | fits the evidence, with a step nobody has read |
| UNKNOWN | DK2's answer is not known |

**Faithful is measured, not asserted.** Where it can be, our simulation is run
against DK2's own: its route log, its world log, native captures, and recorded
sessions replayed headlessly (goal 6). A rule that has only been read is
STRONGLY INFERRED until a run shows it.

**The view is faithful too.** Visual design follows the same rule as gameplay:
preserve the design, increase the fidelity. For animation, preserve the motion
and modernise the representation.

**A deliberate difference is a decision.** It gets an ADR saying what DK2 does,
what we do and why. Each system page lists its own under *Divergences*.
