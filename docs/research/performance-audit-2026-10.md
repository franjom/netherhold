# Performance and leak audit — October 2026

Static audit of master `7281a019`, 2026-10-24. Analysis only: no game code,
tests, player, replay, soak or profiler runs changed or executed in phase 1.
Line references below refer to that commit. No FPS or millisecond gains have
been measured here. CONFIRMED means the code path/cost mechanism was read;
its importance to actual frame time remains SUSPECTED until measured.

Path prefixes: `U` = `unity/DK2.Unity/Assets/DK2/`, `P` =
`src/DK2.Presentation/`, `S` = `src/DK2.Simulation/`, `A` = `src/DK2.AI/`.
Gain estimates are conditional work reductions, not promised FPS percentages.
Risk `V` concerns visuals/input/presentation timing; `H` concerns hashed state,
simulation ordering or RNG. Every simulation proposal must preserve HashVersion 299
(`S/GameSimulation.cs:23`), the determinism pin, ordered events and RNG state.

## Executive summary: top ten by expected impact

| Rank | Finding | Expected benefit and when |
|---|---|---|
| 1 | R1 shared transparent terrain/effect rebuild | Potentially large CPU/upload reduction while mist/effects animate or the camera rotates |
| 2 | C1 animation sampling/upload | Potentially large crowded-scene CPU reduction; skip only provably identical poses |
| 3 | T1 worker join | Potentially large p99 benefit if the next frame actually waits; measure before redesign |
| 4 | L1 native asset teardown | Reclaim a level's runtime art on unload; primary lifetime defect |
| 5 | L2 material/phase cache growth | Bound retained memory and first-use material creation during effects/fades |
| 6 | C2 hover entity picking | Reduce entity scans and triangle reads when the pointer is over the world |
| 7 | S1 imp selection scans | Potentially large tick-tail saving with many idle/searching imps; high fidelity risk |
| 8 | S2 room queries/floor slots/furniture | Remove repeated transient containers and room work from active ticks |
| 9 | S3 diagnostic event strings | Reduce tick garbage, especially effect-heavy ticks and unobserved harness runs |
| 10 | R2 sparse lighting uploads | Reduce upload bandwidth when distant dirty tiles span a large buffer interval |

L3's smaller teardown omissions should accompany L1's ownership audit in a
separate fix. Cheap allocation fixes C3/C4 can precede the larger ranked work.
The reported six persistent allocations have **no established attribution**.

## Unity per-frame CPU and GC

**C1 — CONFIRMED: resample and upload every posed actor.**
`U/ClassicCreatureAnimation.cs:93,120,130`; `U/DungeonRunner.cs:622`.
Each draw samples every vertex, converts it, calls SetVertices and recalculates
bounds even when the resulting pose is unchanged. Cost is O(total animated
vertices/frame), roughly 12 bytes/vertex of position payload per upload, plus
sampling, copies and bounds work. Arrays are reused; this is primarily CPU and
upload work, not a new managed vertex array every frame. Clip replacements
also clone up to three meshes and allocate sample arrays (`:66`). Proposed:
cache the full effective pose/resource revision and skip identical output;
then evaluate tighter upload/bounds handling. Gain: eliminate those operations
on held/identical poses; moving actors still need them. Risk V: control points,
Heart beats, bounds/culling and animation clocks must still advance correctly.
No H; do not reduce animation cadence or extrapolate motion.

**C2 — CONFIRMED: hover picking scans all actors before local fallback.**
`U/DungeonScene.cs:185,192,214,310`; `U/DungeonRunner.cs:1212,1226`.
Each eligible frame offers creatures, objects and structures; sphere/bounds
survivors copy mesh vertices and every submesh's indices into reused lists and
test triangles. A miss can scan creature/object poses nine more times in the
3x3 fallback. Cost O(entities + candidate triangles), with list growth at new
high-water marks. Environment submeshes repeat base triangles in this test.
Proposed: spatial candidate index preserving existing offer/tie order; retain
immutable topology and update animated vertices by mesh revision; avoid testing
duplicate environment triangles. Gain: potentially large in crowded levels.
Risk V: exact hit, eligibility, occlusion and fallback ordering; no H. Cached
delegates already exist (`DungeonRunner.cs:1223`); local functions alone are
not evidence of a heap closure allocation.

