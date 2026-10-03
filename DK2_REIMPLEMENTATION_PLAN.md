# Dungeon Keeper 2 — Modern C#/Unity Reimplementation Plan

**Document status:** Phase 0 research deliverable, and the reference for the
project's strategy, architecture rationale and milestones
**Target original version:** Dungeon Keeper 2 **v1.70** (with the 3 bonus packs)
**Reference install:** `E:\GogGames\Dungeon Keeper 2` (GOG), resolved by
`DK2.Data.Dk2Install`; `DK2_DIR` is authoritative when set

Current state, decisions and next work are **not** here: `docs/HANDOFF.md` holds
what is true today, `docs/DECISIONS.md` holds every decision and its reason, and
`docs/research/open-questions.md` holds the open unknowns. This document holds the
research findings that set the strategy, the architecture rationale, and the
milestone ladder.

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

Anything not tagged is a statement about *our* project, not about DK2. The code and
`docs/research/` use the project's own four rungs — CONFIRMED / STRONGLY INFERRED /
HYPOTHESIS / UNKNOWN — where CONFIRMED is reserved for the running game.

---

## 1. Executive summary

### 1.1 Verdict

**This project is technically realistic for a solo developer with heavy AI assistance,
but only under a specific strategy.** Reverse engineering DK2 from scratch is not
realistic. The viable strategy is:

> **Treat DK2's own data files as the primary specification, treat Flame's v1.70 symbol
> map as the structural ground truth, treat OpenKeeper as a corpus of solved problems, and
> reserve original reverse-engineering effort exclusively for the ~6 systems that nobody
> has solved yet.**

The single most important finding of the research phase:

> **A very large fraction of what people call "DK2's game rules" is not code at all — it is
> data, sitting in the `.kwd` files, in a format that is already ~90% reverse engineered
> and has a working open-source parser.** [KNOWN]

Creature health, melee damage, pay, hunger rates, training costs, room capacities, tile
health values, research times, spell costs, torture conversion times, possession mana
drain — these are `VARIABLES` and `CREATURES` records in the level data, not hardcoded
constants in `DKII.EXE`. We do not need to reverse engineer them. We need to *parse* them.

Values already recovered and named: [KNOWN]

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

The work is **not** "discover the numbers". The work is **"discover the procedures that
consume the numbers"**. One qualification the corpus sweep since added: a shipped
variable with a zero value and a live reader is a *disabled* mechanism, not an absent
one (ADR-0085).

### 1.2 What is actually hard

Six systems carry essentially all the risk. Five were named in revision 1; the sixth was
not, and turns out to be where three of the other five actually live.

| | Risk | Status |
|---|---|---|
| 1 | **Pathfinding and movement** — the least-understood system in the ecosystem. No project has reproduced DK2's actual pathfinder. | **Read, and rebuilt** (ADR-0155, ADR-0157): DK2 routes over a triangulated navigation mesh and follows it with a steering system of eight named behaviours, so `GridPathfinder` differed in kind. All three phases landed 2026-09-17: every creature routes over the mesh and moves by the steering tick, and the grid search survives only behind `TryFindNearest`. Measured against the original's own route log rather than against our expectations — 21,288 of 21,298 Level 7 requests agree (Q351, Q352). |
| 2 | **Creature job/destination selection** | **Settled for workers** (ADR-0058): a byte priority table at `0x00698658`, searched whole-map one kind at a time, then a nearest-first ring scan. Non-workers have a job system (ADR-0082) built on three mood choosers and one 28-way dispatcher `FUN_00478800`. |
| 3 | **Creature mood / anger / happiness** | **Built** (ADR-0053, ADR-0081): six counters, mood banded on the largest, once a game second, with desertion. OpenKeeper's empty `processTick()` was not the obstacle it looked like — the numbers were in the creature records and the ladder came out of the binary. |
| 4 | **Combat resolution** — melee, fear, the worker's arm, the attack spells and their shots, the status effects, the victory pause and the friendly arm are read and built end to end. | Q8 largely closed (ADR-0163–0170). Creatures and hearts take blows and fireballs; the frightened back off and run; Imps raise the alarm; the packet replays of Levels 1–3 compare fights with DK2's (ADR-0339). |
| 5 | **Computer-player (AI Keeper) behaviour** — DKII.EXE is the primary source. | **Initial independent claim review reached** (M12); effect-specific UNKNOWNs remain gated. Entry and task machine are `computer-keeper-tasks.md`; task handlers and attack response are `computer-keeper-handlers.md`. `DK2.AI.KeeperAI` implements the proactive task slice as an input source, with the attack response, Call to Arms, the battle step, the retreat of the wounded, the Heal, Chicken and Inferno casts and the attack planner with its front, task 7's bridges, step 9's training moves and the Treasury/Lair room needs connected (ADR-0278, 0279, 0283, 0284, 0287, 0298, 0302, 0304, 0317, 0327); the other room producers, the mood producers and the research move remain. |
| 6 | **The creature state machine** — **281** states with category-scoped enter/update/leave hooks (ADR-0072), which is where job selection, moods and combat all actually live. | Read (§2.2.1, `docs/research/creature-state-table.md`). Every creature holds DK2's state id from its constructor on and each activity makes its states' moves through the transition gate (ADR-0209); the activity is derived from the state (ADR-0211), and the category hooks are partly built. |

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
It is achievable because the data-driven insight above removes most of the guesswork.

**Measured against this table, 2026-09-21:** phases 0–3 closed in **23 days**
against the 21–34 weeks estimated here. The table is kept as written rather than
rescaled — §8.1 says why, and §8's Run column is the live ordering.

### 1.4 The one thing that must be decided before writing code

**Licensing — resolved.** The project is GPL-3.0-or-later with a clean-room policy
(ADR-0041, amending ADR-0003). Flame publishes **no licence at all**
(`"license": null` on the GitHub API) and OpenKeeper is GPLv3 [KNOWN]; neither has its
*code* copied here, and both are used as research references. §10.1 has the boundary.

---

## 2. Existing ecosystem

### 2.1 Summary table

