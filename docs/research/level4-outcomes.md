# Level 4's outcomes: full replays against DK2's own spread

Q665. Moved from [resync-replay.md](resync-replay.md) on 2026-10-29; that file describes the resync tool the
runs below use. "Above" in the second section means the first. In "X against Y" DK2 is X and ours Y.

## Level 4's two outcomes, over seeds (hashes 320-328)

Evidence: `D:/DK2Evidence/runs/l4-modes-2026-10-29`, `l4-bisect-2026-10-29`, `l4-60seeds-hash328-2026-10-29`.
- Full replays end held out (under 20 Imp deaths) or overrun (20-55); at hash 328, 37 of 60 seeds are
  overrun and 1 wins. No hash in 321-328 moves that beyond seed noise: ADR-0566 (323) shifts the
  generator's draws from turn ~58 and so re-rolls every seed (322 vs 323 deaths p = 0.38).
- Over 73 runs (hashes 320, 323, 326, 328): held-out runs all set flag 11 (median 4336; DK2 4378),
  overrun runs half of them (median 6002), so parties 6 and 8 come. But overrun runs already lose Imps
  at 3660-3830, as parties 3 and 4 arrive (28 of 43 before 3900; 5 of 30 held out): the late flag
  amplifies an overrun that has begun.
- Those deaths fall at (23-29, 33-37), between the hero entrance and the heart; 68 of 111 Imps carry
  gold, last seen mining (e.g. (37,35)), withdrawing or raising the alarm. Tile work there ends by about
  3300 in both outcomes, so it is not a late claim.
- Reading: ours' held and overrun runs differ in how many Imps are on that gold route when parties 3
  and 4 arrive (3.01 against 3.62, next section), but DK2's do not, and over seeds ours does not mine
  more than DK2; the "mining surplus" is a resync-point result (3000-3700). No rule difference found.

## DK2's own spread over 8 seeds (hash 328)

Evidence: `D:/DK2Evidence/runs/l4-native-seeds-2026-10-29`, `l4-imp-states-2026-10-29` and
`l4-resync2400-2026-10-29` (scripts, outputs, READMEs). DK2: native captures of the l4live packets under
DK2 RNG seeds 1-8 (seeds 3-8 recorded 2026-10-29). Ours: the hash-328 runs above, whose generator is
not DK2's, so only distributions compare. Death counts use all 60; the state counts use 58 (seeds 14
and 18 ran under `rig-v2-check-2026-10-29`, 31 and 55 deaths). A DK2 death is a player-3 Imp's
`creature-map.csv` slot leaving type 1 (reproduces seeds 1 and 2: 0 and 19); ours' are `creature_died`
events. State entries are counted from change-only rows, so a state held under a tick is not seen.
- Imp deaths, DK2: 0, 19, 0, 56, 0, 19, 16, 64. None: DK2 3 of 8, ours 1 of 60 (one-sided Fisher
  p = 0.004). 20 or more: 2 of 8 against 37 of 60 (p = 0.056), but with DK2 at 16-19 three times the
  threshold decides it (at 15: 5 of 8 against 38 of 58, p = 0.58). First death median 3920 against 4012.
- The Imp headcount is DK2's through 3900 (16-18). Imps in (16-29, 32-37) over 3400-3900: DK2 2.97
  (2.47-3.60), not ranked with deaths (3.60 with 19, 2.88 with 64); ours 3.01 held, 3.62 overrun.
- Mining stints (state 26 entries) in 2400-3400: DK2 87.6 (66-106), ours 72.7 (13 of 58 below DK2's
  least, about 6 expected); in 3400-3900 38.8 against 37.2. Reinforce walks (21): 54.0 against 61.9
  in 2400-3400, 30.6 against 21.3 in 3400-3900. State 25 is not compared: DK2 enters it about 20 times
  a run and goes to 10 (`0x00465790`: task entry gone, or the Imp not within a tile; seed 1 read the
  first), where ours ends the walk without entering 25.
- Flights (ours: seeds 1-8, 6 of them overrun, against DK2's 2): 203 entries 1595 against 1704.
  Withdraws (202) 42% against 43%, and their destinations are the same room words (`0x004611D0`
  step 4, the room's `+0x27/+0x29`; arrivals at (23,53) 177 against 178; 13 of the top 15 tiles
  shared). Alarms: 479 arrivals in 251 against 684, 30% against 40%, the walk 9 ticks and 2.2 tiles
  against 10 and 2.2. Ours' withdraw walks are longer (median 39 ticks, 15.3 tiles straight-line,
  against 27 and 10.4); why is not tested.