**C3 — CONFIRMED: small presenter containers allocated every frame.**
`P/ScenePresenter.cs:359,387,558`: PresentStructures creates a HashSet and List
even with no structural change. Work scales with structures, not map area.
Proposed: reuse cleared per-presenter scratch, preserving sorted removals.
Gain: at least two container objects/frame plus populated backing storage
removed; low whole-frame benefit alone. No H; low V if call order is retained.
Object/creature passes also enumerate every Present; gate only after measuring
and identifying all between-tick preview/visibility inputs.
Fixed (2026-10-24): structure seen/gone scratch reused, removals sorted by a shared comparer; test pins the calls.

**C4 — CONFIRMED: placement price formatting and digit keys per frame.**
`U/ClassicPlacementOverlay.cs:97,101,109`: an active priced preview formats its
number and concatenates a texture key per digit on every draw, despite cached
textures. Proposed: retain digits and texture references until price/family
changes, as `ClassicResourceHud.cs:223` already does. Gain: one number string
and up to MaxDigits key strings per applicable frame. Low V; no H.
Fixed (2026-10-24): digit textures retained until price/family changes; no number string per frame.

**C5 — SUSPECTED: redundant retained-UI layout work.**
`U/ClassicBottomPanel.cs:181,188,215,239`; `U/ClassicMessageTabs.cs:227`;
`U/ClassicResourceHud.cs:229`. Visible frames write many positions, display
states and tints. Unity may suppress equal-value changes; source does not prove
layout/repaint cost. Proposed after profiling: separate size/content/hover
revisions from genuinely animated changes. Gain unknown, probably modest unless
UI Toolkit markers dominate. Risk V: resize, pinned panels, flashing and scrolling;
no H. Do not recreate the UI: elements are already retained.

**C6 — CONFIRMED: flowers capture all creatures and submit separate layers.**
`P/StatusFlowers.cs:371,390,448`; `U/ClassicStatusFlowers.cs:51,64,78`.
Every frame observes all creature health and computes shown flowers; torture
flowers can construct floor-slot containers. Each shown flower submits its
centre plus optional ring, Imprison and objective: 1–4 RenderMesh calls.
Property blocks are retained at peak layer count (`:93`), not made per sprite.
Proposed: cache tick-stable facts while continuing frame-time cycles/hover,
then measure atlas/batching options preserving layer order. Gain scales with
creatures and displayed layers. V: countdown, torture lift, blink and concentric
icons; no H. Health/centre/hover dictionaries are pruned (`StatusFlowers.cs:510`).

**C7 — SUSPECTED: repeated camera/component lookups.**
`U/DungeonRunner.cs:631,634,637,1218`; `U/DungeonScene.cs:245,560,599,615`.
These occur per frame or per actor; no Find-object scan was found on the main
Update path. Proposed: cache the active camera and actor components with proper
replacement invalidation. Gain likely below geometry/scanning work; measure in
a player because editor GetComponent allocation differs. Low V; no H.

**C8 — CONFIRMED: tile gating still scans and revisits the map.**
`P/ScenePresenter.cs:397,406,409,413,420,449`: every changed tick forces a
whole-map TileView pass and tile snapshot copy, even without terrain changes.
Otherwise gating scans objects for crypts and compares the tile bytes; previews
and highlight phases also invalidate it. Proposed: measure this separately from
chunk rebuilding, then cache complete visual-input revisions or dirty tiles.
Gain: fewer O(map area) passes/copies, potentially material on larger maps.
V: tags, fog, doors, crypts, room facts and animated highlights must stay current.
H risk if adding mutation counters to simulation records; keep derived tracking
outside hashed state and preserve all existing between-tick updates.

## Leaks and long-session degradation

**L1 — CONFIRMED: no explicit teardown for the runtime art owner.**
`U/ClassicMeshes.cs:31,298,390,509,715`; `U/DungeonRunner.cs:283,487`.
ClassicMeshes retains built/shared meshes, textures and material variants but
has no asset-release method. Runner destroys HUD/light resources and disposes
InstallArtProvider; that managed provider does not destroy Unity art objects.
Managed GC alone does not release their native allocations. Proposed: explicit
idempotent ownership registry/disposal, after actors/effects stop using assets;
deduplicate shared ownership rather than destroying every dictionary value.
Gain: reclaim all owned runtime art at unload, magnitude requires snapshots.
No H; V risk from destroying shared/live meshes or Resources-owned assets.
Automatic unused-asset cleanup can hide the gap; measure both natural unload
and retained references before treating it as permanent growth across scenes.
Fixed (2026-10-24): `ClassicMeshes.Release` destroys only what it created (deduplicated registry), run by
Runner.OnDestroy after effects and `DungeonScene.Dispose`. Unity play/stop/reload check pending.

