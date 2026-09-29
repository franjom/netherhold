# Mana pressure and creature removal

**STRONGLY INFERRED**, DKII.EXE v1.70, read-only Ghidra 12.1.4/PyGhidra,
2026-09-28. No runtime observation validates this sequence yet (Q57).
Implementation: independent `ManaPressureRules`; integration boundary: ADR-0266.

## Claim audit

Each row is STRONGLY INFERRED. Addresses identify executable evidence; shipped
record values are checked by `ImpsOnRealDataTests` and `KeeperSpellsOnRealDataTests`.

| Claim | Source | Result |
|---|---|---|
| Initial warning state | `0x004B8640`, player `+0x76`, `+0x9E`, `+0xA6`, `+0xB48` | Warning bit 8 enabled, removal bit 0 clear; both deadlines and stage zero |
| Mana update gate | `0x004C10E0`, player `+0x9A` | Current turn at least last payment plus ticks per second |
| Low-mana threshold | `0x004C155D`, global `0x006F28FA`; variable 125 | Word threshold, shipped 100; mana must be strictly lower |
| Warning/scan interval | `0x004C158E`, `0x004C165D`, `0x004C169F`; variable 110 | Byte seconds, shipped 10, multiplied by ticks per second |
| Population floor | `0x004C15C0..0x004C1611`, `0x004C1BC0`; variable 94 | Count owned definitions with worker and evil bits; removal needs strictly more than shipped floor 4 |
| Warning payload | `0x004C1611..0x004C162F` | Message 52, player tag, current stage, zero, final argument 1 |
| Selection and stamp | `0x004C1BC0` | Policy below; clears creature flag bit 5 at `+0x3E6`, stamps `+0x4B3`, posts message 53 with data1=3, data2=0, final argument 1 |
| Upkeep field | Loader `0x004F7CE0`; Create Imp record `+0xE2` | Runtime word `+0x5C`; loader input includes `0x78` prefix; shipped upkeep 7 |
| Countdown reader | `0x00488930`, affordability `0x004C1740` | Only unpossessed type 1; recovery first, otherwise five seconds to virtual removal call |
| Pickup reader | `0x0048E6A0` | Any nonzero removal stamp refuses pickup, including non-imps |
| Stored idle flag | `0x00463DB0`, `0x00463DF0`, `0x00464240`; creature `+0x3D1` | First two clear it, third sets it and conditionally clears after a successful state-10 transition; not equivalent to current state 10 |

## Warning and scan clocks

After a due mana payment, warning progress requires nonpositive net income
(before clamping mana) and the warning-enabled bit. The first low-mana payment
sets stage 1 and a deadline one interval ahead, without a message. At or after
the deadline, emit the stage and increment it. Before emitting stage 4, if the
evil-worker count is at or below the floor, change the stage to 3. This repeats
warning 3 until the count rises. Held workers count toward that floor.

Stages below 5 advance the **old** deadline by one interval; a late payment
does not catch up several warnings at once. Reaching stage 5 disables warnings
and enables removal scans. A scan runs only when its deadline is **strictly
less** than the current turn, then sets its deadline to now plus one interval,
even when selection finds nobody. It can run on the fourth-warning payment.

Mana at or above the threshold clears the warning deadline, disables removal
and enables warnings, leaving stage and scan deadline untouched. Positive net
income below the threshold pauses warning progress but does not disable an
already-enabled removal scan.

## Candidate and countdown

Selection walks the original owned-creature list. Held creatures are excluded
from candidates. A unique lowest-level creature wins, **without a type or worker
test**. If the lowest level ties, or no unique candidate exists, choose the first
nonheld type-1 creature whose stored idle flag is nonzero, regardless of level.
Otherwise choose the farthest nonheld type-1 creature from the heart tile centre.

Positions use 4096 units per tile. Square each axis difference, shift each square
right by 12, then add as an unsigned 32-bit value. Strictly greater distance
replaces the candidate, preserving list order on ties. The initial maximum is
zero, so imps at the exact centre alone produce no candidate on this fallback.
An existing stamp is not an exclusion: selection may refresh it.

The creature update processes a nonzero stamp only for unpossessed type 1.
Enough mana to afford the Create Imp upkeep clears the stamp, including exactly
7 mana with shipped data. Zero upkeep also clears it. Otherwise, at or after
`start + 5 * ticksPerSecond`, it calls the creature's virtual slot `+0x20`.
The number **5 is literal** here; variable 144's shipped value 10 is not this
reader's countdown. Possession skips both recovery and expiry. Consequently the
unique-lowest non-imp path can leave a pickup-blocking stamp that this reader
never clears. The complete removal side effects and other clearing paths remain
UNKNOWN pending a wider lifecycle audit.

## Integration and observation limits

The rules are executable and tested independently; no system calls them yet.
`ManaSystem` still floors mana at zero. Its existing once-second phase is not
the original per-player deadline. Possession/CTA/Reaper branches can return
before the original warning block; those effects are outside these pure rules.
Owned-list order is maintained and persisted (`creature-ownership.md`, ADR-0273).
The stored idle flag and removal side effects still need integration before using
the selector on a live world. Existing rest/wander states do not store that flag;
see `imp-idle-and-wandering.md`. Do not derive it from the activity category.

An original experiment should hold more than four imps, spend below 100 with
nonpositive net income, record all warning turns, and record selection/expiry.
Recovering to exactly 7 mana during a stamp distinguishes upkeep affordability
from the 100-mana warning reset. This observation is still outstanding.
