# The Godot project

`godot/DK2.Godot` is the Godot adapter that is to replace `unity/DK2.Unity` (ADR-0805). It holds
no DK2 behaviour: rules live in `src/`, and its HUD renderer (`HudCanvas`, Unity's counterpart of the
same name) draws `DK2.Presentation`'s `HudFrame`. The main scene shows the classic front-end menu
(`ClassicFrontEndMenu` over `FrontEndMenuHud`) on a black screen. Not wired yet: station scenery,
movie playback, sound and keyboard. The campaign entries fly to the campaign map page and the movie gallery shows which
movies are unlocked, but with no station scene there is no land picking, briefing or launch, and name typing is not
reachable; the model disables the save buttons, and the settings pages open but their controls are inert. The front end
loads a real campaign profile and hi-score table, and reads and writes the files listed under *The player's files*. Like
Unity's player it starts full screen (see *Verifying* for a window of a fixed size).

`InGameHud.tscn` is the in-game HUD, ported panel by panel (migration plan step 1b): it starts a campaign level as
Unity's `DungeonRunner` does and ticks it at the default game speed, over the level's terrain (`WorldScene`, plan steps 3a
to 3i: a `Camera3D` that the shared camera rules move (3b), one `MeshInstance3D` per 16x16 chunk with one `ArrayMesh`
surface per `TerrainMaterialKey`, drawn with one `ShaderMaterial` per `TerrainMaterialLook`, so the tint, tag, red, fog and
tag-overlay variants show). A changed tile is rebuilt through the shared `TerrainChunkScheduler`: the 5x5 tiles around it
are marked stale, built on the thread pool and uploaded within a 4 ms budget, lowest chunk first. Water and lava wave
(`LiquidSurfaceField`): each liquid's positions are rewritten in place every frame with `SurfaceUpdateVertexRegion`, and
the wave's normals are uploaded with them, for the lighting (3i). Lit materials draw texture x colour x the vertex's light,
which is the lighting step's (below); the tag, red and tint light are added before DK2's clamp. Blended
terrain (alpha, additive, the tag overlay, the fog) is drawn back to front by the shared `BlendedTerrainStream`: one
`RenderingServer` instance per run at the origin, with the stream's order as its `sorting_offset`, each run one triangle
list uploaded with `MeshSurfaceUpdateVertexRegion`. Shaders write stored bytes / 255 (uniforms are `source_color,
color_conversion_disabled`), and the camera's `Environment` (linear tonemap, a colour-correction table of
`srgb_to_linear`) shows those values as they are, so every blend happens in gamma space as in Unity; the HUD and front end
are unaffected. Objects, structures and torches (`WorldScene.Objects.cs`, step 3f) are drawn from shared `ArrayMesh`es, one node per
object with a `ShaderMaterial` per surface from `ObjectMaterialLook`: the Dungeon Heart, animated through
`CreatureAnimationPlayer` with `HeartPulse`'s beat (its on-screen test is a point test on its origin; the beat's sound and
the mana effect wait for 3k and step 4); doors and traps with the door tint, the blinking blueprint and the lock mesh; wall
torches, bent by `TorchGeometry` and bent again in place when a tile near the wall changes; and their flames as additive
camera-facing billboards, drawn upright. A blended object or a flame takes Unity's sorting-order-0 place through
`ZeroOrderOffset`. Creatures (`WorldScene.Creatures.cs`, step 3g) are one node per creature on the same shared models, each
clip played by `CreatureAnimationPlayer` with the vertex positions rewritten in place, from a clip set cached per entry and
mesh copies per template. The scene hosts the shared `SceneFrame` (`ISceneFrameHost`), so a frame runs in Unity's order:
each creature's place from `PoseInterpolator` (through `RightHandedFrame`), its clip, then its head point. A creature that dies
or leaves is removed, and an idle creature's clip swap keeps the node and restarts the clip. Still no-ops in the frame, waiting
for their steps: the crackle (3l), footsteps and the gem sparkle (3k); the possessed model's hide is 3m's (`WorldScene.Possession.cs`). Entity hover
(`WorldScene.Hover.cs`, step 3h) offers the creatures, hover-target objects, doors and traps to the shared `WorldHoverPicker`
(the 3x3 sweep as its fallback); the Hand's red pass, and the burning red, is a cached `ShaderMaterial` copy with `NextPass` set to
the additive red look, and a hovered gold object swaps to its texture twin, both re-applied only when the hover state changes. The
hover sound waits for step 4. The classic lighting (`WorldScene.Lighting.cs`, step 3i) lights every lit vertex with DK2's
tile tables, ambient keys and Hand light with ADR-0522's distance fade (native ambient, as the owner's Unity scene has it; `--light-fill` adds Godot's fill); an environment pass is its submesh material's
next pass, also over a plan, and the selection cage takes a `ZeroOrderOffset` key. `WorldScene` takes its models and textures from the original install, which the scene requires: there is
no placeholder mode. Twelve panels
are there: the hover HUD (`ClassicHoverHud` over `HoverHud`: the wooden bar under the other panels, the status
line and the delayed tooltip over them), which shows what the shared `HudHoverText` picks from the panels under the
pointer (the resource strip, the bottom panel, the creature overview, the message tabs, the battle tab) or, over the world, from what the pick found (a creature's own name included; step 3h); the battle tab right of the message tabs (`ClassicBattleTab` over `BattleTabHud` and
`BattleTabView`, fed by the battle records of `MusicWorldObserver`: the crossed blades while one of the keeper's
creatures fights, the Call to Arms cancel, the alarm; the cancel's left click posts its command, while the blades' zoom
and the cancel's and the alarm's glides move the camera, and the fly-in notice needs the reward tablet and is not wired;
Level1 has no early fight, so the scene's `--stage-battle` argument spawns a hero Knight on the tile of the keeper's
first creature and lets the simulation start the fight); the
resource strip at the top left (`ClassicResourceHud` over `KeeperResourceHud`, fed by
`KeeperResourceView`: mana with its income and upkeep, gold, the Heart and payday gauges), which takes no input; the
bottom panel (`ClassicBottomPanel` over `BottomPanelHud`), whose clicks post the commands Unity's do; the creature
overview in its Creatures panel (`ClassicCreatureOverview` over `CreatureOverviewHud`, fed by `CreatureOverview` and
`InstallCreatureOverviewArt`: the worker column and a column per owned type, counts by mode, the mode buttons and page
strips, which change it; a press on a column or worker row posts the pickup it asks for through `PickupFlow` and moves the
focus it asks for); the creature query details (`ClassicCreatureQueryPanel` over
`CreatureQueryHud`, fed by `CreatureQueryView`: the queried creature's text, with its inert scroll bar when the text is
taller than the widget), which take no input; choosing a creature needs the query tool's world click, so the scene's `--query-first-creature` argument
queries the viewer's first owned creature instead); and the message
tabs above it (`ClassicMessageTabs` over `MessageTabsHud` and `MessageTabsView`: the level's messages sliding in, a left
click opening one in the message panel, a right click dismissing it, the Review Objective button's objective panel, the
level's indicators). The level's scripted view (the shared `ScriptedViewDirector`) runs: it observes the simulation before
turn 1 and writes nothing, the HUD hides during the opening camera path, and its visibility, the panel and resource
flashes and the options-key hold follow Unity's rules (`PlayedHud`, `PanelFlash`). Escape or Space skips a playing camera path and posts `SkipCameraPathCommand`
(`FrameDriver`; the options key is held that frame). The binary also gates Escape
on a flag and Space on a player bit, which `FrameDriver` does not mirror (ADR-0496, [0x00405010](../docs/research/functions/00405010.md)). Not wired:
the path's camera and the letterbox (plan step 3n), the subtitles (step 4) and sound. The level end is `CampaignResultFlow`, shared with Unity: a
level started with `--from-map` (as the campaign map launches one) runs it, so a verdict writes the profile and Space
then switches to `Main.tscn` at the session's return page, the campaign results page;
Escape after a win posts `ContinueAfterResultCommand` and then `RevealMapCommand`, and the level goes on. The won map's
title card and cutscene need media and are not wired (step 4), and there is no win screen or tablet yet, so a won map
goes straight to the results page. A level started directly (no `--from-map`) has no result flow. The options menu (`ClassicOptionsPanel` over `OptionsHud`) opens on Escape (`ClassicKeyboard` maps Godot physical keys to DirectInput scan codes, the inverse of Unity's `ClassicKeyBindings.Key`; the two tables change together) and pauses the level; its root, objective, exit and confirmation pages work, and the session action after a confirmed restart, return or quit is dropped. `--open-options [Entry]` opens it at start and refuses an entry the menu does not support (`GameOptions`, `Save` and `Load` open their pages). Its Save and Load pages are `ClassicSaveLoadPanel` over `SaveLoadHud`: the saves are listed from `user://Saves` (`%APPDATA%/Godot/app_userdata/DK2.Godot/Saves`, not Unity's `persistentDataPath/Saves`, and not created at startup) or from the directory `--saves <dir>` names, a press on a slot selects it and fills in the name, the name is typed through `ClassicKeyboard`, and Back and Continue work. Submitting works (`SessionSaver` and `SessionLoader`, shared with Unity): Save writes `<saves>/<name>.dk2g`, and Load rebuilds the chosen save's simulation and reloads the scene to continue it, from the saved turn, level and return page; a file the loader refuses shows on the page. Snapshot format 523 saves, such as Unity's under `build/`, are refused (the current format is 524). *The player's files* says what a save and a load write. The Game Options pages (root, graphics, sound, controls, keyboard) are `ClassicGameOptionsPanel` over `GameOptionsHud`: hover and press work, a slider drag holds until the left button is released, Back and Continue work, and a key is bound through `ClassicKeyboard` and the shared `OptionsPagesController.CapturePressedKey`, the controls page showing the names of `ClassicKeyNames` (Unity's `KeyCode.ToString()` names, not DK2's). The settings are read from and saved to `user://game-settings.ini` (`%APPDATA%/Godot/app_userdata/DK2.Godot/`, never Unity's file) or the file `--settings <file>` names through `GameSettingsFile`; a failed load warns and uses the defaults, and a failed save warns. They apply to the game speed, the key bindings and the HUD scale; the sound, shadow, water, environment-mapping and invert values change and nothing acts on them yet (no sound player or lighting); the scroll speed and mouse sensitivity move the camera. The full map (`ClassicFullMap` over `FullMapHud`) opens on M and closes on M, Escape or a left or right press (any other press is taken and does nothing); it draws the map image, the creature squares, the keepers' legend and the hovered room's icon over a black screen, hides the panels below it and holds the pointer. The image is `FullMapImage`, the CPU twin of `DK2FullMap.shader`, uploaded only when a tick changes a pixel. A left press moves the camera focus to the tile and stops a glide. Not wired: the camera preview at the top right (needs the 3D world). Godot's PNG decoder applies a PNG's gAMA chunk and DKII.EXE does not, so the map's palette and the front end's art decode through `PngChunks.WithoutColourSpace` and draw the stored bytes. The minimap (`ClassicMinimap` over `MinimapHud`) draws the disc, frame, markers, north sprite, corners and zoom, with the live camera's focus and heading, and, in whole-map mode, the camera footprint (`MinimapHud.SetCamera`, a `HudDrawKind.Triangles` draw cut to the disc's circle) over the disc and under the frame. The disc is `MinimapDiscImage`, the CPU twin of `DK2Minimap.shader`'s tile and marker passes, redrawn only after a capture or a pose change into one reused texture; its rock backdrop is flipped against Unity's ([Q861](../docs/research/open-questions.md)). The options corner opens the options menu, a right click on a tile opens the full map, and the HUD scale follows the Game Options setting. The minimap owns the one `WorldView` and `MinimapHud`, captured once per tick, and the full map reads that grid. A left click on a tile glides the camera there. The minimap has no heading rule for the opening camera
path: the HUD, so the minimap, is hidden for the whole path. The query and sell corners set the tool and post their GUI button command, as Unity does, and light up; a lit corner goes out on a panel choice, a stored special or a right-click cancel; the gold withdraw keeps it selected, as in Unity. While a menu is modal or input is suppressed a press on the minimap's map does nothing, as in Unity. The level start (`CampaignLevelData`), tick pacing (`TickPacer`), the panel's opening state, the stored-special
click, the message string tables (`MessageTextTables`), player names (`PlayerNames`) and which panel's hover text wins
(`HudHoverText`), the creature overview's install art (`InstallCreatureOverviewArt`), the query target and its details
(`CreatureQueryView`), the creature names it shows (`InstallClassicCreatureNames`), and the battle tab's place, hover
text and click actions (`BattleTabView.MenuTop`, `BattleTabHoverText`, `BattleTabView.Action`), the pointer-over-HUD list
(`HudPointerMap`, for the HUD's hover text and the world pointer; the creature query details count only while
no menu is modal), the settings and saves locations (`GameFolders` over `user://`), the launch arguments
(`LaunchArguments`), the played slot (`CampaignSlot.ForLevel`, passed to `WorldHoverText`), the frame clamp and
ticks per frame (`TickPacer.MaximumFrameSeconds`, `DefaultMaxTicksPerFrame`) the focus-out cancel (`PointerGestures.CancelAll`), the HUD tool state (`HudToolState`: the tool, the panel choice,
the query tool, the selected corner and the panel-choice and stored-special transitions) and the HUD action routing
(`HudActionRouter`: the gate, the cancels, the corners), the HUD's and the world's presses (`HudPressRouter` in `QueueHudInput`'s order,
`WorldClickCommands`, `PickupFlow`, `BattleTabView.Click`, and the room, door and trap navigation entries)
are shared with Unity in `DK2.Presentation`. The scene reads the left and right presses and releases from `Window.WindowInput`, before any panel marks
them handled, and routes them once per frame through `WorldPointerInput`, in Unity's order: the ordinary-input gate, possession,
`HudPressRouter`, `WorldClickCommands`, the minimap's map press, `SelectTool`, then a press's or release's intent. The frame
order (the scripted view's step, the local pickups, the speed and pause gate, the ticks, the journal's hand-off, the tag
acknowledgements) is the shared `FrameDriver`, so deferred drops post. The panels only hit-test (`TryPress`, `HandlePress`, `ActionAt`); the full map and options menu swallow presses on the live
`ClassicHudControls` flags the router reads. Working: the overview pickup, the gold withdraw, the right-click cancels of the query tool
and a held tool, and the order of full map, options, overview, minimap and battle tab. The camera moves (`UpdateCamera`, in Unity's order: the path, the glide, the script's follow, then `ClassicCameraInput`: keys, edges, wheel and held buttons with a y-up pointer, `PointerOverHud`, window focus): every glide (the script's, minimap, room, door, trap, battle tab, portal, Heart, fight and a message's goto) runs through `CameraGlide`; the full map and the overview jump the focus; the query follow and turn, the script's zoom and follow, the scroll speed and the panel collapse are wired. The follow runs after the frame's draw and reads the drawn creature's position (`WorldScene.TryGetCreaturePosition`), as Unity's does, so the focus moves with the model (an even step a frame, where the settled position jumped a tick at a time). Real keys, edges and wheel were exercised only by unit tests, not live in Godot. Not done: session actions. The world pointer works (plan step 3e): `WorldScene.TryPickRay` builds the ray from the placed camera pose through `MinimapCameraView.RayDirection` (Godot 4.8's `Camera3D.ProjectRayNormal` drops the frustum offset, `docs/TRAPS.md`), and `InGameHud` runs it through `TilePicker`, `TileTopPlane`, the fog test, `WorldHoverPicker.TryCastTerrain` and `HoverCursorResolver`, so a left click or release finds its tile, and tagging a wall or placing a room, door or trap posts through `WorldPointerInput` and `WorldClickCommands`. The hand cursor (`ClassicHandCursor`, canvas layer 101) is drawn with the OS pointer hidden while it shows (hiding it was not exercised); the placement cursor (`ClassicPlacementOverlay`, layer 100) draws the tool's icon, No Go and price, or the held icons; and the selection cage (`WorldScene`'s `CageRibbon`) pulses red or blue over the hovered tile. Creatures, hover-target objects, doors and traps are hover and pickup targets (`WorldScene.Hover.cs`, step 3h); the press itself was wired here. Not wired: the hover sound (step 4). The `CancelPointerGesture` order: a
frame without ordinary input cancels only the world drag; when the window loses focus the scene releases the keys and cancels the
bottom panel's press and then the world drag; the bottom panel cancels its own press when it is freed. A held choice slot queues its release
sound request 0x136, which nothing plays yet.
Options gamma uses one final scene/HUD/cursor pass; possession colour ramps remain developer-only and off by default (ADR-0822): [implementation and limits](../docs/research/possession-colour-rendering.md).
The possession canvas presents the shared HUD geometry and compositing in height-fitted widescreen content (ADR-0821), uses the map-2 minimap, and blocks ordinary panels and navigation shortcuts while the camera is held, with Options and GUI toggle available in the live possession view: [scope and limits](../docs/research/possession-hud.md).

Authored first-person swings and creature filters use the shared [foreground draw list](../docs/research/possession-foreground.md);
the one current-action target reuses [the hover pass](../docs/research/possession-target-highlight.md).

### Shot effects

`WorldScene.Effects.cs` consumes shot-effect requests through `EffectDrawList` and
uploads sprite particles using MultiMesh batches. This includes possession shot 32's
creation effect102, triggered by the shot update. Entry-star visibility, staging and
native-parity limits are recorded in [Q749](../docs/research/possession-entry-effect.md).
Effects use the scene's frame clock; their lights publish before `PrepareLighting`. Shot bodies, mesh/flat/procedural effect uploads and full transparent
ordering remain unbuilt in this adapter. Native evidence: [shot effects](../docs/research/shot-effects.md).

Possession (step 3m, `InGameHud.Possession.cs` and `WorldScene.Possession.cs`): `InGameHud` drives the shared `PossessionSession` every frame from the keys, the pointer's travel and the scene's engine clock, and the scene puts its pose on the same `Camera3D` while it holds the view (the fly-in, first person, the death view, the fly-out), with the possession cameras' near plane `PossessionCamera.NearClipDistance` (0.05; the ordinary camera keeps 0.3). The pointer lock is `Input.MouseMode = Captured` while `PointerLockWanted`, never under `--pointer-path` (the script's travel stands in; nothing warps the pointer). Virtual room-pillar roof joints are checked in a staged Treasury fixture with no pillar object states; [verification scope](../docs/research/possession-client-completion.md#virtual-room-pillar-roof). The roof includes virtual room-pillar occupancy from the same `RoomPillarLayout` used by room decoration and is one `MeshInstance3D` per 16x16 chunk (`TerrainCeilingScheduler`, `TerrainCeilingGeometry`), shown exactly while a model is hidden, in the terrain material on the `Ceiling` texture with anchor kind 2; the lattice latch (full-height walls, `PossessionScene.Latch`) and the hidden possessed model go through the `SceneFrame` sequence. Checked in 1920x1080 captures from a save made after Level1's opening path, with `--possess-first-creature` and a scripted pointer: no roof and the imp drawn during the fly-in; in first person the roof, full-height walls, the hidden model, the HUD and no Hand; the scripted pointer turns the view and a scripted right click starts the fly-out. Not exercised in Godot: real mouse capture, its re-capture after alt-tab, the walking keys and the death view. Remaining listener, lighting, mouse-calibration and object-death bridge boundaries: [client completion](../docs/research/possession-client-completion.md). Shot creation-effect sprites and lights are built ([adapter scope](#shot-effects)). Not done: the view's sounds are dropped (no sound player, step 4); the roof is black away from walls and torches (lighting); `--possess-first-creature` during Level1's opening path shows the possession camera, as the path camera (3n) is not ported.
Run it by naming the scene, with
optional user arguments after `--`:
`<console exe> --path godot/DK2.Godot res://InGameHud.tscn -- --level Level2 --seed 1` (defaults Level1, seed 1; add
`--glide-to x,y` to glide the camera to a tile one second in (a check; it waits for the opening path to end), `--light-probe-no-torches` and `--light-probe-shift` for frames to compare with Unity's `ClassicWorldLightingProbe` (no torch lights; the camera moved by (1, 0, 1), its shifted frames), each printing the frame each turn is first drawn on, as `--light-probe` alone does, `--light-fill` to add Godot's fill (off by default, as native ambient is in the owner's Unity scene), `--light-perpixel` to evaluate DK2's light per pixel instead of per vertex (step 1 of the lighting direction, owner 2026-11-18: the same tables, formulas, clamp and pack in the fragment step from the interpolated position and normals; classic stays the default), `--light-shadows` with `--light-perpixel` for real torch shadows (step 2, `WorldScene.Shadows.cs`; nothing without per-pixel light), `--pointer-path "frame:x,y[:LlRr];..."` to drive the pointer from a script in place of the OS one (a check, below: keys in strictly increasing frames of this scene counted from 0, x and y in screen pixels from the top left, a straight line between keys, `L`/`l` pressing and releasing the primary button and `R`/`r` the secondary; a malformed value warns and the OS pointer is used), `--query-first-creature` to show the query details, `--possess-first-creature` to focus the ordinary camera on the viewer's first eligible creature before staging possession and its authored creation request (an unpaid check, `PossessionStaging`; [scope](../docs/research/possession-entry-effect.md)), `--stage-battle` to show the battle tab, `--open-options [Entry]` to open the options menu at an entry, `Save` or `Load` for those pages, `--saves <dir>` for the saves directory the pages list, `--save-as <name>` with `--save-tick <n>` to save under that name once the turn reaches n (a check, through the Save page), `--load <name>` to load a save of that name on the first frame, `--hash-at <n>` to print the state hash at turn n (a resumed instance ignores `--save-as` and `--load`; a `--load` run overwrites the profile file, so pass `--profile <temp file>`), `--settings <file>` for the settings file, `--profile <file>` and `--hiscores <file>` for the front end's profile and hi-score table (every scene takes all four of `--profile`, `--settings`, `--saves` and `--hiscores`, through `UserFolders`), `--open-full-map` to open the full map, `--from-map` to start the level as the campaign map launches it (it plays the profile's slot and runs `CampaignResultFlow`; the level end then writes the profile, so pass `--profile <temp file>`), `--stage-win` or `--stage-loss` to end the viewer's game after the first turn, `--leave-result` to press Space once the result is recorded, `--escape-result` to press Escape once on the result, print the commands it posted, and then press Space (the three result checks need `--from-map`; Godot prints the viewer's status changes and a camera path's start and end). `--input-journal <dir>` writes the session's input journals (`frames.journal` and `input.journal`) into a new folder there; it is Godot's only journal switch, because `DK2_INPUT_JOURNAL` is Unity's only (a globally set variable must not make every Godot run write); the header carries `source=godot`, `keys=Godot.Key` and `godot.version`. `LaunchArguments` also parses `--dk2Install <path>`, which the scene ignores: it still uses `Dk2Install.Find()`).
Why the toolchain looks like this: ADR-0810. Agents working here: `.claude/agents/godot-developer.md`.

### The player's files

`UserFolders.FromArguments` builds `GameFolders` over `user://` (`%APPDATA%/Godot/app_userdata/DK2.Godot/`), never Unity's
`persistentDataPath`; `--profile`, `--settings`, `--saves` and `--hiscores` name a file or directory instead, and
`--profile` without `--settings` puts the HUD's settings beside that profile, as Unity does. A front-end start:

- reads `user://campaign-profile.dk2p`; only when it is missing, reads the original game's registry profile
  (HKCU, `KEY_QUERY_VALUE`, read-only) to seed a new one, so the two profiles diverge after that first start;
- reads `user://HiScores.dat`, then the install's `Data/Settings/HiScores.dat`, then starts empty;
- writes the profile only if it was missing or `EnsureOpenMap` changed it, and `HiScores.dat` only on a committed name;
- writes the repair of the won map's branch row to the profile when the front end is entered at CampaignResults (a level
  that ended returns there);
- never writes Unity's `persistentDataPath` or the install's `HiScores.dat`, unless `--profile` or `--hiscores` points there.

A corrupt or unwritable profile, or a registry error, warns and gives a fresh in-memory profile that is not saved
(`CampaignProfileStore.Adopt`); an unreadable hi-score file warns and shows empty rows.

The in-game scene loads the profile on the first save or load, from the same path and with the same fallback
(`UserFolders.LoadProfile`):

- a save writes `<saves>/<name>.dk2g`, the profile file if it is missing, and the profile again when a played slot has a new carry;
- a load overwrites the profile file with the save's profile, as Unity does;
- a `--from-map` level end writes the profile: the level statistics once per player, the result once (the played slot's
  status and attempts), the carry and, at level start, the cleared transfer creature;
- with a corrupt profile (current policy, an owner product decision) a save still works and keeps the profile in memory with no
  file write, and a load is refused with "The campaign profile could not be read, so a save cannot be loaded." and writes nothing.

| File | Role |
|---|---|
| `godot-version.json` | the one pin: editor build and SHA-512, `Godot.NET.Sdk` version, Godot AI version and SHA-256 |
| `setup.ps1` | installs the pinned editor and the Godot AI addon, verified; warns on drift from the pin |
| `editor.ps1` | starts the pinned editor on the project, from the repository root |
| `DK2.Godot/` | the Godot project: `project.godot`, `DK2.Godot.csproj` (`ProjectReference`s into `src/`), scenes, C# |
| `DK2.Godot/Main.tscn`, `Main.cs` | the main scene: the front-end menu (`ClassicFrontEndMenu`) |
| `DK2.Godot/UserFolders.cs` | `GameFolders` over `user://` with the `--profile`, `--settings`, `--saves` and `--hiscores` overrides, for both scenes |
| `DK2.Godot/InGameHud.tscn`, `InGameHud.cs` | the in-game HUD over a running campaign level and its `WorldScene` (see above) |
| `DK2.Godot/WorldScene.cs` | the 3D world: `TerrainTileBuilder`'s chunks as `MeshInstance3D`s through `RightHandedFrame`, rebuilt by `TerrainChunkScheduler` (`ITerrainChunkSink`) with one `ShaderMaterial` per `TerrainMaterialLook`, the waving liquids (`LiquidSurfaceField`), the blended runs (`BlendedTerrainStream`, `ViewFrustum`, one `RenderingServer` instance per run), the gamma-space output `Environment`, the selection cage (`CageRibbon`, `TileHoverOutline.Build`'s ribbon with Godot's own additive shader), `TryPickRay` (the pointer's ray), and the classic `Camera3D`, placed once a frame at the focus the scene moves |
| `DK2.Godot/WorldScene.Objects.cs` | the objects, structures and torches (`WorldScene`, partial): shared model meshes, a node per object with per-surface looks (`ObjectMaterialLook`), the Heart through `CreatureAnimationPlayer` and `HeartPulse`, the door tint, blueprint and lock mesh, torch meshes (`TorchGeometry`, re-bent in place, with a `CustomAabb`), flames as additive billboards, and `ZeroOrderOffset` for blended draws |
| `DK2.Godot/WorldScene.Lighting.cs` | the classic lighting (`WorldScene`, partial, step 3i): the scene's lights in a `ClassicWorldLightCache`, the visible tables published by `ClassicLightPublisher` into a float texture array (a layer per tile row, changed rows uploaded alone), the lit shaders' vertex step, or with `--light-perpixel` their fragment step over flat anchors and model transforms (the `ClassicVertexLight` copy, the optional fill and the distance fade, the environment-map coordinates), the shader globals, the ambient keys and the Hand's light; options gamma is owned by the final [screen-colour pass](../docs/research/possession-colour-rendering.md) |
| `DK2.Godot/WorldScene.Shadows.cs` | `--light-shadows` (step 2 of the lighting direction): the two torches nearest the focus (`ClassicTorchShadowSlots`) each render a distance map from a downward 150-degree `SubViewport` camera that sees only the casters' layers (terrain chunks, creatures, objects, structures); a wall's shadow multiplies only that torch's DK2 term, a creature's or object's darkens the whole per-pixel light by DK2's strength (1 - coverage x strength), with the caster fade from `ClassicShadowSourceSelector` (DK2's three nearest torches within 2.5 tiles, fading from the fourth); a torch holding a map counts 1.5 tiles nearer, so the maps do not swap back and forth |
| `DK2.Godot/WorldScene.Creatures.cs` | the creatures (`WorldScene`, partial, the `ISceneFrameHost` of `SceneFrame`): a `ModelSlot` per creature on the shared model meshes, clips through `CreatureAnimationPlayer` with in-place vertex upload, clip sets cached per entry, the drawn place and head point through `RightHandedFrame`, removal on death or exit; the crackle, footsteps and gem sparkle are no-ops until 3l and 3k |
| `DK2.Godot/WorldScene.Possession.cs` | the possession's drawn world (`WorldScene`, partial, step 3m): the camera's possession pose and near plane, the lattice latch and the hidden possessed model before the present, and the roof (`TerrainCeilingScheduler` into one `MeshInstance3D` per chunk, shown while a model is hidden) |
| `DK2.Godot/WorldScene.PossessionVisibility.cs` | possession tile visibility; scope and verification: [possession-warp section 8.3](../docs/research/possession-warp.md#83-the-enumeration-strongly-inferred-whole-disassembly-of-0x005737e0-and-0x00572f60) |
| `DK2.Godot/WorldScene.PossessionRendering.cs` | the world-shader distance fade; scope and verification: [possession-warp section 4](../docs/research/possession-warp.md#4-the-possession-distance-fade-strongly-inferred-implementable) |
| `DK2.Godot/InGameHud.Possession.cs` | the possession's host (`InGameHud`, partial, step 3m): the shared `PossessionSession` driven every frame from the keys, pointer travel and engine clock, the pointer lock (`Input.MouseMode`, never under `--pointer-path`), `--possess-first-creature` |
| `DK2.Godot/InGameHud.PossessionForeground.cs` | submits the engine-free authored swing triangle list; [scope](../docs/research/possession-foreground.md) |
| `DK2.Godot/InGameHud.PossessionHighlight.cs` | submits the shared current-action target to the existing hover pass; [scope](../docs/research/possession-target-highlight.md) |
| `DK2.Godot/WorldScene.Hover.cs` | entity hover (`WorldScene`, partial, step 3h): `PickHoverEntity` offers the drawn creatures, hover-target objects, doors and traps to `WorldHoverPicker` (then its 3x3 sweep); `DrawHoverOverlay` and `CommitHoverOverlay` set each hovered `ModelSlot`'s red pass (`HoverOverlayPlan`, `ObjectMaterialLook.HoverOverlay`) and gold twin; the burning red is the same pass |
| `DK2.Godot/ClassicHandCursor.cs` | the hand cursor: `HandCursorHud`'s quad drawn by `HudCanvas` on canvas layer 101 from `InstallHandCursorSheets`, the OS pointer hidden while it shows |
| `DK2.Godot/ClassicPlacementOverlay.cs` | the placement cursor: `PlacementCursorHud` drawn by `HudCanvas` on canvas layer 100 (the tool's icon, No Go, price, held icons) |
| `DK2.Godot/ScriptedPointerGate.cs` | drops OS mouse events while a `--pointer-path` script runs, so a capture never takes the owner's mouse |
| `DK2.Godot/PerformanceReport.cs` | the performance harness (`--perf-report`, below): per-frame samples into `DK2.Presentation`'s `FrameRecording`, the `--perf-camera` focus script (`CameraScript`), the exit table and JSON |
| `DK2.Godot/PerformanceReportTail.cs` | the harness's last process callback of the frame, which ends its process time |
| `DK2.Godot/WorldScene.Performance.cs` | `WorldScene`'s exit-line counters (lighting step, blended re-sort, Present, collections) as the report's `counters` |
| `DK2.Godot/ClassicHoverHud.cs` | the hover HUD: `HoverHud` drawn by two `HudCanvas`es, top-docked, the bar at z index -1 and the text at +1 around the other panels, no input |
| `DK2.Godot/ClassicResourceHud.cs` | the keeper's resource strip: `KeeperResourceHud` drawn by `HudCanvas`, top-docked, no input |
| `DK2.Godot/ClassicBottomPanel.cs` | the keeper's bottom panel: `BottomPanelHud` drawn by `HudCanvas`, mouse forwarded |
| `DK2.Godot/ClassicCreatureOverview.cs` | the creature overview in the Creatures panel: `CreatureOverviewHud` drawn by `HudCanvas`, bottom-docked, hit-tests a press for the router without taking it from the panel |
| `DK2.Godot/ClassicCreatureQueryPanel.cs` | the creature query details: `CreatureQueryHud` drawn by `HudCanvas`, bottom-docked, no input |
| `DK2.Godot/ClassicBattleTab.cs` | the battle tab: `BattleTabHud` drawn by `HudCanvas`, bottom-docked, presses on a shown button taken and raised |
| `DK2.Godot/ClassicSaveLoadPanel.cs` | the options menu's Save and Load pages: `SaveLoadHud` drawn by `HudCanvas` over the whole screen, taking every mouse press while it shows, the typed name from `ClassicKeyboard` |
| `DK2.Godot/ClassicGameOptionsPanel.cs` | the options menu's Game Options pages: `GameOptionsHud` drawn by `HudCanvas` over the whole screen, taking every mouse press while it shows, key capture from `ClassicKeyboard` |
| `DK2.Godot/ClassicFullMap.cs` | the full map: `FullMapHud` drawn by two `HudCanvas`es (black backdrop under, room icon, squares and legend over) around `FullMapImage`'s texture, over the whole screen, swallowing every mouse press while the router holds it open (the router acts on it) |
| `DK2.Godot/ClassicMinimap.cs` | the minimap: `MinimapHud` drawn by `HudCanvas` bottom-docked over `MinimapDiscImage`'s texture, hit-tests corner and map presses for the router without taking them from the panels under it |
| `DK2.Godot/ClassicMessageTabs.cs` | the message tabs, message and objective panels, indicators: `MessageTabsHud` drawn by `HudCanvas`, bottom-docked, mouse forwarded |
| `DK2.Godot/HudCanvas*.cs` | the one `HudFrame` renderer every panel draws through |

## Setting up a machine

Needs the .NET 10 SDK (`global.json`; Godot 4.8 requires .NET 10) and, for Godot AI,
[uv](https://docs.astral.sh/uv/getting-started/installation/).

```powershell
powershell -File godot/setup.ps1     # editor into %LOCALAPPDATA%\Programs\Godot\<version>\, addon into DK2.Godot/addons/godot_ai/
powershell -File godot/editor.ps1    # open the editor
```

The game reads the original install in place: `DK2_DIR`, else the usual install locations
(`DK2.Data.Dk2Install`); the in-game scene needs it for its models and textures as well as the HUD art. The front end's replacement TrueType face is Bullfrog's DungeonKeeperII.ttf, which is not
ours to ship (download: see `unity/README.md`). It is read from an absolute path, never imported:
`DK2_REPLACEMENT_FONT`, else `DK2.Godot/Fonts/DungeonKeeperII.ttf` (gitignored), else the Unity project's
gitignored copy. Without it the menu draws DK2's BF4 bitmap font.

`-InstallRoot` or `DK2_GODOT_ROOT` puts editors elsewhere. Each version gets its own folder, so
an upgrade never touches the version you are leaving. Export templates are not installed yet.

## Connecting Claude Code and Codex (Godot AI)

[Godot AI](https://github.com/hi-godot/godot-ai) (MIT) is an editor addon plus a Python MCP server
that lets an agent drive the open editor. There is no Claude or Codex marketplace plugin for it;
its own channel is the dock, which writes each client's MCP entry with a version-pinned
`uvx ... godot-ai==<version> godot-ai attach` command and re-pins it on every update. On
Windows that entry holds machine paths (pythonw, uvx), so it is never committed.

Once per machine (and once per worktree, for Claude Code), in the editor started by `editor.ps1`:

1. **Editor → Editor Settings**, filter for `mcp_client_scope` (turn on *Advanced Settings* if it
   is hidden), set **Plugins → Godot AI → Mcp Client Scope** to `local`. `editor.ps1` starts
   Godot in the repository root because the dock registers against the editor's working directory.
2. **Godot AI dock → Clients & Tools → Clients tab → Configure** next to *Claude Code* and
   *Codex*. Codex's entry goes to `~/.codex/config.toml`, where this repo's other Codex MCP
   servers already live. Restart both clients.

Telemetry needs no step: `editor.ps1` sets `GODOT_AI_DISABLE_TELEMETRY`, so Configure writes
`--disable-telemetry` into each entry; an editor started any other way writes entries without it.
Leave the dock's *Developer mode* off. Check an entry with `claude mcp get godot-ai` from the
repository root (the dock may show Claude Code red anyway; `docs/TRAPS.md`).

Configure deletes `godot-ai` entries from every scope first, including a checked-in `.mcp.json`;
keep Godot AI out of the repository's `.mcp.json`. Its C# support is text-only: it writes `.cs`
files but does not compile them, so build with `dotnet`.

## Verifying

```powershell
dotnet build godot/DK2.Godot/DK2.Godot.csproj                               # compile, no editor needed
powershell -File godot/editor.ps1 -Console --headless --import               # import; errors print here
powershell -File godot/editor.ps1 -Console --headless --build-solutions --quit
powershell -File godot/editor.ps1 -Console --headless --quit-after 10        # run the main scene
```

The last prints `DK2.Godot front end: page Main` once the menu is up; with `res://InGameHud.tscn` added it runs the
in-game HUD instead and prints `DK2.Godot in-game HUD: Level1 seed 1`. `--headless` has no renderer; to see a
frame, run the console build without it, e.g. `--path godot/DK2.Godot --write-movie <dir>/f.png --fixed-fps 30
--quit-after 120` (Movie Maker writes every frame as a PNG; keep the output outside the repository).
`editor.ps1` adds `--editor` to any run without `--headless`, so call the pinned console executable directly for this.
To capture the world, run `res://InGameHud.tscn` that way at 1920x1080 (the `override.cfg` below) with
`-- --settings <temp file>`. Level1's opening camera path hides the HUD for about 1,000 frames at 30 fps, so a capture of the HUD
over the terrain needs a save made after the path (`--save-as <name> --save-tick 130` with `--saves` and `--profile`, then
`--load <name>`); the terrain itself is visible from the first frame.
To drive the pointer in a capture, add `--pointer-path` to those arguments, e.g. `-- --load <name> --saves <dir> --profile <file> --pointer-path "60:960,540;90:700,400:L;92:700,400:l"`: the script's pointer is pushed into the viewport for the panels and the world, and `ScriptedPointerGate` drops the OS mouse events, so the owner's cursor is never warped or read. Frames count from the scene's first, so a capture of the HUD still needs the save after the opening path.
`InGameHud.tscn` reads the saved settings (speed, key bindings, HUD scale) when it starts, so a capture recipe passes
`-- --settings <temp file>` to repeat the same frames and not depend on the player's `user://game-settings.ini`. A recipe that saves or loads
(`--save-as`, `--load`, or a press on the Save or Load page) passes `--saves <temp dir>` and `--profile <temp file>` too: a save writes
the profile if it is missing and a load overwrites it with the save's profile; a `--from-map` run writes the profile at the level end,
so it passes `--profile <temp file>` too. The main
scene now loads the profile and hi-scores too, so its recipe also passes `-- --profile <temp file> --hiscores <temp file>`
(with `--settings` if it opens the HUD); otherwise a run reads, and on a first start creates, the player's own files.
`editor.ps1` cannot forward these user arguments (an ambiguous PowerShell parameter, `docs/TRAPS.md`): call the console
executable directly.
The window takes the desktop's size and `--windowed`/`--resolution` do not change it (`docs/TRAPS.md`); for a
1920x1080 capture put a temporary `DK2.Godot/override.cfg` holding `[display]` / `window/size/mode=0` beside
`project.godot`, and delete it afterwards.

## Measuring performance

Every rendering change is measured (migration plan step 3o). `--perf-report <file>` on `InGameHud.tscn` adds
`PerformanceReport`; without it nothing is added. After `--perf-warmup <n>` frames (120) it samples each frame into a
preallocated buffer: the frame interval, the process time (its first process callback to `PerformanceReportTail`, the
last), the viewport's CPU and GPU render time, frame setup, draw calls, objects, primitives, video, texture and buffer
memory, managed bytes the main thread allocated, collections and pipeline compilations. Each frame is filed as still or
moving by whether the drawn camera moved. The GPU time is the latest the GPU has finished, about two frames behind, so it
smears two frames across a still/moving boundary. At `--perf-frames <n>` it prints mean, p50, p95, p99 and max per phase,
writes the JSON (GPU, driver, API, renderer, render and window size, level, seed, arguments with the home folder as `~`,
build, the world's step counters, all threads' allocation over the whole window, and `harness.selfBytes`, expected 0,
with its largest frame; a nonzero value warns and is left out of `allocatedBytes`) and quits; without `--perf-frames` it
reports at exit. A malformed `--perf-*` value quits at once with code 1. `--perf-camera "frame:dx,dy;..."` moves the focus in tiles east and
south of where the run starts, keyed by frame. `--perf-size WxH` renders at that size whatever the window is.
V-Sync is off unless `--perf-vsync`. The C# runs as the editor builds it (Debug); the report says so.

The recipe, `godot/perf.ps1`, builds the C# first (`-SkipBuild` not to), makes a save after each level's opening camera path once (turn 200, headless), then runs
Level1 and Level7 at 1920x1080 and 1024x576 in real time from it, with a fixed seed, 1,800 frames along one camera script
(still, a loop, still) and the pointer held at the centre by `--pointer-path`, and adds the machine's CPU load to each
report. Pass the same `-State` folder to both sides of a comparison so they start from the same saves:

```powershell
powershell -File godot/perf.ps1 -Out <dir> -Label classic -Repeat 2 -State <dir>\state
powershell -File godot/perf.ps1 -Out <dir> -Label perpixel -Extra --light-perpixel -Repeat 2 -State <dir>\state
python godot/perf_compare.py <dir>/classic <dir>/perpixel --phase moving
```

`perf_compare.py` takes one label a side, groups the reports by level and render size, takes the median of repeats, and
prints each metric's change with the other CPU load and the spread of frame p50 across repeats (differences inside that
spread are noise); it warns when the sides differ in camera script, frame counts, Godot, builds, V-Sync, GPU or seed.

## Upgrading Godot

1. Take the new build's file name and SHA-512 from the release's `SHA512-SUMS.txt`
   (`github.com/godotengine/godot-builds/releases/<version>`), and the matching `Godot.NET.Sdk`
   version from NuGet. Update `godot-version.json`.
2. Run `setup.ps1`, then `editor.ps1`. On a new minor version the editor offers its project
   upgrade tool and rewrites the `Sdk` in `DK2.Godot.csproj` and `config/features` in
   `project.godot`; accept it. `setup.ps1` warns until the csproj and the pin agree.
3. Run the verification above and commit the pin with whatever the editor rewrote, nothing else.
4. Read the release's *breaking changes* list (the `breaks compat` label on the milestone).

Godot AI: press **Update** in its dock (signature-checked, re-pins both clients), then set
`godotAi` in `godot-version.json` to the new version and archive SHA-256. Never extract a release
over an existing `addons/godot_ai/`.