| Project | Language | Stars | Last activity | Status | Value to us |
|---|---|---:|---|---|---|
| [OpenKeeper](https://github.com/tonihele/OpenKeeper) | Java / jMonkeyEngine | 494 | 2026-08-23 | Active | **Highest** — file formats, game logic corpus |
| [Flame](https://github.com/DiaLight/Flame) | C++ | 119 | 2026-02-28 | Quiet six months; unlicensed (re-checked 2026-09-06 against the clone) | **High, with a measured caveat** — see §2.2.1 |
| [Ember](https://github.com/DiaLight/Ember) | C++ | — | Superseded | Closed | Historical; superseded by Flame |
| [GLKeeper](https://github.com/codenamecpp/GLKeeper) | C++ | 40 | 2026-08-13 | Active, "not playable yet" | Low–medium; second opinion on formats |
| [dk2-tools](https://github.com/ufdada/dk2-tools) | 010 Editor templates | 13 | — | Reference | **High** — independent format specs |
| [DK2MapMaker](https://github.com/Nateonus/DK2MapMaker) | C# | 2 | — | Reference | Medium — C# KWD handling precedent |
| [DK2-Map-Editor](https://github.com/NovFR/DK2-Map-Editor) | C | 3 | — | Reference | Low (French only) |
| [dk2botfw](https://github.com/DerelictDrone/dk2botfw) | JavaScript | 1 | — | Reference | Low; multiplayer protocol notes |

### 2.2 Flame — the binary ground truth

> https://github.com/DiaLight/Flame — *"Dungeon Keeper II partial recompilation with bug fixes"*

Flame's approach: DK2 is loaded as `dk2.dll` (rebuilt with a relocation table derived from
a full cross-reference map), individual functions are reimplemented in C++, and the loader
swaps the original function pointers for the new ones at runtime. Its authors have had to
understand real DK2 structures precisely enough that recompiled code runs against live game
memory.

**Measured coverage of `mapping/DKII_EXE_v170.sgmap`:** [KNOWN]

| Metric | Count |
|---|---:|
| Total lines | 86,706 |
| Named structs | 868 |
| Named globals | 10,548 |
| Named struct fields | 7,282 |

Plus `DKII_EXE_v170.refmap` (116,194 lines — code/data cross-references) and
`DKII_EXE_v170.espmap` (182,788 lines — stack-pointer modification sites, used to walk
DK2's frame-pointer-less stack). 145 reconstructed `.cpp` files live under Flame's `src/dk2/`.

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

`CCreature` is a 1343-byte structure with semantically named fields: [KNOWN]

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

`CRoom` (169 bytes) exposes the capacity model directly: [KNOWN]

```
f67_totalCapacity, f69_totalFighterCapacity,
f6B_usedCapacity, f6D_usedCreatureCapacity, f6F_usedObjectCapacity,
f35_health, f2B_navigationPos, f2D_firstSlab, f2F_lastSlab,
f45_sData_DiscoModeStartedOnGameTick
```

Flame also ships its `src/dk2/entities/creature_state.h`, a complete decode of both creature
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
   [KNOWN] Good news for determinism and for our tick-based design.
2. `GameAction` / `GameActionArray` alongside `CNetworkCommunication` strongly implies
   DK2's multiplayer is **command-based lockstep**, which in turn implies **the original
   simulation is deterministic by design**. [INFERRED] Our determinism goal therefore
   matches the original architecture rather than fighting it.
3. `Follow_Path` is named — but has **zero named fields**. [KNOWN] Pathfinding is the
   conspicuous hole in the best RE work that exists. See §9, risk R1.

### 2.2.1 Flame on disk — measured, not assumed

**Use it for structure and navigation, not for answers, and expect it to be weakest
exactly where this project is strongest.**

| What | Where | Pinned at |
|---|---|---|
| **Flame** | `D:\Flame` | `f6e2a3d385bf2e6e95dc70cab88aa387bb7659d9`, committed 2026-03-01 |
| **OpenKeeper** | `D:\OpenKeeper` | tracks `origin/master` |

Both reference checkouts live **outside** the repository, which is a better answer to risk
R15 than vendoring `mapping/` would have been: a clone on another path cannot be staged by
accident at all. All three map files are present — `sgmap` 3.2 MB, `refmap` 3.9 MB,
`espmap` 3.8 MB.

**Both build from source here** (2026-09-07), which is what M0's definition of done asks
for. Flame: Debug, Ninja, the 32-bit MSVC toolset shipping with Visual Studio 18 (its
readme names 2022; `v14.51` compiled all 287 objects unpatched, nine warnings, no errors),
installed to `D:\Flame\install` — `PATCH.dll`, `flame\Flame.dll`, `flame\DKII.dll` and
their `.pdb`s. **Nothing was copied into the game directory**: Flame's install step
overwrites the game's `PATCH.dll`, which is a step to take deliberately with a backup.
OpenKeeper: `gradlew compileJava`, Gradle 9.6.1, a Java 25 toolchain Gradle fetched
itself, 62 deprecation warnings, no errors. Recipes for both:
`original-reference/README.md`. Running DK2 *under* Flame is M5's business.

**Licence** (re-checked against the clone, not quoted from the GitHub API alone): still
`"license": null`, and there is no `LICENSE`, `COPYING` or equivalent anywhere in the
repository tree. Last push 2026-02-28. **Unlicensed means no reuse rights**, unchanged by
this project being GPLv3.

#### What was measured

The `sgmap` was searched for the specific addresses this project had reconstructed by hand
while building eating and closing Q173. The mixture is the finding.

**What Flame would have given us:**

| Address | Ours, by hand | Flame |
|---|---|---|
| `0x00699688` | "a 32-record table of 0x40 bytes, categories with enter/update/leave hooks" | **`g_stateGroups`, `size=2048`, struct `MyStateGroup`, `size=64`** |
| `0x00476D30` | "this is SetState" | **`setCurrentState_476D30`** |
| `0x00479200`, `0x004E70A0` | "the creature-state dump", "the room dump" | **both named `dump`** |
| every state handler read | free functions | **all members of `vtbl_0066D404`** |

Three of those are worth having. `g_stateGroups` independently confirms the 0x40 record
size derived from store spacing, and its fields line up with what was inferred by hand: a
mask at `+0`, an **enter** function at `+4`, a second function at `+0x14`, an **exit**
function at `+0x24` — plus two meanings nobody here had guessed, `+8` and `+0x18` being
**field offsets into the creature state object**, paired with the two function pointers, so
those hooks are told which field to work on. `setCurrentState_476D30` confirms an
identification made from behaviour. And **the vtable membership is structural information
this project did not have at all** — the state handlers are virtual methods on a class,
which reframes the whole state machine.

**What Flame would *not* have given us.** Every function whose *behaviour* was the question
comes back as an auto-generated placeholder:

```
0x00463500  sub_463500     the hatchery's chicken search
0x00463760  sub_463760     eating
0x00477C20  fun_477C20     the needs routine that drives hunger, sleep and pay
0x0047B7C0  sub_47B7C0     the prison drain — the whole of Q173
```

Nor is the creature *state* table at `0x006DA8B0` present, nor are the balance variable
globals (`0x006F2621`, `0x006F2590`) this project mapped through the dispatch table.

**Where the two disagreed, Flame was right.** It names `g_stateEntries` at
**`0x006DA8B0`, size 7025**; this project had placed the first record at `0x006DA8C0` with
239 records of 25 bytes. The base is `0x006DA8B0` with the update handler at `+0` — so
every handler here had been read one state late — and the count is **281**: 7,025 bytes is
281 records of 25, and the 239 came from a scan window that ended `0x420` bytes short.
Settled by six independent checks, ADR-0072. The conflict was recorded with both positions
before the answer came in, which is §5's conflict protocol working as written.

#### The number that decides it

| Region | Symbols | Meaningfully named |
|---|---:|---:|
| Whole `sgmap` | 10,548 | **58%** |
| `0x00450000`–`0x00520000` — creature state, jobs, needs, prison | 2,798 | **34%** |

**Flame's naming is thinnest precisely where this project's remaining risk lives.** That is
consistent with `Follow_Path` being named with zero named fields: the DiaLight work has
concentrated on the executable's shell — loader, rendering, netcode, the parts you must
understand to recompile and run — while creature AI is the part you can leave as a black
box and still ship a working patched game. It is the opposite of our priority.

#### Recommendation

1. **Cloned and pinned** at `D:\Flame` (`f6e2a3d`), recorded in
   `original-reference/README.md`, and built there. A clone outside the repository is a
   better answer to R15 than vendoring, because it cannot be staged by accident.
2. **Use it for four things**: the vtable and struct layout that give a function its
   *context*; confirming an identification made independently; the `refmap` when Ghidra's
   own analysis misses a computed reference; and `espmap` if a stack question arises.
3. **Do not expect it to answer behavioural questions.** On the evidence above, *"what does
   this do"* keeps coming from the binary itself, from DK2's shipped debug dumps, and from
   the owner watching the game.
4. **Ghidra, not IDA.** Flame ships `mapping/ida/*.py` and no Ghidra equivalent, so
   importing the `sgmap` means writing a Ghidra script against its text format. That format
   has a reference parser in Flame's repo (`mapping/sgmap/*.cpp`) which may be **read for
   format facts only**. Not written; it is item 5 of §11.
5. **The identifier boundary is decided — ADR-0080.** A Flame identifier may be *cited* in
   a document, as §2.2 and this section do, provided the sentence makes Flame the source;
   it never appears in `src/`, `tests/` or the Unity adapter, not even in a comment. Names
   are not copyrightable and a citation is not a copy; the source tree is the one place a
   copying argument could be made, so that is where the rule is absolute, and
   `FlameIdentifierTests` checks the shape of it. OpenKeeper's precedent — names used with
   attribution in `THIRD-PARTY-NOTICES.md` — does not transfer, because it rests on a
   licence Flame does not have.

---

### 2.3 OpenKeeper — the solved-problems corpus

> https://github.com/tonihele/OpenKeeper — *"Dungeon Keeper II remake"*, GPLv3, Java + jMonkeyEngine 3.9 / Java 25

Same stated goal as ours (v1.7 + 3 bonus packs, minimal changes, original assets), 12+
years of accumulated work, and still actively developed.

**Where OpenKeeper is strongest — file formats.** A complete converter suite under
OpenKeeper's `tools/convert/`: `wad`, `kmf`, `map` (KWD/KLD), `kcs`, `str`, `sound`, `spr`, `bf4`,
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

All 18 now have typed readers here and every record image round-trips byte for byte
(ADR-0091); per-field layout is in `docs/research/kwd-record-fields.md`.

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

Enumerating the actions is a *format* fact. **Parameter meanings are not**: the offsets,
widths and signedness of a trigger's parameters were re-derived from DKII.EXE reads rather
than from editor labels, and three transcribed meanings were wrong (ADR-0092,
`docs/research/trigger-semantics.md`).

**OpenKeeper's architecture** is an ECS (Simsilica Zay-ES) with a clean split between
`game/controller/*` (authority), `game/logic/*` (systems), `game/task/*` (worker tasks),
`game/navigation/*`, and `game/trigger/*`. It already separates simulation from
jMonkeyEngine reasonably well — direct evidence that the simulation/presentation split we
want is achievable for this game.

**Where OpenKeeper is explicitly weak** — from their own open issue tracker (98 open
issues): [KNOWN]

- `Research and implement creature moods` — `CreatureMoodSystem.processTick()` is
  literally empty in the current source
- `Improve AI and makes states more transparent`, `Fix issues in AI`
- `Match DKII room and bridge placement rules`, `Add better room building rules`
- `Better task management`, `Tasks refactor`
- `level 10 imp "runs into" walls`, `Optimize steering behaviour`, `Implement movement prediction`
- `Campaign progress`, `MPD Level Progress`

**That list is a map of the frontier.** Those are precisely the systems where a 12-year-old
project with a working game still does not match DK2, and where our research budget goes.

### 2.4 Ember — historical

> https://github.com/DiaLight/Ember — *"Dungeon Keeper II patching launcher"*

Its own README states: *"The project was closed due to development complexity and is no
longer maintained. Development of Ember patches continues in the Flame project."* [KNOWN]

Ember established the techniques Flame inherited: `mappings/v170.map` (function/data
symbols), `mappings/references.map` (chunk cross-references — used to synthesise a
relocation table so DK2 can be a DLL), and `mappings/stack.map` (ESP modification sites,
because `DKII.EXE` does not use EBP as a frame pointer, so stack walking requires a
precomputed map). [KNOWN]

**Use Flame exclusively. Do not diff the two mapping sets.**

### 2.5 Secondary resources

- **[ufdada/dk2-tools](https://github.com/ufdada/dk2-tools)** — 010 Editor binary templates
  for ~30 formats, including a per-record breakdown of KWD (`creatures_struct.bt`,
  `rooms_struct.bt`, `terrains_struct.bt`, `triggers_struct.bt`, `variables_struct.bt`,
  `things_struct.bt`, …) **and `Formats/Save/save_struct.bt`** — the save-game format. An
  *independent* derivation of the formats, and therefore a cross-check against OpenKeeper.
  Where the two disagree, we have found a real question.
- **[DK2 Official Editor Manual](http://keeper.lubiki.pl/dk2_docs/dk2_editor_manual.htm)** —
  live (HTTP 200), alongside `dk2_mapper_manual.htm`. *EA's own documentation of how the
  game is supposed to behave*, written for level designers: the best natural-language
  source of intended semantics. Not yet read — §11.
- **[DK2 walls write-up](http://simonschreibt.de/gat/dungeon-keeper-2-walls/)** — live;
  explains DK2's wall/terrain rendering trick.
- **keeperklan.com** — the historical forum (incl. thread 4623, *Reversal of DKII Binary
  File Formats*). Returns HTTP 403 to automated clients; reachable in a normal browser, and
  the `lubiki.keeperklan.com` mirror serves the docs.
- **[GLKeeper](https://github.com/codenamecpp/GLKeeper)** — C++ reimplementation, active but
  self-described as "not playable yet". A third opinion on asset loading.
- **[DK2MapMaker](https://github.com/Nateonus/DK2MapMaker)** — C#. Small, but the only C#
  precedent for reading/writing DK2 map data.
- **The 1999 player manual** (`original-reference/`, `docs/research/manual-1999.md`) — admitted
  as corroboration only, ADR-0037.

---

## 3. Reverse-engineering status

Confidence reflects *our ability to reproduce the behaviour*, not merely to parse a file.

| System | Known | Partial | Unknown | Primary source |
|---|:--:|:--:|:--:|---|
| **File formats (WAD/KMF/STR/textures/video)** | ● | | | OpenKeeper converters; dk2-tools templates; ours: `WadArchive`, `KmfReader`, `Dk2StringTable` |
| **Map & level data (KWD/KLD)** | ● | | | All 18 record types parse and round-trip (ADR-0091); `kwd-record-fields.md` |
| **Terrain data model** | ● | | | KWD `TERRAIN(110)` |
| **Terrain runtime behaviour** | | ● | | Flame `CMapEntry` (11 bytes, **fields unnamed**) |
| **Digging / claiming** | ● | | | One data-driven rule (ADR-0009/0089); crowding cap built (ADR-0026/0030); "fill in" is not a DK2 action (Q219) |
| **Rooms — definitions, types, capacity** | ● | | | Flame `CRoom` + `room_type.h`; KWD `ROOMS(120)` |
| **Rooms — placement/merge/bridge rules** | | ● | | Placement from placeable-on flags (ADR-0016); bridges ADR-0031/0032, span adjacency ADR-0304; OpenKeeper's open issues admit mismatch |
| **Economy — parameters** | ● | | | KWD `VARIABLES(230)` MiscVariables; `game-database-dump.md` |
| **Economy — payday/wage procedure** | ● | | | Built as a journey (ADR-0078/0079); `f3E2_getPaid`, `SkipNextPayment` |
| **Creature stats & data** | ● | | | KWD `CREATURES(170)`, `CreatureStats` StatTypes |
| **Creature state representation** | ● | | | Flame `creature_state.h` — 54 named flags |
| **Creature job/destination selection** | | ● | | **Workers: CONFIRMED and built** (ADR-0018/0058/0061, `imp-task-priority.md`); **non-workers: built from the mood choosers and the 28-way dispatcher `FUN_00478800`** (ADR-0082), against the 281-state table (ADR-0072) that is still being mapped |
| **Creature moods / anger / happiness** | | ● | | **Built** (ADR-0053/0081, `creature-anger.md`): six counters saturating at 65533, mood banded on the largest, measured ceiling 32767, once a game second, desertion. Open: happiness sources beyond the two a player can switch off |
| **Combat — stats** | ● | | | KWD; `MELEE_DAMAGE`, `MELEE_RECHARGE_TIME_SECONDS` |
| **Combat — resolution & target selection** | | ● | | Melee and spells built from the binary (ADR-0163–0170); what is left: `dk2tools coverage`, `docs/HANDOFF.md` |
| **Pathfinding** | ● | | | Triangulated navigation mesh, greedy two-ended search, funnel and steering read from the binary and built (ADR-0155, ADR-0157) |
| **Possession** | | ● | | `Possessed` flags, `f3EE_nextPossessionInfluenceTick`, `POSSESSION_MANA_COST_PER_SECOND` [KNOWN]; control model [PARTIAL] |
| **Spells (keeper & creature)** | | ● | | KWD `KEEPER_SPELLS(150)`, `CREATURE_SPELLS(160)`, `SHOTS(260)`; Create Imp, Thunderbolt, Heal and Call to Arms built (ADR-0021/0022, ADR-0170, ADR-0279, ADR-0287); the rest: `dk2tools coverage` |
| **Traps & doors** | | ● | | KWD `TRAPS(130)`, `DOORS(140)`; authored state retained (ADR-0106); doors fought, gates collapsed, every shipped trap firing (ADR-0318, 0320, 0321, 0331); disarming deferred (ADR-0329, Q455); what is left: the M7 row of §8 |
| **Heroes & parties** | | ● | | `HeroSystem`; parties raised by action 14 (ADR-0108, ADR-0197) and walked behind their leader (ADR-0216); what is left: `docs/HANDOFF.md` |
| **Level scripting / triggers** | | ● | | Parameter views verified against DKII.EXE (ADR-0092); banks are level state (ADR-0102); action points are world state (ADR-0103). **Runs**: cadence closed (Q228, Q239), the world, party, player, action-point and creature roots walked once a turn (ADR-0152, ADR-0204), sixteen action types carried out (list: `docs/GOAL_ALIGNMENT.md` row 2). **Missing**: the other condition and action types; `dk2tools kwd unreached` ranks them |
| **Campaign progression** | | ● | | OpenKeeper open issue "Campaign progress" |
| **Computer player (AI keeper)** | | ● | | Flame `MyComputerPlayer`, `computer_player_flags`, `drop_condition`; DK2's own keeper AI has been read (`docs/KEEPER_AI.md`) and `DK2.AI.KeeperAI` is being built against it |
| **Multiplayer** | | ● | | `GameAction`, Flame `protocol_dump`, `network/protocol.h` |
| **Save system** | | ● | | dk2-tools `save_struct.bt`; Flame `sData_*` prefixes; `Dk2SaveReader` reads the tile grid only (Q182) |
| **Rendering** | ● | | | KMF 95%, EngineTextures 100%; `DK2.Convert` writes glTF/GLB and PNG |
| **Audio — formats** | ● | | | SDT/BANK/SFX parsed |
| **Audio — event semantics** | | | ● | OpenKeeper: BANK/SFX usage 40% |

**Summary:** *data* is largely solved; *procedure* is largely not. The remaining red and
thin cells — the rest of combat and trigger execution, the computer player and
audio event semantics — are where original research is required. `docs/RESEARCH_STATUS.md`
tracks the same ground per mechanic.

---

## 4. Proposed C#/Unity architecture

The rationale for the shape. **What the tree actually contains is
`docs/ARCHITECTURE.md`**; where the two differ, the tree is right and the
difference is noted here.

### 4.1 Guiding constraints

1. `DK2.Simulation` must have **zero** reference to `UnityEngine`. Asserted from the built
   assemblies by `ArchitectureTests`.
2. The simulation is a **fixed-tick, deterministic state machine**, matching the original's
   design. [INFERRED, §2.2]
3. All randomness flows through an explicit, seeded, serialisable RNG.
4. All balance values come from parsed DK2 data. **A magic number in simulation code is a
   bug.** `GameDatabase[VariableTypeId.X]` throws rather than defaulting, so a system
   reading a new variable fails loudly on every synthetic database until the variable is
   added.
5. Rendering observes the simulation; it never mutates it.

### 4.2 Repository layout

```
/DungeonKeeperRemake
├── DK2.slnx
├── DK2_REIMPLEMENTATION_PLAN.md          ← this file
├── src/
│   ├── DK2.Core/                 primitives: fixed-point, TilePos, RNG, hashing, IDs
│   ├── DK2.Data/                 format parsers: WAD, KWD/KLD, STR, SDT, SAV
│   ├── DK2.Data.Model/           parsed, immutable game database
│   ├── DK2.Simulation/           the game. no Unity, no rendering, no I/O.
│   ├── DK2.AI/                   the computer keeper, as an input source
│   ├── DK2.Pathfinding/          isolated on purpose — highest-churn subsystem
│   ├── DK2.Assets/               IAssetProvider abstraction + resolution chain
│   ├── DK2.Presentation/         world → drawable, renderer-independent
│   ├── DK2.Convert/              original art → glTF/GLB and PNG
│   ├── DK2.Harness/              headless scenario runner
│   ├── DK2.Tools/                the dk2tools CLI
│   └── Shared/                   UnityBound.props, netstandard2.1 polyfills
├── unity/DK2.Unity/              Unity project — presentation only
├── unity/DK2.Unity.Compile/      compiles the adapter against Unity's modules
├── tests/                        Core, Data, Simulation, Behavior, Assets,
│                                 Presentation, Convert, AI, Tools, TestSupport
├── docs/
│   ├── research/                 one file per mechanic, on the Appendix B template
│   ├── architecture/             the simulation loop
│   └── experiments/              recorded oracle results (level1-idle-001, level20-idle-001, level20-idle-002, doors-and-traps.md)
├── scenarios/                    declarative scenario files (see §6.2)
├── reverse-engineering/          .gitignored except its README (ADR-0046)
└── original-reference/           .gitignored — reference material, never committed
```

Divergences from the revision-1 proposal, all deliberate: the solution is `DK2.slnx`;
`DK2.Simulation` is flat rather than split into `World/ Entities/ Rooms/ Economy/ Combat/
Jobs/ Scripting/ Snapshot/` subfolders, with one file per rule type and `Systems/` and
`Commands/` the only subfolders; ADRs live in `docs/DECISIONS.md` rather than under
`docs/architecture/`; there is no `DK2.Pathfinding.Tests` project; and `DK2.AI` holds the
*computer keeper* only — creature decision-making is simulation and lives in
`DK2.Simulation`.

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

**On tick rate:** answered. DK2's rate is **4 ticks a game second**, a tick being 250 ms —
read from the binary and since observed at 3.94 ticks/s on a running instance (ADR-0089,
ADR-0102 amendment). `SimulationTiming` is the only place ticks relate to seconds, and it
keeps `TicksPerSecond` (ours, refinable) separate from `OriginalTicksPerGameSecond` (DK2's,
a fact) even though they are equal.

**On fixed-point arithmetic:** the original is a 1999 x86 game using a mix of integers and
x87 floats (Flame ships an `fpu_control.h`). Reproducing float behaviour bit-exactly across
modern hardware is not tractable. **Decision: integer/fixed-point for all
gameplay-affecting quantities**; `float` reserved for presentation. This trades exact float
reproduction (unachievable) for cross-platform determinism (achievable and more valuable).
ADR-0001; the format is Q32.32, `long`-backed (`Fp32`).

### 4.4 Data layer

The parsed DK2 database is loaded once and is immutable. As built, `GameDatabase` exposes
`Creatures`, `Rooms`, `RoomsByTerrain`, `Terrain`, `Objects`, `Doors`, `Traps`,
`KeeperSpells`, `CreatureSpells`, `Players` and the additional record types, plus an
indexer `this[VariableTypeId]` over the balance table and `ForLevel` for per-level
overrides. Simulation code reads `database[VariableTypeId.MineGoldHealth]`, never `-300`.

Two data-layer facts that were not obvious from the format work:

- **Levels play on their own VARIABLES**, layered over the global table (ADR-0040). A
  campaign run that reads only the global table uses the wrong numbers wherever a level
  disagrees, and 26 variable ids disagree on at least one level.
- **`GameDatabase` is DK2-shaped** — records mirror KWD layout field for field, including
  string-table ids. Correct for fidelity; goal 8's mod author would express content in
  DK2's record shape, which is a question for M6 and not before.

### 4.5 AI architecture

**Do not pick an AI paradigm.** DK2 stores creature state as **two 32-bit flag words plus a
`cstate` sub-object** [KNOWN], and Flame names a `FightProcessCounter` occupying 4 bits.
That is the signature of a **hand-written state machine with flag-based modifiers**, not of
a planner or a utility system. [INFERRED] The 281-record state table with category-scoped
enter/update/leave hooks (ADR-0072) confirms it.

The design that followed is an **ordered decision list whose ordering is a declared,
testable hypothesis**, and every decision returns **why** it fired so the harness can diff
*reasons* rather than only outcomes:

> **An implementation that reaches the right destination for the wrong reason is not
> correct, and we must be able to see that.**

As built: `ImpTaskSystem` for workers on the priority table read from the binary
(ADR-0058), and `CreatureJobSystem`'s three mood choosers for everybody else (ADR-0082),
each recording a reason through `ISimulationEventSink`.

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

Routing is DK2's own: a triangulated navigation mesh and a steering system, read from the
binary and built (ADR-0155, ADR-0157). `GridPathfinder` survives only behind
`TryFindNearest`: eight-connected, 10 for a straight step and 14 for a diagonal (ADR-0025,
Q48), deterministically tie-broken, cutting a corner only past an open side (ADR-0339).
What "walkable" means is not its business — `WorldPathGrid` answers that for one
particular mover, from terrain flags and that creature's own flags.

### 4.7 Presentation boundary

```
GameSimulation ──emits──> SimulationEvent stream ──> presentation layer
       ▲                                                      │
       └──────────────── InputFrame (commands only) ──────────┘
```

Unity's job: render, animate, play audio, take input, draw UI. It holds **no**
authoritative state. A `GameObject` is a view of an entity id; deleting every `GameObject`
must not change the simulation. This also means the harness can run the game with no
renderer at all.

As built the boundary is a *view* rather than an event stream: `WorldView` produces
readonly view structs for one viewing keeper, `ScenePresenter` diffs them and pushes
through `IRenderTarget` (ADR-0035). Events remain, and are write-only.

### 4.8 Asset abstraction

```
CreatureDef ──> AssetRef("creature.imp.mesh") ──> IAssetProvider ──> DK2 KMF (dev)
                                                                 └─> fan-made (later)
```

No filename literals outside `DK2.Assets`. This is what keeps a redistributable build
possible (§10.2). ADR-0034; the chain ends in a **total** placeholder provider, so the game
runs with no DK2 install and no art pack.

---

## 5. Reverse-engineering workflow

**The binary is the primary source.** `DKII.EXE` is the actual DK2; every other written
source is somebody's reading of it, so when a question is open, decompile before you infer
(AGENTS.md, ADR-0046 as amended). Two steps come before the flowchart below, and they are
what has actually been working:

**0a. Look for DK2's own debug dump of the structure.** The retail build kept its `printf`
format strings, so one function names an entire struct in EA's own words. Three found so
far: the creature instance, the room, and the creature *state* object. This is a better
first move than the disassembler.

**0b. Ask the owner, if a player could see it.** The oracle is installed and the owner has
played the game. Questions filed as UNKNOWN have been settled by a sentence, and one — the
health a captured creature wakes with — was *built wrong* because nobody asked.
`docs/research/observation-checklist.md` is the standing list.

Then, cheap to expensive:

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
7. Ghidra                        DKII.EXE v1.70; the headless command is in
                                 docs/RESEARCH-QUEUE.md
        ↓
8. Validate on the running game  a live-memory reader, or a stopwatch
        ↓
9. Write docs/research/<x>.md    on the Appendix B template, with a confidence tag
        ↓
10. Implement + regression test
```

**Steps 2–4 are structure, not answers.** §2.2.1 measures why: Flame names only 34% of the
symbols in the address range where creature behaviour lives, and none of the four routines
whose behaviour drove the eating and prison work. Use it for struct layout, vtable
membership and cross-references, and to confirm a reading reached independently. Flame
ships `mapping/ida/espmap_ida.py`, `sgmap_ida.py` and the `refmap_ida` package for IDA;
there is no Ghidra equivalent, so importing the `sgmap` needs a script (§11 item 5).

**Conflict protocol.** When OpenKeeper and Flame disagree, or either disagrees with the
editor manual, this is a *finding*, not a nuisance. Record both positions, design an
experiment against the original game, and record the verdict. **Never silently pick one.**
Its first run was the `g_stateEntries` base address, where Flame turned out to be right on
both counts (§2.2.1, ADR-0072).

---

## 6. Experimental methodology

### 6.1 The oracle

The installed original DK2 v1.70 is the reference implementation, and it outranks a misread
disassembly for anything observable. Three ways in, in the order they have proved useful:

1. **The owner playing**, answering a question by name.
   `docs/research/observation-checklist.md` is what to ask for.
2. **A live-memory reader** attached to a running `DKII-dx.EXE` or `DKII.EXE` with
   `PROCESS_VM_READ` only, sampling world pointers, balance variable 19, record counts,
   the trigger flag and timer banks and the tick counter into a CSV. It lives in
   `reverse-engineering/` (gitignored); its invocation is in `reverse-engineering/README.md`. It has
   settled the tick rate and read level 1's condition array out of the running game.
3. **Flame**, which provides logging and inspection hooks and a TPS limiter, making
   frame-stepping easier than with the stock executable. Not yet used for this — running
   DK2 under Flame is M5's first step.

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

The runner reports every part of a scenario it could not honour — unknown keys, unapplied
setup, observations nothing produces yet — so a run can never look more complete than it is.

### 6.3 Capture protocol

| | Original DK2 | Our simulation |
|---|---|---|
| Setup | generated custom level + trigger script | `ScenarioRunner` |
| Stepping | Flame TPS limit; frame-step | `sim.Tick()` |
| Observation | screen capture + memory inspection at known `CCreature` offsets | direct state read |
| Output | `docs/experiments/<id>/oracle.json` | `<id>/ours.json` |

Reading the oracle's state directly from memory is feasible *because Flame gives us the
field offsets* — e.g. `f3B9_roomId` at `+0x3B9`, `f3E6_stateFlags` at `+0x3E6`. This
converts "watch the screen and guess" into "read the actual variable", and the live-memory
reader in §6.1 is the first working instance of it.

### 6.4 Comparison and regression

```
oracle.json ──┐
              ├──> BehaviorComparer ──> pass / fail / DIVERGENCE REPORT
ours.json ────┘
```

Every scenario becomes a test in `DK2.Behavior.Tests` (`ScenarioExpectationTests` runs every YAML with expectations, one case per file; the computer keepers' scenarios have tests of their own in `KeeperScenarioTests.cs`).
Divergences are triaged as: *our bug* → fix; *unknown mechanic* → new research doc;
*acceptable* → record with an explicit written justification. The corpus only ever grows.
Built: a scenario that asserts carries an `expect:` block (ADR-0150), three idle captures
sit under `docs/experiments/` and `dk2tools capture-diff` compares them, and the packet
replays of Levels 1–3 are ADR-0339's rig. There is no comparer across all scenarios.

### 6.5 Determinism gate

Independent of the oracle: same seed + same inputs ⇒ identical `StateHash()` at every tick,
across OS and CPU. If this ever fails, all fidelity results are meaningless until it is
fixed. It is asserted by `DeterminismTests` with one pinned literal, and
`.github/workflows/ci.yml` declares the Linux + Windows matrix that would make the
cross-machine claim real. **No CI is running**, so the pin rests on local runs and the
commit-msg hook (`git config core.hooksPath .githooks`) is the only automatic gate.

---

## 7. Asset pipeline

### 7.1 Development flow

```
original-reference/DK2/          (local only, .gitignored, never committed)
        │
        ├── *.WAD ──> DK2.Data.Wad ──────> raw members
        ├── *.KWD/KLD ──> DK2.Data.Kwd ──> GameDatabase  ← the specification
        ├── Meshes.WAD/*.KMF ──> DK2.Convert ──> glTF/GLB
        ├── EngineTextures.dat ──> DK2.Convert ──> PNG
        ├── *.SDT / Sounds ──> DK2.Convert ──> WAV
        └── *.STR ──> Dk2StringTable ──> localisation tables
                              │
                              ▼
              build/converted/   (also .gitignored)
                              │
                              ▼
                    IAssetProvider  ←── Unity loads only through this
```

The converter is a **build step the user runs against their own legally-owned copy**,
exactly as OpenKeeper does (`dk2tools convert`). No converted asset is ever committed or
distributed, and no decoded game text is either — the format facts and the reader are ours;
the strings are EA's.

### 7.2 Keeping replacement possible

`AssetRef` is a logical key (`"creature.imp.mesh"`), resolved at runtime by a provider
chain: replacement → converted → placeholder. Adding a free-asset pack later is a data
change, not a code change. `PlaceholderProvider` must always be complete enough that the
game *runs* (as untextured boxes) with no DK2 install — `AssetCatalogueTests` proves it is
total over the whole shipped database, so the property is continuously tested rather than
aspirational.

### 7.3 Data vs. assets — an important distinction

The KWD *balance data* is not an art asset; it is the game's rule specification, and we
cannot ship it either. Mitigation: define our own open `GameDatabase` schema plus a
converter from KWD. Development uses the converted original; a future standalone release
would ship a hand-authored database. Every simulation test already builds a `GameDatabase`
by hand with no DK2 install, so that path is proven; what does not exist is an external
content format.

### 7.4 Modernized assets

M11's modern presentation re-expresses the originals rather than replacing their design:
*preserve design, increase fidelity*, and for animation *preserve motion, modernize
representation* (ADR-0242). The standard is `docs/ART_DIRECTION.md`; the proposed
Blender-based pipeline, its three-asset proof of concept and its open decisions are
`docs/ASSET_PIPELINE.md`. Modern assets are served by the replacement tier of §7.2 and,
being derived from EA's art, are never committed or distributed.

---

## 8. Milestones

**Ids are names, not a sequence.** `M6` and `M7` are cited about eighty times
across the ADRs and research docs, so the ids never move. The **Run** column is
the order, and revision 2 changes only that column and the last three rows.

| # | Run | Milestone | Definition of done | Est. |
|---|---|---|---|---|
| **M0** ✅ | — | Research baseline | This document; repo skeleton; DK2 v1.70 installed; Flame + OpenKeeper building locally | 1–2 wk |
| **M1** ✅ | — | Formats readable | `DK2.Tools extract` dumps WAD contents; KWD parses to `GameDatabase`; all 18 record types round-trip; unit tests over the shipped campaign maps | complete 2026-09-07 |
| **M2** ✅ | — | Deterministic skeleton | `GameSimulation.Tick()` exists; `StateHash()` stable; save/load round-trips; determinism gate green; headless harness runs a scenario file | 3–4 wk |
| **M3** ✅ | — | Dungeon prototype | Grid + terrain from a real KWD; dig, claim, reinforce; one imp doing tasks; treasury; gold; Unity view renders it with placeholder art | 8–12 wk |
| **M4** ✅ | — | First creature loop | Portal spawn; lair claim; hunger→hatchery; sleep; training; payday; job selection with logged reasons | 8–12 wk |
| **M5** ◐ | 2 | First oracle comparison | Memory-reading oracle probe working against DK2; 20+ scenarios captured from both sides; first divergence report | 4–6 wk |
| **M6** ◐ | **1** | Real level loads | A stock campaign level loads, terrain and things place correctly, triggers/objectives fire, **level is winnable** | 6–10 wk |
| **M7** ◐ | 3 | Combat & heroes | Combat resolution, hero parties, hero gates, traps, doors. Built: combat, hero parties, the door fight (ADR-0318), hero gates (ADR-0320), every shipped trap (ADR-0321, ADR-0331), the Boulder's rolling object (ADR-0333), the Jack In The Box's Jack (ADR-0337), the reveal (ADR-0338), the Guard Post (ADR-0330), trap upkeep and trap damage (ADR-0326), the alarm's route word (ADR-0332), creatures fighting a revealed trap (scan D, ADR-0340); disarming is deferred under ADR-0329's scoped inference (Q455), shot heights move to M9 and the Boulder's slap to object slapping (ADR-0341); left: the owner's observations, Q520's wall disputed, Q513, Q524, Q362 (`experiments/doors-and-traps.md`) | 3–5 mo |
| **M8** | 5 | Breadth | All rooms, all creatures, all keeper spells, research, possession, prison/torture/graveyard/temple | 6–12 mo |
| **M9** | cont. | Fidelity | 500+ behavioural regression scenarios green; documented divergence list; pathfinding rewritten against oracle traces | continuous |
| **M10** | 6 | Campaign | Full campaign playable start to finish | after M8 |
| **M11** | 8 | Modern presentation | PBR materials, modern lighting, HD textures, modern UI, ultrawide | after M10 |
| **M12** *new* | **4** | **Keeper AI** | A computer keeper plays a stock level unaided: digs, expands, builds to a need, recruits, defends, attacks. Measured against DK2's own keeper on the same map, not against our expectations | 4–8 mo |
| **M13** *new* | 7 | **Skirmish** | Any multiplayer-capable map playable against 1–3 computer keepers, with win and loss conditions, alliances and the lose-by-conquest path | after M12 |
| **M14** *new* | 9 | **Multiplayer** | Command-based lockstep over a network: identical `StateHash()` across peers for a full match, with rejoin and desync detection | after M13 |

### 8.1 Revision 2 — 2026-09-21

Revision 1 ended at "full campaign playable" plus presentation. The owner's goal
is **campaign, skirmish and multiplayer**, and two of those three had no
milestone at all. This revision adds them and moves one existing item.

**Measured pace, for calibrating everything below.** First commit **2026-08-29**;
**23 days** later, 19 of them with commits, 574 commits, M0–M4 all closed.
Revision 1 estimated those five at **21–34 weeks**. So the attempted work has run
at roughly **6–10× the planned rate**. That is what AI assistance buys on
transcription-shaped work, and the estimates above are left unchanged rather than
rescaled, because the three risk-bearing milestones — M7, M12, M9 — are
research-bound and will not compress the same way. Treat the remaining estimates
as upper bounds everywhere except M12.

**What changed, and why.**

1. **M6 runs first, ahead of finishing M5.** The plan's own words are *"No level
   is winnable"*. Everything else is built on levels that cannot be won, and
   objectives plus defeat are the smallest remaining step to a thing the owner
   can actually play and report on. Owner feedback has outrun research at every
   point where the two were compared, and there is no way to get it without a
   winnable level.
2. **M12 Keeper AI is pulled from post-M8 to immediately after combat.** It was
   never a milestone, only risk #5 of §1.2, and it is the long pole for two of
   the three stated goals: **skirmish is the keeper AI**, and a campaign level
   with a rival keeper needs it too. At that revision `DK2.AI.KeeperAI` was ours — it dug
   to gold and placed rooms in a fixed order — and the plan already said it "would
   inject divergence that cannot be attributed". It is the least-solved system in
   the ecosystem after pathfinding, which is now solved, so it is also the
   largest remaining unknown. Research-bound work should start early and run
   alongside breadth, not after it.
3. **M13 Skirmish and M14 Multiplayer are named** so they stop being implicit.
   R20 in §9 parks multiplayer until post-M10 to keep it from distorting the
   architecture; that guard stands, and M14 sits after M13 accordingly. The head
   start is real but partial: DK2's multiplayer is command-based lockstep, the
   determinism gate has held since M2, and `docs/research/game-actions.md` has
   the 86-record action table the packet stream uses. What is missing is the
   transport, rejoin and desync handling, none of it researched.

**One risk this revision does not solve.** Research has outpaced building. At
revision 2 the repository held 90 research docs, 154 ADRs and 350 open-question rows,
and combat was read end to end with nothing built. The ordering above is only useful
if each milestone closes on working simulation rather than on a document.

<!-- GENERATED by `dotnet run --project src/DK2.Tools -c Release -- status`.
     REGENERATE, DO NOT EDIT. `status --check` exits 1 if this is stale. -->

**Status (2026-10-03, regenerated by `dk2tools status`).** **3708 tests** (of which **452 need the game data and SKIP without it** — ADR-0100), 42 scenarios, **326 ADRs** (ADR-0001 … ADR-0092 plus ADR-0100 … ADR-0238 plus ADR-0240 … ADR-0248 plus ADR-0250 … ADR-0251 plus ADR-0260 … ADR-0266 plus ADR-0270 … ADR-0306 plus ADR-0308 … ADR-0346 plus ADR-0348; 0093–0099, 0239, 0249, 0252–0259, 0267–0269, 0307, 0347 reserved), 160 research docs, 482 open-question rows running to Q533, `HashVersion` **169**, pin `0x1D8367ACE6D6D7CB`.

> **Regenerate that paragraph; never edit it.** `dk2tools status --check` exits 1
> with a list of what disagrees. Three of its figures are not obtainable by the
> obvious command: ADRs must be counted as distinct ids across `##` *and* `###`
> headings, because `ADR-0088` is recorded as a sub-heading and amendments are
> sub-headings too; question rows need `^\| Q<digits>[a-z]? \|`, because three
> table headers read `| Q | Question |` and `Q37a`, `Q37b`, `Q55x` and `Q118c`
> are real rows split off from an original; and the test figure comes from
> `dotnet test --list-tests`, which agrees with the runner exactly, where
> counting `[Fact]` attributes does not. Not wired into CI, because there is
> none — the commit-msg hook notes a stale line rather than blocking on it.

**M0 — closed 2026-09-07.** Flame (`D:\Flame`, pinned `f6e2a3d`) and OpenKeeper
(`D:\OpenKeeper`) both build locally; recipes in `original-reference/README.md`. Running DK2
*under* Flame is M5's first step, not M0's.

**M1 — closed 2026-09-07, ADR-0091.** All 18 record-type ids have typed readers. Editor
definition tables sit on `GameDatabase`; PLAYERS, THINGS, TRIGGERS and LEVEL are
level-scoped. The corpus audit covers 408 KWD/KLD files, all 66 LEVEL records, 6,318 THINGS
and 9,161 TRIGGERS, and byte-round-trips every record image (`KwdRecordRoundTrip`,
`KwdCorpusAudit`; re-checkable with `dk2tools kwd corpus`). GLOBALS is the honest zero:
v1.70 ships no type-0 chunk or LEVEL path row, so its LEVEL-reference representation has a
synthetic round-trip (Q220). The format work neither places things nor executes triggers;
those are M6.

**M2 — closed.** `GameSimulation.Tick()`, `StateHash()`, `WorldSnapshot`, the harness, and
the pinned literal. §6.5 has the caveat that no CI runs it.

**M3 — closed 2026-09-04.** Terrain work is one uniform data-driven rule — dig, mine,
claim, reinforce, repair, attack and take over a rival's room — exercised against the real
game's data, with markers and the first player command (`docs/research/tile-work.md`). An
imp finds the nearest job its keeper can reach, walks at the speed its own record gives and
works, on the priority order read from the binary (Q24, ADR-0018/0058); mined gold is
carried as the tail of a mining job rather than a fourth job (Q31, ADR-0019), and the
crowding cap of three imps to a wall face is built (ADR-0026/0030). Rooms, the treasury,
gold and mana are in. Unity 6 LTS renders campaign level 1 as coloured boxes through an
asset abstraction that resolves every key with no DK2 art installed (ADR-0034/0035/0036).

Three findings from M3 that changed the design rather than filling it in:

- **DK2 has no player gold counter.** The balance table sets four limits on gold and every
  one of them is per place — treasury tile, dungeon-heart tile, floor pile, creature's
  hands. So gold moved onto tiles and `PlayerState.Gold` was removed (ADR-0015), and three
  behaviours stopped being special cases: a keeper with no treasury cannot keep gold,
  destroying a treasury takes the gold with it, and a keeper's first treasury is necessarily
  paid for out of the dungeon heart.
- **Where a room may go is read from the room record's own placeable-on flags**, so nothing
  in the rules knows what a bridge is (ADR-0016).
- **Mana income is territory.** Three terrain types out of 41 generate any mana at all, and
  they are exactly the ownable non-room ones — so claiming ground raises income, losing it
  lowers it, and building a room on your own floor takes that floor off the payroll, none of
  which is written anywhere (ADR-0017). Two things the files could not settle came from the
  owner playing: the mana ceiling is a flat 200000 for every keeper and does not grow with
  the dungeon, and **a keeper has at most one dungeon heart, whose loss is defeat** — which
  turned the heart's flat 30 from unimplementable into a boolean. Defeat itself belongs with
  objectives at M6 (Q40).

**M4 — closed 2026-09-06, ADR-0082.** All seven items: portal spawn (ADR-0042/0047), lair
claim (ADR-0049/0060), sleep, hunger→hatchery (ADR-0066/0067), training (ADR-0083), payday
(ADR-0078/0079) and job selection with logged reasons (ADR-0082). Training pays at the
record's rate per original tick, caps at level 4, bills the keeper five gold a game second,
and a keeper who cannot pay loses the creature's job (Q205). That work also corrected the
experience model underneath it: a promotion **zeroes** the remainder rather than carrying
it, an imp earns four times what this project thought, and `ExpPerSecond` is *combat*
experience rather than a per-minute rate (Q79, Q127). Beyond the original list, M4 grew
death and corpses (ADR-0051/0063), anger and desertion (ADR-0053/0081), levels (Q119), the
Hand of Evil (ADR-0062), carrying (ADR-0063/0065), the prison (ADR-0064/0068) and fog of
war (ADR-0076).

**M5 — the rig works; the milestone is not closed.** The memory probe reads a
running DK2: it clocked the tick rate at 3.94/s with balance variable 19 read live as 4, and
read level 1's whole condition array out of the running game, settling Q233 (ADR-0102
amendments). **A comparer exists** — `dk2tools trigger-diff`, pairing rows by turn — and
level 1 has been captured from both sides and reported three times (`docs/EXPERIMENTS.md`).
Measured by event set over 1176 shared turns, ours against played run 5: **2 fire in both,
46 DK2 only, 2 ours only** (2026-09-09, after ADR-0113).

**Level 1 idle closed the first of those.** With the input removed rather than the player,
ours against DK2 is **4 events in both, 0 either side**, and the two flags we were calling
false positives are DK2's too (`docs/experiments/level1-idle-001`). Turn numbers agree to a
constant over a 568-tick span, so the rate is right. **The origin is load jitter, not a
rule** — timer 15's stamp across six captures is 4, 6, 7, 8, 10 and 11 — so a comparison
aligns on the first evaluation, which a timer's stored stamp gives exactly (Q272). One tick
of residual survives that and may be the probe rather than us (Q273). The ±156 to +285 of
two *played* runs was entirely the human.

**A second level now backs it, and closes one half of Q273.** Level 20 — reachable at all
only after `docs/research/campaign-progress-registry.md` — was picked because its idle run
stamps two timers early and then does nothing: a stamp is *stored*, so it cannot be blurred
by when the probe sampled. Over ticks 36–1218 **all 150 columns are identical**, and both
sides stamp timers 0 and 1 at tick **4** (`docs/experiments/level20-idle-001`). Absolute
tick numbering therefore carries no systematic offset, and the residual must live in the
sampled half.

**The 11-minute version closed it (`docs/experiments/level20-idle-002`).** Timer 1 restamps
inside that window, and a timer slot holds *the tick the game recorded when it acted*: DK2
stored **1724**, we store 1724, and the probe first saw it at **1725**. One event dates the
probe's lag against the game's own record. Applied to the three events it was not fitted to
— Level 20's `f6` and level 1's `f1` and `f34` — all three land exactly on ours, so **every
trigger event captured from DK2 on two levels now matches, with no residual left in the
simulation** (Q273 closed). The comparer gained the stamp sequences that made this readable
at all (ADR-0119).

The same run showed the limits of the level: with no player input, **heroes invaded** and
attacked the heart while **no trigger fired**, so Level 20 is quiet for only about seven
minutes, and its placed heroes act on an `Objective` byte nothing here reads (Q275).

One limit remains measured rather than assumed: **turn numbers are not reproducible between
two plays**, so a played report may be read for *which* events occur and never for when;
any future cell-by-cell scoring against a played capture will manufacture divergence. And
**both flags we invent are the absent player.** `f1` and `f34` each hang off a gate that
tests a flag action point 4 sets when the player tags a wall — DK2's run set it at turn 10,
ours never does. Level 1's own chain is gated the same way at its third node, so **no
condition work can close it**: it is a tutorial that advances when the human does the taught
action.

What remains: 20+ scenarios captured from both sides (three idle captures under
`docs/experiments/` and the packet replays of Levels 1–3, ADR-0339, so far), and running
DK2 under Flame, which §6.1 calls M5's first step and which has not been done.

**M6 — partial, and trigger execution is now the bulk of it.** Records parse (M1); a level's
placed creatures and objects reach the world (ADR-0101), the trigger flag and timer banks are
world state (ADR-0102), action points are ordered world state (ADR-0103), and doors, traps,
hero parties and effect generators are all stored, hashed and snapshotted (ADR-0106,
ADR-0108) — nothing in the THINGS corpus is unprojected. A recorded Level 1 play
now wins headlessly with timing differences (Q444, `navigation-captures.md`); interactive presentation
and broader campaign compatibility remain unfinished.

**Triggers execute.** Q228 settled the cadence — one evaluation per game turn, from the world
frame at `0x00526AA7`. The executor walks four trees (world, player, creature-owner fallback,
action point), answers actions from their dispatcher's return value (ADR-0107), and computes
about twenty condition types across the world, area and player contexts (ADR-0102, ADR-0105,
ADR-0109 … ADR-0116, ADR-0120, ADR-0121, ADR-0124). Campaign-wide the stall frontier is **53 distinct
condition records**, down from 941. World actions 44/45 now perform the saved anger and
notification gate writes, and the anger gate governs every simulated source (ADR-0160).

**But the frontier is spent, and that is measured** (ADR-0125). `dk2tools kwd unreached`
puts all **9,161** shipped records in one bucket each, from a real 600-turn run of every
level: **2,669 ran**, **3,832 closed** (walked to, condition answered honestly false),
**1,138** only under a **creature root**, **864** only under an unarmed **party** subtree,
**513 orphans** under no root at all — and **145 declined**, the entire frontier, **1.6%**.
**Rank what is left by records foreclosed — `gates`, not `stalls`** — the fourth ranking
this section has carried and the first that is the quantity wanted rather than a proxy:

| type | context | stalls | **gates** | what it waits on |
|---|---|---:|---:|---|
| 22 | player | 26 | **95** | kill attribution, so combat at M7 (Q276) |
| 28 / 27 | player | 5 / 2 | **18 / 3** | trap construction and an unknown door field (Q281/Q284) |
| 2 | player | 5 | **14** | the special timer id 16 (Q232) |
| 64 | **world** | 12 | **12** | party members no longer active; spawning and party identity (Q283) |
| 20 / 23 | player | 2 / 1 | 2 / 1 | `creature+0x28A` (Q269); kill attribution |

Five earlier claims were wrong and measurement said so each time: type 68 was named a
blocker and reaches nothing; type 64's 37 records deliver 12 stalls, in the *world* tree;
types 27 and 28 have an availability mode no shipped record uses; ranking by declines put
type 64 second when it is last; and 27 and 28 do **not** share a partition bit — 28 reads
trap `+0x10C` bit 5 and 27 an unidentified door dword at `+0x109` (Q284). Reading arms took the frontier **143 → 66** (ADR-0120)
and **66 → 59** (ADR-0121), then type 70's creature branch moved it to **53**
(ADR-0124) — but reach barely moved, which is the point.

**So the next reach does not come from condition arms** — finishing every one of them
moves 1.6%. The two structural gaps are sized: the **creature walker** is 1,138 records
(289 roots, its largest class, and the dispatcher chain is already documented), and
**action 14's creature creation** is 864 and also unblocks condition 64 (Q283).

**And an idle capture can never exercise more than about a third of the script** — over an
hour of game time, 3,145 of 9,161 records run. ADR-0126 says what closes the rest: 1,576
are cascade, 257 are dead in DK2 too, and the largest identifiable block is **696 records
waiting on the keeper to dig, claim and build — and the keeper they name is player 3, the
human one** (84 of 92 on type 40, 47 of 69 on type 41, 49 of 89 on type 42). That is not a
missing mechanism. **M5's next step is an input channel**, not more simulation:
`Tick(in InputFrame)` is half of one and nothing records DK2's own input to feed the
other half. ADR-0127 takes the recording side from DK2's **player task pool** rather than
from keyboard and mouse — a marked tile is an entry in a per-player list, so diffing it
gives `tag`/`untag` at a coordinate and a tick with no camera to reconstruct
(`player-task-pool.md`); `dk2watch.py --tags` writes it, and **runs 9–14 captured and closed it on 2026-09-10** — replaying the recorded input took DK2-only events from 3 to 0 twice (ADR-0131, `EXPERIMENTS.md`). What remains is rate, not input.
A computer keeper does not substitute for the recorded input: `DK2.AI.KeeperAI` follows
DK2's keeper as read (§1.2 row 5) but has not been compared with it on the same map
(Q497), so it would inject divergence that cannot be attributed.

**Types 22 and 64 are built.** Type 22 counts this keeper's creatures that another
keeper killed, from the heart's stamped killer (Q276, `PlayerState.KilledByPlayerId`);
type 64 subtracts the party's active creatures from its authored members now that
action 14 raises them (Q283, ADR-0122, ADR-0248). The actions carried out are the
sixteen in `docs/GOAL_ALIGNMENT.md` row 2.
**A level can now be won and lost** (ADR-0162, 2026-09-22): `WIN_GAME` and
`LOSE_GAME` set DK2's one-shot `Status`, and a heart whose health reaches zero
ruins, stamps its killer and loses or is conquered a turn later. The blow is combat's
(ADR-0163–0165) and what a defeat does to the loser's dungeon is built (ADR-0207, Q358).

**Deliberate ordering note:** M5 comes *before* breadth work. Building the comparison rig
early is what prevents accumulating a large body of plausible-but-wrong behaviour that is
expensive to unwind later.

---

## 9. Risk assessment

Severity: **C**ritical / **H**igh / **M**edium / **L**ow. Probability: as given.

| # | Risk | Sev | Prob | Mitigation / status |
|---|---|:--:|:--:|---|
| R1 | **Pathfinding cannot be reproduced.** No project has solved it; `Follow_Path` has no named fields. Movement feel is a large part of "is this DK2?" | **C** | High | **Retired**: DK2's navigation mesh and steering were read from the binary and built behind `IPathfinder` (ADR-0155, ADR-0157), and DK2's own route log agrees on all but a handful of requests (Q351, Q352). The residue is open questions, not a placeholder. |
| R2 | **Creature moods/anger unsolved.** OpenKeeper's is an empty stub after 12 years. | **H** | High | **Largely retired**: built from the binary and one measured ceiling (ADR-0053/0081). Remaining: happiness sources beyond the two a player can switch off. |
| R3 | **Licence contamination.** Flame has *no* licence; OpenKeeper is GPLv3. | **C** | Medium | §10.1, ADR-0041/0080. Fact sources, never code sources; provenance per mechanic; `FlameIdentifierTests` and `LicenseHeaderTests` enforce the shape. |
| R4 | **Scope collapse.** DK2 is enormous; motivation fails before the game is playable. | **H** | High | Milestones deliver something runnable early (M3). Vertical slices over horizontal breadth. Accept that M10 may never arrive and that M3–M6 is still a worthwhile artifact. |
| R5 | **Float non-determinism.** | **H** | Medium | Fixed-point for all gameplay maths (ADR-0001); pinned state hash from M2. |
| R6 | **The oracle is hard to instrument.** | **H** | Medium | **Retired**: a `PROCESS_VM_READ` probe against the running game works and has produced two findings (§6.1). |
| R7 | **KWD's unknown 10%** hides something load-bearing. | M | Medium | Every record image round-trips byte for byte and the corpus audit logs unconsumed ranges (ADR-0091). Cross-check OpenKeeper against dk2-tools `.bt` templates. |
| R8 | **Tick rate / timing model wrong**, invalidating all timing fidelity. | **H** | Medium | **Retired**: 4 ticks a second, read from the binary and observed live (ADR-0089, ADR-0102). Every relation between ticks and seconds is in `SimulationTiming` alone. |
| R9 | **Combat formulas unknown**; guessing produces a game that feels wrong. | **H** | High | **Largely retired**: melee, the attack spells, status effects, fear and the friendly arm are built from the binary (ADR-0163–0170), and the Level 1–3 packet replays compare fights with DK2's (ADR-0339). Open: the stand-ins Q376 names. |
| R10 | **Unity leaks into the simulation** over time. | **H** | Medium | `ArchitectureTests` reads the built assemblies' `TargetFrameworkAttribute` and references, so a Unity reference or a wrong target fails the build. |
| R11 | **Original assets unavailable to future contributors.** | M | Medium | `PlaceholderProvider` is total, and `AssetCatalogueTests` keeps it so. |
| R12 | **Computer-player fidelity incomplete.** | M | High | Binary audits are in `docs/research/computer-keeper-*.md`. M12 has a proactive keeper and captured observation; retained combat messages, the attack response, the battle step with all four battle spells, the retreat, the attack planner, task 7's bridges, the training moves and the Treasury, Lair and Hatchery room needs with expansion are built (ADR-0278, 0279, 0283, 0284, 0287, 0298, 0302, 0304, 0317, 0327, 0335); the mood and other room producers and the remaining reactive decisions remain. Close effect-specific UNKNOWNs before implementing their rules, and compare an active original keeper. |
| R13 | **Save format divergence** — our saves cannot be validated against DK2's. | L | High | Accept. Our save format is our own. `Dk2SaveReader` reads DK2's tile grid to cross-check design maps (Q182); `save_struct.bt` is a checklist of *what state exists*. |
| R14 | **Audio event semantics unknown** (40% understood). | L | High | Cosmetic. Defer entirely. |
| R15 | **Upstream projects change or disappear.** | M | Low | **Closed**: the whole Flame repository is cloned at `D:\Flame`, pinned at `f6e2a3d`, recorded in `original-reference/README.md`, and builds here — so the maps, the source and a working toolchain all survive upstream disappearing. The dk2-tools templates are still not vendored. |
| R16 | **AI-generated code silently invents mechanics.** | **H** | Medium | Confidence tags mandatory in docs and code comments; `[Dk2Behavior]` on the rule type. Any simulation constant not traceable to parsed data or a cited source fails review. |
| R17 | **Regression corpus becomes unmaintainable** as systems change. | M | Medium | Scenarios are declarative data, not code. Comparison tolerances explicit and versioned. |
| R18 | **Performance** — hundreds of creatures + pathfinding on a fixed tick. | M | Medium | Headless harness makes profiling easy. Fixed-point is cheap. **Never measured** (goal 9). |
| R19 | **Unity as the wrong long-term choice.** | M | Low | The simulation is engine-agnostic by construction; swapping presentation is weeks, not years. |
| R20 | **Multiplayer expectations** creep in early and distort the architecture. | M | Low | Out of scope until post-M10. Determinism happens to make it feasible later; that is a side effect, not a goal. |

**The two that decide the project's fate: R1 (pathfinding) and R4 (scope).**

---

## 10. Decisions required before implementation

Every decision this section asked for has been made. The rows below record which ADR made
it; the ADR carries the reason.

| Decision | Settled by |
|---|---|
| Project licence | **GPL-3.0-or-later** (ADR-0041, amending ADR-0003). Every source file carries `SPDX-License-Identifier: GPL-3.0-or-later`, enforced by `LicenseHeaderTests` rather than an analyser. |
| Clean-room policy | **In force** (ADR-0041). Under GPLv3 copying OpenKeeper's code would be lawful; it is still not done, because the brief asks for clean-room behavioural reimplementation. |
| Flame identifiers in the repository | **May be cited in a document, never in `src/`, `tests/` or `unity/`**, comments included (ADR-0080, `FlameIdentifierTests`). |
| Vendoring Flame's `mapping/*.map` as research data | **Not vendored, and better than vendored**: the whole repository is cloned outside the tree at a pinned commit (R15, §2.2.1). |
| Decompiler output | **Never enters the repository, anywhere** (ADR-0046). `reverse-engineering/` is gitignored except its README. |
| .NET version | net10.0 for tools, harness and tests; **netstandard2.1** for the eight Unity-loadable assemblies, from `src/Shared/UnityBound.props` (ADR-0002 as corrected, ADR-0036). |
| Unity version | **Unity 6 LTS `6000.6.0f1`** (ADR-0036). |
| Fixed-point format | **Q32.32, `long`-backed** (`Fp32`, ADR-0001). |
| Test framework | **xUnit**, with scenario tests generated from YAML (`ScenarioTests`). |
| Determinism gate | **Enabled from the first commit that had a `Tick()`** (ADR-0006). |
| Scenario file format | **YAML via YamlDotNet** rather than a hand-rolled subset (§6.2). |

### 10.1 Licensing policy — RESOLVED: GPL-3.0-or-later + clean-room (ADR-0041, amending ADR-0003)

Established facts: [KNOWN]

- **Flame** publishes **no licence** (GitHub API reports `"license": null`, re-confirmed
  against the clone). Default copyright applies: **no reuse rights whatsoever**, unchanged
  by this project being GPLv3.
- **OpenKeeper** is **GPLv3**.
- DK2 itself is EA-owned proprietary software.

The position in force:

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

Full boundary table: `docs/research/external-resources.md`. Attribution for adopted
OpenKeeper names: `THIRD-PARTY-NOTICES.md`.

### 10.2 Smaller decisions

Folded into the table above.

---

## 11. Immediate next steps

One item from the original list remains open; the rest are done. **For what to pick up
next, read `docs/HANDOFF.md` §"Next work", not this section** — it holds the current
queue, and `docs/RESEARCH-QUEUE.md` holds what the binary can settle without playing.

**Done — Flame symbol-map query tool.** `dk2tools sgmap` queries structures, fields and
globals at runtime without bringing the external map or its identifiers into the repository.
Its `at` query resolves a recovered structure offset to the deepest named field; `struct`,
`find` and `global` cover layout inspection and discovery. The map path comes from
`--sgmap`, `DK2_SGMAP`, or a sibling Flame checkout.

**Open — read the DK2 Official Editor Manual end to end** and write
`docs/research/editor-manual-notes.md` (no such note exists yet). This is EA's own statement of intended behaviour and
the cheapest high-quality source available. The 1999 *player* manual was read (ADR-0037,
`docs/research/manual-1999.md`); the editor manual was not.

Done: the GOG install (`Dk2Install.Find()`); the licensing question (ADR-0041, §10.1); the
repository; the Flame clone, pinned and building (§2.2.1); the WAD reader; the KWD/KLD
reader, all 18 record types (ADR-0091); `docs/research/game-database-dump.md`; and the
conflict register, which became `docs/research/open-questions.md`.

The five questions that list was seeded with, for the record: DK2's tick rate is **4 a
second** (ADR-0089); `CMapEntry`'s 11 bytes are still [UNKNOWN] against the KWD map;
`f3AF_workModifier` versus `f3B3_workEnhancement` is still [UNKNOWN];
`Follow_Path`'s 36 bytes held no path data — it is a stateless named behaviour, and DK2
routes on a navigation mesh (ADR-0155, Q5); and the job-selection order is
**confirmed for workers from the binary's own priority table** and is not OpenKeeper's
lair → sleep → eat → work (ADR-0058).

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

Every mechanic gets one file in `docs/research/`, budget 400 lines:

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