- After a flight DK2 holds each of 202, 251 and 1 for a tick (1: always exactly one) and 252 for
  four (Imps; 432 of 432), and goes 203 -> 1 -> 10 when the flight ends without a walk. The
  dispatcher `0x00476FC0` runs one handler a turn, so the arrival's turn ends in the state it
  enters. Through hash 329 ours ran 202, 251 and 252 inside the arrival tick and went 203 -> 10 in
  one tick, so it chose sooner: median 6 ticks from leaving 203 to choosing against DK2's 14, 43
  against 58 to a job (ours seeds 1, 3, 7, 8; DK2 2, 4, 6, 8). Built at hash 331 (ADR-0580); the
  figures are not remeasured. DK2 captures: `D:/DK2Evidence/runs/l4-flight-dwell-2026-10-29`.
- Ours' overrun runs differ from its held runs in 3400-3900 by choices after a cut walk or a flight
  (at hash 329, 6 -> 10 and 203 -> 10, 36.5 and 36.6 a run, against 17.8 and 3.2): what being attacked looks like.
- Resynced at 2400 from each of the eight DK2 states (hash 329, native seed 1's entity map, unmapped
  0): ours' mining stints match DK2's for 600 ticks (2401-2700 24.8 against 26.4, 2700-3000 24.9
  against 23.9, per-seed signs mixed); from 3000 four seeds fall 9-16 behind as fights begin. Ours
  enters 22 a little more in 2401-2700 (18.4 against 16.4; more in 7 of 8, fewer in none), not chased.
- Reading, HYPOTHESIS (8 native seeds): DK2 is overrun on Level 4 too; only its runs without a death
  are significantly more frequent than ours. No mining rule differs (the 2400 resync); the full
  replays' mining shortfall is drift. The post-flight dwell above was the one rule-shaped lead; it
  is built (ADR-0580), remeasured below.

## From DK2's state at 2400, five runs a state (hashes 329-331)

Evidence: `D:/DK2Evidence/runs/l4-resync2400-multi-2026-10-29` (README, `compare.out`), and the one-run
tries beside it (`l4-resync2400-full-…`, `-split-…`), whose rank matches with DK2 (-0.28 to +0.72 over
four builds) are within the spread one run a state gives. Each of the eight DK2 states runs to 6804 under
five generator seeds, for hash 329, ADR-0580 alone (`e8eb76b9`, which read hash 330 in its own tree) and
`b2c2d089` (hash 331: ADR-0580 with ADR-0579, sleep debt, hunger checked every tick and need-group gating).
Limits: one DK2 run a state, each under its own native seed, so DK2's per-state figure is one noisy draw
(ours' within-state SD is 12-15); ours' generator is not DK2's; native seed 1's entity map serves every
state's packets (hand pick-ups land 30-61 times by state); DK2's deaths are player-3 Imp slots changing
type in `creature-map.csv`, ours' `creature_died` events, not matched event for event.

| Build | Imp deaths a run | Runs with none | 20 or more | Rank match of per-state means with DK2 |
|---|---|---|---|---|
| DK2 (one run a state) | 21.8 | 3/8 | 2/8 | |
| Hash 329 | 22.9 | 0/40 | 22/40 | +0.71 (p 0.028) |
| ADR-0580 alone | 25.9 | 0/40 | 25/40 | +0.53 (p 0.093) |
| Hash 331 | 28.9 | 0/40 | 27/40 | +0.93 (p 0.001) |

- Ours' state means differ only pooled over the three builds (permutation p 0.026; no build alone
  significant); their ranking agrees with DK2's single runs. That the state at 2400 carries part of the
  outcome is HYPOTHESIS.
- Together the two changes add about 6 deaths a run (hash 331 against 329, sign-flip p 0.016); the split
  (+2.9 for ADR-0580, +3.1 for ADR-0579) is not resolved, each interval spanning zero. Both follow the
  binary and are not tuned.
- No run of ours loses no Imp (0 of 120, from any state, minimum 1), against DK2's 3 of 8 (one-sided
  Fisher p 0.0002). DK2's three came from states 1, 3 and 5 under native seeds 1, 3 and 5, so whether the
  state or DK2's own later draws hold them is untested; a count of 1 against 0 may be the counting
  difference above.

## Where ours' first Imps die, and the heroes' choice (hash 333, ADR-0584)

