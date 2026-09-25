# reverse-engineering/

Working space for decompilation of the original `DKII.EXE`. **Everything in here
except this file is gitignored, deliberately and permanently** (`.gitignore`:
`reverse-engineering/**`, with one negation for this README). Authorised by the
owner on 2026-09-04: *"about ghidra, you are free to use it and decompile it, put
all decompiled resources under reverse-engineering folder."* The rule that governs
what may leave this folder is **ADR-0046**.

## The one rule

**Nothing in this folder is a source of code. It is a source of facts.**

| | |
|---|---|
| May cross into the repository | A number, a formula, a comparison, an order of operations, a description of control flow — **written in our own words**, in `docs/research/`, with provenance saying it came from the binary |
| May **not** cross, ever | Decompiler output itself. Not into `src/`, not into `docs/`, not into a commit message, not into a test fixture. Not reformatted, not renamed, not "cleaned up" |

The project is a clean-room *behavioural* reimplementation and was told not to copy
original source code merely because reverse engineering makes it available.
Decompiler output **is** source derived from the binary. A formula read out of it is
a fact about how the game behaves; the function it was read from is EA's expression
of that fact.

## Rank of evidence

**This folder is the FIRST place to look, not the last.** Owner, 2026-09-04: *"I
want you to make using ghidra and decompiled sources as the primary source and go to
when in doubt."* `DKII.EXE` is the actual DK2; OpenKeeper, Flame, the wiki and this
project's own inferences are all somebody's *reading* of it. An open question owes a
Ghidra pass before it is filed as UNKNOWN — ADR-0046's 2026-09-04 amendment.

**Consulted first is not the same as trusted most.** For behaviour the owner can
watch, the running game still wins: the decompiler is an inference engine that
invents variable names, guesses types, and silently mismodels optimised float and
fixed-point arithmetic. Where the two disagree the running game wins and the
disagreement gets written down; `[Dk2Behavior]` CONFIRMED stays reserved for the
running game.

Where the binary and the *shipped data* disagree, that is interesting rather than
settled — ADR-0023 deliberately disbelieves one shipped value.

## What is in here

Ghidra project: `D:\Ghidra\ghidra projects claude\DK2_RE` for Claude and
`D:\Ghidra\ghidra projects\DK2_RE` for Codex, opened with Ghidra 12.1.4 (outside the repo — a Ghidra project of
this binary contains the binary). Exports land here as `<subject>.c` /
`<subject>.txt`, one file per question, named after the question they were opened
for. The headless Ghidra command is in `docs/RESEARCH-QUEUE.md`; the scan recipes
and the traps that have cost committed claims are in `docs/HANDOFF.md`, *Research
method that has been working*.

## The binary carries RTTI

`DKII.EXE` ships MSVC run-time type information, so **any vtable can be traced to
the class it belongs to** — the dword immediately before a vtable is a complete
object locator, giving both the base's offset within the object (0 for the primary
vtable, non-zero for a secondary base under multiple inheritance) and a type
descriptor holding the decorated class name. `Rtti.py` walks it, and accepts either
a vtable base or any code pointer stored inside one, so it can be pointed straight
at a "1 absolute reference, in .rdata" scan result.

**What may leave this folder from it:** the addresses, the base offset, and whether
two vtables share a class. **The decorated names stay here.** They are the
original's own internal identifiers and they buy nothing a scan consumes — every
script here is driven by addresses, offsets and globals, never by a class name. A
whole-binary class map is a useful local asset and a large import of somebody else's
naming: generate it here if it is wanted, and let only addresses cross.

## Contents

Nothing is committed but this README, by design. `git status --short
reverse-engineering/` must show no other file before any commit.
