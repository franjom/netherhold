---
systems: [creatures, rooms]
---
# Remembered Guard jobs

## Native rule and implementation scope

STRONGLY INFERRED from fresh reads of
[idle 89](functions/004778E0.md), the [Guard starter](functions/00462850.md),
[room capacity](functions/004E53B0.md) and the
[cursor pickup command](functions/00513480.md), including the relevant stores
and branches in disassembly. The preceding counterfactual controls are in
[Q710's fighter study](q710-fighter-decisions.md#inline-job-and-needs-controls-at-hash-519).

Idle 89 tries the dropped job, then the independent remembered job, before the
happy chooser. A refused remembered job is retained. Guard first tries its
saved room when live, owned, of the right type and with capacity; failure falls
through to its ordinary room search. Successful new routing writes the saved
room and remembered Guard job. Reuse does not rewrite them. Capacity adds one
for each matching assignment in the room owner's creature list, even for the
requesting creature and guards away from the room. Corpses are not members of
that list; held and dying creatures remain members.

Cursor command 57 clears both remembered fields after its live-creature lookup
and before the hand can refuse the lift. The computer keeper's direct prepare
and insert path does not perform that command clear. In the clone, interactive
permission rejection precedes command execution; stored packet commands clear
before hand-state refusal, while a released corpse record fails live lookup.

ADR-0806/hash and save 520 implement the bounded keeper-job starter behavior.
The write/retry integration is in `CreatureJobSystem`; other Guard starter paths
are unchanged. The fields are
`CreatureState.ReturnToJob` and `ReturnRoom`; ordinary job clearing preserves
them. Hashing, snapshot read/write and generated world copies retain both.
Older snapshots are refused under the existing version policy.

## Room identity adapter

`RoomReference` uses the existing room-list insertion identity, with a tile
fallback only for untracked fixture rooms. A tracked room can lose its original
anchor tile without losing the reference; a replacement insertion at the same
tile does not inherit it. Owner/type/capacity and route checks still apply.

This is not DK2's pooled room-tag allocation or reuse. The existing room finder,
navigation, Guard handlers and their state scratch remain adapters; this change
does not fully mirror the starter's additional state+0xAF room store. Function
records remain PARTIAL where those limits apply. Native merge, cut, owner
transfer and tag-reuse equivalence are not established by these tests.

## Verification

The regression sequence first admits Guard duty, interrupts ordinary work,
then asks idle 89 again. Tests distinguish remembered Guard from the Goblin's
fresh Training choice, saved-room reuse from a nearer room, and retained memory
from a failed request. Additional cases cover dropped-job precedence, full-room
fallback, dying/corpse reservations, cursor/direct pickup, hand refusal, tracked
room replacement, snapshots, hashing and copied-world continuation.

The full solution run on `f280405e` plus the frozen implementation patch exits 0:
9,444 passed, 30 skipped, none failed across ten test projects. Simulation contributes 5,463 passes, including 24 focused
Guard-memory cases. The immutable pre-fix master `f280405e` fails four of the five
original behavioral regressions; the dropped-job priority control passes.
Reviewer regressions also caught two direct-pickup and three copy/released-target
failures before their fixes. The evidence, exact source hashes, patch identity,
commands and receipts are frozen under
`F:/DK2Evidence/studies/q710-guard-memory-built--2026-11-14--001`
(`manifest-red.json`, `manifest-green.json`). The generated copier was regenerated
with `tools/state-copy/generate.py`, and existing API signatures are unchanged.

These tests establish the bounded implementation; Q710's aggregate exposure and
matched-input outcome remain open. Native capture routes and other restore
inputs are still unavailable or incomplete.
The [Q710 fighter cohort](q710-fighter-decisions.md#fighter-cohort-at-7904875f) records the post-build history/memory comparison and remaining input limits.
