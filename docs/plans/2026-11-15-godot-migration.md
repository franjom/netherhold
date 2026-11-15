# Godot migration: from the Unity adapter to the Godot adapter, then no Unity

> **For agentic workers:** use the `godot-developer` agent for Godot work. A step that moves logic
> into `src/` is ordinary simulation/presentation work with tests. Follow AGENTS.md's
> reviewer/documenter passes before every commit. Steps use checkboxes (`- [ ]`).

**Status:** execution plan, 2026-11-15, approved by the owner the same day. Step 1a is done (`InstallHudSources`, `InstallFrontEndSources`, `EngineCameraPaths`, `FrontEndBox.Origin` in `DK2.Presentation`). Step 1b is done: all twelve group-1 panels and the group-1 helpers. Next is 1c.
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
- [x] 1b. Port group 1, one panel per change, each checked with a Movie Maker capture against the
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
      is open and the Unity runtime are unverified) and the Game Options pages (`ClassicGameOptionsPanel.cs` over
      `GameOptionsHud`: the root, graphics, sound, controls and keyboard pages; hover and press; a slider drag
      holds until the left button is released; Back and Continue; key capture through `ClassicKeyboard`, with
      `capturingKey` now `_optionsPages.CapturingAction != null`; the settings persist through
      `GameSettingsFile` to `user://game-settings.ini` (`%APPDATA%/Godot/app_userdata/DK2.Godot/`, never Unity's
      file) or the file `--settings <file>` names, a failed load warning and using the defaults, a failed save
      warning; `ApplySettings` sets the game speed, the key bindings and the HUD scale, and its panel list is the
      twin of `DungeonRunner.ApplyHudScale`'s, so the two change together; moved into `DK2.Presentation` with
      tests: `ClassicHudControls.OptionsMenuShown` and `GameOptionsShown`, `OptionsPagesController.CapturePressedKey`,
      now the one key-capture loop for both adapters' front ends and in-game pages (it replaces Unity's two
      `Enum.GetValues(KeyCode)` loops), `GameOptionsHud.CaptureKey`, `GameBindingInput.Modifiers` and `KeyPressed`,
      and `ClassicKeyNames`, the one table of 99 key names, which equals Unity's `KeyCode.ToString()` names (a
      script comparison found no difference), used by both front ends and `ClassicKeyBindings.Name`; they are not
      DK2's own names, [Q857](research/open-questions.md); Godot's controls page now shows names instead of
      numbers; the slider test sounds are dropped; when two keys go down in one frame during capture they are
      tried in scan-code order, where Unity tried `KeyCode` order, and DK2's order is unknown (`0x0042B770` takes
      one key and modifiers pair per call, and the path from the OS message is unread); Movie Maker probe
      captures with synthesized input show all pages, master volume 25 after a click on the track, the Sniper row
      capturing and then bound to J, `MasterVolume=25` and `Key.1=36,0` in the settings file, and Continue closing
      the menu, matching the Unity HUD captures in `build/igo-154026` for layout, labels, tints and key names;
      real input, hover lighting, slider drag motion, the key-list arrows and thumb, the settings IO fallback and
      the Unity runtime are unverified; whether a wheel scroll ends a slider drag in DK2 is
      [Q858](research/open-questions.md)). Not wired on the Game Options pages: sound volumes, switches, quality,
      headphones and QSound (no sound player), gamma, shadows, water and environment mapping (no 3D renderer),
      scroll speed, mouse sensitivity and invert (no camera or possession); their values change and nothing acts
      on them. `_Ready` reads the saved settings, so a Movie Maker capture recipe passes `--settings <temp file>`
      for repeatable frames. Godot opens the query panel on the viewer's first creature only with the
      test-scene argument `--query-first-creature`, as it has no world picking yet, and
      the battle tab with `--stage-battle`, which spawns a hero Knight on the tile of the viewer's
      first creature, Level1 having no early fight. Not wired in Godot for the battle tab: the
      blades' zoom, the glides and the fly-in (need the camera). Godot opens the options menu at
      start with the test-scene argument `--open-options [Entry]`, which refuses an entry the menu does
      not support (`GameOptions`, `Save` and `Load` open their pages), lists saves from the directory
      `--saves <dir>` names, and reads the settings from the file `--settings <file>` names. Not wired on the save and load pages: the submit, that is writing or
      loading a save (Godot prints that the page's submit is not wired; it needs the runner's campaign
      profile, `LoadFrontEndProfile`, and `ApplyCampaignCarry` with `_playedSlot`, the session mode and
      the return page, and a save built from a made-up profile would be harmful, since Unity's
      `LoadSession` writes `session.Profile` over the player's profile file); the save folder (Godot
      lists `user://Saves`, `%APPDATA%/Godot/app_userdata/DK2.Godot/Saves`, not created at startup, which
      is not Unity's `persistentDataPath/Saves`, so Unity's saves are not listed); the open and close
      sounds; and `PointerOverHud` leaves the save page out, as in Unity, so tooltips from panels
      beneath it may show (see [Q855](research/open-questions.md)). Not wired in the options menu: the session action after Yes on
      Restart, Return to Menu and Quit, taken and dropped with a `GD.Print` because it needs the
      runner (step 2); the other HUD keys (GUI toggle, heart, portal and fight
      navigation); `AllowOptionsKey`, passed true, where Unity's scripted camera path holds the key
      back (step 2); and the open and close sounds 0x139 and 0x13A. Godot also has the
      full map (`ClassicFullMap.cs` over `FullMapHud`: M, scan 0x32, opens it, and M, Escape or a
      left or right press closes it; it draws the map image, the creature squares, the keepers' legend and the
      hovered room's icon on two `HudCanvas` layers over one reused `Image` and `ImageTexture`, uploaded
      only when a tick changes a pixel; the panels below hide through `_hudControls.Visibility(camera)`,
      Unity's `PlayedHud`, and the resource strip through `SetVisibility`; `PointerOverHud` includes
      it; a second press in the frame that closed it is ignored, because the facade's `Visible` lags a
      frame; test-scene argument `--open-full-map`; the `ApplySettings` panel list does not include it,
      as neither adapter scales the full map). New in `DK2.Presentation`: `FullMapImage`, the CPU twin of
      `DK2FullMap.shader` (class 1 is black); `PngChunks.WithoutColourSpace`, which drops gAMA, cHRM,
      sRGB and iCCP. Moved from Unity as pure moves, with tests, and every Unity call site calls them:
      `ClassicHudControls.FullMapShown`, `FullMapHud.Press` and `Action` (a left press goes to the tile
      and closes, a right press closes, any other press is consumed and does nothing; both adapters apply
      `ApplyHudAction(FullMapHud.Action(press))`), `MinimapPalette.FromTopRow` (Unity's per-pixel
      `GetPixel` became `GetPixels32` plus `FromTopRow`, the same row), and `IMinimapAssets` with
      `InstallMinimapAssets` and a palette-only `PalettePngOf`, from Unity's `DungeonMinimap.cs`.
      Godot's PNG decoder applies gAMA (231,24,24 decoded as 209,2,2) and nearly every shipped PNG
      declares one (FrontEnd.WAD 356 of 371, EngineTextures.WAD all 102), where DKII.EXE draws the
      stored bytes ([0x005B6030](research/functions/005B6030.md)); so the map's palette
      (`ClassicMinimap.DecodePalette`) and the front end's art (`ClassicFrontEndMenuAssets.DecodePng`)
      decode through `WithoutColourSpace`, and the Godot front end now draws brighter than before, as
      Unity does. That Unity's `LoadImage` ignores gAMA is assumed and unverified
      ([Q859](research/open-questions.md)). A Movie Maker capture on Level1 (fog on) matches Unity's
      `level1-heart-courtyard-1024x768-fullmap.png` byte for byte in four map colours, (231,24,24),
      (142,0,0), (126,109,33) and (185,0,34), and in the "Player" legend's place and colour;
      synthesized Escape, M, left and right presses opened and closed it. Real input, the room hover,
      a legend with several keepers, the Unity runtime and the front end's brightness against Unity's are
      unverified. Not wired on the full map: the camera move on a left press (Godot prints that it is not
      wired; step 2), the camera preview at the top right (needs the 3D world); the dig-tag and pickup
      previews are step 2 below, and whether the wheel does anything over it is
      [Q860](research/open-questions.md).
      The twelfth panel is the minimap (`ClassicMinimap.cs` over `MinimapHud`: the disc, frame, markers,
      north sprite, corners and zoom; the options corner opens the options menu, a right click on a tile
      opens the full map, `PointerOverHud` includes it, the hover text asks `MinimapControl`, and
      `ApplySettings` sets its HUD scale, a line in the twin list of `DungeonRunner.ApplyHudScale`). The
      disc is `MinimapDiscImage` in `DK2.Presentation`, the tested CPU twin of `DK2Minimap.shader`'s tile
      and marker passes (`[Dk2Behavior(StronglyInferred)]`, as `MinimapHeartTrail`'s is, while records
      0x0041A9B0, 0x0041ADA0 and 0x0041C660 are HYPOTHESIS), redrawn only after a capture or a pose change
      into one reused texture. The twins differ on the rock backdrop: the CPU one reads `Map-BG` top row
      first, which the 0x0041ADA0 record's "matches the raster indexing" supports, while Unity's shader
      samples a `LoadImage` texture bottom-up, so Unity's rock is flipped; both files say so
      ([Q861](research/open-questions.md)). Godot has one `WorldView` and one `MinimapHud`, owned by the
      minimap, with one full `Capture` per tick; the full map borrows the model (`ClassicMinimap.Model`),
      so the grid-only capture is gone and `ClassicFullMap.Open` takes the minimap. The pose is the
      level start's, since no camera is ported (`ClassicCamera.LevelStartFocus` and `LevelStartYaw` 256).
      Moved from Unity into `DK2.Presentation` as pure moves at every Unity call site: `ClassicHudControls.Accepts`
      (the `ApplyHudAction` gate), `ClassicHudControlLayout.TryGuiButton`, `MinimapHud.Press` (a corner's
      action, or `ToggleFullMap` for a right click on a tile; it replaces `DungeonMinimap.HandleCornerPress`,
      now `HandlePress`), `MinimapHud.FixedFocus` and `DiscPixels`, and `ClassicCamera.LevelStartFocus`
      for a viewer, which replaces the runner's `startingCameraPlayer`. Godot drops a minimap navigation
      press while a menu is modal or input is suppressed, as Unity does (whether DK2 does is
      [Q862](research/open-questions.md)). Movie Maker captures on Level1 (fog on) match Unity's
      `level1-heart-courtyard-1024x768-minimap-yaw256.png` by eye in the frame, corners, north sprite,
      rotation, backdrop shading and rim; the differences have known causes (Unity has fog off and draws
      the camera footprint, and the blink phase differs). Synthesized clicks showed zoom level 1, the options
      corner opening the menu and a right click opening the full map. Real input, corner hover with a real
      cursor, pixel equality against the GPU, HUD scales other than 100% and the Unity runtime are
      unverified. Not wired on the minimap: the camera footprint (step 3), the left-click camera move
      (Godot prints the tile; steps 2-3), the query and sell corners (they light up and print, and post no
      `GuiButtonCommand`, so a Godot session's command stream differs from Unity's for those clicks; step 2,
      tool state) and the heading during the opening camera path (step 2, scripted view). In Godot the
      bottom panel's `CancelPointerGesture` runs before the panel's own `PointerDown` when a minimap action
      applies; no double action has been found. Not wired in
      Godot: sound and
      speech, the Level6a torture-speech sink, the goto camera glide,
      scripted-view visibility (step 2). All twelve panels are in. The Unity helpers are settled:
      `ClassicBitmapText`'s placement rule is now `BitmapTextPlacement.Place` and the subtitle box, scale and
      size rules are `SubtitleLayout` (the remake's layout, not DK2's), both in `DK2.Presentation` with tests,
      leaving Unity the atlas and the blit; `ClassicHudScaling` stays Unity-only, since it is UI Toolkit
      plumbing whose rules are already `HudScaleLayout`; `MinimapFrameVisual` is only `RgbaImage` to
      `Texture2D` for six Unity files, with no Godot counterpart to write (Godot's panels decode through
      `HudCanvas`). The screen-size rule (rounded logical width over full height) is now `HudScaleLayout.Screen`;
      every Unity panel uses it (the hover HUD still sizes from
      `UnityEngine.Screen`, not a fixture viewport, a possible cause of a mis-sized bar in a probe
      capture, unverified). The bottom-docked panel origin in whole pixels (`BottomOffset` scaled, rounded
      half up) is `HudScaleLayout.BottomOriginPixels`, which the six Godot panels that place themselves
      (bottom panel, message tabs, creature overview, creature query, battle tab, minimap) call instead of
      each repeating the expression. Unity at runtime was not run, and no subtitle was seen on screen.
