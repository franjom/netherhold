---
systems: [tooling-process]
---
# Campaign bot: winning Level 5

**Goal.** The campaign player wins shipped Level 5 (`scenarios/campaign-ladder/level5.yaml`, seeds 1-3) and still wins
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
4. **Spend mana on the fight.** Thunderbolt on raiders near home while mana is over a reserve; Create Imp as now.
   Measure: mana no longer capped; raids beaten with fewer losses.
5. **Live within the income.** Recruit only while the gold and the gold still in sight pay the wages (no exodus); mine
   the remaining seams and open the box pockets on the way (boxes: R6 scouting, the special routine). Measure: no
   creature leaves over pay; boxes taken.
6. **Attack at the peak (R20, R19, R15, R23).** An army-strength estimate from what is seen (types, levels, petals of
   own and enemy creatures), the counters in `knowledge/guides.yaml` as the first table; Skeletons first at the Fear
   traps, then the party at the Sentries and Knights, then the whole army, healed, at the Lord. Measure: Level 5 won.

## Open

- Owner, from the reviewer: does lifting an Imp that carries a body drop the body; does an Imp dropped on an unconscious
  hero take it at once; are starving prisoners ever moved by Imps; can rock in a hero Sentry's reach be tagged and kept?
- Owner: does DK2's Level 5 keep sending Knights and Wizards from the NE gate in the mid game (the timer-1 chain our
  simulation never starts)?
