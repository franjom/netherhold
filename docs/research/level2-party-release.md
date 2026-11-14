---
systems: [heroes, campaign-triggers, imps-tile-work]
---
# Level 2: what releases hero party 1 from its patrol (Q525)

2026-11-14. Shipped data (`kwd triggers level2Triggers.kld`, `kwd things level2`, `kwd parties`), DK2's packet run
`level2-live2/seed01` (`F:/DK2Evidence/captures/native/native-dk2runs--unknown-date--001/payload/level2-live2/seed01/level2.dk2cap`)
and ours' replay of the same packet through the frozen rig `F:/DK2Evidence/rigs/rig-hash328--2026-10-29--001` at hash 516.

## 1. The patrol and its release

- **Patrol (shipped data, CONFIRMED in DK2's run).** Party 1's leader walks SendToActionPoint between action point 9 (13,13),
  next 2, and action point 2 (12..14,7) on its own Hero Gate, next 9, wait 10; the three others follow in 222
  ([hero-party-walk.md](hero-party-walk.md) §7; the tile test is [functions/00471EB0.md](functions/00471EB0.md), ADR-0800).
  DK2's leader cycles 173-174-175 from turn 4 to 3083.
- **Release (shipped data, STRONGLY INFERRED).** Members carry creature triggers 38-41, each setting objective KillPlayer
  (action 15, type 19) once flag 2 is 1. Flag 2 has two setters: trigger 31 on action point 3 (12,15), when its area holds dirt
  path (terrain 3) of player 2, that is once that wall is dug (it also sets flag 24); and triggers 48-51 on the members,
  condition 6, "attacked" (fight start, [trigger-walkers.md](trigger-walkers.md)). Action point 8 (13,15) sets flags 24
  and 33, not 2. The placed Dwarves at (25..28,17..19) (triggers 33, 34, 36, 37) also switch on flag 2.
- **DK2's release (capture, CONFIRMED timing).** The owner tags (12,15) at packet turn 2142 and untags it at 2209; DK2's task pool
  (capture region 18) holds that kind-1 entry with no worker throughout. The owner tags (12..13,15..16) at 3046; (13,15) is dug
  at 3108 (flags 24 and 33 set), the leader enters fight state 199 at 3110, flag 2 is set and the job word turns 23 to 19 at
  3111, all four are in 199 by 3117 at full health. (12,15) stays a reinforced wall until 3116, so the release is the
  "attacked" arm, by elimination.

## 2. Ours' whole-game replay (hash 516)

| Seeds | Library (owner's drag at 687) | Create Imp casts (2157-2269) | Breach | Party released |
|---|---|---|---|---|
| 1, 6 | built (site claimed by 480 in seed 1) | researched at 1484/1593; six Imps made | (12,15) dug at 2147/2198 (seed 1 by an Imp that was reinforcing it) | trigger 31 at 2148/2199 |
| 2-5 | refused, no claimed tile yet (seed 2 claims the site 731-800) | `not_available`: nothing researched | the 3045 tag is never dug (four Imps resting far south) | not before 3657 |

Rerun 2026-11-14 at hash 516 (six seeds, same rig): the same split. Ours also claims (11,15) at 2094-2105 in seed 1
against DK2's 2178. Each split is an owner command landing on ground ours has or has not claimed yet; §4 traces why.

## 3. Resynced from DK2's state at 2100

`capture_state.py ... 2100 --state-only` (a partial legacy frame), then `dk2tools replay level2 ... --resync 2100.json
--resync-at 2100 --births creature-map.csv` with seed 1's entity map, seeds 1-3 ([resync-replay.md](resync-replay.md)).
Outputs were not kept; rerun from these commands.

| | DK2 | seed 1 | seed 2 | seed 3 |
|---|---|---|---|---|
| owner's 2142 tag of (12,15) | untaken to the untag | untaken to the untag (2208) | same | same |
| (11,15) claim job | claimed 2178 | 2171 | 2172 | 2172 |
| (13,15) dug after the 3046 tag | 3108 | 3111 | 3120 | 3081 |
| party 1 to KillPlayer (DK2: job word; ours: `hero_job`) | 3111 | 3126 | 3127 | 3085 |

From DK2's ground state ours follows the same mechanism in the same order, within about 30 turns: the 2142 tag untaken, the
breach after the 3046 tag, then the release. The lag from breach to release is 3 turns in DK2 and 4-15 in ours (the leader's
patrol phase and wander draws differ; the run does not draw from DK2's generator). The resync does not restore spell
availability: legacy captures hold the player record but not the heap tables its `+0x6E`/`+0x72` pair points to
([player-object.md](player-object.md)), so seeds 2 and 3 still refuse Create Imp and keep four Imps where DK2 reaches ten.
In resync seed 1, Imps 5 and 26 trade the DigOwnWall task on (12,15) twice a turn from 3078 to 3111 without moving; that is
DK2's steal rule, not an ours fault (§4).
## 4. What splits the runs before 2100 (Q809)
Sources: DK2's `tiles.csv` and `creature-map.csv` beside the capture; ours' `events.tsv` and `ours-creatures.csv` from the rerun.
- **The Library site.** DK2 digs (12..15,31..34) from 310 to 397 and claims it from 445 to 686 (terrain flips), the last
  tile one turn before the owner's drag at 687. Ours digs it from 184-282 to 336-357 (seed 6: 568): the first site tile
  falls 115-125 turns before DK2's in seeds 1, 3 and 5, about 58 in seeds 2 and 4, 28 in seed 6 (see "The dig lead"). By
  687 seeds 1 and 6 have claimed all 16 tiles (last at 504/588), seeds 2-5 none (first at 695-874). New claims by 200: DK2
  10 (14 tiles with the 4 it starts with), seeds 1 and 6 10, seeds 2-5 0.
- **One dedicated claimer.** In DK2 and in ours, a worker keeps the kind it last took, and a marked dig outranks a claim.
  DK2's Imp 449 found nothing at 22 (three Imps held the first wall's slots), rested 23-48, wandered 49-55 and chose at 57,
  while (19,33) was being mined by the other three: it took the (19,34) claim and claimed until about 281. Ours' seeds 1
  and 6 reproduce this (Imp 6 rests, then claims from 59/51). In seeds 2-5 the resting Imp chooses at 72-78, after (19,33)
  is mined out and (18,33) opens, so it digs and all four stay diggers until digs run short (first claim `job_taken`
  244-368). DK2's diggers also turn to claims only then (Imp 448 chooses at 211; ours' seed 6 at 211, `job_taken`). The
  split is the rest and the wander walk's length (DK2 6 turns; ours 1-20), random draws on both sides (STRONGLY INFERRED
  for the mechanism). Over 40 ours seeds of `l2live2`, 24 get the early claimer (60%), so seeds 1-6's 2 of 6 is ordinary
  (P(at most 2) = 0.18): the Imp that finds nothing at its first reselection claims early after a 20-26 tick rest and a
  short walk, late after a 1-tick rest. DK2 has a spread too: replaying `l2win928` under seeds 1-7 it first claims at
  162-171 in three and 321-459 in four (Q484; those captures are gone); ours gets 15 of 40 early, at 138-172 (Fisher
  p=1.0; seven seeds cannot tell a rate).
