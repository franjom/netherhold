---
systems: [tooling-process]
---
# Campaign bot: winning Level 5

**Goal.** The campaign player wins shipped Level 5 (`scenarios/campaign-ladder/level5.yaml`, seeds 1-6 measured) and still wins
Levels 1-4, playing only from what a human sees. Every step adds general behaviour backed by a rule in
`knowledge/rules.yaml`; nothing keys on the level's name.

**Owner direction (2026-11-17).** Level 5 has gold enough, hero raids enough to turn into Skeletons, and fights enough to
train creatures past level 4; boxes lie in the rock to be found by digging and claiming; the goal is the Lord of the
Land's position (R20 attack at the peak, R15/R16 Skeletons against Fear traps, R19 traps then guards then the boss, R65
a bridge under a hero who falls in water).

## What the level is (level files, 2026-11-17; facts from `kwd` dumps of `level5*.kld`)

- Win: the Lord of the Land (level 4, patrols (40,11)-(40,14)) killed. He dies instantly: no collapse, no capture
  (`CreatureDeathRules.DiesInstantly`). Lose: the Heart (16,25) destroyed. No time limit.
- Defences of his hall: Fear traps (30,11), (31,11), (30,14), (31,14) at its two mouths; Hero Sentry traps (34,11),
  (34,14); two level-1 Knights (35,12), (35,13). Owning 16 tiles of (28..41,10..15), a creature in (37..40,11..14), or
  both Knights dead sends the Lord hunting and spawns his entourage (Knight 2, Wizards 3, 2, 3) twice at the NE gate.
  More Sentry traps at (24,34), (25,36).
- Raids: five parties of Dwarves, Thieves and Wizards from the west Hero Gate (4,20), 12 tiles from the Heart, at 390,
  540, 690, 840 and 990 s (ticks 1560-3960). Claiming all 15 tiles of the hero Prison spawns another party at (29,42).
  The timer-1 chain (Knights and Wizards from the NE gate) is never started in our simulation: open owner question.
- Prison: not buildable (availability 4); the only one is the hero Prison at (22..26,41..43). A claimed Prison keeps its
  Activated bit (map prisons load Activated, `WorldState`; conversion keeps it, `0x004ED440`). No Torture Chamber.
- Pool: Warlock, Dark Elf, Troll (Library, Guard Room, Workshop). Spells: Possession, Create Imp, Thunderbolt. Trap:
  Sentry. Door: Wooden. Start gold 1500; 68 gold seams of 3000 (about 204k).
- Boxes in sealed pockets: Mana Boost (3,5), Receive Imps (40,33), Make Happy (6,39), Heal All (16,52), Locate Hidden
  Land (29,3), a gold bag (29,52). Two neutral Skeletons behind Hero Doors at (44,47), (46,48), guarded by two level-3
  Dwarves.
- Route to the Lord: north through the Portal (15..17,19..21), 4 rock and 6 water tiles, into the hall's west mouth
  (28,14). Almost every creature walks on water (`CanWalkOnWater`); bridges serve only to own ground.

## What the bot did before steps 1 and 2 (seeds 1-3, run 2026-11-17 `rd-level5`)

- About 14 heroes collapsed in the early raids (ticks 1800-5300) and died where they lay: the bot owned no Prison until
  tick 4044. One Wizard was carried to the Prison (tick 5387) and was walking free by tick 7378.
- Fighters peaked at 15 (tick ~16000); gold reached 0 by tick ~32000 and 14 creatures left over pay within 200 ticks.
  Mana sat at its 200k cap from tick ~8000. No attack on the Lord.

## Steps (each: tests, Level 5 seeds 1-3 and Levels 1-4 measured, reviewer, documenter, commit)

1. **Take the Prison before the raids (R16, R66).** Built. `PlayerStrategy.TakeRooms` (Portal, Prison, Torture Chamber)
   and `TakeAt` (a playbook's `plan.take: [[x, y]]`, for a room not yet seen) make Dig give up to 4 marks a decision to
   the frontier nearest the room, through held-back ground and away from enemy trap reach, until a known open path joins
   it to own floor. `knowledge/playbooks/level5.yaml` takes the hero Prison at (24,42); runs read it with
   `--playbook knowledge/playbooks/level5.yaml`. Measured (seeds 1-3, with the playbook): the Prison is owned at ticks
   2505/2902/2908 (was 4044). R66's Sentry ring and bridging are not built.
2. **Capture and hold the captives.** An Imp choosing its own work takes a body only once no marked rock, claimable
   floor or loose object is left (idle search table 0x00698658), but a hand-dropped Imp scans its landing ring first,
   where a capture outranks most work (0x0046D810 in the chain at 0x00469240): the Capture routine, between bridge
   and bank in the decide order, lifts the own Imp nearest an enemy collapsed on own ground (not on a Prison or
   Torture tile, no own Imp on its tile, no threat within 2 tiles) while a Prison is owned, and drops it on the body
   the next decision. The Wizard that "walked free" was never freed: the Imp that
   carried it fled a trap and our simulation, unlike DKII.EXE's WorkerJob leave hook 0x004647D0, kept the body in its
   arms (fixed on its own branch, `fix/imp-carry-drop`; the numbers below were measured without that fix). Measured
   (seeds 1-3, with the playbook): enemy bodies carried 11/7/7 of 15 (was 0-1), imprisoned 8/6/6, Skeletons raised 7/5/5
   (was 0). Level 5 is still lost (gold 0 by tick 24000-32000, the army of about 15 then leaves over pay); Level 4
   seeds 1-3 won, Levels 1-3 27 of 27. A down creature (flower level above 0, petals 1 or fewer) is no longer a threat.
3. **Bridge under a hero in water (R65).** A collapsed enemy on a water tile beside own floor gets a Wooden Bridge under
   it while its rescue window runs. Measure: captures from water.
4. **Spend mana on the fight.** Built (`Strike`, between bridge and capture in the decide order). Thunderbolt (spell 6)
   on the highest-level standing enemy on the keeper's own ground (Thunderbolt's location selector 1,
   keeper-spell-casting.md section 1) that is in a fight or near home, while mana minus the price stays above
   `strategy.mana-reserve`. Measured with step 6: 85-199 Thunderbolts a Level 5 run. Create Imp as before.
