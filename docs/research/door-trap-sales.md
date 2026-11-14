---
systems: [doors-traps]
---
# Door and trap sales (Q702)

Source: retail `DKII.EXE`, PyGhidra decompilation and instruction reads,
2026-11-14. STRONGLY INFERRED; no new native play experiment.

## Refund

Both sale paths use **variable 104**, `RoomSellValuePercentageOfCost`,
the byte at `0x006F28EA`. Variables 105 and 106 remain unused here.
The calculators multiply an unsigned definition cost by that byte and divide
by 100, truncating the nonnegative result. The sale callers keep the low
16 bits before passing the amount to the gold transaction.

| Sale | Calculator | Definition cost |
|---|---|---|
| Door `0x004C3670` | `0x004A77E0` | unsigned word `+0x8F` |
| Trap `0x004C3A20` | `0x004AABB0` | unsigned word `+0x92` |

With the shipped rate 50, the refund is half the cost, rounded down.
Neither health nor remaining ammunition enters this calculation. The
construction flag controls manufacture-queue and blueprint cleanup, not
the amount; the already-read cleanup is Q668.

The refund goes through `Player_ChangeGold` at the sold thing's tile,
with flag 0. Its storage/overflow behaviour remains defined by that routine
and [the existing gold transaction research](computer-keeper-messages.md#the-gold-carrier-and-gold-paid-in).

## Click versus drag

`Hand_Click` (`0x004075E0`), sell mode 4, tests a door belonging to the
player first, then a trap. A match posts a command for that single tile:
door action `0x33`, trap action `0x35`. The handlers `0x00513240` and
`0x005132A0` unpack the tile and player and call the respective sale.

Only when neither predicate matches does this press initialise a room-sale
drag. Its release posts action `0x31`; the existing room wave sells room
tiles, not doors or traps encountered along the rectangle. Thus a sale
started on a door or trap is a single-thing sale, not a combined drag.

World slots `+0x23C`/`+0x248` forward through `0x005093A0`/`0x00509410`
to `0x00450F30`/`0x00451080`, which test the tile chain for class 4/3 and
the supplied owner. The sale routines repeat the owner/class lookup, so
the command does not bypass ownership merely by supplying coordinates.

## Scope

Q702's refund and drag-dispatch questions are answered by the binary.
Door/trap sale commands remain unimplemented in the remake; this research
does not claim a completed sale feature or native observation.