**L2 — CONFIRMED retention; growth rate SUSPECTED: variant caches.**
`U/ClassicMeshes.cs:124,443,451,635,642`;
`U/ClassicConstructionEffects.cs:144,148`.
New effect RGB values make native material copies keyed by (material, RGB),
fades retain variants by alpha, and Heart arrays are keyed by raw phase rather
than selected texture indices. No eviction until the owner dies; even then L1
applies. SetAlpha also allocates a Material[] on each nonzero partial-alpha
call. `_opaqueOf` retains GameObject keys until restore/full alpha, without a
destruction callback. Proposed: per-draw tint/alpha properties where equivalent,
canonical texture-index keys, explicit Forget on removal and safe bounded
variant ownership. Gain: avoid one material per distinct tint/alpha and redundant
phase arrays; actual palette/phase diversity needs a soak. V: blend, depth,
emission and batching equivalence; no H. Do not quantize colours to save memory.
Partly fixed (2026-10-24): SetAlpha reuses a scratch Material[]; the cache redesign remains open.

**L3 — CONFIRMED: smaller generated-resource teardown omissions.**
`U/LiquidSurfaces.cs:65,81,112`: Clear destroys layer objects but not layer.Mesh;
rebuild does destroy the previous mesh. `U/TerrainChunks.cs:232` creates opaque
chunk meshes; Clear releases them (`:156,187`), but Runner.OnDestroy only calls
DisposeLightResources, not scene.Clear. Chunk GameObjects have no matching
mesh-destroy component. `U/DungeonScene.cs:1099` creates placeholder materials
without release. `U/DungeonRunner.cs:1051` disposes a previous subtitle bitmap
atlas on size change but `:487` omits final disposal. Proposed: one scene-owned
teardown path plus liquid/atlas cleanup, ordered before shared-art release.
Gain: generated mesh/material/atlas memory per unload, no steady-state FPS
promise. No H; low V with correct ownership and destruction order.
Fixed (2026-10-24): `DungeonScene.Dispose` (Clear, lights, placeholders) on final teardown; liquid and chunk meshes freed; atlas disposed.

**L4 — CONFIRMED negative evidence, warning remains SUSPECTED.**
A source sweep of `src/**/*.cs` and `unity/DK2.Unity/Assets/**/*.cs` found no
Allocator.Persistent use or owned NativeArray/NativeList allocation. The movie's
GetPixelData view (`U/ClassicMoviePlayer.cs:236`) is texture-backed, not a new
owned allocation. GraphicsBuffers exist and have Dispose paths in the minimap
and all three lighting buffer classes. This does not clear Unity/packages of
leaks, prove exception-path cleanup, or explain “6 individual allocations”.
Proposed: capture allocator call stacks during reload/Play transitions and
compare an empty matching URP project only if needed. Gain unknown; no H/V
change authorized from this warning alone. Do not disable leak detection.

**L5 — CONFIRMED bounded/pruned paths; monitor rather than rewrite.**
`U/DungeonScene.cs:294` caps pick-sphere entries at 4096 (a clear causes a later
refill, not unlimited growth). `P/ClassicWorldLightCache.cs:130,186,266` removes
absent sources/tables and clears state. `U/TransparentTerrain.cs:125,126,408`
unsubscribes camera callbacks; `DungeonRunner.cs:497` unsubscribes Heart health.
`ClassicMoviePlayer.cs:274` joins decode and destroys texture/audio clip.
List/block/run capacity retained at a peak is different from leaked live objects.
Proposed: record capacity versus active counts in phase 2 before adding trimming.
Gain unknown; trimming every frame can worsen GC. No H; lifecycle V risk.

## Rendering and GPU submission

**R1 — CONFIRMED: changing effects invalidate shared transparent geometry.**
`U/TransparentTerrain.cs:65,75,79,240,246,264,290`;
`U/ClassicConstructionEffects.cs:257,266`.
Effect meshes are read back into CPU lists each frame. Changed transform/vertex
data dirties the shared stream: Collect walks terrain and effects; global
triangle ordering runs, and geometryChanged invalidates retained run topology
so runs upload again. Camera rotation also sorts, with exact-run reuse when
geometry is unchanged. Cost O(total shared vertices + triangles), plus sorting
and run uploads, even for a small moving effect. Proposed: retain static
geometry/topology separately, merge dynamic triangles into the identical depth
order and update only affected runs. Gain potentially high in active effects;
measure Sort/Order/Upload markers. V high: global transparent ordering is a
fidelity requirement; separating queues or dropping mist is not an acceptable
shortcut. No H. Headless probe does not execute this camera callback. Current implementation and measurements: [portal arrival performance](portal-arrival-performance.md).

