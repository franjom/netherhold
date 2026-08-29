# Dungeon Keeper 2 — Modern C#/Unity Reimplementation Plan

**Document status:** Phase 0 research deliverable, revision 1
**Date:** 2026-08-29
**Target original version:** Dungeon Keeper 2 **v1.70** (with the 3 bonus packs)
**Author:** Technical lead / research architect (AI-assisted)

---

## Reading this document

Every factual claim about DK2 is tagged with a confidence level. This convention is used
throughout the document and should be carried into all future project documentation.

| Tag | Meaning |
|---|---|
| **[KNOWN]** | Confirmed by binary analysis, a parsed data file, or published RE artifacts. Directly verifiable today. |
| **[INFERRED]** | Not directly confirmed, but supported by multiple independent observations. |
| **[HYPOTHESIS]** | Plausible, untested. Must not be implemented as fact without an experiment. |
| **[UNKNOWN]** | We genuinely do not know. Requires an experiment or new RE work. |

Anything not tagged is a statement about *our* project, not about DK2.

---

## 1. Executive summary

### 1.1 Verdict

**Yes — this project is technically realistic for a solo developer with heavy AI assistance,
but only under a specific strategy.** The naive strategy ("reverse engineer DK2 from
scratch") is not realistic. The viable strategy is:

> **Treat DK2's own data files as the primary specification, treat Flame's v1.70 symbol
> map as the structural ground truth, treat OpenKeeper as a corpus of solved problems, and
> reserve original reverse-engineering effort exclusively for the ~6 systems that nobody
> has solved yet.**

The single most important discovery of this research phase is this:

> **A very large fraction of what people call "DK2's game rules" is not code at all — it is
> data, sitting in the `.kwd` files, in a format that is already ~90% reverse engineered
> and has a working open-source parser.** [KNOWN]

Creature health, melee damage, pay, hunger rates, training costs, room capacities, tile
health values, research times, spell costs, torture conversion times, possession mana
drain — these are `VARIABLES` and `CREATURES` records in the level data, not hardcoded
constants in `DKII.EXE`. We do not need to reverse engineer them. We need to *parse* them.

Concretely, values like these are already recovered and named: [KNOWN]

```
ENTRANCE_GENERATION_SPEED_SECONDS      = 25
CLAIM_TILE_HEALTH                      = 825
ATTACK_TILE_HEALTH                     = -140
REPAIR_TILE_HEALTH                     = 125
MINE_GOLD_HEALTH                       = -300
MAX_GOLD_PER_TREASURY_TILE             = 3000
DEAD_BODY_DIES_AFTER_SECONDS           = 60
DECOMPOSE_VALUE_NEEDED_FOR_VAMPIRE     = 1000
CREATURE_DYING_STATE_DURATION_SECONDS  = 60
TIME_BETWEEN_REATTEMPTING_HUNGER_SECONDS = 4
TIME_BETWEEN_REATTEMPTING_SLEEP_SECONDS  = 4
```

This reframes the entire project. The work is **not** "discover the numbers." The work is
**"discover the procedures that consume the numbers."**

### 1.2 What is actually hard

Five systems carry essentially all the risk:

1. **Pathfinding and movement** — the least-understood system in the entire ecosystem. No
   project has reproduced DK2's actual pathfinder. This is risk #1.
2. **Creature job/destination selection** — the priority ordering is [UNKNOWN].
3. **Creature mood / anger / happiness** — genuinely unsolved. OpenKeeper's mood system is
   a stub with an empty `processTick()`, and "Research and implement creature moods" is a
   live open issue on that project. [KNOWN]
4. **Combat resolution** — stats are known, the resolution procedure is not.
5. **Computer-player (AI Keeper) behaviour** — partially reconstructed in Flame, otherwise
   opaque.

Everything else is, in engineering terms, *transcription work* — large, tedious, and highly
suitable for AI-assisted implementation, but not research-risky.

### 1.3 Effort estimate

| Phase | Scope | Realistic elapsed time (1 dev + AI, part-time) |
|---|---|---|
| 0 | Research, this document, repo skeleton | 1–2 weeks |
| 1 | Foundation: solution, determinism, test harness, KWD parser | 4–8 weeks |
| 2 | Dungeon prototype: grid, dig, claim, imp, first room | 8–12 weeks |
| 3 | First creature loop: lair/hatchery/training, job selection | 8–12 weeks |
| 4 | Core systems breadth | 6–12 months |
| 5 | Load a real campaign level | overlaps phase 4 |
| 6 | Behavioural fidelity harness + regression corpus | continuous, forever |
| 7 | Full content | 12–24 months |
| 8 | Modern presentation | after the above |

**A playable, faithful-feeling first level is a 12–18 month goal, not a 3 month goal.**
That is the honest number. It is achievable because the data-driven insight above removes
most of the guesswork.

### 1.4 The one thing that must be decided before writing code

**Licensing.** See §10.1. Flame publishes **no license at all** (`"license": null` on the
GitHub API) and OpenKeeper is **GPLv3**. [KNOWN] Neither can have its *code* copied into a
permissively-licensed C# project. Both can be used as *research references*. This
distinction determines the entire working method and must be settled first.

---

## 2. Existing ecosystem

### 2.1 Summary table

| Project | Language | Stars | Last activity | Status | Value to us |
|---|---|---:|---|---|---|
| [OpenKeeper](https://github.com/tonihele/OpenKeeper) | Java / jMonkeyEngine | 494 | 2026-08-23 | Active | **Highest** — file formats, game logic corpus |
| [Flame](https://github.com/DiaLight/Flame) | C++ | 119 | 2026-02-28 | Active | **Highest** — v1.70 binary ground truth |
| [Ember](https://github.com/DiaLight/Ember) | C++ | — | Superseded | Closed | Historical; superseded by Flame |
| [GLKeeper](https://github.com/codenamecpp/GLKeeper) | C++ | 40 | 2026-08-13 | Active, "not playable yet" | Low–medium; second opinion on formats |
| [dk2-tools](https://github.com/ufdada/dk2-tools) | 010 Editor templates | 13 | — | Reference | **High** — independent format specs |
| [DK2MapMaker](https://github.com/Nateonus/DK2MapMaker) | C# | 2 | — | Reference | Medium — C# KWD handling precedent |
| [DK2-Map-Editor](https://github.com/NovFR/DK2-Map-Editor) | C | 3 | — | Reference | Low (French only) |
| [dk2botfw](https://github.com/DerelictDrone/dk2botfw) | JavaScript | 1 | — | Reference | Low; multiplayer protocol notes |

### 2.2 Flame — the binary ground truth

> https://github.com/DiaLight/Flame — *"Dungeon Keeper II partial recompilation with bug fixes"*

**This is the most valuable single resource for this project, and it is not close.**

Flame's approach: DK2 is loaded as `dk2.dll` (rebuilt with a relocation table derived from
a full cross-reference map), individual functions are reimplemented in C++, and the loader
swaps the original function pointers for the new ones at runtime. This means Flame's
authors have had to understand real DK2 structures precisely enough that recompiled code
runs against live game memory.

**Measured coverage of `mapping/DKII_EXE_v170.sgmap`:** [KNOWN]

| Metric | Count |
|---|---:|
| Total lines | 86,706 |
| Named structs | 868 |
| Named globals | 10,548 |
| Named struct fields | 7,282 |

Plus `DKII_EXE_v170.refmap` (116,194 lines — code/data cross-references) and
`DKII_EXE_v170.espmap` (182,788 lines — stack-pointer modification sites, used to walk
DK2's frame-pointer-less stack). 145 reconstructed `.cpp` files live under `src/dk2/`.

The gameplay-relevant named structures include: [KNOWN]

```
CThing → CPhysicalThing → CMovingThing → CCreature      (entity hierarchy)
CRoom, CDoor, CTrap, CShot, CObject_* (24 variants)
CWorld, CMap, CMapEntry, CWorldEntry
CPlayer, CCreaturePool, CResearchOrder
MyComputerPlayer                                        (the AI keeper)
Follow_Path                                             (pathfinding — see below)
GameAction, GameActionArray                             (command/lockstep layer)
```

**Sample of what "understood" means here.** `CCreature` is a 1343-byte structure with
semantically named fields: [KNOWN]

```
f24E_cstate                       (creature state sub-object)
f36F_typeId
f370_creatureData                 (ptr to the KWD-derived stat block)
f37A_isInBattleFlag
f37E_canLeaveBattleAt
f38D_inst                         (Creature_Instance)
f3AF_workModifier
f3B3_workEnhancement
f3B7_lairId
f3B9_roomId
f3BB_movementMode
f3BF_reloadSpeed
f3C3_carryingId / f3C5_draggingId / f3C7_beingDragged
f3CB_prisonOwner
f3D5_reportingTo
f3D7_fearlessUntil
f3DF_myLeadersId / f3E1_positionInParty
f3E2_getPaid
f3E6_stateFlags / f3EA_stateFlags2
f3EE_nextPossessionInfluenceTick
```

And `CRoom` (169 bytes) exposes the capacity model directly: [KNOWN]

```
f67_totalCapacity, f69_totalFighterCapacity,
f6B_usedCapacity, f6D_usedCreatureCapacity, f6F_usedObjectCapacity,
f35_health, f2B_navigationPos, f2D_firstSlab, f2F_lastSlab,
f45_sData_DiscoModeStartedOnGameTick
```

Flame also ships `src/dk2/entities/creature_state.h`, a complete decode of both creature
state bitfields — 32 + 22 named flags. [KNOWN] Selected entries:

```
StateFlags1: InHand, ContributesToPortalLimit, AttackAbnormalTargets, RebelActive,
             RebelLeader, Possessed, BeingElectrocuted, VampireIsBat, Creeping, Praying,
             Leaving, CombatPitFighter, IgnoringDoors, CallToArmsOn, CreatureDying,
             FightProcessCounter (4-bit field, 0x03C00000), SniperMode, ...
StateFlags2: IsHypnotised, CantBeResurrected, HasBeenConverted, SkipNextPayment,
             IsMercenary, HasBeenKnockedOut, TortureVoluntary, IsHasted, IsSlowed,
             Fleeing, InWater, ...
```

Also present: `room_type.h` (26 room type IDs), `entities_type.h` (8 thing types, 44
creature type IDs with an evil/good flag, 22 object type IDs).

**Three high-signal observations from Flame's structure:**

1. `f45_sData_DiscoModeStartedOnGameTick` and the `limit_tps` / `scheduler` patches confirm
   DK2 runs its simulation on a **fixed integer game tick**, not on variable delta time.
   [KNOWN] This is enormously good news for determinism and for our tick-based design.
2. The presence of `GameAction` / `GameActionArray` alongside `CNetworkCommunication`
   strongly implies DK2's multiplayer is **command-based lockstep**, which in turn implies
   **the original simulation is deterministic by design**. [INFERRED] Our determinism goal
   therefore matches the original architecture rather than fighting it.
3. `Follow_Path` is named — but has **zero named fields**. [KNOWN] Pathfinding is the
   conspicuous hole in the best RE work that exists. See §9, risk R1.

### 2.3 OpenKeeper — the solved-problems corpus

> https://github.com/tonihele/OpenKeeper — *"Dungeon Keeper II remake"*, GPLv3, Java + jMonkeyEngine 3.9 / Java 25

Same stated goal as ours (v1.7 + 3 bonus packs, minimal changes, original assets), 12+
years of accumulated work, and still actively developed.

**Where OpenKeeper is strongest — file formats.** A complete converter suite under
`tools/convert/`: `wad`, `kmf`, `map` (KWD/KLD), `kcs`, `str`, `sound`, `spr`, `bf4`,
`textures`, `hiscores`. `KwdFile.java` alone is 3,427 lines. Their own published
assessment: [KNOWN]

| Format | RE % | Note |
|---|--:|---|
| WAD archives | 100 | extraction + decompression |
| HiScores.dat | 100 | |
| BF4 fonts | 100 | incl. Japanese variant |
| EngineTextures | 100 | extract + decompress |
| .444 loading screens | 100 | decompression |
| .TGQ videos | 100 | synchronised sound |
| KCS camera sweeps | 95 | FOV & near-plane iffy |
| KMF models & animations | 95 | some params not understood |
| STD sound archives | 95 | probably just archives |
| STR game strings | 95 | odd indexing on some params |
| **KWD & KLD map/data** | **90** | huge; some unknown attributes |
| SPR sprites | 80 | palette missing; probably unused |
| BANK & SFX events | 40 | format known, **usage not known** |

**The KWD record model** — 18 top-level record types, which is effectively DK2's schema: [KNOWN]

```
GLOBALS(0)  MAP(100)  TERRAIN(110)  ROOMS(120)  TRAPS(130)  DOORS(140)
KEEPER_SPELLS(150)  CREATURE_SPELLS(160)  CREATURES(170)  PLAYERS(180)
THINGS(190)  TRIGGERS(210)  LEVEL(220)  VARIABLES(230)  OBJECTS(240)
EFFECT_ELEMENTS(250)  SHOTS(260)  EFFECTS(270)
```

**The level scripting language is fully enumerated** — 51 `TriggerAction` types: [KNOWN]

```
CREATE_CREATURE(1) CREATE_PORTAL_GEM(2) SEND_TO_AP(3) DISPLAY_OBJECTIVE(4)
INFORMATION(5) MAKE(6) FLAG(7) INITIALIZE_TIMER(8) FLASH_BUTTON(9) WIN_GAME(10)
LOSE_GAME(11) WIN_SUBOBJECTIVE(12) LOSE_SUBOBJECTIVE(13) CREATE_HERO_PARTY(14)
SET_OBJECTIVE(15) FLASH_ACTION_POINT(16) REVEAL_ACTION_POINT(17) SET_ALLIANCE(18)
ATTACH_PORTAL_GEM(20) FORCE_FIRST_PERSON(21) ALTER_TERRAIN_TYPE(22) SET_TIME_LIMIT(23)
PLAY_SPEECH(24) DISPLAY_TEXT_STRING(25) ZOOM_TO_ACTION_POINT(26)
ROTATE_AROUND_ACTION_POINT(27) GENERATE_CREATURE(28) MAKE_HUNGRY(29)
SHOW_HEALTH_FLOWER(30) FOLLOW_CAMERA_PATH(31) COLLAPSE_HERO_GATE(32) ALTER_SPEED(33)
SET_MUSIC_LEVEL(34) REMOVE_FROM_MAP(35) SET_FIGHT_FLAG(36) SET_PORTAL_STATUS(37)
SET_WIDESCREEN_MODE(38) MAKE_OBJECTIVE(42) ZOOM_TO(43) SET_CREATURE_MOODS(44)
SET_SYSTEM_MESSAGES(45) DISPLAY_SLAB_OWNER(46) DISPLAY_NEXT_ROOM_TYPE(47)
TOGGLE_EFFECT_GENERATOR(48) CHANGE_ROOM_OWNER(49) SET_SLAPS_LIMIT(50) SET_TIMER_SPEECH(51)
```

**OpenKeeper's architecture** is an ECS (Simsilica Zay-ES) with a clean split between
`game/controller/*` (authority), `game/logic/*` (systems), `game/task/*` (worker tasks),
`game/navigation/*`, and `game/trigger/*`. Notably it already separates simulation from
jMonkeyEngine reasonably well — which is direct evidence that the simulation/presentation
split we want is achievable for this game.

**Where OpenKeeper is explicitly weak** — from their own open issue tracker (98 open
issues): [KNOWN]

- `Research and implement creature moods` — and `CreatureMoodSystem.processTick()` is
  literally empty in the current source
- `Improve AI and makes states more transparent`, `Fix issues in AI`
- `Match DKII room and bridge placement rules`, `Add better room building rules`
- `Better task management`, `Tasks refactor`
- `level 10 imp "runs into" walls`, `Optimize steering behaviour`, `Implement movement prediction`
- `Campaign progress`, `MPD Level Progress`

**Read this list carefully — it is a map of the frontier.** Those are precisely the systems
where a 12-year-old project with a working game still does not match DK2. We should expect
to spend our research budget there and nowhere else.

### 2.4 Ember — historical

> https://github.com/DiaLight/Ember — *"Dungeon Keeper II patching launcher"*

Its own README states: *"The project was closed due to development complexity and is no
longer maintained. Development of Ember patches continues in the Flame project."* [KNOWN]

Still worth reading once for methodology. Ember established the techniques Flame inherited:
`mappings/v170.map` (function/data symbols), `mappings/references.map` (chunk
cross-references — used to synthesise a relocation table so DK2 can be a DLL), and
`mappings/stack.map` (ESP modification sites, because `DKII.EXE` does not use EBP as a
frame pointer, so stack walking requires a precomputed map). [KNOWN]

**Recommendation:** read Ember's README for context, then use Flame exclusively. Do not
spend time diffing the two mapping sets.

### 2.5 Secondary resources

- **[ufdada/dk2-tools](https://github.com/ufdada/dk2-tools)** — 010 Editor binary templates
  for ~30 formats, including a per-record breakdown of KWD (`creatures_struct.bt`,
  `rooms_struct.bt`, `terrains_struct.bt`, `triggers_struct.bt`, `variables_struct.bt`,
  `things_struct.bt`, …) **and `Formats/Save/save_struct.bt`** — the save-game format. This
  is an *independent* derivation of the formats and is therefore extremely useful as a
  cross-check against OpenKeeper. Where the two disagree, we have found a real question.
- **[DK2 Official Editor Manual](http://keeper.lubiki.pl/dk2_docs/dk2_editor_manual.htm)** —
  live (HTTP 200), alongside `dk2_mapper_manual.htm`. This is *EA's own documentation of
  how the game is supposed to behave*, written for level designers. It is the single best
  natural-language source of intended semantics and should be read cover to cover early.
- **[DK2 walls write-up](http://simonschreibt.de/gat/dungeon-keeper-2-walls/)** — live;
  explains DK2's wall/terrain rendering trick.
- **keeperklan.com** — the historical forum (incl. thread 4623, *Reversal of DKII Binary
  File Formats*). Returns HTTP 403 to automated clients; reachable in a normal browser, and
  the `lubiki.keeperklan.com` mirror serves the docs.
- **[GLKeeper](https://github.com/codenamecpp/GLKeeper)** — C++ reimplementation, active but
  self-described as "not playable yet." Useful only as a third opinion on asset loading.
- **[DK2MapMaker](https://github.com/Nateonus/DK2MapMaker)** — C#. Small, but the only C#
  precedent for reading/writing DK2 map data.

---

## 3. Reverse-engineering status

Confidence reflects *our ability to reproduce the behaviour*, not merely to parse a file.

| System | Known | Partial | Unknown | Primary source |
|---|:--:|:--:|:--:|---|
| **File formats (WAD/KMF/STR/textures/video)** | ● | | | OpenKeeper converters; dk2-tools templates |
| **Map & level data (KWD/KLD)** | ● | | | OpenKeeper `KwdFile` (~90%); dk2-tools `.bt` |
| **Terrain data model** | ● | | | KWD `TERRAIN(110)` |
| **Terrain runtime behaviour** | | ● | | Flame `CMapEntry` (11 bytes, **fields unnamed**) |
| **Digging / claiming** | | ● | | Tile-health constants [KNOWN]; imp task assignment [PARTIAL]; multi-imp stacking [UNKNOWN] |
| **Rooms — definitions, types, capacity** | ● | | | Flame `CRoom` + `room_type.h`; KWD `ROOMS(120)` |
| **Rooms — placement/merge/bridge rules** | | ● | | OpenKeeper open issues admit mismatch |
| **Economy — parameters** | ● | | | KWD `VARIABLES(230)` MiscVariables |
| **Economy — payday/wage procedure** | | ● | | `f3E2_getPaid`, `SkipNextPayment` flag [KNOWN]; timing [UNKNOWN] |
| **Creature stats & data** | ● | | | KWD `CREATURES(170)`, `CreatureStats` StatTypes |
| **Creature state representation** | ● | | | Flame `creature_state.h` — 54 named flags |
| **Creature job/destination selection** | | ● | | OpenKeeper FSM exists; **DK2's actual priority order [UNKNOWN]** |
| **Creature moods / anger / happiness** | | | ● | OpenKeeper system is a stub; their #1 open issue |
| **Combat — stats** | ● | | | KWD; `MELEE_DAMAGE`, `MELEE_RECHARGE_TIME_SECONDS` |
| **Combat — resolution & target selection** | | ● | | `f37A_isInBattleFlag`, `f37E_canLeaveBattleAt`, `FightProcessCounter` [KNOWN]; formulas [UNKNOWN] |
| **Pathfinding** | | | ● | Flame `Follow_Path` named, **no fields**; OpenKeeper uses its own A*+steering |
| **Possession** | | ● | | `Possessed` flags, `f3EE_nextPossessionInfluenceTick`, `POSSESSION_MANA_COST_PER_SECOND` [KNOWN]; control model [PARTIAL] |
| **Spells (keeper & creature)** | | ● | | KWD `KEEPER_SPELLS(150)`, `CREATURE_SPELLS(160)`, `SHOTS(260)` |
| **Traps & doors** | | ● | | KWD `TRAPS(130)`, `DOORS(140)` |
| **Heroes & parties** | | ● | | `CREATE_HERO_PARTY` trigger; `f3DF_myLeadersId`, `f3E1_positionInParty` |
| **Level scripting / triggers** | ● | | | 51 `TriggerAction` types fully enumerated |
| **Campaign progression** | | ● | | OpenKeeper open issue "Campaign progress" |
| **Computer player (AI keeper)** | | ● | | Flame `MyComputerPlayer`, `computer_player_flags`, `drop_condition` |
| **Multiplayer** | | ● | | `GameAction`, Flame `protocol_dump`, `network/protocol.h` |
| **Save system** | | ● | | dk2-tools `save_struct.bt`; Flame `sData_*` field prefixes |
| **Rendering** | ● | | | KMF 95%, EngineTextures 100% |
| **Audio — formats** | ● | | | SDT/BANK/SFX parsed |
| **Audio — event semantics** | | | ● | OpenKeeper: BANK/SFX usage 40% |

**Summary:** *data* is largely solved; *procedure* is largely not. The six red cells —
pathfinding, moods, combat resolution, job selection, audio event semantics, and the
computer player — are where original research is required.

---

## 4. Proposed C#/Unity architecture

### 4.1 Guiding constraints

1. `DK2.Simulation` must have **zero** reference to `UnityEngine`. Enforced by CI.
2. The simulation is a **fixed-tick, deterministic state machine**, matching the original's
   design. [INFERRED, §2.2]
3. All randomness flows through an explicit, seeded, serialisable RNG.
4. All balance values come from parsed DK2 data. **A magic number in simulation code is a
   bug.** Enforced by review and by an analyser where practical.
5. Rendering observes the simulation; it never mutates it.

### 4.2 Repository layout

```
/DungeonKeeperRemake
├── DK2.sln
├── DK2_REIMPLEMENTATION_PLAN.md          ← this file
├── src/
│   ├── DK2.Core/                 primitives: fixed-point, TilePos, RNG, event bus, IDs
│   ├── DK2.Data/                 format parsers: WAD, KWD/KLD, KMF, STR, KCS, SDT, BF4
│   ├── DK2.Data.Model/           parsed, immutable game database (POCOs)
│   ├── DK2.Simulation/           the game. no Unity, no rendering, no I/O.
│   │   ├── World/                grid, tiles, terrain, ownership, rooms
│   │   ├── Entities/             creatures, objects, doors, traps, shots
│   │   ├── Rooms/                room instances + per-type behaviour
│   │   ├── Economy/              gold, mana, payday, research
│   │   ├── Combat/
│   │   ├── Jobs/                 task generation, claiming, assignment
│   │   ├── Scripting/            triggers, objectives, campaign
│   │   └── Snapshot/             save/load, state hashing
│   ├── DK2.AI/                   creature decision-making, computer player
│   ├── DK2.Pathfinding/          isolated on purpose — highest-churn subsystem
│   ├── DK2.Assets/               IAssetProvider abstraction + DK2-backed implementation
│   ├── DK2.Harness/              headless runner, scenario DSL, event-log recorder
│   └── DK2.Tools/                CLI: extract, inspect, diff, convert, replay
├── unity/DK2.Unity/              Unity project — presentation only
├── tests/
│   ├── DK2.Data.Tests/
│   ├── DK2.Simulation.Tests/
│   ├── DK2.AI.Tests/
│   ├── DK2.Pathfinding.Tests/
│   └── DK2.Behavior.Tests/       ← the regression corpus (grows forever)
├── docs/
│   ├── research/                 one file per mechanic, using the §7 template
│   ├── reverse-engineering/      notes keyed to Flame symbol names & addresses
│   ├── experiments/              experiment definitions + recorded oracle results
│   └── architecture/             ADRs
├── scenarios/                    declarative test-scenario files (see §6.2)
└── original-reference/           .gitignored — local DK2 install, never committed
```

### 4.3 Simulation core

```csharp
namespace DK2.Simulation;

/// The entire game. Deterministic, headless, serialisable.
public sealed class GameSimulation
{
    public GameTick   CurrentTick { get; private set; }
    public WorldState World       { get; }
    public SimRandom  Rng         { get; }

    /// Advances exactly one game tick. The ONLY way time moves.
    public void Tick(in InputFrame input);

    /// Structural hash of all simulation state. Two runs that agree on this
    /// at every tick are behaviourally identical.
    public ulong StateHash();
}

/// DK2 runs on a fixed integer tick. We mirror that rather than using float dt.
public readonly record struct GameTick(long Value);
```

**On tick rate:** DK2's exact simulation tick rate is [UNKNOWN] but must be determined
early — Flame's `limit_tps` patch implies it is both fixed and discoverable. Until measured,
`TicksPerSecond` is a single named constant in one place, flagged `[UNVERIFIED]`.

**On fixed-point arithmetic:** the original is a 1999 x86 game using a mix of integers and
x87 floats (Flame ships an `fpu_control.h`). Reproducing float behaviour bit-exactly across
modern hardware is not tractable. **Decision: use integer/fixed-point for all
gameplay-affecting quantities**; reserve `float` for presentation only. This trades exact
float reproduction (unachievable) for cross-platform determinism (achievable and more
valuable). Recorded as ADR-0001.

### 4.4 Data layer

The parsed DK2 database is loaded once and is immutable:

```csharp
public sealed class GameDatabase
{
    IReadOnlyDictionary<CreatureTypeId, CreatureDef> Creatures { get; }
    IReadOnlyDictionary<RoomTypeId,     RoomDef>     Rooms     { get; }
    IReadOnlyDictionary<TerrainTypeId,  TerrainDef>  Terrain   { get; }
    IReadOnlyDictionary<MiscType,       int>         MiscVars  { get; }  // §1.1 values
    /* traps, doors, objects, shots, effects, keeper spells, creature spells */
}
```

Simulation code reads `MiscVars[MiscType.MineGoldHealth]`, never `-300`.

### 4.5 AI architecture

**Do not pick an AI paradigm yet.** DK2's real structure is [UNKNOWN], and committing to
behaviour trees or utility AI before knowing would be exactly the mistake §33 of the brief
warns about.

What the evidence *does* support: DK2 stores creature state as **two 32-bit flag words plus
a `cstate` sub-object** [KNOWN], and Flame names a `FightProcessCounter` occupying 4 bits.
That is the signature of a **hand-written state machine with flag-based modifiers**, not of
a planner or a utility system. [INFERRED]

Therefore the interim design is a **flag-augmented FSM with an explicit, ordered decision
list**, where the ordering is a *declared, testable hypothesis*:

```csharp
public interface ICreatureDecision
{
    /// Ordered probes. First match wins — mirroring an if/else chain,
    /// which is what the original most likely is. [HYPOTHESIS]
    DecisionResult? Evaluate(CreatureContext ctx);
}
```

Every decision returns **why** it fired, so the harness can diff *reasons*, not just
outcomes:

```csharp
public readonly record struct DecisionResult(
    JobKind Job, TilePos Destination, string ReasonTag);
```

This is the single most important design decision for fidelity work: **an implementation
that reaches the right destination for the wrong reason is not correct, and we must be able
to see that.**

### 4.6 Pathfinding — deliberately isolated

`DK2.Pathfinding` sits behind a narrow interface because we expect to rewrite it several
times:

```csharp
public interface IPathfinder
{
    PathResult FindPath(in PathQuery query);   // query carries owner, movement mode,
                                               // door policy, territory policy
}
```

Ship `AStarPathfinder` as a **known-wrong placeholder**, explicitly labelled as such, so
that no downstream code accidentally assumes it is faithful. Fidelity work happens later,
against recorded oracle traces.

### 4.7 Presentation boundary

```
GameSimulation ──emits──> SimulationEvent stream ──> Unity presentation layer
       ▲                                                      │
       └──────────────── InputFrame (commands only) ──────────┘
```

Unity's job: render, animate, play audio, take input, draw UI. It holds **no** authoritative
state. A `GameObject` is a view of an entity id; deleting every `GameObject` must not change
the simulation. This also means the harness can run the game at 10,000× speed with no
renderer at all.

### 4.8 Asset abstraction

```
CreatureDef ──> AssetRef("creature.imp.mesh") ──> IAssetProvider ──> DK2 KMF (dev)
                                                                 └─> fan-made (later)
```

No filename literals outside `DK2.Assets`. This is what keeps a redistributable build
possible (§10.2).

---

## 5. Reverse-engineering workflow

**Never start from raw disassembly.** Flame has already done the expensive part.

```
1. Name the question             "When does a creature decide to go eat?"
        ↓
2. Search Flame's sgmap          grep the 868 structs / 10,548 globals for
                                 hunger / eat / hatchery / chicken
        ↓
3. Follow refmap cross-refs      116,194 entries — find who reads that global
        ↓
4. Read Flame's reconstructed    145 .cpp files; if the function is already
   C++ if it exists              reimplemented, the logic is described
        ↓
5. Check OpenKeeper's take       does an independent implementation agree?
        ↓
6. Check the KWD data            is the value simply sitting in VARIABLES?
        ↓
7. Only now: IDA/Ghidra          load DKII.exe v1.70, apply Flame's IDA scripts
                                 (mapping/ida/*.py), navigate to the named function
        ↓
8. Validate on the running game  Flame's logging/inspect_tools, or a debugger
        ↓
9. Write docs/research/<x>.md    using the §7 template, with a confidence tag
        ↓
10. Implement + regression test
```

**Steps 1–6 are free and cover most questions. Step 7 is the expensive one and should be
rare.** Flame ships `mapping/ida/espmap_ida.py`, `sgmap_ida.py` and the `refmap_ida`
package to import its symbol data directly into IDA — use them rather than hand-annotating.

**Conflict protocol.** When OpenKeeper and Flame disagree, or either disagrees with the
editor manual, this is a *finding*, not a nuisance. Record it in
`docs/reverse-engineering/conflicts.md` with both positions, design an experiment against
the original game, and record the verdict. **Never silently pick one.**

---

## 6. Experimental methodology

### 6.1 The oracle

The installed original DK2 v1.70 is the reference implementation. Where possible, run it
under Flame, which provides logging and inspection hooks and a TPS limiter — making
observation and frame-stepping far easier than with the stock executable.

### 6.2 Scenario definition — one file, two runners

A scenario is declarative and drives both the original (via a generated custom map + script)
and our simulation:

```yaml
# scenarios/creature/job_selection_hunger_vs_training_001.yaml
id: job-selection-001
question: >
  Given a creature that is both hungry and untrained, and both rooms are
  available and reachable, which does it choose first?
map: flat_16x16_two_rooms
seed: 12345
setup:
  player: { id: 1, gold: 100, mana: 1000 }
  rooms:
    - { type: Lair,         at: [4,4],  size: [3,3] }
    - { type: Hatchery,     at: [8,4],  size: [3,3] }
    - { type: TrainingRoom, at: [4,8],  size: [3,3] }
  creatures:
    - { type: Goblin, at: [6,6], health: 100%, hunger: 90%, experience: 0 }
observe:
  - first_destination
  - ticks_until_decision
  - decision_reason          # ours only; the oracle gives us behaviour, not reasons
  - state_transitions
  - gold_delta
duration_ticks: 600
```

### 6.3 Capture protocol

| | Original DK2 | Our simulation |
|---|---|---|
| Setup | generated custom level + trigger script | `ScenarioLoader` |
| Stepping | Flame TPS limit; frame-step | `sim.Tick()` |
| Observation | screen capture + memory inspection at known `CCreature` offsets | direct state read |
| Output | `docs/experiments/<id>/oracle.json` | `<id>/ours.json` |

Reading the oracle's state directly from memory is feasible *because Flame gives us the
field offsets* — e.g. `f3B9_roomId` at `+0x3B9`, `f3E6_stateFlags` at `+0x3E6`. This
converts "watch the screen and guess" into "read the actual variable." **This is the
highest-leverage tooling investment in the project.**

### 6.4 Comparison and regression

```
oracle.json ──┐
              ├──> BehaviorComparer ──> pass / fail / DIVERGENCE REPORT
ours.json ────┘
```

Every scenario becomes a test in `DK2.Behavior.Tests`. Divergences are triaged as:
*our bug* → fix; *unknown mechanic* → new research doc; *acceptable* → record with an
explicit written justification. The corpus only ever grows.

### 6.5 Determinism gate

Independent of the oracle, CI asserts: same seed + same inputs ⇒ identical `StateHash()` at
every tick, across OS and CPU. If this ever fails, all fidelity results are meaningless
until it is fixed. This runs on every commit from day one.

---

## 7. Asset pipeline

### 7.1 Development flow

```
original-reference/DK2/          (local only, .gitignored, never committed)
        │
        ├── *.WAD ──> DK2.Data.Wad ──────> raw members
        ├── *.KWD/KLD ──> DK2.Data.Kwd ──> GameDatabase  ← the specification
        ├── Meshes.WAD/*.KMF ──> converter ──> glTF/mesh + animation clips
        ├── EngineTextures.dat ──> converter ──> PNG/DDS
        ├── *.SDT / Sounds ──> converter ──> WAV/OGG
        └── *.STR ──> converter ──> localisation tables
                              │
                              ▼
              build/converted/   (also .gitignored)
                              │
                              ▼
                    IAssetProvider  ←── Unity loads only through this
```

The converter is a **build step the user runs against their own legally-owned copy**,
exactly as OpenKeeper does. No converted asset is ever committed or distributed.

### 7.2 Keeping replacement possible

`AssetRef` is a logical key (`"creature.imp.mesh"`), resolved at runtime by a provider
chain: `FanMadeProvider → ConvertedDk2Provider → PlaceholderProvider`. Adding a
free-asset pack later is then a data change, not a code change. `PlaceholderProvider` must
always be complete enough that the game *runs* (as untextured boxes) with no DK2 install —
this keeps the "shippable without proprietary assets" property honest and continuously
tested, rather than aspirational.

### 7.3 Data vs. assets — an important distinction

The KWD *balance data* is not an art asset; it is the game's rule specification, and we
cannot ship it either. Mitigation: define our own open `GameDatabase` schema (JSON) plus a
converter from KWD. Development uses the converted original; a future standalone release
would ship a hand-authored database. Design for this now — it costs almost nothing now and
is very expensive to retrofit.

---

## 8. Milestones

| # | Milestone | Definition of done | Est. |
|---|---|---|---|
| **M0** | Research baseline | This document; repo skeleton; DK2 v1.70 installed; Flame + OpenKeeper building locally | 1–2 wk |
| **M1** ◐ | Formats readable | `DK2.Tools extract` dumps WAD contents; KWD parses to `GameDatabase`; all 18 record types round-trip; unit tests over the shipped campaign maps | 3–5 wk |
| **M2** ✅ | Deterministic skeleton | `GameSimulation.Tick()` exists; `StateHash()` stable; save/load round-trips; CI determinism gate green; headless harness runs a scenario file | 3–4 wk |
| **M3** ◐ | Dungeon prototype | Grid + terrain from a real KWD; dig, claim, reinforce; one imp doing tasks; treasury; gold. Unity view renders it with placeholder art | 8–12 wk |
| **M4** | First creature loop | Portal spawn; lair claim; hunger→hatchery; sleep; training; payday; job selection with logged reasons | 8–12 wk |
| **M5** | First oracle comparison | Memory-reading oracle probe working against DK2 under Flame; 20+ scenarios captured from both sides; first divergence report | 4–6 wk |
| **M6** | Real level loads | A stock campaign level loads, terrain and things place correctly, triggers/objectives fire, level is winnable | 6–10 wk |
| **M7** | Combat & heroes | Combat resolution, hero parties, hero gates, traps, doors | 3–5 mo |
| **M8** | Breadth | All rooms, all creatures, all keeper spells, research, possession, prison/torture/graveyard/temple | 6–12 mo |
| **M9** | Fidelity | 500+ behavioural regression scenarios green; documented divergence list; pathfinding rewritten against oracle traces | continuous |
| **M10** | Campaign | Full campaign playable start to finish | after M8 |
| **M11** | Modern presentation | PBR materials, modern lighting, HD textures, modern UI, ultrawide | after M10 |

**Status (2026-08-29).** M2 is done: the loop, the state hash, save/load and the
scenario harness all exist and are covered by passing tests (198 now), run with
and without a DK2 install. The CI workflow is written and its steps verified
locally on Windows; it has not yet been exercised on GitHub-hosted Linux, which
is the run that turns the pinned hash into a genuine cross-platform claim.

M3 has started and is marked partial. Its terrain half is done: dig, mine,
claim, reinforce, repair, attack and fill-in are implemented as one uniform
data-driven rule, along with dig markers and the first real player command, and
they are exercised against the real game's data. Evidence and the three
anomalies the data throws up: `docs/research/tile-work.md`.

The first worker followed. An imp finds the nearest marked tile its keeper can
reach, walks to it at the speed its own record gives, and digs or mines it —
driven by a tag command and nothing else. That closes the "one imp doing tasks"
half of M3's definition of done. The order it prefers its jobs in was UNKNOWN
when that was written and is now CONFIRMED from the original — marked excavation,
then claiming, then reinforcing (Q24, ADR-0018) — and carrying mined gold is
built as the tail of a mining job rather than a fourth job (Q31, ADR-0019). What
remains stated in every scenario run that places a worker: an imp that runs out
of all three stands still where DK2 would have it wander (Q41); the crowding cap
of three imps a face is not implemented (Q43); and its routing is ours rather
than DK2's (Q5) — an admitted four-connected placeholder behind an interface,
which Q28 has since shown to be the wrong connectivity. Evidence:
`docs/research/imp-tasks.md`.

Rooms and the treasury came next, and turned up the milestone's most structural
finding: **DK2 has no player gold counter.** The balance table sets four limits
on gold and every one of them is per place — treasury tile, dungeon-heart tile,
floor pile, creature's hands. So gold was moved onto tiles and `PlayerState.Gold`
was removed (ADR-0015), and three behaviours stopped being special cases: a
keeper with no treasury cannot keep gold, destroying a treasury takes the gold
with it, and a keeper's first treasury is necessarily paid for out of the
dungeon heart. Where a room may go is read from the room record's own
placeable-on flags, so nothing in the rules knows what a bridge is (ADR-0016).
Evidence: `docs/research/rooms-and-gold.md`.

Mana came last, and it is the first thing in the simulation that changes because
time passed. A keeper's income is the sum of `ManaGain` over the tiles they own
plus a flat 30 for their dungeon heart, capped by `MaximumManaGainPerSecond` and
paid once a game second (ADR-0017). The finding worth keeping is again in the
zeroes: three terrain types out of 41 generate any mana at all, and they are
exactly the ownable non-room ones — so claiming ground raises income, losing it
lowers it, and building a room on your own floor takes that floor off the
payroll, none of which is written anywhere.

Two things the files could not settle came from the owner playing the original,
which is what the oracle is for: the mana ceiling is a flat 200000 for every
keeper and does not grow with the dungeon, and **a keeper has at most one dungeon
heart, whose loss is defeat.** The first closed a question that three
plausible-looking variables would otherwise have been guessed into
(`MaxManaGain`, `DungeonHeartMaxManaIncrease`); the second turned the heart's
"30 per heart" from unimplementable — a heart is a room instance and rooms here
are terrain — into a boolean. Defeat itself is not implemented and belongs with
objectives at M6 (Q40). Evidence: `docs/research/mana.md`.

**Gold now reaches a player** (2026-08-30, Q31, ADR-0019): an imp mines into its
own hands up to `maxGoldHeld` and carries the load to the nearest treasury or
dungeon-heart tile with room in it. On shipped level 1 a 3000-gold seam arrives
in the shipped dungeon heart, 1000 to a tile, across as many trips as an imp's
hands need. What is left of that mechanic is the floor lump DK2 leaves when there
is nowhere to unload, which needs the OBJECTS record (M6).

What M3's definition of done still asks for and does not have: **the Unity
view**. Rooms exist as owned terrain that costs gold; what a room *does* is a
per-room research question that has not been started, and only the treasury and
the heart do anything at all.

M1 is marked partial deliberately. Its definition of done says "all 18 record
types round-trip"; five are parsed (VARIABLES, CREATURES, TERRAIN, ROOMS, MAP)
plus the WAD and KWD containers. That was enough to unblock M2 and M3 — the
remaining record types (THINGS, TRIGGERS, PLAYERS, OBJECTS, SHOTS, ...) are
needed at M6, when real levels have to load, and are scheduled there.

**Deliberate ordering note:** M5 (oracle comparison) comes *before* breadth work. Building
the comparison rig early is what prevents accumulating a large body of plausible-but-wrong
behaviour that is expensive to unwind later.

---

## 9. Risk assessment

Severity: **C**ritical / **H**igh / **M**edium / **L**ow. Probability: as given.

| # | Risk | Sev | Prob | Mitigation |
|---|---|:--:|:--:|---|
| R1 | **Pathfinding cannot be reproduced.** No project has solved it; `Follow_Path` has no named fields. Movement feel is a large part of "is this DK2?" | **C** | High | Isolate behind `IPathfinder` from day one. Ship honest A* placeholder. Dedicate a research spike after M5, driven by recorded oracle traces rather than by reading disassembly. Accept "close enough, documented" as a possible terminal state. |
| R2 | **Creature moods/anger unsolved.** OpenKeeper's is an empty stub after 12 years. | **H** | High | Treat as a first-class research project, not a feature. `SET_CREATURE_MOODS` trigger + `MODIFY_ANGER_OF_CREATURE_IN_LAIR_PER_SECOND` are entry points. Design targeted experiments (isolate one anger source at a time). |
| R3 | **Licence contamination.** Flame has *no* licence; OpenKeeper is GPLv3. Copying either taints the project. | **C** | Medium | §10.1. Written policy, enforced in review. Use as *fact sources*, never as code sources. Record provenance per mechanic. |
| R4 | **Scope collapse.** DK2 is enormous; motivation fails before the game is playable. | **H** | High | Milestones deliver something runnable early (M3). Vertical slices over horizontal breadth. Accept that M10 may never arrive and that M3–M6 is still a worthwhile artifact. |
| R5 | **Float non-determinism.** | **H** | Medium | Fixed-point for all gameplay maths. CI determinism gate from day one (M2). |
| R6 | **The oracle is hard to instrument.** Reading DK2 memory may be fiddlier than hoped. | **H** | Medium | Prototype the memory probe at M5 against *one* field (`f3E6_stateFlags`) before committing to the approach. Fall back to video capture + manual annotation if needed. |
| R7 | **KWD's unknown 10%** hides something load-bearing. | M | Medium | Cross-check OpenKeeper against dk2-tools `.bt` templates; log every unparsed byte range rather than skipping silently. |
| R8 | **Tick rate / timing model wrong**, invalidating all timing fidelity. | **H** | Medium | Determine empirically at M5 before building timing-sensitive systems. Keep it a single named constant until measured. |
| R9 | **Combat formulas unknown**; guessing produces a game that feels wrong. | **H** | High | Isolated 1v1 experiments with fixed seeds; derive damage/interval empirically before implementing. |
| R10 | **Unity leaks into the simulation** over time. | **H** | Medium | Automated CI check: `DK2.Simulation` must not reference `UnityEngine`. Fails the build. |
| R11 | **Original assets unavailable to future contributors** (DK2 is not always easy to buy). | M | Medium | `PlaceholderProvider` must always keep the game runnable without assets. |
| R12 | **Computer-player AI opaque**; single-player levels feel wrong without it. | M | High | Flame's `MyComputerPlayer` + `drop_condition` is the best lead in existence. Defer to post-M8; heroes matter more for the campaign. |
| R13 | **Save format divergence** — our saves can't be validated against DK2's. | L | High | Accept. Our save format is our own. Use `save_struct.bt` only as a checklist of *what state exists*. |
| R14 | **Audio event semantics unknown** (40% understood). | L | High | Cosmetic. Defer entirely. |
| R15 | **Upstream projects change or disappear.** | M | Low | Vendor a pinned snapshot of Flame's `mapping/` and the dk2-tools templates into `original-reference/` (research data, not code) with recorded commit hashes. |
| R16 | **AI-generated code silently invents mechanics.** | **H** | Medium | Confidence tags are mandatory in docs and in code comments. Any simulation constant not traceable to parsed data or a cited source fails review. |
| R17 | **Regression corpus becomes unmaintainable** as systems change. | M | Medium | Scenarios are declarative data, not code. Comparison tolerances explicit and versioned. |
| R18 | **Performance** — hundreds of creatures + pathfinding on a fixed tick. | M | Medium | Headless harness makes profiling easy and early. Fixed-point is cheap. Defer optimisation until a real level runs. |
| R19 | **Unity as the wrong long-term choice** (licensing/business changes). | M | Low | The simulation is engine-agnostic by construction. Swapping presentation is weeks, not years. This risk is already mitigated by the architecture. |
| R20 | **Multiplayer expectations** creep in early and distort the architecture. | M | Low | Explicitly out of scope until post-M10. Determinism work happens to make it feasible later; that is a side effect, not a goal. |

**The two that actually decide the project's fate: R1 (pathfinding) and R4 (scope).**
Everything else is manageable.

---

## 10. Decisions required before implementation

### 10.1 Licensing policy — **RESOLVED 2026-08-29: MIT + clean-room (ADR-0003)**

> Owner decision: the project is MIT-licensed. The clean-room policy below is
> therefore in force — facts from Flame/OpenKeeper may be used, code may not.
> Reference install: `E:\GogGames\Dungeon Keeper 2` (GOG). Original analysis kept
> below for the record.

Established facts: [KNOWN]

- **Flame** publishes **no licence** (GitHub API reports `"license": null`). Default
  copyright applies: **no reuse rights whatsoever**.
- **OpenKeeper** is **GPLv3**. Copying its code obliges us to license the whole project
  GPLv3.
- DK2 itself is EA-owned proprietary software.

The workable position — and the one I recommend:

> **Facts about DK2 may be used. Code from any project may not be copied.**
>
> Struct field offsets, enum values, format layouts and observed numeric constants are
> facts about a third party's file format and program. Implementation code is expressive
> work protected by its author's licence. We consult Flame and OpenKeeper to learn *what
> DK2 does*, then write our own C# implementation. Provenance is recorded per mechanic.

**What this permits:** reading Flame's `sgmap` to learn that `lairId` sits at `+0x3B7`;
reading OpenKeeper to learn that KWD record type 230 holds variables; using the editor
manual's description of intended behaviour.

**What this forbids:** transliterating an OpenKeeper Java method into C#; copying Flame's
reconstructed C++ function bodies; vendoring either project's source into `src/`.

**Decisions needed from you:**
1. Confirm the clean-room policy above (or choose GPLv3, which would permit direct
   OpenKeeper porting and would substantially accelerate M1–M4 — a real and legitimate
   option worth considering).
2. Choose the project's own licence.
3. Confirm we may vendor Flame's `mapping/*.map` files as *research data*. (My reading is
   that these are functional descriptions of a third-party binary rather than creative
   work, but they are unlicensed, so this is worth a deliberate decision rather than a
   default.)

I have flagged rather than resolved this, per §32 of the brief.

### 10.2 Smaller decisions

| Decision | Recommendation |
|---|---|
| .NET version | .NET 9 for libraries/tools; netstandard2.1-compatible surface for the Unity assembly |
| Unity version | Latest LTS; decide at M3, not now — nothing before M3 needs Unity |
| Fixed-point format | Q32.32 (`long`-backed). Revisit if profiling demands |
| Test framework | xUnit + FluentAssertions; scenario tests generated from YAML |
| Determinism gate | Enabled from the first commit that has a `Tick()` |

---

## 11. Immediate next steps (next 1–2 weeks)

Ordered. Items 1–3 are blocking for everything else.

**1. Install Dungeon Keeper 2 v1.70. [BLOCKING — needs you]**
I checked this machine: there is no DK2 installation under `C:\Program Files (x86)`,
`C:\GOG Games`, `D:\Games`, or the Steam common folder. Without the original we have no
oracle and no assets, and roughly half the plan is inert. GOG's release is the usual route;
it needs the official 1.7 patch level. Put it in `original-reference/` (already gitignored).

**2. Settle the licensing question (§10.1). [BLOCKING — needs you]**
Everything downstream depends on whether we may port OpenKeeper code.

**3. Initialise the repository.**
`git init`; create the §4.2 skeleton; `.gitignore` covering `original-reference/` and
`build/converted/`; solution + empty projects; CI running build + tests on push.

**4. Vendor the research corpus** (pending decision 10.1c).
Pin and record commit hashes for Flame's `mapping/DKII_EXE_v170.{sgmap,refmap,espmap}` and
the dk2-tools `Formats/**/*.bt` templates into `original-reference/research/`.

**5. Write `DK2.Tools sgmap` — a Flame symbol-map query tool.**
Small, high-leverage, and needed constantly: parse the 86,706-line sgmap and answer
`sgmap struct CCreature`, `sgmap find lair`, `sgmap global 0x6...`. Turns the single best
research asset into something queryable in seconds. Estimated half a day; will be used
hundreds of times.

**6. Read the DK2 Official Editor Manual end to end** and write
`docs/research/editor-manual-notes.md`. This is EA's own statement of intended behaviour and
is the cheapest high-quality source we have. Expect it to answer a dozen questions that
would otherwise cost days each.

**7. Implement the WAD reader** (`DK2.Data.Wad`) with tests against the real archives.
Simplest format, 100% understood, unblocks everything else.

**8. Implement the KWD/KLD reader** (`DK2.Data.Kwd`) — start with `VARIABLES(230)`,
`CREATURES(170)`, `ROOMS(120)`, `TERRAIN(110)`. Log every byte range not consumed, so the
unknown 10% is visible rather than silently skipped.

**9. Produce `docs/research/game-database-dump.md`** — the full, actual balance table
extracted from a real level. This is the first genuinely new artifact this project creates,
and it is the specification that the whole simulation will be written against.

**10. Open `docs/reverse-engineering/conflicts.md`** and seed it with the questions this
research already raised:
- What is DK2's simulation tick rate? [UNKNOWN]
- Is `CMapEntry` (11 bytes) the whole per-tile runtime state, or a cache over the KWD map? [UNKNOWN]
- Exact semantics of `f3AF_workModifier` vs `f3B3_workEnhancement`? [UNKNOWN]
- What does `Follow_Path` (36 bytes) actually contain? [UNKNOWN]
- Job-selection priority order — is OpenKeeper's ordering (lair → sleep → eat → work) DK2's
  actual ordering, or its own invention? [HYPOTHESIS, must be tested]

---

## Appendix A — Source index

| Resource | URL | Use |
|---|---|---|
| Flame | https://github.com/DiaLight/Flame | v1.70 structures, symbols, reconstructed logic |
| Flame sgmap | `mapping/DKII_EXE_v170.sgmap` | 868 structs / 10,548 globals / 7,282 fields |
| Flame IDA scripts | `mapping/ida/` | Import symbols into IDA |
| OpenKeeper | https://github.com/tonihele/OpenKeeper | Formats, game logic reference (GPLv3) |
| OpenKeeper formats wiki | https://github.com/tonihele/OpenKeeper/wiki/Dungeon-Keeper-2-file-formats | Per-format RE completeness |
| Ember | https://github.com/DiaLight/Ember | Historical methodology |
| dk2-tools | https://github.com/ufdada/dk2-tools | Independent 010 Editor format templates |
| DK2 Editor Manual | http://keeper.lubiki.pl/dk2_docs/dk2_editor_manual.htm | EA's own behaviour documentation |
| DK2 Mapper Manual | http://keeper.lubiki.pl/dk2_docs/dk2_mapper_manual.htm | Level design semantics |
| DK2 walls write-up | http://simonschreibt.de/gat/dungeon-keeper-2-walls/ | Terrain rendering technique |
| GLKeeper | https://github.com/codenamecpp/GLKeeper | Third opinion on asset loading |
| DK2MapMaker | https://github.com/Nateonus/DK2MapMaker | C# precedent for map data |
| KeeperKlan | https://keeperklan.com/ | Community forum (403 to bots; use a browser) |

## Appendix B — Research note template

Every mechanic gets one file in `docs/research/`:

```markdown
# Mechanic: <name>

Source(s):        <Flame symbol / OpenKeeper class / editor manual page / experiment id>
Original version: DK2 v1.70
Evidence:         <what was actually observed, with addresses/offsets/values>
Observed behaviour: <what the original does>
Hypothesis:       <what we believe the rule is>
Confidence:       KNOWN | INFERRED | HYPOTHESIS | UNKNOWN
Implementation:   <C# type/namespace>
Tests:            <test class / scenario ids>
Known exceptions: <quirks and bugs deliberately reproduced>
Open questions:   <what would raise the confidence level>
```
