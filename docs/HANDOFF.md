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
| M5 oracle comparison | first clean comparison | level 1 idle matches exactly by event set (4 / 0 / 0); `dk2tools capture-diff` aligns live captures on stored timer stamps, and the optional `dk2run6.csv` gates a regression test (ADR-0117). Level 20 idle then gave the first window with **no difference at all**, and its 11-minute version closed Q273: timer 1's restamp is a value the game *stores*, DK2 stored **1724** where we store 1724, and the probe first saw it at 1725 — so the lag is the probe's, and applying that one tick makes **every trigger event captured on two levels match exactly** (`docs/experiments/level20-idle-002`, ADR-0119). Broader scenarios and more implemented trigger effects remain |
| M6 real level loads | partial | every parsed THINGS record reaches the world or existing MAP grid (ADR-0101/0103/0106/0108), and trigger state plus partial execution exist (ADR-0102/0105). Still missing: most trigger actions, party/generator behaviour, and objectives. No level is winnable |

### What exists (present tense; the ADR is the specification)

- **Tiles and work.** Dig, mine, claim, reinforce, repair, attack, convert-room, all from terrain flags (`TileWorkRules`, ADR-0009). Work lands once a tick, four a second, `delta × efficiency ÷ 4` (ADR-0089; `SimulationTiming`). Upward jobs cost the deficit (ADR-0089 reversing ADR-0024). Stance: on the tile for floor, beside it for a wall (ADR-0028); three to a wall face, one to a floor tile (ADR-0026/0030). A dug-out tile never closes and "filling in" is not a DK2 action — `TileWorkKind.FillIn` was deleted (Q219, `13a1f47`). Hostile expansion is direct claiming (ADR-0033). A tile nobody can own carries no owner (ADR-0045).
- **Rooms.** A room is a derived instance (`RoomInstance`, ADR-0027); placement from placeable-on flags (ADR-0016); cost per tile, sell for `RoomSellValuePercentageOfCost` = 50 (ADR-0029); a bridge reverts to the hazard it spanned and a wooden one over lava burns in 30 s (ADR-0031/0032); a room is claimed whole (ADR-0044). Behaviour exists for: Portal (ADR-0042/0047/0084/0086), Lair (ADR-0049/0060), Hatchery (ADR-0066/0067), Prison (ADR-0064/0068/0070–0074), Treasury and Dungeon Heart (gold), Training Room (ADR-0083), Library accrual only (ADR-0090), Temple prayer and sacrifice (ADR-0082/0149), and Graveyard corpse tally and Vampire creation (ADR-0063/0151). Portal and heart centres block movement (`StructureBlocks`, ADR-0084).
- **Gold and mana.** Gold lives in tiles and in creatures' hands; no player counter (ADR-0015/0019); floor gold is an object an imp fetches first (ADR-0055/0056). Mana from owned land, ceiling 200000 flat, income cap `MaximumManaGainPerSecond` 500 (`ManaRules`, ADR-0017). Imps: free below `MinimumImpThreshold` every `TimeBeforeFreeImpGeneratedSeconds` as a deadline (ADR-0057), Create Imp 1500 × (imps − 4 + 1), 7 mana/s upkeep (ADR-0021/0022).
- **Imps.** Two-tier job choice (ADR-0058, `imp-task-priority.md`): floor gold, then marked excavation, claiming, reinforcing; gold near rather than last. Carrying: bodies to the graveyard, unconscious enemies to prison, objects back to shelves (ADR-0063/0064/0065). Aimed bonus is a task kind (ADR-0069).
- **Creatures.** Health and hunger (ADR-0048), own bed and healing asleep in it (ADR-0049), eating (ADR-0067), levels as a stat multiplier with experience from work/training/combat rates run through DK2's loader divide (ADR-0083, `SimulationTiming.LoaderRatePerTick`), anger as six counters banded on the maximum with 65533 saturation (ADR-0081; `NeverAngered` flag, ADR-0087), desertion, death as a 60-second window then a corpse (ADR-0051), payday as a journey (ADR-0078/0079), job selection by three mood choosers gated on five game seconds (ADR-0082), Hand of Evil pick-up/drop/slap of creatures and things (ADR-0062/0075/0077), knock-out at one sixth health (ADR-0071), water/lava hazard damage from `TerrainDef.Damage`.
- **Players.** `PlayerIds`: 0 unowned, 1 hero side, 2 neutral (an unclaimed room), 3–7 keepers (ADR-0043); only a keeper is staffed (ADR-0052). `DK2.AI.KeeperAI` is an **input source** that emits `PlayerCommand`s (step 1 of `docs/KEEPER_AI.md` §12.7): digs to gold, sites and places rooms in the shipped order; no simulation surface changed.
- **World.** Fog of war is simulation state, six-tile perception radius, one discovery bit per keeper in ours (ADR-0076; DK2 stores one bit per machine — `fog-of-war.md` §10). Levels play on their own VARIABLES over the global table (ADR-0040). Determinism gate: FNV-1a state hash, one pinned literal, `ChaosSystem` (ADR-0006).
- **A level's placed THINGS.** `ThingProjection.Place(world, database, things)` (ADR-0101/0103/0106/0108/0139) preserves every parsed record in file order and reports counts. ROOM records are already in the MAP grid. Doors and traps additionally derive saved construction state, which backs player conditions 27/28; open/close, navigation and firing remain absent. Party members are unspawned templates and do not count as creatures. Coordinates are **bare tile indices**, `InitialHealth` is a percentage, and `RoomThing.RoomType` is a terrain id. Pinned against every shipped level by `PlacedThingsOnRealDataTests`; hashed state is version 57.
- **Trigger execution.** `TriggerExecutor` walks the world tree, 24 party roots, player roots, action-point roots and creature roots once per game turn (Q239, ADR-0152). It performs actions 6, 7, 8, 10, 11, 22, 44 and 45 plus the partial creation actions 1 and 14; type 6 mutates live player availability, 44 gates anger, 45 preserves notifications, and 10/11 end the game for the tree's player with DK2's one-shot `Status` (ADR-0160/0161/0162). Other successful actions are `Partial`; DK2-refused actions are `Unhandled` (ADR-0107). Conditions cover the world flag/timer/time arms, creature 36/37/49, player counters and the seven area arms; unsupported conditions are `Blocked`, dispatcher false epilogues are answered false, and nothing is silently false. Trigger and availability state is hashed and snapshotted. Q228 fixes cadence; `trigger-walkers.md`, `trigger-tree.md` and `trigger-semantics.md` are the specifications.

