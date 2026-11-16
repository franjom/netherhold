# Godot migration step 2: the group-2 audit

**Status:** audit, 2026-11-16, for [the migration plan](2026-11-15-godot-migration.md) step 2. It was a read-only
pass over the Unity sources at HEAD 5d8ba62a. Nothing was built or run. Line numbers are as of that HEAD and go
stale as moves land. Each row is a candidate for one move into `src/` (ADR-0805). Rows are grouped by the
plan's move order (2a-2k).

Each Unity region was classified in one of four ways:

- **B (behaviour):** DK2 behaviour, game flow or a rule. It moves.
- **P (plumbing):** engine plumbing. It stays per engine and is rewritten in step 3.
- **S (shared):** already a thin call into `src/`.
- **C (camera):** needs the 3D camera or world rendering, so it belongs to step 3. Any pure rule inside it may move now.

## Where the DK2 logic still is

| Unity area | Mostly | The B content |
|---|---|---|
| `DungeonRunner.cs` (2,071) | P, S | The `Update` frame order and the tick hand-off; `QueueInput`; world hover to cursor; tool state; camera input, glide and set-up |
| `DungeonRunner.Hud.cs` (589) | B | Tool state and its reset sites; `ApplyHudAction`; `QueueHudInput`'s order; the pickup and focus flow (lines 408-429, not 431-451); world-click commands; navigation entries; the battle fly-in |
| `.Saves`, `.LevelEnd`, `.Finale`, `.FrontEnd`, `.Cutscene` | B | Building and loading a `SavedSession`; the campaign result state machine; the cutscene phases; the profile and hi-score stores; file locations |
| `.InputJournal` | S, P | Policy only: the opt-in variable, the folder name, the header and the failure handling. The format and the writer are already in `src/` |
| `.Possession`, `.Startup`, `.Settings`, `.Preview` | S, P | One ordering rule: `QueuePossessionPose` inserts at index 0 |
| `.Sound` | B | The level sound driver. It is blocked on an audio abstraction (step 4) |
| `DungeonScene.cs` (1,334) | P | About 450 lines of B: pose interpolation, mesh orientation (written four times, plus heading-only variants), the clip request, the light publisher, the hover picker, the heart pulse, the sparkle gate, the head table and the tile heights |
| `ClassicCreatureAnimation` | B | The animation player: deferred replacement, catch-up, beats, footstep marks and control points |
| `ClassicConstructionEffects` | B | The effects draw list: defaults, stream order, sprite colour, frame pick, quad geometry, blend |
| `ClassicStatusFlowers`, `ClassicFlowerDollar` | B (small) | Layer order and the dollar clock |
| `ClassicKeyBindings` | P | The KeyCode table. `Name(GameKeyBinding)` has no callers |
| `ClassicSettingsRendering` | P | Only `DefaultGamma` 30 and the exponent `30/Gamma` are B |

## Move order

Each row lists the Unity source, the proposed `src/` home, and its size and risk.

- **2a, small and independent.** These remove Godot copies. Done 2026-11-16 (plan step 2a).
  - The pointer-over-HUD list (`DungeonRunner.cs:1699-1709`, copied at `InGameHud.cs:494-502`) becomes a `HudPointerMap`. The copies differ: Unity's query-panel term has `&& !HudModal` and Godot's has none, so the move takes Unity's.
  - The file locations (profile, settings, saves and hi-scores, built in four places) become `GameFolders`.
  - The played slot by level name (`:587-592`) becomes `CampaignSlot.ForLevel`.
  - The subtitle table open (`:1500-1514`) becomes `LevelSubtitles.Open`.
  - Focus-loss cancels (`:1993-2013`) become `PointerGestures.CancelAll`.
  - The DK2 tile heights (`DungeonScene.cs:30-43`) become `TileHeights`.
  - The 1/3 s frame clamp and two ticks per frame (`:85`, copied in `InGameHud.cs:32,34`) move into `TickPacer`.
  - The launch arguments (`:244-275`) become `LaunchArguments`.