5. **Live within the income.** Recruit only while the gold and the gold still in sight pay the wages (no exodus); mine
   the remaining seams and open the box pockets on the way (boxes: R6 scouting, the special routine). Measure: no
   creature leaves over pay; boxes taken.
6. **Attack at the peak (R20, R19, R15, R21). Built before 4 and 5** (2026-11-17): the wage bill of an army of 15 is
   about 8,700 a payday (2,400 ticks), so 24 paydays cost about 209k against about 204k of seams on the whole map; no
   economy keeps that army, so it must be used while the gold lasts (owner: "I usually aim to win asap"). The Offence
   path of `CampaignPlayer`'s hand, with no attack on the dungeon to answer:
   - **Objective.** A playbook's `plan.attack: [x, y]` (`PlayerStrategy.AttackAt`; Level 5: (29,13), the west mouth of
     the Lord's hall, Fear traps at x 30-31). Without one a level is played as before.
   - **Approach.** With no own floor or own bridge within 2 tiles of the objective, and half the party and half the
     levels fit, the dig takes the objective as a room to take and the bridge routine spans toward it (a span must end
     nearer than any own floor; failed spans are retried after 1200 ticks).
   - **Readiness.** `AttackParty` (10) fit fighters whose summed levels reach `AttackLevels` (playbook tunable
     `strategy.attack-levels`; Level 5: 40, researched) and outweigh 1.5 times the enemy levels seen within 12 tiles of
     the objective; or, with the gold covering fewer than `AttackPaydays` (3) paydays, any 4 free. The Thoughts note
     says which.
   - **Order (R19, R15).** Fearless creatures (`CreatureFlags.IsFearless` through `CampaignPlayerOptions.Fearless`, set
     by `PlayerRunCommand`) first, the vanguard counting once landed; then every fit fighter, gathered in the hand over
     decisions and dropped together on own floor or an own bridge within 2 tiles of the objective; reinforcements
     every 400 ticks while they outweigh what is seen. An enemy within 12 tiles of the objective and nearer it than
     home belongs to the attack, not to defence one fighter at a time.
   - **Measured** (2026-11-17, hash 525): Level 5 seeds 1-6 all won, the Lord killed at ticks 12685, 13321, 15631,
     10481, 13641, 19442 (attacks launched at about 12000-18300; 1-4 Skeletons). Earlier iterations: 0 of 3 at the
     start; attacks at summed levels 17-25 lost the Heart by tick 10700 (1 of 6); threshold 40 without Thunderbolt 3
     of 6; with it 6 of 6. Level 4 seeds 1-3 won, Levels 1-3 27 of 27. `DK2.AI.Tests` 669 pass.
   - **Not tested.** Gathering over several decisions, an enemy at the objective, the approach bridging, the playbook
     `attack` parse and the `Fearless` wiring have no unit test; only the Level 5 runs exercise them.

## Open

- Not built: step 3 (bridge under a hero) and step 5 (live within the income). The Sentry traps (24,34), (25,36) on
  the Prison route still make carrying Imps drop bodies (R66's ring and rock tagging).
- Owner, from the reviewer on the attack: can Thunderbolt hit an enemy on hero ground in the real game (the bot casts it
  only on own ground); do players keep sending groups after a first wave dies; does the entourage reach the Heart's east
  side; is a Skeleton's fearlessness visible in its description (the bot reads it as public)?
- Owner, from the reviewer: does lifting an Imp that carries a body drop the body; does an Imp dropped on an unconscious
  hero take it at once; are starving prisoners ever moved by Imps; can rock in a hero Sentry's reach be tagged and kept?
- Owner: does DK2's Level 5 keep sending Knights and Wizards from the NE gate in the mid game (the timer-1 chain our
  simulation never starts)?
