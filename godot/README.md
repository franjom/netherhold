# The Godot project

`godot/DK2.Godot` is the Godot adapter that is to replace `unity/DK2.Unity` (ADR-0805). It holds
no DK2 behaviour: rules live in `src/`, and its HUD renderer (`HudCanvas`, Unity's counterpart of the
same name) draws `DK2.Presentation`'s `HudFrame`. The main scene shows the classic front-end menu
(`ClassicFrontEndMenu` over `FrontEndMenuHud`) on a black screen, after the title screen and the intro movies on the
first front end of the process (*Movies* below). Not wired yet: station scenery and keyboard; the pages' sounds play
(*Sound* below). The campaign entries fly to the campaign map page and the movie gallery shows which
movies are unlocked, but with no station scene there is no land picking, briefing or launch, and name typing is not
reachable; the model disables the save buttons, and the settings pages open but their controls are inert. The front end
loads a real campaign profile and hi-score table, and reads and writes the files listed under *The player's files*. A
start without a scene shows the Netherhold launcher first (*The launcher* below), which sets the install, the menu font and
the screen mode; a scripted run never sees it and opens full screen as before (see *Verifying* for a window of a fixed size).

`InGameHud.tscn` is the in-game HUD, ported panel by panel (migration plan step 1b): it starts a campaign level as
Unity's `DungeonRunner` does and ticks it at the default game speed, over the level's terrain (`WorldScene`, plan steps 3a
to 3i: a `Camera3D` that the shared camera rules move (3b), one `RenderingServer` mesh instance per 16x16 chunk with
one surface per recipe, with shared materials distinguished by look and sight-fog semantics). Tag, red and tint changes
write one texel in a persistent RGBA float state image without rebuilding chunks (step 3p-b). `TerrainTileBuilder`
chooses each vertex's tag and tint sources, carried beside its lighting anchor in `CUSTOM0`: room walls can read their
neighbour, room floors can read quarter tint, and red reads the drawn tile. `TerrainTileState` packs ambient and quarter
RGB bytes, tag/red flags and fog opacity. One `ImageTexture.Update` uploads the image after a dirty frame; this public
Godot API uploads the whole image. Sight fog keeps its opacity material and rebuild path. Geometry changes still use
the shared `TerrainChunkScheduler`: the 5x5 tiles around them are marked stale, built on the thread pool and uploaded
within a 4 ms budget, lowest chunk first. The build task also packs each rebuilt chunk's opaque surfaces as Godot's own
vertex bytes (`ITerrainChunkPacker`, `WorldScene.ChunkUpload.cs`, audit item 12), which `RenderingServer.MeshAddSurface`
takes as they are. Unity keeps the scheduler's default material-state path. Water and lava wave
on the GPU (audit item 9): each liquid is one static mesh, rebuilt only when its tiles change (`LiquidSurfaceField.Refresh`),
whose vertices carry their grid point (`LiquidSurfaceWave.Points`, CUSTOM1); `Shaders/WorldLiquid.gdshader` (the world
shader with `DK2_LIQUID`) computes each position and normal from it and DK2's frame, the one uniform set per frame, with
`LiquidSurfaceWave`'s integer phase masks, sine table, rest height and amplitude (`LiquidSurfaceWave.Vertex` is the CPU
mirror, pinned to the CPU wave bit for bit). Nothing is uploaded per frame. Lit materials draw texture x colour x the vertex's light,
which is the lighting step's (below); the tag, red and tint light are added before DK2's clamp. Blended
terrain (alpha, additive and sight fog) is drawn back to front by the shared `BlendedTerrainStream` and keyed
by the shared `BlendedRunSlots` (step 3q): one `RenderingServer` instance per slot, sorted from the origin with the offset
that makes Godot's own measure, the straight-line distance from the camera, the run's key (the distance to its nearest
triangle centroid, falling down the stream so the runs keep its order). Blended objects, flames, the cage and the effect
batches (each boxed around its live sprites), sorted by their box centres in the same measure, fall between runs.
Opaque overlays are evaluated in the world shader. With `--light-post`, one static overlay mesh per chunk adds
them after AO; transparent recipes use the same chunk batch at priority -1, before the sorted stream. These batches
share one world additive material and sample the tile state, so state changes do not rebuild them. Overlay triangles
leave the sorted stream.
DK2 orders by depth along the view, not distance, so near the
screen's edges the two can disagree. A slot keeps a material's vertices between re-sorts, so a camera move uploads only
the run's indices (`MeshSurfaceUpdateIndexRegion`) and the vertices the slot never held, in the surface's own layout
(Godot adds a tangent beside each normal). Shaders write stored bytes / 255 (uniforms are `source_color,
color_conversion_disabled`), and the camera's `Environment` (linear tonemap, a colour-correction table of
`srgb_to_linear`) shows those values as they are, so every blend happens in gamma space as in Unity; the HUD and front end
are unaffected. Objects, structures and torches (`WorldScene.Objects.cs`, step 3f) are drawn from shared `ArrayMesh`es, one node per
object with a `ShaderMaterial` per surface from `ObjectMaterialLook`: the Dungeon Heart, animated through
`CreatureAnimationPlayer` with `HeartPulse`'s beat (its on-screen test is a point test on its origin; a beat requests the
Heart's mana effect and its sound, step 4); doors and traps with the door tint, the blinking blueprint and the lock mesh; wall
torches, bent by `TorchGeometry` and bent again in place when a tile near the wall changes; and their flames as additive
camera-facing billboards, drawn upright: one texture array of the frames, every flame an instance of one batch with its frame
in the custom data (audit item 6, step 3k). A blended object sorts by its AABB centre among the blended runs.
Creatures (`WorldScene.Creatures.cs`, step 3g) are one node per creature on the same shared models, each
clip timed by `CreatureAnimationPlayer`, with one shared mesh and RGBF position texture per clip
(`WorldScene.VertexAnimation.cs`). The world vertex shader interpolates by each instance's phase before lighting;
control points and lightning sample individual CPU vertices, and picking lazily samples the full pose after sphere rejection.
Clip-wide bounds cover every frame while blended ordering keeps the first-frame centre. Measurements and capture scope:
[modern-rendering audit item 8](../docs/plans/2026-11-18-godot-modern-rendering-audit.md). The scene hosts the shared `SceneFrame` (`ISceneFrameHost`), so a frame runs in Unity's order:
each creature's place from `PoseInterpolator` (through `RightHandedFrame`), its clip, then its head point. A creature that dies
or leaves is removed, and an idle creature's clip swap keeps the node and restarts the clip. A gem carrier's sparkle is
requested from the frame (3k); crackle uses the shared `LightningDrawList` in that order (3l). Footsteps sound through the level's sound driver (step 4);
the possessed model's hide is 3m's (`WorldScene.Possession.cs`). Entity hover
(`WorldScene.Hover.cs`, step 3h) offers the creatures, hover-target objects, doors and traps to the shared `WorldHoverPicker`
(the 3x3 sweep as its fallback); the Hand's red pass, and the burning red, is the model's `dk2_red` instance uniform, added in the
world shader over each surface the plan reds (an alpha-blended surface takes it, with its environment pass, as one additive
next pass over the whole triangle; step 3p), and a hovered gold object swaps to its texture twin, both re-applied only
when the hover state changes. The
hover sound plays (step 4). The classic lighting (`WorldScene.Lighting.cs`, step 3i) lights every lit vertex with DK2's
tile tables, ambient keys and Hand light with ADR-0522's distance fade (native ambient, as the owner's Unity scene has it; `--light-fill` adds Godot's fill); an environment pass is folded into its submesh's
material, also over a plan, and the selection cage's box is the ribbon's, so it sorts by the ribbon's centre. Native cage expectations and comparison limits are in
[hand cursor rendering](../docs/research/hand-cursor-rendering.md#native-cage-observations).
The material model (step 3p): one world shader,
`Shaders/World.gdshaderinc` with DK2's light in `WorldLight.gdshaderinc`, in four render states (`WorldOpaque`, `WorldBlend`,
`WorldBlendDepth`, `WorldAdd.gdshader`; the blend and depth write are not uniforms), each look a `ShaderMaterial` of uniforms
(texture, colour, added light, full light, model inputs, environment pass), and an object's tint and fade, a structure's tint and the
hover's red per drawn thing as instance uniforms (`ModelInstanceLook`), so a tint, fade or red makes no material. `--light-perpixel` and
`--light-shadows` add `DK2_PER_PIXEL` and `DK2_TORCH_SHADOWS` to the shader file's text, once a run (`WorldScene.ShaderFor`); the default
light uses the resources as they ship, which the shader baker can compile at export. The cage and the wall flames are
`Shaders/Cage.gdshader`, `TorchFlame.gdshader`, `EffectSprite*.gdshader` and `StatusFlower.gdshader`; ambient occlusion
uses camera-owned compute passes ([pipeline and probes](#torch-light-and-ao-probes)). `WorldScene` takes its models and textures from the original install, which the scene requires: there is
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
the path's camera and the letterbox (plan step 3n). The Mentor's subtitle shows during a path (*Subtitles*). The level end is `CampaignResultFlow`, shared with Unity: a
level started with `--from-map` (as the campaign map launches one) runs it, so a verdict writes the profile and Space
then switches to `Main.tscn` at the session's return page, the campaign results page;
Escape after a win posts `ContinueAfterResultCommand` and then `RevealMapCommand`, and the level goes on. The won map's
title card and cutscene play as the front end opens (*Movies*); there is no win screen or tablet yet, so a won map
goes straight to them and then the results page. A level started directly (no `--from-map`) has no result flow. The options menu (`ClassicOptionsPanel` over `OptionsHud`) opens on Escape (`ClassicKeyboard` maps Godot physical keys to DirectInput scan codes, the inverse of Unity's `ClassicKeyBindings.Key`; the two tables change together) and pauses the level; its root, objective, exit and confirmation pages work, and the session action after a confirmed restart, return or quit is dropped. `--open-options [Entry]` opens it at start and refuses an entry the menu does not support (`GameOptions`, `Save` and `Load` open their pages). Its Save and Load pages are `ClassicSaveLoadPanel` over `SaveLoadHud`: the saves are listed from `user://Saves` (`%APPDATA%/Godot/app_userdata/DK2.Godot/Saves`, not Unity's `persistentDataPath/Saves`, and not created at startup) or from the directory `--saves <dir>` names, a press on a slot selects it and fills in the name, the name is typed through `ClassicKeyboard`, and Back and Continue work. Submitting works (`SessionSaver` and `SessionLoader`, shared with Unity): Save writes `<saves>/<name>.dk2g`, and Load rebuilds the chosen save's simulation and reloads the scene to continue it, from the saved turn, level and return page; a file the loader refuses shows on the page. Snapshot format 523 saves, such as Unity's under `build/`, are refused (the current format is 524). *The player's files* says what a save and a load write. The Game Options pages (root, graphics, sound, controls, keyboard) are `ClassicGameOptionsPanel` over `GameOptionsHud`: hover and press work, a slider drag holds until the left button is released, Back and Continue work, and a key is bound through `ClassicKeyboard` and the shared `OptionsPagesController.CapturePressedKey`, the controls page showing the names of `ClassicKeyNames` (Unity's `KeyCode.ToString()` names, not DK2's). The settings are read from and saved to `user://game-settings.ini` (`%APPDATA%/Godot/app_userdata/DK2.Godot/`, never Unity's file) or the file `--settings <file>` names through `GameSettingsFile`; a failed load warns and uses the defaults, and a failed save warns. They apply to the game speed, the key bindings and the HUD scale; the sound volumes and switches apply to the sound; the shadow, water, environment-mapping and invert values change and nothing acts on them yet; the scroll speed and mouse sensitivity move the camera. The full map (`ClassicFullMap` over `FullMapHud`) opens on M and closes on M, Escape or a left or right press (any other press is taken and does nothing); it draws the map image, the creature squares, the keepers' legend and the hovered room's icon over a black screen, hides the panels below it and holds the pointer. The image is `FullMapImage`, the CPU twin of `DK2FullMap.shader`, uploaded only when a tick changes a pixel. A left press moves the camera focus to the tile and stops a glide. Not wired: the camera preview at the top right (needs the 3D world). Godot's PNG decoder applies a PNG's gAMA chunk and DKII.EXE does not, so the map's palette and the front end's art decode through `PngChunks.WithoutColourSpace` and draw the stored bytes. The minimap (`ClassicMinimap` over `MinimapHud`) draws the disc, frame, markers, north sprite, corners and zoom, with the live camera's focus and heading, and, in whole-map mode, the camera footprint (`MinimapHud.SetCamera`, a `HudDrawKind.Triangles` draw cut to the disc's circle) over the disc and under the frame. The disc uses the [GPU minimap pipeline](#gpu-minimap); its rock backdrop keeps the top row first ([Q861](../docs/research/open-questions.md)). The options corner opens the options menu, a right click on a tile opens the full map, and the HUD scale follows the Game Options setting. The minimap owns the one `WorldView` and `MinimapHud`, captured once per tick, and the full map reads that grid. A left click on a tile glides the camera there. The minimap has no heading rule for the opening camera
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
and a held tool, and the order of full map, options, overview, minimap and battle tab. The camera moves (`UpdateCamera`, in Unity's order: the path, the glide, the script's follow, then `ClassicCameraInput`: keys, edges, wheel and held buttons with a y-up pointer, `PointerOverHud`, window focus): every glide (the script's, minimap, room, door, trap, battle tab, portal, Heart, fight and a message's goto) runs through `CameraGlide`; the full map and the overview jump the focus; the query follow and turn, the script's zoom and follow, the scroll speed and the panel collapse are wired. The follow runs after the frame's draw and reads the drawn creature's position (`WorldScene.TryGetCreaturePosition`), as Unity's does, so the focus moves with the model (an even step a frame, where the settled position jumped a tick at a time). Real keys, edges and wheel were exercised only by unit tests, not live in Godot. Not done: session actions. The world pointer works (plan step 3e): `WorldScene.TryPickRay` builds the ray from the placed camera pose through `MinimapCameraView.RayDirection` (Godot 4.8's `Camera3D.ProjectRayNormal` drops the frustum offset, `docs/TRAPS.md`), and `InGameHud` runs it through `TilePicker`, `TileTopPlane`, the fog test, `WorldHoverPicker.TryCastTerrain` and `HoverCursorResolver`, so a left click or release finds its tile, and tagging a wall or placing a room, door or trap posts through `WorldPointerInput` and `WorldClickCommands`. The hand cursor (`ClassicHandCursor`, canvas layer 101) is drawn with the OS pointer hidden while it shows (hiding it was not exercised); the placement cursor (`ClassicPlacementOverlay`, layer 100) draws the tool's icon, No Go and price, or the held icons; and the selection cage (`WorldScene`'s `CageRibbon`) pulses red or blue over the hovered tile. Creatures, hover-target objects, doors and traps are hover and pickup targets (`WorldScene.Hover.cs`, step 3h); the press itself was wired here. The `CancelPointerGesture` order: a
frame without ordinary input cancels only the world drag; when the window loses focus the scene releases the keys and cancels the
bottom panel's press and then the world drag; the bottom panel cancels its own press when it is freed. A held choice slot queues its release
sound request 0x136, which the mixer plays.
Options gamma uses one final scene/HUD/cursor pass; possession colour ramps remain developer-only and off by default (ADR-0822): [implementation and limits](../docs/research/possession-colour-rendering.md).
The possession canvas presents the shared HUD geometry and compositing in height-fitted widescreen content (ADR-0821), uses the map-2 minimap, and blocks ordinary panels and navigation shortcuts while the camera is held, with Options and GUI toggle available once the camera settles: [scope and limits](../docs/research/possession-hud.md).

Authored first-person swings and creature filters use the shared [foreground draw list](../docs/research/possession-foreground.md);
the one current-action target reuses [the hover pass](../docs/research/possession-target-highlight.md).

### Shot effects

`WorldScene.Effects.cs` (step 3k) draws the shared CPU particle simulation (`EffectDrawList`), wired as Unity's runner
wires it: the simulation's effect and shot requests (with the Hand's local pickup dust, `LocalPickupEffectSink`), the
KMF group parts an effect flings, one model update before the first frame, and each frame, in the runner's order
through the shared `EffectFrame` (which decides how the seeded generator's numbers fall), the loose level gems' idle
effects after the present, the Heart's mana effect on a beat and a gem carrier's sparkle from the scene frame, then the
shots' bodies and the advance. This includes possession shot 32's
creation effect102, triggered by the shot update. Entry-star visibility, staging and
native-parity limits are recorded in [Q749](../docs/research/possession-entry-effect.md).
Effects use the scene's frame clock; their lights publish before `PrepareLighting`. A sprite particle (billboard or flat)
is an instance of its sheet's batch among the blended items (`WorldScene.BlendedItems.cs`, below); a mesh particle (an
element's model, a group part, a type-6 model's frame, a shot body) is a pooled model per mesh with the element's tint
added to its light (an instance uniform), which Godot sorts by its box as an object. The dollar flower's mesh is not drawn. Native evidence: [shot effects](../docs/research/shot-effects.md).

Lightning (`WorldScene.Lightning.cs`, step 3l) uses `LightningDrawList` for shots, targets, procedural effects and clip-install crackle. It builds ribbons in camera space and writes reused render-world buffers; `Shaders/Lightning.gdshader` draws their original `Lightning` texture, unshaded, additive, without depth writes. Each ribbon section is a pooled additive item carrying its four exact corners; existing upload comparisons and geometry bounds handle changes and culling. Verification and limits: [migration plan, step 3l](../docs/plans/2026-11-15-godot-migration.md).

The blended items (`WorldScene.BlendedItems.cs`, step 3k): the effect sprites, the wall flames and the status flowers
(`WorldScene.Flowers.cs`, each layer an instance of its icon's batch) are added each frame with their distance from the
eye and the screen box of their bounding sphere (one wholly outside the frustum is dropped), and the shared
`BlendedItemOrder` places each among the blended runs by its own distance (it never crosses a run, as DK2's one
back-to-front list draws it) and cuts them into as few draws as leave DK2's pixels: an item joins its batch's latest draw
between the same runs when it moves earlier past nothing it overlaps on screen (additive items pass each other freely).
The transparent things Godot sorts by their own boxes (models with a blended or additive surface, torch holders with one,
the cage), where the frustum keeps them, are barriers at their own distance, additive when every such surface is ONE, ONE
without a depth write. Each draw is one `RenderingServer` MultiMesh instance keyed as a run is (pivot at the origin,
sorting offset the eye's distance less the key), pooled per batch. Godot 4.8 has no region update for a MultiMesh buffer,
so a draw's capacity follows its count (doubled, halved after 120 frames at a quarter or less) and an unchanged draw is not
uploaded. The exit line and the harness's `items.*` counters split the time (effects, flames, flowers, barriers, order,
upload) and the allocation after the warm-up from what loading art and growing pools took.

Possession (step 3m, `InGameHud.Possession.cs` and `WorldScene.Possession.cs`): `InGameHud` drives the shared `PossessionSession` every frame from the keys, the pointer's travel and the scene's engine clock, and the scene puts its pose on the same `Camera3D` while it holds the view (the fly-in, first person, the death view, the fly-out), with the possession cameras' near plane `PossessionCamera.NearClipDistance` (0.05; the ordinary camera keeps 0.3). The pointer lock is `Input.MouseMode = Captured` while `PointerLockWanted`, never under `--pointer-path` (the script's travel stands in; nothing warps the pointer). Virtual room-pillar roof joints are checked in a staged Treasury fixture with no pillar object states; [verification scope](../docs/research/possession-client-completion.md#virtual-room-pillar-roof). The roof includes virtual room-pillar occupancy from the same `RoomPillarLayout` used by room decoration and is one `MeshInstance3D` per 16x16 chunk (`TerrainCeilingScheduler`, `TerrainCeilingGeometry`), shown throughout the possession camera hold, including entry and return, in the terrain material on the `Ceiling` texture with anchor kind 2; the lattice latch (full-height walls, `PossessionScene.Latch`) and the hidden possessed model go through the `SceneFrame` sequence. Entry roof timing follows the [reviewed endpoint rule](../docs/research/kwd-terrain-rooms.md#ceiling-the-possession-view-roof); ordinary HUD display and possession-panel landing follow [the shared HUD rule](../docs/research/possession-hud.md#entry-and-landing). Entry/return hover and ordinary world-space badges use [the shared pointer feedback](../docs/research/possession-target-highlight.md#entry-pointer-feedback). Current capture comparisons are in [Q749](../docs/research/possession-entry-effect.md#capture-verification). Not exercised in Godot: real mouse capture, its re-capture after alt-tab, the walking keys and the death view. Remaining listener, lighting, mouse-calibration and object-death bridge boundaries: [client completion](../docs/research/possession-client-completion.md). Shot creation-effect sprites and lights are built ([adapter scope](#shot-effects)). The view's sounds play (step 4). Not done: the roof is black away from walls and torches (lighting).
Scripted cameras (step 3n) use `ScriptedViewDirector.Pose` on the drawn world's tick, including path orientation,
vertical FOV and view distance. Player camera input stays blocked during a path; `--perf-camera` keeps its explicit
harness priority. Shared `RenderCameraJitter` offsets one frame and paths suppress it. Letterboxing builds the pose
for the reduced view height, then expands its frustum into the full render target and covers the margins with bars.
This avoids another main render target and keeps the inactive projection unchanged. Picking uses that projection
and rejects the bars; SSAO and shadow views retain their own projection/depth inputs.
The full map's preview is a shared-`World3D` `SubViewport`, with `FullMapPreview.Pose` looking down, north up,
and the main camera's gamma/brightness environment. Its light view joins the publisher at distance 10. Blended
terrain and items have independent preview culling, distance keys, pools and billboard geometry; opaque geometry
and the terrain state texture stay shared. Closing the map disables preview rendering and hides its pooled draws.
Run it by naming the scene, with
optional user arguments after `--`:
`<console exe> --path godot/DK2.Godot res://InGameHud.tscn -- --level Level2 --seed 1` (defaults Level1, seed 1; add
`--glide-to x,y` to glide the camera to a tile one second in (a check; it waits for the opening path to end), `--light-probe-no-torches` and `--light-probe-shift` for frames to compare with Unity's `ClassicWorldLightingProbe` (no torch lights; the camera moved by (1, 0, 1), its shifted frames), each printing the frame each turn is first drawn on, as `--light-probe` alone does, `--light-fill` to add Godot's fill (off by default, as native ambient is in the owner's Unity scene), `--light-perpixel` for continuous fragment point lighting over DK2's sources (classic per-vertex lighting stays the default; [scope](../docs/research/godot-torch-light-artifacts.md)), `--light-shadows` with `--light-perpixel` for real torch shadows (step 2, `WorldScene.Shadows.cs`; nothing without per-pixel light), `--light-post` for filtered compute ambient occlusion before transparency ([pipeline and probes](#torch-light-and-ao-probes)), `--pointer-path "frame:x,y[:LlRr];..."` to drive the pointer from a script in place of the OS one (a check, below: keys in strictly increasing frames of this scene counted from 0, x and y in screen pixels from the top left, a straight line between keys, `L`/`l` pressing and releasing the primary button and `R`/`r` the secondary; a malformed value warns and the OS pointer is used), `--query-first-creature` to show the query details, `--possess-first-creature` to focus the ordinary camera on the viewer's first eligible creature before staging possession and its authored creation request (an unpaid check, `PossessionStaging`; [scope](../docs/research/possession-entry-effect.md)), `--stage-battle` to show the battle tab, `--stage-lightning` for the lightning fixture described below, `--open-options [Entry]` to open the options menu at an entry, `Save` or `Load` for those pages, `--saves <dir>` for the saves directory the pages list, `--save-as <name>` with `--save-tick <n>` to save under that name once the turn reaches n (a check, through the Save page), `--load <name>` to load a save of that name on the first frame, `--hash-at <n>` to print the state hash at turn n (a resumed instance ignores `--save-as` and `--load`; a `--load` run overwrites the profile file, so pass `--profile <temp file>`), `--settings <file>` for the settings file, `--profile <file>` and `--hiscores <file>` for the front end's profile and hi-score table (every scene takes all four of `--profile`, `--settings`, `--saves` and `--hiscores`, through `UserFolders`), `--open-full-map` to open the full map, `--from-map` to start the level as the campaign map launches it (it plays the profile's slot and runs `CampaignResultFlow`; the level end then writes the profile, so pass `--profile <temp file>`), `--stage-win` or `--stage-loss` to end the viewer's game after the first turn, `--leave-result` to press Space once the result is recorded, `--escape-result` to press Escape once on the result, print the commands it posted, and then press Space (the three result checks need `--from-map`; Godot prints the viewer's status changes and a camera path's start and end). `--record-frames <dir>` writes per-frame telemetry ([Probes](#probes)). `--input-journal <dir>` writes the session's input journals (`frames.journal` and `input.journal`) into a new folder there; it is Godot's only journal switch, because `DK2_INPUT_JOURNAL` is Unity's only (a globally set variable must not make every Godot run write); the header carries `source=godot`, `keys=Godot.Key` and `godot.version`. `--dk2Install <path>` names the install (*The launcher*)). An exported game cannot name a scene, so `Main.tscn` with `--level` starts `InGameHud.tscn` with the same arguments (*Exporting*).
Why the toolchain looks like this: ADR-0810. Agents working here: `.claude/agents/godot-developer.md`.

### Torch-light and AO probes
`--light-perpixel` uses a continuous fragment point-light representation; the classic vertex path remains the reference.
`--light-post` adds `WorldOcclusionEffect`, a C# compositor owned by each main/preview camera's
`WorldOcclusionHost`. `WorldOcclusionRaw.glsl` estimates AO from opaque depth, then `WorldOcclusionFilter.glsl`
filters it across depth/normal-compatible neighbours before transparent drawing. Resize and reparenting rebuild or
release the camera's resources. The owner accepts the known C# callback-wrapper cost in [TRAPS](../docs/TRAPS.md);
post-enabled rendering has no zero-allocation claim. Historical flag comparisons, focused GPU proof and current
scene/performance acceptance live in [torch-light artifacts](../docs/research/godot-torch-light-artifacts.md).

After building the Godot project, run these synthetic production-shader probes with a writable temporary root
(PowerShell continuation shown). They need no DK2 install:

```text
<console exe> --path godot/DK2.Godot --rendering-driver d3d12 --log-file <temp>/ao.log res://WorldOcclusionProbe.tscn -- `
  --occlusion-probe-out <temp>/ao --profile <temp>/profile.dk2p --settings <temp>/settings.ini `
  --saves <temp>/saves --hiscores <temp>/HiScores.dat --pointer-path "0:0,0"
<console exe> --path godot/DK2.Godot --rendering-driver d3d12 --log-file <temp>/point.log res://TorchLightProbe.tscn -- `
  --torch-probe-out <temp>/point --profile <temp>/profile.dk2p --settings <temp>/settings.ini `
  --saves <temp>/saves --hiscores <temp>/HiScores.dat --pointer-path "0:0,0"
```

Repeat with `--rendering-driver vulkan` and distinct output/log paths. Both write `results.json` and exit nonzero
on a failed check. Add `--occlusion-negative 1` after `--` in a separate AO run to bypass filtering; that control
must fail. Performance reports retain the viewport timing scope under *Measuring performance*; compare overall
frame time and allocation as well as those partial render timings.

### Anti-aliasing and render scale
Ours, not DK2's, and off by default (audit item 17): the settings file (`--settings`, `GameSettingsFile`) takes
`RenderMsaa=1|2|4|8`, `RenderSmaa=True`, `RenderScale=50..100` (percent of the window) and `RenderScaleFsr=True` (FSR 1
instead of bilinear, only below 100). Unity's `Msaa` key is separate and ignored here. No Game Options control shows them:
DK2's graphics page is kept as it was. `WorldScene.RenderQuality.cs` applies MSAA and SMAA to the main view and the full
map's preview and the scale to the main view; the torch-shadow distance maps never take them, and a default value writes
nothing, so the frames with every setting off are HEAD's. Under MSAA, `--light-post`'s filter writes its factor to its
own image and a full-screen draw multiplies it into every sample, because Godot resolves the multisampled colour again
after the transparent pass. TAA and FSR 2 are not offered: the vertex-animation phase and the in-place vertex rewrites give
no previous positions, so animated surfaces would have no motion vectors (audit item 17). Costs and captures:
[modern-rendering audit item 17](../docs/plans/2026-11-18-godot-modern-rendering-audit.md#items).

### Sound

Step 4 (migration plan). What plays is decided in `DK2.Presentation` (ADR-0805): `ClassicSoundMixer` (Unity's
`ClassicSoundPlayer`'s rules: voices and priorities, the throttle, DK2's distance mapping, the pool of 32, decode on workers,
the speech queue and its duck, the music and ambience streams, the terrain beds) and `LevelSoundDriver` (Unity's
`DungeonRunner.Sound`: the simulation's thing sounds through `WorldSoundDirector`, voices following their things, the
ambience grid around the tile under the screen's centre, the speech line handed to `ScriptedViewDirector.SetSpeechPlayback`,
the music level once a settled turn, the Heart beat, footsteps and the Casino party pop). Unity keeps its own copy until step
5. `ClassicSoundOutput` only plays: each clip becomes an `AudioStreamWav` once; an effect is an `AudioStreamPlayer3D` with
Godot's attenuation and filter off (the mixer's volume holds DK2's distance gain) under an anchor that follows the camera,
placed at its flat offset from the listener's point, so Godot pans it by the camera's turn as Unity's listener does
(panning at full strength); the speech line and the front end's sounds are plain `AudioStreamPlayer`s; the music and
ambience are 22,050 Hz `AudioStreamGenerator`s fed from their `PcmAudioQueue`s in 512-frame chunks each frame. The level's
likely clips decode on workers before the first frame, as Unity's runner waits for them. The clock is the scene's engine
time, as the rest of the scene's (a clip still decoding is heard late, so a capture's WAV can vary with the machine). `InGameHud` calls the mixer and driver in Unity's
order (decoded effects and the speech line first, the driver after the present, the panels' requests and the messages'
speech as the HUD draws, then the hover's and the cursor's); the settings' volumes and switches apply; the result flow reads
whether speech is busy. `--sound-log` prints each request as it starts (frame, turn, category, id, voice, place, sample); on `Main.tscn` it prints
the front end's (frame, clock in ms, category, id, sample) and when the title card queues its line. A Movie Maker
capture (*Verifying*) also writes the mixed audio: `<name>.wav` beside PNG frames (48 kHz, 32-bit stereo), or in an `.avi`.
Not wired: the battle fly-in's sounds and the win screen's speech (no fly-in or win screen yet).

### Movies

Step 4 (migration plan). `MoviePlayback` (`DK2.Presentation`) is Unity's `ClassicMoviePlayer`'s rules: the 0.1 s start
delay, frame n shown once the audio block after it plays (`EaMovieTimeline`), frames three behind passed over undecoded,
Esc, Enter, keypad Enter, Space or a mouse button to skip, and the end once both streams are done. It decodes the next due
frame ahead on one worker thread into a second buffer (`TqiDecoder`), and its steady playback allocates nothing.
`ClassicMoviePlayer` (Godot) only shows and plays: each new frame goes into one preallocated `Image` and `ImageTexture`,
drawn nearest-filtered at the movie's shape (each pixel doubled horizontally, `0x005AABF0`) centred on black on canvas
layer 120; the audio goes to an `AudioStreamGenerator` at the movie's rate, and the clock it hands back is what Godot's
mixer has taken (pushed, less what still waits) plus the time since that mix (not under Movie Maker, which mixes once a
frame). Nothing else is ducked, as in Unity: the menu does not run while a movie plays. `ClassicFrontEndMenu.Movies.cs`
runs Unity's `DungeonRunner.Startup` and `.Cutscene`: the title screen for 10 s, `BullfrogIntro.tgq` and `Intro.tgq` once a
process (`FrontEndStartupGate`; skipped with `-- --skip-startup` and when there is no display, `--headless`), the attract
movies after 180 s idle, the gallery's and the trailer's movies, and a won map's card, its Mentor line and
`CutSceneLevel<N>.tgq` when a `--from-map` level returns (`CampaignCutsceneRunner`), with the card's title drawn (*Subtitles*);
the card's line is decoded before the front end's first frame (`ClassicSoundMixer.PrefetchSpeech`, the runner's `TitleSpeech`),
since the front end prefetches nothing else, so it starts on the frame the card queues it. No movie shows subtitles, as in
Unity. Each
movie logs one summary line: frames shown and dropped, decode ms, how late each frame was by the audio clock and its
drift, and the adapter's main-thread ms, upload ms and managed allocation. `Main.tscn -- --play-movie <path>` plays one
movie (relative to the install) and quits; `--movie-skip-frame <n,...>` sends Esc at those frames of `Main.tscn`, through
`Input.ParseInputEvent`. Checked (2026-11-24): BullfrogIntro in Movie Maker at 1280x960 (an exact 4x2 scale; `override.cfg`
with `window/size/viewport_width` and `_height`): all 224 pictured captures equal the `DK2.Data` decoder's frame pixel
for pixel with every 4x2 block uniform, 187 of 189 frames identified in order; the capture's WAV holds the movie's audio from
0.107 s, and each frame's onset is -8 to +33 ms from its audio block (one 30 fps frame), with 5 ms drift across the movie.
Real time (headless, the editor's Debug C#): decode 8 to 13 ms a frame on the worker, the main thread 0.04 to 0.07 ms a
frame and 0.1 ms an upload, at most 88 managed bytes once after the first picture (none in Intro's 17,154 frames), frames
6.5 to 7.7 ms after due on average (at most 16.5) and 1 to 2.5 ms drift over BullfrogIntro, Intro and CutSceneLevel3. Not driven: a gallery press (the menu reads the OS pointer and has no scripted one).

### Subtitles

Step 4 (migration plan). What shows, where and when is `DK2.Presentation`'s, as Unity's runner draws it: the in-game
subtitle is `ScriptedViewDirector.Subtitle` (only while a camera path plays, timed by the speech line, or by
`SubtitleQueue` with the speech switch off) in `SubtitleLayout.SpeechBox`; the won map's title card is
`CampaignCutsceneRunner.VisibleTitle` while its black is up, in `SubtitleLayout.TitleBox` at `TitleScale`. The face is
`InterfaceFonts.LegibleFileName`'s BF4 font for the screen height, wrapped and placed by `BitmapTextBlock` (`BitmapTextLayout.Wrap`
and `BitmapTextPlacement.Place`, the wrap kept until the text, font or wrap width changes). `BitmapTextItem` draws it into one
canvas item, recorded again only when the block changes: each glyph blitted, nearest-filtered, from the font's shared
`BitmapGlyphAtlas` (the HUD's atlas, audit item 11), or, when the menu's replacement face is found (*Setting up a machine*;
`ClassicFrontEndMenuAssets.LoadReplacementFont`) and has every character of the text, as that face's glyph centred on the
bitmap glyph's ink, sized so the text's tallest glyph fills its ink height, white: the menu's own substitution
(`HudCanvas.Font.cs`), so the lines and boxes stay the bitmap layout's. Unity draws its TrueType face at its own size
(`SubtitleLayout.TrueTypePixelSize`), wrapped by the engine; Godot follows the menu instead (owner, 2026-11-24). The
in-game text is canvas layer 102 (`InGameHud.Subtitles.cs`), over the letterbox and the hand cursor and under the screen
colours; the card's is a child of the front end's cover. The win screen's text has no screen to show on yet.

### HUD text atlas, gauges and glows
Audit item 11. Set text (`HudText`) drawn one texel per screen pixel, which is every HUD text since the fonts are chosen
by screen height, is drawn as `HudTextPixels.Pieces` out of one luminance-alpha atlas per BF4 font
(`HudCanvas.Atlas.cs`), shared by every canvas, so a new hover string or a changing counter makes no texture. No two
pieces ink one pixel (where glyph ink overlaps, `Render`'s winner keeps the pixel and the other glyph is cut into
one-row runs), and nearest sampling at texel centres gives `HudTextPixels.Render`'s pixels; the pieces pass no UV clamp
(`docs/TRAPS.md`). A text stretched over a rect of another size keeps its own texture, as before. The Heart and payday
gauges are a `SubViewport` each (`HudCanvas.Gauges.cs`), refilled on the GPU by `Shaders/HudGauge.gdshader` from
`HudGaugeMask`'s per-texel ranks when the lit count changes, and sampled as the CPU-filled texture was. A glow is
baked once per label or image as two textures (the full glow and quarters 1-3's alphas in red, green and blue), drawn
through `Shaders/HudGlow.gdshader` with the second as the draw's normal map, so the hardware filters each quarter as it
filtered the four separate textures. Front-end label bitmaps stay one texture per label: they are drawn scaled and
bilinearly filtered across glyph edges, which pieces cannot reproduce, and they change only with the page. Captures and
costs: [modern-rendering audit item 11](../docs/plans/2026-11-18-godot-modern-rendering-audit.md#items).

### GPU minimap
`ClassicMinimap` presents `MinimapGpuDisc`'s reused RGBA8 `SubViewport`: a base `canvas_item` shader
samples the class grid, palette and rock, then a `MultiMesh2D` draws ordered marker quads from
`DK2.Presentation.MinimapDiscGeometry`, including the Heart trail. Class/palette textures and marker
buffers update when their inputs change; camera changes redraw the viewport without a CPU disc raster or upload.
Each covered marker quad recomposes preceding markers and rounds RGB to bytes explicitly, preserving the disc
alpha. `Shaders/MinimapMath.gdshaderinc` preserves the CPU operation boundaries and compensates division and
square root across Vulkan and D3D12. `MinimapDiscImage` remains the independent, unchanged CPU oracle.
Classification, clocks, navigation and hit testing remain shared rules. Acceptance measurements live in
[modern-rendering audit item 10](../docs/plans/2026-11-18-godot-modern-rendering-audit.md).

The synthetic GPU/CPU parity scene needs no DK2 install. From the repository root, after building the Godot project:

```text
<console exe> --path godot/DK2.Godot --rendering-driver vulkan --log-file <temp>/minimap.log res://MinimapParity.tscn -- `
  --parity-out <temp>/minimap-vulkan --profile <temp>/profile.dk2p --settings <temp>/settings.ini `
  --saves <temp>/saves --hiscores <temp>/HiScores.dat --pointer-path "0:0,0"
```

Repeat with `--rendering-driver d3d12` and a separate output directory. The scene exits nonzero on a mismatch and
writes `parity.json` (RGBA byte differences, image hashes and fractional HUD scaling) and `lifecycle.json` (reparent check).

### The launcher

`Main.tscn` decides once a process (`LauncherSettingsStore.Decide` over the shared `LauncherStartRule`): `--launcher` always
shows the launcher; a scripted run starts as before (no display, Movie Maker, a scene named on the command line, which
never loads `Main.tscn` unless it is `Main.tscn`, `--play-movie` or `--level`); otherwise Shift held at start (read from the OS on
Windows), no saved "Start the game directly next time" or no install found shows it, and a saved start goes straight
into the front end in the saved screen mode. Play saves, applies the mode (`LauncherDisplay`: a resizable window of the
chosen size centred on its screen, exclusive full screen, or Godot's borderless full-screen window) and opens the front end
as before; Quit or Esc quits, Enter is Play once an install is found. A level that ends returns to the menu without it.

The settings are `user://launcher.cfg` (`%APPDATA%/Godot/app_userdata/DK2.Godot/launcher.cfg`; key=value lines, written
only by Play), or the file `--launcher-config <file>` names, which every scripted run passes so it never reads the
player's. Their format, the install check, the window sizes offered (16:9 and common sizes that fit the screen with a
frame; first the largest 16:9 within 90%), the start rule and the words are `DK2.Presentation`'s (`LauncherSettings`,
`LauncherWindowSizes`, `LauncherStartRule`, `LauncherCopy`, `LauncherTests`); the install rule is `Dk2Install.Check`.
The install is DK2_DIR, then `--dk2Install <dir>`, then the launcher's folder while it still holds an install, then the
development default (`Dk2Install.Resolve`). The menu font is DK2_REPLACEMENT_FONT, then `--menu-font <file>`, then the
launcher's file (`MenuFontSearch`), then the gitignored copies below; empty, the menu draws DK2's BF4 bitmap font.

The launcher uses no DK2 art or lettering: its fonts are UnifrakturMaguntia (the title) and Alegreya Sans, SIL OFL, in
`Launcher/` with their licences (`THIRD-PARTY-NOTICES.md`), and its stone and seam are canvas shaders
(`Launcher/LauncherStone.gdshader`, `LauncherSeam.gdshader`). Its one unprompted motion is the seam's lava run (0.9 s)
when an install is found; `--reduce-motion`, or Windows' "Animation effects" off, lights it at once. A check drives it
with `--launcher-keys "frame:Key;..."` (Godot key names, `Shift+` for a shifted press; frames from its first), through
Godot's input, never the OS. Movie Maker keeps the window size it starts with and scales a later resize into it, so a
capture of the launcher runs a copy of `DK2.Godot` (with its `.godot` folder) outside the repository whose `override.cfg`
holds `window/size/mode=0`, `window/size/viewport_width=1100` and `viewport_height=680`, with `-- --launcher` and the
temporary player-file overrides.

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
| `godot-version.json` | the one pin: editor build and SHA-512, the .NET export templates and SHA-512, `Godot.NET.Sdk` version, Godot AI version and SHA-256 |
| `setup.ps1` | installs the pinned editor and the Godot AI addon, and with `-ExportTemplates` the export templates, verified; warns on drift from the pin |
| `editor.ps1` | starts the pinned editor on the project, from the repository root |
| `DK2.Godot/` | the Godot project: `project.godot`, `DK2.Godot.csproj` (`ProjectReference`s into `src/`), scenes, C# |
| `DK2.Godot/export_presets.cfg`, `DK2.Godot.sln` | the one export preset and the solution file the .NET export requires (*Exporting*) |
| `DK2.Godot/Main.tscn`, `Main.cs` | the main scene: the front-end menu (`ClassicFrontEndMenu`) |
| `DK2.Godot/Launcher/` | the launcher (*The launcher*): `Launcher.cs` (the page), `LauncherTheme.cs`, `LauncherDisplay.cs` (applies the screen mode), `LauncherSettingsStore.cs` (the saved file, the install, the start decision), `LauncherSystem.cs` (Shift and reduced motion from Windows), the two shaders and the OFL fonts |
| `DK2.Godot/UserFolders.cs` | `GameFolders` over `user://` with the `--profile`, `--settings`, `--saves` and `--hiscores` overrides, for both scenes |
| `DK2.Godot/InGameHud.tscn`, `InGameHud.cs` | the in-game HUD over a running campaign level and its `WorldScene` (see above) |
| `DK2.Godot/WorldScene.cs` | the 3D world: `TerrainTileBuilder`'s chunks through `RightHandedFrame`, rebuilt by `TerrainChunkScheduler` (`ITerrainChunkSink`) with shared recipe materials on the world shader (`ShaderFor`, `WorldMaterial`), the liquids waved by `WorldLiquid.gdshader` (`LiquidLayer`, `LiquidSurfaceField`), the blended runs (`BlendedTerrainStream`, `ViewFrustum`, one `RenderingServer` instance per run), the gamma-space output `Environment`, the selection cage (`CageRibbon`, `TileHoverOutline.Build`'s ribbon with Godot's own additive shader), `TryPickRay` (the pointer's ray), and the classic `Camera3D`, placed once a frame at the focus the scene moves |
| `DK2.Godot/WorldScene.ChunkUpload.cs` | the opaque terrain chunks' upload (audit item 12): `ChunkPacker`, the scheduler's `ITerrainChunkPacker`, gathers each rebuilt chunk's opaque surfaces on the build task and writes them as Godot's uncompressed vertex, attribute and index bytes (`PackedSurface`, the layout checked against a surface Godot built at open); the frame's thread hands them to `RenderingServer.MeshAddSurface`, one mesh and instance per chunk |
| `DK2.Godot/WorldScene.TerrainState.cs` | the persistent tile-state image, dirty-frame texture upload, state shader parameters and static overlay chunk batches (3p-b); mapping and state comparison belong to `DK2.Presentation` |
| `DK2.Godot/WorldScene.Objects.cs` | the objects, structures and torches (`WorldScene`, partial): shared model meshes, a node per object with per-surface looks (`ObjectMaterialLook`, one material per `ObjectSurfaceKey`, the tint, fade and red as instance uniforms), the Heart through `CreatureAnimationPlayer` and `HeartPulse`, the door tint, blueprint and lock mesh, torch meshes (`TorchGeometry`, re-bent in place, with a `CustomAabb`), flames as additive billboards, and `ZeroOrderOffset` for blended draws |
| `DK2.Godot/WorldScene.Lighting.cs` | the classic lighting (`WorldScene`, partial, step 3i): the scene's lights in a `ClassicWorldLightCache`, the visible tables published by `ClassicLightPublisher` into a float texture array (a layer per tile row, changed rows uploaded alone), the shader globals the light step in `Shaders/WorldLight.gdshaderinc` reads (per vertex, or with `--light-perpixel` per pixel over flat anchors and model transforms: the `ClassicVertexLight` copy, the optional fill and the distance fade), the ambient keys and the Hand's light; options gamma is owned by the final [screen-colour pass](../docs/research/possession-colour-rendering.md) |
| `DK2.Godot/Shaders/` | the world shader (`World.gdshaderinc`; DK2's light `WorldLight.gdshaderinc`, pinned by `ClassicVertexLightShaderTextTests`; the shadow lookup `WorldShadows.gdshaderinc`; the possession fade and visibility includes) in its four render states, the cage, the wall flame (a texture array), the effect sprite in its three blends (`EffectSprite.gdshaderinc`) and the status flower |
| `DK2.Godot/WorldScene.Effects.cs` | the effects (`WorldScene`, partial, step 3k): `EffectDrawList` and its request wiring, sprite sheets as blended-item batches, mesh particles as pooled models per mesh |
| `DK2.Godot/WorldScene.BlendedItems.cs` | the blended items (`WorldScene`, partial, step 3k): batches of MultiMesh draws ordered among the blended runs by `BlendedItemOrder` |
| `DK2.Godot/WorldScene.Flowers.cs` | the status flowers (`WorldScene`, partial, step 3k): each layer an instance of its icon's batch among the blended items |
| `DK2.Godot/WorldScene.PostEffects.cs`, `WorldOcclusionHost.cs`, `WorldOcclusionEffect.cs` | `--light-post`: attaches camera-owned C# AO compute passes after opaque drawing and before transparency; main/full-map preview resources follow camera tree entry/exit ([pipeline](#torch-light-and-ao-probes)) |
| `DK2.Godot/WorldScene.RenderQuality.cs` | the settings' MSAA, SMAA and render scale on the main view and the preview ([anti-aliasing](#anti-aliasing-and-render-scale)) |
| `DK2.Godot/WorldScene.Shadows.cs` | `--light-shadows` (step 2 of the lighting direction): the two torches nearest the focus (`ClassicTorchShadowSlots`) each render a distance map from a downward 150-degree `SubViewport` camera that sees only the casters' layers (terrain chunks, creatures, objects, structures); a wall's shadow multiplies only that torch's DK2 term, a creature's or object's darkens the whole per-pixel light by DK2's strength (1 - coverage x strength), with the caster fade from `ClassicShadowSourceSelector` (DK2's three nearest torches within 2.5 tiles, fading from the fourth); a torch holding a map counts 1.5 tiles nearer, so the maps do not swap back and forth |
| `DK2.Godot/WorldScene.Creatures.cs` | the creatures (`WorldScene`, partial, the `ISceneFrameHost` of `SceneFrame`): a `ModelSlot` per creature on the shared model meshes, clips through `CreatureAnimationPlayer` with in-place vertex upload, clip sets cached per entry, the drawn place and head point through `RightHandedFrame`, removal on death or exit; crackle goes through `WorldScene.Lightning.cs` (3l); footsteps go to the sound driver (step 4); the gem sparkle is `WorldScene.Effects.cs`'s |
| `DK2.Godot/WorldScene.Possession.cs` | the possession's drawn world (`WorldScene`, partial, step 3m): the camera's possession pose and near plane, the lattice latch and the hidden possessed model before the present, and the roof (`TerrainCeilingScheduler` into one `MeshInstance3D` per chunk, shown during entry, first person and return by the shared possession-camera hold rule) |
| `DK2.Godot/WorldScene.Lightning.cs` | shared lightning ribbons submitted through the additive blended-item pool, with geometry bounds; clip-install crackle runs in `SceneFrame` order |
| `DK2.Godot/WorldScene.PossessionVisibility.cs` | possession tile visibility; scope and verification: [possession-warp section 8.3](../docs/research/possession-warp.md#83-the-enumeration-strongly-inferred-whole-disassembly-of-0x005737e0-and-0x00572f60) |
| `DK2.Godot/WorldScene.PossessionRendering.cs` | the world-shader distance fade; scope and verification: [possession-warp section 4](../docs/research/possession-warp.md#4-the-possession-distance-fade-strongly-inferred-implementable) |
| `DK2.Godot/InGameHud.Possession.cs` | the possession's host (`InGameHud`, partial, step 3m): the shared `PossessionSession` driven every frame from the keys, pointer travel and engine clock, the pointer lock (`Input.MouseMode`, never under `--pointer-path`), `--possess-first-creature` |
| `DK2.Godot/InGameHud.PossessionForeground.cs` | submits the engine-free authored swing triangle list; [scope](../docs/research/possession-foreground.md) |
| `DK2.Godot/InGameHud.PossessionHighlight.cs` | submits the shared current-action target to the existing hover pass, and captures the status flowers (`StatusFlowers`) for `WorldScene.DrawFlowers` while the ordinary HUD shows; [scope](../docs/research/possession-target-highlight.md) |
| `DK2.Godot/WorldScene.Hover.cs` | entity hover (`WorldScene`, partial, step 3h): `PickHoverEntity` offers the drawn creatures, hover-target objects, doors and traps to `WorldHoverPicker` (then its 3x3 sweep); `DrawHoverOverlay` and `CommitHoverOverlay` set each hovered `ModelSlot`'s red pass (`HoverOverlayPlan`, `ObjectMaterialLook.HoverOverlay`) and gold twin; the burning red is the same pass |
| `DK2.Godot/ClassicHandCursor.cs` | the hand cursor: `HandCursorHud`'s quad drawn by `HudCanvas` on canvas layer 101 from `InstallHandCursorSheets`, the OS pointer hidden while it shows |
| `DK2.Godot/ClassicPlacementOverlay.cs` | the placement cursor: `PlacementCursorHud` drawn by `HudCanvas` on canvas layer 100 (the tool's icon, No Go, price, held icons) |
| `DK2.Godot/ScriptedPointerGate.cs` | drops OS mouse events while a `--pointer-path` script runs, so a capture never takes the owner's mouse |
| `DK2.Godot/ClassicSoundOutput.cs` | Godot's `ISoundOutput` (*Sound*): pooled `AudioStreamPlayer3D`s beside the camera, the speech line, and the music and ambience `AudioStreamGenerator`s fed from their queues; converted clips kept |
| `DK2.Godot/PerformanceReport.cs` | the performance harness (`--perf-report`, below): per-frame samples into `DK2.Presentation`'s `FrameRecording`, the `--perf-camera` focus script (`CameraScript`), the exit table and JSON |
| `DK2.Godot/FrameRecorder.cs` | `--record-frames` (*Probes*): `FrameTelemetry`'s rows and `metadata.txt`, written when the scene leaves the tree |
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
| `DK2.Godot/ClassicMinimap.cs` | the minimap: `MinimapHud` drawn by `HudCanvas` bottom-docked over `MinimapGpuDisc`'s viewport texture, hit-tests corner and map presses for the router without taking them from the panels under it |
| `DK2.Godot/ClassicMessageTabs.cs` | the message tabs, message and objective panels, indicators: `MessageTabsHud` drawn by `HudCanvas`, bottom-docked, mouse forwarded |
| `DK2.Godot/HudCanvas*.cs` | the one `HudFrame` renderer every panel draws through |
| `DK2.Godot/BitmapGlyphAtlas.cs` | one luminance-alpha glyph atlas per BF4 font, shared by the HUD canvases and the subtitles |
| `DK2.Godot/BitmapTextItem.cs`, `InGameHud.Subtitles.cs` | the subtitles and the title card's text (*Subtitles*) |

## Setting up a machine

Needs the .NET 10 SDK (`global.json`; Godot 4.8 requires .NET 10) and, for Godot AI,
[uv](https://docs.astral.sh/uv/getting-started/installation/).

```powershell
powershell -File godot/setup.ps1     # editor into %LOCALAPPDATA%\Programs\Godot\<version>\, addon into DK2.Godot/addons/godot_ai/
powershell -File godot/editor.ps1    # open the editor
```

The game reads the original install in place: `DK2_DIR`, else the launcher's choice, else the usual install locations
(`DK2.Data.Dk2Install`, *The launcher*); the in-game scene needs it for its models and textures as well as the HUD art. The front end's replacement TrueType face is Bullfrog's DungeonKeeperII.ttf, which is not
ours to ship (download: see `unity/README.md`). It is read from an absolute path, never imported:
`DK2_REPLACEMENT_FONT`, else `--menu-font`, else the launcher's choice, else `DK2.Godot/Fonts/DungeonKeeperII.ttf` (gitignored), else the Unity project's
gitignored copy. Without it the menu draws DK2's BF4 bitmap font.

`-InstallRoot` or `DK2_GODOT_ROOT` puts editors elsewhere. Each version gets its own folder, so
an upgrade never touches the version you are leaving.

## Exporting

`setup.ps1 -ExportTemplates` installs the pinned .NET export templates (about 1.2 GB; SHA-512 pinned and listed in the
release's `SHA512-SUMS.txt`) into `%APPDATA%\Godot\export_templates\4.8.beta3.mono`, where the editor looks.
`DK2.Godot/export_presets.cfg` has one preset, *Windows Desktop*: x86_64, the PCK beside `DK2.exe` (not embedded, so it
can be listed and hashed on its own), no .NET debug symbols, every resource but `addons/` (agent tooling; Godot AI strips
its autoload from an export), `Fonts/` (the gitignored DungeonKeeperII.ttf) and the probe scenes, plus the launcher
fonts' OFL texts. The game reads every DK2 file from the install at run time, so the pack holds none;
`tools/tester-package/pck_contents.py` lists a pack and refuses one that could. The .NET export needs `DK2.Godot.sln`
beside the project and, without it, still exits 0 (`docs/TRAPS.md`). `tools/tester-package/build.ps1` is the recipe
(README there). Godot's release template refuses a scene on the command line, so an exported run starts a level with
`-- --level <name>` and the same user arguments as `InGameHud.tscn`.

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

Possession colour ramps normally stay off. For developer inspection only, add
`--possession-colours` after Godot's `--`; no player setting exposes it. This is a
remake test path, not evidence of native gamma enablement. [Policy and capture
comparison](../docs/research/possession-colour-rendering.md).

```powershell
dotnet build godot/DK2.Godot/DK2.Godot.csproj                               # compile, no editor needed
powershell -File godot/editor.ps1 -Console --headless --import               # import; errors print here
powershell -File godot/editor.ps1 -Console --headless --build-solutions --quit
powershell -File godot/editor.ps1 -Console --headless --quit-after 10        # run the main scene
```

The last prints `DK2.Godot front end: page Main` once the menu is up; with `res://InGameHud.tscn` added it runs the
in-game HUD instead and prints `DK2.Godot in-game HUD: Level1 seed 1`. `--headless` has no renderer; to see a
frame, run the console build without it, e.g. `--path godot/DK2.Godot --log-file <dir>/godot.log --write-movie <dir>/f.png --fixed-fps 30
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

To capture the opening path, start Level1 without `--load`, with Movie Maker and a scripted pointer. To capture the
preview, load a save after the path and add `--open-full-map`; place the pointer over explored territory on the map.
Both pass `--log-file <temp>/godot.log` before `--` and the same temporary player-file overrides after it:
`-- --level Level1 --seed 1 --settings <temp>/settings.ini --profile <temp>/profile.dk2p --saves <temp>/saves --hiscores <temp>/HiScores.dat --launcher-config <temp>/launcher.cfg --pointer-path "0:960,540"`.
For the preview append `--load <name> --open-full-map` and use map-area pointer coordinates; repeat with
`--light-perpixel --light-shadows --light-post` for the lighting check. Keep Movie Maker at 1920x1080 as above.
The optional lightning fixture is `--stage-lightning` on `InGameHud.tscn`, normally with `--level Level1 --seed 1`. It places a Mistress and an opposing hero on dry owned floor, activates three Increase Level specials and a Mana Boost through ordinary commands, and lets combat produce the creature cast and electrification. After thirty turns it posts one ordinary paid Thunderbolt at a fresh target. It turns the ordinary camera through half a turn to face the hero. It is off by default; no simulation rule changes. Add `--possess-first-creature` for the possession-camera check. Use all four temporary player-file overrides and `--pointer-path` as above.

## Measuring performance

Every rendering change is measured (migration plan step 3o). `--perf-report <file>` on `InGameHud.tscn` adds
`PerformanceReport`; without it nothing is added. After `--perf-warmup <n>` frames (120) it samples each frame into a
preallocated buffer: the frame interval, the process time (its first process callback to `PerformanceReportTail`, the
last), the main viewport's CPU and GPU render time (plus the full-map preview while open; the sparse minimap and
existing torch-shadow viewport times are excluded), frame setup, draw calls, objects, primitives, video, texture and buffer
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
`python godot/perf_targets.py <dir>/<label>` holds one label's reports to the harness ceilings in
`tools/tester-package/performance-target.json` (Level1, Level7 and Level6a at 1080p on the floor machine, each with its
source; exit 1 over one). Run perf.ps1 with `-Levels Level1,Level7,Level6a -Sizes 1920x1080` for it. The recipe passes
its own launcher file with the other player files.

Texture arrays remain deferred after measurement; see [modern-rendering audit item 4](../docs/plans/2026-11-18-godot-modern-rendering-audit.md#items).

## Probes

`--record-frames <dir>` on `InGameHud.tscn` is Unity's release-player recorder (`-recordFrames`) and the game probe's
`frames.csv`: one row a frame in their columns (`FrameTelemetry` in `DK2.Presentation`, `FrameRecorder.cs`), written with
`metadata.txt` when the scene leaves the tree, never over an existing capture; without it there is no node. Godot ticks on
the frame's thread at the frame's top, for the interval that just ended (Unity's inline order; `physics_jitter_fix` is 0
outside Movie Maker, `docs/TRAPS.md`), so `wait_tick_ms` is 0 and a row's ticks are drawn in the same row; `hud_ms` also holds the hover and the blended items' order; `cam_*` is the
drawn eye in the renderers' frame, as Unity's. Run it in real time (no `--fixed-fps`, which decouples the draw clock from
the wall clock), e.g. `--headless --max-fps 60 --quit-after 1500 res://InGameHud.tscn -- --level Level1 --seed 1
--record-frames <temp>/rec` with the temporary player files, then `python tools/game-unity-probe/draw_clock.py
<temp>/rec/frames.csv`. `tools/tester-package/summarise_capture.py` reads it under `recorder/` and rates a capture valid
only from the release export (its development, backend, API and resolution checks; *Exporting*). Headless, the metadata has
no GPU and a 64x64 resolution, which it rates invalid.

The Unity probes (migration plan step 4), by verdict. *Covered*: Godot has its own check; *obsolete*: a Unity-only check
or reference whose purpose ended; step 5 deletes them.

| Unity probe | Produced | Verdict |
|---|---|---|
| `FrameRecorder`, `tools/game-unity-probe` (`frames.csv`, `draw_clock.py`) | per-frame timing, the fly-by's draw clock | ported: `--record-frames` |
| game probe's summary, `-Benchmark`, `-CaptureFrom` | frame percentiles, GPU ms per settings variant, a Unity Profiler capture | covered: `--perf-report`, `perf.ps1 -Extra`, `perf_compare.py` |
| `Level8MotionProbe` | the opening fly-by's poses | covered: `--record-frames` and `draw_clock.py`, its metric |
| `ClassicWindowFrameProbe`, `ClassicRotationProbe`, `Portal*Performance`/`Profile`/`CpuProfileReport`, their summarisers | windowed frame, rotation and arrival timings | covered: `perf.ps1` (`--perf-camera`); the PresentMon FPS gate is the tester package's item |
| `ClassicCrowdProbe` (+ `summarize.py`), `tools/classic-animation-probe` | CPU vs VAT crowd costs | obsolete: Godot animates on the GPU (audit item 8); the animation probe needs no Unity |
| `ClassicCameraReferenceProbe` (+ `compare-camera.py`) | frames and wall markers against the native recording | covered: Godot draws `ClassicCamera`'s pose (`ClassicCameraTests`, `RightHandedFrameTests`; 3a's focus at y 453.37 of DK2's 453.375); the measurement is in [classic camera](../docs/research/classic-camera.md) |
| `ClassicWorldLightingProbe`, `ClassicStartupCaptureProbe`, `ClassicLiveCameraAudit` | Unity reference frames, timed startup views | obsolete: 3f, 3g and 3i are accepted; Movie Maker with `--light-probe*` and `--glide-to` captures any turn |
| editor `ClassicLighting*`, `ClassicTileLight`, `ClassicTagMaterial`, `ClassicSightFog`, `ClassicShadow`, `ClassicLightning` probes, `ClassicAllocationCalibration`, `ClassicLookSetup` | GPU checks of Unity's shaders, URP setup, Unity's allocation meter | obsolete; Godot's: `TorchLightProbe.tscn`, `WorldOcclusionProbe.tscn`, `ClassicVertexLightShaderTextTests`, `--stage-lightning`, the harness's `harness.selfBytes` |
| `TagOverlayOrderProbe`, `TransparentTerrainReuseProbe`, `HoverOutlineFiniteProbe` | blended order, run reuse, finite cage output | covered: `BlendedItemOrderTests`, `BlendedRunSlotsTests`, `BlendedTerrainStreamTests`, `Cage.gdshader`'s clamp |
| `ShotEffectsProbe`, `HeartDestructionProbe`, `PortalEntranceProbe`, `PortalCrystalMovementProbe`, `CreatureLevelEffectProbe`, `TutorialHighlightProbe`, `RoomLifeProbe` | effect and room captures | covered: the shared effect and draw-list tests, Movie Maker captures of `InGameHud.tscn` |
| `SpellSlotStateProbe`, `HudComparisonProbe`, `ScaledIntegrationProbe` (still failing in Unity) | panel state, HUD captures at two sizes, HUD scale | covered: `BottomPanelHudTests`, `HudScaleLayoutTests`, `HudPointerMapTests`; step 1 compared Godot's HUD with those captures |
| `FrontEndSessionProbe`, `FrontEndMenuProbe`, `CompletionFlowProbe`, `SessionSaveLoadProbe`, `Options*Probe`, `InGameOptionsProbe`, `DungeonSetup.SmokeTest`/`SaveSmokeTest`, `ClassicMovieMenu`, `FrontEndPreview` | front end, flow, save/load, options, smoke runs, movie, editor jumps | covered: the `Main.tscn` and `InGameHud.tscn` runs (*Verifying*), `--from-map`, `--stage-win`, `--save-as`/`--load`/`--hash-at`, `--open-options`, `--play-movie` and their `DK2.Presentation` tests |
| `tools/hud-probe` | UI Toolkit pilot | obsolete; deleted |

`capture-native-render-inputs.py` reads DK2's process, not Unity, and stays a tool.

## Upgrading Godot

1. Take the new build's file name and SHA-512 from the release's `SHA512-SUMS.txt`
   (`github.com/godotengine/godot-builds/releases/<version>`), the same for its
   `..._mono_export_templates.tpz` (with the templates' folder name, their `version.txt`), and the matching
   `Godot.NET.Sdk` version from NuGet. Update `godot-version.json`.
2. Run `setup.ps1`, then `editor.ps1`. On a new minor version the editor offers its project
   upgrade tool and rewrites the `Sdk` in `DK2.Godot.csproj` and `config/features` in
   `project.godot`; accept it. `setup.ps1` warns until the csproj and the pin agree.
3. Run the verification above and commit the pin with whatever the editor rewrote, nothing else.
4. Read the release's *breaking changes* list (the `breaks compat` label on the milestone).

Godot AI: press **Update** in its dock (signature-checked, re-pins both clients), then set
`godotAi` in `godot-version.json` to the new version and archive SHA-256. Never extract a release
over an existing `addons/godot_ai/`.