- **Data.** WAD, KWD/KLD (18 record types; per-field file layout in `kwd-record-fields.md`), STR string tables, SDT sound banks, KMF models, textures; `Dk2SaveReader` reads the tile grid of a `.SAV`/`.QSAV` (grid located by correlation, `FindGridOffset`). `DK2.Convert` writes glTF/GLB and PNG (`dk2tools convert`).
- **Presentation.** `AssetRef` → `AssetProviderChain` (replacement → converted → placeholder, total); `WorldView(database, viewingPlayerId)` is the one translation to drawables; `TextRenderer` writes `world.txt` per scenario; `ScenePresenter` diffs and pushes through `IRenderTarget`; `PlayerInputTranslator` turns renderer-free gestures into all eight existing commands and `TilePicker` says which tile a pointer ray is over; Unity 6 LTS `6000.6.0f1` supplies the input/camera shim and renders campaign level 1 as boxes (ADR-0034/0035/0036/0104).

### What does not exist

Combat (Q8), hero-party spawning and effect-generator behaviour (their state exists; Q254–Q259), every keeper spell but Create Imp, trap behaviour, and everything of a door beyond opening, locking and its navigation byte (their state exists; Q249–Q253, ADR-0158), the creature trigger event filter and ten creature-owned action effects, most other trigger action types and 31 of the 46 shipped condition types — `dk2tools trigger-run` prints each stall and the records it gates; ADR-0125 owns the full accounting — objectives, win/lose/defeat (a fallen heart is not claimable — Q207/Q93), the research tree (accrual only), Workshop/Casino/Torture/Guard Room/Combat Pit behaviour, Temple sacrifice action `0x87`, wandering (Q41), vampire reincarnation (Q111), `Obstacle` furniture for `Repel_Obstacle`, the remembered victim of `Separation`, run and shuffle speeds and the per-state path flags (Q5 itself is built and wired, ADR-0157). Unity: `DungeonScene` has no branch that loads the converter's glTF/PNG (Unity ships no runtime glTF importer); boxes rather than DK2's walls (Q71).

### How to keep this file from lying

**A claim that something has NOT happened carries a date.** Added 2026-09-15:
`dk2tools docs --check` fails any line here matching *never / not yet / awaits /
still no* that has no `20xx-xx-xx` on it. It exists because this file claimed the
input channel awaited its first capture five days after runs 13 and 14 closed it,
and that was handed to the owner as a request for work already done. **Prefer a
pointer to a restatement:** every stale line here was a copied conclusion.

### Standing caveats — each still true on 2026-09-08

