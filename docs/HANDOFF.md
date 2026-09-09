# Handoff notes

Working knowledge that would otherwise live only in chat history, per
`AGENTS.md` ("Do not leave important knowledge only inside chat history").
Update it whenever a session ends or context is compacted. It is a reference,
not a log: state the current fact; git has the history. The dated "sitting"
narratives that used to follow these sections (2026-09-04 → 2026-09-07) were
removed on 2026-09-08 — `git show 233a068:docs/HANDOFF.md` has them, and every
decision, convention and DK2 fact they held now lives in `docs/DECISIONS.md`,
`docs/research/*.md` or a section below.

---

## Current state

### Counts — never hand-maintained; run these

| Figure | Command |
|---|---|
| Tests (and how many skipped without an install) | `dotnet test DK2.slnx` — `DK2_DIR=/nonexistent dotnet test DK2.slnx` is what CI sees |
| ADRs | `grep -c '^## ADR-' docs/DECISIONS.md`. The file is **not** in numeric order: ADR-0100 was taken out of sequence to reserve 0092–0099 against a worktree collision (ADR-0100), and 0092, 0101 and 0102 have since landed, so 0093–0099 are what is still reserved |
| Questions | the highest `Q` id — see the command below; `open-questions.md` is not in numeric order either, and rows are written both plain and bold-wrapped |
| `HashVersion` / snapshot `FormatVersion` | `grep -n 'HashVersion = ' src/DK2.Simulation/GameSimulation.cs; grep -n 'FormatVersion = ' src/DK2.Simulation/WorldSnapshot.cs` (kept equal) |
| Pinned state hash | `grep -n 'Assert.Equal(0x' tests/DK2.Simulation.Tests/DeterminismTests.cs` |
| Scenarios | `find scenarios -name '*.yaml' \| wc -l` |
| Research notes | `ls docs/research/*.md \| wc -l` |
| KWD record coverage | `dk2tools kwd corpus` (needs an install) |
| TRIGGERS coverage | `dk2tools kwd trigger-types` (needs an install) |

The question count needs a sort, because the table is neither numerically ordered
nor uniformly formatted:

```sh
grep -oE '^\| \*{0,2}Q[0-9]+' docs/research/open-questions.md \
  | grep -oE '[0-9]+' | sort -n | tail -1
```

### Milestones (plan §8)

| | Status | What closed it |
|---|---|---|
| M0 research baseline | ✅ 2026-09-07 | Flame (`D:\Flame`, pin `f6e2a3d`) and OpenKeeper (`D:\OpenKeeper`) both **build** locally; recipes in `original-reference/README.md` |
| M1 formats readable | ✅ 2026-09-07 | ADR-0091: all 18 KWD/KLD record-type ids parse; definition tables sit on `GameDatabase`, PLAYERS/THINGS/TRIGGERS/LEVEL are level-scoped; every record image round-trips byte for byte (`KwdRecordRoundTrip`, `KwdCorpusAudit`) |
| M2 deterministic skeleton | ✅ | `GameSimulation.Tick()`, `StateHash()`, `WorldSnapshot`, harness, CI gate |
| M3 dungeon prototype | ✅ 2026-09-04 | tile work, imps, rooms, gold, mana, asset layer, presentation boundary, Unity view renders campaign level 1 |
| M4 first creature loop | ✅ 2026-09-06 | ADR-0082: portal, lair, hunger→hatchery, sleep, training, payday, job selection for every creature |
| M5 oracle comparison | started, no comparison | the live-memory reader exists (`reverse-engineering/dk2watch.py`, gitignored, documented under "Running things") and has settled the tick rate at 3.94/s and read level 1's condition array. What is missing is the comparison itself: no expectation blocks, no comparer, no divergence report, and `docs/EXPERIMENTS.md` has no recorded runs |
| M6 real level loads | partial | records parse (M1); placed creatures, objects, action points, doors and traps reach the world (ADR-0101/0103/0106), and trigger state plus partial execution exist (ADR-0102/0105). Still missing: most trigger actions, hero-party/effect-generator storage, and objectives. No level is winnable |

### What exists (present tense; the ADR is the specification)

- **Tiles and work.** Dig, mine, claim, reinforce, repair, attack, convert-room, all from terrain flags (`TileWorkRules`, ADR-0009). Work lands once a tick, four a second, `delta × efficiency ÷ 4` (ADR-0089; `SimulationTiming`). Upward jobs cost the deficit (ADR-0089 reversing ADR-0024). Stance: on the tile for floor, beside it for a wall (ADR-0028); three to a wall face, one to a floor tile (ADR-0026/0030). A dug-out tile never closes and "filling in" is not a DK2 action — `TileWorkKind.FillIn` was deleted (Q219, `13a1f47`). Hostile expansion is direct claiming (ADR-0033). A tile nobody can own carries no owner (ADR-0045).
- **Rooms.** A room is a derived instance (`RoomInstance`, ADR-0027); placement from placeable-on flags (ADR-0016); cost per tile, sell for `RoomSellValuePercentageOfCost` = 50 (ADR-0029); a bridge reverts to the hazard it spanned and a wooden one over lava burns in 30 s (ADR-0031/0032); a room is claimed whole (ADR-0044). Behaviour exists for: Portal (ADR-0042/0047/0084/0086), Lair (ADR-0049/0060), Hatchery (ADR-0066/0067), Prison (ADR-0064/0068/0070–0074), Treasury and Dungeon Heart (gold), Training Room (ADR-0083), Library accrual only (ADR-0090), Temple prayer only (ADR-0082). Graveyard is a carry target (ADR-0063) and nothing more. Portal and heart centres block movement (`StructureBlocks`, ADR-0084).
- **Gold and mana.** Gold lives in tiles and in creatures' hands; no player counter (ADR-0015/0019); floor gold is an object an imp fetches first (ADR-0055/0056). Mana from owned land, ceiling 200000 flat, income cap `MaximumManaGainPerSecond` 500 (`ManaRules`, ADR-0017). Imps: free below `MinimumImpThreshold` every `TimeBeforeFreeImpGeneratedSeconds` as a deadline (ADR-0057), Create Imp 1500 × (imps − 4 + 1), 7 mana/s upkeep (ADR-0021/0022).
- **Imps.** Two-tier job choice (ADR-0058, `imp-task-priority.md`): floor gold, then marked excavation, claiming, reinforcing; gold near rather than last. Carrying: bodies to the graveyard, unconscious enemies to prison, objects back to shelves (ADR-0063/0064/0065). Aimed bonus is a task kind (ADR-0069).
- **Creatures.** Health and hunger (ADR-0048), own bed and healing asleep in it (ADR-0049), eating (ADR-0067), levels as a stat multiplier with experience from work/training/combat rates run through DK2's loader divide (ADR-0083, `SimulationTiming.LoaderRatePerTick`), anger as six counters banded on the maximum with 65533 saturation (ADR-0081; `NeverAngered` flag, ADR-0087), desertion, death as a 60-second window then a corpse (ADR-0051), payday as a journey (ADR-0078/0079), job selection by three mood choosers gated on five game seconds (ADR-0082), Hand of Evil pick-up/drop/slap of creatures and things (ADR-0062/0075/0077), knock-out at one sixth health (ADR-0071), water/lava hazard damage from `TerrainDef.Damage`.
- **Players.** `PlayerIds`: 0 unowned, 1 hero side, 2 neutral (an unclaimed room), 3–7 keepers (ADR-0043); only a keeper is staffed (ADR-0052). `DK2.AI.KeeperAI` is an **input source** that emits `PlayerCommand`s (step 1 of `docs/KEEPER_AI.md` §12.7): digs to gold, sites and places rooms in the shipped order; no simulation surface changed.
- **World.** Fog of war is simulation state, six-tile perception radius, one discovery bit per keeper in ours (ADR-0076; DK2 stores one bit per machine — `fog-of-war.md` §10). Levels play on their own VARIABLES over the global table (ADR-0040). Determinism gate: FNV-1a state hash, one pinned literal, `ChaosSystem` (ADR-0006).
- **A level's placed THINGS.** `ThingProjection.Place(world, database, things)` (ADR-0101/0103/0106) places creatures, objects, action points, doors and traps in file order and reports counts. ROOM records are already in the MAP grid; hero parties and the one effect generator remain `NotYetProjected`. Door/trap state copies every parsed field verbatim and has no behaviour (`doors-and-traps.md`). Coordinates are **bare tile indices**, `InitialHealth` is a percentage, and `RoomThing.RoomType` is a terrain id. Pinned against every shipped level by `PlacedThingsOnRealDataTests`; hashed state is version 46.
- **Trigger execution.** `TriggerExecutor` walks the world tree, the 24 party roots, every player's root and every action point's root once per game turn, in the frame's own order (Q239, `trigger-walkers.md`); it runs action types 7, 8 and 14's arming half, and evaluates condition types 1, 2, 34 plus the area context's 40, 41, 42, 62 and 69 (ADR-0105). Every other **action** returns what DK2's dispatcher returns and is counted in `Partial` or `Unhandled` (ADR-0107); every other **condition** is `Blocked`, except the eight the player dispatcher answers false from its own epilogue (ADR-0109). Nothing is silently false. **Trigger flags and timers.** `TriggerState` on `WorldState` (ADR-0102, `trigger-state.md`): 128 flag bytes, 16 timer dwords, one opaque special value — DK2's own banks sit at `+0x3F70` and `+0x3FF0`, `0x80` apart, which is what fixes the flag bank at 128, and the timer bank is bounded at sixteen three independent ways. **Level-global, not per-player**, settled from the binary before it was written (the player-context dispatcher `0x004C8620` holds a player as its own `this` and still loads the global). Hashed and snapshotted. `TriggerConditionEvaluator` evaluates one tag-213 type-1 flag or ordinary type-2 timer record, including indirect modes and authored seconds; unsupported types and the UNKNOWN timer id 16 are `NotEvaluated`, not false. It does not walk the tree or run actions. Q228 establishes the once-per-turn cadence; `trigger-tree.md` establishes traversal and repeat retirement. Parameter offsets, widths and signedness come from DKII.EXE reads, not editor labels (ADR-0092, `trigger-semantics.md`).
- **Data.** WAD, KWD/KLD (18 record types; per-field file layout in `kwd-record-fields.md`), STR string tables, SDT sound banks, KMF models, textures; `Dk2SaveReader` reads the tile grid of a `.SAV`/`.QSAV` (grid located by correlation, `FindGridOffset`). `DK2.Convert` writes glTF/GLB and PNG (`dk2tools convert`).
- **Presentation.** `AssetRef` → `AssetProviderChain` (replacement → converted → placeholder, total); `WorldView(database, viewingPlayerId)` is the one translation to drawables; `TextRenderer` writes `world.txt` per scenario; `ScenePresenter` diffs and pushes through `IRenderTarget`; `PlayerInputTranslator` turns renderer-free gestures into all eight existing commands and `TilePicker` says which tile a pointer ray is over; Unity 6 LTS `6000.6.0f1` supplies the input/camera shim and renders campaign level 1 as boxes (ADR-0034/0035/0036/0104).

