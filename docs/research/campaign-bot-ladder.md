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
Sections 1-6 describe the controller before Task 2 (the strategy-driven refactor of Task 1 played
the same); section 7 is the developing controller after it.

The player's own default cap, `--player-max-ticks 14400` (one hour of game time), applied, so
"no result" means none within that hour, not that the level cannot be won.

## 1. Results

| Result | Levels |
|---|---|
| Won, all seeds | 2, 3 (rerun after ADR-0800, below; accepted, section 2) |
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

## 2. Acceptance of Levels 1-3 (2026-11-14)

Won on the ladder (plan: at least 9 of 10 accept seeds, journals replayed). A fresh receipted
Release build of master `179d9e38` (clean tree, hash 516) ran the unchanged controller on accept
seeds 11-20 with `watch <scenario> --campaign-player 3 --player-receipt <dir> --seeds 11-20`. Every
run claimed acceptance: Completed, the Reaper took the Portal Gem, and the journal replayed without
the controller with every hash matching.

| Level | Won | 95% Wilson interval | Win ticks |
|---|---|---|---|
| 1 | 10 / 10 | 72-100% | 2079-2489 |
| 2 | 10 / 10 | 72-100% | 3821-4761 |
| 3 | 10 / 10 | 72-100% | 3595-6906 |

Evidence: `F:/DK2Evidence/studies/campaign-bot-l01-l03-acceptance--2026-11-14--001` (receipt, 30 run
folders, `manifest.json`, `files.sha256.csv`).

## 3. The common pattern: the controller waits

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

## 4. Level 4

Seed 1 lost its Heart at 5249 to player 1; seeds 2-3 had no result. The controller built one 3x3
Treasury, Lair and Hatchery by about tick 1300; 15, 54 and 32 creatures came through the portal and
died untrained. Level 4's file makes the Training Room and Workshop AVAILABLE, the Portal and Guard
Room UNAVAILABLE_ROOM (claim only; three neutral Guard Rooms draw Dark Elves,
[auto-placed-furniture.md](auto-placed-furniture.md)), and the Wooden door, one trap type and two
spells AVAILABLE. Our Level 4 outcomes do not yet match DK2's spread even under the owner's replayed
input (37 of 60 seeds overrun at hash 328, [level4-outcomes.md](level4-outcomes.md); residual Q710),
so a controller loss there is not wholly the controller's.

## 5. Triage of Levels 4-10 and the driver (2026-11-14)

Reruns with the 4-hour cap (`scenarios/campaign-ladder/`, `--seeds 1-3 --player-max-ticks 57600
--player-wall-seconds 1800`) on master at hash 516, plus the level files (`kwd triggers`, `kwd
things`, `kwd vars -a`) and the open questions. Every level gives the human player 3 (PLAYERS
records). On Levels 4-9 the win is killing the Lord of the Land (a "killed" condition sets a flag,
then WIN_GAME, action 10); on Level 10 it is destroying player 4's (Asmodeus's) Heart.

| Level | Rerun, seeds 1-3 | Win needs | Rival keeper | Open questions touching the outcome |
|---|---|---|---|---|
| 4 | lost 14419, lost 4656, no result | assault on the Lord about 50 tiles north, in a keep with doors and traps | none | Q710, Q612 |
| 5 | lost 7622, 8087, 8141 | assault on the Lord at (40,14); Heart defence first | none | Q772, Q719, Q762, Q763 |
| 6a | lost 4542, 6523, 6072 | the Lord at (40,43), 22 tiles from the Heart; empty creature pool, so only the starting roster | none | none names the level (Q748 is observation only) |
| 6b | no result (over the 64 MiB output budget) | the Lord at (16,7), about 68 tiles away | none | none names the level |
| 7 | lost 6045, 6190, no result (2379 own creatures died) | assault on the Lord at (55,36) | Gormagon (player 4) | Q582, Q586 |
| 8 | no result (over the output budget) | the Lord starts 4 tiles from the keeper's camera: defence | Dante (player 4, no AI) | Q589, Q345 |
| 9 | lost 7491, 7127, 6845 (scripted loss) | the Lord comes in a hero party | none | none names the level |
| 10 | lost 4338, 4317, 4311 | destroy Asmodeus's Heart | Asmodeus (player 4) | Q757 (computer keepers) |

Every defeat on Levels 4-7 and 10 is the keeper's Heart destroyed. Level 9 changed since the 1-hour
ladder (no result then); master moved several hash versions between the two.

**Driver: Level 6a**, the earliest losing level whose loss is the controller's and that no open
outcome question names. It has no portal creatures, so it tests defending with and directing the
starting roster, and the Lord is near. Generic combat questions (Q567, Q725, Q764, Q770) apply to
every level. Levels 6b and 9 also qualify and come next.