**R2 — CONFIRMED: lighting dirty ranges include unchanged gaps.**
`U/ClassicTileLightBuffer.cs:65,93,97`; `ClassicTileFillBuffer.cs:45`;
`ClassicTorchLightBuffer.cs:65`. Updates already compare values. Flush uploads
the inclusive first-to-last dirty range, not necessarily the whole buffer, but
two distant changes can approach a full upload. Tile source entries are 32 bytes
times 24 sources = 768 bytes/tile, plus 4 count bytes: a 128x128 full interval
is about 12 MiB source data (and a managed mirror), before other buffers.
Proposed: measure dirty count/span/upload bytes; coalesce several dirty spans
only when saved bytes outweigh extra SetData calls. Gain proportional to gaps;
no gain for dense changes. V: no stale lighting/global-buffer disposal; no H.

**R3 — CONFIRMED extra geometry; GPU bottleneck SUSPECTED.**
`U/ClassicMeshes.cs:303,313,316,369`: each eligible shiny material adds a submesh
with the same triangles and an additive environment material. Submission and
vertex/fragment work increase with shiny visible submeshes, alongside flowers,
shadows and effect draws. Actual per-frame batches require Frame Debugger.
`Resources/Shaders/ClassicVertexLit.shader:38–49` declares instancing plus seven
binary axes and one four-way shadow axis: 512 combinations before instancing,
platform/stage handling and stripping; this is not a measured compiled count.
Proposed: inspect actual draws/variants, preserve shine, and evaluate equivalent
batching/variant retention. Gain unknown; high V for pass fusion or stripping.
No H. Transparent/additive no-depth-write passes also create overdraw; assess
in a rendered player at 720p versus 1440p before blaming CPU or disabling art.

**R4 — CONFIRMED existing minimap gates; remaining whole-grid CPU scan.**
`U/DungeonMinimap.cs:204,207,231`; `U/MinimapGpuMap.cs:117,126,141,184`.
Capture skips an unchanged tick/preview; changed captures scan all map tiles
but upload only changed classes. Markers upload on captured changes; disc render
is skipped for an unchanged view/dirty flag. Proposed only if measured hot:
track complete tile-input revisions for sparse CPU capture, retain marker
updates independently. Gain O(map area) scan reduction per changed capture;
not an every-frame full texture upload fix. V: fog, tags, neutral cycling and
markers must update; no H.

## Simulation tick allocation and scanning

**S1 — CONFIRMED: map-wide selection remains after reachability caching.**
`S/Systems/ImpTaskSystem.Selection.cs:430,442,461,724`. Excavation walks every
tile, collects candidates and sorts them; diamond searches can visit the map
when no job qualifies. Cost per search O(map area + candidates log candidates),
multiplied by searching imps. Proposed: ordered candidate indexes and reusable
scratch, retaining exact selection/tie rules. Gain potentially large on no-job
and mass-tag ticks. **H high:** the scan clears DigTaskOutlivesWallFor at `:449`
and emits events, and ring starting sides consume RNG. Do not skip those writes,
change RNG draws or replace traversal with nearest-first heuristics.

**S2 — CONFIRMED: known transient room work still exists.**
`S/RoomNeedRules.cs:84,118,168,280,330,403,441` allocates filtered room lists
per applicable creature need/job query. `S/AutoFurnitureRules.cs:299,308,316`
builds a List and room-membership HashSet and sometimes another result HashSet
per call. `S/Systems/AutoFurnitureSystem.cs:19,27,35,45` allocates five collections
and traverses rooms/furniture/objects every tick. Proposed: reuse per-owner
scratch and then cache only facts with a complete input key, following ADR-0466.
Gain: remove container/backing-array churn; magnitude needs allocation stacks.
**H:** preserve object creation/removal order, dynamic floor-slot inputs, graveyard
adoption and per-query live occupancy. A per-tick cache may already be stale
within that same tick; do not assume RoomIndex's key covers these facts.