### What does not exist

Combat (Q8), heroes and hero parties, every keeper spell but Create Imp, door and trap behaviour (their state exists; Q249–Q253), the creature trigger walker (the other three run; `trigger-walkers.md`), most trigger action types and 39 of the 46 shipped condition types — `dk2tools trigger-run` prints the census of what a level asked for and did not get, objectives, win/lose/defeat (a fallen heart is not claimable — Q207/Q93), the research tree (accrual only), Workshop/Casino/Torture/Guard Room/Combat Pit behaviour, Temple beyond prayer, Graveyard beyond receiving corpses, wandering (Q41), vampire reincarnation (Q111), the pathfinder is an admitted placeholder (ADR-0012, Q5). Unity: `DungeonScene` has no branch that loads the converter's glTF/PNG (Unity ships no runtime glTF importer); boxes rather than DK2's walls (Q71).

### Standing caveats — each still true on 2026-09-08

- **One deliberate departure from shipped data:** `DigRockHealth` −5000 is corrected to −500 because the owner measured two swings to dig a wall (ADR-0023, Q20). `ObservedCorrections` is the only place such a number may live, and lists the five conditions for adding one.
- **Portal interval** `(n² + EntranceGenerationSpeedSeconds)` s with `n` this portal's own tally (ADR-0086, stopwatch-confirmed); whether the tally is ever zeroed is unread (both zero-stores are vtable-only). A worn portal lost and re-claimed is the missing observation.
- **Fill-in of a claimed path completes in one application** because Claimed Path ships at 660 of 660 and the read rule converts at `MaxHealth` (Q215: DK2's guard is unread; disassemble `0x00453058` onward). Unreachable in ours since `FillIn` was deleted, so no imp can do it.
- **The determinism pin spawns no creatures** (Q206): `TestWorld.World` places none, so creature-state changes move the pin through `HashVersion` alone.
- **A trigger timer's unit depends on where you read it** (ADR-0102 amendments): the `.kld` holds **seconds**, DK2's level trigger initialiser multiplies by variable 19 in place at load (`0x0050C2DB` in `FUN_0050B5B0`, condition types 2 and 34, action types 16 and 23), the comparison consumes **ticks**, and a save game would contain the scaled value. Confirmed by reading level 1's condition array out of the running game: all 22 timer thresholds held the authored value times four. Ours takes seconds and converts in `HasElapsed`; when the `.kld` trigger loader is written the conversion must happen exactly once — there or at the comparison, never both.
- **Install-gated tests skip, they do not pass** (ADR-0100, `tests/DK2.TestSupport/InstallGate.cs`): `[InstallFact]`/`[InstallTheory]` set `Skip` when `Dk2Install.Find()` is null; `InstallGateTests` fails the build if a `return`-on-null reappears; `DK2_REQUIRE_INSTALL=1` turns a missing install into a failure. CI (`.github/workflows/ci.yml`, Linux + Windows) sets `DK2_DIR: /nonexistent`, so every real-data test is reported skipped there.
- **Simulation speed has never been measured** (goal 9, `GOAL_ALIGNMENT.md`).
- **Timing is DK2's own**: `TicksPerSecond = 4`, a tick is 250 ms (ADR-0089), now **observed** as well as read — a memory reader attached to a running DKII-dx.EXE clocked 3.94 ticks/s over two runs with variable 19 read live as 4 (ADR-0102 amendment). `TicksPerSecond` (ours, refinable) and `OriginalTicksPerGameSecond` (DK2's, a fact) are equal and kept separate on purpose. Every tick count in a scenario log is a wall-clock claim; several scenario budgets are longer than their authors intended.
- **Reference install:** `E:\GogGames\Dungeon Keeper 2` (GOG; `DKII.EXE` MD5 `2f3bbc21eae8c3aa468136baa303036d`, = v1.70 STRONGLY INFERRED, Q10), resolved by `DK2.Data.Dk2Install`; `DK2_DIR` is authoritative when set — an invalid `DK2_DIR` means *no install*, no fallback.

---

## Where the code lives

Layering: `DK2.Core` ← `DK2.Data.Model` ← `DK2.Data` / `DK2.Simulation` ← `DK2.Pathfinding`, `DK2.Assets`, `DK2.Presentation`, `DK2.AI`, `DK2.Convert` ← `DK2.Harness`, `DK2.Tools`, Unity. Eight projects import `src/Shared/UnityBound.props` and build netstandard2.1 (ADR-0002, enforced by `ArchitectureTests.UnityLoadableAssemblies_TargetNetStandard21`). `docs/ARCHITECTURE.md` has the full layering.

| Project | Holds | Key types |
|---|---|---|
| `src/DK2.Core` | primitives, no game rules | `Fp32` (fixed point), `GameTick`, `TilePos`, `Hash64` (FNV-1a), `SimRandom` (xoshiro256\*\*, ADR-0004), `Confidence` + `Dk2BehaviorAttribute` |
| `src/DK2.Data.Model` | immutable definitions, no IO — the data/simulation seam | `GameDatabase` (indexer `this[VariableTypeId]` **throws** on a missing variable; `ForLevel`), `GlobalVariables` (`MergedWith`, `CreatureStats`), `VariableTypeId`, `TerrainDef`, `RoomDef`, `RoomTypeId`, `CreatureDef`, `CreatureEnums` (`CreatureFlags`, `CreatureFlags2`, `JobType`), `CreatureStatId`, `ObjectDef`, `DoorDef`, `TrapDef`, `KeeperSpellDef`, `PlayerDef`, `KeeperAIParameters`, `PlayerIds`, `LevelMap`, `KwdRecordType`, `KwdAdditionalRecords` (`CreatureSpellDef`, `ShotDef`, `EffectDef`, `EffectElementDef`, `ThingRecord` subclasses, `TriggerRecord`, `LevelDef`), `ThingTypeId` |
| `src/DK2.Data` | readers for the shipped files | `Dk2Install`, `GameDatabaseLoader` (`LoadForCampaignLevel`); `Kwd/`: `KwdReader`, `KwdChunk`, `KwdVariables`, `KwdCreatures`, `KwdTerrainRooms`, `KwdMap`, `KwdObjects`, `KwdDoors`, `KwdTraps`, `KwdKeeperSpells`, `KwdPlayers`, `KwdTaggedRecords`, `KwdAdditionalRecords` (`KwdThings`, `KwdTriggers`, `KwdLevel`), `KwdRecordRoundTrip`, `KwdCorpusAudit`, `KwdTriggerCorpusAudit`; `Wad/`: `WadArchive`, `WadDecompressor`, `WadEntry`; `Save/`: `Dk2SaveReader`, `SavedGame`, `SavedTile`; `Text/`: `Dk2StringTable` |
| `src/DK2.Simulation` | all game rules and mutable state | `GameSimulation` (loop, `StateHash`, `HashVersion`), `WorldState` (`TileState`, `PlayerState`, `FromMap`), `WorldSnapshot` (`FormatVersion`), `InputFrame`/`PlayerCommand`, `SimulationEvents` (write-only), `SimulationTiming`, `ISimulationSystem`/`SimulationContext`; rules: `TileWork` (`TileWorkKind`, `TileWorkRules`), `WorkStance`, `ImpCrowding`, `ImpTaskKind`, `CarryRules`, `ObjectCarryRules`, `GoldStore`, `FloorGold`, `RoomRules`, `RoomInstance`, `RoomIndex`, `BridgeRules`, `StructureBlocks`, `ManaRules`, `ImpSupplyRules`, `PortalRules`, `LairRules`, `HatcheryRules`, `EatingRules`, `PrisonRules`, `ResearchRules`, `TrainingRules`, `PaydayRules`, `HandRules`, `JobRules`, `Efficiency`, `CreatureState`, `CreatureHealthRules`, `CreatureDeathRules`, `CreatureLevelRules`, `CreatureAnger`/`CreatureAngerRules`, `ObjectState`, `ObservedCorrections`, `WorldPathGrid`, `ThingProjection` (+`ThingProjectionResult`), `TriggerState` (+`FlagWriteMode`); `Systems/` (one class per system; order declared once in `SimulationSystems.Default()`, ADR-0013): `TileTagSystem`, `TileWorkSystem`, `ImpTaskSystem`, `CreatureMovementSystem`, `RoomBuildSystem`, `RoomSellSystem`, `BridgeBurnSystem`, `ManaSystem`, `ImpSupplySystem`, `PortalSystem`, `HandSystem`, `LairSeeker`, `CreatureSleepSystem`, `CreatureHealthSystem`, `CreatureEatSystem`, `HatcherySystem`, `CreatureAngerSystem`, `CreatureJobSystem`, `CreatureExperienceSystem`, `PaydaySystem`, `PrisonSystem`, `PrisonerWanderSystem`, `PrisonerEatSystem`, `FogOfWarSystem`; `Commands/`: `TileCommands` (`TagTilesCommand`), `RoomCommands` (`BuildRoomCommand`), `ImpCommands`, `HandCommands` |
| `src/DK2.Pathfinding` | the Q5 placeholder behind an interface (ADR-0012) | `IPathfinder`, `GridPathfinder` (eight-connected, √2 diagonals, ADR-0025) |
| `src/DK2.Assets` | logical asset keys and resolution (ADR-0034) | `AssetRef` (`[a-z0-9_]`, the only validator), `IAssetProvider`, `AssetProviderChain`, `PlaceholderProvider` (total), `FileAssetProvider` (the only type that turns a key into a path), `AssetCatalogue` |
| `src/DK2.Presentation` | world → drawable and intent → command, renderer-independent (ADR-0035/0104) | `WorldView` → `TileView`/`CreatureView`, `PlayerInputTranslator`, `TilePicker`, `TextRenderer`, `IRenderTarget` (nine methods, the Unity seam), `ScenePresenter` |
| `src/DK2.AI` | the computer keeper as an input source (`docs/KEEPER_AI.md` §12) | `KeeperAI`, `KeeperAIState`, `KeeperView`, `KeeperTask`, `RoomPlanner`, `SiteFinder`, `GoldScanner`, `LineWalker`, `KeeperDirections` |
| `src/DK2.Convert` | original art → modern formats | `AssetConverter`, `Dk2ArtCatalogue`, `KmfReader`/`KmfModel`, `GlbWriter`, `Dk2TextureDecoder`, `EngineTextureArchive`, `TextureExtractor`, `PngWriter`, `SdtArchive` |
| `src/DK2.Harness` | headless scenario runner | `Program` (`list`, `run`), `Scenario`, `ScenarioRunner` |
| `src/DK2.Tools` | `dk2tools` CLI, one file | `Program.cs` |
| `src/Shared` | `UnityBound.props`, `Polyfills.cs` (netstandard2.1 shims) | |
| `unity/DK2.Unity` | the adapter | `Assets/DK2/DungeonScene.cs` (`IRenderTarget` in GameObjects), `DungeonRunner.cs` (`MonoBehaviour`, input and camera shim), editor `DungeonSetup` (`SmokeTest`), committed `Dungeon.unity` |
| `unity/DK2.Unity.Compile` | compiles the adapter against Unity's module assemblies; **outside `DK2.slnx`** | |

