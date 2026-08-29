# Dungeon Keeper 2 Reimplementation

## Mission

Create a behaviorally faithful reimplementation of Dungeon Keeper 2
using C# and Unity.

## Critical rule

Do not invent original DK2 behavior when it can be researched.

Distinguish:

- CONFIRMED
- STRONGLY INFERRED
- HYPOTHESIS
- UNKNOWN

## Existing research

Before implementing DK2 behavior, check:

1. OpenKeeper
2. Flame
3. Ember
4. DK2 reverse-engineering documentation
5. Original game experiments

## Architecture

Game simulation must remain independent from Unity wherever practical.

Unity is presentation/integration.

## Original assets

Original DK2 assets may be used for development/reference.

Do not assume they are redistributable.

All asset access must go through an abstraction layer.

## Fidelity

Do not "fix" strange DK2 behavior without documenting the deviation.

## AI collaboration

If you discover something important:

DOCUMENT IT.

Do not leave important knowledge only inside chat history.

## Session handoff

Read docs/HANDOFF.md first: current state, next steps, decisions already made,
environment gotchas, and the research method in use.

## Before changing architecture

Read:

docs/ARCHITECTURE.md
docs/DECISIONS.md

## Before implementing a reverse-engineered mechanic

Read:

docs/RESEARCH_STATUS.md
docs/EXPERIMENTS.md