Evidence: `D:/DK2Evidence/runs/l4-retreat-tracking-2026-10-29` (README, `alive.out`). The first Imp
death in each of the 15 hash-331 runs from states 1, 3 and 5 follows the same loop: a job, engaged by a
hero, a flight, a disengage, the next job, engaged again; the killers are party heroes (9 Guards, 5
Thieves, 1 Dwarf), not the post Guards, whose flag 14 is set near DK2's 5283. Counting live creatures
only, 2400-4400:
- heroes choose a player creature they meet at DK2's rate (0.264 against 0.258 a pair-tick) and an Imp
  less often (0.104 against 0.159); the target choice was audited against the binary (ADR-0584 built its
  one missing rule, which did not move the deaths);
- hero-Imp pairs within four tiles are 2.3 times DK2's; live heroes spend 1.7 times as long in
  (16-29, 32-38) and Imps 1.3 times. Which side's rule keeps them apart is open; the Imp's own arm
  (`0x0045BDF0`) matches the binary but for a thing-target detail that would make ours flee more.

## Hero phases in the gold-route zone (Q665 follow-up, hash 333)

Evidence: `D:/DK2Evidence/runs/q665-hero-zone-codex-2026-10-30` (`phase-v2.out`,
`lives.out`, scripts and README). Exact change-row intervals over turns 2401-4400,
alive only, zone (16-29,32-38). Ours is 40 resyncs normalized to eight runs; DK2 is
eight native runs. The corrected final interval adds seven hero-ticks to each
earlier zone total (14,077 / 8,405). Counts below are ours / DK2.

| Phase in zone | Hero-ticks |
|---|---|
| Fight 199 | 6,309 / 4,072 |
| Approach 8 | 3,869 / 2,792 |
| Walk 6 | 1,690 / 700 |
| Victory 260 | 1,297 / 26 (see the 2026-10-30 note below) |
| Standing job 90 | 250 / 78 |
| Flee 203 | 250 / 329 |

Note 2026-10-30 (corrected later the same day): an earlier note here said 26 counted
change-only rows. It was wrong: 26 is the exact interval count for 2401-4400, and episode
durations agree (DK2: 3 celebrations, 39 hero-ticks, 26 in zone; ours h336 per eight runs:
89.6, 1,357, 1,260). Section "Celebrations" below covers 2401-6804.

By spawn wave and type, party 4's 3470 Dwarfs account for 2,196 / 172 zone-ticks:
11 / 4 enter, mean 200 / 43 zone-ticks per entrant. Party 4's 3470 Thieves account
for 1,443 / 677 (4.6 / 1 entrants), party 5's 3830 Thieves 3,944 / 2,350 (15.4 / 14),
and its 3830 Guards 3,195 / 2,346 (8 / 7). Counts include repeat zone visits.
Party ids are checked in `party-map.out`. Modern births are one turn earlier at the same gates;
entry ages from birth, ours / DK2: Dwarf3470 mean 122 / 185 turns, Thief3830
65 / 81, Guard3830 64 / 68. Party 3's Dwarf2990 age at first excluded state averages 240 / 198 turns;
all entrants reach that endpoint by 4400. This is not permanent mortality: native
217 -> 75/78 transitions occur in these logs. The gap includes survival into the zone, not only
longer individual visits. These are distributions over distinct RNG streams.

