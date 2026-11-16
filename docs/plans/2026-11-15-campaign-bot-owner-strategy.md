# Campaign bot: the owner's strategy from a won Level 6a (draft)

> Draft, not reviewed. Notes of the owner's directions (2026-11-15) and the recording's evidence, to be turned into
> plan tasks after the overnight captures confirm creature types. Nothing here is built yet.

**Principle (owner):** "bot needs to adapt to the map and the situation". Every number below is a playbook setting with
a default, derived from what the bot sees; nothing is tuned to Level 6a. Measure across levels, not one map.

## Evidence

- Recording: `F:/DK2Evidence/studies/l6a-owner-20261115-1821--2026-11-15--001/` (owner won Level 6a; 7243 turns,
  1084 commands, 31 min). `commands.tsv` and `camera.tsv` decoded by `tools/oracle/play-record.ps1`.
- Packet fields: 47 room = (type in start_x, tile in end); 52 trap / 50 door = (tile in start, type in end_x);
  55 cast on creature = (spell in start_x, thing id in end_x); 61 drop: x = (start_x + start_y·65536)/4096,
  y = (end_x + end_y·65536)/4096; the hand is LIFO (57 pick-ups pair with 61 drops last-in first-out).
- Spell ids: 2 Possession, 4 Create Imp, 6 Lightning Strike (owner), 10 Heal, 14 Horny talisman (owner).
- Trap 3 Sentry (15), 8 Fear (2); door 6 Barricade (8), paired with the traps on the same approach.
- Level 6a own rooms at start (level file): Heart (30,19)-(34,23), Prison (30,13)-(34,17), Torture (36,14)-(38,17),
  Library (26,14)-(28,17), Lair (24,19)-(28,23), Hatchery (36,19)-(40,23), Training (30,25)-(34,29), Guard Room
  (26,25)-(43,28), Workshop (36,25)-(38,28), Treasury (22,11)-(24,13). No Portal anywhere.
- Findings: 12 heroes dropped in the Torture Chamber; 21 of 28 Heals on prisoners or the tortured; 271 drops, mostly
  groups of 3-12 on one or two tiles, 42 into the Guard Room; late game (turn 5200+) drops moved south toward the hero
  gates (offence); 57 of the map's 68 gold tiles tagged, mostly before turn 1900; 21 bridges, Treasury grown at ~4690.
- Owner army at the win: game-given 3 Goblins, 4 Mistresses, ~4 Warlocks, Trolls; converted 2 Knights, 6 Guards
  (maybe 1 Thief); many Skeletons from prisoners who starved.

## Rules to plan (owner directions)

- **R10, recruit from heroes when there is no Portal:** prisoners sorted by type. Melee heroes the army lacks (Guard,
  Knight) go to the Torture Chamber and are healed when low so they convert (needs the captive's health in the
  observation; the owner confirms a player sees it). Others (Dwarf, Wizard, Thief) stay in the Prison and starve into
  Skeletons (only starving counts, prison.md). Converted heroes work apart from keeper creatures (hated company:
  mixed Library/Training/Guard Room/Workshop and prison cells anger, creature-anger.md) and rotate between Training
  Room and Guard Room before they turn unhappy (mood is on the flower). Needs each type's alignment as host data.
- **Training:** new recruits train first, up to the Training Room cap (`TrainingRoomMaxExperienceLevel`, 4 on 6a;
  pass it like `CombatPitMaxLevel`), then the Combat Pit if the map has one, else guard duty.
- **R11, gold:** mine gold first (GoldFirst exists); keep gold for the next payday and training (needs payday timer
  and wage total in the observation); grow the Treasury while short of that; Create Gold only in an emergency
  (payday near, gold short), never below the defence mana.
- **R12, rooms keep ahead of the army:** Lair by free beds (not tiles), expand or build a second Lair where there is
  space; Hatchery by visible chickens and hunger complaints (check whether the unhappy notice's reason reaches the
  player), expand or build another. Stop converting when melee (Blocker, Flanker, Blitzer, Skeletons) is at least
  `strategy.melee-per-support` (default 2) times the Support creatures, or when no bed is left.
- **Traps:** a Barricade beside each Sentry or Fear trap, on the side attackers come from (owner pattern).
- **D6 spells:** Lightning Strike on attackers only (owner cast it twice in one fight); Heal as above; the Horny
  talisman kept for the strongest enemy (a boss such as the Lord of the Land) and used before a level ends unused.
