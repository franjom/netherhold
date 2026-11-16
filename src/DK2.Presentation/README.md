# DK2.Presentation

Everything between the simulation and a renderer that does not need an engine.
`WorldView` projects a `WorldState` for one keeper; `ScenePresenter.Present` diffs it
against what was last shown and drives an `IRenderTarget` (create, update, remove,
`Flush`) with assets from an `IAssetProvider`. `TextRenderer` is the same job against
a character grid. `PlayerInputTranslator` turns a `PlayerIntent` into commands,
`TilePicker` picks a tile from a ray (over the `TileHeights` planes), and `TorchLayout` and `RoomWalls` compute
placement. Small rules both engine adapters share: `HudPointerMap` (whether the pointer is over the HUD), `GameFolders`
(profile, settings, saves and hi-score locations), `LaunchArguments`, `LevelSubtitles`, `PointerGestures.CancelAll`
(focus loss), `TickPacer` (tick debt, the frame clamp, ticks per frame), `HudToolState` (the tool, the panel
choice, the query tool, the selected corner) and `HudActionRouter` (a HUD action's gate, cancels and corners). The HUD's and the world's presses:
`HudPressRouter` (`HudPress`, `IHudPressHost`: full map, options, overview, minimap, gold withdraw, heart),
`WorldClickCommands` (`IWorldClickHost`: the query click, cancels, door, trap and spell placement), `PickupFlow`
(the pickup sequence), `BattleTabView.Click` and the navigation entries (`RoomNavigation.Entries`,
`ThingNavigation.DoorEntries` and `TrapEntries`). World hover to the hand cursor: `HoverCursorResolver`
(`Resolve`: cursor, placement, hovered creature, hover sound and tint; `FogHit`, `TryHandLightPoint`).
The front end's stores and glue: `CampaignProfileStore` (`Load`, `Save`, `Track`, `Adopt`, `SecretFiveAvailable`),
`HiScoreStore` (its own file, then the install's, then empty), `FrontEndHost` (`OpenCampaign`, `TypeName`,
`FillPickSlots`, `CreatureFrame`, `MovieGallery`) and `FrontEndStartupGate`.
Save and load: `SessionContext` (level, mode, return page, played slot, carry; `ApplyCarry`, `PlaySlotOf`), `SessionSaver` (`Save`),
`SessionLoader` (`Load`, to a `GameSessionRequest` carrying a `SessionResume`; it restores with `WorldSnapshot.LoadGame`, so rooms re-chain row-major as in DK2, ADR-0241) and `GameSessionRequest.Resumed`.
The level end and its cutscene: `CampaignResultFlow` (`Prepare`, `Update` with a `CampaignResultFrame`, `Prompt`: the carry, the
statistics once per player, the result once, the win keys, and Space back to the front end), `CampaignCutsceneRunner` (the won
level's title, speech, pause and movies, over an `ICampaignCutsceneHost`) and `TabletDrawList` over `TabletMesh` (the reward
tablet's projection and back-face cull, shared by the win screen and the battle fly-in). `ScriptedViewDirector` also gives
`PlayedHud` (the HUD's visibility under the scripted view) and `PanelFlash` (the flash-button target).
The frame order and the command stream, shared by both adapters: `FrameDriver` (`UpdateScript`, `AdvancePickups`,
`TakeScriptedInput` with the camera-path skip, `TakeTicks` and `AfterTicks`; `ITurnPoseSource`, which `PossessionSession`
implements, puts the possession pose first, and `FrameScriptInput` and `FrameTicks` carry the results), `WorldPointerInput` (the world
pointer's gates from the HUD press to the translated commands, over an `IWorldPointerHost` and a `WorldPointerFrame`) and
`FrameJournalSession` (the input journal's policy: the opt-in variable, folder name, header with the key table's name, and
the failure and close handling; the format is `FrameInputJournal` and `InputJournalWriter`).
The ordinary camera's rules, shared by both adapters: `CameraGlide` and `CameraFocus` (the action-26 glide, `TileCentre`
clamped to the bounds, `Over`, `Bounds`), `ClassicCameraInput` (the 5 px edge scroll, frame time, pointer delta and `Apply`, over a
y-up `CameraPointerSample`), `ClassicCameraSetup` (preset lookup, `ZoomOutLimit`, `Create`), `TileTopPlane` (the ray's hit on a
tile's top plane, command positions and the hand-light point), `GameSessionStarter` (the simulation for a campaign, front-end or DK2-save
start), `ClassicCreatureQueryRules.Follows` and `TurnToFollowHeading`, and `CameraPathPose.FrontEndVerticalFieldOfView`.
The scene mapping for creatures and objects, render only (it reads views, never the world): `PoseInterpolator` (draw seconds, the
clamped position lerp, the shortest-arc wrap and the object turn), `MeshOrientation` (a mesh's turns as a `RenderEuler`, with the
ordering of `0x00580480` and our frame mapping stated), and `AnimationClipRequest` (the clip identity records, `Replacement` and
`StartSeconds`). The clip player itself is `CreatureAnimationPlayer` in `DK2.Art`.
The classic light frame and the hover pick, measured by the adapter and decided here (they read no world state, ADR-0458):
`ClassicLightPublisher` (`Prepare` finds the visible tiles again only when the pose, viewport or distance changes and joins the
second camera at view distance 10; `Publish` writes a diff to an `ITileLightSink`; `PublishPortalLights`, `MarkTablesWritten`,
`Clear`), `ClassicLightFlagsRules` (`Torch`, `IsCheapTorch`), `TileView.LitAsExplored` (the tile fill rule),
`LatticeDirtyTracker` (`ForgetReach`, `TorchReach`), `WorldHoverPicker` (`TryCastTerrain` through `TileTopPlane.TryHit`, then
`Holds`, `Admits`, `Offer`, `TryRenderedResult`, the sweep and `OnPointerTile`; its vector arithmetic is Unity's term by term,
so a pick is bit-identical) and `HoverOverlayPlan` (the frame's red creatures and each submesh's swap and red decision).

Targets `netstandard2.1`. Depends on `DK2.Core`, `DK2.Data.Model`, `DK2.Simulation`
and `DK2.Assets`. The Unity adapter (`unity/README.md`) implements `IRenderTarget`.

Test: `dotnet test tests/DK2.Presentation.Tests`.
