# Godot migration: from the Unity adapter to the Godot adapter, then no Unity

> **For agentic workers:** use the `godot-developer` agent for Godot work. A step that moves logic
> into `src/` is ordinary simulation/presentation work with tests. Follow AGENTS.md's
> reviewer/documenter passes before every commit. Steps use checkboxes (`- [ ]`).

**Status:** execution plan, 2026-11-15, approved by the owner the same day. Step 1a is done (`InstallHudSources`, `InstallFrontEndSources`, `EngineCameraPaths`, `FrontEndBox.Origin` in `DK2.Presentation`). In 1b the bottom panel, the resource strip, the message tabs, the hover HUD, the creature overview, the creature query details, the battle tab and the in-game options menu are done; the other group-1 panels remain.
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
      from Unity's `DungeonRunner.Hud.cs` into `DK2.Presentation`) and the hover HUD
      (`ClassicHoverHud.cs`: wooden bar, status line, tooltip popup, showing hover text from the
      resource strip, bottom panel and message tabs; which panel's text wins moved from Unity's
      `DungeonRunner.CaptureHudHover` into `HudHoverText` behind `IHudHoverSources`, which the
      Unity runner implements; Movie Maker 1920x1080 frames show status line and tooltip for
      mana, gold, the Rooms tab, a Hatchery slot, Review Objective and a message tab; no Unity
      hover capture exists to compare) and the creature overview (`ClassicCreatureOverview.cs`:
      worker column, a column per owned type, counts by mode, mode buttons, page strips, hover
      text through `IHudHoverSources`, mode and page presses; art and layout match Unity's
      1024x768 capture by eye at 1920x1080, and Movie Maker frames show the Imp portrait's status
      text, the Jobs selector's tooltip and a click switching to Jobs mode; the page strips look
      greyer than Unity's, probably the disabled tint for one page at that width, unverified;
      `InstallCreatureOverviewArt`, the `Creatures.kwd` portrait and order reader, moved from
      Unity's `CreatureOverviewAssets.cs` into `DK2.Presentation` with an `[InstallFact]` test, and
      `CreatureOverviewHud.TryPress` now takes raw shift/control/alt and decides the chord (Shift
      alone picks the lowest level, Ctrl alone the highest, otherwise a plain pickup) itself,
      where Unity's `DungeonRunner.Hud.cs` and the Godot panel each had a copy) and the creature
      query details (`ClassicCreatureQueryPanel.cs`: `CreatureQueryHud` drawn bottom-docked, no
      input; the Movie Maker capture shows "Shintaro the Imp. Blood Group: BA. Current Wage: 0.
      Gold Held: 0." above the bottom panel; no Unity capture of the panel exists to compare;
      `ClassicCreatureNames` (`InstallClassicCreatureNames`) and `CreatureQueryView`, the query
      target, its snapshot and the per-frame capture that were Unity runner fields, moved into
      `DK2.Presentation` as pure moves with tests; both adapters clear the view when a bottom-panel
      choice is selected) and the battle tab (`ClassicBattleTab.cs`: `BattleTabHud` draws the crossed
      blades, the Call to Arms cancel and the alarm right of the message tabs, fed by
      `MusicWorldObserver.Battles`, which Godot captures every tick; `BattleTabView.MenuTop`,
      `BattleTabHoverText` and `BattleTabView.Action` moved from Unity's `ClassicBattleTab` and
      `DungeonRunner.BattleTabClicked` into `DK2.Presentation` with tests; the cancel's left click
      posts `RemoveCallToArmsCommand`; a Movie Maker capture with `--stage-battle` shows the blades
      cycling through four frames; hover and press with a real cursor are unverified, and so are
      the cancel and the alarm in Godot) and the options menu (`ClassicOptionsPanel.cs` over `OptionsHud`,
      `ClassicKeyboard.cs`: Escape opens and closes it, through `GameBindingInput.HudShortcut` with the
      default `GameAction.Options` binding, scan 0x01; the root, objective, exit and confirmation
      pages, entries lighting under the pointer and acting on a left click; while it shows it takes
      every mouse press and the level pauses; `ClassicKeyboard` maps Godot physical keys to
      DirectInput scan codes and is the inverse of Unity's `ClassicKeyBindings.Key` table, so the two
      must change together, with no drift test; `OptionsHud.HudAction`, `OptionsHud.Supported` and
      `ClassicHudControls.OptionsMenuVisible`, `Modal`, `EditingText`, `TicksPaused`,
      `ModalInput` and `ShortcutContext` moved from Unity's `DungeonRunner.QueueHudInput` and
      `HudModal` into `DK2.Presentation` with tests, and Unity calls them; a Movie Maker probe with
      synthesized Escape and clicks shows the root menu, the objective page, the Restart
      confirmation and the menu closed again; a real key press or click, right and middle press
      swallowing and the tick count during the pause are unverified) and the save and load pages
      (`ClassicSaveLoadPanel.cs` over `SaveLoadHud`: the slots listed read-only through
      `SavedSessionFile.List`, an unreadable save showing its error; a press selects a slot and fills in
      the name; the name is typed through `ClassicKeyboard`, which feeds the new `TypedTextBuffer` and
      `TypedKey` in `DK2.Presentation`, the counterpart of Unity's `Input.inputString` (Backspace gives
      `\b`, Enter and KpEnter `\n`, a character above the BMP a surrogate pair; 64 characters a frame,
      refusing everything after an overflow until cleared); Back and Continue work; Escape is ignored
      while typing on the Save page and closes the menu on the Load page; `ClassicHudControls.SaveLoadVisible`
      and `SaveLoadPage(HudVisibility)` (returns `(Shown, Saving)`), `OptionsHud.RefreshesSaves` and
      `SaveLoadHud.HudAction`, which replaces its `Back` and `Continue` events and raises
      `OptionsHud.HudAction(Back/ContinueGame)`, moved into `DK2.Presentation` and Unity calls them;
      `SaveLoadHud.Type` takes a `ReadOnlySpan<char>`; Movie Maker probe captures with synthesized input
      and a scratch saves folder (four header-only saves and a truncated one) show a click on a slot
      filling in its name, space, 2, Backspace, 3 giving "Mine run 3_", Back returning to the root with
      Save and Load enabled, and on the Load page "The save is truncated (byte 3)." clearing when a good
      save is selected; a real keyboard and mouse, Ctrl+letter, the wheel, the tick count while the page
      is open and the Unity runtime are unverified). Godot opens the query panel on the viewer's first creature only with the
      test-scene argument `--query-first-creature`, as it has no world picking yet, and
      the battle tab with `--stage-battle`, which spawns a hero Knight on the tile of the viewer's
      first creature, Level1 having no early fight. Not wired in Godot for the battle tab: the
      blades' zoom, the glides and the fly-in (need the camera). Godot opens the options menu at
      start with the test-scene argument `--open-options [Entry]`, which refuses an unsupported
      entry (Game Options only; `Save` and `Load` open their pages), and lists saves from the directory
      `--saves <dir>` names. Not wired on the save and load pages: the submit, that is writing or
      loading a save (Godot prints that the page's submit is not wired; it needs the runner's campaign
      profile, `LoadFrontEndProfile`, and `ApplyCampaignCarry` with `_playedSlot`, the session mode and
      the return page, and a save built from a made-up profile would be harmful, since Unity's
      `LoadSession` writes `session.Profile` over the player's profile file); the save folder (Godot
      lists `user://Saves`, `%APPDATA%/Godot/app_userdata/DK2.Godot/Saves`, not created at startup, which
      is not Unity's `persistentDataPath/Saves`, so Unity's saves are not listed); the open and close
      sounds; and `PointerOverHud` leaves the save page out, as in Unity, so tooltips from panels
      beneath it may show (see [Q855](research/open-questions.md)). Not wired in the options menu: Game
      Options (greyed; the later panel `ClassicGameOptionsPanel`); the session action after Yes on
      Restart, Return to Menu and Quit, taken and dropped with a `GD.Print` because it needs the
      runner (step 2); the other HUD keys (GUI toggle, full map, heart, portal and fight
      navigation); `AllowOptionsKey`, passed true, where Unity's scripted camera path holds the key
      back (step 2); `capturingKey`, passed false until the controls page; the settings file, so
      the default bindings apply; the open and close sounds 0x139 and 0x13A; and the minimap corner
      button that opens the menu in DK2 and Unity, the minimap not being ported. Not wired in
      Godot: sound and
      speech, the Level6a torture-speech sink, the goto camera glide,
      scripted-view visibility (step 2). Remaining: `ClassicGameOptionsPanel`,
      `ClassicFullMap`, `DungeonMinimap`,
      `ClassicHudScaling`, `ClassicBitmapText`, `MinimapFrameVisual`.
      The screen-size rule (rounded logical width over full height) is now `HudScaleLayout.Screen`;
      every Unity panel uses it (the hover HUD still sizes from
      `UnityEngine.Screen`, not a fixture viewport, a possible cause of a mis-sized bar in a probe
      capture, unverified).