- **One deliberate departure from shipped data:** `DigRockHealth` −5000 is corrected to −500 because the owner measured two swings to dig a wall (ADR-0023, Q20). `ObservedCorrections` is the only place such a number may live, and lists the five conditions for adding one.
- **Portal interval** `(n² + EntranceGenerationSpeedSeconds)` s with `n` this portal's own tally (ADR-0086, stopwatch-confirmed); whether the tally is ever zeroed is unread (both zero-stores are vtable-only). A worn portal lost and re-claimed is the missing observation.
- **The max-health conversion has no work-kind guard** (Q215, `tile-work.md`). It is safe because floor repair and fill-in are unreachable; repairable walls/rooms point to themselves.
- **The determinism pin holds two creatures** (Q206, done 2026-09-10), so `CreatureState.HashInto` is guarded at last — but the gate registers no creature *system*, so the chaos system is the only thing that moves them.
- **A trigger timer's unit depends on where you read it** (ADR-0102 amendments): the `.kld` holds **seconds**, DK2's level trigger initialiser multiplies by variable 19 in place at load (`0x0050C2DB` in `FUN_0050B5B0`, condition types 2 and 34, action types 16 and 23), the comparison consumes **ticks**, and a save game would contain the scaled value. Confirmed by reading level 1's condition array out of the running game: all 22 timer thresholds held the authored value times four. Ours takes seconds and converts in `HasElapsed`; when the `.kld` trigger loader is written the conversion must happen exactly once — there or at the comparison, never both.
- **Install-gated tests skip, they do not pass** (ADR-0100, `tests/DK2.TestSupport/InstallGate.cs`): `[InstallFact]`/`[InstallTheory]` set `Skip` when `Dk2Install.Find()` is null; `InstallGateTests` fails the build if a `return`-on-null reappears; `DK2_REQUIRE_INSTALL=1` turns a missing install into a failure. CI (`.github/workflows/ci.yml`, Linux + Windows) sets `DK2_DIR: /nonexistent`, so every real-data test is reported skipped there.
- **Simulation speed has never been measured** (still true 2026-09-15; goal 9, `GOAL_ALIGNMENT.md`).
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
| `src/DK2.Simulation` | all game rules and mutable state | `GameSimulation` (loop, `StateHash`, `HashVersion`), `WorldState` (`TileState`, `PlayerState`, `FromMap`), `WorldSnapshot` (`FormatVersion`), `InputFrame`/`PlayerCommand`, `SimulationEvents` (write-only), `SimulationTiming`, `ISimulationSystem`/`SimulationContext`; rules: `TileWork` (`TileWorkKind`, `TileWorkRules`), `WorkStance`, `ImpCrowding`, `ImpTaskKind`, `CarryRules`, `ObjectCarryRules`, `GoldStore`, `FloorGold`, `RoomRules`, `RoomInstance`, `RoomIndex`, `BridgeRules`, `StructureBlocks`, `ManaRules`, `ImpSupplyRules`, `PortalRules`, `LairRules`, `HatcheryRules`, `EatingRules`, `PrisonRules`, `ResearchRules`, `TrainingRules`, `PaydayRules`, `HandRules`, `JobRules`, `Efficiency`, `CreatureState`, `CreatureHealthRules`, `CreatureDeathRules`, `CreatureLevelRules`, `CreatureAnger`/`CreatureAngerRules`, `ObjectState`, `ObservedCorrections`, `WorldPathGrid`, `ThingProjection` (+`ThingProjectionResult`), `TriggerState` (+`FlagWriteMode`), `SeparationRules` (wired by ADR-0157 phase 3), `MeleeDamageRules` (built, called by nothing but tests: ADR-0153); `Navigation/`: `WorldSteering` (the navigation byte per tile, the mesh it rebuilds and the four steering probes); `Steering/` (ADR-0157): `Fixed16`, `Bearing`, `SteeringBody`, `RouteWindow`, `ISteeringWorld`, `FollowPathRules`, `AvoidWallsRules`, `RepelObstacleRules`, `SeparationBehaviour`, `SteeringIntegrator`; `Systems/` (one class per system; order declared once in `SimulationSystems.Default()`, ADR-0013): `TileTagSystem`, `TileWorkSystem`, `ImpTaskSystem`, `CreatureMovementSystem`, `DoorSystem`, `RoomBuildSystem`, `AutoFurnitureSystem`, `RoomSellSystem`, `BridgeBurnSystem`, `ManaSystem`, `ImpSupplySystem`, `PortalSystem`, `HandSystem`, `LairSeeker`, `CreatureSleepSystem`, `CreatureHealthSystem`, `CreatureEatSystem`, `HatcherySystem`, `CreatureAngerSystem`, `CreatureJobSystem`, `CreatureExperienceSystem`, `PaydaySystem`, `PrisonSystem`, `PrisonerWanderSystem`, `PrisonerEatSystem`, `TempleSacrificeSystem`, `GraveyardSystem`, `FogOfWarSystem`; `Commands/`: `TileCommands` (`TagTilesCommand`), `RoomCommands` (`BuildRoomCommand`), `ImpCommands`, `HandCommands` |
| `src/DK2.Pathfinding` | the original's router (ADR-0157): `NavMesh`, `MeshRouter`, `Funnel`, `GateWalk`; the grid search survives only as `TryFindNearest`, the work chooser | `IPathfinder`, `GridPathfinder`; `INavByteGrid`, `NavMesh`, `MeshRouter`, `Funnel`, `GateWalk`, `GateRecord`, `NavPoint` |
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

Reference material: `docs/DECISIONS.md` (ADRs), `docs/research/*.md` (one note per mechanic, runtime layouts indexed by `debug-dumpers.md`; template in `DK2_REIMPLEMENTATION_PLAN.md` Appendix B), `docs/research/open-questions.md`, `docs/research/observation-checklist.md` (what the owner can settle by playing), `docs/RESEARCH-QUEUE.md` (what the binary can settle without playing, and the Ghidra headless command), `docs/RESEARCH_STATUS.md`, `docs/EXPERIMENTS.md`, `docs/KEEPER_AI.md`, `docs/GOAL_ALIGNMENT.md`, `docs/CODEX_TASKS.md` (briefs for a second agent), `docs/architecture/simulation-loop.md`, `reverse-engineering/README.md` (the quarantine rule and Ghidra method; the README is the **only** tracked file in that folder — `.gitignore` excludes `reverse-engineering/**`, so neither the scripts nor the exports are committed, and the scripts live in the session scratchpad), `original-reference/README.md` (Flame/OpenKeeper build recipes; the 1999 manual PDF), `unity/README.md`, `THIRD-PARTY-NOTICES.md` (OpenKeeper/GLKeeper/YamlDotNet attribution).

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

**Live memory reader** (same directory, same gitignore): `python reverse-engineering/dk2watch.py --seconds 300 --csv D:\run.csv`. Attaches to `DKII-dx.EXE` **or** `DKII.EXE` with `PROCESS_VM_READ` only — the two exes differ in 1126 of 2,987,568 bytes and none is in `.text`, `cseg` or `.data`, so every address is valid for both. It prints the cached world pointers, variable 19, the record counts and every type-2 timer threshold, then samples the flag bank, timer bank, tick counter and root trigger id at the tick rate into a 150-column CSV. **Load the level first**; it waits for a real level, because the front end is itself one (1 condition, 1 action) and a non-null pointer is not proof. It accumulates the tick counter across a level load, which resets it. Fullscreen is fine — DK2 has no windowed switch and DDrawCompat's borderless mode crashed level load here. `dk2move.py` beside it logs every creature's position and switches on the game's own `nav.log`; the unattended recipe (`-LEVEL`, elevated probe first, then touch nothing) is `navigation-captures.md`.

**Recording what the player did.** A tagged tile is not a bit on the tile: DK2 keeps a per-player **task pool**, one entry per marked tile, reachable at `playerArray + id*4389 - 0x20` (equivalently `player+0x1105`). Fifteen singly-linked kind lists, kinds 1 and 2 being the tagged tiles; head index at `pool+0x4656+kind*2`, live word at `+0x4678+kind*2`, entries 36 bytes at `pool+index*36` with `x:u16@0`, `y:u16@2`, next index `u16@0x1F`, zero ending the list. `dk2watch.py --tags <out.csv>` diffs it per sample and writes `wall,tick,player,event,kind,x,y,type,data,id,workers` — a command log, which is the recording half of the input channel M5 needs (ADR-0126, ADR-0127, ADR-0131, `player-task-pool.md`). **Sampled live and CLOSED (2026-09-10, runs 9–14, `EXPERIMENTS.md`):** run 9's replay applied 30 marks with none rejected (Q287, so the coordinates are tile coordinates), and runs 13 and 14 fed the recorded input back through `InputLog` — DK2-only events fell from 3 to **0**, twice, on controlled three-mark captures. What is left is rate, not input: two imp-driven flags fire 37 and 28 ticks early.