- **2b, tool state.** Done 2026-11-16 (plan step 2b). `HudToolState` covers the tool, the panel choice, the query tool, the selected corner and the selected room. It replaces the six reset sites in `Hud.cs`, which reset different subsets (190, 211-212, 344-348, 440-441, 472-473, 482-483), and absorbs the tool mapping and `SelectKnownRoom` (`DungeonRunner.cs:1355-1374`). With it come `ApplyHudAction` as `HudActionRouter` (`Hud.cs:315-352`), the placement cursor per tool, `CageTool` and `CastSpellId` (`:1926-1991`). This is journalled, because the tool feeds `PlayerIntent` and `FrameInputRecord.ActiveTool`. Godot gains query and sell, which post a `GuiButtonCommand`.
- **2c, HUD and world presses.** Done 2026-11-16 (plan step 2c). These are journalled.
  - The pickup flow was copied twice: in the overview press (`Hud.cs:408-429`) and in the world click (`DungeonRunner.cs:1297-1316`). It became `PickupFlow`, with one difference kept: the overview press swallows a refused pickup and calls `QueuePickupPreview`, while the world loop continues past a refusal and never calls the preview. The local `TryQueue` decides whether a pickup is posted (dead, dying, already pending, not pickable, hand full), and `TryQueueDrop` defers a drop onto a pending pickup until `DrainReadyDrops` posts it, so Godot shares the filter and queues the deferral, but its per-frame `DrainReadyDrops` waits for 2h.
  - `QueuePanelWorldAction` (`Hud.cs:449-510`) became `WorldClickCommands`. It covers the query click, the right-click cancel, doors, traps, spells and the Workshop gate.
  - `QueueHudInput`'s order (`Hud.cs:372-447`) became `HudPressRouter`. The order is full map, options, overview, minimap, then gold withdraw and the heart right click.
  - The navigation entries (`Hud.cs:215-279`) became `RoomNavigation.Entries`, `ThingNavigation.DoorEntries` and `TrapEntries`, the last two newest first.