- [ ] 1c. Fix `FrontEndMenuController.UpdatePointer` (`DK2.Presentation`), which allocates (LINQ
      `First`) on every hovered frame. Also `KeeperResourceView.Capture` (`DK2.Presentation`, shared
      with Unity), which allocates two `int[]` per tick. Also `MessageTextTables`
      (`DK2.Presentation`, a pure move from Unity, so both adapters have it): it builds the briefing
      file name from the level table's rule (`SECRET1_BRIEFING.str`, `MYPETDUNGEON_BRIEFING.str`,
      absent from the install), where `DKII.EXE`'s 0x00553800 table gives `LEVELS1_BRIEFING.str`,
      `LEVELMPD1_BRIEFING.str` and so on; secret-level and My Pet Dungeon objective panels show no
      briefing lines. Use `BriefingTextTable.FileName`, as `CampaignBriefingLoader` does
      ([Q854](research/open-questions.md)). Also the hover text, shared with Unity:
      `BottomPanelHud.HoverText` and `MessageTabsHud.HoverText` build strings on each call while
      hovered, and `KeeperResourceInteraction.ResourceHover` rebuilds a `Dictionary` whenever the
      snapshot (tick included) changes. Also `WorldHoverText.CreatureType`, which builds a
      `Dictionary` and strings on each call while a creature overview column is hovered. Also
      `MusicWorldObserver.Capture`, which allocates per tick (`Keys.ToArray()`, LINQ `Any` with
      closures) and now runs on Godot's tick path as well as Unity's.

