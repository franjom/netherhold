# Campaign bot: economy, capacity and the assault on hero fortifications

> For the owner's review (2026-11-16). T0, E1-E3, A1 and A2 are built (Status); A3, A4, D6, R10 and R13 are not. Source of every rule: the owner's directions in
> [the strategy notes](2026-11-15-campaign-bot-owner-strategy.md) (R10-R14) and the Level 6a recording
> (`F:/DK2Evidence/studies/l6a-owner-20261115-1821--2026-11-15--001`). Follows
> [the defence plan](2026-11-15-campaign-bot-defence.md) (D1-D4 built; D6 and D5 move here).

**Goal:** the bot wins the campaign levels it loses today (Level 4 1 of 3; 6a, 15a lost; 7, 11a no result at the
57600-tick limit, seeds 1-3) by keeping a working economy and an army that fits its rooms, and by breaking hero
fortifications instead of bleeding into them.

**Principle (owner):** "bot needs to adapt to the map and the situation". Every threshold is a playbook setting with a
default, decided from what the bot sees. Nothing is tuned to one map; every task is measured on the whole ladder.

**Rule for the observation (unchanged):** the bot sees only what a human player can see on screen or in the panels.
Each new observation field below names the place a human sees it; a field whose human source is not established is a
research question first, not a field.

## What existed before T0-A2 (code survey, 2026-11-16)

- Decide order (`CampaignPlayer.cs:123-154`): Stalled, Untag, Hand, Room, Trap, CreateImp, then one of Work/Pit/Scout,
  Special, Dig; at most `MaxActionsPerDecision` (4) actions.
- Gold: `PlayerObservation.Gold`; Treasury fullness only derived (`Outgrown`, `strategy.treasury-full-percent`).
  **Payday is not observed**, though the HUD's `KeeperResourceSnapshot` has `WageBill`, `NextPaydayTick` and
  `PaydayCutoffTicks` (`KeeperResourceView.cs:16-17`). Gold tiles are `PlayerTileFlags.GoldSeam`; digging sorts gold
  first (`DigMode.GoldFirst`) but has no gold budget.
- Rooms: `Room` (`:338`) builds `Strategy.Rooms`, grows `Outgrown` rooms, then `ExtraRooms`. The Lair grows by tiles per
  non-worker, the Hatchery by tiles per non-worker; **no bed count, no chicken count, no hunger signal**.
- Notices: `PlayerObservation.Notices` carries kinds (CreatureUnhappy 17, CreatureAngry 18, RoomFull 42, RoomNeeded 41,
  TrainingUnpaid 55, CreatureConverted 58, ...) without their data; **the bot never reads them**.
- Spells: only Heal; `strategy.mana-reserve` is checked only by CreateImp. Offers carry no range or area.
- Possession: built in the simulation (`PossessionCommands.cs`: Leave, Move, Act) and for the human
  (`PossessionControls`, `PossessionSession`); **the bot has no possession action** and the builder hides spell 2.
