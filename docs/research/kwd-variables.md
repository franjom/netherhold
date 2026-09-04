# Mechanic: VARIABLES(230) record layout — the global game-rule table

Source(s):
- ufdada/dk2-tools `variables_struct.bt` (payload layouts + 200 named ids)
- Direct parsing of the reference install's GlobalVariables.kwd and all
  per-level *Variables.kld files

Original version: DK2 v1.70 (GOG)

Evidence:
- Records are 16 bytes: s32 variableId + 12-byte payload.
  621 records x 16 == chunk contentSize of GlobalVariables.kwd, exactly.
- Payload shapes by id:
    1  CreaturePool             s32 creatureId, s32 value, s32 playerId
    2  Availability             u16 kind, u16 playerId, s32 typeId, s32 value
    43 Sacrifice                6 x u8 ingredient (type,id pairs), u8 rewardType,
                                u8 speechId, s32 rewardValue
    65 CreatureStats, 224 CreatureFirstPersonStats
                                s32 statId, s32 value, s32 level
    otherwise                   s32 value, s32 unk1, s32 unk2
- Values documented by earlier community work now CONFIRMED against real data:
  ClaimTileHealth=825, MineGoldHealth=-300, MaxGoldPerTreasuryTile=3000,
  DeadBodyDiesAfterSeconds=60, PayDayFrequencySeconds=600, ...
  Full table: docs/research/game-database-dump.md
- GlobalVariables.kwd also carries a THINGS(190) chunk (39 items) — the editor's
  default things? Unexplored (open question).

Observed behaviour: this file IS the game's balance specification. Per-level
*Variables.kld files override it (same record format).

Confidence: CONFIRMED (layout and values) / UNKNOWN (ids 17, 19, 29, 44, ...
present in data without names; semantics of unk1/unk2 payload fields)

Implementation: `DK2.Data.Kwd.KwdReader`, `DK2.Data.Kwd.KwdVariables`,
`DK2.Data.Model.VariableTypeId`

Tests: `DK2.Data.Tests.KwdReaderTests` (incl. full editor + 71 maps + 330 KLD corpus)

Known exceptions: LEVEL(220) chunks understate contentSize; reader attaches
trailing bytes to the chunk (validated look-ahead).

Open questions: unnamed ids; meaning of the THINGS chunk inside
GlobalVariables.kwd; CreaturePool lives in per-level files (global has 0).

---

## Update 2026-09-04 — the per-level files, and what they actually change

**This file already said it.** The line *"Per-level `*Variables.kld` files
override it (same record format)"* was written on 2026-08-29. `GameDatabaseLoader`
read `GlobalVariables.kwd` and nothing else until 2026-09-04, so every campaign
run used the global numbers wherever a level disagreed. **A fact recorded in a doc
and read by no code is the same failure as a rule stated in a doc and asserted by
no test** — the third instance of that pattern in a week (see
`GOAL_ALIGNMENT.md`). Fixed: ADR-0040.

### How much is overridden

**26 distinct variable ids are overridden by at least one shipped level.** The
whole set, from a sweep of all 66 `*Variables.kld`:

`EntranceGenerationSpeedSeconds`, `SpecialIncreaseGoldAmount`,
`PayDayFrequencySeconds`, `TimeBeforeFirstPayDaySeconds`,
`PrisonModifyCreatureHealthPerSecond`, id 66, `GameTicks`, id 77,
`ModifyAngerInCompanyOfHatedCreaturesPerSecond`, `ForceAppliedToSlappedCreature`,
**`MinimumImpThreshold`**, `BoulderInitialHealth`, `BoulderSpeedTilesPerSecond`,
`BoulderToCreatureDamagePercentage`, `DungeonHeartReportingDistanceTiles`,
`TriggerTrapTriggerSpeedTilesPerSecond`, `FirstPersonAccuracyAngleDegrees`,
`GamblingModifyCreatureAngerSmilesPerSecond`,
`FirstPersonRechargeModifyPercentage`, the three `MusicLevel*ThreatThreshold`s,
`TimeBeforeDungeonHeartConstructionBegins`, `BoulderSlapDamage`, `LevelRating`,
`AverageTime`.

**One of them was already live**, which is what made this urgent rather than
tidy:

| level(s) | override | global |
|---|---|---|
| `Secret1`, `Secret2`, `Secret4`, `Secret5`, `FrontEnd3DLevel` | **`MinimumImpThreshold` = 0** | 4 |
| `Level6b` | `PrisonModifyCreatureHealthPerSecond` = −5 | −3 |
| `level2`, `level8` | `TimeBeforeDungeonHeartConstructionBegins` = 15 / 16 | 0 |
| `level1` | payday 600 → **1200**, first payday 600 → **1200**, `EntranceGenerationSpeedSeconds` 25 → 18, hated-roommate anger 150 → 100, `DungeonHeartReportingDistanceTiles` 4 → 6, gambling anger −2000 → −1000 | — |

