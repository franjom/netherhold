# Godot migration: from the Unity adapter to the Godot adapter, then no Unity

> **For agentic workers:** use the `godot-developer` agent for Godot work. A step that moves logic
> into `src/` is ordinary simulation/presentation work with tests. Follow AGENTS.md's
> reviewer/documenter passes before every commit. Steps use checkboxes (`- [ ]`).

**Status:** execution plan, 2026-11-15, approved by the owner the same day. Step 1a is done (`InstallHudSources`, `InstallFrontEndSources`, `EngineCameraPaths`, `FrontEndBox.Origin` in `DK2.Presentation`). In 1b the bottom panel, the resource strip and the message tabs are done; the other group-1 panels remain.
**Goal:** reach parity in `godot/DK2.Godot`, then delete `unity/` and everything that exists only
for it.
**Rules:** ADR-0805 (adapters draw a `HudFrame` and forward input; DK2 behaviour lives in `src/`),
ADR-0810 (Godot toolchain). Logic found in a Unity file moves into `src/` with tests **before** it
is ported, so it is never written twice.
**Where it stands:** `docs/HANDOFF.md` (engine switch). How to run Godot: `godot/README.md`.

## Inventory (2026-11-15)

The Unity adapter is about 14,800 runtime lines (79 C# files), 3,100 editor-tool lines and
940 shader lines, under `unity/DK2.Unity/Assets/DK2/`. Classified by file name, size and ADR-0805;
group 2 is a list to audit, not a finding.

| Group | What | Files |
|---|---|---|
| done | front-end menu | `HudCanvas`, `ClassicFrontEndMenu`, its assets |
| 1. glue over `DK2.Presentation` | HUD panels whose model is already in `src/` | `ClassicBottomPanel` (+`Assets`), `ClassicResourceHud`, `ClassicMessageTabs`, `ClassicHoverHud`, `ClassicCreatureQueryPanel`, `ClassicCreatureOverview` (+`CreatureOverviewAssets`), `ClassicOptionsPanel`, `ClassicGameOptionsPanel`, `ClassicSaveLoadPanel`, `ClassicBattleTab`, `ClassicFullMap`, `DungeonMinimap`, `ClassicHudScaling`, `ClassicBitmapText`, `ClassicCreatureNames`, `MinimapFrameVisual` |
| 2. audit: likely DK2 logic | game flow and scene mapping | `DungeonRunner` (13 partial files, ~4,000 lines: startup, settings, saves, input journal, possession, cutscene, finale, level end, preview, sound, HUD, front end), `DungeonScene` (1,320), `ClassicConstructionEffects`, `ClassicCreatureAnimation`, `ClassicStatusFlowers`, `ClassicFlowerDollar`, `ClassicKeyBindings`, `ClassicSettingsRendering` |
| 3. engine-specific rewrite | ADR-0805's list | terrain and meshes (`ClassicMeshes`, `TerrainChunks`, `TransparentTerrain`, `LiquidSurfaces`); lighting and shadows (tile/torch/fill light buffers, `ClassicCreatureShadows`, `ClassicLightning`, torch flames); 13 shaders; 3D overlays (hover outline, placement, possession, `MinimapGpuMap`); `ClassicSoundPlayer`, `ClassicMoviePlayer`, `ClassicHandCursor`; `FrameRecorder`, `DebugFrameOverlay`, `UnitySimulationProfiler`, `JitWarmup` |
| 4. tooling | probes and packaging | 14 editor probes; `tools/classic-unity-probe`, `tools/game-unity-probe`, `tools/hud-probe` (delete), `tools/tester-package`; `unity/DK2.Unity.Compile`, `sync-assemblies.ps1`, `copy-runtime-assemblies.ps1` (delete) |

## Step 1: shared loaders, then the in-game HUD in Godot

- [x] 1a. Move the engine-neutral loading code both adapters need into `src/`, with tests, and
      make Unity and Godot call it: `InstallHudSources` (strings, BF4 fonts),
      `ClassicFrontEndAssets` (WAD image index and loading; PNG decode stays per engine),
      `DungeonRunner.CameraPathLoader`, and the 640x480 box origin (a `FrontEndBox` helper beside
      `ToUnits`). Delete the Godot copies (`FrontEndCameraPaths.cs`, the loading half of
      `ClassicFrontEndMenuAssets.cs`).
- [ ] 1b. Port group 1, one panel per change, each checked with a Movie Maker capture against the
      Unity probe capture of the same state. Done: the bottom panel (`InGameHud.tscn`,
      `ClassicBottomPanel.cs`; `HudSector` and additive blend both draw in Godot) and the resource
      strip (`ClassicResourceHud.cs`; mana rising once per simulated second, strip matches a Unity
      capture by eye) and the message tabs (`ClassicMessageTabs.cs`: tabs sliding in, message panel,
      Review Objective panel, right-click dismiss; the open panel matches Unity's probe capture at
      1920x1080; `PlayerNames`, `MessageTextTables` and `MessageTabsView.LocationOncePerTick` moved
      from Unity's `DungeonRunner.Hud.cs` into `DK2.Presentation`). Not wired in Godot: sound and
      speech, the Level6a torture-speech sink, the goto camera glide, the tabs inside
      `HudModal`/options, scripted-view visibility (step 2). A status line needs the tabs'
      `HoverText`. Remaining: `ClassicHoverHud`, `ClassicCreatureQueryPanel`,
      `ClassicCreatureOverview`, `ClassicOptionsPanel`, `ClassicGameOptionsPanel`,
      `ClassicSaveLoadPanel`, `ClassicBattleTab`, `ClassicFullMap`, `DungeonMinimap`,
      `ClassicHudScaling`, `ClassicBitmapText`, `ClassicCreatureNames`, `MinimapFrameVisual`.
      The screen-size rule (rounded logical width over full height) is now `HudScaleLayout.Screen`;
      Unity's `ClassicMessageTabs` uses it, `ClassicCreatureOverview`, `ClassicCreatureQueryPanel` and
      `ClassicHoverHud` still repeat it inline and switch to `layout.Screen` when ported.
- [ ] 1c. Fix `FrontEndMenuController.UpdatePointer` (`DK2.Presentation`), which allocates (LINQ
      `First`) on every hovered frame. Also `KeeperResourceView.Capture` (`DK2.Presentation`, shared
      with Unity), which allocates two `int[]` per tick. Also `MessageTextTables`
      (`DK2.Presentation`, a pure move from Unity, so both adapters have it): it builds the briefing
      file name from the level table's rule (`SECRET1_BRIEFING.str`, `MYPETDUNGEON_BRIEFING.str`,
      absent from the install), where `DKII.EXE`'s 0x00553800 table gives `LEVELS1_BRIEFING.str`,
      `LEVELMPD1_BRIEFING.str` and so on; secret-level and My Pet Dungeon objective panels show no
      briefing lines. Use `BriefingTextTable.FileName`, as `CampaignBriefingLoader` does
      ([Q854](research/open-questions.md)).

## Step 2: move game-flow logic out of Unity

- [ ] Audit each group-2 file: list what is DK2 behaviour (rules, timing, ordering, defaults)
      and what is drawing or engine plumbing. Record the split in this plan. Known from the
      bottom panel, not yet wired in Godot: HUD hiding during the opening camera path
      (`ScriptedViewDirector`), the resource flash, the flash-button hint, the tool state after choosing a panel
      slot, right-click navigation (needs a camera).
- [ ] Move the behaviour into `DK2.Presentation` or the simulation, with tests; leave Unity a
      facade over it, as ADR-0805 did for the HUD.

## Step 3: a playable level in Godot

- [ ] Terrain meshes and the classic camera (`TerrainPieces`, `TerrainLattice`, `ClassicCamera`
      are already in `src/`).
- [ ] Creatures, animation playback, shadows.
- [ ] Lighting tuned to feel like DK2 (owner exception: tuned per engine, not ported natively).
- [ ] 3D overlays, the minimap GPU image, the hand cursor, then the in-game HUD from step 1.

## Step 4: media, tools, packaging

- [ ] Sound and movies (decoding stays in `src/`).
- [ ] Godot equivalents of the probes that are still needed (headless scenes, Movie Maker
      captures; keep the Python summarisers).
- [ ] Performance targets set again in Godot (owner, 2026-11-15: Unity FPS work stopped).
- [ ] Tester package as a Godot export (export templates, committed export preset).
- [ ] CI builds `godot/DK2.Godot` (no editor needed) and the architecture gate checks Godot.

## Step 5: remove Unity (one change, at parity)

- [ ] Delete `unity/`, the Unity-only tools and the CI steps for them.
- [ ] Move every `src/` assembly from netstandard2.1 to net10.0: drop `src/Shared/UnityBound.props`
      and the polyfills.
- [ ] Supersede ADR-0690 (IL2CPP players); close ADR-0041's licensing question.
- [ ] Update AGENTS.md, ARCHITECTURE, READMEs and `.gitignore`; remove the `unity-developer`
      agent and the `unity@claude-plugins-official` plugin; drop the Unity font fallback in
      `ClassicFrontEndMenuAssets`.
