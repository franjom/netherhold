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

Ours also claims (11,15) at 2094-2105 in seed 1 against DK2's 2178. Each split is an owner command landing on ground ours has
or has not claimed yet; why ours' Imp work runs ahead of or behind DK2's before 2100 is not traced (Q809).

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
availability: seeds 2 and 3 still refuse Create Imp and keep four Imps where DK2 reaches ten. Not checked: in resync seed 1,
Imps 5 and 26 pass the DigOwnWall task on (12,15) back and forth each turn from about 3078 to 3111.
