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
starting the prison's inner delivery leg. The task-release routine `0x004647D0`
restores both for states 43/44/46/49 (including saved travel callers).
`0x004683F0` restores both before returning to worker idle. Combat routines
`0x00474850`/`0x004748E0` also restore them. These are traced call sites,
not a claim that every runtime writer has been integrated.

## Countdown lifetime

The initializer clears `+0x4B3`. `0x004C1BC0` stamps it with the current turn
when selecting a creature for removal; `0x00488930` clears it if its keeper
condition recovers, otherwise removes the creature after five seconds.
The full keeper condition and selection policy are not established here.
It is not a generic pickup cooldown and must not be mapped to an unrelated
existing countdown merely because both are timers.

## Implementation boundary

`HandRules.HasPickupPermission` now checks the represented common, current-state,
Pit, worker-byte, tool and prisoner gates. `CanPickUp` additionally checks hand
capacity. `KeeperView` captures both results separately; schema 7 persists them.
Neither result claims complete original permission outside the represented state.

The existing `CreatureState.Flags` now starts at 9; placed/party projection still
applies its existing record masks. Runtime worker-bit writers above, countdown
removal, Turncoat, the disguise tag and the original room association remain
unimplemented. A tile's room is not substituted for that association. The existing
zero-health refusal remains a simulation adaptation before its delayed collapse.
Consequently live keeper defence remains gated on these dependencies (ADR-0262).
No new original-game observation validates this slice.
