# The resync replay: continuing our simulation from a DK2 capture's state

2026-10-28, ADR-0556 and ADR-0558. Built for Q665: a full replay cannot isolate an Imp rule once the two
worlds have drifted, so this run starts from DK2's state at a tick and continues with the same
packets. Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/resync/` (the JSON, the map,
three runs and `analysis/resync_compare*.out`).

## How it runs

1. `python -I tools/oracle/capture_state.py <capture.dk2cap> <out> <tick> --state-only` writes
   `<tick>.json`: tiles (terrain, owner, room slot, health), players (money `+0x7E`, mana
   `+0x82`, and the task pool), creatures with the Imp job block (resume `+0xDF`, task tile
   `+0x8C`/`+0x90`, task id `+0x8A`, side `+0x94`, slot `+0x96`, last kind `+0x98`, `JustDropped`
   `+0x99`, the aimed kind `+0x9D` and the last search's turn `+0x9E`, all at the state object
   creature `+0x24E`; gold in hand `+0x3FB`), objects (type, point, gold) and the world seed
   `+0x15A0`; doors and traps (type `+0xF6`, construction, health); each player's manufacture
   ring (ADR-0558, below). The marks are the pool's kind-1 and kind-2 entries, each kind's list walked from
   its head (player-task-pool.md); the capture records the pool as region `REGION_POOL` + player
   index, 18 KB for player 3 at 2340. Tile flag `+0x0B` bit 0 is the Secret Door flag, not a mark.
2. `dk2tools replay level4 - out.csv --packet ... --entity-map ... --resync <tick.json>
   --resync-at <tick> [--resync-map slot,ours.csv] --jobs jobs.csv`. After our turn `tick`
   (the replay's turn numbers are DK2's, so the capture's tick `T` is applied after turn `T`),
   `WorldResync.Apply` copies the state (ADR-0556 and ADR-0558 list what it copies and what it
   leaves) and the replay goes on. `--resync-map` names our creature for native slots the replay's entity map gets
   wrong for this build; the map here was built from the packet imp casts' turns, which match.
   `--resync-skip part,...` leaves the named parts ours (ADR-0569): tiles, marks, doors (and traps),
   gold, crates, creatures (the pairing; the parts below need it, and without it the entity map is
   not refreshed), pose (point, health, state; it also skips the dying collapse and the worker
   handling, so it includes jobs), levels, fear (with the fight-return stamp), jobs (without it a
   worker is placed as any other creature and keeps ours' job fields until its next choice),
   players (mana, manufacture), triggers, seed. A skip makes a hybrid state, so it splits what an
   outcome depends on rather than naming a cause.
3. `analysis/resync_compare.py` counts job starts per window and kind from `jobs.csv` and the
   capture's creature records.

Before the pool read, the marks were derived from the packet tags in our tile log and DK2's tile
log (a tile marked at or before the tick, not untagged, still solid in DK2's log). At 2340 the
derivation gave the same seven tiles as the pool: (9,57), (11,57), (11,55), (21,34), (20,34),
(20,33), (19,33).

## Level 4 seed 1, resynced at 2340, to 3400

| run | mine starts | claim | reinforce | Imps mining / claiming / reinforcing |
|---|---|---|---|---|
| native | 110 | 66 | 83 | 4.4 / 0.8 / 4.6 |
| aim read from `+0x6E`, no marks | 139 | 68 | 37 | 5.8 / 0.9 / 1.7 |
| aim from `+0x9D`, no marks | 119 | 37 | 85 | 4.4 / 0.4 / 4.1 |
| aim from `+0x9D`, derived marks, mine slots without their face | 118 | 67 | 88 | 4.3 / 0.8 / 4.6 |
| aim from `+0x9D`, the pool's marks, dig and mine slots with their face | 122 | 46 | 90 | 4.3 / 0.6 / 4.4 |
| the same, with DK2's doors, traps, crates, manufacture list and crate trips, and ADR-0557 (final) | 112 | 57 | 90 | not measured |

Without the blueprints the windows matched native's through 3100 (claims 20/44 by then are the
chains both sides run); after 3100 native claims the corridor (34,34)-(39,26), 37 claims, and ours
none. That corridor opens from an on-the-way claim of a miner walking to the store at 2861
(README, the seam-delay chase), whose crew came from Workshop trips. With them (the last row) the
east seam (29,34) is first struck at 2482 against native's 2476 (3232 with blueprints alone, 3072
with neither); this run's corridor claims are not recorded here.

- The dumper's `DroppedToDoJobId` (`+0x6E`) is 0 on DK2's three aimed reinforcers at 2340; the
  aim state 10 retries first is `+0x9D` (functions/00463DF0.md), 6 on all three. With the aim
  copied, our mapped Imps reinforce again as DK2's do.
- Without the marks, the Imp DK2 had digging (9,57) dropped the dig at once: ours had dug that
  tile at 2291 and its mark was gone; DK2 dug it at 2344. With the marks it digs (9,57) and then
  (11,57), as DK2's slot 431 does, at the same ticks.
- After the aim and the marks, the job mix matched for the first 750 ticks, but the seams still
  finished in another order: DK2's crew for (29,34) came from the Workshop at 2375-2378 (door-crate
  trips for blueprints our replay refused at 2006 and 2229, since those tiles were not ours yet).

## Doors, traps, crates and the manufacture list (ADR-0558)

- `capture_state.py` writes each door's and trap's type, construction flag and health, and each
  player's manufacture ring: points, the orders (cost, kind, type, the blueprint's point), the
  tail and slot 0's cost. `WorldResync` removes a door or trap DK2 lacks, lays one ours lacks
  (no gold, no order), takes DK2's construction flag and health, rebuilds each keeper's
  blueprint list in thing-slot order, replaces every crate with DK2's floor crates and copies the
  manufacture list, points, tail and slot-0 cost.
- An Imp on a crate trip (resume 34/36: task tile the blueprint, side word the crate's object;
  35/37 carrying) walks to the keeper's nearest crate of that object, or is given one and walks to
  the blueprint (`ImpTaskSystem.ReissueCrateJob`).
- Limits: the side word as the crate's object is STRONGLY INFERRED for 34/36 (four Imps at 2340,
  all 52, the Wooden Door crate) and a HYPOTHESIS for 35/37, where no carrying Imp is captured.
  "Nearest" is ours, so several reissued Imps may go to one crate where DK2 may have paired them
  otherwise. The blueprint list's order is DK2's thing-slot order, an assumption while slots are
  reused.
- The copy found Q618: with DK2's state in, our Imp met a trap crate waiting at 2380 where DK2's
  walked to an empty Workshop, because ours paid for a Sentry Trap at its own 1500 and DK2 at
  slot 0's 3000 (ADR-0557). The tiles that differ at the resync fall from 140 to 101.
- Job starts 2340-3400, native/ours: mine 110/112, claim 66/57, reinforce 83/90.

## The death window (resyncs at 3900 and 3496)

Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, section "The death window".

- Resynced at 3900, ours already differs in who is alive, and 12 native slots map to nothing (a
  stale entity map, Imps cast after 2340); ours then loses 27 Imps by 4800, native 0.
- With slot reuse handled, the hero parties match native in composition. Native arrives at
  2990, 3470, 3830, 4190; ours 30 ticks later. The offset is drift, not a trigger rule (ADR-0560):
  ours claims the Portal at 2059 against 2030, and flag 0 from trigger 1 starts timers 4-9.
- Ours also raises parties 6 (4460) and 8 (4940). A party spawns only while flag 11 is 0; native's
  Imps reach (8,25) in action point 8 at 4378 and set it, ours mines (9,26) at 4572 against
  native's 4320 and never does.
- Goblin slot 435 flees at 3500 natively on the intake its stun piled up (ADR-0559). In the 3496
  resync ours still does not flee: its frighteners, party 4, are not spawned yet.
- Full seed 1 replays to 6804: Imp deaths 45 with and without ADR-0559; the hash-316 run had 32,
  one seed, not attributed to ADR-0557.
- Resynced at 3520 with every creature and the trigger banks copied (below), the parties arrive
  on time; party 5's Guard still survived the fight at (15-17,31-34), resolved in the next section but one.

## Every creature, and the trigger banks (ADR-0560)

Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, section "The parties' offset".

- Pairing: the map's id while it names a live creature of the same player and type, else the
  nearest unpaired one, greedily in capture order. A native lying in 216/217 may pair with ours
  standing or dying, never with a corpse.
- Made creatures come at level 1 and are raised with the rest. They have no lair or party, and a
  native whose point is off the map is left unpaired. Extras are removed the way a creature that
  dies is, so what they carry goes on the floor; one in the hand is left.
- Dying: health 0, collapsed, with `DyingTicks` = 240 − DK2's health. That DK2's health counts the
  window down one a tick is read from the capture (3940-4180), not the binary. Ours books the window
  a game second at a time, so a death can land up to 3 ticks off DK2's.
- A higher level of ours is kept, since nothing in ours takes a level away.
- Fight return (ADR-0567): OldFightPos (`+0x52A`/`+0x52C`, both words the x tile) and OldFightTick
  (`+0x52E`) are copied onto each paired creature, clearing ours' own stamp. Without it a stale
  pre-resync stamp sent Dark Elf 27 back to (23,23), behind the hero door, at 3502: the 3400 resync
  lost 36 keeper creatures and the door fell at 3854. With it: 3 lost, door at 5312 (DK2 about
  5313). At 3000 the copy changes nothing (37 both ways); at 2340 45 becomes 33, not analysed. A
  capture written before the field leaves ours' stamp as it was. Full seed 1 replays at hash 323 lose
  65 Imps (default generator) and 23 (`--rng dk2`) against DK2's 0, while resyncs at 3400 (with the
  copy), 3520 and 3790 lose 3, 0 and 0: the full-replay losses come from drift before 3400 that
  headcounts do not show (one seed; which part of DK2's 3400 state protects the dungeon is open).
- Seed 1 at 3520 (measured at ADR-0560's hash; 0 lost on the rerun at hash 323): 4 Imps lost against none, the door falls at 5314 against about 5313, and flag 11
  is set at 4330 against 4378. Party 5's Guard survived at (15-17,31-34): see the Guard fight below.
- Not copied:
  - the trigger records' own active bit and repeat byte (record `+0x10`; `+0x0F` STRONGLY INFERRED,
    [trigger-tree.md](trigger-tree.md); not in the capture);
  - the hero job triple (creature `+0x409`/`+0x40D`/`+0x40F`, delay `+0x413`; ours GoodJob,
    GoodJobPlayerId, GoodJobActionPointId, WaypointDeadlineTick);
  - a native in DK2's hand, which the capture does not mark (it is placed at the hand's point);
  - an old-map row for a slot that starts after the resync, and the row for a native left
    unpaired;
  - creatures spawned after the resync, which still go through the old map.

## The Guard fight (resyncs at 3860 and 3790)

Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, section "The Guard fight, and a
resync at 3790" (`resync-guard/`, `analysis/native_elf_choice_3893_3903.out`,
`native_shot_flight_3900_3962.out`, `native_post_guards_job_5270_5320.out`,
`death_window-run3790.out`). Seed 1, one run each.

- Target choice is consistent with nearest-first in this one fight, with three heroes. At 3895
  DK2's Guard 418 is the nearest hero to each Dark Elf (squared distance in tiles² × 4096: 40385
  against 53390 and 100710), and by 3903 all five Elves have picked it. In the 3520 run ours'
  Thief stood nearer and was picked; ours' heroes had walked east to kill an Imp DK2 never lost.
- Shots are tested once a tick against a box (shots.md §4a). Ours' half-width for a Dark Elf shot
  on a Guard is (1638 + 942) / 2 = 1290. DK2's hits fit it once the Guard's own move that tick is
  allowed for: shot 2965 is sampled at (69,-1473) at 3909, which is (-62,-1272) against the
  Guard's 3910 point. DK2's shot 2997 is sampled at (2559,1603) and then (-1617,-988), so it
  passes through the Guard's centre between two samples and is not hit.
- Ours' Elves stack on one point and fire identical shots, so a pass between samples repeats for
  all five (3929-3936). DK2's launch points differ by about 700 units. An observation, not a rule
  change.
- Resync at 3860: party 5 is beaten (Guard down 3968, native 3957), but the three post Guards kill
  five Imps. Ours' Guards got KillPlayer jobs at 3805-3847, before the resync, which shows that
  their roots (records 47/77/80: condition type 6 Attacked, setting flag 14 and action 15
  objective 19) had fired. DK2's flag 14 is still 0. Rule for a resync: do it before the creature
  triggers that matter have fired in ours.
- Resync at 3790, run to 5400:
  - Party 5 arrives at 3829, 39 ticks after the resync, and is beaten; parties come on time and
    none extra are raised.
  - Rerun with the steal-holder tool (hash 322; `resync-guard/run3790-h322`; not bisected, and the
    resync's own pairing and counts differ from the first run's): no keeper creature dies in
    3790-5400, against 0 natively. DK2's slot 431, the Imp the first run lost at (14,29) at 3852
    (id 26, level 2), is now id 34 at level 1: it mines (8,29), heads east and flees party 5 at
    3842 (DK2 3846), a few ticks' margin. It then chooses another seam where DK2's walks to store.
  - Party 5 matches DK2's (Thief, Guard in the Dwarf's reused slot 418, Thief), and hero deaths match
    within 10 ticks except party 7's Thief (4508 against about 4546). The hero door (24,27) falls at
    5301 (first run 5310, native about 5313).
  - The post Guards: ours logs `hero_job` only when state 90's dispatch succeeds. A probe of the
    trigger's objective action (hash-320 build; its events and creatures match `run3790-h322` line
    for line to 5350) gives ours' Guard 10 5280 (DK2's 446: 5284), Guard 11 5314 (445: 5283) and
    Guard 12 5319 (444: 5307): the last two after ours' door falls.
  - DK2's attackers at the closed door re-run `SelectAVictim` every tick out of reach, and each pick
    fires event 6 on that Guard. Its Trolls (Blockers) take the nearest Guard; its Goblins
    (Flankers) join the Guard a Troll already fights (combat-target-selection.md §5), so DK2 touches
    two Guards before the door falls. Ours' attackers all stay on Guard 10.
- Reading, one seed, STRONGLY INFERRED: from DK2's state at 3790, ours reproduces the window apart
  from the post Guards' switch (two of three late), which follows from ours' attackers all taking
  one Guard, and a door falling 12 ticks early. The full replay's Imp deaths (65 at hash 323) are
  drift before 3400, not the window itself (see Fight return above). Imp levels are
  not the drift source; other species' were not compared (creature-update-order.md).
### Why ours chose one Guard; the wait-action gap (ADR-0566, hash/save 323)

One seed, 3790 resync. Evidence: `resync-guard/run3790-points-codex-20261028/`
under the evidence root above: saved tool, logging patch, exact-point log and `comparison.txt`.
The probe preserves replay/events/creatures/jobs byte for byte against `run3790-h322`.
At 5283 ours scans before moving, just as DK2 does; the scan repeats while out of reach.
Our Troll point is (100761,116062), near native 440's pre-move (100766,116167).
The Guard points differ more: ours 10=(100261,110003), 11=(103081,107062);
native 446=(98984,107176), 445=(105641,108449). The discounted Blocker scores are:

| Position control at 5283 | Guard 10 / 446 | Guard 11 / 445 | winner |
|---|---:|---:|---|
| our attacker, our Guards | 7895 | 18452 | 10 |
| our attacker, native Guards | 17541 | 17467 | 445 |
| native 440, our Guards | 8170 | 18853 | 10 |

CONFIRMED in this probe: points explain the Troll choice; the Flanker crowd counts favor
that engaged Guard, using both arrays and the existing per-creature sweep. Approach meets the door
and forces its target at 5282/5284/5288 (ours 21/45+16/19); the Guard choice already differs before
that contact. This does not establish global steering or scheduler parity.

The smaller retry issue has a rule gap. State 176 (`0x00486CF0`) holds a current action regardless
of its deadline; without an action it starts counted action 43 while the deadline is live, otherwise
wandering. Failed state 90 (`0x00477850`) still writes
now+12; state 4 (`0x00477680`) holds the action before testing that deadline. Native 445's action 43
(start stamp 5276) remains through 5303; 446's (5274) through 5301, explaining their long state-4 stays.
Their north walks are 87 -> 6 resuming 1 at 5307/5308 and 5305/5306: idle wandering, not KillPlayer success.
Ours omitted the wait action. ADR-0566 adds its start and hold using existing ActionUntilTick timing.
Control flow is CONFIRMED; fresh-install timing is STRONGLY INFERRED, retaining the existing
phase/alternate-draw limits of [the timer surrogate](animation-clock.md).

One post-fix run, `resync-guard/run3790-h323-wait-action-codex`: the two Trolls first choose 11 at
5280/5281; Goblins 17/18 choose 11 at 5283. Guard-target changes through 5290 are only 11 -> door,
not native's 445 -> 446 -> door. Native touch times 445/446/444=5283/5284/5307; ours 11/10/12=
5280/5313/5301 (first state-90 entry). Door destroyed 5307 (baseline 5301, native about 5313).
The fix changes the pre-3790 run too: resync tiles 95 vs 102, added/removed creatures 7/5 vs 9/4,
so this is not an identical-input causal A/B. No campaign or win-rate inference follows.

The drift before 3790 (the dig order at about 550) comes from the first dig's lag, which is RNG
(STRONGLY INFERRED, one seed; creature-update-order.md, "The first dig and RNG").

## Mining against reinforcing (resyncs at 3000, 3400 and 3700)

Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, "Mining over reinforcing in
3000-4000, resynced", and its review corrections; runs in `resync-reinforce/`. Unsynced in 3000-4000
(`job_taken` events), ours takes 224 mining and 35 reinforcing jobs at hash 316 and 184 and 54 at
hash 318. Resynced, job starts native/ours (`resync_compare.py`, a different count):

| from | reinforce | claim | mine | Imps walking to or at a seam |
|---|---|---|---|---|
| 3000, to 4000 | 90/83 | 74/84 | 127/175 | 5.5/4.8 |
| 3400, to 4400 | 79/81 | 59/69 | 137/165 | 5.6/5.2 |

- From DK2's state ours reinforces at DK2's rate. The unsynced reinforce shortfall is drift
  (STRONGLY INFERRED, one seed).
- Ours starts about 20% more mining walks. From 3400 to 4000 it finishes about as many seams (14
  against 13, leaving out three the resync tick converts). It has more stints (65 against 48), more
  full deliveries (46 against 37), and more part-load ends when a seam falls under its miners (16
  against 8; none at a standing seam on either side).
- Why the stints are shorter, HYPOTHESIS: crowding at the seam (README, "Why ours' mining stints
  are shorter", and its review corrections). Measured over 3420-4000:
  - Strikes: native 144, ours 155, at 272-273 gold, 1 or 4 ticks apart. About 3.0 against 2.4 a
    stint.
  - Two or more Imps at one seam: DK2 4% of seam-ticks, ours 22%. At each fall DK2 has one Imp at the
    seam (13 of 13), ours 22 at 14 seams.
  - This does not hold as a cause on its own:
    - the 3000 resync is crowded like DK2 (6%), and its stints are short too (not recounted);
    - DK2 itself crowds 14% of seam-ticks in 2400-3000, and 15% in 3900-4400.
  - Ours also chooses after a part load more often: mine 24 against 12, claim 7 against 1.
  - No rule difference was found besides ADR-0562 and ADR-0563 (when state 26 asks its checks).
  These match DK2's:
  - the store walk starts in the tick of the filling strike (native 25 of 36, ours 36 of 44;
    `0x00465870`, ADR-0512);
  - gold is picked up in passing on a walk to mine, and the full load is walked on to the seam;
  - an Imp chooses a job while full.
  One did not match: a miner that reached its seam full stayed in 26 two ticks where DK2 stays one.
  ADR-0562 fixes it (hash 319). ADR-0563 (hash 320) moves the hands, store and pile checks to the
  call that begins an action, so a first-strike fill leaves 3 or 4 ticks after its strike, as DK2's
  do. From the 3400 resync ours still starts 101 mining walks to DK2's 80, to 4000. The counts above
  (strikes, crowding, stints) were taken before ADR-0563 and are not recounted.
- A third point, 3700 to 4100 at hash 320 (`resync-reinforce/run3700`, "The mining surplus at a second
  resync point"): mine starts 70 native, 91 ours; reinforce 22/24, claim 21/27. Imp-time on mining is
  equal (6.8 and 6.9 Imps), so ours spends it in more, shorter starts. Seam crowding is 19% against
  7% native there but 8% from the 3400 resync, so it is not a steady rule difference.
  - Steals of a walking Imp's seam slot: 18 against 6 (DK2's count is inferred from capture frames, so
    it may be exaggerated); from 3400 only 4 against 1. A steal adds at most one start, so from 3700
    that is about half of the 21 extra starts, and from 3400 3 of 21. The rest is open, HYPOTHESIS:
    state.
  - The steal test in `0x00469AF0`'s mine arm ours matches (`TaskStealing` shifts each axis'
    square right 12 as DK2 does), for a walking holder and one at its seam alike: both read
    `+0x1A/+0x1E` (`0x0046A1AA`-`0x0046A2F9`). Ours measured a holder in 25 and 26 from its stance
    point until ADR-0565; no steal counted here was from such a holder, and the reruns are
    byte-identical.
- A walk to a seam that fell on the way: DK2's state 25 (`0x00465790`) finds the task entry gone and
  goes to 10. Ours ends the walk `out_of_reach` through state 1. Both choose two ticks after arrival
  (20 native, 15 ours), so it is only a label.
- Mid-walk mine steals with the seam standing: native 1, ours 4 (leaving out the resync tick).
- From 3000, ours' Imps enter 203 92 times in 3000-4000, against DK2's 22. This is a resync artefact.
  DK2's two Dwarf party members (423, 424; spawned at 2990 with 750 and 787 gold) were walking to
  attack the Heart, resuming 222 and 178, and both were down by 3124. The resync placed them on DK2's
  point, sent them idle and copied neither the job triple nor the gold ("Every creature" above). Ours'
  32 wanders by the seam crews until it dies at 3624. 77 of the 84 flights after 3127 are within 8
  tiles of it. A non-Imp attacker sets a NonFighter's flee counter to 3 (`0x0045EC10` through
  `0x0045EC80`, STRONGLY INFERRED).
- From 3400, ours' Imps rest or wander more from 4100 (35/33 against 16/11). Only ours has Imps in
  states 251, 42, 202 and 203. At 4208-4210 six Imps converge on one unconscious body, and the
  4-selections cap throttles three to rest. 17 of 20 idle entries are over 10 tiles from any hero.
  Reading: combat drift (a casualty only ours has), HYPOTHESIS.

## Imp deaths and the player's door (resyncs at 3400, by part)

Evidence: `D:/DK2Evidence/runs/q665-job-choice-2026-10-28/README.md`, "Level 4 Imp deaths, seed 1, at
hash 323" and "What protects the dungeon at 3400". Seed 1, hash 323.
- Full replays lose 65 Imps (default generator) and 23 (`--rng dk2`), DK2 none. Resynced at 3400
  they lose 3 keeper creatures, and 0-4 over seeds 2-9 with the hero door at 5309-5316: from DK2's
  state the outcome is robust.
- Leaving one part ours at 3400: pose 45, jobs 43 (nested in pose), fear 36 (the stale stamp),
  doors 35; levels, players, marks, tiles, gold 0-3.
- The doors part is the player's own: DK2 has a built door at (24,39), north of the dungeon between
  the heart area and the hero door, and trap blueprints at (26,35), (22,35), (29,32), (30,33). Ours
  refused the placements by DK2's own tile rule (`0x00450EB0`, `BlueprintPlacementRules.CanTrap`),
  because it had not yet claimed the tiles: (24,39) claimed at 2093 against 1997 (DK2's placement at
  2006 is 9 ticks after its claim), the trap tiles 190-250 ticks later or not by 3100. The default
  generator's run refused the door and those four traps; the `--rng dk2` run the door, (9,52),
  (29,32), (30,33), (36,32) and (34,32).
- The placements are not the full runs' cause. A replay that holds a refused placement until ours
  owns the tile (branch `tools/replay-blueprint-retry`) sends every one and builds the door, yet the
  Imp deaths go 65 -> 36 and 23 -> 39; heroes break the door (also from DK2's 3400 state, at 4264,
  with 3 deaths). Resyncs at 2340 and 3000 carry the door and still lose 33 and 37.
- Reading, one seed: the doors part is sufficient to break DK2's 3400 state, HYPOTHESIS as anything
  more. What protects the dungeon from DK2's state is open; the creature parts (pose, jobs) are the
  remaining candidates, and hero positions were not examined. No rule difference was found.

## Limits

The resync copies state, not history: routes, action phases, the task pool's holders and room
occupancy are ours, rebuilt from the same packets. Our loader queues no order for an authored
trap where DK2's does (Q667). An idle or wandering Imp is set choosing. A wander walk's goal, a
state-12 Imp's running idle action and its `+0x7E` deadline are not copied. So it sees a new mark at
once, where DK2's waits out its spell or walk. Resynced at 130, the first dig's state 14 is at
133-134 against DK2's 138. A creature that is
not an Imp is placed on DK2's point and sent to idle when its state differs (ADR-0560). Treasury gold is tile stores
here (ADR-0015), so DK2's money is reported, not copied. The run drifts again after the resync;
compare the first few hundred ticks, and resync again later for the next window.
A DK2 fight-return stamp is copied raw in DK2's turn unit, while ours stamps with the tick before
the turn it runs, so a copied window can end one tick after DK2's; no creature held a stamp at
2340, 3000 or 3400, so the copy has so far only cleared ours. It is skipped for a creature in the
hand and one left unpaired.
