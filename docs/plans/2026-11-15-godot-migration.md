# Godot migration: from the Unity adapter to the Godot adapter, then no Unity

> **For agentic workers:** use the `godot-developer` agent for Godot work. A step that moves logic
> into `src/` is ordinary simulation/presentation work with tests. Follow AGENTS.md's
> reviewer/documenter passes before every commit. Steps use checkboxes (`- [ ]`).

**Status:** execution plan, 2026-11-15, approved by the owner the same day. Step 1a is done (`InstallHudSources`, `InstallFrontEndSources`, `EngineCameraPaths`, `FrontEndBox.Origin` in `DK2.Presentation`). Step 1b is done: all twelve group-1 panels and the group-1 helpers. Step 1c is done, so step 1 is complete. Step 2 is complete (2026-11-16): the audit and 2a to 2j are done, and 2k moved its two deferred items to step 4. 2j covered creature and object poses, mesh turns, clip requests, the creature animation player, light publishing, the lattice tracker, hover picking, the effects draw list, the heart pulse, the sparkle gate, the head table, the flower layers and the dollar clock. Step 3 is in progress: the audit, 3a (static terrain from the level-start camera), 3b (camera control and the minimap), 3c (terrain that changes), 3d (the blended stream), 3e (the pointer, cage and placement cursor: the level is playable from the pointer) 3f (objects, structures and torch meshes), 3g (creatures), 3h (entity hover and the Hand), 3i (the classic lighting) and 3o (the performance harness) are done.
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
      script comparison found no difference), used by both front ends; they are not
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
      profile (`CampaignProfileStore.Load`), and `ApplyCampaignCarry` with `_playedSlot`, the session mode and
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
      unverified. Not wired on the full map: the camera preview at the top right (needs the 3D world; the move on a left press
      is wired in 3b); the dig-tag and pickup
      previews are step 2 below, and whether the wheel does anything over it is
      [Q860](research/open-questions.md).
      The twelfth panel is the minimap (`ClassicMinimap.cs` over `MinimapHud`: the disc, frame, markers,
      north sprite, corners and zoom; the options corner opens the options menu, a right click on a tile
      opens the full map, `PointerOverHud` includes it, the hover text asks `MinimapControl`, and
      `ApplySettings` sets its HUD scale, a line in the twin list of `DungeonRunner.ApplyHudScale`). The
      disc uses the [GPU minimap pipeline](../../godot/README.md#gpu-minimap); `MinimapDiscImage`
      remains the independent CPU oracle. The top-row-first rock backdrop is preserved; Unity's shader
      samples bottom-up ([Q861](../research/open-questions.md)). This changes no confidence level in
      records 0x0041A9B0, 0x0041ADA0 or 0x0041C660. Godot has one `WorldView` and one `MinimapHud`, owned by the
      minimap, with one full `Capture` per tick; the full map borrows the model (`ClassicMinimap.Model`),
      so the grid-only capture is gone and `ClassicFullMap.Open` takes the minimap. The pose is the
      level start's until step 3b moves the camera (`ClassicCamera.LevelStartFocus` and `LevelStartYaw` 256).
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
      unverified. Not wired on the minimap (the camera footprint and the left-click camera move are wired in 3b): the query and sell corners (they light up and print, and post no
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
- [x] 1c. Allocation fixes in `DK2.Presentation` code that Godot's frame or tick path now runs
      (bytes measured on a warmed second call, behaviour unchanged, pinned by
      `PresentationAllocationTests`):
      `FrontEndMenuController.UpdatePointer` uses index loops instead of `foreach` and LINQ `First`
      (152 B to 0 per hovered frame); `HudHoverText` compares the resource snapshot with `Tick` and
      `Payday` zeroed, since no hover text reads either, and `KeeperResourceInteraction.ResourceHover`
      substitutes through a `WorldHoverText.Substitute(template, token, value)` overload instead of a
      `Dictionary` (400 B to 0 while only the tick moves; a Gold or Mana change still rebuilds, as an
      existing test requires); `WorldHoverText.CreatureType` caches the last definition and reuses its
      token dictionary (800 B to 0 for the same definition); `MusicWorldObserver.Capture` copies each
      battle's keys into a `List<int>` and uses loops for `Any`/`FirstOrDefault` in the same order
      (672 B to 216 B per tick); `WorldView.CaptureMinimapMarkers` finds the heart from the union box of
      the viewer's heart-terrain rooms in the `RoomIndex` it already builds, not a whole-map scan (equal
      to the old scan's box on Level1-10 for four players and on a synthetic two-heart map; a fallback
      scan covers 1-wide or 1-high maps). That one never allocated: the change is a CPU saving that
      nothing measures, and its test is a regression guard. `BottomPanelHud.HoverText` and
      `MessageTabsHud.HoverText` already allocated 0 B; tests now pin that. Also done:
      `MessageTextTables` (`DK2.Presentation`, both adapters) takes the briefing file from
      `BriefingTextTable.FileName`, as `CampaignBriefingLoader` does, so secret-level and My Pet Dungeon
      objective panels show their briefing lines ([Q854](research/open-questions.md), whose play-through
      question stays open). Left for a simulation-side change, not fixed here: `KeeperResourceView.Capture`
      allocates 208 B per tick, all inside `DK2.Simulation` (`ManaRules.GainPerSecondByPlayerIndex` 136 B
      for three arrays, the upkeep call 32 B, about 40 B untraced, perhaps `GoldStore.Total` or
      `CasinoJackpotRules.WageBill`; the view allocates nothing), where buffer or span overloads in
      `ManaRules` would remove it; and `MusicWorldObserver.Capture`'s remaining 216 B, three
      `WorldState.OwnedCreatures` iterators, where a struct enumerator would remove it. Also left (2026-11-16, found in
      2h): `LocalPickupFeedback.Advance`'s `_dust.RemoveAll(p => ...)` captures `milliseconds`, so it allocates a closure
      every frame (unmeasured); Godot now calls it every frame, and a backwards loop would remove it.

## Step 2: move game-flow logic out of Unity

- [x] Audit each group-2 file (2026-11-16): [the step-2 audit](2026-11-16-godot-step2-audit.md) records
      what is DK2 behaviour and what is plumbing, file by file, the move order below, and the open
      points the moves must not decide silently (creature heading interpolation, the dollar clock,
      the six tool-reset sites, journal key tables). Each move goes into `DK2.Presentation` (or the
      simulation) with tests, Unity calls it, and Godot wires it where it has what it needs (the
      camera arrives in step 3). One move per change, reviewed and documented.
- [x] 2a. Small, independent moves that remove Godot copies (2026-11-16): `HudPointerMap` (the pointer-over-HUD
      list), `GameFolders`, `CampaignSlot.ForLevel`, `LevelSubtitles` (`Open`, `Line`, `TextFolder`),
      `PointerGestures.CancelAll`, `TileHeights`, the frame clamp and ticks-per-frame into `TickPacer`
      (`MaximumFrameSeconds`, `DefaultMaxTicksPerFrame`), `LaunchArguments`. Each is a pure move with tests;
      Unity calls it and Godot calls it where it had its own copy. Godot's hover now asks `HudPointerMap`,
      so the query details no longer count as HUD while a menu is modal (Unity's term; an intended change);
      its settings and saves paths come from `GameFolders` over `user://` (no path changed); it passes the
      played slot to `WorldHoverText`; and on focus-out it now cancels the bottom panel's press and the
      world drag, queuing the slot-release sound request 0x136 if a choice slot was held, as Unity does (Godot has no sound player). Godot parses
      `--dk2Install` and ignores it (`Dk2Install.Find()`), and has no subtitles yet (2k). Unity's
      `OnDisable` and `Hud.cs:318` cancel in another order and are unchanged; the order Godot uses was settled in 2h. Not run: Godot's focus-out
      cancel and the modal query gate with a real window and pointer, and Unity in Play mode.
- [x] 2b. Tool state (2026-11-16): `HudToolState` (`DungeonToolMode` with its values unchanged, so `Dungeon.unity`'s
      `tool: 0` loads; the tool, room, panel choice, query tool and selected corner; `ActiveTool`, `StartWith`,
      `SelectKnownRoom`; `Placement`, `CageTool`, `CastSpellId`, `SpellArmed`) and `HudActionRouter` over
      `IHudActionHost` (the gate, the cancels, navigation to the host, then zoom and corners posting the GUI button
      before `ClassicHudControls.Apply`), in `DK2.Presentation`; Unity calls them. The six reset sites moved unchanged
      as named transitions, so the open tool-reset point in the audit stands. Godot's query and sell corners now set
      the tool, post `GuiButtonCommand` (Query, Sell) and draw lit; its `SelectPanelChoice` and `ActivateStoredSpecial`
      use the shared transitions, the latter with Unity's Options-visible gate (0x00411140); navigation prints "not
      wired: no camera". The gold withdraw and the right-click cancels came with 2c. In Unity, `LoadLevel` copies the live tool and room
      back into the serialized fields before cloning the runner, and editing `tool` or `selectedRoomId` in the
      Inspector during Play mode no longer changes the live tool. `ScaledIntegrationProbe` reads the panel choice
      through `_toolState` (no build compiles probes, so that fix is uncompiled). Not run: Unity in Play mode and
      Godot corner clicks with a real mouse.
- [x] 2c. HUD and world presses (2026-11-16), pure moves into `DK2.Presentation`: `PickupFlow` (`PickUpFromOverview`,
      `PostWorldCommand`; it replaces the pickup sequence copied in the overview press and the world click, so
      `LocalPickups.TryQueue` filters which pickups and `TryQueueDrop` defers a drop; the overview swallows a refused
      press and the world loop continues past it, and only the overview calls `QueuePickupPreview`), `HudPressRouter`
      over `IHudPressHost` (`QueueHudInput`'s order: full map, options, overview, minimap, gold withdraw, then the
      heart right click; `HudPress` is the frame's buttons and modifiers), `WorldClickCommands` over `IWorldClickHost`
      (`QueuePanelWorldAction`: the query click, the right-click cancels through 2b's transitions, door, trap and
      spell placement with the Workshop gate and the spell-target rule; picking stays in the adapter),
      `RoomNavigation.Entries`, `ThingNavigation.DoorEntries`/`TrapEntries` (newest first) and `BattleTabView.Click`.
      Unity calls them and reads the buttons once per frame (`ReadHudPress`). Godot reads presses from
      `Window.WindowInput` and runs a `QueueInput` (the gate, `HudPressRouter`, `WorldClickCommands`, then the minimap's
      map press); the panels' own per-signal press handlers are gone, and the full map and options menu swallow
      presses on the live `_hudControls` flags (one narrow split is left: a mouse press and the M or Escape shortcut in
      the same frame). Now working in Godot: an overview column press posts its pickup, the gold withdraw posts and
      resets the tool, a right click cancels the query tool or a held tool (so a lit query or sell corner can be turned
      off), the full map, options and minimap follow Unity's order, the battle tab goes through `Click`, and room, door
      and trap navigation compute their entries and print the target. Game Options key capture is polled only in the
      options branch, `None` is applied while the full map is open, the pointer is taken at frame time, and an options
      left press that misses every entry still runs `TakeSessionAction`, all as in Unity. Still unwired: every camera
      move, session actions, picking (world left clicks do nothing), the hand's own world press and possession, and the
      hand cursor. Godot's `QueueInput` gate order moved with 2h (`WorldPointerInput`); its battle-tab dispatch still
      mirrors the runner's. Not run: Godot presses with a real mouse,
      Unity in Play mode, and Godot 4.8's `WindowInput`-before-`_Input` order (inferred, not read in source).
- [x] 2d. World hover to cursor (2026-11-16), a pure move into `DK2.Presentation`: `HoverCursorResolver.Resolve` takes a
      `HoverCursorInput` (the pick, whether input is allowed, the pointer over the HUD, the held button, the gold, the
      clock, the hand's world point) and returns a `HoverFeedback` (cursor, placement, hovered creature, hover sound,
      tint, tooltip reach). It holds the hover sound, pickup eligibility per hit kind, dig, `HandCursor.ForHover`, the
      tint, the room-mode dig rule, `HoverCaged`, `HeldIcons`, the over-HUD SmallPoint with No Go, the spell cursor and
      the cursor-feedback gate; `CastPosted` and `SpellRefused` gate only the cast-hand animation. The static helpers
      are `FogHit` (an unexplored tile resolves to a Tile hit), `ObjectsOnlyKeyCounts` and `TryHandLightPoint` with
      `HandLight` (the pure part of `AimHandLight`). Unity's `CaptureWorldHover` keeps the picking and the drawing and
      calls `Resolve` once; `PointerOverHud` is evaluated once per frame, which can differ for one frame in the
      placement overlay's offset on the first frame or a resize frame (`HoverHud`'s screen was set later in the old
      order). Nothing journalled changed. Godot is unchanged, since it draws no hand cursor; it can take
      `HoverFeedback.TooltipReach` instead of its fixed SmallPoint reach. `HoverCursorResolverTests` has 18 tests; twelve
      mutations (the cast-hand ordering, the Query, Options and panel-choice gates, the `HoverCaged` modal and
      possession gates, the room-mode dig rule, tint, sound, specials and the store-pile reach) each fail at least one.
      Not covered: `TintBlended` true (a type-8 door or trap) and the unreachable `CageTool is null` branch. The
      SmallPoint test is named `...AsHypothesised`, because its source 0x0040BBE0 is HYPOTHESIS. Not run: Unity in Play
      mode (the cursor, cage, tint and cast hand by eye).
- [x] 2e. Profile, hi-score and front-end glue (2026-11-16), a move into `DK2.Presentation` that Unity calls through a
      `_profiles` field. `CampaignProfileStore`: `Load(path, mode, returnPage)` keeps `LoadFrontEndProfile`'s order
      (create or read seeded by the registry import, `EnsureOpenMap`, `RepairAfterLevel` only from the campaign-results
      page, save if changed, then the moon-phase secret five from an injected clock), `Save`, `Track` (raises
      `Changed`, then writes, and only when it has a file), `Adopt` (a fileless in-memory profile) and
      `SecretFiveAvailable`. `HiScoreStore` reads its own file, then the install's `Data/Settings/HiScores.dat`, then
      empty, and writes its own file only. `FrontEndHost`: `TypeName`, `FillPickSlots` (a map piece wins over a marker),
      `CreatureFrame`, `MovieGallery` and `OpenCampaign` (the shared store and controller wiring). `FrontEndStartupGate`
      replaces the static `_startupShown`. Every Unity read and write maps 1:1 to the old code, including the null checks
      and `LoadSession`'s write of `session.Profile` without adopting it. Godot: `UserFolders` builds `GameFolders` over
      `user://` with `--profile`, `--settings`, `--saves` and `--hiscores` (both scenes take them); the front end loads a
      real profile and hi-scores, so the hi-score page shows real rows, the movie gallery shows which movies are
      unlocked (no playback) and the campaign entries fly to the map page. Not wired: land picking, briefing and launch
      (no station scene), name typing, playback. A corrupt or unwritable profile, or a registry error, warns and gives
      an unsaved in-memory profile; an unreadable hi-score file warns and gives empty rows. Tests:
      `CampaignProfileStoreTests`, `HiScoreStoreTests`, `FrontEndHostTests`; 34 mutations across the review all fail a
      test. Not run: Unity in Play mode and the Godot front-end pages by eye. `SessionSaveLoadProbe` reflects `_profiles`
      (no build compiles it). Left for later: DK2's 0x00534F70 reaches `RepairAfterLevel` for return states 0x1A, 0x1B,
      0x2A and 0x2D and ours only for 0x1A (its record says so; possibly a separate gap);
      `CampaignMapController.AdvanceVisuals` calls `RouteSlots(...)` every frame on the map page, a possible
      allocation, unmeasured, which Godot can now reach. Owner question, product policy and not a DK2 unknown (so no Q
      row): is it acceptable that Godot's first start imports the original game's registry profile once into
      `user://` and the two then diverge. The corrupt-profile fallback is the current policy (see 2f).
- [x] 2f. Save and load (2026-11-16), a move into `DK2.Presentation` that Unity calls from `Saves.cs`, `LevelEnd.cs` and
      `DungeonRunner.cs` (`_playedSlot` and `_carryApplied` are gone). `SessionContext` holds the level, mode, return
      page, `PlayedSlot` and `CarryApplied`, with `ApplyCarry` (the old `ApplyCampaignCarry`), `PlaySlotOf` and the
      front-end launch rule. `SessionSaver` is the old `SaveSession`: load the profile if needed, apply the carry when a
      slot is played, write the header and `ToBytes`, then the profile, and refresh the list. `SessionLoader` is the old
      `LoadSession`: it validates, reuses `CampaignLevelData.Load` and `CampaignSimulationFactory.Triggers` (Unity's
      copies are gone), calls `FromBytes` with `SimulationSystems.Default()` (see the follow-up below) and writes the save's profile to the path
      without adopting it. The snapshot format and load order are unchanged. `GameSessionRequest.Resume` and `Resumed` carry the result;
      `CampaignLevelData`'s constructor is internal. The one Unity difference: a save whose level map is missing is now
      refused, since `CampaignLevelData.Load` reads the map (all 66 shipped maps load; an `InvalidOperationException`
      from it reads as "not available in this installation"). Unity's `LoadLevel` copied `_pendingResume*` instead of
      reading `request.Resume` until 2h. Godot: `InGameHud` has a `CampaignProfileStore`, loaded on the
      first save or load. Save writes `<saves>/<name>.dk2g` through `SessionSaver`. Load goes through `SessionLoader`,
      a static handoff and a deferred scene reload (`ReloadForResume`); the new instance continues from the restored
      simulation, a second submit while a reload is pending is ignored, and a failed reload clears the handoff and shows
      "The scene could not be reloaded." A save writes the `.dk2g` and the profile file if it is missing, and rewrites
      the profile when a played slot has a new carry; a load overwrites the profile file with the save's profile, as
      Unity does. Corrupt-profile policy (an owner product decision): a save still works, keeping the profile
      in memory with no file write; a load is refused with "The campaign profile could not be read, so a save cannot be
      loaded." and writes nothing. Check arguments: `--save-as <name>` with `--save-tick <n>`, `--load <name>`,
      `--hash-at <n>`; a resumed instance ignores `--save-as` and `--load` (`godot/README.md`). Portability: the Unity
      saves under `build/` are snapshot format 523, the current format is 524, and Godot refuses them cleanly; no
      current-format Unity save exists to cross-check. `SessionSaveLoadTests` (new) and `CampaignProfileStoreTests`;
      20 mutations across the review all fail a test, the `LoadGame` one only in an install-gated test. Headless
      Godot on Level1: the state hash at turn 400 is `D7290428D81A902F` whether the run never saved, saved at turn 200
      (`3057C00AA838C44A`) and went on, or loaded that save and went on; the install-gated test repeats it over 301 +
      300 turns. A corrupt-profile run refused the load and left the file unchanged. Not run: Unity in Play mode, and
      a save written by a current Unity build loaded in Godot.
      Follow-up (2026-11-16): `SessionLoader` now calls `WorldSnapshot.LoadGame`, as ADR-0241 item 2 says a
      save-load feature should; Unity's runner had used `FromBytes`, so step 2f carried that deviation over. A load
      re-chains every room row-major, as DK2 does, in both adapters (`SessionSaveLoadTests`, 22/22). The Level1 hash
      above is unchanged as observed (map-placed rooms have order 0); no test pins it.
- [x] 2g. Level end and cutscenes (2026-11-16), a move into `DK2.Presentation` that Unity calls from `LevelEnd.cs`,
      `Finale.cs`, `Cutscene.cs`, `Hud.cs`, `Preview.cs` and `DungeonRunner.cs`. `CampaignResultFlow` (`Prepare`,
      `Update`, `Prompt`, `CampaignResultFrame`): `Prepare` runs `TransferCreatureRules.Arrive` before the first turn.
      `Update` runs the front-end gate, the carry, the statistics once per player in verdict-then-player order and then
      one profile save, `ClassicHudControls.UpdateResult` (with `ScriptedPath` while a path plays), the ended gate, the
      result once and its save, the path gate, the win keys, and Space, which leaves unless a continue is pending.
      `CampaignCutsceneRunner` (`ICampaignCutsceneHost`) runs the title, speech, pause and movies. `TabletDrawList` and
      `TabletMesh` hold the tablet projection and back-face cull, which the battle fly-in shares.
      `ScriptedViewDirector.PlayedHud` and `PanelFlash` hold the HUD visibility and the flash-button rules. Two
      behaviour changes, in both adapters: Escape after a finished win screen now posts `ContinueAfterResultCommand`
      (134) and then `RevealMapCommand` (75), the order of the done arm at 0x004055E3 (0x86 posted at 0x00405664,
      +0x13DA set at 0x00405668, 0x4B posted at 0x00405697; [0x00405010](../research/functions/00405010.md)); our code
      had them the other way round. `FinaleTests.TheContinueAndTheRevealCommuteWithinTheirTurn` shows the state hash is
      equal in either order, so only that frame's command stream changes, and no journal fixture holds the commands
      ([Q865](../research/open-questions.md)). A null `WinScreen` now counts as done, so Escape continues at once;
      Godot has no win screen and Space never left before. The minimap heading during the opening path needs no rule:
      the HUD, so the minimap, is hidden for the whole path. Open, not decided: the old runner's comment said Space
      posts action 1, but the code switches the session directly and `CampaignResultFlow` keeps that
      ([0x00510F40](../research/functions/00510F40.md)). Godot: it runs `ScriptedViewDirector`, observing the simulation
      before turn 1 and writing nothing (the Level1 turn-400 hash is unchanged, `D7290428D81A902F`), hides the HUD
      during the opening path, and follows Unity's HUD visibility, panel and resource flashes and options-key hold. A
      level started `--from-map` runs `CampaignResultFlow`: Space switches to `Main.tscn` at the return page, and the
      level end writes the profile (statistics, result, carry, transfer creature) to the `user://` profile unless
      `--profile` is given. `ClassicFrontEndMenu.Open` passes the return page to the profile load, so the won map's
      branch repair runs as in Unity (it writes the profile when the front end is entered with CampaignResults). New
      check arguments: `--from-map`, `--stage-win`, `--stage-loss`, `--leave-result`, `--escape-result`; Godot also
      prints the viewer's status changes (`godot/README.md`). Not wired in Godot: subtitles and the win screen and tablet; the post-win title card and cutscene play
      in the front end since step 4's movies (the card's text waits for the subtitles). Verification: `dotnet test DK2.slnx` green (Presentation 2276, Simulation 5583,
      Behavior 674 with 5 skipped); Godot builds clean and the Unity compile has 0 errors; headless Level3 `--from-map`
      with temp folders: a win then Space leaves, a win then Escape posts Continue then Reveal and the game keeps
      playing, a loss then Escape posts nothing and Space leaves; 17 + 8 mutations across the review all fail a test.
      Not run: Unity in Play mode, and `CompletionFlowProbe` (uncompiled; its reflection is updated).
- [x] 2h. The frame order and the command stream (2026-11-16), last and in one piece, in `DK2.Presentation`.
      `FrameDriver` (with `ITurnPoseSource`, `FrameScriptInput`, `FrameTicks`; the scripted-view director is passed in on
      each call, since two Unity probes swap it): `UpdateScript` (render tick = current + pending + min(debt, 1)),
      `AdvancePickups` (reset on a viewer change), `TakeScriptedInput` (Escape or Space skips a playing path and posts
      `SkipCameraPathCommand`, 0x00405362; a glide; `AllowOptionsKey = !PathWasPlaying`), `TakeTicks` (`DrainReadyDrops`,
      the speed and pause gate, accrue, take, the pose inserted at index 0 only with ticks, `FirstInput`, the journal's
      `HandOff`, then `Clear`) and `AfterTicks` (tag acknowledgements, terrain highlights). `WorldPointerInput` (with
      `IWorldPointerHost`, `WorldPointerFrame`) is `QueueInput`'s gate sequence: ordinary input, possessing (modal only),
      HUD and world clicks, the minimap, `SelectTool`, press or release, the intent, the journal's intent, the snapshot
      and hover, `Translate`, the post-processing. `FrameJournalSession` is the journal policy (`DK2_INPUT_JOURNAL`,
      folder name, header, failure and close). `PossessionSession` implements `ITurnPoseSource`; the worker-tick
      protocol stays in Unity. The `keys` header now names the key table ("UnityEngine.KeyCode" or "Godot.Key"), which
      closes the audit's journal-portability point; the format is unchanged, so existing Unity journals still read.
      Unity's `LoadLevel` reads `request.Resume` and `_pendingResume*` is gone (strictly more correct). Known gap: the
      binary gates Escape on 0x0075A661 and Space on player +0x76 bit 0x400000; `FrameDriver` mirrors only the path
      branch with the bare keys, as Unity did (ADR-0496; [0x00405010](../research/functions/00405010.md)).
      Godot: `InGameHud` runs `FrameDriver` and `WorldPointerInput` in Unity's order, replacing its `TickPacer` loop and
      gate copies (`QueueInput` is a thin call). Deferred drops now post. Escape or Space skips a playing camera path and
      posts `SkipCameraPathCommand`, and the options key is held that frame; the script's glide prints "not wired".
      Releases are read, but without picking they find no tile and post nothing. The journal is opt-in through
      `--input-journal <dir>` only: Godot does not read `DK2_INPUT_JOURNAL`, Unity's variable, so that a globally set
      variable does not make every Godot run write; the header carries `source=godot`, `keys=Godot.Key` and
      `godot.version`. The `CancelPointerGesture` order, settled: a frame without ordinary input and possession cancel
      only the world drag, focus loss cancels the panel's press and then the world drag, and the panel cancels its own
      press when freed. Unity's cancel sites are unchanged. Evidence: Unity's own recordings in `F:\DK2Evidence` hold no
      commands and use old HashVersions, so none replays as pass or fail. `FrameOrderJournalEquivalenceTests`
      (Behavior) feeds the committed `level1-seed1-win/input.journal` through the frame order with hand-built frame
      splits (paused, speed 0, short and two-tick frames), rebuilds the fixture byte for byte, checks that each handing
      frame's record holds exactly that tick's commands, and an install-gated replay matches all 2445 hashes (that
      replay is a sanity check of the fixture, not of the frame order). `UnityFrameJournalFixtureTests` (Presentation)
      re-records a real Unity recording, committed under `tests/DK2.Presentation.Tests/Fixtures/unity-level1-frames/`
      (our adapter's output, a SHA-verified copy, no personal data, marked binary in `.gitattributes`), and reproduces
      Unity's `frames.journal` byte for byte; it covers the recorder only. `FrameDriverTests` (18),
      `WorldPointerInputTests` (9) and `FrameJournalSessionTests` (10) cover what recordings cannot: the pose, drops,
      the skip, the options hold, a viewer change, the camera mode, the render-tick clock, `AfterTicks`, every gate
      and the journal policy. 26 mutations (the render-tick terms, the pose position, hand-off before clear, drops
      before hand-off, the gate, the skip and the `WorldPointerInput` gates) all fail a test. The reviewer found no
      equivalence defect in either product path; fixed after review: the cached `_scriptedView` (now passed per call),
      the clock expression tests, the Godot journal switch (argument-only) and the sanity replay's name. Verification:
      `dotnet test DK2.slnx` green (Presentation 2315, Behavior 677 with 5 skipped, Simulation 5583); Godot builds 0
      warnings and 0 errors and the Unity compile has 0 errors; headless Godot Level1 turn-400 hash `D7290428D81A902F`,
      the save and load hashes unchanged, `--from-map` win and Escape as before, an `--input-journal` run wrote both
      journals with the same hash, and `DK2_INPUT_JOURNAL` without the argument wrote nothing. Not run: Unity in Play
      mode (the possession pose, pickup and drop, the skip) and Godot with real input.
- [x] 2i. Pure camera rules (2026-11-16), in `DK2.Presentation`; Unity calls them, Godot has no camera until step 3.
      `CameraGlide` and the static `CameraFocus`: the action-26 glide ([0x00442650](../research/functions/00442650.md)),
      with `TileCentre` (clamps to the bounds; on the room-goto path, 0x00415B30, DK2 drops an off-map point first and
      ours clamps, [Q866](../research/open-questions.md)), `Over` and `Bounds` for the full map and the overview focus,
      and `Clamp` mirroring `Mathf.Clamp`, minimum first; the lerp is written as `Vector3.Lerp`. Unity's four glide
      fields are one `_glide`. `ClassicCameraInput` with `CameraPointerSample`: the strict 5 px edge scroll
      ([0x00406DF0](../research/functions/00406DF0.md)) with the window-focus gate, `FrameTime`, `PointerDelta`, and
      `Apply` in Unity's order (pan, one zoom step, with the wheel ignored over the HUD and zoom-in winning over
      zoom-out in a frame, then held rotate or zoom). `ClassicCameraSetup`: `Preset`, `PossessionPreset`,
      `ZoomOutLimit` (rounds half to even), `ReferenceScreenWidth` (1600) and `Create`. `TileTopPlane`: `TryHit` (a copy of
      `Plane.Raycast`), `PositionOn` (feeds command positions with the old float steps) and `HandLightPoint`; the camera
      ray stays in the adapter; the hover pick's terrain cast went through `TileTopPlane.TryHit` in 2j part A. `GameSessionStarter`:
      campaign, front end, DK2 save, `PlayersOf` and `CameraTicksPerSecond`, with the world built in the same order in
      every branch. `ClassicCreatureQueryRules.Follows` (the per-frame gate) and `TurnToFollowHeading` (equal to the old
      degrees round-trip for all 2048 yaws; the old `Pose` call in the query turn is gone, which could only throw on a
      bad viewport; the `Camera.main` guard stays). `CameraPathPose.FrontEndVerticalFieldOfView` is the front end's 4:3
      field of view. The probes go through `_glide`; they are not compiled. Godot: the "not wired" prints now show the
      targets computed through the shared rules (room, door, trap, battle tab, script, the minimap glide, the full map,
      the overview focus); no camera was added. For step 3: the pointer sample is y-up like Unity's
      `Input.mousePosition`, which runs from (0, 0) to (Screen.width, Screen.height), so a top-left host passes
      `y = ScreenHeight - yTop`; the Up band is then `0 < yTop < 5` and the Down band `ScreenHeight - 5 < yTop`. That
      rests on Unity's documented range and is not measured; which pixel row Windows reports at the top edge is open
      ([Q867](../research/open-questions.md)). The reviewer found every expression equivalent and determinism intact;
      fixed after review: the off-map comment's scope, a test pinning the `Clamp` order, tests for the `Apply` priority
      and order, and the documented y flip. 13 mutations all fail a test. Verification: `dotnet test DK2.slnx` green
      (Presentation 2382, Simulation 5583, Behavior 677 with 5 skipped); Godot and the editor headless build are
      clean and the Unity compile has 0 errors; headless Level1 turn-400 hash `D7290428D81A902F`, unchanged. Not run:
      Unity in Play mode.
- [x] 2j. Scene mapping for step 3 (2026-11-16, in three parts: the first half, part A and part B, below).
      Done, in `DK2.Presentation`: `PoseInterpolator` (the draw seconds, the clamped position lerp, the shortest-arc
      wrap and the object turn; creatures still take their turns uninterpolated and objects interpolate theirs, the
      audit's open point, kept as drawn, [Q868](../research/open-questions.md)). `MeshOrientation` returns a
      `RenderEuler`: `Mesh` is the one chain that was written four times (objects, creatures, instances, construction
      particles), `Particle` is a copy of `Mathf.LerpAngle` plus the half turn, `Heading` serves structures, torches and
      `ClassicTorchGeometry`. Its doc states the order of [0x00580480](../research/functions/00580480.md) (M = S·Rx·Ry·Rz);
      the half turn and the negated pitch are our frame mapping. `AnimationClipRequest`: the clip identity record
      structs, `Replacement` and `StartSeconds`. `ObjectView` gains `DrawnTexturePhase`, `MeshScale` and `MeshScaleY`
      (`ClassicMeshes.ConfigureHeartDecay` had no callers and is gone); `LightningControlPoint.FromRenderWorld` does the
      (x, -z, y) swap.
      Done, in `DK2.Art`: `CreatureAnimationPlayer<TResources>` behind `ICreatureAnimationSurface` (Install, Show,
      Upload) holds the deferred replacement, the start tick `floor(start*4)`, the catch-up, the beat, the footstep
      normalisation ([Q869](../research/open-questions.md)), `AdvanceHealthPhase`, the control-point lookup
      ([0x0059D260](../research/functions/0059D260.md), [0x0059D380](../research/functions/0059D380.md)) and the
      lightning points with `Id < 2000`. `CreatureAnimationArt` came from `ClassicMeshes.ConfigureAnimation`; its
      `TurnsPerSecond` must equal `SimulationTiming.OriginalTicksPerGameSecond` (DK2.Art cannot reference it) and a
      Presentation test pins that.
      Unity: `RenderEulerRotation` composed the Euler chain by hand, checked by eye against all four old sites (3f moved the
      composition into `RenderRotation`, with tests). Not visible in output: the profiler marker scopes moved, a
      lookup with no meshes returns nothing where it threw, and a few allocations went. The ADR-0458 threading contract
      holds: the moved code reads view copies, never `WorldState`. Godot is not wired; step 3 builds the 3D world.
      The reviewer found every formula and call order equal to HEAD, bit for bit; three mutation gaps (the clip identity
      fields, the install, show and upload order in the `Draw` that installs a deferred clip, and the footstep window
      over several turns) are closed by tests, and 17 mutations all fail a test. Verification: `dotnet test DK2.slnx`
      green (Presentation 2405, Convert 205, Simulation 5583, Behavior 677 with 5 skipped); Godot and the editor
      headless build are clean and the Unity compile has 0 errors; the simulation hashes are unchanged. Not run:
      Unity in Play mode.
      Part A of the second half (2026-11-16), in `DK2.Presentation`: `ClassicLightPublisher` with `ITileLightSink` and
      `ClassicLightPublication` finds the visible tiles again only when the pose, viewport or distance changes, joins the
      second camera at view distance 10, publishes a diff (removes tiles no longer visible, sets every visible table,
      flushes; `Unchanged` short-circuits), publishes only new portal-light requests, and has `MarkTablesWritten` and
      `Clear`. `ClassicLightFlagsRules.Torch` and `IsCheapTorch` give the torch flags. `TileView.LitAsExplored` is the fill
      rule `Explored || Scanned || PointLight.HasValue` (owner, 2026-10-21). `LatticeDirtyTracker` holds `ForgetReach` (1,
      used by `TerrainLattice.Invalidate`) and `TorchReach` (2). `WorldHoverPicker`, with `HoverPickRay` and
      `HoverPickProbe`, holds the terrain cast through `TileTopPlane.TryHit`, candidate offering, the rendered winner
      (nearest triangle, then depth; a refused winner gives the tile), the 3x3 sweep, Moller-Trumbore and `OnPointerTile`;
      Unity's `Vector3` arithmetic is written out term by term so the pick, which reaches commands through hover, is
      bit-identical. `HoverOverlayPlan` is the per-frame red set and each submesh's `(Swap, Red)` decision. Unity's
      `DungeonScene` keeps only the measuring (frustum, UV2 sphere and bounds, mesh reads, camera to pose), and
      `ClassicTileLightBuffer` implements the sink. The ADR-0458 threading contract holds: nothing reads `WorldState` but
      the injected filters, and everything runs before the worker tick. Godot is not wired: it has no 3D world yet. The
      comment's citation `0x00599710` is a branch inside 0x005992B0 (`Hand_ResolveScreenTarget`), not a function of its
      own, and the comment says so. The reviewer found every move equivalent, with no leftover mutation; three gaps closed
      after the review (the depth against ray distance ordering pinned through float rounding, the oblique-ray `Dot`,
      `PointAlong` and `Cross`, and the cache forgotten on `Clear`), and 36 mutations all fail a test. Verification:
      `dotnet test DK2.slnx` green (Presentation 2465, Simulation 5583, Behavior 677 with 5 skipped); Godot and the editor
      headless build are clean and the Unity compile has 0 errors; the hashes are unchanged. Not run: Unity in Play mode
      (the hover tint, the pick and the lighting).
      Part B (2026-11-16), in `DK2.Presentation`: `EffectDrawList` is the construction-effects draw list that was in
      `ClassicConstructionEffects`: the defaults (`TerrainDebris`, `CaptureEffects`, `HeartSparkles` and the stopwatch
      clock), the stream order (`EffectParticleStream`: room effects, then shots), the sprite colour (an unlit tint; no
      tables makes black white; no table for the tile gives the tint; else lit; taken before the StandsOnPoint lift),
      the frame pick, the billboard and flat quads ([0x0059C720](../research/functions/0059C720.md) →
      [0x0057FB80](../research/functions/0057FB80.md), the lift and offset of 0x0059C360, the UVs of 0x0057F3D0) into an
      `EffectQuadBuffer`, and the blend. A reference-keyed group-key cache removes a per-particle, per-frame allocation.
      `ClassicConstructionEffects` keeps the Unity meshes, materials and draw calls. `RenderWorld` is the DK2 to render
      frame, with `ToNative`, which replaces the three inverse-frame copies (shadows, head, sparkle). `HeartPulse`
      ([0x0043CC30](../research/functions/0043CC30.md)), `GemSparkleGate`
      ([0x005A3AA0](../research/functions/005A3AA0.md)) and `CreatureHeadTable`
      ([0x00434E80](../research/functions/00434E80.md)) came from `DungeonScene`; `StatusFlowerLayers` and
      `FlowerDollarClock` ([0x005961A0](../research/functions/005961A0.md): the dollar descriptor is installed only
      when the current type is not 6, and the clock advances once per dollar drawn) came from `ClassicStatusFlowers`
      and `ClassicFlowerDollar`. `DisplaySettings` holds `DefaultGamma` 30 and the brightness exponent `30 / Gamma`
      from `ClassicSettingsRendering`; `ClassicKeyBindings.Name`, which had no callers, is deleted.
      `RoomConstructionParticles` gained three internal read-only style properties. `ObjectView`'s texture phase and
      scale (`DrawnTexturePhase`, `MeshScaleY`) moved in the first half, not here. The reviewer found the moved code
      arithmetic- and order-identical to HEAD, with threading, reflection and allocation sound; two test gaps (the
      flat-quad interpolation and the frame-pick guards) are closed by two new tests and all six surviving mutants
      now fail a test. The frame pick's `Math.Min(frames - 1, ...)` is a guard `ConstructionParticle` cannot reach,
      since `RemainingSpriteLifetimeFraction` caps the phase at 4095. `ClassicConstructionEffects.DrawFlat` copies
      the quads into Unity's lists, an extra pass over the vertices per group per frame with no allocation; accepted
      for now. Whether the dollar's speed, which follows the number of dollars drawn and the frame rate, is an
      original quirk is open (record 0x005961A0). No simulation file changed and HashVersion is unchanged; the Level1
      turn-400 hash is `D7290428D81A902F`. Verification: Presentation 2519 passed (2517 and two new), Simulation 5583,
      Behavior 677 with 5 skipped, and `godot/DK2.Godot` builds with 0 warnings.
- [x] 2k. Closed 2026-11-16 by moving its two items to step 4, where they now sit, since both follow
      speech playback: the level sound driver (`DungeonRunner.Sound`, which waits for the audio abstraction) and
      Godot's speech subtitles. Step 2 is complete. Unity's batch checks (`unity/README.md`, *Verifying it inside Unity*)
      run unattended here, and at 1a766d8f, over all of step 2, its own compile, `DungeonSetup.SmokeTest` (Level1 30x38,
      1140 tile objects) and `SaveSmokeTest` (20 saves) passed; the "Not run: Unity in Play mode" lines above mean Play
      mode in the owner's open editor, which no session drives.

## Step 3: a playable level in Godot

The sub-steps 3a to 3n, their order and the owner's decisions on the open points are in
[the step-3 audit](2026-11-16-godot-step3-audit.md); the order from 3j onward, and the new 3o to 3q, are in
[the modern-rendering audit](2026-11-18-godot-modern-rendering-audit.md) (ADR-0813). The step-3 audit's *How a step is checked here* lists the checks: the
rules' tests, a Movie Maker capture, the stand-in compile and Unity's batch checks (Unity 6000.6.0f1 is installed and
licensed; its lighting probes give reference frames for 3f, 3g and 3i, see *Unity reference frames* below).

*Unity reference frames.* Commit d735490a fixed the Editor probes, which had been broken since 2026-10-20.
`ClassicWorldLightingProbe` passes on Level1 and Level7 in batch mode and writes start, tick70, shifted and torch-only
frames, each with and without torches. Since cd4e04a0 the probe renders the in-game look (it opens `Scenes/Dungeon.unity`
for the sun, sky and the `DungeonRunner` settings, fog of war and native initial ambient included, and applies the
grading; `DK2_NATIVE_AMBIENT_PROBE` 0 or 1 overrides the scene's ambient switch); before it the frames had no grading
and no sun, since batch mode's scene was empty. To produce them: set `DK2_LIGHTING_PROBE_LEVEL` to `Level1`, `Level7` or `Level8`,
set `DK2_LIGHTING_PROBE_OUTPUT` to a directory outside the repository, then run `"C:\Program Files\Unity
6000.6.0f1\Editor\Unity.exe" -batchmode -projectPath unity/DK2.Unity -executeMethod
DK2.Unity.Editor.ClassicWorldLightingProbe.Run`, without `-quit`. 3f, 3g and 3i compare Godot's frames against these.
Unity's batch checks and probes run in the `unity-verify` worktree through `.claude/worktrees/unity-verify.sh` and
`unity-probe.sh`. Both call `unity-sync.sh`, which resets the worktree to master and copies the main tree's uncommitted
files; before 2026-11-18 neither synced, so the 3h and 3i Unity runs before that date tested stale code. A rerun
with the sync found 3h and 3i byte-identical, so 3h's claim holds.

- [x] 3a. Static terrain from the level-start camera (2026-11-16). In `DK2.Presentation`, out of `TerrainChunks` and
      `ClassicMeshes`: `TerrainTileBuilder` (`Prepare`, `Build`, `Draws`, `PrepareSinglePiece`) with
      `TerrainTileGeometry` and `TerrainPiece` (`ITerrainPieceSource`, `InstallTerrainPieces`); `TerrainMaterialKey`
      (`For` chooses the variant: fog, red, tag or tint; `Overlay` is the tag pass); `ClassicMaterialRecipe` (texture
      choice, blend, `WritesDepth`, `TakesAddedLight`: an additive material takes no added light, an explicit rule
      where Unity had the implicit "URP Unlit has no `_EmissionColor`"). `RightHandedFrame` is the Unity
      (left-handed) to Godot (right-handed) Z flip, audit open point 1, option 1: triangle indices pass through
      unchanged, because the geometry and the camera are mirrored together and the screen winding stays. Unity's
      `TerrainChunks` keeps the upload, the scheduler (moved in 3c) and `Append`; `ClassicMeshes` implements `ITerrainPieceSource`
      and maps a key to a cached `Material`. Godot's new `WorldScene`, under `InGameHud`, has one `MeshInstance3D` per
      16x16 chunk, one `ArrayMesh` surface per key, unshaded shaders and a `Camera3D` at the level-start pose
      (`PROJECTION_FRUSTUM`, keep-height, `frustum_offset = -shift*h/2`, near 0.3); the focus projects to y 453.37
      against DK2's expected 453.375 at 1080p. In 3a the fog and tag-overlay keys draw nothing (3c) and tint, tag and
      red draw with the plain material; there is no lighting (3i) and no camera movement (3b), and the chunks are built
      once, synchronously, in `Open`. Positions and normals match the old build to 1e-6 (its quarter turn went through
      a float quaternion, one ulp off); keys, triangles, UVs and light anchors match exactly. Reviewer: about 60
      mutants and 8 follow-ups, all now fail a test. Verification: `dotnet test DK2.slnx` 10,537 passed, 30 skipped
      (Presentation 2,556); the Level1 turn-400 hash is unchanged (`D7290428D81A902F`); the `DK2.Unity.Compile` stand-in
      passes; Unity's batch compile, `SmokeTest` and `SaveSmokeTest` passed on the final change, after the review fixes. A
      Movie Maker capture at 1920x1080 shows Level1's Heart room from DK2's start camera with the HUD on top, not
      mirrored (a Gold tile at the minimap's north notch), with correct wall faces and textures, full-bright.
- [x] 3b. Camera control and the minimap (2026-11-16). In `DK2.Presentation`: `MinimapCameraView` holds the drawn
      camera's floor-plane centre and corners (rays through the off-centre frustum, `TileTopPlane.TryHit`), its yaw
      (Unity's `eulerAngles.y`; looking straight down, from `Up`'s horizontal part) and the heading, yaw x 2048/360 rounded
      half to even and unmasked; it moved out of Unity's `DungeonMinimap`. `HudDrawKind.Triangles`, `HudVertexRange` and
      `HudFrame`'s vertex store (`AddTriangles`, `Vertices(draw)`; `Clear`, `SameAs` and `CopyFrom` cover it) carry a textured
      triangle list, and both `HudCanvas` renderers draw it. `HudTriangles.ClipToCircle` cuts the footprint to the disc's circle
      on the CPU, within 0.12% of the radius; it replaces Unity's rounded-overflow mask (the remake's choice).
      `MinimapCameraFootprint.Triangles` builds the ribbon, and `MinimapHud.SetCamera` makes it the frame's first draw, under
      the frame, in whole-map mode only (the draw order is the remake's choice, the native z-order being unread). Unity's
      `MinimapFootprintVisual` is deleted and `DungeonMinimap` calls `SetCamera`. `ClassicCameraSetup.DefaultZoomOutLimitScale`
      is 1.3 and both adapters use it: Unity's serialised `cameraZoomOutLimitScale` was 1.3 in `Dungeon.unity` (1.2 was only
      the code default); the scene keeps an orphaned field Unity ignores, and tuning the scale now needs a code change.
      `LaunchArguments.GlideTarget` reads `--glide-to x,y`. Godot's `WorldScene` exposes the focus and places the camera once a
      frame, and `InGameHud.UpdateCamera` moves it in Unity's order: the path, the glide, the script's follow, then
      `ClassicCameraInput` (keys, edges, wheel, held buttons; y-up pointer, `PointerOverHud`, window focus). Every glide that
      was printed now runs through `CameraGlide`: the script's, minimap, room, door, trap, battle tab, portal, Heart, fight and
      a message's goto. The full map and the overview jump the focus; the query follow and turn, the script's zoom, the scroll
      speed and the panel collapse are wired, and the minimap draws from the live camera with its footprint.
      `tools/hud-probe`'s pilot footprint checks are removed (the tool is due for deletion in step 4). Reviewer: 49 mutants,
      the survivors now killed or equivalent; it re-derived that the Heart, the focus, sits about 31% of the floor depth in from
      the footprint's narrow edge, as the capture shows. Verification: `dotnet test DK2.slnx` 10,578 passed before the review
      fixes (Presentation 2,610 after them); the Level1 turn-400 hash is unchanged (`D7290428D81A902F`); the
      `DK2.Unity.Compile` stand-in passes; Unity's batch compile, `SmokeTest` and `SaveSmokeTest` passed. A Movie Maker capture
      with `--glide-to 30,12` from a save after the opening path: the 3D view moves for 16 frames (the 500 ms glide) and
      stops, and the minimap footprint moves from around the Heart to the top of the disc and is clipped cleanly at the rim;
      the disc does not scroll, since Level1 is in whole-map mode. Not exercised: live keys, edges and wheel in Godot (they ran
      only in unit tests, since driving the pointer would move the owner's cursor). 3g resolved the query follow: it runs after the frame's draw and reads the drawn,
      interpolated model, as Unity does. The footprint's native scale and centre
      constants are an open item ([minimap-rendering.md](research/minimap-rendering.md), *Camera footprint*). An allocation
      test fails in about 1 of 5 to 10 full Presentation runs, also on a clean HEAD (`docs/TRAPS.md`).
- [x] 3c. Terrain that changes (2026-11-16, ADR-0805; ADR-0457 and ADR-0696 for the scheduler). In `DK2.Presentation`:
      `TerrainChunkScheduler` over `ITerrainChunkSink`, out of Unity's `TerrainChunks`. A change forgets its lattice cells and
      marks the 5x5 tiles stale (`StaleReach` 2; without a lattice only the tile itself; a tag or tint change marks the 4
      neighbours). The flush keeps Unity's numbers: a 4 ms budget, at least one chunk, inline while no chunk shows, lowest
      chunk first. A tile is prepared on the main thread and built on the pool, joined by the next flush or `Set`; a budgeted
      flush returns without waiting for an unfinished build. The profiler markers keep Unity's names, created at type init as
      before. `TerrainMaterialLook` is one look per key: `TagTint` (128,128,200), `TagOverlayColour` (63,64,255),
      `HoverOverlayColour` (255,64,65; global 0x006FCD73, read at 0x0043BF1F in 0x0043B220), `RedLight`, `TaggedLight`,
      `TintedLight` and `FogAlpha`; Unity's `ClassicMeshes.Terrain(key)` now picks its `Material` from it. `LiquidSurfaceWave`
      and `LiquidSurfaceField` came out of `LiquidSurfaces`: rest heights 0.9 and 0.85, amplitudes 0.03 and 0.04, the 30 a
      second frame, both phase formulas and a 3x3 grid a tile. `TerrainTileGeometry`'s starting buffers are cut to 32 and 16:
      on Level1 (1,140 tiles) the retained memory a tile went from 8,970 B to 1,954 B (9.8 to 2.1 MB), and a dig's 5x5 rebuild
      takes 0.23-0.27 ms through the per-tile cache against 2.3-2.5 ms for a whole chunk, so the cache stays. Audit
      corrections: the decay stage was already in `src` (`WorldView.DecayStage`, `TileView.FaceTexture`,
      `TerrainPiece.FromModel`, `ClassicMaterialRecipe.FromKmf`), and `HoverOverlayColour` moved now with the overlay, not in
      3h. Unity: `TerrainChunks` is a sink over the scheduler and `LiquidSurfaces` uploads the field. Two deviations, both
      the remake's: Unity takes the shared liquid normals (the normalised sum of unit face normals) in place of
      `Mesh.RecalculateNormals`, which the reviewer bounds at 1.5 degrees (0.02 against area weighting), at most a one-byte
      light step; and an alpha-blended recipe under fog opacity 255 reuses the plain material instead of an identical
      alpha-1 twin, which renders the same. A race that was already there is fixed: `DungeonRunner` set
      `_terrainLattice.Possession` (`InvalidateAll`) while a pool build could run; `TerrainChunks.JoinBuild` and
      `DungeonScene.JoinTerrainBuild` are new, and the runner joins before the latch changes. Godot's `WorldScene` rebuilds
      through the scheduler with one `ShaderMaterial` per look, and rewrites the liquids' positions in place each frame
      (`SurfaceUpdateVertexRegion`). Its normals came in 3i, which also replaced 3c's stand-in light of 0.5 (the remake's
      choice, a fixed sum under Unity's packed diffuse) with the table lighting.
      The overlays and the fog drew weaker than in Unity; 3d found the cause (double linearisation, mostly) and fixed it.
      Mutation checks: the developer's 37 and 11 more, and the reviewer's 56, each killed or equivalent. Verification:
      `dotnet test DK2.slnx` 10,646 passed, 30 skipped (Presentation 2,662); the Level1 turn-400 hash is unchanged
      (`D7290428D81A902F`); the `DK2.Unity.Compile` stand-in passes; Unity's batch compile, `SmokeTest` and `SaveSmokeTest`
      passed. Captures: Level1 at tick 130 against tick 1000 is identical except one gold tile lit by the tutorial's blinking
      red highlight (the red twin plus the overlay), rebuilt within a frame or two; Level1's terrain does not change on its own
      (no tags, and the imps do not dig without the player). Level8 shows lava and water surfaces in their beds; no clean wave
      capture exists (the camera drifted after a load), so the vertex upload was checked by reading Godot's vertex buffer back.
      The tag tint is not captured. Open: (1) the steady-frame allocation that remains is shared code, about 2.8 KB a frame on
      Level1 and 10.6 KB on Level8, from `ScenePresenter.Present`'s object, creature and structure passes (yield iterators in
      `WorldView` and `Scenery`); a separate `DK2.Presentation` change. (2) A
      `--stage-tags x0,y0,x1,y1` launch argument would make the tag tint capturable. (3) A staged liquid capture, with the
      camera held, would show the wave.
- [x] 3d. The blended stream (2026-11-16, ADR-0805). In `DK2.Presentation`, out of Unity's `TransparentTerrain`:
      `BlendedTerrainStream<TMaterial>` collects every chunk's blended triangles, culls them with a 1e-4 margin, sorts them
      back to front through `TriangleDepthOrderBuffer`, splits the order into runs by material and gives each run
      `SortingOrder(run, tagOverlay)`: a tag overlay run - 32767, every other run + 1. It holds the effect-mesh slot
      (`AddEffect`, `RepeatEffect`) and the status-flower slot (`SetItems`, `Refresh`, `ItemOrders`), and a change test
      copying Unity's approximate `==` (squared difference below 1e-10). Its chunk table is a `SortedList`, so a re-collect
      allocates nothing. `ViewFrustum` (`FromPose`, `FromViewProjection`) is the plane test, with a margin. Unity's
      `TransparentTerrain` now only uploads: it compares the raw effect inputs first, as before, and computes the
      `GeometryUtility` planes only when `NeedsRefresh` says so; its output is unchanged. Open point 4 is decided by a
      prototype and the engine source (Godot 4.8, commit c51ff06e8): one mesh with ordered surfaces is impossible, because
      the transparent sort is an unstable introsort, `MAX_MESH_SURFACES` is 256 and Level7 makes up to 192 runs, and
      `render_priority` is per material and limited to -128..127. Godot draws one `RenderingServer` instance per run, at the
      origin, with `sorting_offset` set to the stream's order: its transparent depth is distance(camera, pivot) -
      `sorting_offset`, and `InstanceSetPivotData(order, false)` puts the pivot at the origin (the default is the AABB centre,
      `use_aabb_center` true; 3e's `SortFromOrigin`). Each run is one triangle list, so
      the order is exact, run for run and triangle for triangle; a capture with the offsets negated changes 9,941 pixels, with
      visibly wrong compositing. Runs upload with `MeshSurfaceUpdateVertexRegion` and `MeshSurfaceUpdateAttributeRegion`;
      normals are uploaded in 3i. The 3c weakness was mostly double linearisation, not linear-space blending: Godot linearises a
      `Color` set on a plain `vec4` uniform, and 3c's shader linearised it again. Now the uniform is `source_color,
      color_conversion_disabled`, the shaders write stored bytes / 255, and the camera has an `Environment` (linear
      tonemap, exposure 1, neutral adjustments, a 4096x1 half-float colour-correction table of `srgb_to_linear`), so the
      window shows the buffer values and every blend happens in gamma space, as in Unity. The HUD and front end are
      unaffected. The overlay is within 1 byte of the gamma prediction (mean error 0.107), and opaque pixels are within 1
      byte of 3c. Cost: a re-sort takes about 0.2 ms on Level1, about 0.5 ms on a Level7 glide (94 runs) and 1.57 ms mean
      on Level7 with 192 runs and 1,916 triangles; the first re-sort after a load takes 5 to 19 ms. Allocation is 0 B after
      warm-up (Level7, 3,000 frames: 1.69 MB in the first 60 re-sorts, then 0 B in the next 141); Godot's interop calls
      allocate no managed memory (read from the GodotSharp IL). 3k gave flowers and effects their order through sorting
      offsets (`BlendedItemOrder`, not `PlaceItems`/`RunsBehind`). Mutation
      checks: the developer's 39 and 20 more, the reviewer's about 98, each now killed, equivalent or failing to compile.
      Verification: `dotnet test DK2.slnx` 10,676 passed, 30 skipped (Presentation 2,692); the Level1 turn-400 hash is
      unchanged (`D7290428D81A902F`); the `DK2.Unity.Compile` stand-in passes; Unity's batch compile, `SmokeTest` and
      `SaveSmokeTest` passed, and `ClassicWorldLightingProbe`'s Level7 start, tick70 and shifted frames
      (which draw the gem crystals through the moved stream) are byte-identical to the frames before 3d. Captures: a Level7 glide across the gem crystals shows no popping, and the 42,069 crystal
      pixels are byte-identical over a 130-frame hold; Level1's red tutorial overlay is now at Unity's strength (it adds
      64/65 to green and blue). Not captured: the sight fog, which needs a Sight of Evil cast that nothing stages. Open:
      (1) a launch argument that stages a Sight cast (or the fog) makes the fog capturable. (2) Godot's float buffer does not
      clamp between blends, where Unity's 8-bit target might; no capture has compared the two.
- [x] 3e. The pointer, cage and placement (2026-11-17, ADR-0805). In `DK2.Presentation`: `TileHoverOutline.Build` with
      `TileHoverRibbon`, out of Unity's `ClassicTileHoverOutline`: the cage's edge masks, the 0.05 lift around the lattice
      bend, the camera-facing ribbon side, red or blue x `BluePeak`, and the pulse read before it advances. Unity's cross
      product and `normalized` (1e-5 cut) are written out per component, so the floats are Unity's; the Unity class now only
      uploads. `PlacementCursorHud` is `ClassicPlacementOverlay.Draw` as a `HudFrame`: icon names, DK2's draw order, the No Go
      shift, held icons, I-Dismiss, the overflow icon and the price digits (the i-/lo- family at 640 wide, colours, offsets,
      the last 7 digits). Unity's overlay is a thin wrapper drawn by `HudCanvas`, which snaps edges to whole pixels; the
      reviewer judged that equivalent to UI Toolkit's rounding. The refund colour, the 1/1200 scale, the +30/+10 offsets and the
      spell No Go shift stay HYPOTHESIS, as in [placement-cursor.md](research/placement-cursor.md), and the code says so.
      `HudImagePixels.KeyGreen` and `HudImageVariant.GreenKey` are the hand sheet's green key; `HandCursorHud` and
      `InstallHandCursorSheets` draw and load the sheets. `PointerScript` with `LaunchArguments.PointerPath`/`ParsePointerPath`
      reads `--pointer-path "frame:x,y[:LlRr];..."`: audit open point 10 is decided as a scripted path, not an input-journal
      replay. Godot pushes its events into the viewport and never warps the owner's cursor; `ScriptedPointerGate` drops OS
      mouse events while a script runs, and non-finite coordinates are rejected. Godot: `WorldScene.TryPickRay` builds the ray
      from the placed pose through `MinimapCameraView.RayDirection`, not `Camera3D.ProjectRayNormal`, which in 4.8 assumes a
      symmetric frustum and drops `frustum_offset` (`camera_3d.cpp`, `projection.cpp` at c51ff06e8; the reviewer confirmed it);
      the pose is computed once a frame and `TryPickRay` reads it. `InGameHud`'s world pointer runs through `TilePicker`,
      `TileTopPlane`, the fog test, `WorldHoverPicker.TryCastTerrain` and `HoverCursorResolver`; tagging and placing post through
      the shared `WorldPointerInput`/`WorldClickCommands`. Entity offers came in 3h. `ClassicHandCursor` draws on canvas layer 101 with the OS pointer hidden while it shows, and
      `ClassicPlacementOverlay` on layer 100. `CageRibbon` uses Godot's own shader with the owner-matched profile pow(centre,
      2.15) x 1.6, additive, no depth write, both faces. A Godot instance sorts by its AABB centre by default
      (`use_aabb_center`), so `SortFromOrigin` (`InstanceSetPivotData(order, false)`) is now the one place the blended runs and
      the cage set their order; the cage's 0 sorts after the tag overlays and under every other blended run, as Unity's
      sortingOrder 0 did. A Level7 capture shows the cage behind the gem crystals; a deliberately sorted-last build draws it
      across them (9,703 pixels differ). Mutation checks: the developer's 44 and 6 more, and the reviewer's 63, each killed or
      equivalent. Verification: `dotnet test DK2.slnx` 10,754 passed, 30 skipped (Presentation 2,750); the Level1 turn-400
      hash is unchanged (`D7290428D81A902F`) with no pointer argument; the `DK2.Unity.Compile` stand-in passes; Unity's batch
      compile, `SmokeTest` and `SaveSmokeTest` passed. Captures, scripted, from a tick-130 save: the claw cursor over the Heart
      floor; the blue pulsing cage on an earth wall; a dig tag (the tag tint plus the blue overlay, the tag-tint capture 3c
      lacked), which the imps dig out about 25 s later and then claim; a Lair cursor with No Go and the red price 300 at gold 0.
      No Unity Editor probe renders the cage or the placement cursor. Not exercised: a real OS click dropped under a script
      (it needs the owner's mouse), and hiding the OS pointer. Open: (1) the shared `HoverCursorResolver.Resolve` still
      allocates 128 B a frame with the Hand and 360 B with a room tool, because `PlacementCursor.Room` builds a new List and
      HashSet per call; it is older than 3e and Unity hits it too, so it belongs in a separate `DK2.Presentation` change.
      (2) 3h took the world hover text and the red tint; the hand light came in 3i; the hover sound came in step 4.
- [x] 3f. Objects, structures and torch meshes (2026-11-17, ADR-0805). In `DK2.Presentation`: `RenderRotation` (yaw, roll
      about Z, pitch about X as a `System.Numerics` quaternion, plus `Heading`; Unity's `RenderEulerRotation` only converts
      it, and the composition now has tests). `ObjectMaterialLook`, `ObjectPass` and `ObjectModelPasses`, out of
      `ClassicMeshes`: `Own`; `EnvironmentPass` (which materials get one, and its light); `Blueprint` and `BlueprintOverlay`;
      `HoverTwin` (`HoverTwinQueueOffset` 1; the description only, the red pass is 3h); `Tinted` and `InstanceTinted` (the
      door's and an element's tint); `Faded` (alpha-blended, still writing depth, and a faded environment pass draws black);
      `IsDrawn`; one pass per submesh and then the environment passes; and the `TexturePhase` material set.
      `EffectGroupParts.Of(KmfGroup)` holds the KMF group part offsets (HYPOTHESIS, [heart-intro.md](../research/heart-intro.md)).
      `ClassicTorchFlame.RenderPoint` is the flame point bent by the lattice and `ClassicTorchFlamePhases` the `Dk2Lcg(1)`
      phase offsets in first-seen order. Unity's `ClassicMeshes`, `ClassicTorchFlames` and `RenderEulerRotation` call
      them. Godot, in the new `WorldScene.Objects.cs`: shared model meshes and one node per object with per-surface looks; the
      Heart, animated through `CreatureAnimationPlayer` with `HeartPulse`'s beat (its on-screen test is a point test on the
      origin, where Unity uses `renderer.isVisible`; the mana effect came in 3k, the beat sound in step 4); structures
      (the door tint, the blinking blueprint, the lock mesh); torch meshes bent by `TorchGeometry`, bent again in place on
      tile changes and given a `CustomAabb`; flames as additive billboards. `ZeroOrderOffset` puts blended objects and
      flames in Unity's sorting-order-0 window: after the tag overlays and before runs >= 1, back to front by distance to
      the bounds centre. The presenter now sends Godot a `TorchLayout`. The environment pass, which needs the env-map coordinates in the vertex shader, came in 3i. Mutation checks: the developer's 41 and 2 more, and the reviewer's
      57, each killed, equivalent or unreachable. Verification: `dotnet test DK2.slnx` 10,802 passed, 30 skipped
      (Presentation 2,779); the Level1 turn-400 hash is unchanged (`D7290428D81A902F`); the `DK2.Unity.Compile` stand-in
      passes; Unity's batch compile, `SmokeTest` and `SaveSmokeTest` passed. Unity's `ClassicWorldLightingProbe` frames
      (Level1 and Level7; start, tick70, shifted and torch-only, with and without torches) are byte-identical to before 3f,
      16 of 16, so the Unity side of the move changed nothing visible. Godot captures against those frames, from the same
      level-start pose: Level1's terrain and the Heart in its pit line up to the pixel; all nine wall torches and their
      flames are in place at the right size; at tick 70 the Heart's arches and big steps match in place, shape and size; a
      tick-32 save shows the fading arches at about half alpha, the depth they write hiding what is behind them; Level7 shows
      the Heart, torches, crystals and markers in place. Imps (3g) and lighting (3i) were missing then. Allocation: object draws
      allocate 0 B a frame in every phase (hearts, objects and clips, torch bend, flames, sort), on a real-renderer Level7
      run (294 steady frames and 46 bend frames) and on Level1 headless (2,782 frames). Findings: (1) Unity's wall flames
      are drawn upside down, a defect that predates 3f. `ClassicTorchFlames` sets `SpriteRenderer.flipY`, which
      `ClassicEffect.shader` never applies (URP applies it only in its own sprite shaders); the shipped frames are upright
      and DK2 draws billboards top row up ([object-drawing.md](../research/object-drawing.md) §10, 0x0057F3D0); a crop of the
      probe frame against Godot's capture shows it, and Godot's upright flames are right. Fixed after 3f: Unity now
      loads the frames bottom row first and no longer sets `flipY`; only flame pixels of the probe frames changed. (2) DK2 draws a plan over an
      environment-mapped material as the plan overlay plus one additive environment pass; Unity draws the flat overlay twice
      (older than 3f) and Godot, since 3i, the overlay plus the environment pass. [structure-drawing.md](../research/structure-drawing.md) §5 and the record of
      0x00581190 have the reading (STRONGLY INFERRED). (3) The cage (sorting order 0) drew under blended objects in
      Godot, where Unity sorts it among them by distance; 3i gave it a `ZeroOrderOffset` key. Open: (1) and (2) were 3i's, now
      done (the env pass over a plan is not yet captured, see 3i). (3) A separate commit fixes Unity's upside-down flames.
- [x] 3g. Creatures (2026-11-17, ADR-0805). In `DK2.Presentation`, out of Unity's `DungeonScene.DrawCreatureFrame` (audit open
      point 7 is decided: a shared frame): `SceneFrame` over `ISceneFrameHost`, with `SceneCreatures`. Before the creatures it
      starts the hover overlay's frame and advances the Hearts' health; then per creature the place from `PoseInterpolator` (a
      false skips the rest), the clip, hiding the possessed model, the footstep crossing and request (the drawn place is the
      remake's choice; the cadence stays HYPOTHESIS, [sound-effects-continuous.md](../research/sound-effects-continuous.md)), the
      burning red, the crackle, the head point and the gem sparkle; then the objects and the torch refresh. It holds the drawn
      creatures' views, the head table, the sparkle pace, the hidden creature and the draw clock, and Unity's `DungeonScene` hosts
      it. Two shared allocation fixes reach Unity too: `CreatureHeadTable.Set` used the `ConcurrentDictionary` indexer (48 B per
      creature a frame) and now rewrites one slot per creature under a lock (the worker reads it through `Attachment`);
      `CreatureAnimationPlayer` keeps one sample buffer per clip, not per segment (it reallocated when the walk and idle vertex
      counts differed, so its "allocates only when a new clip set arrives" comment was wrong). Godot, in the new
      `WorldScene.Creatures.cs`: shared KMF models, one node per creature, clips through `CreatureAnimationPlayer` with in-place
      vertex upload from per-template mesh copies (bounded by the distinct clips per entry; Unity releases its copies on a clip-set
      change), clip sets cached per entry, pose interpolation and orientation through `RightHandedFrame` with the head point
      converted back, removal on death or exit, and `ZeroOrderOffset` for blended surfaces. On an idle-clip swap Godot keeps the
      node and restarts the clip, which the reviewer confirmed is equivalent on screen to Unity's rebuild. Crackle is wired in 3l;
      footsteps came with sound (step 4); the sparkle came in 3k, the burning red in 3h, the possession hide in 3m. The query follow now runs after `DrawFrame` and reads the drawn position, as
      Unity's does (this resolves 3b's note); the blended stream's sort moved from `Flush` into `FinishFrame`, after the follow
      places the camera. Mutation checks: the developer's 30 and the reviewer's 27, each killed except the head table's lock,
      which a two-thread test covers. Verification: `dotnet test DK2.slnx` 10,814 passed, 30 skipped (Presentation 2,790,
      Convert 206); the Level1 turn-400 hash is unchanged (`D7290428D81A902F`); the `DK2.Unity.Compile` stand-in passes; Unity's
      batch compile, `SmokeTest` and `SaveSmokeTest` passed, and `ClassicWorldLightingProbe`'s Level1 frames are byte-identical
      to the frames after the flame fix (8 of 8), so the shared frame order changed nothing in Unity. Captures on the real
      renderer: Level1 at the start shows the three imps where Unity's are, with the same size, pose and facing; only lighting
      (3i) and shadows (3j) are missing. At tick 70 the imp is one tick's offset from Unity's frame straight after a load, since
      Unity's probe draws at fraction 1, and is on Unity's spot within nine frames; a comparison at a single frame needs the
      fraction near 1. About 300 frames with a scripted tag on a gold seam: the imps arrive, swing picks, dig it out (gold 0 to
      3000) and leave. With `--query-first-creature` the imp stays centred and the focus moves an even 0.0616 tiles a frame,
      where the settled position jumped up to 0.43 tiles a tick. Allocation: 0 B a frame in every steady phase (hearts,
      creatures, objects and clips, torch bend, flames, sort), headless over 3,300 frames and on the real renderer over 1,500;
      a clip start costs 136 B (one clock object), accepted. Present's about 3.2 KB a frame is the presenter-iterator item of 3c.
      Open: (1) Godot's per-template mesh copies cost memory that Unity releases on a clip-set change; measure it before
      deciding whether to release them. (2) The 136 B clip-start clock object. (3) The presenter iterators in `WorldView` and
      `Scenery` still allocate (3c's item).
- [x] 3h. Entity hover and the Hand (2026-11-17, ADR-0805). In `DK2.Presentation`, as pure moves out of Unity's
      `DungeonScene`: `PickBox`, `PickSphere` (`Of(KmfModel)`, which `ClassicMeshes` now writes into UV2 through) and
      `PickTriangles` in `HoverEntityMeasure.cs`; `WorldHoverPicker.TryAdmit` (the view cull, then the sphere or the bounds
      circle, then the bounds entry, then `Admits`); `ViewFrustum.Excludes(PickBox)`; `HoverOverlayPlan.Overlays` and `Swaps`;
      `ObjectMaterialLook.HoverOverlay`. Unity's `DungeonRunner` hands the picker a `ViewFrustum` built from its camera's planes.
      The cull is an approximation carried over from Unity: DK2's 0x00575D70 tests the KMF bounds sphere against four side
      planes, with no near or far ([record](../research/functions/00575D70.md), STRONGLY INFERRED), and `Excludes` tests the
      renderer box against six. Godot, in the new `WorldScene.Hover.cs`: creatures, hover-target objects, doors and traps
      are offered to the picker with their measures, falling back to the 3x3 sweep. A `ModelSlot` measures a model from its
      renderer box and the sphere; an animated model's pick box is computed during upload, over indexed vertices only, and the
      triangle test runs on the clip's last sample. The red pass is a cached `ShaderMaterial` copy whose `NextPass` is the
      additive red look, and the burning red, a no-op since 3g, uses the same mechanism; the gold hover swaps the surface to its
      texture twin. Looks are re-applied only when a model's hover state changes. The world hover text, with a creature's own
      name, goes through the screen-space hover HUD, as in Unity, not through 3D text. Pickup and drop needed no new code (wired
      in 3e). `WorldScene.PlacedFrustum` is the one accessor of the pose's frustum, shared by the blended stream and the pick.
      The hover sound came in step 4; the Hand's light in 3i. Verification: `dotnet test DK2.slnx` 10,833 passed, 30
      skipped (Presentation 2,809, the hover tests 67); the Level1 turn-400 hash is unchanged (`D7290428D81A902F`); the
      `DK2.Unity.Compile` stand-in passes; Unity's batch compile, `SmokeTest` and `SaveSmokeTest` passed, and `ClassicWorldLightingProbe`'s
      Level1 frames were byte-identical to 3g's, before and after the review fixes. Mutation checks: 33 run, 29 killed,
      2 did not compile, 2 survived and were killed by added assertions. The reviewer confirmed the Unity
      move is pure and that the red passes in gamma space: over dark texels green and blue bottom out at 64 and 62, and the near-solid red
      is red saturating, as in Unity. Scripted captures show the hover red on an imp, a pickup, the drop cage and the drop. Allocation:
      the pick and the tint allocate 0 B a frame in steady state (the exit report prints it). Not captured: the gold swap and the
      burning red (no hoverable gold at tick 130). Not verified: Unity's `Bounds.IntersectRay` from inside a box and
      `TestPlanesAABB` on a tie, which `RayEntry` and `Excludes` stand in for. Finding: a sweep pick can land far from the pointer over a wall top (the shared sweep, the same in Unity).
      Open: (1) capture the gold swap and the burning red once a staged level offers them. (2) The cull tests the renderer
      box against six planes where DK2 tests the KMF sphere against four.
- [x] 3i. Lighting, the classic baseline (2026-11-18, ADR-0805). Pure moves into `DK2.Presentation`: `ClassicVertexLight`
      (`TableTile`, `TableSum`, `PackedDiffuse`), tested against a C# transcription of the HLSL
      (`LegacyClassicVertexLit`); `ClassicLightTableLayout` (49 texels a tile, 83 tiles a texture row, so at most 4,096
      texels, and the GLSL constant substitution). `ClassicLightTable.Reuse` and a table pool in `ClassicWorldLightCache`
      (and an indexed loop) make a scrolling view allocate 0 B after warm-up (tested). Godot, in the new
      `WorldScene.Lighting.cs`: an unshaded vertex-lit shader with the 24-source tile tables in float textures; the
      publisher, the ambient keys and the torch, portal, object and Hand lights (deferred from 3h; the construction
      effect sprite uploads and lights are now wired; [scope](../../godot/README.md#shot-effects)); the environment pass as the submesh material's `NextPass`, also over plans (a faded
      one draws black); the cage's `ZeroOrderOffset` key, set in the frame; the liquids' normals uploaded; exposure folded
      into the gamma table of the camera's colour correction, with no canvas pass; and `skip_vertex_transform` on every
      world shader (Godot 4.8 drew moved models black otherwise; the cause, Godot's shared depth-prepass material, was
      found and the workaround removed in audit item 16, `docs/TRAPS.md`).
      Look (owner, 2026-11-18): the target is Unity's real in-game look, from the owner's saved scene (native initial
      ambient 1, fog of war on): DK2's own light with ADR-0522's distance fade 0.5 to 1, and no fill or sun. Godot
      defaults to that; `--light-fill` adds ADR-0522's fill (tint 0.55/0.44/0.28, unlit 0.25, floor 0.5, sun 1.1, Godot's
      fitted sky). The audit's open points 2 and 3 are corrected: the old probe frames had no grading and no sun (cd4e04a0
      fixed that). The light publisher's pose (open point 5) stays matched to Unity: zero shift, pitch and yaw. Reviewer:
      no blockers in two rounds; the DK2 terms, GLSL arithmetic, texel layout, environment pass and vertex layout
      (`rendering_server.cpp`) match; fixed: the allocation attribution (the tables, now pooled), hardcoded GLSL constants
      (substituted; the formula lines are pinned and counted in the HLSL and GLSL, 26 of 26 mutants killed), the cage key's
      one-frame lag, the texture width cap. Verification: `dotnet test DK2.slnx -c Release` 10,967 passed, 30 skipped, 0
      failed (Presentation 2,924). Mutation checks: the moved code 33 killed and 4 equivalent; the pool 4 killed and 2
      equivalent; the shader text 18 of 18; the layout 8 of 8. Unity's batch compile, `SmokeTest` and `SaveSmokeTest` pass; with
      the 3i tree the old probe's Level1 and Level7 frames are byte-identical to 3g's and the in-game probe frames are
      byte-identical with and without the pool. The Level1 turn-400 hash is `60C97ECC6F9F0501`, the same on clean master
      b3a31f5e (other sessions' simulation commits changed it from `D7290428D81A902F`; 3i does not touch the simulation).
      Godot's in-game frames against Unity's are within 0.002 luminance and 0.004 saturation in every pair (Level7
      shifted without torches: saturation -0.016); with torches luminance is 0.21 to 0.23 and saturation 0.75 to 0.82. The
      Acceptance (owner, 2026-11-16: not too dark, too bland next to Unity's frames) is met by those figures; the
      owner's own look at the frames is not recorded. The lighting step costs about 2 ms a frame. Allocation: a still camera allocates 3.4 to 5.3 KB per 467 frames (bounded
      warm-up); `ClassicLightVisibility.BuildPolygon` (shared) allocates about 1.8 KB per camera move, left for a separate
      change, plus bounded first-visit growth. Not verified: the environment pass over a plan, a faded one and the cage key
      (none captured), the ambient keys live, a non-default brightness, the wrap past 83 tiles at runtime. Probe review
      notes, both pre-existing: an already-open dirty scene, and the tile-light globals not restored after a run.
- [x] Modern lighting prototype (`--light-modern`, real omni lights and Godot's shadows over DK2's sources; built
      2026-11-18 in f8843f83): rejected by the owner for its look and removed (2026-11-18), with `ModernLightSelector`
      and `ClassicWorldLightCache.CopyPreparedSources`. Classic frames stayed byte-identical through the removal.
- [ ] DK2 light per pixel (`--light-perpixel`, classic the default; ADR-0813). The fragment point-light representation
      is continuous; the classic per-vertex reference remains unchanged. Modern AO (`--light-post`) uses filtered
      compute passes before transparency. Historical cause, focused proofs, scoped scene validation and measured costs:
      [torch-light artifacts](../research/godot-torch-light-artifacts.md). Owner visual acceptance remains separate.
- [ ] The rest of step 3, in the order of [the modern-rendering audit](2026-11-18-godot-modern-rendering-audit.md)
      (ADR-0813; it supersedes the step-3 audit's move order from 3j; acceptance is the owner's eye, 3o's numbers and
      gameplay correctness, not byte-identity with Unity):
  - [x] 3o. Performance harness (2026-11-18, ADR-0813; audit item 0). `--perf-report <file>` on `InGameHud.tscn` samples
        every frame after a warm-up (`PerformanceReport`, `PerformanceReportTail`, `WorldScene.Performance.cs`; the shared
        `FrameRecording` and `CameraScript` in `DK2.Presentation`, 17 tests): frame time, process time (between the first-
        and last-priority nodes), viewport CPU and GPU render time (the GPU's lags about 2 frames), frame setup, draw
        calls, objects, primitives, video, texture and buffer memory, main-thread allocation per frame and all threads'
        and the main thread's totals over the window, collections, pipeline compilations. The exit table gives mean, p50,
        p95, p99 and max, the frames over 16.67 ms, and still and moving phases; the JSON (`dk2-godot-perf/1`) adds
        machine, GPU, driver, build and load information and the adapters' counters. The harness's own allocation is
        expected to be 0 and is reported as `harness.selfBytes` and `selfMax`. Options: `--perf-camera`, `--perf-warmup`,
        `--perf-frames`, `--perf-size`, `--perf-vsync`; a malformed value quits with an error. Recipe: `godot/perf.ps1`
        (Level1 and Level7, 1920x1080 and 1024x576, real time from a save, a fixed camera loop, seed 1, 120 warm-up and
        1,800 measured frames, machine load sampled, C# built first) and `godot/perf_compare.py` (two labels by median,
        warnings for setting mismatches, grouped by render size). Reviewer: no blockers; all 8 findings fixed (the
        per-second process monitors replaced, the imprecise all-threads allocation, the `selfBytes` claim, the GPU lag
        documented, script bugs, the compare warnings, silent fallbacks, tests). Baseline (Radeon 780M iGPU, Vulkan,
        Forward+, Debug C#, a heavily loaded machine, so rough), GPU p50 at 1080p: classic Level1 1.9 ms, Level7 2.4 ms;
        modern Level1 6.2 ms, Level7 9.3 ms; draw calls classic 143 and 320, modern about 490 and about 995. Open: a quiet
        re-run, from which step 4's targets are set.
  - [x] 3p. Material model, audit items 7 and 15 (2026-11-19, ADR-0813): one world shader, `Shaders/World.gdshaderinc`
        with DK2's light in `WorldLight.gdshaderinc` (pinned by `ClassicVertexLightShaderTextTests`, now expanded as Godot's
        preprocessor does, with and without the shadows), in four render-state `.gdshader` files; full light, model inputs
        and the environment pass are material uniforms, an object's tint and fade, a structure's tint and the hover's red
        are instance uniforms (the shared `ModelInstanceLook`, tested against `Tinted`, `InstanceTinted` and `Faded`);
        `_redMaterials`, `_chainedMaterials` and `Copy` are gone, and only an alpha-blended surface keeps a next pass, one
        additive material with its environment pass and red over the whole triangle (DK2's 0x200 overlay, so a Fear
        Trap's or Hero Portcullis's blended parts redden whole); `--light-perpixel` and `--light-shadows` add their defines
        once a run, kept for the run; the cage and flame shaders are files too; `ClassicLightTableLayout.Substitute` is
        deleted. Frames against HEAD at
        Level1 start and tick 70 (Movie Maker, 1920x1080, scripted pointer): flags off identical but the Heart's
        environment-mapped surface, about 2,500 to 4,900 pixels off by 1 (the pass is added in the shader, not blended
        in the half-float buffer); with `--light-perpixel --light-shadows --light-post` 3,800 to 5,100 pixels, all but 3
        off by 1 to 3 (the environment shine now sits under the occlusion), none visible side by side. A hovered imp is
        now red all over; HEAD drew its red twin with black triangles (the `docs/TRAPS.md` depth trap). Every shipped
        Fear Trap and Hero Portcullis is the heroes' and unexplored at its level's start, so the blended red was captured
        with a capture-only build reddening every object over the Heart's fading arches: whole triangles red, as HEAD,
        less HEAD's black ones. Harness (Level1 and
        Level7, 1080p, `-Repeat 2`, 20 to 30% other load, rough): world shaders 4-6 to 2-3, materials 32-37 to 27 and
        108-113 to 94, draw calls -5 (L1) and -5 (L7) flags off, -11 and -7 flags on; flags-off GPU p50 1.91 to 1.96 ms
        (L1) and 2.72 to 2.59 (L7), frame p50 4.38 to 4.49 and 5.92 to 6.41 (inside the load noise); flags on, GPU
        p50 within the noise (interleaved Level1 runs: HEAD 5.28 and 5.93 ms, after 5.75 and 6.12; the same build
        spans 5.3 to 6.2 ms across runs); per-frame allocation p50 unchanged.
        Turn-400 hash unchanged (`B5C0CB842094AB59` on HEAD and after). Per-tile state: see 3p-b.
  - [x] 3p-b. Terrain state texture (2026-11-19, audit item 3):
        `TerrainTileState` and per-vertex tag/tint sources preserve `TerrainMaterialKey.For`'s colour rules, including
        neighbour walls, quarter floor tint and additive recipes. Godot opts into state-only updates without chunk or
        lattice rebuilds; Unity keeps its existing path. Rendering and upload limits: [Godot README](../../godot/README.md).
        Turn-400 hash unchanged (`B5C0CB842094AB59`); the full solution suite (3,233 Presentation tests), builds,
        Godot import/build-solutions and Unity sync/compile/smoke/save-smoke gates pass. Final captures, warmed dig-tag
        timing and alternating repeated performance results: [audit item 3](2026-11-18-godot-modern-rendering-audit.md).
        This completes source parity for the terrain-state path; it does not establish native visual acceptance
        in every scene. Native cage expectations and remaining comparison limits: [hand cursor rendering](../research/hand-cursor-rendering.md#native-cage-observations).
  - [x] 3q. Blended ordering, audit item 5 (2026-11-19, ADR-0813): `ZeroOrderOffset`, `SortFromOrigin` and the
        unindexed run rewrites are gone. The shared `BlendedRunSlots` (`DK2.Presentation`, tested, including a fake renderer
        that applies only what each update reports) keys each run of the stream by its distance from the eye to its
        nearest triangle centroid, falling by at least 1/1024 tile down the stream so the runs keep DK2's back-to-front
        order (0x0058B9D0, 0x00588D00); tag overlays now use the static shader batches in 3p-b above. Godot 4.8
        sorts transparents by the straight-line distance to a pivot, not by depth along the view (render_forward_clustered.cpp
        at c51ff06e8; two quads in a scratch project drew in distance order where the measures disagree), so the runs are
        keyed in that measure and sort from the origin with the matching offset; blended objects, flames, the cage (its box
        the ribbon's) and the effect batches (each boxed around its live sprites) are keyed by their box centres in the same
        measure and now fall between runs instead of all under them. A run takes, in two passes, the slot of its material
        that last drew its first corner, else a free slot nobody claimed; a slot keeps its vertices, so a re-sort uploads
        the indices (`MeshSurfaceUpdateIndexRegion`) and only the vertices the slot never held. Found on the way: Godot adds
        a tangent beside each normal, so the runs had written their normals at the wrong vertices; the slot reads the
        surface's real layout (`docs/TRAPS.md`). Frames against HEAD (Movie Maker, 1920x1080, scripted pointer, flags off
        and `--light-perpixel --light-shadows --light-post`): Level1 start (12 frames), tick 70 (40) and a dig-tag drag
        after the opening path with the cage, tag overlays, the Heart and the flames (90) byte-identical, but for one
        terrain-edge pixel in the drag with the flags on (by 18 to 25, gone with the effect-batch box backed out); Level7
        start differs only over the Portal's crystals (about 17,000 to 18,000 pixels): their light is smooth instead of in
        triangle blocks (the normals, up to 34 a channel), and a flame nearer than the crystals draws over them (about 130);
        judged more correct. Harness, 1080p, interleaved with HEAD, 2 repeats, 11 to 23% other load: draw calls, objects
        and run instances (94 on Level7) unchanged; Level7 uploads 1.74 MB for 209,084 drawn triangles over 903 re-sorts
        (8.3 B a triangle; HEAD rewrote 96 B for each triangle of a changed run); re-sort CPU mean 0.28 ms on HEAD, 0.36 to
        0.39 ms; Level7 moving frame p50 6.84 to 7.37 ms (mean 7.47 to 7.63), GPU p50 unchanged (2.45 ms); Level1 frame
        and GPU p50 unchanged within the noise (4.50 to 4.37 ms, 1.86 ms); per-frame allocation p50 unchanged, the re-sort's
        after warm-up 48 KB against HEAD's 138 KB. A box centred on each run's nearest centroid, which leaves Godot to
        measure the distance and saves the per-frame pivot calls, measured no better (7.29 ms) and was not kept. Turn-400
        hash unchanged (`B5C0CB842094AB59`). Open: (1) DK2 orders its one list by depth along the view and Godot by
        distance, so near the screen's corners a blended object up to about 20% nearer along the view can still draw under a
        run; (2) a blended object straddling a run sits wholly on one side of it (DK2 interleaves per triangle; splitting
        runs at blended objects, as at flowers, would close it); (3) done in 3k: each effect sprite is placed on its own; (4) Level7's moving frame costs 0.2 to 0.5 ms more than HEAD's,
        partly the re-sort; (5) the liquids' normals had the same tangent-layout fault (gone with audit item 9, which waves them in a shader).
  - [x] 3j. Creature shadows, folded into lighting: done as lighting step 2 (`--light-shadows`, ClassicShadowSourceSelector,
        the 2 torches nearest the camera focus; real distance-map shadows, no silhouette projection).
  - [x] 3k. Effects and flowers on MultiMesh over the CPU particle simulation, with the torch flames (2026-11-19, ADR-0813;
        audit item 6). The shared `BlendedItemOrder` (`DK2.Presentation`, 35 tests) answers 3q's open item (3): each effect
        sprite, flame and flower layer is placed among the runs by its own distance, never crossing a run, and joins its
        batch's latest draw when it moves earlier past nothing it overlaps on screen (`BlendedItemRect.OfSphere`; additive
        items pass each other), so the pixels are DK2's one back-to-front list wherever the boxes are tight; a blended model,
        torch holder or the cage that Godot sorts by its box is a barrier at its own distance (additive when every such
        surface is ONE, ONE without a depth write); spread keys keep a few float steps at the sort's magnitude. The shared
        `EffectFrame` (2 tests) makes a frame's requests in Unity's runner order: the loose gems' idle effects after the
        present, the scene's mana effect and gem sparkle, then the shots and the advance; Unity is not wired to it yet.
        Godot (`WorldScene.BlendedItems.cs`): one `RenderingServer` MultiMesh instance per draw, pooled per batch, keyed as a
        run, boxed around its items (a world-sized box was culled by the possession camera's far plane); items and barriers
        the frustum culls are dropped; 4.8 sets a MultiMesh buffer only whole, so the capacity follows the count (halved after
        120 low frames) and an unchanged draw is not uploaded. The flames are one `Texture2DArray` and one material, the frame
        in the custom data (was one instance and one material per frame per flame); sprites and flower icons are `.gdshader`
        files; mesh particles (the Heart's plug, group parts, type-6 frames, shot bodies) are pooled models per mesh with the
        element's tint as an instance uniform. Also wired: the group parts, the local pickup dust, the first model update.
        Footsteps and the beat sound are sound only (wired in step 4); the dollar mesh is not drawn; lightning is drawn by 3l. Frames
        against HEAD (Movie Maker, 1920x1080, scripted pointer, flags off and `--light-perpixel --light-shadows
        --light-post`): flames, flowers and all but the Heart byte-identical in those examples; this does not establish
        native flower overlap with opaque walls ([comparison limits](../research/hand-cursor-rendering.md#native-cage-observations)). New content judged right against
        [heart-intro.md](../research/heart-intro.md): the plug lid, beam and burst at each level's start, the mana sparkles on
        a beat; the possession stars overlap differently (a batch now draws back to front, HEAD in particle order; the mana
        effect, the first update, the group parts and the culling were each ruled out). Harness (1080p, interleaved, 14 to
        47% other load): Level7 setup CPU -55%, draw calls 190 to 187.5 moving (46 flames at most 21 draws among 94 runs),
        frame p50 lower; Level1 (4 rounds) draw calls -3 to -6, frame p50 +0.12 ms moving and +0.25 ms still (the item steps
        measure about 0.2 ms, 0.13 to 0.16 of it the particle walk HEAD also made), GPU p50 within noise; allocation p50 not
        grown, the items' steps 300 to 460 B after warm-up beside art loading. Turn-400 hash `B5C0CB842094AB59`. Open: (1) the
        Heart's intro takes up to 166 draws (alpha dust and additive sparks overlap at interleaved depths; exact, as Unity's
        per-material runs); (2) effect lights stay DK2's; the flames' HDR glow waits for the post effects; (3) the flowers
        keep HEAD's `source_color` (linearised), which the gamma-space convention would draw brighter; (4) the run slots also
        carry a world-sized box and may be culled the same way in possession; (5) wire Unity's runner to `EffectFrame`.
  - [x] 3l. Lightning (2026-11-19, ADR-0813; no-lightning cost about 6 us a frame, frame p50 within run noise). `LightningDrawList` now owns shot selection,
        interpolation, all target kinds, procedural lightning and clip-install crackle. It builds ribbons in camera space and fills reused
        render-world buffers, which both adapters consume; Unity retains native point conversions through `ILightningCameraTransform` (TRAPS).
        Godot submits each four-corner
        section through the existing additive `BlendedItemOrder` pool, with exact corner payloads, upload comparisons and
        geometry bounds. `Lightning.gdshader` uses the original texture, unshaded and additive, without depth writes.
        `--stage-lightning` supplies a real combat cast and a paid Thunderbolt through existing hooks and commands
        ([launch recipe](../../godot/README.md#verifying)). No simulation change or hash/save bump.
        Verified: 65 lightning tests and 3 human-command policy tests pass; the final full suite has 11,506 passed and
        30 skipped. Warmed ribbon/crackle and Heart-target generation allocate 0 B (the old Heart resolver test fails at
        55,296 B over 256 frames). Unity's native batch probe matches 122,212 vertices bit for bit across three camera
        poses, 64 seeds and eight frames; colour remains the same texture-only output. Archived HEAD and new Level1
        start/turn 70 and Level7 start captures at 1920x1080, 30 fps, with scripted pointer, flags off and all three
        modern-lighting flags on, match in all 1,056 repeated PNG frames. Turn-400 hash is unchanged at `B5C0CB842094AB59`.
        Release solution and Godot builds, headless import and `--build-solutions`, and Unity sync/compile/smoke/save-smoke
        gates pass. Staged captures show the creature's triple bolt, torso crackle, paid keeper cast and a visible ribbon
        across the possession view. No dedicated glass/lava overlap capture or Unity beauty frame was taken; ordering
        retains the reviewed blended-item path, and Unity geometry parity is tested by the native probe.
        The first staged 300-frame performance window has whole-frame p50 9.242 ms, process p50 7.318 ms and whole-app
        allocation p50 5,152 B/frame; the lightning counter averages 0.156891 ms/frame. Its 51,736 B after the counter's
        first 60 frames includes first-use/new-kind and pool growth; it is not a steady zero-allocation claim. The warmed
        unit measurement is separate. Staging triggers non-finite-normal warnings in the existing `ModelMesh` upload path;
        the lightning shader has no errors and import is clean. No-lightning performance was measured at 1080p with
        literal `perf.ps1 -Repeat 2` runs and a second, strictly interleaved set of two repeats. In the latter, mean repeat
        frame p50 is Level1 5.575 → 5.794 ms (+3.9%) and Level7 7.468 → 7.134 ms (−4.5%); process p50 is
        2.888 → 2.948 and 4.365 → 4.265 ms. Whole-app allocation p50 is unchanged at 7,296 and 10,304 B/frame.
        The new idle lightning counter is about 5.5/6.6 µs per frame, with 0 late bytes. Repeat frame-p50 ranges overlap:
        Level1 HEAD 5.496–5.654/new 5.510–6.077 ms; Level7 HEAD 7.060–7.875/new 6.708–7.560 ms. Earlier datasets
        alternated apparent gains and regressions. There is no consistent timing regression beyond repeat spread,
        but Level1's latest median is higher, so the strict no-regression acceptance remains unproven. Final exact
        main-workspace build, test, import/build-solutions and hash gates also pass after the shared camera-refresh fix.
        Artifacts: `C:\Users\franj\AppData\Local\Temp\dk2-lightning-20261119-886axhag`
        (`captures/`, `captures/crops/`, `perf-interleaved/` and verification logs).
  - [x] 3m. Possession (2026-11-18, ADR-0805; no simulation change, no hash or save bump). Moved into `DK2.Presentation`
        with tests, which Unity calls now: `TerrainCeilingScheduler` and `TerrainCeilingGeometry` (out of Unity's
        `TerrainCeilingChunks`), `PossessionScene` (`Latched`, `Latch`, `RoofShown`, out of `DungeonRunner.cs`); new, for
        captures, `PossessionStaging.PossessFirst` and `--possess-first-creature` (`LaunchArguments.PossessFirstCreature`).
        Godot (`InGameHud.Possession.cs`, `WorldScene.Possession.cs`): the shared `PossessionSession` is driven every
        frame, its pose goes on the same `Camera3D` (fly-in, first person, death view, fly-out), the pointer lock is
        `Input.MouseMode = Captured` while `PointerLockWanted` (never under `--pointer-path`, no `warp_mouse`), the roof is
        one `MeshInstance3D` per 16x16 chunk shown throughout the possession camera hold (terrain material, `Ceiling` texture,
        anchor kind 2), and the lattice latch and the hidden possessed model go through the `SceneFrame` sequence. The
        possession cameras take `PossessionCamera.NearClipDistance` (0.05, a remake choice: DK2 has no near plane,
        possession-warp.md section 2); the ordinary camera keeps 0.3, and Unity still uses 0.3 in first person. With 0.3 a
        wall beside the creature was cut into a black band, with 0.05 it is not. Verified: `DK2.slnx` builds in Release with
        0 errors and 0 warnings; the Possession-filtered tests in `DK2.Presentation.Tests` 106 passed; Godot build 0
        errors; the `DK2.Unity.Compile` stand-in 0 errors; the reviewer found Unity's behaviour identical after the move.
        Captures at 1920x1080 from a save made after Level1's opening path, with `--possess-first-creature` and a scripted
        pointer checked settled first person and fly-out. Entry roof timing and ordinary-versus-possession HUD display
        are corrected by the later [entry review](../research/possession-entry-effect.md#capture-verification);
        [transition pointer feedback and ordinary badges](../research/possession-target-highlight.md#entry-pointer-feedback) are built. Not
        verified: `unity-verify.sh` and `unity-probe.sh` (they cannot target a worktree); in Godot real mouse capture,
        alt-tab re-capture, the walking keys and the death view. Open: the view's sounds are dropped (no sound player, step
        4); the roof is black away from walls and torches (lighting). Scripted paths take camera priority over
        possession (3n). Test gaps: `TerrainCeilingScheduler.Refresh` from a
        `WorldState` (its once-per-turn read) and `PossessionScene.Latch` with a terrain.
  - [x] 3n. Scripted paths, letterbox, jitter and full-map preview (2026-11-20, ADR-0813; closed-map frame p50 within run noise: Level7 classic pairs split two better, two worse).
        Shared render jitter and preview orientation are consumed by Unity and Godot; see
        [camera wiring](../../godot/README.md) and the step-3 audit for the viewport and ordering choices.
        Verification: Release solution build 0 warnings/errors; full suite 11,507 passed, 30 skipped; focused jitter/preview
        11 passed and path-suppression 1 passed. HEAD/new Godot builds, imports and build-solutions clean. Turn-400 hash
        B5C0CB842094AB59 unchanged. The exact main-checkout Unity verification script reports sync, compile, smoke
        and save-smoke all exit 0. Reviewer code approved.
        Capture root: `C:/Users/franj/AppData/Local/Temp/dk2-step3n-yypra1c1`; opening path frame
        `captures/path/f00000089.png` has symmetric 135px bars at 1920x1080; the 1,100-frame path returns to the HUD.
        All 460 ordinary, possession and lightning PNGs are byte-identical to archived HEAD. Populated Level1/Level7
        preview crops were inspected for orientation, terrain, creatures, flames and ordering.
        Preview-open Repeat 2 p50 means (frame ms / GPU ms / draws / allocated B): classic Level1
        4.775 / 1.789 / 153 / 7136, Level7 7.847 / 2.611 / 350.5 / 9440; all lighting flags Level1
        9.442 / 6.385 / 239 / 7136, Level7 13.026 / 6.947 / 526 / 9440. GPU is main plus preview, excluding shadow
        viewports. Whole-frame allocation includes simulation and is identical between repeats; no accumulating preview
        allocation observed. Exact reports and comparisons: `perf-summary/preview-summary.json` under the capture root.
        Closed-map alternating Repeat 2 frame p50 means HEAD → new (ms): classic Level1 4.759 → 4.680;
        all lighting flags Level1 9.381 → 9.094 and Level7 11.422 → 11.450. Classic Level7 was noisy, so four pairs
        were run: median of run p50s 6.805 → 7.066 (+3.8%), with overlapping ranges 6.268–7.208 and 6.541–7.187;
        two pairs improved and two worsened. GPU medians 2.426 → 2.393 ms; steady-camera allocation and draws
        match. This does not establish the required absence of a regression; 3n and remaining-overlay acceptance stay
        unchecked until the Level7 classic frame-time difference is resolved. Raw reports: `perf/`, `perf-modern/`,
        `perf-preview/`; summaries: `perf-summary/`. No subtitles or function-record edits; subtitles remain step 4.
  - [x] Then the remaining replacement: the chunk upload tweak (audit item 12, done 2026-11-24: the build task packs
        the opaque chunk surfaces as Godot's vertex bytes; the liquid waves, item 9, the HUD text atlas, item 11, the
        `skip_vertex_transform` cause, item 16, and AA/scaling, item 17, are done too). No replacement remains. The minimap shader is complete with audit item 10's limits; vertex animation is complete (item 8) (texture arrays remain measured/no-go, item 4).
- [x] The remaining 3D overlays (the scripted paths, 3n), then the in-game HUD from step 1.

## Step 4: media, tools, packaging

- [x] Sound (decoding stays in `src/`), 2026-11-24. Godot plays what Unity's player plays: world and effect sounds
      (positioned beside the camera at DK2's distance gain), the GUI, hover, Heart beat, footsteps, party pop and possession
      sounds, the terrain ambience, speech with its queue and duck, and the music and ambience streams; the front end plays
      its pages' sounds. `godot/README.md` (*Sound*) has the files and the checks.
- [x] Movies (decoding stays in `src/`), 2026-11-24. `MoviePlayback` (`DK2.Presentation`) holds every decision Unity's
      `ClassicMoviePlayer` made (start delay, audio-clock pacing, dropping three behind, skip input, end) and decodes ahead on
      one worker; Godot's `ClassicMoviePlayer` uploads frames into one texture and feeds an `AudioStreamGenerator`. The front
      end plays the startup, attract, gallery and trailer movies and a won map's title card and cutscene. Not drawn yet: the
      title card's text (subtitles batch). `godot/README.md` (*Movies*) has the files and the checks.
- [x] The level sound driver (`DungeonRunner.Sound`, deferred from step 2k), 2026-11-24: its decisions and the player's are
      `LevelSoundDriver` and `ClassicSoundMixer` in `DK2.Presentation`, behind `ISoundOutput`; Godot calls them. Owner
      ruling (2026-11-24): Unity is being phased out, so nothing under `unity/` changed and Unity keeps its own copy
      (`ClassicSoundPlayer`, `DungeonRunner.Sound`) until step 5 deletes it.
- [ ] Godot's speech subtitles (deferred from step 2k): wrap with `BitmapTextLayout`, pack a `.bf4` atlas, and blit
      `BitmapTextPlacement.Place` in `SubtitleLayout`'s boxes, timed by the speech playback.
- [ ] Godot equivalents of the probes that are still needed (headless scenes, Movie Maker
      captures; keep the Python summarisers). `tools/hud-probe` is deleted here; its
      `HudPilot.Compile.csproj` already fails to compile (25 errors: `ClassicHudScaling` and
      `Input` missing from its compile list, stale signatures), so do not repair it.
- [ ] Performance targets set again in Godot, from 3o's harness numbers (owner, 2026-11-15: Unity FPS work stopped).
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

## Step 6: tidy the C# codebase (owner, 2026-11-20; after step 5, on net10.0)

A pass over every C# file in `src/`, `tests/` and `godot/` once Unity is gone and every assembly targets net10.0.
Behaviour stays the same: the turn-400 hash, the replays and the frame captures are the checks.

- [ ] Coding style, defined first and enforced by the build. Survey the styles in use, propose one per question
      (`var`, braces, expression bodies, file-scoped namespaces, `using` placement, naming of private fields, constants
      and async methods, pattern matching, collection expressions, primary constructors) for the owner to choose, then
      write them into `.editorconfig` with severities. `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` make each
      rule a build error, so each rule lands with `dotnet format` fixing every file it flags (`.editorconfig`'s header).
      Rules the analysers cannot express (a number cast to an enum instead of a named member, magic numbers for DK2
      ids) go in a small Roslyn analyzer or an `ArchitectureTests` check.
- [ ] Modern .NET performance first: spans, `stackalloc`, `ArrayPool`, `FrozenDictionary` and `FrozenSet`,
      `SearchValues`, `CollectionsMarshal`, generic math, `[SkipLocalsInit]` where measured, source-generated regex and
      JSON, vectorisation (`TensorPrimitives`, `Vector128/256`) on hot loops; each change measured (BenchmarkDotNet or
      the 3o harness), kept only when it pays. Use the agents' .NET skills: analyzing-dotnet-performance, vectorization,
      microbenchmarking, csharp-refactoring.
- [ ] Sloppy and generated-looking code refactored: casts of numbers to enums (add the member), primitives where a
      meaningful type belongs (AGENTS.md), duplicated helpers, dead code, over-long methods, comments that narrate
      instead of explain, defensive clutter, polyfills and `UnityBound` leftovers.
- [ ] One area per commit (simulation, presentation, data, tools, Godot adapter), each with the hash, tests and
      captures unchanged; any change to DK2 behaviour is a bug, not a tidy-up.