- [ ] 1c. Fix `FrontEndMenuController.UpdatePointer` (`DK2.Presentation`), which allocates (LINQ
      `First`) on every hovered frame. Also `KeeperResourceView.Capture` (`DK2.Presentation`, shared
      with Unity), which allocates two `int[]` per tick. Done: `MessageTextTables` (`DK2.Presentation`, both adapters) takes
      the briefing file from `BriefingTextTable.FileName`, as `CampaignBriefingLoader` does, so
      secret-level and My Pet Dungeon objective panels show their briefing lines
      ([Q854](research/open-questions.md), whose play-through question stays open). Also the hover
      text, shared with Unity:
      `BottomPanelHud.HoverText` and `MessageTabsHud.HoverText` build strings on each call while
      hovered, and `KeeperResourceInteraction.ResourceHover` rebuilds a `Dictionary` whenever the
      snapshot (tick included) changes. Also `WorldHoverText.CreatureType`, which builds a
      `Dictionary` and strings on each call while a creature overview column is hovered. Also
      `MusicWorldObserver.Capture`, which allocates per tick (`Keys.ToArray()`, LINQ `Any` with
      closures) and now runs on Godot's tick path as well as Unity's. Also
      `WorldView.CaptureMinimapMarkers`, which rebuilds `RoomIndex` and scans the whole map for the
      heart on each captured tick; shared with Unity, and now on Godot's tick too
      (`ClassicMinimap.Capture`).

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
      runner logic that Godot's `InGameHud` duplicates. From the creature overview: the pickup
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
      session) with it; decide the save folder both adapters share. From the full map: the camera
      move to the tile under a left press (`FullMapPressed` prints that it is not wired; Unity sends
      the camera there), and the camera preview at the top right. From the minimap: the
      left-click camera move (`MinimapNavigated` prints the tile; needs the camera, step 3), the query and
      sell corners (Godot prints a line, sets no tool and posts no `GuiButtonCommand`, which Unity's runner
      posts through `ClassicHudControlLayout.TryGuiButton`, so the command streams differ for those
      clicks; tool state), the heading during the opening camera path (`ScriptedViewDirector`; Godot draws
      the level-start heading), and the bottom panel's `CancelPointerGesture`, which Godot runs before the
      panel's own `PointerDown` when a minimap action applies. Speech subtitles: Godot draws none
      until speech is in. Draw them by wrapping with `BitmapTextLayout`, packing a `.bf4` atlas into an
      `ImageTexture` and blitting `BitmapTextPlacement.Place`'s rects in `SubtitleLayout`'s boxes.
- [ ] Move the behaviour into `DK2.Presentation` or the simulation, with tests; leave Unity a
      facade over it, as ADR-0805 did for the HUD.

## Step 3: a playable level in Godot

- [ ] Terrain meshes and the classic camera (`TerrainPieces`, `TerrainLattice`, `ClassicCamera`
      are already in `src/`).
- [ ] Creatures, animation playback, shadows.
- [ ] Lighting tuned to feel like DK2 (owner exception: tuned per engine, not ported natively).
- [ ] 3D overlays, the minimap's camera footprint, the hand cursor, then the in-game HUD from step 1.

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