- **2d, world hover to cursor.** Done 2026-11-16 (plan step 2d). `HoverCursorResolver` (`DungeonRunner.cs:1771-1921`) covers hover sound, pickup eligibility, dig, the spell-armed cursor, held icons, the No-Go icon over the HUD and the spell refusal. It also takes the fog gate and the hand-light point (`:1715-1755`). It is presentation only.
- **2e, profile and front-end stores.** Done 2026-11-16 (plan step 2e). `CampaignProfileStore` covers load-or-create, `EnsureOpenMap`, `RepairAfterLevel` and the secret-five moon-phase unlock (`FrontEnd.cs:35-45`). `HiScoreStore` falls back from its own file to the install's file to empty. `FrontEndHost` covers the typed-name keys, the pick-slot table and elapsed to turn.
- **2f, save and load.** Done 2026-11-16 (plan step 2f). `SessionSaver` (`Saves.cs:43-58`) builds `SavedSessionHeader`, applies the campaign carry and calls `ToBytes`, then `SavedSessionFile.Save`. `SessionLoader` (`Saves.cs:60-90`) reuses `CampaignLevelData.Load` and `CampaignSimulationFactory.Triggers`; Unity copies them today. Both need a `SessionContext` (level, mode, return page, played slot, carry) and a resume payload on `GameSessionRequest`. Each adapter joins its tick worker first. A save embeds the profile, so it is portable between adapters. The live profile and the settings follow `GameFolders` (2a). The load uses `WorldSnapshot.LoadGame`, as ADR-0241 item 2 says (changed 2026-11-16 after 2f; Unity had called `FromBytes`).
- **2g, level end and cutscenes.** Done 2026-11-16 (plan step 2g). `CampaignResultFlow` (`LevelEnd.cs:25-114`, `Finale.cs:207-230`) covers the carry, the statistics once per player, the result once, the path gate, the win keys and Space to the front end. It posts `RevealMapCommand` and `ContinueAfterResultCommand`. The comment at `LevelEnd.cs:60-61` says Space posts action 1, but the code switches the session directly, and the move kept that (open, see the plan). `CampaignCutsceneRunner` (`Cutscene.cs:26-76`) takes them in turn: title, speech, pause, movies. The tablet projection (`Finale.cs:293-343`, also used by the battle fly-in) becomes a draw list. The scripted view's HUD hiding went here too (`ScriptedViewDirector.PlayedHud`); the minimap heading during the opening path needed no rule, since the HUD, so the minimap, is hidden for the whole path.
- **2h, the frame order and the command stream.** This is the high-risk part, so it is done last and in one piece. `FrameDriver` (`DungeonRunner.cs:925-1005`) covers the tick gate, the accrual, `QueuePossessionPose` at index 0, the first-input hand-off and its clear, the local pickup `Advance` and `DrainReadyDrops`, the tag acknowledgements and the scripted-view skip (`SkipCameraPathCommand`, `allowOptionsKey`). It also absorbs `QueueInput`'s gate sequence (`:1249-1315`) as `WorldPointerInput`. `FrameJournalSession` takes the journal policy. Before the move, write a journal-equivalence test: the same recorded input gives the same command stream before and after. The worker-tick protocol (`:1118-1228`) stays optional, because Godot ticks inline.
- **2i, camera rules.** These are pure. Godot can use them once step 3 gives it a camera. They are the glide (`:1463-1480`), the edge-scroll, wheel and key mapping with the 5 px margin (`:1377-1406`), the camera set-up (`:542-558`), the focus clamp, the query follow and turn (`Hud.cs:459-467, 560-568`, which needs `ClassicCamera.Yaw` exposed), the front-end 4:3 vertical field of view (`FrontEnd.cs:218`) and the session build branch (`:319-347`). The full map's camera preview waits for step 3's 3D world.
- **2j, scene mapping for step 3.**
  - `PoseInterpolator` and `MeshOrientation`. DK2 composes the rotation in the order of 0x00580480; the half turn on the heading (`MeshYaw`) and the negated pitch are our frame mapping (`RenderMesh.ToRenderFrame`). The full chain is written four times with differing interpolation (`DungeonScene.cs:721-727, 1257-1259, 1290-1292`, `ClassicConstructionEffects.cs:259`), plus three heading-only variants (`DungeonScene.cs:869, 990`, `ClassicTorchGeometry.cs:46`).
  - `AnimationClipRequest`, whose start-time rule is copied twice.
  - `CreatureAnimationPlayer`, which also takes `ClassicMeshes.ConfigureAnimation`'s segment and timing rules.
  - `ClassicLightPublisher` and `LatticeDirtyTracker`.
  - `WorldHoverPicker` and `HoverOverlayPlan`.
  - The effects draw list: the `ClassicConstructionEffects` B rows.
  - The heart pulse, the gem sparkle gate, the creature head table, `ObjectView.TexturePhase`/`ScaleY`, the status flower layers and the dollar clock.
- **2k, deferred.** The level sound driver waits for step 4's audio abstraction. The `DefaultGamma` exponent goes with lighting. `ClassicKeyBindings.Name` is deleted.

## Open points the moves must not decide silently

- **Creature headings.** Creatures do not interpolate their heading between ticks (`DungeonScene.cs:672-677`, `1257-1259`), but objects do (`:721-727`). Is it a rule or an accident?
- **The dollar clock.** It advances once per dollar drawn and resets on every non-dollar flower. So its speed depends on how many dollars are visible and on the frame rate (`ClassicFlowerDollar.cs:55`, cited to 0x005961A0).
- **Tool resets.** The six tool-reset sites reset different subsets. The gold withdraw skips `CancelTool`, the stored special skips `SelectTool`, the right-click query cancel calls neither, and the right-click tool cancel skips `CancelTool` and keeps `_queryTool`. 2b moved them unchanged, as named transitions of `HudToolState` (the gold withdraw also keeps the selected corner, so a lit corner outlives it). A fix is a separate change.
- **Journal portability.** The journal header names Unity `KeyCode` ints, so a Godot journal needs its own key table.