**S3 — CONFIRMED: event details built before sink acceptance.**
`S/ISimulationSystem.cs:85,88`; `S/SimulationEvents.cs:33,39`.
Emit takes string, so callers pay formatting before NullEventSink discards it;
RequestEffect formats detail even when only typed payloads are consumed. The
historical “~139 sites” is not a current count: a bounded source-pattern sweep
found 411 `context.Emit` sites with nearby interpolated strings, not 411 events
per tick or an exhaustive syntax count. Unity installs a real visibility/view
sink (`U/DungeonRunner.cs:337`), so a null-sink-only guard has limited live gain.
Proposed: explicit sink interest in text versus typed payloads, lazy details
without allocating a closure per call. Gain proportional to emitted text volume;
unknown until measured. H low if event payload/order and all RNG/effect work
still run; never suppress the effect request or simulation draw itself.

**S4 — CONFIRMED: KeeperView copies the observation per Think, conditionally.**
`A/KeeperAI.cs:73`; `A/KeeperView.cs:34,41,52,61,84,98,146`.
Tiles, room views, creatures, messages and traps are copied per keeper Think;
cost O(map + entities + rooms) per AI tick. The old ~341 KB estimate is not a
current measurement. Runner's audited path does not itself call KeeperAI. This
is principally an AI/harness proposal unless a live AI caller is connected.
Proposed: fill bounded detached storage or reuse immutable components with
complete revision keys, retaining snapshot semantics. Gain potentially large
with several keepers. **H:** AI commands, iteration order and routes feed future
hashed state/RNG; compare commands as well as hashes. Never expose mutable world
storage through an observation to save copying.

## Burst work and synchronization

**T1 — CONFIRMED join; frame-pacing attribution SUSPECTED.**
`U/DungeonRunner.cs:517,645,674,681`: next Update waits for the prior Task.Run
tick batch; the batch can contain two ticks. Excess over available overlap
blocks the main thread. Proposed: attribute worker execution versus queue/join
delay first; follow the [parallelism roadmap](../plans/2026-10-17-parallelism-design.md).
Gain can be zero when ticks finish in time. **H/V high** for publication changes:
commands/events/world readers and draw-clock endpoint holds must stay coherent.
Do not remove the join while the presenter reads mutable simulation state.

**T2 — CONFIRMED burst paths; magnitude SUSPECTED.**
`U/TerrainChunks.cs:136,142,148,232`: first flush is unbudgeted; later flushes
budget between chunks, so a single chunk can exceed 4 ms. Rebuilds allocate
meshes and geometry arrays (`:271–291`). `ClassicMeshes.cs:287,369,713` still
loads/converts previously unseen art synchronously; effect first-use can build
every animation frame (`ClassicConstructionEffects.cs:306,314`). HUD Texture
helpers similarly decode on cache miss. Proposed: log first-use misses and
largest chunk time; bounded prewarming and finer rebuild work only if needed.
Gain: fewer startup/first-encounter spikes, not necessarily higher steady FPS.
V: preserve complete initial scene and the accepted terrain backlog semantics;
no H. Existing prefetch/startup improvements are documented in the roadmap.

**T3 — CONFIRMED movie-specific sync and upload.**
`U/ClassicMoviePlayer.cs:86,217,221,225,236,274`: open occurs synchronously;
ShowFrame waits for decode and, after a dropped frame, may wait for a replacement
decode as well. Due frames copy/upload width*height*4 bytes; teardown joins the
worker. Proposed: measure decode wait separately from texture copy/upload before
altering buffering. Gain limited to movies/skip transitions. V: audio clock,
frame drop policy, worker ownership; no H. It is not a continuous dungeon cost.

## Phase 2 — proposed measurements, awaiting owner go-ahead

Run serially; no automatic benchmark/test sweep. Preserve the editor's scene
and use this branch's code/DLLs, fixed seed 1, 1280x720, 60 FPS first and then
the owner's usual resolution/cap. Record hardware, Unity/URP/backend, hash,
GC mode, settings and command/camera script. Separate startup from warm frames.

1. **Cheap CPU baseline:** use existing `tools/game-unity-probe/run.ps1` with
   `-Level Level1`, `Level8`, `Level18`, each `-Seconds 150`, sequentially.
   Use a fresh, non-existing `-OutputRoot` per level/run. Script reuses/removes
   Assets/partial caches; inventory and obtain owner approval before any reuse
   or cleanup, as AGENTS.md requires. First run may import/build; approve its
   CPU cost with this phase. Keep artifacts, do not auto-clean worktrees.
   Collect summary.txt and frames.csv p50/p95/p99/max, >16.67/>33 ms counts,
   GC bytes/frame and collections; distinguish tick/non-tick and tag waves at
   5/60 seconds. Check recorder units with ClassicAllocationCalibration; unavailable
   or uncalibrated bytes are unknown, not zero. The probe forces worker ticks
   off and cameras do not render: **no GPU, transparency-sort, or join verdict**.
