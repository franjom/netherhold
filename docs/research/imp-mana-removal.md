# Mana pressure and creature removal

**STRONGLY INFERRED**, DKII.EXE v1.70, read-only Ghidra 12.1.4/PyGhidra,
2026-09-28, lifecycle rechecked 2026-09-29. No runtime observation validates this sequence yet (Q57).
Implementation: `ManaPressureRules` (arithmetic, ADR-0266), `ManaRemovalLifecycle`,
`ManaSystem` and `ManaRemovalSystem` (the live lifecycle, ADR-0277).

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
| Warning/removal message metadata | `0x006A0D08` / `0x006A0D12`; subtables `0x006A0610` / `0x006A0638`; `0x004C4600` | Flags `0xA000`: subtype matching, NeedsTab, duplicates suppressed per recipient/type/Data1. Warning variants 1–4 and removal variant 3 live 20 seconds; `0x004C5530` has no extra suppression for these variants |
| Selection and stamp | `0x004C1BC0` | Policy below; clears creature flag bit 5 at `+0x3E6`, stamps `+0x4B3`, posts message 53 with data1=3, data2=0, final argument 1 |
| Upkeep field | Loader `0x004F7CE0`; Create Imp record `+0xE2` | Runtime word `+0x5C`; loader input includes `0x78` prefix; shipped upkeep 7 |
| Countdown reader | `0x00488930`, affordability `0x004C1740` | Only unpossessed type 1; recovery first, otherwise five seconds to the zero-health virtual call |
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
`start + 5 * ticksPerSecond`, it calls virtual slot `+0x20`, which only zeroes health.
The number **5 is literal** here; variable 144's shipped value 10 is not this
reader's countdown. Possession skips both recovery and expiry. Consequently the
unique-lowest non-imp path can leave a pickup-blocking stamp that this reader
never clears. A direct-store scan of `+0x4B3` finds constructor initialization,
this recovery clear, and the selector's two stamp writes; this does not exclude
indirect load/copy writes.

## Expiry and subsequent cleanup

The creature constructor installs table `0x0066D524` (`0x004B5ED7`). Its
`+0x20` slot at `0x0066D544` points to `0x004B2AD0`: the entire routine writes
zero to health `+0x40` and returns. It neither deletes the entity nor invokes
the damage/collapse helper. `ManaRemovalAction.ZeroHealth` names that effect.
The cleanup slot is separately `+0x30`, pointing to `0x0048AF80`, which reaches
`0x004B7A20` and then `0x00487C70`.

`0x004B7830` first updates the eight thing lists through slot `+0x14`, then
makes a separate cleanup pass through predicate `+0x28` and cleanup `+0x30`.
The creature predicate is `0x004B5C30`: health strictly below 1. Thus the
zero-health write does not interrupt the current creature update, but qualifies
it for the subsequent cleanup pass. It does not start the collapsed-state timer.

| Cleanup effect | Binary evidence | Modern boundary |
|---|---|---|
| Spill carried gold | `0x0048814B` calls `0x0048F930` unless flag `+0x3E6 & 0x40` | Existing `FloorGold.DropFrom` preserves pile-count division and lost remainder; sub-tile scattering remains absent |
| Release carried object / dragged creature | `0x004881D6` / `0x00488221` call `0x0048B3A0` / `0x0048B500` on the ordinary cleanup path | Health cleanup now releases both, including when the carrier becomes a corpse |
| Place the dragged thing and clear its drag state | `0x0048B500` moves it to the carrier; creature slot `+0x70` is `0x004B5E00`, clearing `+0x3C7`; carrier `+0x3C5` is cleared | `CarryRules.DropWhatever` moves the passenger and clears both modern relationship ids; no death or ownership change is applied to it |
| Release engagements, upkeep and owner membership | `0x0048833E`, `0x00488365`, `0x00488468` | Existing combat unregister, recomputed upkeep and owned-list removal cover these represented effects |
| Corpse creation gate | `0x00487C70` tests definition bit `0x8000` and runtime bit `0x20`; the constructor `0x00487555` copies definition bit 15 into the runtime bit; selection clears it; the Prison's refused raise at the Skeleton cap sets it back (`0x004B3240`) | `CreatureState.CorpseSuppressed`, read by `CreatureDeathRules.LeavesBody(def, state)` in the death bookkeeping only; `PrisonSystem` clears it at the cap |

These are ordinary gameplay branches, not an exhaustive destructor model. The
routine also handles beds, room/state release, possession, special creatures,
statistics, UI and rendering resources. Seven regression cases in `CarryingTests`
check passenger/object survival, placement, released relationships, corpse
carriers, missing passengers and gold rounding. No new original observation was made.

## Turn order and the players updated

The world turn `0x0050A450` calls the thing update `0x004B7830` (creatures'
countdowns, then the cleanup pass), then the per-player update `0x004C2310`,
then the message sweep `0x004C41E0`. `0x004B9EC0` returns for the neutral tag
(`0x006F2548`) and the hero tag (`0x006F254C`, mana set to `0x7FFFFFFF`) before
reaching the mana update through `0x004BB2A0`, so warnings and scans are a
keeper's alone. The heart the selector measures from is `player+0xB43/+0xB45`,
which `0x004B8640` initialises to (1, 1). Affordability `0x004C1740` is
`upkeep == 0 || upkeep <= mana`.

## Integration and observation limits

Built (ADR-0277): `ManaSystem` advances the clocks after each keeper's payment,
posts message 52 and runs the scan; `ManaRemovalSystem` runs the countdown every
tick before it and ends an expired imp through the death bookkeeping;
`HandRules` refuses a stamped creature; `ManaRemovalLifecycleTests` cover the
timing, selection, refusal, recovery, expiry, corpse suppression, carried
contents and save continuation. Our deviations are listed in the ADR: the
payment's shared phase rather than `player+0x9A`, the countdown and cleanup in
one system, the heart position derived from tiles, and the unrepresented
possession, Call to Arms and Reaper early returns and the "no mana" notice.

An original experiment should hold more than four imps, spend below 100 with
nonpositive net income, record all warning turns, and record selection/expiry.
Recovering to exactly 7 mana during a stamp distinguishes upkeep affordability
from the 100-mana warning reset. This observation is still outstanding (Q57).