- Enemy traps: **never observed** (`WorldView.Player.cs`: another keeper's traps stay hidden).
- Prisoners: no captive flag (`IsThreat` infers it from Prison/Torture tiles); no Prison or Torture routine.
- Specials: activated at once when on own ground (`PlayerActionRoutines.Special`); boxes elsewhere are not listed.
- Training: `Work` exists but `UseRooms` is empty by default (training lost Level 3 seeds 12-13); no Guard Post routine.

## Tasks, in order

Each task: tests first (AI tests on synthetic observations), then the ladder runs it touches, reviewer, documenter,
commit named files, merge. A task that changes the observation bumps `PlayerObservation.SchemaVersion` and updates
`CoverageLimits`.

### T0: telemetry of the bot's thinking (owner, 2026-11-16: "you need metrics, telemetry and diagnostics of bot's
thinking and decision making")
- The bot's own account, per decision (`PlayerDecision.Thoughts`, `Beliefs`): every routine's verdict (acted or not)
  with a note on what it did or why it did nothing, and the numbers it decided on. Diagnostics only; nothing reads them
  back.
- The host's ground truth (`PlayerRunDiagnostics`, privileged, never shown to the bot): every 100 ticks the dungeon as
  it really is (gold, fighters, Imps, hungry, unhappy, beds, chickens, wage bill, enemy creatures and traps, losses,
  kills); every own creature lost with what was near it (the nearest enemy trap and enemy); every enemy trap's firings
  (each forward step of its recharge) and whether it was destroyed.
- Files beside each run: `player-thinking.tsv` (a routine's verdict when it changes, every action), `player-beliefs.tsv`
  (a value when it changes), `player-metrics.tsv`, `player-losses.tsv`, `player-enemy-traps.tsv`; the headline numbers
  go into `player-run.json` under `diagnostics`. About 1.5 MB for a 57600-tick run.
- First reading (Level 4 seeds 1-3, before any other task): seed 1 lost 353 creatures, 217 of them within 6 tiles of
  a hero Sentry trap (2866 firings); the hand answered an "attack" at (24,15) 2617 times on seed 3, the Dwarf lookout
  standing between the Sentries at (22,15) and (26,15); 92k gold idle in 563 of 576 samples; no extra room grew.
- Found while reading it: a 57600-tick run writes an 80 MB `events.log` (the scenario report, older than this plan),
  past the 64 MiB output budget, so long runs end as incomplete evidence. Not changed here.

### E1: see payday, beds and food (observation)
- Payday: `PlayerObservation.Payday(NextPaydayTick, WageBill)` from the same source as the HUD's payday clock and wage
  readout (`KeeperResourceSnapshot`); a human sees both in the panel.
- Room fill: `PlayerRoomSummary` gains `Capacity` and `Used` for the Lair (owner, Q870: a human hovering over the Lair
  reads its capacity in the top status bar) and the Treasury (the top bar's yellow gold figure, beside the green mana;
  capacity from the room's tiles). The per-tile capacity comes from the same rules the status bar uses.
- Food: the bot counts own Hatchery chickens from `Things` (already visible on own ground). Hunger: owner, Q871: the
  creature's status flower shows a chicken icon when it is hungry, so `PlayerCreatureSummary` gains `Hungry` from the
  same state the flower draws (hidden when the flower is not shown, as `Mood` is).
- The bot starts reading `Notices` (kind and tick) for CreatureUnhappy, CreatureAngry and RoomFull.
- Tests: the builder fills each field from a world fixture; a hidden thing stays hidden.

### E2: a gold budget (R11)
- `GoldNeed` = next payday's wage bill + the training it runs, plus `strategy.gold-margin-percent` (default 20).
- While gold is below `GoldNeed`: gold seams first in digging, with no distance cap toward a known reachable seam
  (`DigMode.Toward` on the nearest seam beside own ground); no new optional room; no training drops.
- Grow the Treasury when gold plus the gold in sight would not fit (today it grows only when 80 % full).
- Create Gold (looked up by name in the spell offers): only when payday is within `strategy.payday-alarm-ticks` and gold
  is short of the wage bill, and never below the defence mana reserve. The owner saves it for emergencies.
- Spend idle gold: when gold is above `GoldNeed` and the Treasury is near full, build or grow the next useful room
  (Level 4 ended with 92k unspent).
- Tests: the budget in each case; Create Gold only in the emergency; the reserve kept. Ladder: Levels 1-4, 6a.

### E3: rooms keep ahead of the army (R12)
- Lair: grow when free beds fall below `strategy.free-beds` (default 2); when the Lair cannot grow (rock, other rooms),
  build a second one where `DigSite` finds space.
- Hatchery: grow when visible chickens fall below `strategy.chickens-per-creature` x non-workers (default 0.5), or on
  repeated CreatureUnhappy notices while chickens are short (and hunger, once Q871 is answered); a second Hatchery as for
  the Lair.
- Army size: the bot stops adding creatures it controls (conversion in R10, Imps beyond the floor) when no bed is free
  or the mix is enough: melee at least `strategy.melee-per-support` (2) times the Support creatures.
- Tests: grow, second room, stop. Ladder: Levels 3, 4, 6a, 7.

### A1: see hero fortifications (R14)
- Owner, Q872: Sentry, Fear, Boulder and the like are visible; a hidden trap shows once it fires. The binary agrees
  (`traps.md` section 11): a trap's hidden bit (`+0x10C` bit 4) is set from its definition's flag `0x20`, cleared by
  firing with flag `0x2` and by a creature with `RevealsAdjacentTraps` (Troll, Thief, Rogue, Guard) beside it; ADR-0338
  built the reveal. So the observation shows every enemy trap whose hidden bit is clear, with its type, on any tile
  the player sees: `PlayerVisibleTrap(Tile, Type, Owner, ReachTiles)`. The reach is how far its target finder looks
  (traps.md sections 5 and 6): 0 for a trap that fires at its own tile, else the definition's range, plus two for a
  trap that turns toward its target (flag 0x80: Sentry, Hero Sentry, Magic Door). A tile counts as covered when its
  centre lies within the reach plus half a tile (the finder measures real positions); line of sight is not modelled.
- Owner, Q873: own creatures attack an enemy trap by themselves when they can (a Skeleton, which fears nothing,
  attacks a Fear trap). ADR-0340 built the attack on a revealed trap (observed in the original). So the bot's job is to
  put the right creatures beside it, not to order the attack.

### A2: keep out of fire and break the trap (R14)
- Never drop a creature, nor tag digging, inside a known enemy trap's reach (definition data from the host), except to
  attack it.
- Lookout: a hero that stays on one tile across `strategy.lookout-observations` observations beside an enemy trap is a
  lookout; digging, claiming and drops stay out of its sight (its reach from the creature definition).
- Attack a revealed trap with a party: `strategy.assault-party` (default 4) fighters, never Imps, dropped together beside
  it from the safe side; retreat the hurt as D3 does. Choose creatures the trap cannot stop (owner: a Skeleton against a
  Fear trap); which creature resists which trap is host data from the trap and creature definitions.
- Tests: no drop or tag in reach; the party drop; the lookout found and avoided. Ladder: Level 4 seeds 1-3 first.

### A3: possession for the bot (R14)
- `PlayerAction.Possess(handle)` (spell 2 on an own creature), `Possessed.Move(pose)`, `Possessed.Act(aim, melee)`,
  `Possessed.Leave()` (the right-click exit), adapted to the simulation's existing commands; the builder offers spell 2
  only through this path. A possessed view: the creature's position, heading, health, and the creatures and traps in its
  sight, as a human sees through its eyes.
- Uses: snipe a lookout from range with a ranged creature (owner: a Dark Elf), and lead the trap assault. Leave as soon
  as the target is gone or the creature is hurt.
- Tests: enter, move, fire, leave; the bot never stays possessed past `strategy.possession-max-ticks`.

### A4: the final push (R14)
- Once the fortifications in sight are broken: Imps claim up to the enemy walls, dig through, then the whole army is
  dropped together on the boss. The Horny talisman (spell 14) on the boss (owner).

### D6: attack spells, never on own creatures or walls (moved from the defence plan, unchanged)
- Creature spells (Lightning Strike, spell 6, owner) on attackers only; area spells only where no own creature is in
  reach; Tremor only on walls that are not the bot's; the mana reserve kept. Needs each spell's reach and targets as
  host data (`keeper-spell-*.md`).

### R10: recruit from heroes without a Portal; R13: special boxes
Planned after E1-A4 are measured, as written in the strategy notes: melee heroes the army lacks tortured and healed
until they convert, others left to become Skeletons, converted heroes kept apart and rotated between the Guard Post and
the Training Room, training to the Training Room's cap; boxes searched in nooks and held until they pay (Increase Level
when most fighters are level 4 or more; Transfer Creature on the most valuable keeper creature by the owner's ranking;
Kill Creatures on a high-value enemy; Destroy Walls, Stun Imps, Make Unhappy left alone). Both need a captive flag and
boxes beyond own ground in the observation (questions to raise then).

### D5: measure
Levels 1-3 seeds 1-3 must stay won; Levels 4, 6a, 7, 11a, 15a seeds 1-3 before and after each task; the acceptance
seeds 11-20 on the levels that turn.

## Status (2026-11-16)

Built: T0, E1 (payday, wage bill, Lair beds, Treasury fill, hunger; schema 7), A1 (enemy traps in view, schema 7), A2
(drops, answers and, while other ground is left, dig marks kept out of enemy traps' reach; the assault party: the whole
party lifted in one decision or none, never while an enemy is near home, never a fighter in a fight or one whose
flower does not show five petals), E2 (the gold budget kept after a purchase; idle gold trains below the Training
Room's cap), E3 (Lair by beds, Hatchery by hunger, a second room when one cannot grow). Each new rule has a playbook
switch: `strategy.assault-party` (0-4), `strategy.avoid-traps`, `strategy.gold-budget`, `strategy.gold-margin-percent`,
`strategy.free-beds` (0: the old tiles rule), `strategy.hungry-percent` (100: never), `strategy.second-rooms`,
`strategy.train-gold`. The Training Room's level cap applies whenever the Training Room is used.

Measured (final ticks; Levels 1-3 from `scenarios/campaign-player`, Level 4 `scenarios/campaign-ladder/level4.yaml`
seeds 1-3, 57600-tick cap):
- After the review's fixes (final build): Levels 1-3 9 of 9 won (Level 1 2584/2436/2349, Level 2 4466/4676/4325,
  Level 3 5624/7607/9156); Level 4 3 of 3 won and accepted with `--player-output-mib 512` (32476/21540/48646).
- Before the review's fixes: Levels 1-3 9 of 9 won. Level 3 6900/5813/8079 (before the plan 10151/7010/6887; with the assault, avoidance, bed,
  hunger and training switches off, before the gold budget and second rooms had switches, 10254/7899/6615: not the old
  bot). Switching off the assault alone, or the bed and hunger rules alone, loses Level 3 seed 3.
- Level 4: 3 of 3 won at 25380/31099/55844 (before: 1 of 3, seed 2 at 19746); every seed destroyed all four hero
  Sentry traps; losses within 6 tiles of an enemy trap fell from 217 to 6 on seed 1. Seeds 2 and 3 are reported as
  incomplete evidence only because the run's `events.log` exceeds the 64 MiB output budget (T0's finding); ladder runs
  need `--player-output-mib` until that is changed.
