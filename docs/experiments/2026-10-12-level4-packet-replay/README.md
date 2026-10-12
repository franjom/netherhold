# Level 4 packet replay over seeds, branch l4/guard-room-replay (2026-10-12)

The owner's Level 4 game (`l4live.sav`, won at capture tick 6743) replayed through ours
over seeds 1–20, as `2026-10-06-packet-replay-seeds/` does for Levels 1–3. DK2's side is
the one live capture, `reverse-engineering/dk2runs/level4-live1/seed01`; there are no
seeded DK2 playbacks of it yet.

## Rig

Drivers in `reverse-engineering/dk2runs/level4-live1/rig/` (local, not in the repo): `runset.py` runs one set (levels × seeds through
`dk2tools replay`), `lmap.py` builds each seed's entity map, `pktdump.py` lists a packet's
actions. Level 4 runs 6804 turns and needs three things Levels 1–3 did not:

- **Possession.** The recording possesses a creature from turn 6274 to 6388 (actions 55,
  25, 26, 21). The decoder builds actions 21, 25 and 26 since `bd6ef19a`.
- **Specials by tile.** Action 88 activates four placed boxes. DK2's slots 1342–1359 are
  the THINGS file's 18 objects in order; ours numbers them 1–18 on the same tiles, so the
  boxes (1356–1359) are our 15–18 and are mapped as `Object`, not `GoldTile`.
- **A placed count from the capture.** Level 2 places 16 creatures, Levels 3 and 4 twelve.

**The older builders paired hero parties one off.** `l2map-any.py` and `l3map.py` counted
the placed heroes as party 0, but our side's party list leaves them out. Level 2's builder
then mapped DK2's four placed heroes onto our Lord's party from turn 2, and the Lord's
party itself onto placeholders; Level 3's skipped the placed slots but still paired DK2's
1291 party with our second. A reused slot also took its start turn from its last party.
`lmap.py` fixes all three. Measured on the same tool (`bd6ef19a`), the fix leaves Level 2
unchanged tick for tick (6/20) and Level 3 at 18/20 with the same two losing seeds (8, 18);
three Level 3 wins move (seed 13 by −24 turns, 19 by +12, 20 by +8). The 2026-10-06 counts
stand.

## Results, seeds 1–20

| Set | Tool | Builder | Level 1 | Level 2 | Level 3 | Level 4 |
|---|---|---|---|---|---|---|
| A | `bd6ef19a` | old | 19/20 | 6/20 | 18/20 | — |
| B | ADR-0440 | old | 19/20 | 6/20 | 18/20 | — |
| C | `bd6ef19a` | `lmap.py` | — | 6/20 | 18/20 | 0/20 |
| D | ADR-0440 | `lmap.py` | — | 6/20 | 18/20 | 0/20 |
| E | ADR-0441 | `lmap.py`, batches by tile | — | 6/20 | 18/20 | 0/20 |

Every run is valid (all turns, map stable, no placed type mismatch). A and B agree tick
for tick, as Levels 1–3 hold no Guard Room; D and E agree tick for tick on Levels 2 and 3.
E's builder pairs a batch both sides create on one turn by tile, not arrival order.

**Dark Elves.** DK2 has eleven, the first at capture tick 1943; the keeper's first Guard
Room was captured about 1576. Set C: none in any seed, refused by the attraction gates.
Set D: 6–16 (median 11).

**No seed wins.** Sets D and E. The Lord ends at full health (7500) in all 20 seeds, on (24–26, 4) in
seeds 1, 5 and 14. The owner's final drops land on (16,5) from turn 6489, (17,5) from 6554
and (20,5) from 6674; DK2's imps claimed (16,5) at 6487. Ours cannot drop there, and the
replay's drops land on our nearest land: in set D (15,15) in seed 1, (24,21) in seeds 5 and
14; in set E seed 1 drops on (16,5) too, and the Lord still takes no damage. The northern
claim frontier (DK2: first owner-3 tile on the row; ours: first claim):

| Row reached | DK2 | set D (seeds 1, 5, 14) | set E, seed 1 |
|---|---|---|---|
| 28 | 2193 | 2114, 2220, 2132 | 2114 |
| 25 | 4391 | 4599, 5830, 5700 | 4599 |
| 24 | 4516 | 5277, 5908, 5778 | 5277 |
| 15 | 5090 | 6332, not reached, not reached | 6420 |
| 5 | 6310 | not reached | 6600 |

In set E seeds 5 and 14 creep down the middle (x 22–26) to row 21, by 6103 and 5973, and
never reach row 15.

Most tags reach ours. Rejected in all three seeds: four claim tags on open path at x 42–45,
rows 28–29 (3201–3570), and on the west route (12,18) at 4925, which DK2 claimed at 4964
and ours at 6413 in set D (6488 in set E seed 1; seeds 5 and 14 never). (12,18) starts as neutral open path (terrain 3), so it wants a claim,
not a dig; the rejection may only say that ours does not take a claim tag there, and is
a weak lead. Special 124, Receive Imps, gave DK2 ten level-5 Imps at turn 5783 (DK2 slots
408–414, 419, 444, 445); since set E ours makes them on the same ten tiles (ADR-0441), in state 10 where DK2's
are in 11 on that tick, as for Create Imp. DK2's ten all go 11, 10, 12 (`ImpStates.Resting`), 87
(`Wandering`) and walk at 5787, four turns, the second's wait the owner sees; nine of ours find a task
in 10 and walk at 5784–5786, one to three turns early.
Special 118, Reveal Map, is unbuilt; it changes only the activator's view.

## What to compare next

The frontier lags from row 25 on in every seed, before Receive Imps: compare imp counts
and dig and claim rates on the west route (rows 25–15, x 8–14) against the capture before
reading more of the binary. Then, in seed 1, why the army dropped at (16,5) does not
reach the Lord.