- **Hand:** drop fighters in groups on one point; offence toward the hero gates once the army is strong.
- **R13, special boxes (owner):** almost every map hides boxes "in nooks and crannies"; finding them pays, and is one
  more reason the owner digs through the map. Today the bot opens any box at once (`PlayerActionRoutines.Special`) and
  never searches. Wanted: dig into unexplored pockets near own ground once the gold is under way; hold each box until
  it pays. Owner's example: Increase Level (117) waits until most fighters are at least level 4 (the Training Room cap),
  a free level past what training gives. Proposed timings to confirm with the owner (ids in specials.md): Increase Gold
  (121) when payday is near and gold is short; Mana Boost (122) when mana is low in a fight; Heal All (128) in a big
  fight with many hurt; Make Happy (125) when several are unhappy; Receive Imps (124) when Imps are few; Reveal Map
  (118) and Locate Hidden Land (129) early, to find gold and more boxes; Make Safe (119) when a rival digs in.
  Owner: leave Destroy Walls (120), Stun Imps (123) and Make Unhappy (126) alone; Kill Creatures (127, targeted: one
  creature not the keeper's loses all its health, specials.md) on a high-value enemy (the highest-level one in sight, a
  boss). The owner also named Transfer Creature (140) for that, but it acts only on an own creature (removes it and
  carries its type and level to the next level, specials.md). Owner: when found, use it on the most valuable own
  creature, ranked by creature quality, then level. Owner's order, lowest first: Firefly, Goblin, Troll, Dark Elf,
  Salamander, Bile Demon, Skeleton, Warlock, Mistress, Rogue, Vampire, Black Knight, Dark Angel. Imp and Horny are
  never transferred, nor converted heroes (owner: only keeper creatures).

## R14, hero fortifications and the final push (owner, 2026-11-15, after Level 4 losses)

Evidence: Level 4 seed 1 on master 56d5a08d (scratchpad run, not kept): no result in 57600 ticks; four Hero Sentry
traps (trap type 14, player 1) fired 2866 times from tick ~4200 to the end and killed most of the bot's 172 Imps
and 121 Dark Elves; the bot's hand also dropped creatures at (15,5) inside one trap's range from tick 26500. It ended
with 4 fighters, 4 Imps and 92k unspent gold. Seed 2 won at 19643, before the attrition took hold (from ~24000).
Seeds 1-3: 1 won, 2 no result (was 2 of 3 before defence D1-D4). The bot never sees another keeper's traps.

Owner's play on Level 4:
- Avoid the Dwarf lookout: walking into it raises the heroes' alarm.
- Dig left along the map toward the Lord of the Land's territory.
- When the Sentry traps start firing: drop several offensive creatures near (never Imps), possess one, and attack
  the traps together as a party until they are destroyed.
- Then Imps claim all territory up to the enemy walls; then Imps dig through, and every creature is dropped to
  rush the Lord.

As bot rules (to plan; adapt to the map): see a fired enemy trap (check in the binary that firing reveals it to the
player); never drop or mark digging inside a known enemy trap's range unless attacking it; destroy revealed traps
with a group of fighters, not Imps, dropped together; avoid alarm-raising lookouts (owner: on Level 4 a Dwarf
standing still next to two Sentry traps in the middle of the map; as a rule, a hero that stays on one tile across many
observations, beside enemy traps, is a lookout: keep digging, claiming and drops out of its post; owner: it can be sniped by possessing a Dark Elf
and shooting it from range. The simulation has possession built (`LeavePossessionCommand`, the right-click exit;
`MovePossessedCreatureCommand`; `ActPossessedCreatureCommand`, ranged or melee with an aim); only the bot's
interface lacks it (no PlayerAction to leave, move or act, so the observation does not offer the spell). Adding
those actions plus a possessed view, what the creature sees and can aim at, lets the bot snipe and lead a party); finale: claim up to the enemy's walls, breach, drop the whole army on the boss. Possession: built in the simulation, missing only
from the bot's interface (see the lookout above); until added, the party goes without it. Spend idle gold on rooms and training.

## Open

- The overnight captures (`play-record.ps1 -Capture -Study <folder>`) desynced a Level 6a replay at turn 76, cause
  unsettled (`tools/oracle/play-record.ps1`, `play-record.md`), so creature types per thing id are still not read from
  them. `play-record.ps1` is committed on master.