The binary comparison found the chase-boundary differences in ADR-0590; see
[combat-approach-and-victory.md](combat-approach-and-victory.md#the-chase-boundary-adr-0590).
The matched-master comparison uses clean detached tools at `31a05119` (hash 335)
and `33fcd18f` (336), the same eight native states and five generators to 6804,
two workers, all 80 exits zero. All 80 paired creature/event prefixes through
2400 match byte for byte. `final-comparison.out` reports exact windows below;
modern occupancy totals are normalized to eight runs.

| Metric | DK2 | h333 | h335 baseline | h336 correction |
|---|---:|---:|---:|---:|
| Imp deaths/run, 2401-6804 | 21.75 | 28.30 | 29.63 | 26.60 |
| Runs without an Imp death | 3/8 | 0/40 | 1/40 | 1/40 |
| Zone hero-ticks, 2401-4400 | 8,405 | 14,077 | 14,146 | 13,746 |
| Zone Imp-ticks, 2401-4400 | 58,314 | 74,617 | 74,312 | 72,341 |
| Global four-tile hero-Imp pair-ticks | 34,288 | 78,984 | 79,073 | 73,698 |

The correction leaves 6,180 fight-199 and 3,812 approach-8 zone-ticks (DK2
4,072 and 2,792). Relative to h335, deaths fall 3.025/run, zone hero-time
400.6/eight runs and global proximity 5,374.2/eight runs. Exact two-sided
sign-flips of eight state means (five generators each, 256 signs) give
p = 0.172, 0.664 and 0.305; against h333, deaths fall 1.7 (p = 0.422),
zone hero-time 331.2 (p = 0.648). These shifts are unresolved over seeds.
The rule difference is established; it does not explain the residual 1.64x
zone time. Pair-ticks count every nearby Imp using integer tile coordinates,
not unique hero time or zone-local exposure. Native death counts are Imp-slot
departures, ours events; they are not matched individual deaths.

UNKNOWN: why party 4 reaches the zone eligible more often and why the remaining
fight/approach time is high. No matched native RNG continuation or controlled
defender-combat comparison has isolated that residual. No win-rate inference
or tuning follows. Archived tool manifests and verification are in the evidence
folder's `README-final.md`.

## Celebrations (Q665 lead, hash 336, ADR-0592)

Evidence: `D:/DK2Evidence/runs/q665-hero-zone-codex-2026-10-30` (h336 runs) and the native
`dk2runs/level4-live1*` logs. Window 2401-6804, per run; a celebration (state 260) is an
episode measured from its first to its last turn in the change-only rows, not a row count.

| Per run | DK2 (8 runs) | ours h336 (40 runs) |
|---|---:|---:|
| Celebrations | 9.5 | 31.9 |
| Celebration hero-ticks | 97.6 | 454.8 |
| Of those, in zone (16-29,32-38) | 76 | 313.7 |
| Imp deaths | 21.75 | 26.6 |
| Deaths followed by a celebration | 25% (a hero entering 260 within 3 ticks) | 54% |

Episode lengths (4, 13 and 22 ticks by creature) match DK2. Over 2401-4400 alone DK2's heroes
celebrate 3 times in eight runs, ours about 90 per eight. Ours' fight checked the victory
nibble before looking for another victim, so every kill celebrated even with enemies in
sight; DK2 celebrates only after a failed `SelectAVictim` (ADR-0592, hash/save 338). The
fix at hash 338 (`D:/DK2Evidence/runs/l4-celebration-2026-10-30`, 40 runs): per eight runs over
2401-4400, celebrations 3 / 89.6 / 16.4 (DK2 / h336 / h338), zone 260 hero-ticks 26 / 1,241 / 195;
over 2401-6804 celebrations 76 / 255.2 / 97.4. Zone hero-ticks barely move (8,405 / 13,746 / 13,360):
the time goes to fight and approach (6,864 / 9,992 / 11,008), and Imp deaths per run rise
(21.75 / 26.6 / 33.4; h338 - h336 sign-flip p 0.023). Gaps left open by the fix are in the ADR.

## Keeper fighters at the party-4 gate (Q665, hash 338-340, ADR-0593, ADR-0594)

Evidence: the same folder's README, "Party 4's Dwarfs" onward. Party 4's Dwarfs die before the zone 13
of 20 in DK2, 4.4 of 19 in h338: keeper fighters attack them half as long and back off (203) into a
walk home twice as often (25.2 against 13 per eight runs). The step back now follows DK2's heading
vector (ADR-0593). At hash 339, per eight runs DK2 / h338 / h339: Imp deaths per run
21.75 / 33.4 / 28.1 (h339 - h338 sign-flip p 0.14), zone hero-ticks 8,405 / 13,360 / 12,077 (p 0.17),
Dwarfs dying first 13 / 4.4 / 8.0. Back-offs ending in a walk home stay 25.0: most come another way.

The h339 step back walked a route, which turned the heading and flipped the next turn's "behind". Keeper
creatures backing off in 203 (stays of 4+ turns), per DK2 / h338 / h339 (`backoff-motion.out`,
`backoff-damage.out`): 0.069 / 0.072 / 0.004 tiles a turn, 94% of h339's never leaving their tile; health
share lost a turn in 203 0.0071 / 0.0092 / 0.0149. At hash 340 (ADR-0594; 40 runs, README "Hash 340"):
0.092 tiles a turn, 19% never leave their tile, 0.0068 lost a turn; healthy back-offs ending in the fight
43.2 and in a walk home 18.2 (DK2 52 / 13). Zone hero-ticks 10,998 (DK2 8,405; h340 - h338 p 0.12),
hero-Imp pair-ticks 54,937 (34,288; h340 - h339 p 0.023), Dwarfs dying first 7.8 (13), Imp deaths per run
29.1 (21.75; h340 - h339 p 0.59): the back-off matches DK2's, the deaths do not move.