2. **Tick attribution:** one targeted Level18AttackKeeperScenarioTests run,
   with `DOTNET_TieredCompilation=0`, using RunClock/ScenarioRunner timings.
   Report p50/p99/max and worst-tick systems/allocations; separate state-hash
   and keeper-AI spans, which RunClock's tick envelope includes. Use existing
   scenario output options, no new automation in this pass. Do not compare
   .NET allocation counts directly to Unity Mono results.
3. **Rendered player capture:** build an isolated Development player, enable
   Development Build and Autoconnect Profiler; leave Deep Profiling off. Open
   Window > Analysis > Profiler, choose the player in the target dropdown,
   enable CPU Usage and Memory modules, enable Call Stacks > GC.Alloc, Record.
   Capture 20–30 seconds each of Level8 entrance, paused camera rotation, and
   busy Level18 gameplay with a repeatable pointer sweep/effect burst. Stop
   Record and Save each capture. In CPU Timeline select a spike; inspect main
   and worker threads, DK2.Sim.*, animation sample/upload and transparent
   Sort/Order/Upload markers. Record allocation stacks and actual wait duration.
   Worker-on versus worker-off runs must be separate matched experiments.
   Use Frame Debugger on representative paused frames to count environment,
   flower, effect and transparent-run draws. GPU profiling where supported,
   plus matched 720p/1440p frames, separates fill cost from CPU submission.
4. **15-minute soak:** same rendered level, camera and repeating actions;
   warm all tested art first. Capture at warm start, 5, 10 and 15 minutes:
   Texture2D/Material/Mesh counts and bytes, graphics-buffer counts/bytes,
   managed used/reserved heap, active entities/effects and frame-time tails.
   Include repeating hover, fades, mist, digging and creature churn. Plateau
   capacity is not a leak; correlate growth with new keys versus dead owners.
   Then unload/reload the level three times and compare after destruction has
   completed. Do not force GC or UnloadUnusedAssets in the timed baseline;
   use those only as a separately labelled reachability diagnostic.
5. **Memory snapshots:** in the isolated project use the Memory Profiler package
   (install there if absent), Window > Analysis > Memory Profiler, select the
   same player and Capture at warm start/end/post-unload. Open snapshots in
   Compare, sort native types by count/size delta, inspect references for
   retained ClassicMeshes variants and generated meshes; compare managed roots
   separately. Capture costs pause/hitch: exclude those intervals from timing.
   If a buffer type is absent from the snapshot view, record live constructor/
   Dispose counts or native-memory counters in a separately approved probe.
6. **Persistent warning:** preserve full Editor.log; reproduce one Play/stop
   and one domain reload separately in isolation with allocation call stacks
   enabled before the allocation. Record Unity/package versions and stacks.
   Only then decide whether an empty matching URP project is needed. Editor
   warnings and player object growth are separate evidence, not one diagnosis.

Profiler instructions were checked via Context7 against Unity's
[CPU allocation guidance](https://docs.unity3d.com/Manual/performance-track-garbage-collection.html),
[call-stack controls](https://docs.unity3d.com/Manual/profiler-cpu-navigating.html),
[native lifetime guidance](https://docs.unity3d.com/Manual/performance-managed-memory-introduction.html)
and [Memory Profiler introduction](https://docs.unity3d.com/Manual/profiler-memory-introduction.html).
Confirm performance gains in a visible release player with probes off; use
alternating variants and report spread, following the roadmap's sampling rules.

## Proposed fix order and acceptance

First measure. Then review/measure each change separately: C3/C4 scratch and
formatting; L1/L3 ownership; L2 cache lifetime; S3 text interest; S2 room scratch;
C2 picking; R2 uploads; C1 pose/upload gating; R1 transparent work. Move a
measured dominant cost ahead of this cheap/safe ordering. S1/S4 and T1 redesign
come last unless attribution establishes a larger critical-path benefit.

For each: same warmup/inputs and before/after frame distributions, allocation
stacks, soak deltas and draw counts where relevant; review independently.
Simulation/AI work must match per-tick hashes, RNG, commands, ordered events and
save/load continuation without re-pinning. Presentation changes require matched
visual/input checks and the Unity C# 9 adapter build. No code fix, shader change,
new capture automation, package installation or phase-2 run is authorized by
this report alone.
