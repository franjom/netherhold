---
systems: [keeper-spells, creatures]
---
# Creature pickup permission

## Evidence

**STRONGLY INFERRED**, read-only DKII.EXE in Ghidra 12.1.4/PyGhidra,
2026-09-28. The creature predicate `0x0048E6A0` is distinct from the
keeper handler `0x004BC500`, which checks hand capacity and repeated ids.
The caller tag is the stack word read at `0x0048E70F`; the decompiler's
inferred return-address variable is not its meaning.

| Predicate order | Rule |
|---|---|
| Common refusals | Dying flag, InHand, nonzero dword `+0x4B3`, missing `CanBePickedUp`, current state 76 or 193 |
| Same owner, Combat Pit fighter | Return whether the creature's tile belongs to that owner; bypass remaining same-owner gates |
| Other same-owner creature | Refuse current states 51/52, nonzero Turncoat word `+0x453`, or a worker with `(byte[+0x46F] & 9) == 0` |
| Remaining same-owner creature | Permit if `RoomId` is zero or that room belongs to the owner |
| Different owner | Permit `IsTool`, nonzero word `+0x4A3` matching the caller tag, or nonzero `PrisonOwner` matching it |

The state tests read current state `+0x25E`, not a travel resume slot.
Foreign permission does not run the same-owner teleport or worker tests.
The Pit branch still follows all common refusals. The debug name `SneakingAs`
for `+0x4A3` does not establish its meaning outside this player-tag comparison.

## Worker-byte writers

The constructor `0x004872A0` initializes `+0x46F` to 9. Helpers `0x0048DF10`
and `0x0048DFA0` toggle bits 0 and 3 respectively. Either bit admits pickup;
this reader does not establish separate names for them.

`0x004651A0` clears both during a successful teleport setup;
`0x004654A0` restores both after completion. `0x00467DD0` clears both after
starting the prison's inner delivery leg. State 44 (`0x00468010`) keeps both
clear on admission and restores them on terminal cleanup. State 47
(`0x00468270`) restores both before choosing the refused-body destination;
state 46 (`0x004683F0`) restores them before returning to worker idle. Combat routines
`0x00474850`/`0x004748E0` also restore them. These are traced call sites,
not a claim that every runtime writer has been integrated.

The task-release routine `0x004647D0` restores both for states 43/44/46/49.
While current state is 6/7, its switch reads the **resume** (`state+0xDF`),
not the saved caller. The dispatcher `0x00476D30` chooses the leave hook by
the saved caller's category when leaving travel for a different category.
Entering travel skips that hook; travel-to-travel recurses through idle first.
A refused transition runs no hook. These distinctions were rechecked on 2026-09-28.

## Room association

The fresh reads agree with the membership and capacity evidence in
`state-category-hooks.md` §1. Additionally, load/reconstruction `0x00488750`
calls the relink helper `0x004E5DA0` for a nonzero stored room id;
`0x004887B0` resolves that stored id. Caller and room-rebuild integration
remain open; the current tile cannot substitute for stored membership.

## Countdown lifetime

The mana-pressure writer, selection quirks and countdown reader are established
in `imp-mana-removal.md`, and the live lifecycle is connected: the stamp is
`CreatureState.ManaRemovalStamp`, written by the scan and cleared or expired by
`ManaRemovalSystem` (ADR-0277). This is not a generic pickup cooldown.

## Implementation boundary

`HandRules.HasPickupPermission` now checks the represented common, current-state,
removal-stamp, Pit, worker-byte, tool and prisoner gates. `CanPickUp` additionally
checks hand capacity. `KeeperView` captures both results separately; schema 7
persists them. Neither result claims complete original permission outside the
represented state.

The existing `CreatureState.Flags` starts at 9; placed/party projection still
applies its existing record masks. The pickup-bit subset of the WorkerJob leave
hook and prison delivery is built (ADR-0263). The existing compressed inner
prison walks clear and restore the bits within one tick; this does not reproduce
their original duration. Of the stored room association only a Prison's is kept
(`PrisonOwnerPlayerId`, written as `0x0047B740` calls `0x004E5A70` on entry), so an
own creature in another keeper's cells is refused; other rooms' membership,
teleport/combat writers and the disguise tag remain unimplemented. The Turncoat
owner-hand refusal is built; see `keeper-spell-turncoat.md`.
A tile's room is not substituted for that association. The existing
zero-health refusal remains a simulation adaptation before its delayed collapse.
The keeper's attack response and its retreat of the wounded consume this permission
from their observation (ADR-0278, ADR-0284); the unrepresented gates above still
bound those responses' fidelity.
No original-game observation validates this slice; the next section's stored path is the one capture-backed claim.

## Permission is the cursor's; a stored pick-up skips it (ADR-0616)

**STRONGLY INFERRED (binary, reviewed 2026-11-01)** for the path; the Level 5 capture is one observation. `0x0048E6A0`'s callers are the cursor and hand-click code (`0x00407A78`,
`0x0040C1E0`, `0x00408EE0`, `0x00418xxx`) and the computer keeper. The stored pick-up never calls
it. That path is action 57 (`0x00513480`), hand state 1's callback `0x004C0BE0` and the insert
`0x004BC500`. It refuses only these:
- a dead slot, or a thing whose vtable `+0x28` (`0x004B5C30`, health below 1) says no;
- an id already held, or a full hand;
- a dying creature, or one in state 51 or 52;
- a failed kick to state 78.
It unmasks a disguised enemy and refuses it. Two more facts about the path:
- `0x004C0BE0` also stops when the creature refuses state 75 and the retry through idle
  (`0x004BFBD0`, `0x004BFC24`). Ours goes ahead with the pick-up; that refusal is not modelled.
- The dead test comes before that preparation. Bit 21 and states 51/52 come after it, in
  `0x004BC500`, which state 1's exit callback `0x004C0EA0` runs one turn after the action. Ours
  tests them before the preparation and inserts in the command's tick.
So a recorded command lifts whatever its slot holds.
Level 5 seed 2's pick-up of slot 439 at 1684 lifted the hero Wizard, and landing state 76 stunned
it: 76's stun test is a non-worker, or a worker of a player numbered below 3, never the hand's
owner (`0x0046EFA7`, `0x0046EFB7`).

The replay runs this path with `--pickups stored` (`HandSystem.StoredCommandPickups`,
`HandRules.StoredPickupAccepts`). Play keeps the permission in `HandSystem`.
