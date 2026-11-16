# Handoff

Current work and entry points for the next session. Decisions, evidence and open
questions live in their canonical documents; this file links to them.

## Current state

- Milestones and acceptance criteria: [reimplementation plan §8](../DK2_REIMPLEMENTATION_PLAN.md).
  Generate counts with `dk2tools status`; use `dk2tools coverage` for implementation
  coverage. Neither counts nor historical test totals are maintained here.
- Current simulation/save composition: [hash versions](research/hash-versions.md).
  Combat Pit completion and remaining parity limits: [canonical scope](research/combat-pit.md#9-what-is-built-adr-0213-hashsave-523).
  Behavioral parity with DK2 remains incomplete; bounded replay controls do not
  establish full-state, campaign-outcome or interactive-play acceptance.
- Classic rendering, HUD and front-end integration are on master. Owner play,
  visual and hearing acceptance remain open in the
  [completion acceptance record](plans/2026-10-29-hud-and-front-end-completion.md#acceptance-record)
  and [HUD comparison checklist](research/hud-comparison-checklist.md).
  Lighting acceptance and remaining comparison/performance limits live in
  [classic lighting verification](research/classic-lighting-verification.md).
- Engine switch (owner, 2026-11-15): the switch to Godot is decided ([ADR-0805](DECISIONS.md)).
  Stop Unity FPS optimization; continue engine-independent Q710 simulation/replay work.
  Engine-neutral lighting-cache measurements are in [Level6a performance](research/level6a-editor-performance.md);
  local pickup dust timing (ADR-0808) and verification are in [the pick-up's turn](research/hand-pickup-turn.md).
  The hover-cage shader check is in [hand cursor rendering](research/hand-cursor-rendering.md#selection-outline-rendering); live-scene acceptance remains open.
  GPLv3 code linked to Unity's runtime remains [ADR-0041](DECISIONS.md)'s open question. ADR-0805 moves each HUD
  panel's DK2 logic into `DK2.Presentation` as a draw list and says where Godot files go. The bottom
  panel is moved (`BottomPanelHud`, `HudCanvas`) and the owner checked it in the Unity editor on 2026-11-14. The
  resource strip (`KeeperResourceHud`), the message tabs (`MessageTabsHud`), the hover HUD (`HoverHud`), the creature overview and query details (`CreatureOverviewHud`, `CreatureQueryHud`), the in-game options menu, save browser and settings pages (`OptionsHud`, `SaveLoadHud`, `GameOptionsHud`) the front-end menu (`FrontEndMenuHud`, with its label bitmap and glow pixels in `HudLabelPixels`) and the minimap and full-map markers (`MinimapHud`, `FullMapHud`; the minimap's GPU disc and footprint stay Unity, and `MinimapDiscImage` is the disc's CPU twin for Godot) are moved too; they build and their models are
  tested, but none has been run in the Unity editor as of 2026-11-15, and the message-tab frame tile seams against UI Toolkit
  background-repeat are unchecked. Every step of the order in the ADR is done. Three of the four probes that broke during the panel moves are fixed; `run-scaled-integration.ps1` still reads blank panel textures: [known-broken probes](../tools/classic-unity-probe/README.md#known-broken-probes). `godot/DK2.Godot` opens on the classic front-end menu (`FrontEndMenuHud`), drawn by its own `HudCanvas`, and has been run (captures at 1152x648, 1024x768 and 1920x1080, a scripted hover and click to the Options page); the owner has not used it with a real mouse. The remaining Godot work is under Other pending work. The pinned toolchain and agent setup are in [godot/README.md](../godot/README.md) (ADR-0810).
- OpenKeeper clean-up (owner, 2026-11-14): [ADR-0003](DECISIONS.md) makes OpenKeeper facts only and has code taken
  earlier rewritten from the binary one item at a time. The engine-texture decoder is rewritten and matches DKII.EXE's
  own block decoders run in emulation ([texture codec](research/texture-codec.md); Q812, the loading-screen precision,
  is open). Remaining: the items in ADR-0003's audit list, as `THIRD-PARTY-NOTICES.md` lists them.
- Presented-world P1 and its follow-ups are merged. Measurements and remaining
  performance targets: [presented-world result](plans/2026-11-06-presented-world.md#result-measured-2026-11-06)
  and [performance policy](PERFORMANCE.md).

## Active continuation

Q712, the Level 5 draw-continuation investigation, is answered through turn 1900.
At hash 524 with ADR-0811, with the replay inputs including the five hero guards
and the keeper's research points restored at turn 1400, the controlled replay matches all 8466 native draws of the
capture's log, turns 1401-1900 (the log ends there). The result is in
[Q712](research/open-questions.md), with its causes in
[the Heart's pillars and centre-tile objects as obstacles, and the lava centre's refused route](research/structure-blocks.md#53-the-hearts-obstacle-is-forwarded-by-the-room-not-found-on-its-tile-q355) (ADR-0750, ADR-0751, ADR-0755),
[the mover block for every non-Imp](research/steering-behaviours.md#75-the-creatures-tick-0x004d5a40-and-its-callers) (ADR-0751),
[the generic stop self-point](research/stop-self-route.md#every-non-imp-stops-the-same-way-q712-adr-0751) and
[a hero's arrival at an action point](research/stop-self-route.md#a-hero-arriving-at-an-action-point-keeps-its-self-route-q712-adr-0750) (ADR-0750),
[a follower's turn toward its leader](research/hero-party-walk.md) (ADR-0751),
[an Imp's alarm walk ends on its last step](research/combat-handler-handovers.md) (ADR-0752),
[an attacked floor's claim clip](research/imp-claim-action-clock.md#an-attacked-floors-claim-starts-its-own-clip-adr-0746-hashsave-472) (ADR-0746),
[a fallen creature's stale body](research/steering-body-retention.md#state-217-leaves-its-body-where-it-fell-adr-0745-hashsave-471) (ADR-0745),
[possessed pick heights](research/possessed-pick-heights.md),
[step-back speed mode](research/step-back-speed-mode.md),
[a speed a handler sets, and the low-health half speed](research/speed-set-turn.md) (ADR-0737, ADR-0756),
[the body's blocker bits a fight sets](research/navigation-and-steering.md#32-three-of-the-eight-do-nothing-and-separation-is-read-to-its-store) (ADR-0756),
[the hand drop's fall and its landing in the creature's own update](research/hand-drop-landing.md) (ADR-0730, ADR-0753),
[the shot finder's tile window and first-match order](research/shots.md#4a-collision-0x004abd60) (ADR-0754),
[the pick-up's turn](research/hand-pickup-turn.md) (ADR-0731),
[the fall heading and the fall's step](research/death-fall-heading.md) (ADR-0732, ADR-0741),
[the shot's probe and the hit's heading](research/shot-probe-and-hit-heading.md) (ADR-0741),
[the drop's heading](research/drop-heading.md) (ADR-0733, ADR-0736) and
[the fight's step 5](research/combat-fight-state.md) (ADR-0734, ADR-0736),
[the stored attackable bit](research/combat-acquisition.md#4-isattackable-is-a-stored-bit-refreshed-at-four-sites-0x0045a8b0) and
[the collapse's action end](research/functions/0048B770.md) (ADR-0757),
[the head count's collapsed creature](research/combat-fear.md) (ADR-0758),
[the flee state's turn toward its target](research/combat-fear.md#4-state-203-fleeing-0x004602e0-in-order) (ADR-0766),
[a guard post's target, read by the slot's facing](research/guard-room.md#1-state-97-to-the-nearest-post) (ADR-0780),
[the steering crowd probe and separation query count a creature by its tile link](research/steering-behaviours.md#54-the-seek) (ADR-0781),
[the needs clocks' constructor stagger and the hunger stamp checked first](research/creature-needs-routine.md#5-implementation-adr-0198) (ADR-0782),
[the hunger retry as a stamp and the hatchery walk's arrival draw](research/eating.md#9-implementation) (ADR-0784),
[state 92 running a turn after the shortcut, choosing the bird by the original's ranking, walking to it and preparing it](research/travel-states.md#8-worked-example-a-hungry-creature-eats--strongly-inferred) (ADR-0785),
[state 93's turn toward the bird and its 0x1000 reach, and the stance route's point on the 16.8 grid](research/stopped-waypoint-precision.md) (ADR-0786),
[the previous-position copy before the state handler](research/creature-update-order.md) (ADR-0787),
[a creature's floor height and the physical pass's fall](research/creature-height.md) (ADR-0788),
[an eaten chicken stays until its own update removes it](research/hatchery-roaming.md#6-how-a-meal-removes-the-bird--strongly-inferred-q796) (ADR-0789),
[a Hatchery's laying capacity counts only its room list](research/hatchery.md#3-where-an-egg-appears) (ADR-0790),
[a floor egg's clip first advances the turn after the lay](research/chicken-birth-clock.md#floor-egg-the-clip-starts-the-turn-after-the-lay-adr-0791) (ADR-0791),
[a creature the eating handlers drop to idle chooses a turn later](research/travel-states.md#8-worked-example-a-hungry-creature-eats--strongly-inferred) (ADR-0792),
[a chicken's reaction passes over a creature above its floor word, and a portal arrival stands at height 0](research/hatchery-roaming.md#4-implementation-boundary) (ADR-0793),
[a guard's walk arriving holds its next handler a turn](research/guard-room.md#8-built-and-not-built) (ADR-0794),
[a struck object's state 18 runs in the object pass, in thing order](research/object-drawing.md#8-furniture-eggs-and-chickens) (ADR-0795),
[the eating states' stores overlay a held Library target point](research/library-states.md#4-the-finder-0x004e1ed0--strongly-inferred-the-variant-seed-strongly-inferred) (ADR-0796),
[a torturer's arrival and state 7's arrival into 92 hand over a turn later, and 92 with no bird sets 91](research/travel-states.md#8-worked-example-a-hungry-creature-eats--strongly-inferred) (ADR-0797),
[a room walk's point passes over a Hatchery pillar corner](research/hatchery-pillar-steering.md#room-walks-adr-0798) (ADR-0798),
[the portal's entrance rules: notice only a weighted type, decay per definition index, a Unique definition as a second candidate](research/portal-creature-action.md#the-portals-type-pick-built-2026-09-28-adr-0272) (ADR-0801),
[a finished research goes to a random Library with a place-holder, and an unreached place-holder cheers the same tick](research/research-tree.md#4-delivering-it-a-book-on-a-library-shelf) (ADR-0803),
[a wall repair's side slot outlives its holder, and its strike runs before the fight scan](research/imp-task-stealing.md#4-reinforce-gold-and-tier-b-q2) (ADR-0811),
[own-land regeneration every turn](research/creature-needs.md) (ADR-0759),
[the fighting arms' candidate walk order](research/combat-target-selection.md) (ADR-0761),
[the melee hold's end, the lifted creature's bit, own-land healing and the collapse booking](research/open-questions.md)
(ADR-0762 to ADR-0765, Q768 to Q771); earlier prefixes are
[possessed melee draw](research/possessed-melee-draw.md) and
[store-gold steering](research/store-gold-steering.md). The continuation is split into
Q772 (past turn 1900: draws match through 2334 at hash 524; turn 2335 draw 1 differs, Q853), Q773 (five creatures with small position
offsets and no draw affected) and Q774 (state words that differ in representation: held
78, a collapsed creature's health countdown, Imp 440's states 10 and 22), besides
Q764 to Q767 and Q733(d). Build only behavior supported
by the binary or a capture; do not tune rules to outcome averages.

The controlled prefix does not establish full-state or Heart-outcome parity.
Restore and adapter gaps remain separate: Q713, Q719, Q723, Q736 and Q738
in [open questions](research/open-questions.md). The broader M6 work is in
[Heart defence parity](research/heart-defence-parity.md) and
[resync replay](research/resync-replay.md); Level 4's remaining Guard-room
exposure is Q710; the [fighter-decision study](research/q710-fighter-decisions.md) records the controls and [remembered Guard memory](research/guard-job-memory.md) is built. The [route/body inventory](research/q710-fighter-decisions.md#routebody-inventory-at-65a0290c) is complete; next prepare a source-bound short continuation guarded for arrival/stop, refill, stuck requery and new routes, with neighbour/action/mesh/world-history limits explicit. No matched result or global closure is established; no new native recording is prescribed.

## Other pending work

These are entry points, not a new priority order.

- Owner's standing priority: Level 7 Imp-casualty comparison, Q582. See
  [combat acquisition](research/combat-acquisition.md) for the matched encounter
  checks still needed.
- M12 keeper acceptance and remaining decisions: [Keeper AI §12.7–12.8](KEEPER_AI.md),
  [native keeper comparisons](research/computer-keeper-observations.md), Q563.
  Enhanced AI remains outside Classic fidelity scope.
- M7 trap/door comparisons: Q732's survey correction and bounded results are in
  [combat spells](research/combat-spells.md#q732-current-trap-comparison-2026-11-14);
  Q737's native closure and scope are in
  [boulder](research/boulder.md#9c-q737-recheck-stun-duration-does-not-establish-the-next-state).
  Owner observations are in [traps](research/traps.md); original-game acceptance remains open.
- Level6a torture-death scene: the shared speech override is built (ADR-0809); Q824 retains native playback/subtitle and live-scene checks
  ([evidence](research/torture-information.md#7-level6as-opening-guard-2026-11-15)). The owner's corpse-placement change remains deferred under
  [ADR-0807](DECISIONS.md#adr-0807---defer-moving-torture-corpses-clear-of-their-devices).
- Possession first-person view: built and unit-tested, never seen running in an editor, no owner play
  acceptance. Rules are in `PossessionSession` (`DK2.Presentation`, ADR-0805); `DungeonRunner.Possession.cs`
  is the Unity glue. Not built: the death view and the entry and exit colour changes (Q745), the two
  special-ability keys, the possession HUD, the third-person view, near clip, lighting distance and
  ambience listener in first person. Next: one editor run, then owner play. Evidence and remake choices:
  [possession client](research/possession-client.md), [plan](plans/2026-11-14-possession-view.md);
  simulation side and owner play questions: [possession arms](research/possession-arms-and-inputs.md).
- Godot adapter (ADR-0805, ADR-0810): not wired are the 3D station scenery, movies, sound and most keys (`ClassicKeyboard` reads the options and full-map keys, the save name and the key being bound on the controls page so far).
  The road to parity and to deleting `unity/` is [the migration plan](plans/2026-11-15-godot-migration.md); its step 1a is
  done (the install loaders and camera paths live in `DK2.Presentation`, both adapters call them). Step 1b is done: twelve
  panels, all in: the in-game bottom panel, the resource strip (`ClassicResourceHud` over `KeeperResourceHud`), the message tabs
  (`ClassicMessageTabs` over `MessageTabsHud`), the hover HUD (`ClassicHoverHud`: wooden bar, status line, tooltip) and the creature
  overview (`ClassicCreatureOverview` over `CreatureOverviewHud`) and the creature query details (`ClassicCreatureQueryPanel` over `CreatureQueryHud`), the battle tab (`ClassicBattleTab` over `BattleTabHud`) the in-game options menu (`ClassicOptionsPanel` over `OptionsHud`; Escape opens it and the level pauses) its save and load pages (`ClassicSaveLoadPanel` over `SaveLoadHud`) and its Game Options pages (`ClassicGameOptionsPanel` over `GameOptionsHud`; settings persist through `GameSettingsFile` to `user://game-settings.ini`) and the full map (`ClassicFullMap` over `FullMapHud`; M opens it, M, Escape or a left or right press closes it; `--open-full-map`) and the minimap (`ClassicMinimap` over `MinimapHud` and `MinimapDiscImage`: disc, frame, markers, north sprite, corners and zoom, drawn from the level-start camera pose; the options corner opens the options menu and a right click on a tile opens the full map) run over a live
  Level1 simulation in the test scene `godot/DK2.Godot/InGameHud.tscn` (`InGameHud`, `ClassicBottomPanel`; the main menu
  stays the default scene). The strip matched a Unity capture by eye at 1920x1080; the open message panel matches Unity's probe capture. Their shared logic (`InstallBottomPanelArt`,
  `BottomPanelHud.OpenAtLevelStart`, `PlayerInputTranslator.ChooseStoredSpecial`, `TickPacer`, `CampaignLevelData`, `HudScaleLayout.Screen` and `BottomOriginPixels`, `BitmapTextPlacement`, `SubtitleLayout`,
  `MessageTextTables`, `PlayerNames`, `MessageTabsView.LocationOncePerTick`, `HudHoverText`,
  `InstallCreatureOverviewArt`, the overview's modifier chord in `CreatureOverviewHud.TryPress`, `CreatureQueryView`, `InstallClassicCreatureNames`, `OptionsHud.HudAction` and `RefreshesSaves`, `ClassicHudControls.Modal`, `ShortcutContext`, `SaveLoadVisible` and `SaveLoadPage`, `SaveLoadHud.HudAction`, `TypedTextBuffer`, `ClassicHudControls.OptionsMenuShown` and `GameOptionsShown`, `OptionsPagesController.CapturePressedKey` (the one key-capture loop, front end and in game, both adapters), `GameOptionsHud.CaptureKey`, `GameBindingInput.Modifiers` and `KeyPressed`, `ClassicKeyNames` (the one table of 99 key names, equal to Unity's `KeyCode.ToString()` names; not DK2's own names, [Q857](research/open-questions.md)), `FullMapImage`, `FullMapHud.Press` and `Action`, `ClassicHudControls.FullMapShown`, `MinimapDiscImage`, `MinimapHud.Press`, `FixedFocus` and `DiscPixels`, `ClassicHudControls.Accepts`, `ClassicHudControlLayout.TryGuiButton`, `ClassicCamera.LevelStartFocus`, `MinimapPalette.FromTopRow`, `InstallMinimapAssets`, and `PngChunks.WithoutColourSpace`) lives in
  `DK2.Presentation`. Hover text from the strip, bottom panel and tabs shows in Godot (Movie Maker 1920x1080 frames: mana, gold, the Rooms tab, a Hatchery slot, Review Objective, a message tab); no Unity hover capture exists to compare. The overview's art and layout match Unity's 1024x768 capture by eye at 1920x1080; Movie Maker frames show the Imp portrait's status text, the Jobs selector's tooltip and a click switching to Jobs mode (its page strips look greyer than Unity's, probably the disabled tint for a single page at that width, unverified). The query details show "Shintaro the Imp. Blood Group: BA. Current Wage: 0. Gold Held: 0." above the bottom panel in a Movie Maker frame (no Unity capture to compare); the scene opens them on the viewer's first creature only with `--query-first-creature`, since Godot has no world picking. `HudSector` (triangle fan) and additive blend both draw in Godot (research-pie sweep 1..99% and
  additive hover and sparkles captured); no Unity hover or sparkle capture exists to compare pixels. Not wired in Godot,
  waiting for plan step 3's camera and world, or step 4's audio (step 2 moved the rules into `src/` and is complete, 2026-11-16): every camera move a press asks for (Godot prints the focus of each: room, door, trap, battle tab, script, minimap glide, full map and overview, through the shared rules of step 2i); for the tabs also sound and speech, the Level6a torture-speech
  sink and the goto camera glide; for the options menu (Movie Maker frames with synthesized Escape and clicks show the root, objective and Restart confirmation pages; a real key or click is unverified; test-scene argument `--open-options [Entry]`, which refuses only Game Options) the session action after Yes on Restart, Return to Menu and Quit (dropped; needs the runner), the other HUD keys, `AllowOptionsKey` (passed true) and the open and close sounds 0x139 and 0x13A; for the Game Options pages (Movie Maker frames with synthesized input show all five pages, master volume 25 after a click on the track, the Sniper row bound to J and `MasterVolume=25`, `Key.1=36,0` in the settings file, matching Unity's HUD captures for layout, labels and key names; real input, hover lighting, slider drag motion, the key-list arrows and thumb, the settings IO fallback and the Unity runtime are unverified) the values nothing acts on yet: sound volumes, switches, quality, headphones and QSound (no sound player), gamma, shadows, water and environment mapping (no 3D renderer), scroll speed, mouse sensitivity and invert (no camera or possession); the speed, bindings and HUD scale do apply, and a Movie Maker capture passes `--settings <temp file>` because the scene reads the saved settings; for the save and load pages (Movie Maker frames with synthesized input and a scratch folder, `--saves <dir>`, show the slot list, a slot's name filled in, typing and Back; a real keyboard and mouse and the Unity runtime are unverified) nothing but the save folder, which differs on purpose (Godot lists `user://Saves`, not Unity's `persistentDataPath/Saves`; the submit works since step 2f); for the hover HUD world hover (things and tiles, needs the world and a camera) and the
  hand cursor's resolution (`HoverCursorResolver` computes it; Godot still uses the small pointing finger's reach, valid only while nothing is held, where it can take `HoverFeedback.TooltipReach`); for the battle tab (`ClassicBattleTab`, `BattleTabHud`; the blades, the cancel and the alarm, hover text from
  menu 43) only the cancel's left click is wired, the blades' zoom, the glides and the fly-in need the camera (a Movie Maker capture with
  `--stage-battle` shows the blades cycling through four frames; hover and press with a real cursor, and the cancel and alarm in Godot, are unverified); for the creature overview a column press now posts its pickup through `PickupFlow` (no hand cursor is drawn), and the focus it asks for needs the camera; for the query details the query tool's world click (no picking yet; its right-click cancel works), and the camera follow and turn. Secret levels and My Pet Dungeon objective panels show their briefing lines in both adapters (`MessageTextTables` takes the file from `BriefingTextTable.FileName`); whether the original shows them in play is open ([Q854](research/open-questions.md)). Full map (Movie Maker on Level1, fog on, matches Unity's capture byte for byte in four map colours and the "Player" legend; real input, room hover, a legend with several keepers and the Unity runtime are unverified): not wired are the camera move on a left press (printed, step 2), the camera preview (needs the 3D world). Minimap (Movie Maker on Level1, fog on, matches Unity's capture by eye for the frame, corners, north sprite, rotation, backdrop shading and rim; the zoom, the options corner and a right click opening the full map were shown with synthesized clicks; real input, corner hover with a real cursor, pixel equality against the GPU, HUD scales other than 100% and the Unity runtime are unverified): not wired are the camera footprint (step 3), and the left-click camera move (printed; steps 2-3); the minimap has no heading rule to move, since the HUD and so the minimap are hidden for the whole opening path. The query and sell corners set the tool and post their `GuiButtonCommand` as Unity does, and light up; a lit corner goes out on a panel choice, a stored special or a right-click cancel (step 2c); the gold withdraw keeps it selected, as in Unity. Godot's PNG decoder applies gAMA and DKII.EXE does not ([0x005B6030](research/functions/005B6030.md)), so the map palette and the front-end art decode through `PngChunks.WithoutColourSpace`; the front end now draws brighter than before, whether as Unity's does is [Q859](research/open-questions.md). Speech subtitles are not drawn in Godot (they come with speech, step 2); `BitmapTextPlacement` and `SubtitleLayout` hold their rules for it. `ClassicHudScaling` stays Unity-only and `MinimapFrameVisual` needs no port. Step 1c is done (allocation fixes in the shared hover, music and minimap capture code; the remaining per-tick allocations are in `DK2.Simulation`, see the plan), so step 1 is complete. Step 2 is in progress: the audit and step 2a are done (`HudPointerMap`, `GameFolders`, `CampaignSlot.ForLevel`, `LevelSubtitles`, `PointerGestures.CancelAll`, `TileHeights`, `TickPacer`'s frame clamp and ticks per frame, `LaunchArguments`, all in `DK2.Presentation` and called by Unity too; Godot uses the pointer map for its HUD hover text, with the query details gated off while a menu is modal as in Unity, the folders for its `user://` settings and saves, the launch arguments (it parses `--dk2Install` and ignores it), the played slot for world hover and the cancel on focus-out; Godot's focus-out cancel and the modal gate are unrun with a real window and pointer, and Unity was not run in Play mode). Step 2b moved the HUD tool state (`HudToolState`) and the HUD action routing (`HudActionRouter`) into `DK2.Presentation`: Unity calls them, and Godot sets the tool from the minimap's query and sell corners, a panel choice and a stored special (with Unity's Options gate); the six tool-reset sites differ and moved unchanged ([audit](plans/2026-11-16-godot-step2-audit.md)); Unity in Play mode and Godot corner clicks with a real mouse are unrun. Step 2c moved the HUD and world presses into `DK2.Presentation` (`PickupFlow`, `HudPressRouter` in `QueueHudInput`'s order, `WorldClickCommands`, the room, door and trap navigation entries, `BattleTabView.Click`) and Unity calls them; Godot reads presses from `Window.WindowInput` into the same router, so the overview pickup, the gold withdraw, the right-click cancels, the full map, options, minimap and battle tab press as Unity's do. Unwired in Godot: every camera move, session actions, picking (so world left clicks do nothing), the hand's own world press and possession, and the hand cursor. Unity in Play mode, real-mouse Godot presses and Godot 4.8's `WindowInput`-before-`_Input` order (inferred) are unverified. Step 2d moved world hover to the hand cursor into `DK2.Presentation` (`HoverCursorResolver`: the cursor, placement cage and icons, hovered creature, hover sound and tint; `FogHit` and the hand-light point rule); Unity's `CaptureWorldHover` keeps the picking and drawing and calls it once, nothing journalled changed, and Godot is unchanged because it draws no hand cursor; the cursor, cage, tint and cast hand in Unity Play mode are unverified, and `PointerOverHud` is now read once per frame (a possible one-frame difference in the placement overlay's offset on the first or a resize frame). Step 2e moved the campaign profile store, the hi-score store and the front-end glue into `DK2.Presentation` (`CampaignProfileStore`, `HiScoreStore`, `FrontEndHost`, `FrontEndStartupGate`); Unity calls them through `_profiles` with every read and write unchanged, and Godot's front end now loads a real profile and hi-scores (`UserFolders`; the hi-score page shows real rows, the movie gallery shows which movies are unlocked without playback, the campaign entries fly to the map page; no land picking, briefing, launch or name typing without a station scene). A corrupt profile or registry error gives a warning and an unsaved in-memory profile, an unreadable hi-score file empty rows ([plan](plans/2026-11-15-godot-migration.md) for the open points, including the two owner questions on that fallback and the one-time registry import). Unity in Play mode and the Godot front-end pages by eye are unverified. Step 2f moved save and load into `DK2.Presentation` (`SessionContext`, `SessionSaver`, `SessionLoader`, `GameSessionRequest.Resume`); Unity calls them, with one difference: a save whose level map is missing is now refused. Godot's Save and Load pages now work: Save writes `<saves>/<name>.dk2g`, Load rebuilds the simulation and reloads the scene to continue it, and with a corrupt profile a save still works (kept in memory, no file write) while a load is refused and writes nothing. `SessionLoader` restores with `WorldSnapshot.LoadGame` (ADR-0241), so a loaded game re-chains every room row-major as DK2 does, in both adapters (a dragged Prison's door moves from the east column to the west); `SlabOrder` is hashed, so a loaded game can hash differently from the saved run once a room tile has a non-zero order. Headless Level1 gave the same state hash at turn 400 (`D7290428D81A902F`) whether a run never saved, saved at turn 200 and went on, or loaded that save (observed, map-placed rooms have order 0; no test pins it); the Unity saves under `build/` are snapshot format 523 against the current 524, so Godot refuses them. Unity in Play mode and a save written by a current Unity build loaded in Godot are unrun. Step 2g moved the level end, the won-level cutscene sequence, the tablet projection and two scripted-view HUD rules into `DK2.Presentation` (`CampaignResultFlow`, `CampaignCutsceneRunner`, `TabletDrawList`, `ScriptedViewDirector.PlayedHud` and `PanelFlash`); Unity calls them. Two behaviour changes in both adapters: Escape after a finished win screen posts `ContinueAfterResultCommand` (134) then `RevealMapCommand` (75), the order of 0x004055E3 (the state hash is equal in either order, `FinaleTests.TheContinueAndTheRevealCommuteWithinTheirTurn`; no journal fixture holds these commands), and a missing win screen counts as done, so Escape continues at once. Godot runs `ScriptedViewDirector` (it observes before turn 1 and writes nothing; the Level1 turn-400 hash stays `D7290428D81A902F`), hides the HUD during the opening path, and runs `CampaignResultFlow` for a level started `--from-map`: Space switches to `Main.tscn` at the return page and the level end writes the profile. Not wired in Godot: the path camera, letterbox, subtitles, the win screen and tablet, and the post-win cutscene (needs media), so a won map goes straight to the results page. Space switches the session directly; the old runner's comment said it posted action 1, which is recorded at [0x00510F40](research/functions/00510F40.md) and not decided. Unity in Play mode and `CompletionFlowProbe` are unrun. Step 2h moved the frame order and the command stream into `DK2.Presentation` (`FrameDriver`, `WorldPointerInput`, `FrameJournalSession`; the worker-tick protocol stays in Unity): Unity calls them, and its `LoadLevel` now reads `request.Resume` (`_pendingResume*` is gone). Godot's `InGameHud` runs the same frame order, so deferred drops post, Escape or Space skips a playing camera path and posts `SkipCameraPathCommand` (0x00405362; the options key is held that frame; the script's glide prints "not wired"), and releases are read but, without picking, post nothing. Godot's journal is opt-in through `--input-journal <dir>` only (it does not read Unity's `DK2_INPUT_JOURNAL`); the `keys` header names the key table ("UnityEngine.KeyCode" or "Godot.Key"), the format is unchanged and Unity journals still read. Known gap: the binary gates Escape on 0x0075A661 and Space on player +0x76 bit 0x400000, and `FrameDriver` mirrors only the path branch with the bare keys, as Unity did (ADR-0496). The evidence and the verification are in the [plan](plans/2026-11-15-godot-migration.md), step 2h; the Level1 turn-400 hash is unchanged (`D7290428D81A902F`). Unity in Play mode and Godot with real input are unrun. Step 2i moved the pure camera rules into `DK2.Presentation` (`CameraGlide` and `CameraFocus`, `ClassicCameraInput`, `ClassicCameraSetup`, `TileTopPlane`, `GameSessionStarter`, `ClassicCreatureQueryRules.Follows` and `TurnToFollowHeading`, `CameraPathPose.FrontEndVerticalFieldOfView`): Unity calls them (its glide is one `_glide`), and Godot's "not wired" prints show targets computed through them; no camera was added. Off-map goto targets clamp where DK2 drops them ([Q866](research/open-questions.md)); the edge-scroll pointer sample is y-up and the top-edge row is unmeasured ([Q867](research/open-questions.md)). The headless Level1 turn-400 hash is unchanged (`D7290428D81A902F`); Unity in Play mode is unrun. Step 2j's first half moved the creature and object scene mapping into `src/`: `PoseInterpolator`, `MeshOrientation` (one mesh-turn chain instead of four, returning a `RenderEuler`) and `AnimationClipRequest` in `DK2.Presentation`, and `CreatureAnimationPlayer` behind `ICreatureAnimationSurface` in `DK2.Art` (deferred replacement, catch-up, beat, footstep normalisation, control points). Unity calls them, and its new `RenderEulerRotation` composes the turns, which no test covers (checked by eye against the four old sites); output is unchanged and the simulation hashes are unchanged. Creatures still draw their turns uninterpolated while objects interpolate theirs ([Q868](research/open-questions.md)), and a footstep window spanning several turns is normalised ([Q869](research/open-questions.md)). Part A of the second half moved the light publishing, the lattice dirty tracking and the hover picking into `DK2.Presentation`: `ClassicLightPublisher` (the visible tiles, found again only when the pose, viewport or distance changes, the second camera at view distance 10, and the diff handed to an `ITileLightSink`, which `ClassicTileLightBuffer` implements), `LatticeDirtyTracker` (`TerrainLattice.Invalidate` takes its reach), `WorldHoverPicker` (the terrain cast through `TileTopPlane.TryHit`, the rendered winner, the 3x3 sweep; Unity's `Vector3` arithmetic is written out term by term so the pick, which reaches commands through hover, is bit-identical) and `HoverOverlayPlan`, with `TileView.LitAsExplored` for the tile fill rule. `DungeonScene` keeps only the measuring (frustum, UV2 sphere and bounds, mesh reads, camera to pose). The ADR-0458 threading contract holds. Godot is not wired for any of it (picking is 3e, lighting 3i); Unity in Play mode is unrun (the hover tint, the pick, the lighting). Step 2 is complete. Step 3a is done (2026-11-16): `InGameHud` draws Level1's terrain under the HUD from the level-start camera (`WorldScene`, over `TerrainTileBuilder`, `TerrainMaterialKey`, `ClassicMaterialRecipe` and `RightHandedFrame` in `DK2.Presentation`; the Z flip leaves triangle indices unchanged). In 3a the sight fog and tag overlay draw nothing, there is no lighting, and the chunks are built once; a 1920x1080 Movie Maker capture shows the Heart room from DK2's start camera, not mirrored, and the Level1 turn-400 hash is unchanged (`D7290428D81A902F`). Step 3b is done (2026-11-16): Godot's camera moves through `ClassicCameraInput` and `CameraGlide` (every glide, the query follow and turn, the full map's and the overview's focus jumps, `--glide-to x,y`), and the minimap draws from the live camera with its footprint (`MinimapCameraView`, `HudTriangles`, `HudDrawKind.Triangles`, drawn by both `HudCanvas`es); a Movie Maker capture with `--glide-to 30,12` shows the view and the footprint move, and live keys, edges and wheel ran only in unit tests. Next is 3c, terrain that changes ([audit](plans/2026-11-16-godot-step3-audit.md), [plan](plans/2026-11-15-godot-migration.md)).
- World-sound coverage and pending hearing checks: [world-sounds plan](plans/2026-10-31-world-sounds.md).
  Check the current code and Git history before relying on its branch-status prose.
- Autonomous playtesting: [Stages A–C](plans/2026-10-27-autonomous-playtesting.md)
  and [Stage D](plans/2026-10-27-stage-d-unity-input.md). Input replay/extraction
  and rendered-HUD automation remain follow-up work; use the completion acceptance
  record above for the integrated walkthrough and its journals.
  Approved design: [agentic campaign playtesting](plans/2026-11-07-agentic-campaign-playtesting-design.md).
  The [stages 1-2 plan](plans/2026-11-07-campaign-player-stages1-2.md) is built and merged
  to master. Task 1, the detached player contract
  (`DK2.Simulation.Players`, `DK2.AI.Players`), task 2, the visibility-safe observation
  (`PlayerObservationBuilder` in `DK2.Presentation`), and task 3, bounded action admission
  (`PlayerSession` in `DK2.Harness`, `PlayerActionAdapter` in `DK2.Presentation`), task 4,
  the tick-loop integration (`RunOptions.PlayerRun`, `PlayerRunCoordinator`, replay of
  `player.run.schema=1` journals, `PlayerRunManifest`), and task 5, the observation-only
  `CampaignPlayer` strategy (`DK2.AI.Players`), and task 6, `run --campaign-player` with its
  limits, evidence folder and verdicts (`PlayerRunCommand`, `PlayerRunReport`; see
  [`DK2.Harness`](../src/DK2.Harness/README.md)), are built. Task 7, the Level 1 acceptance
  (`scenarios/campaign-player/`, `CampaignPlayerLevel1Tests`; run with `run --campaign-player 3`),
  is built on top of the Portal Gem fix (ADR-0725, hash 456, merged into this branch from fix/reaper-gem-pickup): from the unmodified
  start the player won seeds 1, 2 and 3 (ticks 2360, 2244, 2216), each with the finale reaching the
  gem pickup (ticks 2490, 2346, 2316), and every journal replayed all hashes without the controller.
  The study is `F:/DK2Evidence/studies/campaign-player-l01-acceptance--2026-11-09--002`.
  This is Level 1 only, headless, not a whole-game or rendered
  acceptance. Build the receipt with `tools/build_receipt.py`; a run given `--player-receipt <dir>`
  can claim acceptance under the conditions in the [harness README](../src/DK2.Harness/README.md),
  and prints `not claimed: <reasons>` otherwise.
  Next (owner direction, 2026-11-14): stage 3 of the design, a capable bot that needs no model
  and plays every campaign level as it becomes playable
  ([plan](plans/2026-11-14-campaign-bot-stage3.md): strategy, per-level playbooks, a self-play tuner).
  Tasks 0a-3b are built: `PlayerStrategy` drives the routines, and the player develops (grows
  outgrown rooms, builds the other offered rooms, digs within `BreachMode`; carrying creatures to
  rooms is `UseRooms`, empty by default) and reads keeper knowledge: `knowledge/rooms.yaml`
  (generated by `DK2.Harness keeper-knowledge`; a test compares it with a fresh run) and the acting
  rules of `knowledge/rules.yaml` size its rooms and set a Library's shelves from the level's
  research (`--player-knowledge <dir|none>`). A level's playbook (`knowledge/playbooks/<level>.yaml`,
  `--playbook <file>`; format and loader in the plan's Task 3b) holds what the level offers
  (generated by `DK2.Harness playbook-available`, checked by a test), the rooms and rules to plan,
  bounded integer tunables, notes, events and caps; only `level1.yaml`, with its available section
  alone, exists and plays as without one. The bot wins Levels 1-3 on accept seeds 11-20 (10/10 each,
  hash 519); Level 4 won 2 of 3 diagnosis seeds but stays out of the driver choice while Q710 is
  open, Levels 5-10 are not won ([ladder](research/campaign-bot-ladder.md) sections 7-8). Open
  from Tasks 3a and 3b: Q813-Q818. Task 4, the observation and action additions, is built one row per
  branch: each own creature's level, petals and mood, every offered spell with `CastSpell`, then door and trap
  offers with `PlaceDoor` and `PlaceTrap` (Q819, Q820, Q825 and Q826 open, Q821-Q823
  answered by the owner). Its observation and action rows are all built except Possession; the routines that
  use them: the Combat Pit routine is built (`strategy.pit-fighters`, off by default; on Level 15a it never acts: the peaceful
  routines now stop only for a threat within `strategy.enemy-near-tiles` (8) of home or the room, but the bot's own
  Black Knights are prisoners, a level objective not built, Q843; Q827, Q828, Q831, Q836 and Q844-Q846 open) and so is Imp scouting (`strategy.scouts`, off by default; Level 6a
  seeds 1-3 show no gain yet; Q834, Q835, Q838 open). Imp creation, R8, is built too
  (`strategy.create-imps`, off by default; Level 6a seeds 1-3 show no gain; Q842 open;
  [plan](plans/2026-11-14-campaign-bot-stage3.md)). The bot defends its whole dungeon (an enemy within 8 tiles of own ground, the one nearest the Heart
  first, Q847) and drops fighters in a formation by fighting style (observation schema 6; Q848-Q850 answered by the owner;
  [defence plan](plans/2026-11-15-campaign-bot-defence.md), D1-D4 built). It also places traps on the corridors the
  attacks take (`strategy.max-traps`, 8; the trap for a site follows its kind, Q851; Q852 open). Next: D6, attack spells
  that never hit own creatures or walls, then D5, measuring the lost levels 6a, 7, 11a and 15a (traps alone turn none).
  Agent supervision (now stage 4) and skirmish playtesting (stage 8) come after it.
- Modern Imp asset proof of concept: [asset pipeline §10](ASSET_PIPELINE.md)
  and [image-to-3D trial](plans/2026-10-07-imp-image-to-3d-trial.md).
  Read [art direction](ART_DIRECTION.md) before asset work.
- Rendering cost candidates, none measured yet. Measure each with `game-unity-probe -Benchmark`
  ([README](../tools/game-unity-probe/README.md)) before changing anything:
  - Overdraw of large additive and transparent effects in a busy battle (`ClassicEffect.shader`,
    `ClassicLightning`, `ClassicTorchFlames`, `ClassicHoverOutline`): trim quads to the visible texels
    where the frame is fill-bound. Needs a busy scene, not Level1's opening.
  - Off-screen and menu work only when something changed: `MinimapGpuMap` command buffers,
    Unity's `ClassicFullMap` blit and preview target, `HudCanvas`'s font-atlas blit for the front-end menu's replacement font.
  - `ClassicCreatureShadows`: skip the coverage redraw when no caster moved. It is already cheap
    (R8 targets, frustum-culled casters), so this is the lowest priority.

## Workspace and evidence

- Check `git status` and `git worktree list` before editing. Another session may
  own a checkout; do not switch branches in the shared main checkout. Retained
  worktrees and their branch names must be discovered, not copied from this file.
- The main checkout has an owner-edited `unity/DK2.Unity/Assets/DK2/Scenes/Dungeon.unity`.
  Preserve it. Use serial Unity checks; isolated probes must not save or restart
  the owner's editor. Editor workflow: [Unity README](../unity/README.md).
- Irreplaceable native captures, extracts and replay rigs belong in
  `F:/DK2Evidence/`. Follow AGENTS.md's add-only, no-links and cleanup-approval
  rules. Do not rely on legacy `reverse-engineering/dk2runs/` paths;
  the 2026-10-18 cleanup lost captures and rigs. Follow each research note's
  external evidence path and verify availability before planning a replay.
- Which original-game tool serves which job: [tools/oracle/README.md](../tools/oracle/README.md). Exact full-memory
  recordings use the [complete recorder](../tools/oracle/native-capture.md).
  Research setup and tools: [reverse-engineering README](../reverse-engineering/README.md)
  and [research queue](RESEARCH-QUEUE.md). Read function records before binary queries.
- The reference install is `E:/GogGames/Dungeon Keeper 2`; `DK2_DIR` overrides
  discovery. Install-dependent checks skip without data unless
  `DK2_REQUIRE_INSTALL=1`. See [build/test guidance](../README.md#build-and-test).

## Where to work

- Project boundaries and simulation order: [architecture](ARCHITECTURE.md).
  Rules and mutable state belong in `src/DK2.Simulation`; `src/DK2.AI` observes
  and emits commands; `src/DK2.Harness` runs headless scenarios. Unity integrates
  and presents them.
- Tool and scenario entry points: [tool commands](../src/DK2.Tools/README.md)
  and [scenarios](../scenarios/README.md).
- Follow AGENTS.md for verification, reviewer/documenter passes and documentation
  gates. Read [traps](TRAPS.md) before long work. Evidence belongs in research
  notes, decisions in ADRs, function findings in their KB records, and remaining
  unknowns in [open questions](research/open-questions.md).

## Run and verify

From the repository root, the verification gate list is:

```sh
dotnet build DK2.slnx -c Release
dotnet test DK2.slnx -c Release --no-build
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
dotnet run --project src/DK2.Tools -c Release --no-build -- status --check
```

Build/test prerequisites and install-dependent skips: [root README](../README.md#build-and-test).
The Unity compile project is outside the solution; run `unity/sync-assemblies.ps1`
after source changes before editor checks. See [Unity workflow](../unity/README.md).
Check the changed diff with `git diff --check`.

A simulation state change requires matching `GameSimulation.HashVersion` and
`WorldSnapshot.FormatVersion`, a new determinism pin, and a
[hash-versions row](research/hash-versions.md); an observation-format change
requires a schema bump.