**Reading a condition arm.** `DispatchMap.py` prints `index + 1`, so a true type is **printed + base - 1** — `+18` for the player table (byte table `0x004C85E8`, 56 entries, jump table `0x004C8580`); the world table is base 1, so printed == type (byte `0x0051897C`, 67, jump `0x00518950`). Get the context first: a type is dispatched by its own walker's table and only then delegated, so the same number is a different arm in a player tree and in the world tree — `dk2tools trigger-run` prints it. Then check the *shipped* parameter distribution before implementing (`dk2tools kwd trigger-params 213 <type> <name>`): types 27 and 28 have an availability mode that is easy to read and that **no shipped record uses**, so building it would have moved nothing. `docs/research/trigger-dispatch-tables.md`.

**Reaching a level other than the tutorial**: the world map is gated by the registry value `Player Level Status` under `HKCU\Software\Bullfrog Productions Ltd\Dungeon Keeper II\Configuration\Player` — **160 bytes, forty dwords, one per campaign *map* and not per level number**, so the twenty levels occupy twenty-four entries with 6b, 11B, 11C and 15b interleaved. Setting the first 23 to 1 offers level 20 and leaves everything below it selectable. `Level Number` and `Total Evil Rating` gate nothing; both were tried. Take the size from the loader's clamp at `0x0055FEE1`, not from the community write-up that says 25 dwords — that wrong size is what makes DK2 discard the value. Back the key up first, close DK2 before editing, and see `docs/research/campaign-progress-registry.md`. **Pass `--delay 0`** when the probe waits at the front end: the countdown exists to cover an alt-tab back into the game, and waiting for a level already does that, so leaving it on simply spends the first 32 ticks. **A capture's turn is one tick late**: a sample labelled N shows state before tick N's trigger pass, so capture row N is our row N-1 (measured, ADR-0119) — a timer's *stored* value needs no such correction, which is why `capture-diff` compares stamp sequences. **Level 20 is only quiet for about seven minutes**: after that its placed heroes invade unprompted, with no trigger involved (Q275).

---

## Next steps