## Step 2: move game-flow logic out of Unity

- [ ] Audit each group-2 file: list what is DK2 behaviour (rules, timing, ordering, defaults)
      and what is drawing or engine plumbing. Record the split in this plan. Known from the
      bottom panel, not yet wired in Godot: HUD hiding during the opening camera path
      (`ScriptedViewDirector`), the resource flash, the flash-button hint, the tool state after choosing a panel
      slot, right-click navigation (needs a camera). From the hover HUD: world hover (things and
      tiles, needs the world and a camera); the hand cursor's resolution (Godot uses the small
      pointing finger's reach, valid only while nothing is held; Unity's runner resolves the
      held, slap and tag cursors, and "over the HUD means SmallPoint" is itself HYPOTHESIS there);
      the "pointer over HUD" membership list and the `AllowOrdinaryInput` && focus && inside gate,
      runner logic that Godot's `InGameHud` duplicates; the minimap hover text,
      which answers empty until that panel is ported. From the creature overview: the pickup
      and focus flow a column press starts (`DungeonRunner.Hud.cs` around lines 431-451:
      `CreatureOverview.Select`, the pickup preview, the hand and cursor feedback, the camera
      focus), and `QueueHudInput`'s order and the possession gate (the modal gate is
      `ClassicHudControls.Modal`, wired in Godot), none wired in Godot, where a column press raises a pickup or focus nothing handles. From the
      creature query details: the query tool (select it, world click to query, which needs
      picking, right-click cancel, the stored-special and gold cancels), the camera follow and
      turn (`ClassicCreatureQueryRules.FollowHeading`), none wired in Godot. From the save and load
      pages: move "build a `SavedSession` from the running session" (the campaign profile, its carry,
      the header, `WorldSnapshot.ToBytes` through `SavedSessionFile.Save` and `PathForName`) into
      `DK2.Presentation`, and the load (rebuilding the database, the triggers, the simulation and the
      session) with it; decide the save folder both adapters share.
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
      captures; keep the Python summarisers). `tools/hud-probe` is deleted here; its
      `HudPilot.Compile.csproj` already fails to compile (25 errors: `ClassicHudScaling` and
      `Input` missing from its compile list, stale signatures), so do not repair it.
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
