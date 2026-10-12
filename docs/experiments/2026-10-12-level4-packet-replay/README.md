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
| F | ADR-0443 | as E | 19/20 | 6/20 | 18/20 | 2/20 (seeds 19, 13: 6748, 6760) |
| M | master 15c2b038 | as E | 19/20 | 2/20 | 13/20 | 2/20 (seeds 11, 12: 6740, 6732) |
| G | M + ADR-0443 | as E | 19/20 | 2/20 | 13/20 | 3/20 (seeds 9, 11, 19: 6724–6748) |
| Q | G + ADR-0448 | as E | 19/20 | 2/20 | 13/20 | 2/20 (seeds 9, 11: 6724, 6732) |
| M2 | master d1e67516 | as E | 19/20 | 2/20 | 13/20 | 3/20 (seeds 9, 11, 19: 6732–6740) |
| Q2 | M2 + ADR-0448 | as E | 19/20 | 2/20 | 13/20 | 2/20 (seeds 9, 11: 6724, 6736) |

M and G agree tick for tick on Levels 1–3: the gate changes nothing there, and master's
own commits since 3877e855 took Level 2 from 6/20 to 2/20 and Level 3 from 18/20 to
13/20. Every run is valid (all turns, map stable, no placed type mismatch). A and B agree tick
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

## The claim lag (set E, seed 1)

Measured against the capture's tiles, creatures and trigger flags (`flags_bank()`):

- **Imps.** DK2 keeps all 30 of its keeper's Imps. Ours loses eleven (`creature_died`, 3920–5612),
  seven of them in the second Guard Room (23–25,36–38), and misses three Create Imps: 1374
  for mana (7442 of 7500) and 3231, 3471 because (19,33) and (12,33) were not yet ours.
- **The door.** Seven of our Imps took an AttackTile job on the hero door at (24,27)
  about 5,000 times (2117–5300), each dropped as `out_of_reach`. `0x0046AB50` never
  claims a door tile; the gate was unbuilt. ADR-0443 builds it (set F).
- **Mining.** The gold at (8,24) took ours 670 turns (4587–5258) against DK2's 16: the
  two Imps on it died, and nobody else came for 590 turns.
- **Parties.** Trigger 1 (AP 1, nine Guard Room tiles owned) starts party timers 4–9, whose
  values (240–720) match the spawn gaps at 4 turns per unit (inferred from the gaps). DK2 fires it at 2030, ours at 1963 (the room converted 68 turns
  early; ours began mining the gold before it, (24,39), at 1851, DK2 at 1902), so every party came 67
  turns early, before the owner's drops at (13–18,34) at 3880–3897. Flag 11 (AP 8,
  (7–9,23–25), a keeper creature inside) stops the parties: DK2 4378, ours 4583, so ours
  also gets party 7 at 4363, which DK2 never sees.
- **Flag 14**, the three placed Guards at (23–25,26) waking, fired at 3430 in ours (our
  Warlock engaged them through the door) and at 5283 in DK2, after the owner's drop at
  (24,28). In set F seed 1 moves to 5283; seeds 10–12 still wake them early (3212–4260),
  and the winners, 13 and 19, were at 5283 in set E already.

**Scratch run H** (set E's tool, trigger 1 held until turn 2030, not committed): 2/20,
at 6772 and 6784. Party timing alone moves the outcome.

## Set F

The door takes fall to 0–42 a seed (seed 1: six, from 5325, 19 turns before DK2's
break starts). The winning seeds reach rows 25, 15 and 5 at 4319,
5121, 6307 (seed 13) and 4334, 5129, 6297 (seed 19), against DK2's 4391, 5090, 6310.
Over all 20 seeds row 15 is reached in 11 and row 5 in 6. Seed 1 loses 25 Imps (3424–6076):
eleven in the Guard Room (from 3980, the 3763 party first), four at the Heart (27–29,54)
at 5364–5484, and nine down the middle column from 5404, where the woken Guards 10–12
walk to the Heart.
DK2's owner beats those Guards at (24,28) at once.

## Levels 2 and 3 on master after 3877e855

Levels 2 and 3, seeds 1–20, at each commit of the classic-look line that changes the
simulation (base `687066e3`, which wins as many as 3877e855 on both):

| Commit | Level 2 | Level 3 |
|---|---|---|
| `687066e3` base | 6 | 18 |
| 449 `62fba108`, 452 `4f048d71`, 453 `acae8f7a` | 6, 6, 5 | 17, 16, 17 |
| 454 `d679bebd` react to registered attackers before choosing work | 6 | 8 |
| 455 `8c82572d` retreat to the alarm room's original random point | 3 | 8 |
| 457 `96fa1942` restore room construction and ambient life | 2 | 13 |
| 458 `6f4f2bb2` merge with this branch | 2 | 13 |

Level 3's drop is 454's and Level 2's is 455's; neither is examined here.

## Set G: what separates the winners

- **Row 5 in time.** All three winners claim (16,5) and (17,5) before the owner's drops
  there (6489, 6554) and lose at most 3 Imps before 6400. The Lord then comes out to
  (20–21,5) and dies, as DK2's does at (21,5) from 6692. In the other 15 losers he ends
  unhurt on (22–26,4). Seeds 2 and 17 also claim both tiles in time (6484–6588), hurt him
  and run out of turns, so row 5 is necessary, not sufficient.
- **Imp losses.** Losers lose 1–82 Imps before 6400 (DK2 none; 13 of 17 lose 11 or more). In seed 14 the parties
  push from the second Guard Room down the middle to the Heart from 4400: in 3600–5200 heroes
  land 73,548 melee damage on the keeper's side and take 24,504 (17,304 melee, 7,200 from
  spells); three of our creatures (19, 15, 18) do most of it.
- **Missing creatures.** Of the owner's 14 pick-ups at 3862–3893 and 5263–5266, the creatures
  named have no counterpart on our side (an entity-map placeholder) 0, 0 and 1 times in
  the winners and 4–8 times in seeds 14, 15, 18 and 20 (our portal never
  sent DK2's 1199 and 1315 type-10 arrivals, so the hand grabs nothing). Seven seeds miss
  none and lose anyway, so this is part of it, not all.
- **Party timing is a spread.** Trigger 1 fires between 1917 and 2397 by seed (median about
  1995; DK2 2030) in both E and G. Seed 1's 67 turns early was its draw, not a fixed lead.

## Set Q

ADR-0448 refuses a take whose walk cannot start. Level 4's `out_of_reach` drops fall
from 4,979 to 17 over the 20 seeds; Levels 1–3 replay identically (every output file),
and 14 of 20 Level 4 runs change (7 in `replay.csv`). Seed 19 no longer wins: within one
seed, no measured gain, and again on master d1e67516 (M2 against Q2: drops 4,878 to 17).
Seed 19 loses both times: 26 imps' takes of the owner's dig at (16,5) are refused 139
times at 6443–6800, and (17,5) is never claimed. That the door-blind grid's side choice
causes the refusals is inferred, not traced. Gold, object and body fetches and the side test are Q590.

## What to compare next

Why the keeper's creatures lose the 3766–4600 fights in the losing seeds when most of the
owner's drops do land: who is in them and their levels against DK2's. Seeded DK2 playbacks
of `l4live` would say how much of the spread in arrivals and wins is DK2's own.