Agreed order as of 2026-09-08 (M0–M4 closed; the research queue's head is clear). Every item is checked against the tree, and nothing listed here is built except where an item says which part of it already is. **Milestone ordering is the plan's §8 Run column, revision 2 (2026-09-21):** M6 first, then combat and the keeper AI *ahead of* breadth, and skirmish and multiplayer now exist as M13/M14. Items 3, 5 and 6 below are breadth and move behind 7 and 8.

**Features, cheapest evidence first**

1. **Defeat — the verdict is built, the consequences are not** (ADR-0162, `win-and-defeat.md`). `Status`, the heart's health, ruin and killer, `WIN_GAME`/`LOSE_GAME`, and last-keeper-standing are simulation state; a ruined heart is claimable and holds no gold. Left: what a defeat does to the loser's creatures, rooms and gold (Q358, `0x004DAA10`), alliance propagation (Q359), and the blow itself — nothing damages a heart until combat lands (Q8).
2. **M6 — the rest of THINGS, and trigger execution.** Placement and trigger *state* landed on 2026-09-08 (ADR-0101, ADR-0102); what is left, in the order the evidence allows:
   - **Trigger execution.** The executor performs eight action types, including availability action 6 and world gates 44/45 (ADR-0160/0161); `kwd unreached` ranks remaining effects by unique records in its `Partial` census. Types 17 and 24 rank above 6 but stay parked: 17 has no simulated reader (Q309), while 24 still needs Q227's binary read.
   - **Action points are world state** (ADR-0103): all 1,152 are placed. Actions 16, 17, 27 and 31 do not mutate them; `action-points.md` records the corrected binary read.
   - **Door and trap behaviour** (Q249–Q253): their verbatim ordered state is built (ADR-0106); opening, locking, blocking, arming, firing and field transforms are UNKNOWN.
   - **Hero-party spawning and effect-generator behaviour** (Q254–Q259): verbatim state is built (ADR-0108); action type 14 spawning and generator cadence/effects are UNKNOWN.
   - Level scripting is enumerated: 3,753 generic records over 46 types and 5,408 action records over 38 types in the shipped corpus (`trigger-semantics.md`, `dk2tools kwd trigger-types`); `RESEARCH_STATUS.md` counts 51 action types including those the corpus never uses (Q225). Q220/Q221 remain the two open record-layout questions, Q223 the ROOM-owner disagreement.
3. **Rooms still without behaviour:** Workshop (manufacture, category 5 hook), Casino, Torture Chamber and conversion (category 15's hook can kill on conversion at a zero rate, ADR-0085), Guard Room, Combat Pit (including the fallback DK2 gives a creature that has outgrown the training room — ours refuses it and ejects a capped trainee one five-second gate late; and its loser is **knocked out, not killed**, dying only if no imp carries it to its bed in time — owner, Q310 — so the pit needs no death path of its own: it produces `IsDown` and every consequence is already built), Temple keeper-spell intake (`0x87`; sacrifice otherwise built, ADR-0149). Vampire reincarnation is **read and unbuilt**: `FUN_0048FFC0`'s guards are level > 1 and the Monk bit (Q111 answered, `combat.md` §6.3), and it waits on combat, since nothing kills a vampire. Its destination is Q329, separate from Graveyard creation (ADR-0151).
5. **Wire the attraction budget** (`auto-placed-furniture.md`, Q89/Q270). Generic floor and wall furniture, Temple Hand, Hatchery coops, object ownership, and the five furniture-count capacity arms are built. Binary research resolved the extra wall-grid bit as cached `Solid`, so the wall pass now requires both `Solid` and `AllowRoomWalls`; sorted order still stands in for DK2's construction-order phase. `PortalRules.IsAttracted` is still the boolean gate and the per-type budget that would cap arrivals is still unbuilt (Q89, checked 2026-09-15); the Lair is the uncapped special case and the Temple contributes 2 iff its Hand exists.

6. **Rebel** (`CreatureAngerSystem` implements Leave only; `RebelLeavingPercentage` 70 / `RebelBecomeGoodPercentage` 15 / `RebelDefectPercentage` 15 are read by nothing).
7. **Combat** (Q8) — the largest missing mechanic, the first to subtract health per tick, and M7's head. **The damage formula is now READ end to end and BUILT as a rule nothing calls** (ADR-0153, `combat-damage.md`): stance multiplier, level scaling, negation, bearing bonus, both player modifiers and the Monk bracket, with the original's truncate-toward-zero rounding reproduced. There is **no resistance step** (§6.1.1; whether v1.70 applies resistance at all is **Q335**). The cadence is `combat-cadence.md`: the swing waits on `def+0x5CA`, **not** `MeleeRecharge` (Q327). **What is missing is not arithmetic but how an attack is entered**: the routine's real entry is `0x00498E30` — `ForceGaps.py` had named it four bytes late — and **combat is entered by starting creature action 17** in the 81-record action table at `0x006E4198`, not by a creature state (Q338, `creature-action-table.md`). **Target selection is now read end to end** (`combat-target-selection.md`, 2026-09-21): `FightStyle` picks one of five arms, each the same scan over every candidate differing in one scoring term, accepting on line of sight within `DistanceCanSee` or a walkable route within `DistanceCanHear`. **Melee combat is BUILT end to end, fear and the worker's arm included** (ADR-0163–0166, `combat-acquisition.md`, `combat-fight-state.md`, `combat-fear.md`, `combat-alarm.md`): `SelectAVictim` runs from `ProcessFight`; all five arms score; a fighter runs up, swings every `MeleeRecharge` ticks and lands `MeleeHitDelay` later through `MeleeDamageRules`; a struck creature turns on its attacker when its own target is double-teamed; the enemy heart is a target. Every tick a fighter radiates its `Threat`; a creature over its `Fear` backs off, then runs for lair, heart or room; one under a third of its health far from its heart runs at once. An Imp fights anything within a tile of its heart and enemy Imps anywhere, withdraws from a hero away from its rooms, and outnumbered near them raises the alarm: its room's creatures run to the hero. State 8's walk and the victory pause are READ, not built (`combat-approach-and-victory.md`: re-selection every tick, a ten-call reroute, a chase limit; celebration lasts one animation, Q376). The spell arms are READ (`combat-spells.md`): in reach and off cooldown a caster starts action 16, which fires a shot thing `ShotDelay` ticks later with a lead on the target; the survey keeps the spell whose next-ready time is latest. The shot thing is READ too (`shots.md`): straight flight at `Speed` a tick, the first enemy within its radius, `ShotData1` flat contact damage, explosions and gas by linear area, status spells as process handlers whose creature side is Q396. **BUILT (ADR-0167, `HashVersion` 72):** a caster surveys its attack spells, engages at the spell's range, casts when the cooldown allows, and the shot leaves after the wind-up, flies at its speed and lands its data word flat; the status spells' creature side is READ (`creature-status-effects.md`: countdown words, damage multipliers, freeze and stun states, fire is cosmetic) and not built. Left: Q337's possession scalar; heroes and hero parties follow.
8. **Computer keeper — now milestone M12, pulled ahead of breadth** (plan §8.1: skirmish *is* the keeper AI, and it is the least-solved system left after pathfinding). **DK2's own is being read**: its per-turn entry, nine-arm `Task` dispatcher and three-slot event ring are `computer-keeper-tasks.md` (2026-09-22); the seven task handlers' bodies are the next read. Ours meanwhile, `docs/KEEPER_AI.md` §12.7 steps 2–5: an `IWorldView` facade (no hash change); §12.3's missing commands and §12.4's messages (each bumps the hash and wants an ADR); threat evaluation, attack planning, Call to Arms, traps, prisoners, retreat; `KeeperAIPersonality`. §12.8's four deviations (own RNG stream, flood-fill connectivity, Chebyshev range, batched room placement) each want an ADR if they survive. **All seven task handlers and the room planner are read** (`computer-keeper-handlers.md`, Codex, 2026-09-22): rooms come from the record's `RoomList` order and a fixed 16..26 tail filtered by availability, never from needs; a footprint is a `sqrt(DefaultRoomSize)` square searched deterministically along a direction; tunnels go from the owned room nearest the deposit; `RespondToAttack` moves at most `TendancySpeed` creatures a turn. Left: three unnamed words, the expansion-strip tie-break.
9. **M5 — the oracle rig.** DK2 can run under the Flame built on 2026-09-07 (back up `PATCH.dll` first); plan §2.2.1 item 4 (a Ghidra import for the `sgmap`) no longer waits on anything; §11's query tool is **built and merged** (`dk2tools sgmap`, 2026-09-21), and its `at` query turns a recovered offset into a named field, which is the shape the binary work actually needs. Three runs are recorded under `docs/experiments/` and runs 1–8 under the gitignored `reverse-engineering/dk2runs/` (**the tag captures 9–14 were not kept — `EXPERIMENTS.md` and `player-task-pool.md` are the only surviving record of them**). 23 of the 27 scenarios assert; the four that do not say in the file why they cannot (ADR-0150).
10. **Unity:** loading the converter's glTF/PNG (Unity ships no runtime glTF importer); DK2's wall pieces instead of boxes (Q71/Q72, `wall-rendering.md`).

**Not in the order above — a scheduling question, raised 2026-09-10.** The creature state machine's shape is settled research (`creature-state-table.md`: 281 states CONFIRMED, `SetState`'s group/category rule in §2.1, the 32 category hooks in §6), but three parts of it are engine architecture rather than breadth and none is tracked anywhere: a **resume slot** for the utility states category 2 marks (nothing in `CreatureState` saves a caller's state); a **transition gate**, since 35 sites assign `Activity` directly across seven systems and nothing refuses an illegal move; and the **category update hook** that ADR-0083 found runs before the state handler, carrying the prison drain and the training award. `GOAL_ALIGNMENT.md` gap 2 defers breadth until after M5 and these add no behaviour, so whether they precede it is the owner's call. Reading the handlers is separately large and has no shortcut: 259 of the 264 serve one state each, and Flame names the behaviour of none.

**Known "ours, not DK2's" left in the code, each flagged at its site**

- Treasury deposit order is tile-index order (`GoldStore`; Q35's withdrawal half unread).
- Which lair tile a bedless creature walks to, and the 12-tick idle cadence (Q157; `lair-acquisition.md` §7).
- Task stealing: a nearer imp does not take a task from a farther one (Q155; DK2 does).
- `ManaSystem` runs for every player, not only keepers (deliberate, ADR-0052).
- `RoomRules` does not model a level's per-room `Availability` (ADR-0040 notes the gap; only `PortalRules` reads availability).
- The portal tally is never zeroed (ADR-0086; DK2's reset is vtable-only and unread).
- `HandSystem.Drop` gates by `PlayerIds`/worker rule (ADR-0077) and wrongly lets Imps use portals (ADR-0084). **The Heart disposal route is BUILT** (ADR-0148, owner 2026-09-15): the heart's centre tile takes a worker and refuses everything else, the portal's hole takes anything but a worker. Q196's object-drop gate is in the input layer.

**Housekeeping**

- The pin's empty world (Q206): give `TestWorld.World` a creature so `HashInto` changes fail the pin directly.
- Scenario tick budgets were written against 20 ticks/s and still cover five times the game time; trim them. **This note understated itself and cost a session** (2026-09-15): the same rate change did not merely lengthen runs, it DISARMED four of them — work tuned in ticks stopped reaching its subject entirely (Q316, ADR-0150). A budget that is too long is cosmetic; an experiment that no longer runs is not.
- No `README.md` at the repository root.
- Re-run the zero-value audit (ADR-0085's method) whenever a new record type gains a reader; it has been run on the balance table, the creature record and the object record (Q213), not on terrain or rooms.

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
| 0084 | Portal disposal removes non-Imps with their gold; Imps are refused there and discarded through the Heart |
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
| 0112 | `PlayerState.GoldMined` counts gold **released** by a swing, credited to the **miner's owner**; condition type 31 compares it. `HashVersion` 51. The first change to move the oracle: level 1 goes to 4 columns, 2 past turn 1 |
| 0111 | **DK2 names its own fields.** Ten debug dumpers print ~800 field names across creature, player, room, computer keeper, placed object, world, creature job/needs, steering body, task pool and level statistics — indexed by `debug-dumpers.md`. Condition type 32 is mana; 71 is slaps *received* and stays blocked. `HashVersion` 50 |
| 0110 | A player tree carries its player (`TriggerPlayerRoot`), which unblocks the 30 player-owned condition types; the first is 30, the keeper's gold, confirmed twice over. `HashVersion` 48, then 49 with ADR-0108. Deviates on a Dungeon Heart flag we do not model (Q263) |
| 0109 | Player condition types 48, 53 and 56–61 are answered **false**, not declined: their jump-table entry is the dispatcher's zero-return epilogue. No behaviour or hash change; the campaign stall frontier falls 777 → 616 |
| 0108 | All 220 hero parties, their nested members and the one effect generator are ordered verbatim world state without behaviour. Party members are not placed creatures; `TriggerTree` remains independently sourced. Took `HashVersion`/`FormatVersion` to 49 (47 on its branch; renumbered by the merge) and opened Q254–Q259 |
| 0107 | An action returns what its dispatcher returns whether or not we perform it; `Blocked` becomes conditions only and `Unhandled` holds what DK2 itself refuses. Took `HashVersion`/`FormatVersion` to 46. **Bought no reach**: all 5,408 shipped action records are leaves, so the oracle did not move |
| 0106/0139 | All 940 doors and 959 traps retain ordered authored state; 0139 adds derived construction state and player-condition 27/28 counts, but no door navigation or trap firing. All door trigger ids are zero. `HashVersion`/`FormatVersion` 57 (`doors-and-traps.md`) |

Standing owner rulings that are not ADRs but are CONFIRMED from the original and must not be reopened: a keeper has at most one dungeon heart and losing it is defeat (Q40); the mana ceiling is 200000 from the first second and never moves — `DungeonHeartMaxManaIncrease` and `MaxManaGain` are not ceiling contributions (Q37/Q37a); imps gain speed with level (Q118c/Q119); the Mercenary Portal is what the game calls the room the editor labels "Hero Portal" (Q211).

---

## Conventions that are easy to violate by accident

- **A balance constant hardcoded in simulation code is a bug** — read it from `GameDatabase`. `GameDatabase[VariableTypeId.X]` **throws** on a missing value, on purpose, so a system that reads a *new* variable breaks every synthetic database until the variable is added to `TestWorld.Variables()`, `Dk2TerrainFixture.Variables()` and `ScenarioTests.Variables()`. Add the entry; never default the lookup. The one sanctioned exception is `ObservedCorrections` (ADR-0023), which has five admission conditions.
- **A number that does not match how the original plays is not licence to change it.** Reproduce the data, record the discrepancy as an open question, let the scenario output show it. Only a measured, arithmetically impossible shipped value goes through `ObservedCorrections`.
- **Changing simulation state breaks the pinned hash — that is the design.** Bump `GameSimulation.HashVersion`, keep `WorldSnapshot.FormatVersion` equal to it, and update the literal in `DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` in the same commit. Never loosen the assertion. The pin's world holds two creatures (Q206), so a change *to* `HashInto` moves it — but a field left *out* of `HashInto` is still unguarded, population or not.
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
- *An id that appears as no constant may be stored encoded, and a decode's off-by-one shifts every row.* Speech references are held as `-(10000 + id)`, so no Mentor line's id exists as an immediate and a constant search "proved" the taunt pool empty. Then a bias of 10001 instead of 10000 mislabelled all 1542 lines by one and produced a confident, wrong "this line is unreachable". **Discriminate a decode with a set whose size is fixed independently** — 13 variants against exactly 13 spell-upgrade lines settled it where any single plausible-looking row could not; the owner's recollection of two lines confirmed it (Q282, `mentor-speech-and-notifications.md` §3).
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

`docs/research/open-questions.md` is the record. The ones that gate work today, by how they are answered:

**Cheapest — one look at the running game** (`observation-checklist.md` is the list to hand over)

| Q | What to watch |
|---|---|
| Q86 leftover | a **worn** portal lost and re-claimed — does the tally reset? Needs a skirmish map with two claimable portals (`The Deep End`, `Siege`, `U Turn`; `dk2tools kwd portals` finds them). Also: portal 1's arrivals after 03:18 were missing from the second run |
| Q202 | slap a creature to Unhappy with a bed available; does sleeping bring it down? Names anger buckets 0 and 5 |
| Q177 | does the aimed (drop) bonus carry from one marked tile to the next? The binary says yes |
| Q118 | how much does water slow a creature (a Vampire in a lake: 25 s to die = per-second hazard, 1.25 s = per-tick) |
| Q103 | the 240 s / 9 s sleep cycle — corroborated by stopwatch (owner, 2026-09-07), not settled. Q107 and Q109 answered with it |
| Q223 | ~~load Level13~~ **CLOSED 2026-09-16**: the grid owns a structure, not the ROOM record, whose player is the constant 1 across all 133 hero structures; owner confirmed both Cherish gates read unclaimed |

**Binary reads** (`docs/RESEARCH-QUEUE.md` keeps the working list and the Ghidra command)

| Q | Read |
|---|---|
| Q217, Q218 | who sets the keeper's casino flag at `player+0x20D`; what passes the research adder `FUN_004c2dc0` a negative amount (`CallScanAll.py`, all blocks) |
| Q216 | does slapping a researcher visibly triple research (needs the research tree) |
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
| Q5, Q3, Q8 | **Q5 is answered and the replacement is built and wired** (ADR-0155, ADR-0157; `navigation-and-steering.md`, `navigation-mesh-and-search.md`, `steering-behaviours.md`). DK2 routes over a constrained triangulation with a greedy two-ended search and a funnel, then steers with four weighted forces integrated by Euler; a creature turns to face its waypoint before it moves. Phase 1 (router) and phase 2 (`DK2.Simulation.Steering`: the four behaviours, route window, turn, integrator with slide and wall clamp) are built and tested, and phase 3 wired them on 2026-09-17: `WorldSteering` and `CreatureMovementSystem` drive every creature. Left: `Obstacle` furniture, the remembered victim, the sixteen route requests that pass a register (Q350; the 118 literal ones are read and built as `MoveOptions`) and Q28's open half. Doors are in the navigation byte since ADR-0158 (`DoorSystem` opens and closes them; Q353 lists what of the door model is still unread) — routing refuses a corner-only pair in DK2 as in ours, so the crossing the owner saw was steered or not at all; the experiment is in the Q28 row |

**Triggers — what still stands between the banks and a winnable level** (`trigger-semantics.md`, `trigger-state.md`; `dk2tools kwd triggers` shows a level's tree, `kwd trigger-params` a parameter's corpus distribution)

| Q | Open |
|---|---|
| Q229 | flag id **128** is not a flag — both dispatchers divert to a dword at `+0x72A2`, set a companion at `+0x72A6` to 1, call through virtual slot `+0x348`, and take the operand's full 32 bits. Decompile that slot and find what consumes `+0x72A2`. `TriggerState` carries it as an opaque value; do not name it first |
| Q232 | timer id **16** is not a timer — the condition path compares against `0x10` (`0x005184EB`) and reads through virtual slot `+0x34C`, and the bank's only indexed store is action type 8's clock stamp, so a write naming 16 does not reach it either. **All 86** action type 23 records in the campaign target it and five type 2 conditions test it, so it is live, not a curiosity. `TriggerState` refuses the id |
| Q230 | a flag id above 128 would index past the bank into the timers; **no shipped level does it** (asserted over all 66 levels in `TriggerStateOnRealDataTests`) and `TriggerState` throws instead of reproducing the overrun. Faithfulness and sanity conflict, so this is a deliberate decision if a real map is ever found doing it |
| Q226, Q227 | the trigger selectors and fields DKII.EXE reads without revealing a role. Exposed as `Unknown_*` rather than as an external guess; action type 24 alone has 827 shipped records |
| Q225 | do DK2-authored files corroborate the trigger types the shipped corpus never uses? Generic 3/5/25/29 and their action counterparts have body-read paths in the executable and **no** instance in the 9,161 shipped records. Needs a map made in the original editor; not M6 for any shipped level |

**Answered and removed from the lists above** (the ADR is the record): Q66, Q97, Q107, Q109 (owner, 2026-09-07); Q204 — `player+0x76 & 4` is `CallToArmsOn`, not defeat (ADR-0145); Q222 action points (ADR-0103); Q224 doors and traps (ADR-0106); Q228 the root evaluator's cadence.
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
- A zero-valued variable with a live reader need not be inert. Variable 112's inherited sacrifice name is wrong: its zero **doubles converted-creature prayer mana** (`x += x × (pct + 100) ÷ 100`); no sacrifice path reads it (`sacrifice.md`, Q212).
- `RoomSellSystem`/`RoomBuildSystem` already refuse on a dungeon heart; Q207's real gap is `ConvertRoom` excluding a *fallen* heart.
- In the MAP record player **2** is the unowned marker, not 0 (`PlayerIds`, `kwd portals`).
- Only an imp may be dropped on unclaimed Dirt Path (Q198, ADR-0077); "the handler does not refuse" proved nothing about what the UI offers.
- `IncreasedWorkRateDurationFromSlappingSeconds` was recorded as read by nothing and governs a second slap effect; write "no reader found", never "no reader" (`RESEARCH-QUEUE.md`).
- `DK2.Convert` exists (KMF → glTF/GLB, textures → PNG, SDT → WAV) — documents saying "there is no converter" are stale; what does not exist is a Unity branch that loads its output.
- A THINGS record's `X`/`Y` are **tile indices**, not `Fp32` subtile units; `InitialHealth` is a **percentage** of the maximum (nothing in the corpus exceeds 100); `RoomThing.RoomType` is a **terrain** id and a ROOM record is not a room to build — the MAP grid already holds it (ADR-0101). All three were transcribed from an external field list and all three were wrong.
- A trigger timer threshold in a `.kld` is in **seconds**; DK2's level trigger initialiser multiplies it by variable 19 once at load and the comparison consumes **ticks** (ADR-0102 amendments, Q233). The ADR's first reading — and any document repeating it — said ticks. A *stamp* in the timer bank has always been a tick count.
- The trigger flag and timer banks are **level** state, not per-player (ADR-0102) — the player-context dispatcher `0x004C8620` is the one call site that could have shown otherwise and it loads the global instead. The timer bank is **16** slots, not the 256 the masked write path suggests. The byte the external field list calls `Flag` is an **operation and addressing mode**; the flag it acts on is a different byte. Flag id 128 and timer id 16 are one past their banks and are neither a flag nor a timer (Q229, Q232).
- A pure evaluator exists only for one tag-213 type-1 or ordinary type-2 record. It does not walk a tree, retire repeats, or run an action; Q228 settles its once-per-turn caller and `trigger-tree.md` records those still-unimplemented execution semantics (ADR-0102).

- **`DispatchMap.py` prints `index + 1`, but a dispatcher's true type is `index + base`** — base 19 for player conditions, 15 for creature actions, 10 for player actions, 1 for world actions. Three independent checks fix the player-condition offset at +18 (types 30/31/32 land on the three minimal counters, 48/53/56–61 on the zero-return epilogue, 73 on `0x004C8500`). Reading the printed number as the type is wrong and would misfile every arm.
- **Condition types 29 and 47 are answered whole, not compared** — each reads one byte and returns a bare boolean, and neither record carries a `Comparison` or a `Value`. Routing either through the shared `PlayerCounter` compare path throws on the missing key. Type 29 is keeper-spell availability, `Availability` kind 5, tested **strictly `== 3`** where a creature also accepts RESEARCHABLE 2 (ADR-0120); id 0 means *any* spell.
- Condition type 19's `CreatureId` **0 is not "any creature"** — it is the owned total *minus every worker*, while a named id counts workers normally (ADR-0113). Bit 0 of the creature definition's `+0x686` is `IsWorker`, and exactly Imp and Dwarf carry it.
- The player object's `+0x5A`, `+0x62`, `+0x6A`, `+0x72` are **four availability tables** — rooms, doors, traps, spells — not a four-entry keeper-spell array; `player-object.md` said the latter until 2026-09-09. Each entry is nine bytes, `TypeId`/`Availability`/`Upgraded`.

- **The player task pool is a work pipeline, not a set of marks** (runs 9–12). A tile sits in several kind lists at once and migrates as an imp takes it up, so **a row is not an event** — entering the pool and leaving it are. Kinds 1 and 2 are dig-rock and dig-gold (owner), 3 is claiming and 6 is reinforcing; the entry layout in `debug-dumpers.md` was four bytes low, because that dumper pairs offsets against a cursor at `record + 4`.
- **A finished dig and a cancelled one both leave the pool, and the `Worker` count does not separate them** — the owner cancelled a mark with three imps on it. What separates them is the claim that follows, since a dug tile is claimed and a cancelled one stays rock (ADR-0131).
- **A capture records what the game did, never who caused it.** Two discriminators inferred from the shape of a capture were both wrong within a day — arrival cadence, then the worker count. A player clicking tiles 1.5 s apart looks exactly like the game trickling out its own work. Ask for labelled play before attributing anything.
- **The player record base is `playerArray + (id−1) × 4389`**, one stride earlier than it reads: the pool formula's `id × 4389 − 0x20` resolves to `player+0x1105`. A room's `FirstSlab`/`LastSlab` are the **ends of a list, not a minimum and a maximum** (a settled room ran (8,30) to (10,26)), and while imps build a room its `Size` runs ahead of them.
- **The runtime map is reachable**: `map = world + 0x9DA`, width `+0x0C`, tiles `+0x14`, 16 bytes each, health `word +0x06`, terrain `+0x0A` (`runtime-map.md`). It was twice set aside as optional and is not — it turns the work-rate questions into measurements.
- **DK2 claims a floor tile in about twelve ticks, three game seconds; ours takes four** (Q292, 16 samples at 20 Hz). Reinforcing is ≥61 against our 50. Neither constant has moved: 825 fits twelve swings of 55 or three of 220 a second, and those differ in mechanism rather than arithmetic.
- **A fixture can build a tile DK2 cannot** — Dirt Path with an owner (it is not `Ownable`), Claimed Path without one (claimed *means* owned). Both behave uninterpretably, and a test standing on one measures nothing.
