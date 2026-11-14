---
systems: [tooling-process]
---
# Campaign bot ladder: which levels the campaign player wins

2026-11-14. What the observation-only `CampaignPlayer` (`src/DK2.AI/Players/`) does on each shipped
campaign level from its unmodified start, with its default options, DK2 random seeds 1-3, viewing
player 3, through `watch <dir> --campaign-player 3`. Diagnosis only: no build receipt, so none of
it is an acceptance claim. Plan: [stage 3](../plans/2026-11-14-campaign-bot-stage3.md). Scenarios
for the won levels are in `scenarios/campaign-player/level2/` and `level3/`; the others were
generated in a scratch folder like them (`map: campaign:<level>`, `duration_ticks: 57600`).

The player's own default cap, `--player-max-ticks 14400` (one hour of game time), applied, so
"no result" means none within that hour, not that the level cannot be won.

## 1. Results

| Result | Levels |
|---|---|
| Won, all seeds | 2, 3 (rerun after ADR-0800, below) |
| Lost, all seeds | 5, 10, 13, 14, 15a, 20 |
| Lost or no result | 4, 6a, 6b, 7 |
| No result | 8, 9, 11A, 11B, 11C, 12, 15b, 16, 18, 19 |
| Controller gave up after stalling | 17, seeds 2-3 (seed 1: no result) |

Runs took 11-17 s on Levels 2-3 and 45-320 s on the others.

**Levels 2 and 3 after ADR-0800 (Q525's party release), master 2026-11-14.** Level 2 won at ticks
3741, 4582, 4657; Level 3 at 3656, 4559, 5319. Every journal replayed all hashes without the
controller. In each the Lord of the Land died in the keeper's area. Before ADR-0800 Level 2 won at
3963, 3708, 5421 (the owner's native win: 3263, [navigation-captures.md](navigation-captures.md)).
Level 2's own availability table (`level2Variables.kld`, `kwd vars -a`) makes the Treasury
UNAVAILABLE and the Training Room AVAILABLE; the controller built only a Lair and a Hatchery.

## 2. The common pattern: the controller waits

Share of decisions that were "wait: nothing offered to do", seed 1:

| Level | Waits | Own creatures dead | Portal arrivals |
|---|---|---|---|
| 4 | 963 / 1313 | 20 | 15 |
| 5 | 1614 / 1906 | 2 | 0 |
| 6a | 1326 / 1668 | 67 | 0 |
| 6b | 2517 / 3600 | 2 | 0 |
| 7 | 1214 / 1401 | 56 | 14 |
| 8 | 3537 / 3600 | 25 | 0 |
| 9 | 2637 / 3600 | 0 | 7 |
| 10 | 953 / 1119 | 32 | 0 |
| 12 | 3420 / 3600 | 0 | 0 |
| 13 | 807 / 1020 | 11 | 0 |
| 14 | 446 / 544 | 4 | 0 |
| 16 | 3491 / 3600 | 6 | 0 |
| 17 | 3309 / 3600 | 0 | 0 |
| 18 | 2888 / 3600 | 1 | 0 |
| 19 | 3154 / 3600 | 16 | 0 |

On every losing level the controller builds at most one 3x3 of each room in its fixed list and
then waits for 70-98% of its decisions. It has no routine for anything else to do.

## 3. Level 4

Seed 1 lost its Heart at 5249 to player 1; seeds 2-3 had no result. The controller built one 3x3
Treasury, Lair and Hatchery by about tick 1300; 15, 54 and 32 creatures came through the portal and
died untrained. Level 4's file makes the Training Room and Workshop AVAILABLE, the Portal and Guard
Room UNAVAILABLE_ROOM (claim only; three neutral Guard Rooms draw Dark Elves,
[auto-placed-furniture.md](auto-placed-furniture.md)), and the Wooden door, one trap type and two
spells AVAILABLE. Our Level 4 outcomes do not yet match DK2's spread even under the owner's replayed
input (37 of 60 seeds overrun at hash 328, [level4-outcomes.md](level4-outcomes.md); residual Q710),
so a controller loss there is not wholly the controller's.

## 4. Limits

- Three seeds per level; one run per seed. Win rates are not estimated.
- Every level was started from its level file alone. Whether a campaign's carried-over rewards
  (`PreviousRewards`, read only by the presentation today) change a level's real start is not
  checked here.
- All runs used viewing player 3; none was refused, but which player each level gives the human
  was not checked against its PLAYERS records.