- **The draws.** Native captures of Levels 2-5 against ours' replays (2026-11-14): the rest before a wander falls on
  1/20/25/26 ticks at 27/37/23/13% in DK2 (n=2056) and 26/37/24/13% in ours (n=3077, chi-square p=0.73), STRONGLY
  INFERRED that the rest lengths match. Walk lengths match in Levels 3-5 (Level 4 Mann-Whitney p=0.24), but Level 4's
  arrival distances do not (Kolmogorov-Smirnov p=0.0007; 8% of DK2's walks end 9 or more tiles away, 1.7% of ours'),
  unexplained. Level 2's own walks are HYPOTHESIS (DK2 n=79, one run).
- **The dig lead.** Every corridor and site tile is tagged by game turn 108 in both (DK2's pool shows the last at capture
  frame 109), and DK2's pool holds (17,33)'s kind-1 task from 36, but no worker takes it until 284. At each DK2 dig or mine
  choice from 72 to 283, with walls measured from the Imp's tile and seams from its last seam after a mine, (17,33) is
  never strictly nearer than the task taken (ties at 141 and for two of three choices at 209-211). DK2's Imps follow the
  north gold to 206, the east block (21..24,32..34) from 222 to 282, and the former claimer Imp 449 takes (17,33) at 284
  while walking (why it left claims then is unread). Tiles dug by 150/200/250/290: DK2 15/19/24/31; seed 6, with a
  claimer and DK2's order, 15/19/23/30 and (17,33) at 266. Seeds 2-5 have no claimer, so four Imps dig where DK2 has three;
  seeds 2 and 4 dig the east block alongside the north gold (from 110; DK2 from 222) and reach (17,33) last, at 226/221.
  Seeds 1, 3 and 5 take the west corridor before the north gold ((17,33) at 162-176); seed 1 has a claimer, and its Imp 5,
  standing at (20,33) at 96, takes (21,33) where DK2's Imp 448 at (19,33) mined (19,32). HYPOTHESIS: the lead is the
  claimer split plus Imp positions at near-tied choices, not a rule difference; a resync near 90 from DK2's positions
  would test it.
- **The (12,15) trade.** `0x00469AF0` takes a free slot first; with every slot held it measures a taker from its point
  to the wall's centre (`0x00469BD4`-`0x00469C4B`, `[EBP-0x3C]`) and a holder from its point to the side tile's centre
  (`0x00469D96`-`0x00469DE1`), strictly, read 2026-11-14. In resync seed 1 (12,15)'s only open side is (11,15) and its other
  slots are held; seen from about (25,38), each of two Imps at nearly one point is nearer the wall than the other is to the
  side. The newer Imp, updated first, kicks the older before its walk handler runs, and the older retakes in the same turn
  ([imp-task-stealing.md](imp-task-stealing.md) §5, §8), so the older ends each turn holding and neither walks. It ends when
  a new side opens (3112, after (13,15) is dug) or the keeper's fifth selection in a turn rests one of them. DK2's run has
  no Imp in state 10 for more than two frames; ours has this one, 34.