| Test project | What it pins |
|---|---|
| `tests/DK2.Core.Tests` | `LicenseHeaderTests` (SPDX notice on every source file, ADR-0041), `FlameIdentifierTests` (no Flame-shaped identifier in `src/`, `tests/`, `unity/`, ADR-0080), `Hash64Tests`, `SimRandomTests`, `TilePosTests` |
| `tests/DK2.Simulation.Tests` | one file per mechanic; `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` holds the pin; `ArchitectureTests` (no Unity reference, netstandard2.1); `TestWorld` (synthetic database + `ChaosSystem`, no DK2 numbers); `Dk2TerrainFixture` (DK2's real numbers transcribed so rules test in CI) |
| `tests/DK2.Behavior.Tests` | `*OnRealDataTests` — the same rules against the shipped install (drift guards for every fixture); `ScenarioTests` runs every YAML; `CampaignLoadTests`; `InstallGateTests` |
| `tests/DK2.Data.Tests` | parsers; `KwdRecordLayoutTests` pins every record stride against the install; `Dk2SaveReaderTests`; `GlobalVariablesMergeTests` |
| `tests/DK2.Assets.Tests`, `DK2.Presentation.Tests`, `DK2.Convert.Tests`, `DK2.AI.Tests` | as named; `AssetCatalogueTests` proves the placeholder is total over the whole shipped database |
| `tests/DK2.TestSupport` | `InstallGate.cs`: `Dk2InstallGate`, `InstallFactAttribute`, `InstallTheoryAttribute` |

Reference material: `docs/DECISIONS.md` (ADRs), `docs/research/*.md` (one note per mechanic, template in `DK2_REIMPLEMENTATION_PLAN.md` Appendix B), `docs/research/open-questions.md`, `docs/research/observation-checklist.md` (what the owner can settle by playing), `docs/RESEARCH-QUEUE.md` (what the binary can settle without playing, and the Ghidra headless command), `docs/RESEARCH_STATUS.md`, `docs/EXPERIMENTS.md`, `docs/KEEPER_AI.md`, `docs/GOAL_ALIGNMENT.md`, `docs/CODEX_TASKS.md` (briefs for a second agent), `docs/architecture/simulation-loop.md`, `reverse-engineering/README.md` (the quarantine rule and Ghidra method; the folder's scripts are committed, its exports are not), `original-reference/README.md` (Flame/OpenKeeper build recipes; the 1999 manual PDF), `unity/README.md`, `THIRD-PARTY-NOTICES.md` (OpenKeeper/GLKeeper/YamlDotNet attribution).

---

## Running things

```
dotnet build DK2.slnx                       # one solution; `dotnet build` takes one project, not several
dotnet test  DK2.slnx
DK2_DIR=/nonexistent dotnet test DK2.slnx   # what CI sees: install-gated tests report Skipped
DK2_REQUIRE_INSTALL=1 dotnet test DK2.slnx  # a missing install fails instead of skipping

dotnet run --project src/DK2.Harness -- list
dotnet run --project src/DK2.Harness -- run scenarios
dotnet run --project src/DK2.Harness -- run scenarios/smoke/determinism_flat_map.yaml [--out <dir>]
```

Harness output lands in `build/scenario-runs/<id>/` (gitignored): `report.md`, `hashes.tsv`, `events.log`, `world.txt`. Scenario format, map kinds (`flat_WxH`, `rock_WxH`, `chamber_WxH`, `campaign:<level>`), `fog_of_war`, `viewing_player`: `scenarios/README.md`. `setup.placed_things: true` places the level's own THINGS before tick 0 — opt-in, off by default, meaningful only on a `campaign:` map, and documented in ADR-0101 rather than in `scenarios/README.md`.

**`dk2tools`** = `dotnet run --project src/DK2.Tools --`. Everything under `kwd` needs an install except where a file path is given. Subcommands present in `src/DK2.Tools/Program.cs` (the built-in `usage` text lists only some of them):

| Group | Subcommands |
|---|---|
| install / assets | `where`; `convert --out <dir>`; `wad list <file>`, `wad extract <file> <entry> [out]`; `sdt list <file>`, `sdt extract <file> [--out <dir>]`; `str list <file>`, `str get <file> <id>` |
| tables | `kwd vars [file.kwd]` (`-a` on a level's `*Variables.kld` shows its overrides), `kwd terrain`, `kwd terrainflags`, `kwd terrainmana`, `kwd rooms`, `kwd roomflags`, `kwd creatures`, `kwd creature <name>` (prints a `raw:` line per `Fp32`), `kwd objects`, `kwd research` |
| maps | `kwd map <mapfile> [terrainId]`, `kwd mapbytes <mapfile> [byte] [n neighbours]`, `kwd portals` (every portal in all 66 maps; player 2 is the unowned marker, 7 the Heroes), `kwd alterterrain [-v]` |
| containers / M1 | `kwd chunks <file>`, `kwd tagged <file>`, `kwd records <file>`, `kwd corpus` (parse + round-trip every shipped KWD/KLD, and print the 18-type coverage) |
| placed entities / triggers | `kwd things [file]` (one level's placed THINGS with the fields the projection reads; **with no argument** it sweeps every `*Things.kld` and prints the corpus distribution and coordinate range), `kwd trigger-types` (every tag/type pair, its count and the levels holding it, across the whole corpus), `kwd triggers <file> [tag] [type]` (one level's records with their links, decoded parameters and one level of children — the only view that shows a *tree*), `kwd trigger-params <tag> <type> [parameterName]` (the distribution of one named parameter over every shipped record of that tag and type — how the timer-unit question was framed) |
| offset hunting | `kwd recscan <file> <value> [rec]`, `kwd creaturescan <name> <value>`, `kwd creaturehex <offsetHex> <count>`, `kwd objecthex <offsetHex> <count> [-w]`, `kwd creaturebits <name[,name]> [file]`, `kwd fieldsweep <creatures\|objects>` (fields that never vary), `kwd varsweep` (every VARIABLES record in the corpus) |

**Unity** (nothing under `unity/` is in `DK2.slnx`; CI has no Unity):

```
powershell -File unity/sync-assemblies.ps1                    # after ANY change under src/ — copies the netstandard2.1 DLLs into Assets/Plugins/DK2 (gitignored)
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj  # compiles the adapter against Unity's real module assemblies; -p:UnityManagedDir=... if the editor is elsewhere

"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics -projectPath unity\DK2.Unity -logFile build\unity-batch.log
"C:\Program Files\Unity 6000.6.0f1\Editor\Unity.exe" -batchmode -quit -nographics -projectPath unity\DK2.Unity -logFile build\unity-smoke.log -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest
```

The smoke test loads campaign level 1 (30x38) through Unity's Mono runtime, presents it, ticks 20, presents again, and fails unless there is one GameObject per tile (1140 tiles, 1178 assets all placeholders); it exits 0 with no install. Interactive: open `unity/DK2.Unity`, put `DungeonRunner` on a GameObject, set Install Directory or `DK2_DIR`, and Play. The interactive runner projects the campaign's THINGS through `ThingProjection`; the older smoke test still exercises terrain only. Full controls and notes: `unity/README.md`.

**Ghidra** (headless, project `D:\ghidra projects\DK2_RE`, outside the repo): the command is in `docs/RESEARCH-QUEUE.md` §"How the binary work is done here"; the rule is `reverse-engineering/README.md` and ADR-0046. The scripts (`ScanAll.py`, `CallScanAll.py`, `CopyPairs.py`, `AtAndCallers.py`, `Up.py`, `Intersect.py`, `CallSlot.py`, `ReadsAt.py`, `MaskScan.py`, …) and every export live in `reverse-engineering/` **on this machine only** — `.gitignore` excludes `reverse-engineering/**`, and only its `README.md` is tracked. `git status --short reverse-engineering/` must be empty before every commit.

**Live memory reader** (same directory, same gitignore): `python reverse-engineering/dk2watch.py --seconds 300 --csv D:\run.csv`. Attaches to `DKII-dx.EXE` **or** `DKII.EXE` with `PROCESS_VM_READ` only — the two exes differ in 1126 of 2,987,568 bytes and none is in `.text`, `cseg` or `.data`, so every address is valid for both. It prints the cached world pointers, variable 19, the record counts and every type-2 timer threshold, then samples the flag bank, timer bank, tick counter and root trigger id at the tick rate into a 150-column CSV. **Load the level first**; it waits for a real level, because the front end is itself one (1 condition, 1 action) and a non-null pointer is not proof. It accumulates the tick counter across a level load, which resets it. Fullscreen is fine — DK2 has no windowed switch and DDrawCompat's borderless mode crashed level load here.

---

## Next steps

Agreed order as of 2026-09-08 (M0–M4 closed; the research queue's head is clear). Every item is checked against the tree, and nothing listed here is built except where an item says which part of it already is.

**Features, cheapest evidence first**

1. **Q209 — DK2's tile-walkability predicate for objects.** The workshop's anvil, the library's bookcases, the casino's tables and the graveyard's tombstones all block tiles in DK2 and none does here; `StructureBlocks` reproduces only the portal hole and the heart's 3×3 (ADR-0084). The obvious object-flag rule is refuted by the data (`Obstacle` is on every creature bed). Read it from the pathfinder side.
2. **Defeat.** A fallen dungeon heart needs a state: `TileWorkRules.Applies` excludes the heart from `ConvertRoom` unconditionally and a *fallen* heart should be claimable (Q207's remaining half, Q93). Q204's `player+0x76 & 4` (restricts a keeper's creatures to Leave and Rebel) is the candidate flag. Q40 describes what defeat does. Needs objectives, so it sits with M6.
3. **M6 — the rest of THINGS, and trigger execution.** Placement and trigger *state* landed on 2026-09-08 (ADR-0101, ADR-0102); what is left, in the order the evidence allows:
   - **Trigger execution.** Single-record type-1 flag and ordinary type-2 timer conditions evaluate. Q228 established that the root evaluator runs once per game turn; `trigger-tree.md` now records its link order and repeat retirement. The executor and all action effects remain to be built; Q226/Q227 are parameter meanings they still lack, and Q229/Q232 are live mechanisms that must not be named before their consumers are read.
   - **Action points as world state** (Q222, now a build task rather than a question: action types 16, 17, 27 and 31 write an `Available` byte against an `ActionPointId`, read from the binary, so availability belongs in the world hash). 1152 are placed across the campaign.
   - **Door and trap behaviour** (Q249–Q253): their verbatim ordered state is built (ADR-0106); opening, locking, blocking, arming, firing and field transforms are UNKNOWN.
   - **Hero parties and effect generators**, which need combat and the presentation layer respectively.
   - Level scripting is enumerated: 3,753 generic records over 46 types and 5,408 action records over 38 types in the shipped corpus (`trigger-semantics.md`, `dk2tools kwd trigger-types`); `RESEARCH_STATUS.md` counts 51 action types including those the corpus never uses (Q225). Q220/Q221 remain the two open record-layout questions, Q223 the ROOM-owner disagreement.
4. **Rooms still without behaviour:** Workshop (manufacture, category 5 hook), Casino, Torture Chamber and conversion (category 15's hook can kill on conversion at a zero rate, ADR-0085), Guard Room, Combat Pit (including the fallback DK2 gives a creature that has outgrown the training room — ours refuses it and ejects a capped trainee one five-second gate late), Temple beyond prayer (sacrifice; `PercentageOfManaGainedBySacrificing` 0 **doubles**, ADR-0085 amendment), Graveyard (1000 life-force → Vampire, `CreatedCreatureId` 8; reincarnation Q111 was deferred until levels existed — levels exist now).
5. **Rebel** (`CreatureAngerSystem` implements Leave only; `RebelLeavingPercentage` 70 / `RebelBecomeGoodPercentage` 15 / `RebelDefectPercentage` 15 are read by nothing).
6. **Combat** (Q8) — the largest missing mechanic, the first to subtract health per tick, and M7's head. Heroes and hero parties follow it.
7. **Computer keeper, `docs/KEEPER_AI.md` §12.7 steps 2–5:** an `IWorldView` facade (no hash change); §12.3's missing commands and §12.4's messages (each bumps the hash and wants an ADR); threat evaluation, attack planning, Call to Arms, traps, prisoners, retreat; `KeeperAIPersonality`. §12.8's four deviations (own RNG stream, flood-fill connectivity, Chebyshev range, batched room placement) each want an ADR if they survive.
8. **M5 — the oracle rig.** DK2 can run under the Flame built on 2026-09-07 (back up `PATCH.dll` first); plan §2.2.1 item 4 (a Ghidra import for the `sgmap`) and §11 item 5 (an `sgmap` query tool) no longer wait on anything. `docs/EXPERIMENTS.md` has no recorded run.
9. **Unity:** loading the converter's glTF/PNG (Unity ships no runtime glTF importer); DK2's wall pieces instead of boxes (Q71/Q72, `wall-rendering.md`).

**Known "ours, not DK2's" left in the code, each flagged at its site**

- Treasury deposit order is tile-index order (`GoldStore`; Q35's withdrawal half unread).
- Which lair tile a bedless creature walks to, and the 12-tick idle cadence (Q157; `lair-acquisition.md` §7).
- Task stealing: a nearer imp does not take a task from a farther one (Q155; DK2 does).
- `ManaSystem` runs for every player, not only keepers (deliberate, ADR-0052).
- `RoomRules` does not model a level's per-room `Availability` (ADR-0040 notes the gap; only `PortalRules` reads availability).
- The portal tally is never zeroed (ADR-0086; DK2's reset is vtable-only and unread).
- `HandSystem.Drop` gates by `PlayerIds`/worker rule (ADR-0077) but the object-drop gate of Q196 is the input layer's.

**Housekeeping**

- The pin's empty world (Q206): give `TestWorld.World` a creature so `HashInto` changes fail the pin directly.
- Scenario tick budgets were written against 20 ticks/s and now cover five times the game time; trim them.
- A `dk2tools status` subcommand that prints the counts (plan §8 asks for it) so no document carries a hand-edited figure.
- No `README.md` at the repository root.
- Re-run the zero-value audit (ADR-0085's method) whenever a new record type gains a reader; it has been run on the balance table, the creature record and the object record (Q213), not on terrain or rooms.
- Simulation speed has never been measured (goal 9).

---

## Decisions already made — do not relitigate

Index only; the ADR is the argument. Where a later ADR reverses an earlier one, the earlier entry says so.

| ADR | Decision |
|---|---|
| 0001 | Integer/fixed-point (`Fp32`) for gameplay maths; `float` is presentation-only |
| 0002 | Simulation independent of Unity, netstandard2.1, no Unity reference — enforced by `ArchitectureTests` on the built artefact |
| 0003 | Clean-room use of reference projects (MIT struck; see 0041). Amended: OpenKeeper (GPLv3) may be reused with attribution in `THIRD-PARTY-NOTICES.md`, but the standing practice is reference-and-compare, and its implementation is its guess, never evidence (Q86). Flame: facts only, no identifiers |
| 0004 | xoshiro256\*\* is the simulation RNG (`SimRandom`) |
| 0005 | `.slnx` solution format, .NET 10 |
| 0006 | Determinism gated by one pinned state hash (FNV-1a, RNG position included), exercised by a test-only `ChaosSystem` |
| 0007 | Our own save format (`WorldSnapshot`); DK2 save compatibility is not a goal (the `.SAV` reader is research tooling) |
| 0008 | YamlDotNet for scenario files, tooling only |
| 0009 | Tile transitions are triggered by the work kind, not by the health level — the level-triggered reading is refuted by the data |
| 0010 | Work orders are a FIFO queue on `WorldState`; order is behaviour — never sort or deduplicate it |
| 0011 | Ticks relate to seconds in `SimulationTiming` and nowhere else |
| 0012 | The pathfinder is an admitted placeholder behind `IPathfinder` |
| 0013 | One declared system order, `SimulationSystems.Default()` |
| 0014 | Worker behaviour covers marked work only (superseded in scope by 0018/0058) |
| 0015 | Gold lives in tiles; a player has no gold counter; `GoldStore` is the only way gold moves |
| 0016 | Where a room may go is the room's own placeable-on flags; nothing knows what a bridge is |
| 0017 | A `…PerSecond` balance rate is applied in whole, once a game second |
| 0018 | Imps take jobs in a strict CONFIRMED order — excavation, claiming, reinforcing (refined by 0056/0058) |
| 0019 | A creature's hands are a place gold can be (`GoldHeld`, bounded by `maxGoldHeld`) |
| 0020 | Work lands a fifth of the tabled delta at a time (273 gold a swing). **Amended by 0089**: the fifth is the size, `delta × 0.8 ÷ 4`; the cadence is four a second |
| 0021 | Imps come from a floor (`MinimumImpThreshold`) and a price (Create Imp, escalating, falls back when imps die) |
| 0022 | An imp costs 7 mana/s to keep, read off the Create Imp spell record (`KeeperSpells.kwd` 0x00E2; cast cost 0x11C) |
| 0023 | One measured behaviour overrides one shipped number: `DigRockHealth` −5000 → −500 (`ObservedCorrections`, five conditions) |
| 0024 | ~~An upward job costs the terrain's whole `MaxHealth`~~ — **reversed by 0089**: it costs the deficit |
| 0025 | Movement is eight-connected; a diagonal costs √2 (octile) |
| 0026 | Three workers to a wall face; the fourth goes elsewhere |
| 0027 | A room is an instance derived from tiles, not stored (`RoomInstance`: count, bounds, rim, functional size) |
| 0028 | Floor is worked standing on it, walls from beside (`WorkStance`, derived from two physical clauses) |
| 0029 | Selling returns `RoomSellValuePercentageOfCost` (50 %) per tile; `ReturnPercentage` 0 marks the unsellable rooms |
| 0030 | Crowding cap follows the stance: three to a wall, one to a floor tile |
| 0031 | A bridge reverts to the hazard it spanned; the MAP record's third byte says which |
| 0032 | A wooden bridge over lava burns; the clock (30 s) starts at the build |
| 0033 | A rival claims your ground directly (no neutral step); hostile expansion falls out of `Claim` |
| 0034 | An asset is a logical key resolved by a provider chain; placeholder is total |
| 0035 | Rendering goes through one `WorldView`; the harness draws it as text |
| 0036 | Unity 6 LTS; the adapter is `IRenderTarget`'s nine methods (six when the ADR was written; objects joined 2026-09-05) |
| 0037 | The 1999 manual corroborates, never overrides (`manual-1999.md`) |
| 0038 | A creature never enters a tile it could not have walked into (flight buys nothing over rock) |
| 0039 | A dug-out tile is never closed again; Impenetrable Rock and Gems never open |
| 0040 | A level plays on its own VARIABLES layered over the global table (`GameDatabase.ForLevel`) |
| 0041 | GPL-3.0-or-later; three-line SPDX notice enforced by `LicenseHeaderTests`, deliberately not by IDE0073 |
| 0042 | The portal produces creatures: unlocked, pool has room, something attracts, under capacity (interval half superseded by 0086) |
| 0043 | Not every player is a keeper: `PlayerIds` 0/1/2/3–7 |
| 0044 | A room is claimed whole, never tile by tile (`ConvertRoomHealth` −4000) |
| 0045 | A tile that cannot be owned carries no owner |
| 0046 | `DKII.EXE` may be decompiled and is the **primary source** (2026-09-04 amendment); decompiler output never enters the repository |
| 0047 | A portal produces only creatures whose own flags allow it (`AvailableViaPortal`) |
| 0048 | A creature has health and a hunger clock |
| 0049 | A creature has its own bed and heals only while asleep in it |
| 0050 | The six anger fields are signed; anger waited for its unit (settled by 0053) |
| 0051 | Zero health is a 60-second window; the dying creature is a creature, the corpse is not |
| 0052 | Only a keeper is staffed; the hero side is not "Keeper" in tests |
| 0053 | Anger's ceiling is 32767, measured twice by two methods (see 0081 for the counters) |
| 0054 | An object is one type with per-kind state; a bed knows whose it is |
| 0055 | Gold on the floor is an object; `MaxGoldPerFloorPile`-style limits are not limits |
| 0056 | Gold on the floor is the first thing an imp does |
| 0057 | The free-imp clock is a deadline, not a countdown |
| 0058 | An imp chooses in two tiers; gold is near rather than last |
| 0059 | A diagonal leg is walked surplus-first; the corner is the only point touched |
| 0060 | A bed is walked to: the room by distance, the tile on arrival |
| 0061 | The angry job is rolled again, and a lair is tried before it |
| 0062 | The Hand of Evil; Efficiency is a number a keeper can move (slap 25 s, Q159) |
| 0063 | Carrying: an imp picks a body off the floor (graveyard) |
| 0064 | The Prison: capture, and the enemy half of carrying |
| 0065 | Kind 8: an imp puts a book back on the shelf |
| 0066 | The Hatchery lays (`ChickenGenerationTime…`), one chicken per tile |
| 0067 | A creature eats; the hunger clock stops saturating |
| 0068 | The prison drain is hunger, and a Skeleton never withers |
| 0069 | The aimed (drop) bonus is a task kind and lasts as long as that kind of work; looks three tiles (Q162) |
| 0070 | A prison holds its inner tiles plus one — a 3×3 holds two |
| 0071 | A knocked-out creature has exactly one sixth of its health |
| 0072 | The creature state table has 281 records; 239 was a scan window |
| 0073 | A prisoner shuffles about its cell one orthogonal tile at a time |
| 0074 | A prisoner gets hungry, hunts the cell, races the others to the bird |
| 0075 | The Hand of Evil holds a thing, not only a creature |
| 0076 | Fog of war is simulation state (hash bump); the per-keeper mask is ours — DK2 stores one bit per machine |
| 0077 | A creature goes down on the keeper's own ground; only a worker may go anywhere else (Q198) |
| 0078 | Payday is a journey; the money comes off the tile underfoot |
| 0079 | Being owed is the whole trigger (`AngerNoPay` has no treasury clause); a captive is charged nothing |
| 0080 | A Flame name may be cited in a document, never appears in code (`FlameIdentifierTests`) |
| 0081 | Anger is six counters banded on the **largest**, saturating at 65533 — not the sum (retires 0079's 550-vs-60 comparison: the two never meet) |
| 0082 | A non-worker has a day: three mood choosers, five-second gate, only the happy chooser charges `AngerNoWork`; M4 closed |
| 0083 | Training pays; experience: loader divide by variable 19 floored at 1, promotion zeroes the balance, imp work experience is per tick (Q79) |
| 0084 | A portal has a hole (centre tile), a heart a 3×3; dropping a creature into the hole removes it with its gold |
| 0085 | Ten mechanics DK2 built and switched off (zero-valued variables with live readers); "cut" ≠ "disabled". Amended: a shipped 0 used as `(pct+100)/100` doubles; a level file can re-enable a global zero |
| 0086 | A portal's arrival interval is `(n² + base)` s with `n` the portal's own tally (supersedes 0042's interval) |
| 0087 | `CreatureFlags2.NeverAngered` (Skeleton, Jack) gates the single anger adder; Jack's anger values are a decoy |
| 0088 | The room → job map is confirmed from the room table's `JobType` slot; `JobRules.RoomFor` is a fact (written as a `###` heading in `DECISIONS.md`) |
| 0089 | A tick is 250 ms, `TicksPerSecond = 4`; Q47's fifth was never a cadence; reverses 0024, amends 0020 |
| 0090 | The Library accrues research to the keeper (category 4 hook); three bonuses are not what their names say |
| 0091 | M1 closes: all 18 record types readable, byte-accountable round trip, `dk2tools kwd corpus` audit |
| 0092 | A TRIGGERS parameter view follows what DKII.EXE reads, not an editor label: offsets, widths and signedness from the dispatchers; a name the binary does not settle stays a transcription; a byte no handler reaches is marked unreached. Generic comparison operands are **signed** (unsigned inverted every comparison against a negative threshold). Static-data views and audit tooling only — no hash change, no trigger execution. Sharpened by 0102 |
| 0100 | A test that needs the game data must **skip** without it, not pass (`[InstallFact]`); 0093–0099 remain reserved against a worktree collision (0092, 0101, 0102 and 0103 have since been taken) |
| 0101 | A level's placed THINGS become world contents (`ThingProjection`): creatures and objects placed in file order, ROOM records recognised and not placed, everything else counted and returned. Three transcribed field meanings were wrong — coordinates are tile indices, `InitialHealth` is a percentage, `RoomThing.RoomType` is a terrain id. No hash change |
| 0102 | The trigger flag and timer banks are **level** state, not per-player (`TriggerState` on `WorldState`, hashed and snapshotted): 128 flags, 16 timers, one opaque special target. The byte the external field list calls `Flag` is an **operation and addressing mode**, not a flag id. Amended twice the same day: an authored timer threshold is in **seconds** and DK2's loader multiplies it by variable 19 once at level load (the ADR first said ticks); type 1's byte layout corrected to match type 2's |
| 0103 | A level's 1,152 action points are **ordered world state**, and they have **no availability field**. This corrects ADR-0102, which inferred one from a parameter *name*: nothing in the binary writes availability to an action point. Took `HashVersion`/`FormatVersion` to 40. Answered Q222 and opened Q235–Q238 (`action-points.md`) |
| 0109 | Player condition types 48, 53 and 56–61 are answered **false**, not declined: their jump-table entry is the dispatcher's zero-return epilogue. No behaviour or hash change; the campaign stall frontier falls 777 → 616 |
| 0107 | An action returns what its dispatcher returns whether or not we perform it; `Blocked` becomes conditions only and `Unhandled` holds what DK2 itself refuses. Took `HashVersion`/`FormatVersion` to 46. **Bought no reach**: all 5,408 shipped action records are leaves, so the oracle did not move |
| 0106 | All 940 doors and 959 traps are ordered world state with every parsed field verbatim and no behaviour. All door trigger ids are zero. Took `HashVersion`/`FormatVersion` to 45; answered Q224 and opened Q249–Q253 (`doors-and-traps.md`) |

Standing owner rulings that are not ADRs but are CONFIRMED from the original and must not be reopened: a keeper has at most one dungeon heart and losing it is defeat (Q40); the mana ceiling is 200000 from the first second and never moves — `DungeonHeartMaxManaIncrease` and `MaxManaGain` are not ceiling contributions (Q37/Q37a); imps gain speed with level (Q118c/Q119); the Mercenary Portal is what the game calls the room the editor labels "Hero Portal" (Q211).

---

## Conventions that are easy to violate by accident

- **A balance constant hardcoded in simulation code is a bug** — read it from `GameDatabase`. `GameDatabase[VariableTypeId.X]` **throws** on a missing value, on purpose, so a system that reads a *new* variable breaks every synthetic database until the variable is added to `TestWorld.Variables()`, `Dk2TerrainFixture.Variables()` and `ScenarioTests.Variables()`. Add the entry; never default the lookup. The one sanctioned exception is `ObservedCorrections` (ADR-0023), which has five admission conditions.
- **A number that does not match how the original plays is not licence to change it.** Reproduce the data, record the discrepancy as an open question, let the scenario output show it. Only a measured, arithmetically impossible shipped value goes through `ObservedCorrections`.
- **Changing simulation state breaks the pinned hash — that is the design.** Bump `GameSimulation.HashVersion`, keep `WorldSnapshot.FormatVersion` equal to it, and update the literal in `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` in the same commit. Never loosen the assertion. Remember the pin's world has no creatures (Q206): a creature field that is not in `HashInto` will not fail the pin.
- **New state must be reachable from `WorldState`** — add the field to `HashInto` *and* to `WorldSnapshot` in the same commit, or it is silently non-deterministic and lost on load.
- **Nothing in the simulation may read an event back.** `SimulationEvents` is write-only observation.
- **Ticks relate to seconds only in `SimulationTiming`.** Use `TicksFromSeconds`; per-second rates once a game second (ADR-0017); reach for `OnOriginalTick`/`OriginalTickPeriodTicks` only when the original's own cadence is load-bearing (a non-additive effect such as a promotion that zeroes the remainder) and say why at the call site. Rates read from a creature record go through `LoaderRatePerTick` (integer divide by 4, floored at 1 — it truncates 30 to 28 a second and inflates 1 to 4).
- **Ask flags, never ids.** A gameplay rule keyed on a terrain id, room id or creature id is a rule about the reference install, not about DK2: `TileWorkRules` reads terrain flags, `IsWorker` picks the Imp and the Dwarf, `CanWalkOnWater`/`CanWalkOnLava` decide crossing, `NeverAngered` decides anger. The `*OnRealDataTests` guards check the partition still holds against the shipped tables.
- **Read a creature's capability from its flags and the room's job from the room table** (`JobRules.RoomFor`, ADR-0088); do not map by name.
- **A decimal printed from an `Fp32` is lossy.** Transcribe the raw value (`dk2tools kwd creature <name>` prints a `raw:` line). The fixture's first Imp hunger rate was 32767 where the raw value is 134213632; only the real-data guard caught it.
- **A fixture creature inherits nothing.** Every field a mechanic reads must be transcribed for every fixture creature and covered by a drift guard — the fixture Goblin carried the Imp's `ExpPerSecondTraining` unnoticed until training paid (ADR-0083).
- **Only a keeper is staffed** (`PlayerIds.IsKeeper`, ADR-0043/0052); player 2 is neutral and owns unclaimed rooms; test worlds must not call the hero side "Keeper".
- **Order in the work queue is behaviour** (ADR-0010): never sort or deduplicate `WorldState`'s work orders.
- **Confidence tags are mandatory** in docs and code comments — CONFIRMED / STRONGLY INFERRED / HYPOTHESIS / UNKNOWN — and `[Dk2Behavior(Confidence.X, "docs/research/…")]` on the rule type. `Confirmed` is reserved for the running game (owner observation or stopwatch); a binary read is STRONGLY INFERRED.
- **Record a shipped variable as "no reader found", never "no reader"**, and say what was scanned — a `.text`-only scan misses `cseg`; a zero-valued variable with a live reader is *disabled*, not *cut* (ADR-0085).
- **Every researched mechanic gets `docs/research/<name>.md`** on the Appendix B template; DK2 facts read from the binary go there in our own words with the address as provenance. Decompiler output stays in `reverse-engineering/` and is never committed (ADR-0046).
- **Licence boundaries:** every source file carries the three-line SPDX notice (`LicenseHeaderTests`); no Flame identifier in `src/`, `tests/` or `unity/`, comments included (`FlameIdentifierTests`, ADR-0080); OpenKeeper facts carry provenance in `THIRD-PARTY-NOTICES.md`; wiki content in our own words with a citation (`wiki-as-a-source.md`), never its prose.
- **Never `git add -A`.** Nothing from `original-reference/`, no `.kwd`/`.wad`/`.kmf`/`.sav`, nothing from `reverse-engineering/` except its README, no crash dumps (`85c0f41` untracked one). Check `git status --short` first.
- **A test that needs the game data uses `[InstallFact]`/`[InstallTheory]`** and `Dk2InstallGate.Path`; an early `return` on a null install is the bug ADR-0100 removed and `InstallGateTests` fails on it.
- **Put the doubt in the test name**, not only in a doc comment — a test that pins a guess hides it (Q86's slope, Q56's countdown, Q127's readings all shipped under honest comments and green tests).
- **A status line is a claim with a date.** Do not hand-edit counts anywhere; run the commands in *Current state*.

---

## Environment gotchas (each of these already cost time)

- **`cat > file` with no stdin hangs the Bash tool** until its timeout, and so does anything that pipes into `python` (`python - <<EOF`, even an empty probe with `|| echo fallback` — the hang happens before the fallback). Never write a bare `cat >` redirect; never pipe into an interpreter. `python -c "..."` works (python 3.13 is at `/d/Python/python`) but is not needed: `perl -0pi -e`, `awk`, `sed` and the `Write`/`Edit` tools cover every case.
- **Long heredocs die in the Bash tool** (`unexpected EOF while looking for matching '` at ~150+ lines, even with a quoted delimiter and plain content). `Write` the block to the scratchpad, then `cat scratchpad/file >> target`. Heredocs also eat backslashes in C# (`Replace('\\', '/')` → `Replace('\', '/')`) and choke on backticks — use `Write`/`Edit` for any such source.
- **`ctx_batch_execute` sandbox:** `for … do … done` fails to parse (use `;`-chained statements or separate commands); `/tmp` does not persist between commands; `python3` is absent, `curl`/`grep`/`sed`/`awk`/`perl`/`xxd` are present.
- **`dotnet build` takes one project, not several** — pass `DK2.slnx`.
- **Another session may be editing the tree.** Check `git status --short` and file mtimes before any tree-wide edit; never touch a file with uncommitted changes that are not yours.
- **Numbers collide across worktrees.** ADR numbers, question numbers and `HashVersion` are read from committed files, so two worktrees pick the same next number (it happened twice on 2026-09-06; ADR-0100 exists because of it). Run `git worktree list` and check every worktree's branch before taking one; prefer appending to an existing question row over opening a new one. Currently: `master` in `D:/DungeonKeeperRemake` and a second worktree at `D:/DungeonKeeperRemake-codex` (on 2026-09-08 it held `codex/action-points`). `docs/DECISIONS.md` is therefore **not** in numeric order, and `open-questions.md` is not either.
- **Windows/git:** CRLF warnings on every commit are expected noise. Documents here are CRLF with a UTF-8 BOM; preserve both.
- **Ghidra headless:** run from a path without spaces in the *script* argument, set `JAVA_HOME` to JDK 26 (`C:/Program Files/Java/jdk-26.0.2.1`), Ghidra 11.0.1 at `D:/ghidra_11.0.1_PUBLIC`, project `D:/ghidra projects/DK2_RE`, `-noanalysis` (already analysed). Full command in `docs/RESEARCH-QUEUE.md`.
- **Ghidra scans:** the image has **two** executable blocks — `.text` (`0x401000`–`0x652c00`) and `cseg` (`0x653000`–`0x66bfff`, 100 KB) — so scan all thirteen initialised blocks (`ScanAll.py`, not `ScanAddr.py`); put a positive control in every run (variable 19's global must return **226** hits); render addresses with leading zeros (`0x006e4154`); an instruction-text match on `+0x758` also hits `[ESP + 0x758]` (check the base register); a dword load at a byte field followed by `AND …,0xff` is not a byte read; a contiguous read across the block gap truncates silently; the decompiler drops empty `if` bodies — disassemble when a compare has no consequence.
- **Flame/OpenKeeper builds** (`original-reference/README.md`): run from `cmd`, call `gradlew.bat` by full path, the Gradle file-watcher error is noise; Flame's install step would replace the game's `PATCH.dll` — never pass `-DDK2_DIR` casually, back up first.
- **`winget search` without `--source winget` prompts** for the msstore agreement and fails (`Error reading input in prompt`). Always pass `--source winget`.
- **Unity Hub is an MSIX package** (`Get-AppxPackage *Unity*`), not in `Program Files`; the editor is at `C:\Program Files\Unity 6000.6.0f1`. First launch on a project takes minutes (`Opening project...` is not a hang).
- **Unity exit code 198 with `Found 0 entitlement groups`** = editor not activated: sign in via Unity Hub, then *add a free personal licence* as a separate step.
- **Never reference `UnityEngine.dll` alongside the module assemblies** — every type becomes ambiguous (CS0433). `DK2.Unity.Compile` references only the modules, as Unity's asmdef build does.
- **The `.sln`/`.csproj` in `unity/DK2.Unity` are Unity's**, regenerated on every open and gitignored; Unity opens the *folder*.
- **`pwsh` is not installed.** Use the `PowerShell` tool or `powershell.exe -NoProfile -File …`.
- **Re-run `unity/sync-assemblies.ps1` after any change under `src/`**, or Unity runs stale DLLs.

---

## Research method that has been working

Cheap to expensive; stop when the question is answered. `AGENTS.md` gives the rank of sources; this is the working order.

0. **Ask the owner, or hand them `docs/research/observation-checklist.md`.** They have played DK2; anything a player would *see* is settled in a sentence (Q37, the one-heart rule, Q159, Q162, Q171, Q88). Ask for **quantities by name** ("how much gold per swing", not "how fast") and for values that **differ** (two identical measurements cannot separate a real column from a coincidence). Record "no" as CONFIRMED too. A recording of play turned into a question list (`docs/CODEX_TASKS.md` Brief 4) reversed shipped behaviour that every test agreed with (Q56).
1. **Is the answer in the shipped data?** `dk2tools kwd …`, `game-database-dump.md`, `creature-table-dump.md`, `terrain-room-dump.md`. Check our own dump before recording a number as absent (Q34 was "absent" for four sessions on a mis-transcribed 100).
2. **The binary, via Ghidra** — first for anything the data and the owner cannot settle, and before filing UNKNOWN (ADR-0046). Working recipes: find a record loader by `CopyPairs.py` (source offset = file offset + a constant per record type: `0x3F0` creatures, `0x80` objects), take the store on the next line; the three references that are never consumers are the initialiser, the loader and the definition-copy routine. When a counter's writer will not be found, find a timer set in units of it and decremented on a known cadence (that is how Q1 fell). If a mechanic has no *state* handler, look at its state **category** — the 32 records at `0x00699688` carry enter/update/leave hooks, and the prison drain, training, research and manufacture live in the update hook.
2a. **Read the running game's memory**, once the addresses are known from step 2. Proven on 2026-09-08 (ADR-0102 amendments, `trigger-state.md` §5): a reader attached to a live `DKII-dx.EXE` sampled the flag bank, the timer bank and the tick counter at the tick rate over two 300-second plays of campaign level 1, and settled a unit question the disassembly had answered the wrong way. What made it discriminating was **predicting the specific flag a condition's subtree writes**, not looking for activity near a predicted tick — level 1 changes some flag every ~13 ticks, so a tick-only match is worth nothing. It also produced facts nothing else could: the steady-state tick rate (3.93–3.95 ticks/s; the first 60 s of a fresh level runs at 3.687), the in-memory condition and action counts, and a chain of two predictions where the second is only right if the first was.
3. **`docs/research/external-resources.md`** for what is outside the repo: ufdada/dk2-tools 010 templates (format facts), Flame's `mapping/DKII_EXE_v170.sgmap` (868 structs, 10,548 globals — structure, not behaviour: it names 34 % of symbols in the creature-behaviour range), OpenKeeper as documentation (its creature AI is a stub and its comments call behaviour "remembered"; Q86's slope came from it and was wrong), the wiki (facts, own words).

**Validate structurally, never on trust.** Record size = `contentSize ÷ itemCount` exactly (caught the 40-byte WAD stride the template had at 36); sum a template's field widths and hit the stride exactly — partial sums (TRAPS 516 vs 579), tagged unions (VARIABLES 100 vs 16) and mark/reset double counts (174 vs 84) all fail it; a whole stride is not proof of fixed-stride records (`GlobalVariables.kwd` THINGS divides at 92 and is 39 self-describing records); semantic invariants (happy-job chances sum to 100 over 46 creatures); cross-parser references resolving; known values (Vampire 666 damage, Prison → Skeleton, Hero Lair heals).

**Traps with names, each of which has cost a committed claim:**

- *A claim nothing checks may already be false.* Ask what would fail if the rule stopped holding; if nothing, write the test.
- *A named variable is not evidence the engine consults it.* Scan the global and count readers before building on a balance variable, a flag or a record column — twelve balance variables and three creature fields are cut, and ten more are live with a zero rate (ADR-0085).
- *A sweep of keys is not a sweep of values.* "X globally, Y on level 1" is unverified until the field is swept across all 66 level files (`dk2tools kwd varsweep`); a byte scan for `u32 == id` gave 47 levels where the parser gives 5.
- *Two correct numbers may never meet.* Before comparing two shipped values (550 vs 60, 20 × 60 = 1200), check the engine puts them in one place (ADR-0081, ADR-0083).
- *A measured amount is not a cadence.* 273 gold a swing sized an application; it never said how often one lands (ADR-0089).
- *Two wrong constants can cancel*; the sign of the residual discriminates (a hand timer over-reads and cannot under-read).
- *A negative is a claim about the scope searched.* Say what was swept (glob, block, file set) before believing an absence. A scan filtered to a `0x2600`-byte window "proved" there was no multiply by variable 19 anywhere; rerunning it unfiltered found 225 sites in 82 functions, one of which was already on the list of candidates (ADR-0102 amendment).
- *A value can pass through a transform, so a single unit is an assumption.* A trigger timer threshold is authored in seconds, scaled to ticks by the level loader, and compared in ticks: every individual binary read was right and the unit conclusion was wrong. Ask where the value is *written* as well as where it is read (ADR-0102 amendments, Q233).
- *A near-constant byte makes every record look alike.* An argument about "twelve gates that all test flag 0" was read off a byte that is zero in 1374 of 1375 shipped records — the indirect operand, not the flag id. Check a field's corpus distribution before reasoning from its value (`dk2tools kwd trigger-params`).
- *Do not name a mechanism from the shape of its handling.* Flag id 128 and timer id 16 have fully-read dispatch paths and completely unknown meanings; they are carried as an opaque value and a refusal (Q229, Q232). This is the discipline `FillInHealth` and creature flag bit 30 were missing.
- *A differential experiment needs its null.* The fog bit was found in twenty minutes only because a do-nothing save was diffed first.
- *Count what a tile borders, not what a level contains* (a fully bridged pond leaves no water to count).
- *Some questions are cheaper to derive than to observe* (the work stance fell out of two physical clauses; octile cost out of √2).
- *A handler that does not refuse is not a UI that offers* (drop rules, Q196/Q198).
- *Ask before building.* Where a room's rule needs a field the simulation lacks, say so; do not invent the field because the manual names it.

---

## Live research questions

`docs/research/open-questions.md` is the record (rows are written both as `| Q123 |` and `| **Q123** |`, and the table is not in numeric order). The ones that gate work today, by how they are answered:

**Cheapest — one look at the running game** (`observation-checklist.md` is the list to hand over)

| Q | What to watch |
|---|---|
| Q86 leftover | a **worn** portal lost and re-claimed — does the tally reset? Needs a skirmish map with two claimable portals (`The Deep End`, `Siege`, `U Turn`; `dk2tools kwd portals` finds them). Also: portal 1's arrivals after 03:18 were missing from the second run |
| Q202 | slap a creature to Unhappy with a bed available; does sleeping bring it down? Names anger buckets 0 and 5 |
| Q177 | does the aimed (drop) bonus carry from one marked tile to the next? The binary says yes |
| Q176 | does a hungry creature wake up to eat? |
| Q97 | does a *large* lair change how fast arrivals slow? |
| Q66 | is a tile touching owned ground only at a corner claimable? |
| Q118 | how much does water slow a creature (a Vampire in a lake: 25 s to die = per-second hazard, 1.25 s = per-tick) |
| Q107, Q109, Q103 | rescue after the bed is sold; enemy collapse; the 240 s / 9 s sleep cycle |
| Q223 | load Level13 and see who owns the hero gate at (11,8). Five of the campaign's 275 ROOM records disagree with the MAP grid about the owner of a structure, every one a Hero Gate (two on Level13, one on Onslaught, two on Siege): the record says the good player, the grid says a keeper. The grid wins today by default. A hero gate owned by a keeper spawns for the wrong side, if it spawns at all |

**Binary reads** (`docs/RESEARCH-QUEUE.md` keeps the working list and the Ghidra command)

| Q | Read |
|---|---|
| Q209 | the walkability predicate for objects (see Next steps 1) |
| Q215 | what guards DK2's "health reached `MaxHealth`" conversion — disassemble `0x00453058` onward; the decompile dropped the branch body |
| Q217, Q218 | who sets the keeper's casino flag at `player+0x20D`; what passes the research adder `FUN_004c2dc0` a negative amount (`CallScanAll.py`, all blocks) |
| Q216 | does slapping a researcher visibly triple research (needs the research tree) |
| Q204 | `player+0x76 & 4` — a fallen dungeon heart? |
| Q201 | the dizzy-drop driver: `CreatureStunnedTimeSeconds` is dead (one reference, the setter); candidate is the per-creature `fp32 stunDuration` at record `0x0ED0` |
| Q199 | the pet-dungeon mana cap: skirmish regen clamps at 500 (`MaximumManaGainPerSecond`), pet dungeon shows +1400; no level file overrides variable 235 — find its reader |
| Q197 | portals visible through fog on the minimap — a second mechanism; `RevealThroughFogOfWar` is a terrain flag and a Portal is a room |
| Q185 | state-record flag byte `+0x18`: bit 0 is on 147 of 281 states and none of the seven readers tests it; bits 1, 3, 4 named |
| Q186, Q184 | the 32 category records at `0x00699688` (enter `+0x04`, update `+0x14`, leave `+0x24`); the three pointers at `+0x04/+0x08/+0x0c` of a state record are written once and read nowhere |
| Q210 leftovers | creature flag bit 0's attack (installed into a table, not called); bit 1 has no reader in either block; the room table's integers at `+0x19`, `+0x1d`, `+0x21` |
| `creature+0x3EA` | bits 4 (doubles prayer mana) and 7 (Q69) both gate real behaviour and are UNKNOWN |
| Q84 | id 66 is the level's player count (54 of 57 agree); `Level17` reads 2 against a pool reaching player 5 — the PLAYERS record now parses and can settle it |
| Q35 | which treasury tile a withdrawal drains |
| Q188, Q168 | when a prisoner's step roll halves; how often an overcrowded prison rolls its jailbreak |
| Q220, Q221 | a standalone GLOBALS(0) chunk; the 16-byte DeadBody THINGS payload (absent from all 6,318 shipped THINGS records) |
| Q5, Q3, Q8 | pathfinding (`Follow_Path`, 36 bytes — the project's biggest risk), the RNG, combat resolution — all UNKNOWN and all large |

**Triggers — what still stands between the banks and a winnable level** (`trigger-semantics.md`, `trigger-state.md`; `dk2tools kwd triggers` shows a level's tree, `kwd trigger-params` a parameter's corpus distribution)

| Q | Open |
|---|---|
| Q228 | **answered.** The root evaluator runs once per game turn: world slot `0x18` has one caller at `0x00526AA7`, reached through the owning container rather than the world global. The turn driver advances at `1000 / 4` ms. See `trigger-state.md` §6 |
| Q229 | flag id **128** is not a flag — both dispatchers divert to a dword at `+0x72A2`, set a companion at `+0x72A6` to 1, call through virtual slot `+0x348`, and take the operand's full 32 bits. Decompile that slot and find what consumes `+0x72A2`. `TriggerState` carries it as an opaque value; do not name it first |
| Q232 | timer id **16** is not a timer — the condition path compares against `0x10` (`0x005184EB`) and reads through virtual slot `+0x34C`, and the bank's only indexed store is action type 8's clock stamp, so a write naming 16 does not reach it either. **All 86** action type 23 records in the campaign target it and five type 2 conditions test it, so it is live, not a curiosity. `TriggerState` refuses the id |
| Q230 | a flag id above 128 would index past the bank into the timers; **no shipped level does it** (asserted over all 66 levels in `TriggerStateOnRealDataTests`) and `TriggerState` throws instead of reproducing the overrun. Faithfulness and sanity conflict, so this is a deliberate decision if a real map is ever found doing it |
| Q226, Q227 | the trigger selectors and fields DKII.EXE reads without revealing a role. Exposed as `Unknown_*` rather than as an external guess; action type 24 alone has 827 shipped records |
| Q225 | do DK2-authored files corroborate the trigger types the shipped corpus never uses? Generic 3/5/25/29 and their action counterparts have body-read paths in the executable and **no** instance in the 9,161 shipped records. Needs a map made in the original editor; not M6 for any shipped level |
| Q222 | **answered and built** (ADR-0103): all 1,152 action points are ordered world state. The answer reversed the expected one — an action point has **no availability field**, and the `Available` byte read against action types 16, 17, 27 and 31 was inferred from a parameter *name* rather than from anything the binary writes. Q235–Q238 carry what is left |
| Q224 | **answered and built** (ADR-0106): all 940 doors and 959 traps are ordered, hashed, snapshotted world state with every parsed field verbatim. Q249–Q253 carry behaviour and possible field transforms |

**Standing UNKNOWNs that are parked, not forgotten:** Q57 (what DK2 does when a keeper cannot pay imps; ours floors mana at zero), Q58 (Possession's 25/s upkeep that the owner never saw), Q70 (the last 72 bytes of a CREATURES record), Q75 (a player-level starting gold stockpile the manual mentions), Q76, Q78, Q81, Q83 (`GameTicks` = 340 in every level; not a rate and not Q1), Q87 (`CreaturePool`: limit or quota), Q90 (portal per-type weighting), Q94, Q102, Q160 (`TimeInHandBeforeCreaturesBecomeAngrySeconds` = 1000), Q164, Q169, Q194, Q208 (the Imp's `ExpPerSecond` is 40× its training rate).

---

## Corrections carried forward

Each is a thing a reader would plausibly re-assume. One line; the cited file has the evidence.

- There is no `docs/ai/CLAUDE.md` and `AGENTS.md` does not reference one; `AGENTS.md` is the operating instruction and does not restrict implementation.
- "Five work applications a second" was never DK2's cadence — the fifth is the size of one application (`tabled × 0.8 ÷ 4`); work lands once a tick, four a second (ADR-0089). Any document still saying `TicksPerSecond` is "the placeholder 20" is stale.
- An upward job costs the **deficit**, not the terrain's whole `MaxHealth` (ADR-0089 reversing ADR-0024); Q46's "2-3-4 jumps" counted visual damage stages (`terrain+0x1c3`), not swings.
- "Filling in" a floor tile back to rock is not a DK2 action (Q219); `FillInHealth`, the `FillInable` flag and Claimed Path → Rock were a shape in the data only.
- The room the editor labels "Hero Portal" is the **Mercenary Portal** in game and a keeper who claims one receives mercenaries, never a Dwarf (Q211); portal capacity counts every portal room, producing or not, `(n − 1) × 5 + 15` (Q98).
- The portal interval is `(n² + base)` with `n` the portal's own tally — not `max(base, base × creatures ÷ 2)`, which was OpenKeeper's guess (ADR-0086); `EntranceGenerationSpeedSeconds` is overridden on **five** levels (18/20/22/30/10000), not only level 1.
- Anger is six counters banded on the **largest**, not a sum; a bed's −550/s and an unpaid wage's +60/s never meet (ADR-0081). `AngerNoPay` has no treasury clause; only the *happy* chooser charges `AngerNoWork` (ADR-0079/0082). Jack has anger values and never uses them (ADR-0087).
- `ExpPerSecond` is **combat** experience; "the record's rates are per minute" was a coincidence of 20 × 60 (ADR-0083). Promotion zeroes the remainder. Imp work experience is per tick (80 a game second), so an imp reaches level 2 in about 37 s, not 150.
- DK2 stores **one** fog bit, per machine, not per keeper (`fog-of-war.md` §10); our per-keeper mask is ours. The save's tile grid is **not** at a fixed `0x8861` (mpd1 `0x8861`, TrapTime `0x14880`, Caverns `0x19e0f`); locate it by correlation.
- The creature state table has **281** records (ADR-0072); 239 was a scan window.
- The flower above a creature is **health**, six petals, not anger and not ten (Q108, Q171). The Hand cannot pick up a collapsed creature (Q161).
- `MaxFreeRangeChickensPerPlayer` (12) does not cap a hatchery — one chicken per tile does (Q135); `MaxGoldPileOutsideTreasury`-style limits are not limits (ADR-0055); `TimeBetweenReattemptingSleepSeconds` and other "wrongly named" variables are **live** — a wrong name is not a missing reader.
- A zero-valued variable with a live reader is *disabled*, not *cut*; `PercentageOfManaGainedBySacrificing` = 0 **doubles** (`x += x × (pct + 100) ÷ 100`), so "sacrificing yields no mana" is wrong; a level file can re-enable a global zero (ADR-0085 amendments).
- `RoomSellSystem`/`RoomBuildSystem` already refuse on a dungeon heart; Q207's real gap is `ConvertRoom` excluding a *fallen* heart.
- In the MAP record player **2** is the unowned marker, not 0 (`PlayerIds`, `kwd portals`).
- Only an imp may be dropped on unclaimed Dirt Path (Q198, ADR-0077); "the handler does not refuse" proved nothing about what the UI offers.
- `IncreasedWorkRateDurationFromSlappingSeconds` was recorded as read by nothing and governs a second slap effect; write "no reader found", never "no reader" (`RESEARCH-QUEUE.md`).
- The determinism pin's world has no creatures (Q206) — a green pin says nothing about creature state.
- `DK2.Convert` exists (KMF → glTF/GLB, textures → PNG, SDT → WAV) — documents saying "there is no converter" are stale; what does not exist is a Unity branch that loads its output.
- A THINGS record's `X`/`Y` are **tile indices**, not `Fp32` subtile units; `InitialHealth` is a **percentage** of the maximum (nothing in the corpus exceeds 100); `RoomThing.RoomType` is a **terrain** id and a ROOM record is not a room to build — the MAP grid already holds it (ADR-0101). All three were transcribed from an external field list and all three were wrong.
- A trigger timer threshold in a `.kld` is in **seconds**; DK2's level trigger initialiser multiplies it by variable 19 once at load and the comparison consumes **ticks** (ADR-0102 amendments, Q233). The ADR's first reading — and any document repeating it — said ticks. A *stamp* in the timer bank has always been a tick count.
- The trigger flag and timer banks are **level** state, not per-player (ADR-0102) — the player-context dispatcher `0x004C8620` is the one call site that could have shown otherwise and it loads the global instead. The timer bank is **16** slots, not the 256 the masked write path suggests. The byte the external field list calls `Flag` is an **operation and addressing mode**; the flag it acts on is a different byte. Flag id 128 and timer id 16 are one past their banks and are neither a flag nor a timer (Q229, Q232).
- A pure evaluator exists only for one tag-213 type-1 or ordinary type-2 record. It does not walk a tree, retire repeats, or run an action; Q228 settles its once-per-turn caller and `trigger-tree.md` records those still-unimplemented execution semantics (ADR-0102).

