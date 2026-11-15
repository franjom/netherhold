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
- **Trap choice:** the strongest offered trap the bot can afford, a Boulder Trap on long straight corridors,
  keeping a gold reserve for rooms.
- **Which attacks:** "heart defense is priority, but bot shouldn't let attackers destroy the rooms of dungeon
  just because the rule says near heart... it should defend its entire dungeon, like if dwarfs break into
  dungeon it should react to that attack vector." An enemy on or beside the bot's own ground is an attack; the
  one nearest the Heart is answered first. Enemies far from the dungeon (Level 15a's Guards in their own Prison)
  are watched, not attacked. This answers Q847.
- **Formation (tight):** Blockers 1-2 tiles in front of the enemy, between it and the Heart; Support (and
  fragile creatures) 3-4 tiles behind the Blockers; Flankers and Blitzers 1-2 tiles behind the enemy.

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
- A **corridor tile** is open floor with solid neighbours on two opposite sides (DK2's own test, `004FFA50`).

## Tasks

### Task D1: what the bot needs to see
**Built 2026-11-15.**
- `CampaignPlayerOptions.FightStyles` and `StyleOf`: creature type to fighting style, supplied by the harness from
  `CreatureDef.FightStyle` (public data, like `WorkerTypes`), heroes' types included. A type not listed is a
  NonFighter; with no data at all every type is a Blocker, so a host without it still defends.
- `PlayerTileFlags.OwnTrap` (observation schema 6): a tile holding the player's own trap or trap blueprint, so
  sites are not reused. Another keeper's traps stay hidden. Nothing reads it yet; D4 does.
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
  - Support: 5 tiles in front of the enemy, so 3-4 behind the Blockers (Q850).
  - Flanker, Blitzer: 1.5 tiles behind the enemy; with no own floor truly behind it, beside the Blockers
    (`FormationDrop`).
- How many: until the bot's healthy fighters near the attack outnumber the attackers (heads, not levels), or none
  left. NonFighters and hurt creatures do not count.
- A fighter down to two petals is healed when Heal is offered, else lifted out to a Hatchery (only when one is
  owned) and dropped there whatever the danger, never back at the front (Q849).
- Tests (`CampaignPlayerTests.Defence.cs`): each role's drop tile on a synthetic map; the fallback when behind is
  not own ground; the order; the outnumber stop; the hurt defender. No end-to-end test on the shipped data that a
  Blocker lands in front and a Flanker behind: the Behavior tests cover only D1 (the harness fills every
  creature's style; an own trap blueprint shows on its tile, another keeper's does not).
- The unused `PlayerActionRoutines.Fighter` is removed.

### Task D4: traps on the attack routes
- Entry points: hero gates and portals seen, enemy-owned ground at the dungeon's edge, past sightings.
- Routes: the shortest walkable path from each entry to the Heart over known tiles.
- Sites: corridor tiles on those paths, 3+ tiles apart and from any own trap or door, nearest the entry first.
- Choice: the strongest offered, affordable trap; a Boulder Trap where the corridor runs straight 5+ tiles;
  gold kept for rooms (`strategy.use-room-min-gold`), at most `strategy.max-traps` placed (playbook tunable).
- Needs a Workshop; with none, it waits (the room plan builds one when a playbook lists it).
- Tests: sites on a synthetic corridor map; spacing; no site without a Workshop; the Boulder rule; affordability.

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

## Status

D1-D3 are built; D4, D6 and D5 follow in that order. Measured on the built change:
- AI 610/610 (the role order, the Flanker fallback, the outnumber stop, the hurt exclusion and count, the retreat
  drop and attacks against any enemy are mutation-checked), Behavior 674 passed and 5 skipped, Simulation 5575/5575.
- Levels 1-3 seeds 1-3 all Completed. Final ticks, defence against master: Level 1 2466/2322/2225 against
  2334/2364/2291; Level 2 4704/4403/4048 against 5384/5236/3956; Level 3 10151/5792/5636 against 6966/5709/8593.
- Seeds 11-20, mean final tick, defence against master: Level 2 4542 against 4851 (faster in 5 of 10); Level 3
  6902 against 7082 (faster in 6 of 10).
- Level 1 seed 1 moved from tick 2334 (hash `fd4199d3977b784f`) to tick 2466 (hash `0ab563748b5d098b`) on purpose:
  the fighting behaviour changed.
- `scenarios/campaign-player/baselines.tsv` is a record, not a test. It predates this change and is not
  re-recorded.

## Open questions this plan raises
- Q847 is answered by the owner's direction above, and built (D2).
- Q848: whether 8 tiles is right for "near"; open. D2 reads it.
- Q849: a defender at two petals in the middle of an attack on the Heart: pulled back to a Hatchery, as now, or
  only healed? Open.
- Q850: Support 5 tiles "toward home" when that straight line runs through rock and is not the attackers' route;
  open.
- Which trap types count as "strongest": by cost, or by a fixed order per type? Default: by cost.
