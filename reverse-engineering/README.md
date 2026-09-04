# reverse-engineering/

Working space for decompilation of the original `DKII.EXE`. **Everything in
here except this file is gitignored, deliberately and permanently.**

Authorised by the owner on 2026-09-04: *"about ghidra, you are free to use it
and decompile it, put all decompiled resources under reverse-engineering
folder."* The rule that governs what may leave this folder is **ADR-0046**.

## The one rule

**Nothing in this folder is a source of code. It is a source of facts.**

| | |
|---|---|
| May cross into the repository | A number, a formula, a comparison, an order of operations, a description of control flow — **written in our own words**, in `docs/research/`, with provenance saying it came from the binary |
| May **not** cross, ever | Decompiler output itself. Not into `src/`, not into `docs/`, not into a commit message, not into a test fixture. Not reformatted, not renamed, not "cleaned up" |

The reason is the brief, not caution for its own sake: the project is a
clean-room *behavioural* reimplementation, and it was told not to copy original
source code merely because reverse engineering makes it available. Decompiler
output **is** source derived from the binary. A formula read out of it is a
fact about how the game behaves; the function it was read from is EA's
expression of that fact.

## Rank of evidence

**This folder is the FIRST place to look, not the last.** Owner, 2026-09-04:
*"I want you to make using ghidra and decompiled sources as the primary source
and go to when in doubt."* `DKII.EXE` is the actual DK2; OpenKeeper, Flame, the
wiki and this project's own inferences are all somebody's *reading* of it. An
open question owes a Ghidra pass before it is filed as UNKNOWN. See ADR-0046's
2026-09-04 amendment.

**Consulted first is not the same as trusted most.** For behaviour the owner can
watch, the running game still wins: a misread disassembly is still a misread,
and the decompiler is an inference engine — it invents variable names, guesses
types, and silently mismodels optimised float and fixed-point arithmetic. Where
the two disagree, **the running game wins** and the disagreement gets written
down. `[Dk2Behavior]` CONFIRMED remains reserved for the running game.

Where the binary and the *shipped data* disagree, that is interesting rather
than settled — ADR-0023 already deliberately disbelieves one shipped value.

## What is in here

Ghidra project: `D:\ghidra projects\DK2_RE` (outside the repo — a Ghidra
project of this binary contains the binary).

Exports land here as `<subject>.c` / `<subject>.txt`, one file per question,
named after the question they were opened for.

## Contents

_(nothing committed, by design — `git status` should never show a file here)_
