# Mechanic: VARIABLES(230) record layout — the global game-rule table

Source(s):
- ufdada/dk2-tools `variables_struct.bt` (payload layouts + 200 named ids)
- Direct parsing of the reference install's GlobalVariables.kwd and all
  per-level *Variables.kld files

Original version: DK2 v1.70 (GOG)

Evidence:
- Records are 16 bytes: s32 variableId + 12-byte payload.
  621 records × 16 = 9,936 == the VARIABLES chunk contentSize of
  `GlobalVariables.kwd`, exactly. Corpus-wide there are 8,246 VARIABLES records
  (`dk2tools kwd corpus`).
- Payload shapes by id (`VariableTypeId` enum name in brackets):
    1  CreaturePool             s32 creatureId, s32 value, s32 playerId
    2  Availability             u16 kind, u16 playerId, s32 typeId, s32 value
    17 Research                 s32 kind, s32 typeId, s32 cost — one research-list
                                entry, appended in file order; 29 in the global
                                file, none per level (research-tree.md §1)
    43 Sacrifice [SacrificesId] 6 × u8 ingredient (type,id pairs), u8 rewardType,
                                u8 speechId, s32 rewardValue
    65 CreatureStats [CreatureStatsId], 224 CreatureFirstPersonStats
       [CreatureFirstPersonId]  s32 statId, s32 value, s32 level
    otherwise                   s32 value, s32 unk1, s32 unk2
- Values documented by earlier community work now CONFIRMED against real data:
  ClaimTileHealth=825, MineGoldHealth=-300, MaxGoldPerTreasuryTile=3000,
  DeadBodyDiesAfterSeconds=60, PayDayFrequencySeconds=600, ...
  Full table: docs/research/game-database-dump.md
- `GlobalVariables.kwd` also carries a THINGS(190) chunk of 39 records; they are
  all tag 205 (Camera) and they parse (Q15, ADR-0091). What the editor uses them
  *for* is still unrecorded.

Observed behaviour: this file IS the game's balance specification. Per-level
*Variables.kld files override it (same record format).

Confidence: CONFIRMED (layout and values) / UNKNOWN (ids 19, 29, 44, ...
present in data without names — `VariableTypeId` preserves the gaps). Id 17 was on
that list until 2026-09-28: DK2's own Variables dispatcher (`0x0051AA90`) sends it
to the research-list handler.

Implementation: `DK2.Data.Kwd.KwdReader`, `DK2.Data.Kwd.KwdVariables`,
`DK2.Data.Model.VariableTypeId`, `DK2.Data.Model.GlobalVariables`

Tests: `DK2.Data.Tests.KwdReaderTests` (the whole shipped corpus: 12 editor
KWDs, 66 map KWDs, 330 KLDs)

Known exceptions: LEVEL(220) chunks understate contentSize; reader attaches
trailing bytes to the chunk (validated look-ahead).

Open questions: unnamed ids; what the global file's 39 camera THINGS are for;
`CreaturePool` lives in per-level files (global has 0).

---

## Update 2026-09-04 — the per-level files, and what they actually change

`GameDatabaseLoader` read `GlobalVariables.kwd` and nothing else until
2026-09-04, so every campaign run used the global numbers wherever a level
disagreed, although this file had recorded the override since 2026-08-29. Fixed:
ADR-0040. A fact recorded in a doc and read by no code is the same failure as a
rule stated in a doc and asserted by no test (`GOAL_ALIGNMENT.md`).

### How much is overridden

**26 distinct variable ids are overridden by at least one shipped level.** The
whole set, from a sweep of all 66 `*Variables.kld` (`dk2tools kwd varsweep`):

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

Which level overrides what:

| level(s) | override | global |
|---|---|---|
| `Secret1`, `Secret2`, `Secret4`, `Secret5`, `FrontEnd3DLevel` | **`MinimumImpThreshold` = 0** | 4 |
| `Level6b` | `PrisonModifyCreatureHealthPerSecond` = −5 | −3 |
| `level2`, `level8` | `TimeBeforeDungeonHeartConstructionBegins` = 15 / 16 | 0 |
| `level1` | payday 600 → **1200**, first payday 600 → **1200**, `EntranceGenerationSpeedSeconds` 25 → 18, hated-roommate anger 150 → 100, `DungeonHeartReportingDistanceTiles` 4 → 6, gambling anger −2000 → −1000 | — |
| `level1`, `level2`, `level3`, `Level10`, `Secret1` | **`EntranceGenerationSpeedSeconds` 18, 20, 22, 30 and 10000** | 25 |

