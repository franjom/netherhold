# Hatchery startup interval (Q695)

## Writer order

STRONGLY INFERRED from whole `0x004DE060`, `0x004DE0A0`, `0x004DE0E0`,
`0x004DE560`, `0x004EDFC0` and `0x0050B5B0`, read 2026-10-31.

The room constructor stamps +0x49 with the current turn, writes +0x4D as
variable18 times variable19, clears +0x51 and sets +0xA3 to1. The two globals
are `0x006F258C` (18, ChickenGenerationTimePerHatchery) and `0x006F2590`
(19, ticks per second). This initial16-second interval is provisional.
Room tile insertion ends in the type's +0x09 slot, `0x004DE560`, which writes
the ordinary power-formula interval ([hatchery.md section4](hatchery.md#4-how-often-it-lays)).

On a fresh level, the player/room walk around `0x0050B935` calls
`0x004DE0E0` at `0x0050B935` on every existing type4 room, including neutral
and other players' rooms. It skips this walk for restored saves (world+0x7239
bit0x40). The callee consumes but ignores the pushed variable18 argument
(RET4); it writes only the receiving room, not a room walk itself.
The later startup furniture rebuild (`0x0050BC9D`, `0x004EDFC0`) places coops
and generic furniture without invoking +0x09 or writing the interval.
Thus the table persists until another tile pass; mere ownership adoption
does not itself write +0x4D. Runtime construction uses the power formula.

## Full table and clock

Raw bytes read with read_bytes at `0x004DE1D0` (12 jump destinations,48bytes)
and `0x004DE200` (19 selector bytes). Size zero leaves the previous interval;
the unsigned size check sends20 and above directly to3seconds.

| tiles | seconds |
|---|---|
| 1-3 | 15 |
| 4-6 | 13 |
| 7-9 | 12 |
| 10 | 11 |
| 11 | 10 |
| 12 | 9 |
| 13 | 8 |
| 14 | 7 |
| 15 | 6 |
| 16 | 5 |
| 17-19 | 4 |
| 20+ | 3 |

`0x004DE0A0` checks every turn whether current turn is at least last-generated
plus interval, attempts a lay and stamps the current turn even if it failed.
Neither the power-formula pass nor the startup table resets that stamp.
The first attempted lay therefore follows the final selected interval from
construction's stamp, not an additional16-second delay.

Full executable-block CallScanAll finds one direct caller of the table setter,
`0x0050B935`; construction and tile pass are table pointers at `0x006A1F44`
and `0x006A1F4C`. StoreAt(+0x4D) lists69 sites; the Hatchery interval writers
are constructor`0x004DE07D`, the table's12stores, and tile pass`0x004DE5C1`.
The last-generated stamp has constructor and tick writers, not the table setter.

## Built scope

ADR-0606, hash/save 348: fresh-level initialization applies the table to every
existing Hatchery before `TriggerExecutor` is constructed. Restore retains
snapshot state. `HatcheryLastGeneratedTick` and `HatcheryStartupIntervalTicks`
are stored on the lowest tile and included in the hash and snapshot.

Growth and construction merges retain the first joined room's stamp while
clearing the startup override; removal starts new piece clocks. An actual
capture merge retains the captured room's stamp, clears the override and moves
the stamp to the merged room's lowest tile. Standalone capture retains the
startup interval. `ChickensGenerated` keeps its separate lifetime deviation
([hatchery.md section 3](hatchery.md#3-where-an-egg-appears)).

The focused regression run passed 86 tests with no failures or skips, including
capture merge, standalone capture and construction merge controls. Native
gameplay first-lay timing remains unmeasured; the implementation follows the
reviewed binary reading above.
