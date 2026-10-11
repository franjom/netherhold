# Levels 1–3 packet replays over seeds, master 5691f56d (2026-10-06)

What ours does with the owner's recorded games, across our generator's seeds. The seed
is the start of `SimRandom` (`dk2tools replay --seed N`; no `--seed` is 0), not a DK2
seed: the packet's commands are fixed, and the dice move. DK2's own spread on the same
recordings comes from the owner's seeded DK2 playbacks in `reverse-engineering/dk2runs/`.

## Rig

The drivers are not in the repo. They live in the session scratchpads (`l1live_m.py`,
`l2live_m.py`, `l2map-any.py`, `l3map.py`, `l3live_m.py`, `l2steps.py`) and must be
pointed at a clean worktree with `TOOLROOT`. Each seed rebuilds its own entity map,
because a seed changes our entity ids.

| Level | Recording | Turns | DK2 side |
|---|---|---|---|
| 1 | `l1live.sav`, map `l1m/map.csv` | 2044 | `level1-live1`, `level1-live1-seeds` (02–07) |
| 2 | `l2live2.sav` | 3657 | `level2-live2`, `level2-live2-seeds` (18 distinct seeds) |
| 3 | `l3live.sav` | 3704 | `level3-live1/seed01`, `level3-live1-seeds` (03–07; 02 did not seed) |
| 4 | `l4live.sav` (recorded 2026-10-11, won by killing the Lord; not yet replayed or mapped) | 6804 captured | `level4-live1/seed01` only; no seeded DK2 playbacks yet |

A win is the replay's `player 3 status=2`. Level 3 was also run past the recording's end
with no further input. That needs a throwaway change in `PacketLog.Read`: stop at the
last framed turn instead of throwing. The recording's final turn has no following header
to frame it, so it is dropped. The change was not committed. Up to 3704 the extended run
is the plain run: seeds 1, 6 and 8 re-run plainly with their final maps win at the same
ticks. The map builder's own log is not a result; its trial runs use incomplete maps.

## Results

**Level 1: 20/20 won,** at 1772–1996 (median 1792); seeds 2 and 8 are about 200 turns
late. DK2: 7/7 (`level1-live1` and `-seeds`, decoded) at 1791–1902, median 1803; the live
game won at 1802. Last measured: 18/20 at hash 193 (ADR-0389).

**Level 2: 2/20 won** (seed 14 at 3600, 17 at 3396). DK2: 4/18. The first claim is
bimodal (69–82 or 231–373). The steps before the win, DK2's decoded from its 18 captures
(`level2-live2/seed01`, `level2-live2-seeds` 02–05, 07–18 and 234567) with the scratchpad
script `l2outcome.py`:

| Step | Ours (20) | DK2 (18) |
|---|---|---|
| Second hideout Dwarf group (DK2 slots 444–447) all dead before 2500 | 7 | 8 |
| The Lord's party spawns | 7 | 6 |
| The Lord dies (here, the level is won) | 2 | 4 |

The first two match. After the party spawns, ours kills the Lord in 2 of 7 seeds and DK2
in 4 of 6. The samples are small; Q567.

**Level 3: ours wins more often by 3704 in this sample.** By 3704, the recording's end, ours has killed the
Lord of the Land (the win) in 14 of 20 seeds, at 3560–3700. DK2 has done so in 1 of its 6
distinct seeds: the live game, at 3683. DK2's seed 02 failed to seed ("the level was past
tick 1") and repeats seed 01. Where the Lord lives at 3704, his last recorded health is 860–4338 in ours
and 1150–5100 in DK2 (seeds 03–07). DK2's sample is six seeds, one of them the live game
the recording came from, and the ranges overlap; Fisher's exact test on 14/20 against 1/6
gives p ≈ 0.05. A lead, not a finding.

Run past 3704 with no input, ours wins all 20 (late: 3728, 3736, 3752, 3820, 4348, 4476;
seeds 19, 12, 9, 11, 4, 3). DK2 has not been run past the recording.

## Seed 0 is the late one

Level 3 had only been measured on seed 0: a win at 3608 on `885ec6d8`, and 3556 in
ADR-0389's measurement on its branch (the replay here gives 3560 for `bd67f862`). On seed 0, the merge `02fe9533` stops the win inside the recording. Both its
parents win alone (`89cb6e72`, ADR-0388, at 3600; `4a299968`, ADR-0389, at 3560), and the
merge leaves the Lord at 603 health at 3691. Later commits do not change it
(`be2426c0`, `c19643cf`), and run past 3704 it wins at 3736. Across 20 seeds this is
spread, not a regression; a single seed cannot tell the two apart. Against DK2's spread,
seed 0's earlier wins were the outlier, not this result.

## What to compare next

Neither difference is significant on its own: Level 2's 2/20 against 4/18 gives p ≈ 0.4.
If both hold up with more DK2 seeds, they point opposite ways (Level 2 fewer Lord kills,
Level 3 more), and one bias in our fighting strength would not explain both (Q567).
