# Campaign bot defence implementation plan

> Execute task by task; each task: tests first, reviewer, documenter, commit named files, merge.

**Goal:** The campaign bot answers danger: it defends its whole dungeon against an attack wherever it breaks
in, drops its creatures in a formation by fighting style, and slows attacks with traps on the routes they take.

**Owner direction (2026-11-15):** "bot needs to respond to danger properly. it needs to set up traps in the
dungeon to slow down attacks, pick up and drop creatures to fight attackers, based on their class. tanks in
front, support and squishy in the back, flankers and blitzers behind enemies to attack in the back." Choices
made when asked:
- **Trap sites:** corridor tiles on the routes attackers take (from hero gates, portals, enemy ground and past
  sightings) to the Heart, 3 or more tiles apart, nearest the entry first.
- **Trap choice:** by the kind of site, not by cost alone (owner's answer to Q851, below), keeping a gold reserve
  for rooms.
- **Which attacks:** "heart defense is priority, but bot shouldn't let attackers destroy the rooms of dungeon
  just because the rule says near heart... it should defend its entire dungeon, like if dwarfs break into
  dungeon it should react to that attack vector." An enemy on or beside the bot's own ground is an attack; the
  one nearest the Heart is answered first. Enemies far from the dungeon (Level 15a's Guards in their own Prison)
  are watched, not attacked. This answers Q847.
- **Formation (tight):** Blockers 1-2 tiles in front of the enemy, between it and the Heart; Support (and
  fragile creatures) 2 tiles behind the Blockers; Flankers and Blitzers 1-2 tiles behind the enemy. (The
  first reading was 3-4 tiles behind the Blockers, built as 5 in front of the enemy; the owner corrected it on
  2026-11-15, Q850: five "may not engage at that distance".)

**Architecture:** Bot policy in `DK2.AI` (`CampaignPlayer`, `PlayerActionRoutines`), fed by public definition
data the host supplies (as worker types are) and the player-visible observation. No simulation change. DK2's
own computer keeper is no model here: it never reads fighting style and drops on the owned tile nearest the
attack (`computer-keeper-threat.md`, `computer-keeper-handlers.md`); this plan follows the owner's play.

## Facts the design rests on

- **Fighting style** is creature data (`CreatureDef.FightStyle`: NonFighter, Blitzer, Blocker, Flanker,
  Support) that a player reads in the creature's description ("Fighting Style: Flanker"). In combat a Blocker
  takes the nearest enemy, a Blitzer enemy Support first, Support a target its own melee already fights, and a
  Flanker walks to a point behind its target (`combat-target-selection.md`, `combat-approach-and-victory.md`).
- **The hand** drops a non-worker only on own or allied ground (`HandRules.MapAllowsCreature`, 0x00450910): a
  Flanker can go behind attackers only where that ground is the bot's. Elsewhere it goes beside the Blockers.
- **Traps** are bought as blueprints on own, non-room, non-solid floor with no door or trap (`blueprint-placement.md`);
  the gold is paid at once, the Workshop builds the crate and Imps carry it (`workshop-crates.md`). A Workshop and
  creatures working in it are needed; `PlayerWorkshopOffer.NeedsWorkshop` says when it is missing.
- A **corridor tile** is open floor with solid neighbours on two opposite sides (DK2's own test, `004FFA50`, which
  tries only the left/right pair; the bot tries either pair).

## Tasks

### Task D1: what the bot needs to see
**Built 2026-11-15.**
- `CampaignPlayerOptions.FightStyles` and `StyleOf`: creature type to fighting style, supplied by the harness from
  `CreatureDef.FightStyle` (public data, like `WorkerTypes`), heroes' types included. A type not listed is a
  NonFighter; with no data at all every type is a Blocker, so a host without it still defends.
- `PlayerTileFlags.OwnTrap` (observation schema 6): a tile holding the player's own trap or trap blueprint, so
  sites are not reused. Another keeper's traps stay hidden. The trap routine (D4) counts it.
- Tests: the harness fills the styles; the observation lists a placed trap.

### Task D2: which attacks to answer
**Built 2026-11-15.**
- An attack is a visible threat within `strategy.enemy-near-tiles` (8) of any own tile (claimed floor or room), not
  only of the Heart (`PlayerActionRoutines.NearestAttacker`). Several attacks: the one nearest the Heart first.
- The fight routine (`FightMode.Engage`) answers only attacks (Q847); a far enemy is left alone. `HoldHome` stays
  as a stricter mode.
- Tests: Dwarves digging into a far room are answered; Guards in their own Prison are not; the nearer-the-Heart
  attack first.

### Task D3: the formation drop
**Built 2026-11-15.**
- The attack's front faces from the enemy toward the Heart (`PlayerActionRoutines.FormationPoint`; the enemy's
  own position, not the room it threatens).
- Lift order by role, healthy first (five petals or more; a hurt one is not sent): Blockers, then Flankers and
  Blitzers, then Support (`PlayerActionRoutines.Defender`). NonFighter and workers never; nor one already near
  the enemy or its own drop tile.
- Drop tiles, all own floor the hand accepts, nearest the ideal point (halves round away from zero):
  - Blocker: 1.5 tiles in front of the enemy, between it and the Heart.
  - Support: 2 tiles behind the Blockers, 3.5 in front of the enemy (`SupportTiles` = `FrontTiles` + 2; Q850).
  - Flanker, Blitzer: 1.5 tiles behind the enemy; with no own floor truly behind it, beside the Blockers
    (`FormationDrop`).
- How many: until the bot's healthy fighters near the attack outnumber the attackers (heads, not levels), or none
  left. NonFighters and creatures at two petals do not count (one at three or four petals counts but is not lifted).
- A fighter down to two petals is healed when Heal is offered, ready and affordable, else lifted out to a Hatchery (only when one is
  owned) and dropped there whatever the danger, never back at the front. It does not count toward outnumbering
  the attackers, so a fresh fighter goes in its place; healed to five petals or more it is eligible again, which
  is the rotation (Q849).
- Tests (`CampaignPlayerTests.Defence.cs`): each role's drop tile on a synthetic map; the fallback when behind is
  not own ground; the order; the outnumber stop; the hurt defender (healed when affordable, else the Hatchery). No end-to-end test on the shipped data that a
  Blocker lands in front and a Flanker behind: the Behavior tests cover only D1 (the harness fills every
  creature's style; an own trap blueprint shows on its tile, another keeper's does not).
- The unused `PlayerActionRoutines.Fighter` is removed.

### Task D4: traps on the attack routes
**Built 2026-11-15** (bot policy only, no simulation change; `PlayerTrapRoutines`, `CampaignPlayer.Trap`).
- Entries: hero gates; hero portals; a Portal only when a rival keeper owns it (an unclaimed one is a map resource
  for the bot to claim, owner); enemy-owned ground; every tile where an enemy was ever seen.
- Routes: the shortest 4-connected walkable path (known, not solid, not lava) from each entry to the Heart. One BFS
  from the Heart's ring is walked downhill from each entry; routes that merge are walked once.
- Sites: own plain corridor floor on a route (a corridor tile is open floor, not on the map border, solid on two
  opposite sides, neither marked for digging). Outermost first, 3+ tiles (Chebyshev) from own traps, doors and
  earlier sites (DK2's `004FFC40` tests dx^2 + dy^2 < 9, the same distance). Tiles already refused are skipped, and so
  are sites with an enemy on or beside them. Traps stand on own tiles only (owner).
- Kind of site: Long when 4 straight corridor tiles run on from it (the length DK2's keeper checks, loop
  `004FFE28`..`004FFE81`); Choke when it opens onto walkable non-corridor floor; else Narrow. The ranks, owner (Q851):
  - Long: Boulder, Fear, then the strike traps.
  - Choke: the strike traps (Sentry, Spike, Freeze, Fireburst), then Fear.
  - Narrow: Fear, then the strike traps.
  - Any other trap is a fallback, a Boulder off a long run included ("depends on the situation, don't prevent it").
  Within a rank the dearer wins. Roles are read from the trap's name on the Workshop panel, because the docs disagree
  on the Freeze trap's id (Q852). A trap must be offered, not greyed (`NeedsWorkshop`), affordable, and leave
  `strategy.use-room-min-gold`.
- Pacing: at most `strategy.max-traps` (default 8, 0-64; 0 places none) own traps, counted by `OwnTrap`; one
  placement per 20 ticks; none in a decision that also builds a room. A `NotOffered` or `OutOfBounds` refusal bans the
  tile, and so does a placement not shown as `OwnTrap` 20 ticks later (Level 6a had the world refuse (29,16) as
  `Occupied` 2,581 times before that rule).
- Needs a Workshop; with none the panel greys the traps and the bot waits (the room plan builds one when a playbook
  lists it).
- Left as it is (reviewer): doors are blockers whatever their owner, because the observation gives a door no owner;
  water is walkable for routes; DK2's Boulder long-corridor rule applies only with its flag bit 29 set
  (`BoulderTrapsOnLongCorridors`), whereas the bot prefers the Boulder on long runs.
- Tests (`CampaignPlayerTests.Traps.cs`): entries and routes on synthetic maps; the corridor test, border and dig
  mark; spacing; the three site kinds and their ranks; no Workshop; affordability and the room gold; the cap; the
  refusal filter and the never-shown placement; a room and a trap not in one decision. 16 mutants of the trap rules
  were all killed.

### Task D6: attack spells, never on own creatures or walls
Owner (2026-11-15): "spells should be used against them but also used in a way they dont accidentaly hurt their
own creatures, for example, inferno spell hurts any creature within range, tremor spell damages all walls, not
just enemy's". Today the bot casts only Heal.
- The spells' reach and what they hit (single creature, creatures in a radius, walls in a radius) are definition
  data the host supplies, read from the binary-backed spell records (`keeper-spell-*.md`, `KeeperSpellRules`);
  research first where a spell's area is not yet known.
- A creature spell (Thunderbolt, Chicken, Turncoat and the like) only on an attacker.
- An area spell (Inferno) only at a point where no own creature stands within its reach; the point that catches
  the most attackers.
- Tremor only where every wall it would damage is not the bot's own.
- Spent from the mana kept for defence (`strategy.mana-reserve`), never below one Heal's price.
- Tests: no cast that would touch an own creature or own wall; the best point for an area spell; reserve kept.

### Task D5: measure
- The campaign ladder levels the bot loses (6a, 7, 11a, 15a), seeds 1-3, before and after; Level 1 unchanged.
  D4 alone is measured (Status); the whole defence is measured in the economy plan (D5).

## Status

D1-D4 are built; D6 and D5 moved to [the economy plan](2026-11-16-campaign-bot-economy-and-assault.md). Measured on the built change:
- AI 626/626, Behavior 674 passed and 5 skipped, Simulation 5575/5575. The D3 expectations were mutation-checked
  before D4 (the role order, the Flanker fallback, the outnumber stop, the hurt exclusion and count, the retreat
  drop, attacks against any enemy, Support's two tiles behind the Blockers); 16 mutants of the D4 trap rules were
  killed.
- Levels 1-3 seeds 1-3 all Completed. Final ticks: Level 1 2466/2322/2225; Level 2 4804/4403/4048; Level 3
  10151/7010/6887. With D4 only Level 3 seeds 2 and 3 differ from the build without it (5792 and 5636 before),
  because traps were placed there. Level 2 seed 1 gives 4804 on master as well; its move from 4704 came from
  master's own changes.
- Final hashes: Level 1 seed 1 `0ab563748b5d098b`; Level 2 seed 1 `fbd0271a627c894f`; Level 3 seed 2
  `3e56a79c53b52a8b`, seed 3 `5721be18f23e3591`.
- Level 1 seed 1 moved from tick 2334 (hash `fd4199d3977b784f`) to tick 2466 on purpose: the fighting behaviour
  changed (D3).
- D5 levels, seeds 1-3, master against the D4 traps: 6a Defeated at 7736/3898/7784 against Defeated at
  6427/7628/8501 (5-6 traps placed in each seed); 7 no result at the 57600-tick limit on both (9-16 traps); 11A no
  result on both (16-20 traps); 15a Defeated at 4282/10311/4450 against 4282/10801/4450 (2 traps, in seed 2).
  Traps alone do not turn these losses.
- The mean final ticks over seeds 11-20 (Level 2 4542 against 4851; Level 3 6902 against 7082) were measured at D3,
  before traps, against an earlier master; they are not re-measured.
- `scenarios/campaign-player/baselines.tsv` is a record, not a test. It predates this change and is not
  re-recorded.

## Open questions this plan raises
- Q847 is answered by the owner's direction above, and built (D2).
- Q848: 8 tiles for "near" is right (owner, 2026-11-15); answered, no change. D2 reads it.
- Q849: a defender at two petals is healed if the mana allows, else lifted to the Hatchery (owner, 2026-11-15);
  answered, built.
- Q850: Support goes 2 tiles behind the Blockers, not 5 in front (owner, 2026-11-15); answered, built. Whether
  the straight line to the Heart runs through rock is not asked again: at 3.5 tiles the error is small.
- Q851: which trap is "strongest" for a site is decided by the site's kind (owner, 2026-11-15); answered, built (D4).
  The same sitting: an unclaimed Portal is no entry ("a map resource, probably for it to claim"); traps go on own
  tiles only ("the only way possible"); a Boulder off a long run is allowed ("don't prevent it").
- Q852: which id the shipped Freeze trap has (17 in `Dk2TerrainFixture`, 7 by the table order in `traps.md` section
  12); open. D4 does not depend on it.