**A sweep of ids is not a sweep of values.** The list of 26 overridden *ids* was
swept from all 66 files; the table of *which level overrides what* was written
from level 1 alone, so "18 on level 1, the only one of 66" propagated into
`portals.md`, `PortalRules`, two tests and a statement to the owner. Sweeping
that one id finds **five** levels, including `Secret1`'s **10000** — a portal
that produces nothing in any real session, the disabled-not-cut shape of
ADR-0085 — and levels 1–3 at 18/20/22 with 25 from level 4 on, a deliberate
tutorial ramp invisible while only one of the three was known.
`LevelVariablesOnRealDataTests.FiveLevelsOverrideThePortalInterval_NotOnlyLevelOne`
pins all five, and `dk2tools kwd portals` prints each level's own base.

`MinimumImpThreshold` = 0 means a keeper on those levels gets **no free imps at
all**, which is right for fixed-roster puzzle levels — and `ImpSupplyRules`
(ADR-0021) would have handed out four. `Secret3` is the exception and keeps the
global 4; the test pins that too, so "all the secret levels" cannot be assumed.

### The merge rules

Two, because the file holds two kinds of thing. Full reasoning in
`GlobalVariables.MergedWith` and ADR-0040:

1. **Plain values replace by id; ids only the level has are added.** Level 1
   introduces five the global table lacks entirely — `GameTicks`, `LevelRating`,
   `AverageTime`, and ids 66 and 77.
2. **The four lists replace wholesale when the level provides any, and are kept
   when it does not.** STRONGLY INFERRED (Q82, answered and built).

### Availability, decoded — and it decoded itself

The `Availability` payload (`u16 kind, u16 playerId, s32 typeId, s32 value`) was
parsed but unread. Comparing the global table against several levels names both
enum fields, and the naming was derived **from the data before any external
source was consulted**.

**`kind`**, from how many type ids appear under each:

| kind | count of type ids | so it is |
|---|---|---|
| 1 | up to 17, and level 20 has 7..17 | **rooms** |
| 2 | matches that level's `CreaturePool` creature ids exactly | **creatures** |
| 3 | 1..11 — eleven | **traps** (the manual lists exactly eleven) |
| 4 | 1..6 — six | **doors**; `Doors.kwd`'s own order is Wooden, **Steel, Braced**, Magic, Secret, Barricade, then two hero records the player cannot build |
| 5 | 1..11 and 13 | **keeper spells** (KeeperSpells.kwd has thirteen) |

**`value`**: the global table uses **3** for rooms and **2** for
traps/doors/spells; level 1 uses **1** for almost everything and **4** for the
Portal; level 20 uses **3** for nearly all of it.

OpenKeeper names the same fields `ROOM(1), CREATURE(2), TRAP(3), DOOR(4),
SPELL(5)` and `UNAVAILABLE(1), RESEARCHABLE(2), AVAILABLE(3),
UNAVAILABLE_ROOM(4)` — agreeing with all of the above. *(GPLv3: two cited facts,
no code copied.)* `PortalRules` carries the value enum.

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
  `CreaturesSupportedByFirstPortal` / `CreaturesSupportedPerAdditionalPortal`
  for the cap.
- **The rooms panel** — which rooms a keeper may build on a given level — is
  `Availability` kind 1, and is not modelled: `RoomRules` lets a keeper build
  any buildable room on any level.
- **Research** has its target list in kinds 3, 4 and 5 at state `RESEARCHABLE`.

### Still open

Q83 (what `GameTicks` = 340 is — not a rate and not Q1), and what the global
file's 39 camera THINGS records are for. Q85 is answered: `unk1`/`unk2` are
slots 2 and 3 of a generic three-dword payload that the structured ids fill and
plain variables leave at zero (`variables-corpus-sweep.md`). Q84 is answered: id 66 is an
alliance and id 77 a creature field override, several records a level each
(`variables-corpus-sweep.md` §Q84).