- What went wrong on the way, from the telemetry: avoiding every tile in a trap's reach cut Levels 3 and 4 off from
  their gold and their route (Level 3 0 of 3); an assault dropped 6 tiles from its trap never arrived and was re-sent
  every 8 ticks; Lair and Hatchery growth spent the payday's gold and creatures left (54 on Level 4 seed 1). The rules
  above are the fixes.
- Next, from the same telemetry: Level 4 seed 3 lost 188 Dark Elves after the traps fell, fed one at a time into the
  last fight (A4's whole-army push); then Levels 6a, 7, 11a and 15a.

## Questions for the owner (from the review, 2026-11-16)
- Does a Sentry reach exactly its range plus two in play, and does a Fear or Boulder trap fire at the shorter range?
- Does an eating but unhappy creature show the chicken? By the binary its centre shows the mood and then the largest
  grievance, never the job.
- Should the Hatchery grow when a quarter of the army is eating at the same time, even if nobody starves?
- Should a party ever take defenders out of a fight to attack a trap? (Built: never.)

## Questions this plan raised (all answered by the owner, 2026-11-16)
- Q870: Lair capacity shows in the top status bar while hovering over the Lair; gold is the top bar's yellow figure.
- Q871: hunger shows as a chicken icon in the creature's status flower.
- Q872: visible traps (Sentry, Fear, Boulder, ...) are always seen; hidden traps once they fire (binary agrees,
  `traps.md` section 11).
- Q873: own creatures attack an enemy trap by themselves when they can (binary-backed, ADR-0340).

## For the owner
1. Is this order right: economy and capacity (E1-E3), then Level 4's fortifications (A1-A4), then spells (D6), then
   conversion and boxes (R10, R13)?
2. Any default above that is wrong from your play (2 free beds, half a chicken per creature, a party of 4, 20 % gold
   margin)?
