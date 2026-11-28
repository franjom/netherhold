---
name: housekeeping
description: Find and remove text and files that no longer give value - finished plans, obsolete research narrative, stale task lists, retired-adapter docs, old local run output and scratch folders. Use when the owner asks for housekeeping or a cleanup, after a plan or workstream finishes, and before a session ends that produced reports, briefs or run output.
---

# Housekeeping

The owner's rule (2026-11-28): anything that does not give value goes. Git
history keeps every deleted tracked file, so a deletion is cheap to undo; the
cost of keeping dead text is every later reader and agent wading through it.

## What stays

- **DK2 behaviour evidence**: what DKII.EXE or the running game does, when it
  is not recorded elsewhere. The function knowledge base
  (`docs/research/functions/`) and `docs/DECISIONS.md` always stay.
- **Current reference**: HANDOFF, ARCHITECTURE, TRAPS, PERFORMANCE and the
  other docs agents read before working; READMEs of code that exists.
- **Open or future work** that someone will pick up: active plans, open
  questions, the next step of a running workstream (check the worktree
  branches before calling a plan dead).
- **Native evidence** (captures, packet logs, capture extracts, replay rigs):
  it lives in `F:\DK2Evidence` (AGENTS.md, HARD RULE). A local copy may only
  go once the evidence store holds a SHA-256-verified copy.

## What goes

- A plan whose work is done: its outcome is in code, an ADR or a reference
  doc. Move any still-open item to HANDOFF (or an ADR) first, then delete it.
- Session narrative, progress logs, one-off measurement reports and audits
  whose conclusion is recorded elsewhere.
- Superseded drafts, duplicates, stubs, finished TODO lists.
- Anything that only describes removed code or a retired tool or adapter.
- Local ignored output: run directories, probe projects, decompile dumps,
  converted assets, old packages and scratch - anything a command regenerates.

## Procedure

1. **Inventory.** Tracked text by folder, ignored files by folder with size and
   newest date. Skip folders another session is writing today, and worktree
   branches' own files.
2. **Classify** each candidate as delete, merge (move the live part first) or
   keep, with a one-line reason. For a tracked file, count who links to it
   (`git grep -l <name>`): code comments, KB records, ADRs, docs.
3. **Native evidence check.** For every local folder to delete, look for native
   material (`*.dk2cap`, saves, packet logs, oracle reads, native draw logs).
   Copy it to `F:\DK2Evidence\archive\<name>--<date>--001\`, compare SHA-256,
   and keep the local copy until the owner confirms.
4. **Ask once.** Show the owner the full delete list (every path, ignored ones
   included), the merges and the evidence copies. A bulk delete needs that
   approval in the current conversation (AGENTS.md).
5. **Delete and repair.** Remove tracked files with `git rm`; fix or drop every
   inbound link so the docs gate stays clean; move merged facts first. Delete
   local paths with plain `rm` on literal paths.
6. **Commit by path** (`git commit -- <paths>`), then `git show --stat HEAD`.
   One commit per area keeps a mistaken deletion easy to revert.

## Habits that keep the pile small

- Write findings into the doc they belong to, not into a new dated file.
- When a plan's last box is ticked, fold what is still open into HANDOFF and
  delete the plan in the same change.
- Put run output under the OS temp directory, not in the clone, and delete it
  when the report is written.