**Rewards.** Each level's `PreviousRewards` equal the previous level's `NextRewards`, and every
reward item for Levels 5-10 is already in that level's own availability table, so the level files
carry their unlocks themselves. Whether DK2 also applies `PreviousRewards` at a level's start is not
established; `campaign-win-sequence.md` section 5 covers what is read of them.

**Output budget.** A 4-hour player run writes more than the 64 MiB default (6b: 109 MB, 8: 83 MB);
per-level caps need `--player-output-mib` too.

## 6. Limits (sections 1-5)

- Three seeds per level; one run per seed. Win rates are not estimated.
- Every level was started from its level file alone. Whether a campaign's carried-over rewards
  (`PreviousRewards`, read only by the presentation today) change a level's real start is not
  checked here.
- All runs used viewing player 3; none was refused, but which player each level gives the human
  was not checked against its PLAYERS records.

## 7. Task 2: development (2026-11-14)

Plan: [stage 3, Task 2](../plans/2026-11-14-campaign-bot-stage3.md). The player now grows an
outgrown Lair, Hatchery or Treasury, builds the panel's other rooms once each, and holds digging back
from other sides' open ground and from enemy sightings until no safe tile is left (`BreachMode`
`WhenDeveloped`). It carries idle creatures to a Training Room or Workshop only when a level's strategy
names it (`UseRooms`, empty by default). Build: base `18ed2ec6` plus the Task 2 change, hash 519.
Evidence: `F:/DK2Evidence/studies/campaign-bot-task2-development--2026-11-14--001` (add-only,
`manifest.json`; the full per-seed table is `payload/scripts-and-tables/t2-after.md`).

**Diagnosis, seeds 1-3, unchanged player (before) against Task 2 (after).** Seeds claim nothing.
Outcomes are wins, Defeated (Heart lost) or timed out at the 57600 cap; room tiles are the mean of
the three seeds at the last observation.

| Level | Before | After | Room tiles before / after | Wait share after |
|---|---|---|---|---|
| 1 | 3/3 won | 3/3 won | 18 / 18 | 70-75% |
| 2 | 3/3 won | 3/3 won (later, ticks 4198-5245) | 18 / 42 (adds Library, Training Room) | 74-78% |
| 3 | 3/3 won | 2/3 won; seed 3 Defeated at 9402 | 26 / 61 | 65-73% |
| 4 | 0/3 (2 Defeated, 1 timed out) | 2/3 won (ticks 21245, 16688); seed 3 Defeated at 6959 | 54 / 161 | 73-77% |
| 5 | 3 Defeated (7622-8141) | 3 survive to the cap | 47 / 77 | 97% |
| 6a | 3 Defeated | 3 Defeated, sooner (3854-4920) | 131 / 166 | 72-80% |
| 6b | 3 timed out | 1 timed out, 2 Defeated (seed 3 at 4444): worse | 92 / 126 | 55-91% |
| 7 | 2 Defeated, 1 timed out | same | 101 / 134 | 83-87% |
| 8 | 3 timed out | same (idle 99%) | 16 / 16 | 99-100% |
| 9 | 3 Defeated | 3 Defeated | 63 / 69 | 67-69% |
| 10 | 3 Defeated | 3 Defeated | 25 / 70 | 75-81% |

Room tiles rise 2-3 times on most levels; the wait share is reported, not a goal. Level 3 seed 3 and
Level 6b are worse than before. Level 5 no longer loses its Heart but does nothing for 97% of its
decisions. The Level 4 wins are diagnosis seeds on a level whose outcomes still differ from DK2's
(Q710, section 4): Level 4 stays out of the driver choice and is not accepted.

**Strategy decisions.** A first default of never breaching could not win Level 1 (it stopped digging at
tick 3253), so the default breaches once no safe tile is left. An earlier accept with the Training Room
carrying on by default lost Level 3 accept seeds 12 and 13 (8/10). On those two seeds, training off won
both, extras off won both, and always breaching still lost seed 13, so carrying creatures to the
training lifts was the cause and `UseRooms` defaults to empty.

**Re-acceptance of Levels 1-3 on accept seeds 11-20** (receipted build, dirty tree recorded with
`tracked.patch`): Level 1 10/10, Level 2 10/10, Level 3 10/10 (each 72-100% Wilson), all acceptance
claimed, finale `ReachedPickup`. Two later edits (clearing refused work tiles; one `IsThreat` doc line)
change no default decision: the nine seed 1-3 baseline games rerun identical in hashes, decisions and
actions. `scenarios/campaign-player/baselines.tsv` is re-recorded at hash 519.

