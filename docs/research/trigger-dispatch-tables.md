# The trigger dispatch tables, and the arms behind them

How a condition type becomes a rule, and what the arms read that
`trigger-semantics.md` does not already cover. Written 2026-09-10 while ranking
the campaign's remaining stalls by the walker that produces them.

## Reading a dispatcher

Each of the four dispatchers is two-level: a range check, a byte table indexed
by `type - base`, and a dword jump table indexed by that byte. Several types
therefore share one arm, and reading either table alone tells you nothing.

| context | check | byte table | count | jump table | base |
|---|---|---|---:|---|---:|
| world | `0x00518282` | `0x0051897C` | 67 | `0x00518950` | 1 |
| player | `0x004C7B30` | `0x004C85E8` | 56 | `0x004C8580` | 19 |
| area | `0x004B0D7F` | — | 32 | — | 39 |
| creature | `0x0049AAAD` | — | 63 | — | 3 |

`DispatchMap.py` prints `index + 1`, so a true type is **printed + base - 1** —
`+18` for the player table. Three arms already known independently confirm it:
printed 4, 29 and 54 land exactly on types 22 (`0x004C7D0F`), 47 (`0x004C7B4B`)
and 72 (`0x004C84B8`).

**A shared arm is a real finding.** The player table's `0x004C850C` is
`XOR EAX,EAX; RET` — types 48, 53 and 56–61 are *constant false* rather than
unimplemented, which is why declining them would be wrong.

## Type 46 is DK2's own silent false

The world table gives type 46 its own slot, and that slot's jump entry is
`0x00518941` — the tail of the unhandled path, **past** the
`Unhandled Trigger When [ Type %d, Index %d ]` printf at `0x0051892F`. So the
engine returns zero for it deliberately and says nothing, unlike the 57 types
that reach `0x00518929` and do print. One shipped record uses it, in level 1's
player tree, reaching the world arm by delegation. CONFIRMED from the tables.

## The three availability arms: 27, 28, 29

Four `{count, base}` availability tables hang off the player at stride 8, and
four near-identical lookups read them. The base offset each one loads settles
which table it is, with no inference:

| lookup | loads | table | condition |
|---|---|---|---|
| `0x004BAB20` | `+0x56`/`+0x5A` | `RoomInfo` | (types 24/25/68) |
| `0x004BACD0` | `+0x5E`/`+0x62` | `DoorInfo` | **27** |
| `0x004BAE30` | `+0x66`/`+0x6A` | `TrapInfo` | **28** |
| `0x004BAFA0` | `+0x6E`/`+0x72` | keeper spells | **29** (ADR-0120) |

Types 27 and 28 share one arm shape (`0x004C80CE`, `0x004C8240`) with **two
modes**, and the corpus decides which one matters:

- `Comparison == 0` is availability, the same question type 29 asks: a named
  `TargetId` is looked up and tested against AVAILABLE (3); zero walks the whole
  table for any entry at 3.
- `Comparison != 0` counts, and byte 02's bits 3 and 4 pick what is counted.
  For type 28: bit 3 counts things in the list at `player+0xC` with
  `thing+0x10C & 0x20`; bit 4 is `player+0x22` *minus* that count; neither bit
  with `TargetId == 0` is `player+0x22` itself; neither bit with a named type
  calls `0x004BA890` and then adds a second walk when the type record's `+0xBB`
  is `0x1A`.

**All ten shipped records use the counting mode** — comparisons 3 and 5, never
0 — so implementing the availability half would have moved nothing. The
`+0x10C & 0x20` bit is unidentified (Q281), and `player+0xC`/`+0x22` are
`ThingsOwnedList[3]`/`TotalNumberOfOwnedThings[3]`.

## Area type 70 is not type 39 asked for existence

Both walk the action point's rectangle and both end at the same predicate
`FUN_004B0A80(thing, category, typeId)`, but the existence walk
(`0x004B06D0`, behind type 70) applies **two filters the counter
(`0x004B0620`, behind type 39) does not**:

- a creature must have `thing+0x3E6` bit 7 set;
- an object must have `thing+0x146` bit 3 set.

Neither flag is identified (Q280), so type 70 stays declined: guessing "any
thing matching" would over-answer an existence test, which is the direction that
fires a trigger early. Type 70 also reads no comparison and no value — its arm
`0x004B0DD7` returns the walk's result directly, and maps byte 03 `== 6` to
category 2 and anything else to category 0, exactly as type 39 does.

## Four player arms read whole

| type | arm | reads | rule |
|---|---|---|---|
| 23 | `0x004C7DF0` | `+0x146`, else `+0x19E + 2n` | creatures **this** keeper killed: byte 02 zero is the total, a named player is that player's slot |
| 52 | `0x004C7E4E` | creature `+0x403` | count of owned non-worker creatures at or above byte 03's level |
| 71 | `0x004C8491` | `+0xEE6` | `NumberOfSlaps` |
| 73 | `0x004C8500` | `+0x134`, then `+0x488` | not possessing → **false without comparing**; possessing → compare the possessed creature's `+0x488` |

Types 22 and 23 are the two directions of one array. `A[P][n]`, the word at
player `P`'s `+0x19E + 2n`, is read by type 22 as `A[Q][me]` and by type 23 as
`A[me][Q]`; the totals beside them, `+0x173` and `+0x146`, are "creatures of
mine lost" and "enemy creatures killed" from the score block. Both therefore
wait on kill attribution, which waits on combat.

Type 52's byte 03 is a **level**: the arm compares it against the creature's own
`+0x403` with `JC`, so the count is "level >= byte 03", and workers are excluded
by the definition's `+0x686` bit 0 — the same bit type 19's unfiltered total
subtracts them by. The three shipped records ask for one creature at level 4,
one at 8, and twenty at 2, which is what a level threshold should look like.

## What is implemented, and what is not

ADR-0121 built 46, 52, 71 and 73. Types 71 and 73 read player fields nothing
writes yet — the slap tally and the possessed creature — so both answer from
state rather than from a constant, and type 73 **declines** rather than guesses
the moment possession stops being zero. Whether `NumberOfSlaps` counts slaps
given or received is unread (Q278), and the possessed creature's `+0x488` is
unread (Q279).

Declined and why: 22/23 combat, 20 `creature+0x28A` (Q269), 27/28 trap and door
counting (Q281), 64 the world's own large arm, 70 two unidentified flags (Q280),
2 the special timer id 16 (Q232).