`MinimumImpThreshold` = 0 means a keeper on those levels gets **no free imps at
all**, which is exactly right for fixed-roster puzzle levels — and
`ImpSupplyRules` (ADR-0021) would have handed out four. `Secret3` is the
exception and keeps the global 4; the test pins that too, so "all the secret
levels" cannot be quietly assumed.

### The merge rules

Two, because the file holds two kinds of thing. Full reasoning in
`GlobalVariables.MergedWith` and ADR-0040; in short:

1. **Plain values replace by id; ids only the level has are added.** Level 1
   introduces five the global table lacks entirely — `GameTicks`, `LevelRating`,
   `AverageTime`, and ids 66 and 77.
2. **The four lists replace wholesale when the level provides any, and are kept
   when it does not.** STRONGLY INFERRED (Q82).

### Availability, decoded — and it decoded itself

The `Availability` payload (`u16 kind, u16 playerId, s32 typeId, s32 value`) was
parsed but unread and unexplained. Comparing the global table against several
levels names both enum fields, **and the naming was derived from the data before
any external source was consulted**, which is why it is worth recording as
evidence rather than as a lookup.

**`kind`**, from how many type ids appear under each:

| kind | count of type ids | so it is |
|---|---|---|
| 1 | up to 17, and level 20 has 7..17 | **rooms** |
| 2 | matches that level's `CreaturePool` creature ids exactly | **creatures** |
| 3 | 1..11 — eleven | **traps** (the manual lists exactly eleven) |
| 4 | 1..6 — six | **doors** (Wooden, Braced, Steel, Magic, Secret, Barricade) |
| 5 | 1..11 and 13 | **keeper spells** (thirteen exist) |

**`value`**: the global table uses **3** for rooms and **2** for
traps/doors/spells; level 1 uses **1** for almost everything and **4** for the
Portal; level 20 uses **3** for nearly all of it.

OpenKeeper names the same fields `ROOM(1), CREATURE(2), TRAP(3), DOOR(4),
SPELL(5)` and `UNAVAILABLE(1), RESEARCHABLE(2), AVAILABLE(3),
UNAVAILABLE_ROOM(4)` — agreeing with all of the above. *(GPLv3: two cited facts,
no code copied.)*

Read together they explain the shape of a DK2 campaign:

- **The global list is a player-0 template**: every room available, every trap,
  door and spell *researchable*. That is the default a skirmish or an editor
  session would start from.
- **A level's list is that level's own**, keyed to its own player ids. Level 1
  starts its keeper with Treasury and Library **UNAVAILABLE**, the Portal
  **UNAVAILABLE_ROOM**, every trap, door and spell **UNAVAILABLE**, and exactly
  one creature — the **Goblin** — AVAILABLE, matching its one-entry creature pool
  of fifteen Goblins. The tutorial then unlocks things as the Mentor introduces
  them, which is what the `MAKE` and `FLASH_BUTTON` trigger actions are for.
- **By level 20 the same list is eleven rooms, six creatures, eleven traps, six
  doors and twelve spells, nearly all AVAILABLE.**
- **`Secret1` carries no availability entries and no creature pool at all**,
  alongside its `MinimumImpThreshold` = 0. A level where you build nothing, get
  no imps, and play the creatures you are given.

`RESEARCHABLE(2)` is also the state the **library** moves things out of — which
ties this table straight to the manual's finding that research is stored in the
room and lost with it (`manual-1999.md`).

### What this unblocks

- **The portal** needs exactly this: `CreaturePool` for who may come,
  `Availability` kind 2 for whether they are unlocked yet,
  `EntranceGenerationSpeedSeconds` for how often, and
  `CreaturesSupportedByFirstPortal` / `...PerAdditionalPortal` for the cap.
- **The rooms panel** — which rooms a keeper may build on a given level — is
  `Availability` kind 1, and is currently not modelled at all: `RoomRules` lets a
  keeper build any buildable room on any level.
- **Research** has its target list in kinds 3, 4 and 5 at state `RESEARCHABLE`.

### Still open

Q83 (what `GameTicks` = 340 is), Q84 (ids 66 and 77), Q85 (the two trailing
fields on every record, which turn out to *group* the metadata ids apart from the
balance ids), and the THINGS(190) chunk inside `GlobalVariables.kwd` that nobody
has explained.
