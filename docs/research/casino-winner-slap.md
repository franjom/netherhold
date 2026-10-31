# Slapping an unpaid Casino winner

Q697; ADR-0608. CONFIRMED binary reading, 2026-10-31; no native gameplay capture.

## Reachability and lifetime

The complete `0x0048B8A0` main slap path calls `0x004D6CB0` at `0x0048BB75`
when creature word `+0x3F2` is nonzero, replacing ordinary slap anger. It tests
the unpaid deadline, not a state or category. Refused transitions, damage-only
and short-return arms do not reach it. The separate Horny path is unchanged.
CallScanAll across `.text` and `cseg` finds one caller of each: the winner
routine from the slap, the slap from `0x004C0FBE`, and jackpot setup
`0x004D6AA0` from `0x0047AEF2`.

StoreAt inventories for `+0x3F2`, `+0x3F4` and `+0x3F6` identify the counter's
zero stores in construction (`0x004875A9`, window within `0x004872A0`) and
new jackpot setup (`0x004D6BCE`); its increment is `0x004D6EB6`. Overlapping
dword stores in `0x005A8430` belong to movie audio predictors, not creatures.
The whole jackpot setup was reread. Category changes do not clear this counter.

## Ordered effects of the complete winner routine

1. Read unsigned word winnings `+0x3F4`; compute `winnings * 25 / 100`, cap
   against the current purse `+0x3FB`, and debit it through `0x0048AF50`.
   That helper, reread whole, subtracts with a zero floor and returns the balance.
2. Add 25 percent of the winner's current anger bucket 5 through the anger
   wrapper, complaint 4. Bucket 5 is named Treatment here; no ordinary SlapAnger
   is added on this arm.
3. If the counter was nonzero before this slap, post message `0x4C` with zero
   payload and give every other member of the winner owner's creature list
   +5000 in bucket 5, complaint 4. No type, state or health filter occurs here.
4. Read winnings again, divide by four, and cap against the **remaining** purse.
   This is the amount to spawn, without another purse debit. A purse at or
   below one quarter of winnings is emptied and produces no pile.
5. Positive amounts below 1000 make one pile; below 3000 two; otherwise three.
   Each receives the integer quotient, losing the remainder. This routine has
   its own loop, not a call to `0x0048F930`.
6. Each pile consumes two `random(4096)` draws, X then Y, from world `+0x15A0`.
   They replace the low twelve bits of the winner's position. Z is `0x3000`.
   Object 1 is created neutral, then receives the share through the gold adder.
   Both draws precede creation, so a refused creation still consumes them.
7. Increment the byte counter, wrapping 255 to zero. Winnings are unchanged.

The message's ten-byte table row at `0x006A0DF8` is `0x2032`: fifty seconds,
tab, no duplicates. Raw bytes `0x006A0DDA..0x006A0E0B` were checked.

## Implementation boundary

`SlapRules.SlapCasinoWinner` mirrors this arm in the existing full slap path.
The byte counter is hashed and snapshotted; a new jackpot resets it, while
leaving or paying the jackpot does not. Existing purse, anger wrapper and
message suppression rules remain in force. Tests derive expected arithmetic
and RNG advancement from these reads; the global determinism pin is remake
output, not native evidence. Native allocation failure remains outside our
unbounded object allocator.

Elevated scattered gold uses its saved Z to distinguish explicit coordinates
from legacy implicit piles; its cell band is also populated. Rendering,
landing candidates and worker reach use that position. Falling physics remains
unmodelled, as for other loose-gold creation here.
