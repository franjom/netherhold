// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Camera;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;
using DK2.Data.Save;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Systems;
using Unity.Profiling;
using UnityEngine;

namespace DK2.Unity
{
    public enum DungeonToolMode : byte
    {
        Hand = 0,
        BuildRoom = 2,
        SellRoom = 3,
        CreateImp = 6
    }

    /// <summary>Owns a simulation, ticks it, and hands the result to a
    /// <see cref="DungeonScene"/> — the whole of Unity's involvement in the game.</summary>
    /// <remarks>
    /// <para>Holds no game state, sends the simulation nothing but
    /// <see cref="InputFrame"/>s, reads no asset path, and decides nothing about
    /// what changed: deleting this component leaves the simulation running
    /// identically (plan §4.7).</para>
    /// <para>The on-screen tick rate comes from
    /// <see cref="SimulationTiming.TicksPerSecond"/> — one constant, and it lives
    /// in the simulation (ADR-0011). Do not tune it here.</para>
    /// </remarks>
    public sealed partial class DungeonRunner : MonoBehaviour
    {
        [Header("Where Dungeon Keeper 2 is installed")]
        [Tooltip("Leave blank to use the DK2_DIR environment variable, or the usual install locations.")]
        [SerializeField] private string installDirectory = "";

        [Header("What to load")]
        [SerializeField] private string levelName = "Level1";
        [Tooltip("A .SAV or .QSAV to load instead of the campaign level. " +
                 "Bare filename (looked up in the install's Data/Save) or a full path. " +
                 "Loads terrain and ownership only — a save's creatures and objects are undecoded.")]
        [SerializeField] private string saveFile = "";
        [SerializeField] private ulong seed = 1;
        [SerializeField] private bool fogOfWar;
        [Range(0, TileState.MaxTaggingPlayerId)]
        [SerializeField] private int viewingPlayerId = PlayerIds.FirstKeeper;

        [Header("Input")]
        [SerializeField] private DungeonToolMode tool = DungeonToolMode.Hand;
        [Tooltip("Room used by Build. Select a name here or cycle rooms with [ and ].")]
        [SerializeField] private RoomTypeId selectedRoomId = RoomTypeId.Treasury;

        [Header("Running")]
        [Tooltip("Multiplies the simulation's own tick rate. 0 pauses; 1 is the placeholder rate (Q1).")]
        [SerializeField] private float speed = 1f;
        [SerializeField] private bool showDiagnostics = true;

        /// <summary>Ticks run on a worker thread after the frame has been presented, while Unity
        /// renders it, so a tick costs the next frame only what it exceeds the frame's own time by
        /// (ADR-0458). The view shows a tick one frame after it ran. Off, ticks run inside Update
        /// before the present, as they did.</summary>
        [SerializeField] private bool tickOnWorkerThread = true;

        /// <summary>Whole ticks one frame may run: a stall is paid back at this rate rather than
        /// fast-forwarding the game in one frame.</summary>
        [SerializeField] private int maxTicksPerFrame = 2;

        [Header("Art")]
        [Tooltip("Draw objects with their meshes from the install (ADR-0408). Off draws every object as a box.")]
        [SerializeField] private bool classicMeshes = true;
        [Tooltip("Decode the first frame's art on worker threads while the scene is built. Off decodes it as Present asks.")]
        [SerializeField] private bool prefetchFirstFrameArt = true;
        [Tooltip("Use DK2 terrain and object lighting through the classic shader.")]
        [SerializeField] private bool classicVertexLighting = true;
        [Tooltip("Use DK2's Cheap Lighting mode for animated torch illumination.")]
        [SerializeField] private bool cheapLighting;
        [Tooltip("Diagnostic: DK2's own light inputs only. Its fill lights are unknown, so unlit geometry goes black; off keeps Unity's sky and sun fill (owner 2026-10-20: Unity lighting tuned to look like DK2).")]
        [SerializeField] private bool nativeInitialAmbient;
        [Tooltip("Projected 32-pixel silhouettes from each creature's current animated mesh.")]
        [SerializeField] private bool classicCreatureShadows = true;

        [Tooltip("Bend the classic terrain as DK2 does (the terrain lattice, Q72).")]
        [SerializeField] private bool bendTerrain = true;

        [Tooltip("DK2's HighWalls display option, on by default; off rests walls at about half height.")]
        [SerializeField] private bool highWalls = true;

        [Header("Camera")]
        [Tooltip("Original camera profile used when Play starts.")]
        [SerializeField] private ClassicCameraMode cameraPreset = ClassicCameraMode.Dungeon;
        [Tooltip("Zoom-out limit multiplier. 1 keeps DK2's original bound; 1.2 allows 20% more distance.")]
        [Range(1f, 2f)]
        [SerializeField] private float cameraZoomOutLimitScale = 1.2f;
        [Tooltip("Diagnostic whole-map overview instead of the original playing camera.")]
        [SerializeField] private bool frameWholeMap;
        [Tooltip("Pitch for the diagnostic whole-map overview.")]
        [Range(20f, 90f)]
        [SerializeField] private float cameraPitch = 55f;
        [SerializeField] private float cameraPanSpeed = 12f;
        [SerializeField] private float cameraRotationSpeed = 90f;
        [SerializeField] private float cameraZoomFraction = 0.15f;
        private const float MinCameraHeight = DungeonScene.FloorHeight + 0.5f;
        private ClassicCamera _classicCamera;
        private readonly ClassicAmbientLight _ambientLight = new ClassicAmbientLight();
        internal Func<EffectTime> EffectClock { get; set; }
        private Vector3 _cameraFocus;
        private Vector2 _cameraBounds;
        private float _cameraTurnRemainder;

        // With TickMarker and PresentMarker below, so the Profiler separates input, the
        // position snapshot and the tick itself (whose systems UnitySimulationProfiler shows).
        private static readonly ProfilerMarker InputMarker = new ProfilerMarker("DK2.Frame.Input");
        private static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("DK2.Simulation.CaptureObjectPositions");

        private GameSimulation _simulation;
        private ScenePresenter _presenter;
        private WorldView _worldView;
        private DungeonMinimap _minimap;
        private ClassicHandCursor _handCursor;
        private ClassicTileHoverOutline _tileHighlight;
        private WorldHoverText _hoverText;
        private ClassicHoverHud _hoverHud;
        private ClassicResourceHud _resourceHud;
        private KeeperResourceView _resourceView;
        private HoverTextSnapshot _hoverSnapshot = HoverTextSnapshot.Empty;
        private DungeonScene _scene;
        private AssetProviderChain _assets;
        private InstallArtProvider _installArt;
        private ClassicMeshes _classic;
        private ClassicConstructionEffects _constructionEffects;
        private Action<HeartHealthChange> _heartHealthObserver;
        private WorldState _heartHealthWorld;
        private readonly PlayerInputTranslator _input = new PlayerInputTranslator();
        private TilePos? _dragTo;
        private readonly List<PlayerCommand> _pendingCommands = new List<PlayerCommand>();
        private ScriptedViewDirector _scriptedView;
        private Vector3 _glideFrom;
        private Vector3 _glideTo;
        private double _glideStart;
        private double _glideMilliseconds;
        private GUIStyle _subtitleStyle;
        private string _textFolder;
        private string _subtitleFontFile;
        private ClassicBitmapText _subtitleFont;
        private RoomTypeId[] _roomIds = Array.Empty<RoomTypeId>();
        private double _tickDebt;
        private string _failure;
        private string _loadedFrom;
        private Task _tickTask;
        private ulong _stateHash;
        private string _diagnostics = "";
        private readonly DebugFrameOverlay _frameOverlay = new DebugFrameOverlay();
        private StartupTimings _startup;

        /// <summary>The running simulation, or null if it could not start. Read-only by intent.
        /// Waits for a tick in flight on the worker thread, so a reader sees a settled world.</summary>
        public GameSimulation Simulation
        {
            get
            {
                WaitForTick();
                return _simulation;
            }
        }

        private void Start()
        {
            _startup = new StartupTimings();
            try
            {
                StartGame();
            }
            catch (Exception ex)
            {
                // A missing install is the normal reason this fails and it is
                // not exceptional, so it is reported on screen rather than
                // thrown into the console and forgotten.
                _failure = ex.Message;
                Debug.LogError($"DK2: could not start — {ex.Message}");
            }
        }

        private void StartGame()
        {
            if (tool != DungeonToolMode.Hand && tool != DungeonToolMode.BuildRoom &&
                tool != DungeonToolMode.SellRoom && tool != DungeonToolMode.CreateImp)
                tool = DungeonToolMode.Hand;

            string install = string.IsNullOrWhiteSpace(installDirectory)
                ? Dk2Install.Find()
                : installDirectory;

            if (install == null)
                throw new InvalidOperationException(
                    "no Dungeon Keeper 2 installation found. Set the Install Directory field, " +
                    "or the DK2_DIR environment variable.");
            _startup.Step("install");

            bool campaignLevel = string.IsNullOrWhiteSpace(saveFile);
            GameDatabase database = campaignLevel
                ? GameDatabaseLoader.LoadForCampaignLevel(install, levelName)
                : GameDatabaseLoader.Load(install);
            _startup.Step("database");

            LevelMap map = campaignLevel
                ? GameDatabaseLoader.LoadCampaignMap(install, levelName)
                    ?? throw new InvalidOperationException($"the install has no '{levelName}' map.")
                : LoadSave(install);

            PlayerDef startingCameraPlayer = null;
            if (campaignLevel)
            {
                var level = GameDatabaseLoader.LoadCampaignLevel(install, levelName)
                    ?? throw new InvalidOperationException($"the install has no '{levelName}' level definition.");
                var players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName)
                    ?? throw new InvalidOperationException($"the install has no '{levelName}' players.");
                startingCameraPlayer = players.FirstOrDefault(p => p.PlayerId == viewingPlayerId);
                var things = GameDatabaseLoader.LoadCampaignThings(install, levelName)
                    ?? throw new InvalidOperationException($"the install has no '{levelName}' things.");
                var triggers = GameDatabaseLoader.LoadCampaignTriggers(install, levelName)
                    ?? throw new InvalidOperationException($"the install has no '{levelName}' triggers.");
                _simulation = CampaignSimulationFactory.Create(database, map, level,
                    players, things, triggers, seed, fogOfWarEnabled: fogOfWar);
            }
            else
            {
                WorldState savedMap = WorldState.FromMap(
                    database, map, PlayersOf(map), fogOfWarEnabled: fogOfWar);
                _simulation = new GameSimulation(database, savedMap, seed, SimulationSystems.Default());
            }
            _simulation.ProfileWith(new UnitySimulationProfiler());
            WorldState world = _simulation.World;
            _roomIds = database.Rooms.Keys.OrderBy(id => (byte)id).ToArray();
            SelectKnownRoom();
            _startup.Step("level");

            // The chain the plan specifies (§7.2), with the install read in place as the
            // converted tier (ADR-0408). Both roots are relative to the project directory and
            // normally missing; with the install off too, everything draws as boxes.
            if (classicMeshes)
            {
                _installArt = InstallArtProvider.Open(install, database);
                _classic = new ClassicMeshes(_installArt, classicVertexLighting, nativeInitialAmbient);
            }
            _assets = AssetProviderChain.Standard(
                replacementRoot: "art-pack",
                convertedRoot: "build/converted",
                install: _installArt);
            _startup.Step("art");

            // The first frame's art decodes on workers while the scene is built and the first tick
            // runs; Present waits for it. The keys are read here, before anything else reads the world.
            // What the first tick adds (room furniture, open doors, new clips) loads on request:
            // listing the keys again after it cost about what decoding that art does.
            Task prefetch = null;
            InstallArtProvider art = _installArt;
            int workers = PrefetchWorkers(); // SystemInfo is main-thread only
            if (art != null && prefetchFirstFrameArt)
            {
                IReadOnlyCollection<AssetRef> firstFrame = FirstFrameArt.Keys(database, world, new PlayerId((byte)viewingPlayerId));
                prefetch = Task.Run(() => art.Prefetch(firstFrame, workers));
                _startup.Step("prefetch.keys");
            }

            TerrainLattice lattice = _classic != null && bendTerrain
                ? new TerrainLattice(database, world, (byte)viewingPlayerId) { HighWalls = highWalls }
                : null;
            _scene = new DungeonScene(transform, world.Width, world.Height, _classic, lattice, classicCreatureShadows, cheapLighting);
            if (EffectClock != null) _scene.HeartEffectClock = EffectClock;
            _worldView = new WorldView(database, (byte)viewingPlayerId);
            _presenter = new ScenePresenter(
                _worldView, _assets, _scene, new TorchLayout(database));
            _startup.Step("scene");
            if (_installArt != null)
            {
                var sprites = new InstallEffectSpriteProvider(database, _installArt.LoadTexture);
                _scene.AttachTorchSprites(sprites, EffectClock);
                _constructionEffects = new ClassicConstructionEffects(transform, database, sprites, seed,
                    request => _worldView.CanShowTile(world, request.Tile),
                    request => _worldView.CanShowTile(world, request.Tile)
                        // DK2 asks for the Heart's opening effects only for the local player (0x004D9A60).
                        && (!HeartIntroRules.IsViewerOnly(request.EffectId.Value) || request.Owner.Value == viewingPlayerId),
                    point => _worldView.GroundHeightAt(world, point), meshes: _classic,
                    captureEdges: request => CaptureEffectLayout.Boundary(world, database, request),
                    attachment: _scene.CreatureAttachment, clock: EffectClock);
                _scene.HeartEffectRequested = request => _constructionEffects.Model.Request(request);
                // 0x0043A8E0's owner is the render sink's word +0xC, whose writer is unread; taken as the viewer.
                _scene.GemSparkleRequested = point => _constructionEffects.Model.Request(new ViewEffectRequest(
                    new EffectId(CreatureView.GemDustSparkle.EffectId), new PlayerId((byte)viewingPlayerId), point));
                _constructionEffects.LightTable = _scene.LightTableAt;
            }
            _startup.Step("effects");
            _textFolder = Path.Combine(install, "Data", "Text", "Default");
            _scriptedView = new ScriptedViewDirector(new PlayerId((byte)viewingPlayerId),
                CameraPathLoader(install), SubtitleLoader(install, database.Level?.TextTableId ?? 0),
                (ISimulationEventSink)_constructionEffects?.Model ?? NullEventSink.Instance);
            _simulation.ObserveWith(new VisibilityEventSink(_worldView, world,
                new PlayerId((byte)viewingPlayerId), _scriptedView));

            int maximumHeartHealth = database[VariableTypeId.DungeonHeartObjectHealth];
            _heartHealthObserver = change => _scene.QueueHeartHealthChange(change.Owner,
                DungeonHeartVisual.FromHealth(currentHealth: change.Health, maximumHealth: maximumHeartHealth));
            world.HeartHealthChanged += _heartHealthObserver;
            _heartHealthWorld = world;
            _startup.Step("script");
            // The Heart's plug lid is asked for on game turn 1 and by nothing earlier, so DK2 draws
            // no frame before its first turn (heart-intro.md §4, an inference from 0x004D9A60's
            // guard). The first tick runs here, and the effect model one update, so the first frame
            // shows the lid and not the open pit beneath it.
            RunTicks(1, InputFrame.Empty);
            _constructionEffects?.Model.UpdateNow();
            _startup.Step("tick");
            WaitForPrefetch(prefetch);
            _startup.Step("prefetch.wait");
            double[] artBefore = _classic?.Breakdown.Snapshot();
            _presenter.Present(world);
            _startup.Step("present");
            _startup.Breakdown("present", artBefore, _classic?.Breakdown.Snapshot());
            CameraThing profile = GameDatabaseLoader.LoadCameraPresets(install, campaignLevel ? levelName : null)
                .FirstOrDefault(c => c.CameraId == (ushort)cameraPreset);
            if (classicMeshes && profile != null)
            {
                _classicCamera = new ClassicCamera(profile with
                {
                    MaxZoom = Mathf.RoundToInt(profile.MaxZoom * Mathf.Clamp(cameraZoomOutLimitScale, 1f, 2f)),
                }, initialYaw: ClassicCamera.LevelStartYaw) { ReferenceScreenWidth = 1600 };
                _cameraBounds = new Vector2(world.Width, world.Height);
                System.Numerics.Vector3 focus = startingCameraPlayer != null
                    ? ClassicCamera.LevelStartFocus(new TilePos(startingCameraPlayer.CameraX, startingCameraPlayer.CameraY))
                    : new System.Numerics.Vector3(world.Width / 2f, 1, -world.Height / 2f);
                _cameraFocus = new Vector3(focus.X, focus.Y, focus.Z);
            }
            if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
            else LookAt(world);
            _startup.Step("camera");
            var minimapAssets = new InstallMinimapAssets(install, _installArt != null ? _installArt.LoadTexture : null);
            _startup.Step("minimap.assets");
            _minimap = new DungeonMinimap(transform, minimapAssets, world.Width, world.Height);
            _startup.Step("minimap.build");
            _minimap.Capture(world, _worldView, _simulation.Navigation);
            _startup.Step("minimap.capture");
            _minimap.Draw(Camera.main, _cameraFocus);
            _startup.Step("minimap.draw");
            _handCursor = new ClassicHandCursor(new InstallHandCursorAssets(install));
            _tileHighlight = new ClassicTileHoverOutline(transform, lattice);
            _hoverHud = new ClassicHoverHud(new InstallClassicHoverAssets(install));
            _resourceHud = new ClassicResourceHud(transform, new InstallClassicResourceAssets(install));
            _resourceView = new KeeperResourceView(_simulation.Database, new PlayerId((byte)viewingPlayerId));
            InitializeHudControls(install, database, campaignLevel);
            var hoverStrings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
            _hoverText = new WorldHoverText(database, hoverStrings.Resolve, new PlayerId((byte)viewingPlayerId));
            _startup.Step("hud");
            _scene.SetObjectLights(_worldView.LightObjects(_simulation.World), 1);
            if (_constructionEffects != null) _scene.SetEffectLights(_constructionEffects.Advance());
            if (_classicCamera != null)
                _scene.PrepareClassicLighting(Camera.main, _classicCamera.NativeViewDistance);
            _startup.Step("lights");
            Debug.Log(_startup.Summary(_installArt));
        }

        /// <summary>Threads the first frame's art decodes on, at most; fewer on a machine with fewer cores.</summary>
        private const int MaxPrefetchWorkers = 8;

        private static int PrefetchWorkers() => Math.Max(1, Math.Min(SystemInfo.processorCount - 1, MaxPrefetchWorkers));

        /// <summary>Waits for the art prefetch. A failure is only a warning: the art then loads on
        /// request, which reports a real problem as it always has.</summary>
        private static void WaitForPrefetch(Task prefetch)
        {
            if (prefetch == null) return;
            try
            {
                prefetch.Wait();
            }
            catch (AggregateException ex)
            {
                Debug.LogWarning($"DK2: art prefetch failed — {ex.GetBaseException().Message}");
            }
        }

        /// <summary>Wall time of each startup step, logged as one line when <see cref="StartGame"/>
        /// ends and again after the first frames, which still decode art on first sight. It measures
        /// loading for the parallelism plan (docs/plans/2026-10-17-parallelism-design.md).</summary>
        private sealed class StartupTimings
        {
            public const int SettleFrames = 120;
            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
            private readonly System.Text.StringBuilder _steps = new System.Text.StringBuilder();
            private double _last;
            private int _frames;
            private double _slowestFrame;
            private int _slowestFrameIndex;
            private readonly double _launchToStart = Time.realtimeSinceStartupAsDouble * 1000;

            public void Step(string name)
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                Detail(name, now - _last);
                _last = now;
            }

            public void Detail(string name, double milliseconds) =>
                _steps.Append(' ').Append(name).Append('=').Append(Format(milliseconds));

            /// <summary>The art work done between two breakdown snapshots, by kind.</summary>
            public void Breakdown(string step, double[] before, double[] after)
            {
                if (before == null || after == null) return;
                for (var work = ClassicMeshes.LoadBreakdown.Work.None; work < ClassicMeshes.LoadBreakdown.Work.Count; work++)
                    Detail(step + "." + (work == ClassicMeshes.LoadBreakdown.Work.None ? "other" : work.ToString().ToLowerInvariant()),
                        after[(int)work] - before[(int)work]);
            }

            public string Summary(InstallArtProvider art) =>
                $"DK2 startup: {Format(_last)} ms (launch to Start {Format(_launchToStart)} ms);{_steps}{Decoded(art)}";

            /// <summary>Counts a frame after startup; returns the line to log on the last one, else null.</summary>
            public string Frame(double milliseconds, InstallArtProvider art)
            {
                if (_frames >= SettleFrames) return null;
                _frames++;
                if (milliseconds > _slowestFrame)
                {
                    _slowestFrame = milliseconds;
                    _slowestFrameIndex = _frames;
                }
                return _frames < SettleFrames ? null
                    : $"DK2 first {SettleFrames} frames: slowest {Format(_slowestFrame)} ms (frame {_slowestFrameIndex});{Decoded(art)}";
            }

            private static string Decoded(InstallArtProvider art) => art == null ? "" :
                $" {art.ModelsDecoded} models, {art.ImagesDecoded} images kept; {Format(art.DecodeTime.TotalMilliseconds)} ms decoded on request";

            private static string Format(double milliseconds) =>
                milliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void OnDestroy()
        {
            WaitForTick();
            DisposeHudControls();
            _minimap?.Dispose();
            _handCursor?.Dispose();
            _tileHighlight?.Dispose();
            _hoverHud?.Dispose();
            _resourceHud?.Dispose();
            if (_heartHealthWorld != null && _heartHealthObserver != null)
                _heartHealthWorld.HeartHealthChanged -= _heartHealthObserver;
            _constructionEffects?.Dispose();
            _scene?.DisposeLightResources();
            _installArt?.Dispose();
            _installArt = null;
        }

        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("DK2.Simulation.Tick");
        private static readonly ProfilerMarker PresentMarker = new ProfilerMarker("DK2.Scene.Present");
        private readonly FrameElapsedClock _frameElapsed = new FrameElapsedClock();

        private void Update()
        {
            _frameOverlay.Sample(showDiagnostics);
            if (_simulation == null) return;
            string settled = _startup?.Frame(Time.unscaledDeltaTime * 1000d, _installArt);
            if (settled != null) Debug.Log(settled);

            // The tick started at the end of the last frame, if any, has the world until it is done.
            WaitForTick();
            if (_simulation == null) return;
            double elapsed = _frameElapsed.Sample(Time.realtimeSinceStartupAsDouble,
                fallbackDeltaSeconds: Time.deltaTime, maximumDeltaSeconds: Time.maximumDeltaTime, timeScale: Time.timeScale);

            // The path is placed by the last tick run plus the debt toward the next, the clock
            // condition 72 counts, so the script's next path follows it. On the worker path that
            // is the world this frame draws; on the main thread this frame's ticks come after.
            _scriptedView.Update(Time.unscaledTimeAsDouble * 1000d,
                _simulation.World.CurrentTick.Value + Math.Min(_tickDebt, 1d));
            using (InputMarker.Auto())
            {
                ApplyLetterbox();
                bool pathWasPlaying = _scriptedView.PathPlaying;
                if (_scriptedView.PathPlaying)
                {
                    // 0x00405362: Escape or Space ends the path; the simulation hears it next tick.
                    if ((Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space)) && _scriptedView.Skip())
                        _pendingCommands.Add(new DK2.Simulation.Commands.SkipCameraPathCommand(new PlayerId((byte)viewingPlayerId)));
                }
                if (_scriptedView.TryTakeCameraMove(out TilePos glideTile)) StartGlide(glideTile);
                UpdateHudControls(allowOptionsKey: !pathWasPlaying);
                HudVisibility hud = CurrentHud;
                _minimap?.SetVisibility(hud);
                _hoverHud?.SetVisibility(hud);
                _resourceHud?.SetVisibility(hud);
                if (_scriptedView.PathPlaying) ApplyPathCamera();
                else
                {
                    Glide();
                    if (hud.AllowOrdinaryInput && !HudModal) ControlCamera();
                    else if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
                }
                ReadAmbientKeys();
                if (hud.AllowOrdinaryInput && !HudModal) ReadToolKeys();
                QueueInput();
            }

            int ticks = 0;
            InputFrame firstInput = InputFrame.Empty;
            if (speed > 0f && !_hudControls.OptionsVisible)
            {
                _tickDebt += elapsed * SimulationTiming.TicksPerSecond * speed;

                // Whole ticks only, and a cap so a long editor stall does not
                // fast-forward the game by a thousand ticks in one frame.
                ticks = (int)Math.Min(_tickDebt, Math.Max(1, maxTicksPerFrame));
                _tickDebt -= ticks;
                if (ticks > 0 && _pendingCommands.Count > 0)
                {
                    firstInput = new InputFrame(_pendingCommands.ToArray());
                    _pendingCommands.Clear();
                }
            }

            if (ticks > 0 && !tickOnWorkerThread) RunTicks(ticks, firstInput);
            // Retain released tag previews until the command's tick has actually completed,
            // including the frame before a worker tick starts.
            _worldView.AcknowledgeTagPreviews(_simulation.World.CurrentTick);

            // How far the drawn world is from its last tick toward its next, for the creature
            // interpolation. On the worker path this frame's ticks have not run yet, so the world
            // drawn is still the previous one and the frame that owes a tick draws it complete (1);
            // the next frame then starts the new tick's motion from there.
            float fraction = (float)Math.Min(tickOnWorkerThread ? _tickDebt + ticks : _tickDebt, 1d);

            // Off the map, a Hand excavation preview keeps the last tile the pointer was on.
            TilePos? dragFrom = _input.DragFrom(PlayerToolMode.Hand);
            if (dragFrom == null) _dragTo = null;
            else _dragTo = ScreenToTile(Input.mousePosition) ?? _dragTo ?? dragFrom;
            _worldView.PreviewTags(dragFrom, _dragTo, _input.ExcavationPreviewTagged ?? true);
            _scene.SetCreatureFrameTime(_simulation.World.CurrentTick, fraction);
            _worldView.SetPossessionView(_simulation.World.Player(new PlayerId((byte)viewingPlayerId)).PossessedCreatureId != 0);
            using (PresentMarker.Auto()) _presenter.Present(_simulation.World);
            _minimap?.Capture(_simulation.World, _worldView, _simulation.Navigation);
            double preparedElapsed = _frameElapsed.Sample(Time.realtimeSinceStartupAsDouble,
                fallbackDeltaSeconds: 0, maximumDeltaSeconds: Math.Max(0, Time.maximumDeltaTime - elapsed), timeScale: Time.timeScale);
            if (speed > 0f && !_hudControls.OptionsVisible) _tickDebt += preparedElapsed * SimulationTiming.TicksPerSecond * speed;
            if (tickOnWorkerThread && speed > 0f && !_hudControls.OptionsVisible)
            {
                int preparedTicks = (int)Math.Min(_tickDebt, Math.Max(1, maxTicksPerFrame) - ticks);
                ticks += preparedTicks;
                _tickDebt -= preparedTicks;
                if (ticks > 0 && firstInput.All.Count == 0 && _pendingCommands.Count > 0)
                {
                    firstInput = new InputFrame(_pendingCommands.ToArray());
                    _pendingCommands.Clear();
                }
            }
            fraction = (float)Math.Min(tickOnWorkerThread ? _tickDebt + ticks : _tickDebt, 1d);
            bool drawingPath = _scriptedView.PathPlaying;
            _scriptedView.SeekCamera(_simulation.World.CurrentTick.Value + fraction);
            if (_scriptedView.PathPlaying) ApplyPathCamera();
            else if (drawingPath)
            {
                if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
                else LookAt(_simulation.World);
            }
            _scene.DrawCreatureFrame(_simulation.World.CurrentTick, fraction);
            FollowQueriedCreature();
            _scene.SetObjectLights(_worldView.LightObjects(_simulation.World), fraction);
            if (_constructionEffects != null) _scene.SetEffectLights(_constructionEffects.Advance());
            if (_classicCamera != null)
                _scene.PrepareClassicLighting(Camera.main,
                    _scriptedView.Pose is CameraPathPose path ? path.ViewDistance : _classicCamera.NativeViewDistance);
            _scene.AdvanceTorchAnimation();
            _scene.DrawCreatureShadows(Camera.main, _worldView, _simulation.World);
            _constructionEffects?.Draw();
            _minimap?.SetVisibility(CurrentHud);
            _minimap?.Draw(Camera.main, _scriptedView.PathPlaying ? (Vector3?)null : _cameraFocus);
            DrawHudControls();
            CaptureWorldHover();
            if (showDiagnostics) RefreshDiagnostics();

            // Nothing on this thread reads the world again before the next Update's wait: OnGUI
            // reads the strings built above, and Simulation's getter waits.
            if (ticks > 0 && tickOnWorkerThread)
            {
                int count = ticks;
                InputFrame input = firstInput;
                _tickTask = Task.Run(() => RunTicks(count, input));
            }
        }

        /// <summary>The ticks of one frame, the first with the frame's commands. Capturing the
        /// object positions first is what the view's interpolation reads.</summary>
        private void RunTicks(int ticks, InputFrame firstInput)
        {
            for (int i = 0; i < ticks; i++)
            {
                InputFrame input = i == 0 ? firstInput : InputFrame.Empty;
                using (TickMarker.Auto())
                {
                    using (CaptureMarker.Auto())
                        _worldView.CaptureObjectPositions(_simulation.World);
                    _simulation.Tick(input);
                }
            }
            // The hash is for the diagnostics only, and costs a few milliseconds; here it is off the
            // frame when the ticks are. Turned on at run time, it shows 0 until the next tick.
            if (showDiagnostics) _stateHash = _simulation.StateHash();
        }

        /// <summary>Joins the worker tick, if one is running. A tick that threw stops the game and
        /// shows the failure, as a throw out of Update used to.</summary>
        private void WaitForTick()
        {
            Task task = _tickTask;
            if (task == null) return;
            _tickTask = null;
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _failure = $"tick failed: {ex.Message}";
                speed = 0f;
                _simulation = null;
            }
        }

        private TilePos? _debugHoverTile;

        private void RefreshDiagnostics()
        {
            string hover = "";
            if (_debugHoverTile is TilePos at && _simulation.World.Contains(at))
            {
                TileState state = _simulation.World.Tile(at);
                var viewer = new PlayerId((byte)viewingPlayerId);
                hover = Environment.NewLine + $"hover tile ({at.X},{at.Y})  {_simulation.Database.TerrainById(state.TerrainId).Name}  owner {state.OwnerPlayerId}"
                    + $"  explored {state.IsExploredBy(viewer)}  scanned {state.IsDiscoveredBy(viewer)}";
            }
            _diagnostics =
                $"tick {_simulation.World.CurrentTick.Value}   " +
                $"state {_stateHash:x16}   " +
                $"{_scene.TileObjects} tile objects   {_scene.CreatureObjects} creatures   " +
                $"pushed {_presenter.TilesPushed} tiles / {_presenter.CreaturesPushed} creatures this frame" + hover;
        }

        private void QueueInput()
        {
            if (!CurrentHud.AllowOrdinaryInput)
            {
                _input.CancelPointerGesture();
                return;
            }
            if (QueueHudInput() || QueuePanelWorldAction()) return;
            if (!_scriptedView.PathPlaying && !frameWholeMap && _minimap != null && Input.GetMouseButtonDown(0)
                && _minimap.TryNavigate(Input.mousePosition, Camera.main, _cameraFocus, out TilePos navigationTile))
                StartGlide(navigationTile);
            _input.SelectTool((byte)viewingPlayerId, ActiveTool);
            PointerButtonTransition button = PointerButtonTransition.None;
            if (Input.GetMouseButtonDown(0)) button |= PointerButtonTransition.Pressed;
            if (Input.GetMouseButtonUp(0)) button |= PointerButtonTransition.Released;

            PointerButtonTransition secondaryButton = PointerButtonTransition.None;
            if (Input.GetMouseButtonDown(1)) secondaryButton |= PointerButtonTransition.Pressed;
            if (Input.GetMouseButtonUp(1)) secondaryButton |= PointerButtonTransition.Released;

            if (button == PointerButtonTransition.None && secondaryButton == PointerButtonTransition.None) return;

            TilePos? tile = ScreenToTile(Input.mousePosition);
            var intent = new PlayerIntent(
                (byte)viewingPlayerId, ActiveTool, tile, button, selectedRoomId,
                SecondaryButton: secondaryButton,
                PointerOverHud: PointerOverHud(Input.mousePosition));
            PlayerInputSnapshot snapshot = PlayerInputSnapshot.Capture(
                _worldView, _simulation.World, (byte)viewingPlayerId, _simulation.Database, pointerTile: tile);
            if (tile is TilePos at && (ActiveTool == PlayerToolMode.Hand || ActiveTool == PlayerToolMode.Slap))
                snapshot = snapshot.ForHover(ResolveWorldHover(at));
            InputFrame frame = _input.Translate(intent, snapshot);
            long nowMs = (long)(Time.unscaledTimeAsDouble * 1000);
            if ((button & PointerButtonTransition.Pressed) != 0 && _input.DragFrom(PlayerToolMode.Hand) != null)
                _cursorFeedback.Play(HandCursorKind.Tag, nowMs);

            foreach (PlayerCommand command in frame.All)
            {
                _pendingCommands.Add(command);
                if (command is DK2.Simulation.Commands.SlapCreatureCommand) _cursorFeedback.Play(HandCursorKind.Slap, nowMs);
                if (command is DK2.Simulation.Commands.DropCommand)
                    _cursorFeedback.Play(HoldingGold() ? HandCursorKind.DropGold : HandCursorKind.DropThing, nowMs);
                if (command is DK2.Simulation.Commands.TagTilesCommand tag)
                    _worldView.QueueTagPreview(tag, _simulation.World.CurrentTick.Next);
                if (command is DK2.Simulation.Commands.PickUpCreatureCommand pickup)
                    _worldView.QueueMinimapPickupPreview(pickup, _simulation.World.CurrentTick.Next);
            }
        }

        /// <summary>
        /// Which tile the pointer is over. The rule is <see cref="TilePicker"/>'s, because
        /// it is testable and casting at the ground plane — what this did first — reads
        /// the tile behind whatever wall the pointer is on.
        /// </summary>
        private TilePos? ScreenToTile(Vector3 screenPosition)
        {
            if (PointerOverHud(screenPosition)) return null;
            Camera camera = Camera.main;
            if (camera == null || _worldView == null) return null;

            // TilePicker works in DK2's plane, Y south; Unity's Z runs north (DungeonScene.Ground).
            Ray ray = camera.ScreenPointToRay(screenPosition);
            return TilePicker.Pick(
                new PickRay(
                    ray.origin.x, ray.origin.y, -ray.origin.z,
                    ray.direction.x, ray.direction.y, -ray.direction.z),
                _worldView,
                _simulation.World,
                DungeonScene.SolidHeight,
                DungeonScene.OpenHeight);
        }

        private PlayerToolMode ActiveTool
        {
            get
            {
                switch (tool)
                {
                    case DungeonToolMode.BuildRoom: return PlayerToolMode.BuildRoom;
                    case DungeonToolMode.SellRoom: return PlayerToolMode.SellRoom;
                    case DungeonToolMode.CreateImp: return PlayerToolMode.CreateImp;
                    default: return PlayerToolMode.Hand;
                }
            }
        }

        private void ReadToolKeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha5)) tool = DungeonToolMode.Hand;
            if (Input.GetKeyDown(KeyCode.Alpha3)) tool = DungeonToolMode.BuildRoom;
            if (Input.GetKeyDown(KeyCode.Alpha4)) tool = DungeonToolMode.SellRoom;
            if (Input.GetKeyDown(KeyCode.Alpha7)) tool = DungeonToolMode.CreateImp;

            if (Input.GetKeyDown(KeyCode.LeftBracket)) SelectAdjacentRoom(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) SelectAdjacentRoom(1);
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha5)
                || Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Alpha4)
                || Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                _panelChoice = null; _queryTool = false; _selectedCorner = ClassicHudAction.None;
                _bottomView?.SelectTool(ActiveTool, selectedRoomId);
            }
        }

        private void SelectKnownRoom()
        {
            if (_roomIds.Length == 0) return;
            if (Array.IndexOf(_roomIds, selectedRoomId) >= 0) return;
            selectedRoomId = _roomIds[0];
        }

        private void SelectAdjacentRoom(int direction)
        {
            if (_roomIds.Length == 0) return;

            int index = Array.IndexOf(_roomIds, selectedRoomId);
            if (index < 0) index = 0;
            index = (index + direction + _roomIds.Length) % _roomIds.Length;
            selectedRoomId = _roomIds[index];
        }

        private void ControlCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            if (_classicCamera != null && !frameWholeMap)
            {
                float turn = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
                _cameraTurnRemainder += turn * cameraRotationSpeed * (2048f / 360f) * Time.unscaledDeltaTime;
                int angle = (int)_cameraTurnRemainder;
                _cameraTurnRemainder -= angle;
                _classicCamera.Rotate(new Dk2Angle(unchecked((ushort)angle)));
                var direction = _classicCamera.PanDirection(new System.Numerics.Vector2(
                    Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")));
                _cameraFocus += new Vector3(direction.X, direction.Y, direction.Z)
                    * cameraPanSpeed * Time.unscaledDeltaTime;
                _cameraFocus.x = Mathf.Clamp(_cameraFocus.x, 0, _cameraBounds.x - 1f / Fp32.One);
                _cameraFocus.z = Mathf.Clamp(_cameraFocus.z, -_cameraBounds.y + 1f / Fp32.One, 0);
                float wheel = PointerOverHud(Input.mousePosition) ? 0 : Input.mouseScrollDelta.y;
                if (wheel > 0) _classicCamera.ZoomIn();
                else if (wheel < 0) _classicCamera.ZoomOut();
                ApplyClassicCamera();
                return;
            }

            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            var pan = new Vector3(horizontal, 0f, vertical);
            if (pan.sqrMagnitude > 1f) pan.Normalize();
            camera.transform.position += pan * cameraPanSpeed * Time.unscaledDeltaTime;

            float scroll = PointerOverHud(Input.mousePosition) ? 0 : Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // A step in proportion to the height, so the wheel is as useful over a whole level
            // as beside a third-of-a-tile imp.
            float height = camera.transform.position.y - DungeonScene.FloorHeight;
            Vector3 next = camera.transform.position +
                (camera.transform.forward * scroll * cameraZoomFraction * height);
            if (next.y > MinCameraHeight) camera.transform.position = next;
        }

        private void ReadAmbientKeys()
        {
            if (!classicVertexLighting) return;
            bool changed = false;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            { _ambientLight.Increase(); changed = true; }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            { _ambientLight.Decrease(); changed = true; }
            if (changed)
            {
                float level = _ambientLight.Normalized;
                Shader.SetGlobalVector("_DK2AmbientColour", new Vector4(level, level, level, 0));
            }
        }

        /// <summary>The letterbox shrinks the drawn area, as DK2's viewport rect does
        /// (camera-paths.md section 4); OnGUI paints the bars.</summary>
        private void ApplyLetterbox()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            float bar = _scriptedView.LetterboxBar;
            camera.rect = new Rect(0f, bar, 1f, 1f - 2f * bar);
        }

        /// <summary>Action 26 (game action 4, 0x00442650): the focus glides to the point's
        /// centre, kept inside the bounds, angles unchanged.</summary>
        private void StartGlide(TilePos tile)
        {
            _glideFrom = _cameraFocus;
            _glideTo = new Vector3(
                Mathf.Clamp(tile.X + 0.5f, 0, _cameraBounds.x - 1f / Fp32.One), _cameraFocus.y,
                Mathf.Clamp(-tile.Y - 0.5f, -_cameraBounds.y + 1f / Fp32.One, 0));
            _glideStart = Time.unscaledTimeAsDouble * 1000d;
            _glideMilliseconds = ScriptedViewDirector.GlideMilliseconds(
                _glideTo.x - _glideFrom.x, _glideTo.z - _glideFrom.z);
        }

        private void Glide()
        {
            if (_glideMilliseconds <= 0) return;
            double t = (Time.unscaledTimeAsDouble * 1000d - _glideStart) / _glideMilliseconds;
            _cameraFocus = Vector3.Lerp(_glideFrom, _glideTo, (float)Math.Min(t, 1d));
            if (t >= 1d) _glideMilliseconds = 0;
        }

        private void ApplyPathCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || camera.pixelHeight <= 0 || _scriptedView.Pose is not CameraPathPose pose) return;
            int screenWidth = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            if (screenWidth <= 0) return;
            camera.orthographic = false;
            camera.fieldOfView = pose.VerticalFieldOfView(screenWidth, camera.pixelHeight);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, pose.ViewDistance);
            camera.transform.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z),
                new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z));
        }

        /// <summary>EnginePath%d.kcs out of Paths.WAD, read once per path.</summary>
        private static Func<int, IReadOnlyList<KcsFrame>> CameraPathLoader(string install)
        {
            string wad = Path.Combine(install, "Data", "Paths.WAD");
            var cache = new Dictionary<int, IReadOnlyList<KcsFrame>>();
            return pathId =>
            {
                if (cache.TryGetValue(pathId, out IReadOnlyList<KcsFrame> frames)) return frames;
                frames = null;
                if (File.Exists(wad))
                {
                    using WadArchive archive = WadArchive.Open(wad);
                    string name = $"EnginePath{pathId}.kcs";
                    WadEntry entry = archive.Entries.FirstOrDefault(e =>
                        string.Equals(Path.GetFileName(e.Name), name, StringComparison.OrdinalIgnoreCase));
                    if (entry != null) frames = KcsCameraPath.Read(archive.Extract(entry), entry.Name).Frames;
                }
                cache[pathId] = frames;
                return frames;
            };
        }

        /// <summary>The level's own string table by line index: the subtitle text of a
        /// scripted Mentor line (0x00553780 loads it from the level's text-table byte).</summary>
        private static Func<int, string> SubtitleLoader(string install, int textTableId)
        {
            string file = LevelTextTable.FileName(textTableId);
            string path = file == null ? null : Path.Combine(install, "Data", "Text", "Default", file);
            Dk2StringTable table = null;
            try
            {
                if (path != null && File.Exists(path)) table = Dk2StringTable.Open(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"subtitles unavailable: {ex.Message}");
            }
            return line => table != null && line >= 0 && line < table.Entries.Count ? table.Entries[line].Value : null;
        }

        /// <summary>The letterbox bars and, while a path plays, the Mentor's subtitle in the
        /// bottom bar (0x0040A3E0 draws it at h - h/8, the bar's full width).</summary>
        private void DrawCinematic()
        {
            if (_scriptedView == null) return;
            float bar = _scriptedView.LetterboxBar * Screen.height;
            if (bar > 0f)
            {
                // Opaque: no camera clears outside the shrunk rect, so a transparent bar
                // (Texture2D.blackTexture's alpha is 0) left every earlier frame's text on screen.
                Color colour = GUI.color;
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, bar), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, Screen.height - bar, Screen.width, bar), Texture2D.whiteTexture);
                GUI.color = colour;
            }
            string subtitle = _scriptedView.Subtitle;
            if (subtitle == null) return;
            float height = Screen.height / 8f;
            var box = new Rect(0f, Screen.height - height, Screen.width, height);
            // The owner's order (2026-10-17): Bullfrog's DungeonKeeperII TrueType face first,
            // DK2's own bitmap font second, Unity's default last.
            Font trueType = Resources.Load<Font>("Fonts/DungeonKeeperII");
            if (trueType == null)
            {
                ClassicBitmapText bitmap = SubtitleBitmapFont(Screen.height);
                if (bitmap != null)
                {
                    bitmap.DrawCentred(box, subtitle, 1f);
                    return;
                }
            }
            _subtitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                font = trueType != null ? trueType : GUI.skin.label.font,
                alignment = TextAnchor.MiddleCenter, wordWrap = true,
                fontSize = Mathf.Max(12, Screen.height / 30),
                normal = { textColor = Color.white },
            };
            GUI.Label(new Rect(Screen.width * 0.05f, box.y, Screen.width * 0.9f, height), subtitle, _subtitleStyle);
        }

        /// <summary>DK2's own bitmap font, the fallback: the shipped size that reads at this
        /// screen height, unscaled (InterfaceFonts.LegibleFileName). Null when the install lacks it.</summary>
        private ClassicBitmapText SubtitleBitmapFont(int screenHeight)
        {
            string file = InterfaceFonts.LegibleFileName(screenHeight);
            if (file == _subtitleFontFile) return _subtitleFont;
            _subtitleFontFile = file;
            _subtitleFont?.Dispose();
            _subtitleFont = null;
            string path = _textFolder == null ? null : Path.Combine(_textFolder, file);
            try
            {
                if (path != null && File.Exists(path)) _subtitleFont = new ClassicBitmapText(Bf4FontReader.Open(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"subtitle font unavailable: {ex.Message}");
            }
            return _subtitleFont;
        }

        private void ApplyClassicCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
            int screenWidth = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            if (screenWidth <= 0) return;
            ClassicCameraPose pose = _classicCamera.Pose(
                new System.Numerics.Vector3(_cameraFocus.x, _cameraFocus.y, _cameraFocus.z),
                new CameraViewport(screenWidth, camera.pixelWidth, camera.pixelHeight));
            camera.orthographic = false;
            camera.fieldOfView = pose.VerticalFieldOfView;
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, pose.FarClipDistance);
            camera.transform.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z),
                new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z));
        }

        /// <summary>Frames the whole level, whatever shape it is: fits the level's
        /// bounding sphere inside the vertical field of view. Uses the
        /// half-diagonal so the framing cannot depend on which dimension is the
        /// larger — a camera height derived from the width alone dropped the near
        /// end of campaign level 1 (30 wide, 38 deep) off the screen, and no test
        /// can catch that; only a screenshot can.</summary>
        private void LookAt(WorldState world)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            Vector3 centre = DungeonScene.Ground(world.Width / 2f, world.Height / 2f, DungeonScene.FloorHeight);

            float radius = 0.5f * Mathf.Sqrt(
                (world.Width * (float)world.Width) + (world.Height * (float)world.Height));

            // Unity's fieldOfView is the vertical one. A wide game view has a
            // wider horizontal field, so fitting the sphere vertically always
            // fits it horizontally too.
            float halfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / Mathf.Max(0.01f, Mathf.Sin(halfFov));

            Quaternion rotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            camera.transform.rotation = rotation;
            camera.transform.position = centre - (rotation * Vector3.forward * distance);

            // A level far enough away to fit needs the far plane to match, or it
            // is clipped away rather than drawn small.
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance * 3f);
        }

        /// <summary>Loads the grid out of a DK2 saved game: terrain and ownership
        /// only. The 189-byte entity records after the grid are undecoded (Q182),
        /// so a save arrives with the dungeon the player left and nobody in it —
        /// unlike a campaign level, whose placed entities do arrive, through
        /// <c>ThingProjection</c>. The save names its own level and the reader
        /// takes the grid dimensions from that level's design map, so
        /// <c>levelName</c> is ignored here.</summary>
        private LevelMap LoadSave(string install)
        {
            string path = Path.IsPathRooted(saveFile)
                ? saveFile
                : Path.Combine(Dk2SaveReader.SaveDirectory(install), saveFile);

            if (!File.Exists(path))
                throw new InvalidOperationException($"no save at '{path}'.");

            SavedGame save = Dk2SaveReader.Load(install, path)
                ?? throw new InvalidOperationException(
                    $"'{Path.GetFileName(path)}' names a level this install has no map for.");

            // The read's own check, surfaced rather than swallowed. A correct
            // read of a played save agrees with the design map on most tiles; a
            // grid read at the wrong offset agrees on almost none. Worth seeing
            // in the log on every load, because the failure it catches produces
            // a plausible-looking dungeon of noise rather than an exception.
            _loadedFrom =
                $"{Path.GetFileName(path)} ({save.Header.LevelName}, " +
                $"{save.TerrainAgreement:P0} of terrain unchanged from the design map)";

            Debug.Log($"DK2: loaded {_loadedFrom}");

            return save.Map;
        }

        private static byte[] PlayersOf(LevelMap map)
        {
            // Whoever the map says owns something. The same rule the harness
            // uses when a scenario does not name players.
            var seen = new System.Collections.Generic.SortedSet<byte>();
            foreach (MapTile tile in map.Tiles) seen.Add(tile.PlayerId);

            var players = new byte[seen.Count];
            seen.CopyTo(players);
            return players;
        }

        private bool PointerOverHud(Vector3 position) =>
            (_minimap != null && _minimap.ContainsPointer(position)) || (_hoverHud != null && _hoverHud.ContainsPointer(position))
            || (_resourceHud != null && _resourceHud.ContainsPointer(position))
            || (_bottomPanel != null && _bottomPanel.ContainsPointer(position))
            || (_overviewPanel != null && _overviewPanel.ContainsPointer(position))
            || (_optionsPanel != null && _optionsPanel.ContainsPointer(position)) || _hudControls.FullMapVisible
            || (_queryPanel != null && _queryTarget != CreatureQueryTarget.None && !HudModal
                && _queryPanel.ContainsPointer(position, CurrentHud));

        private WorldHoverHit ResolveWorldHover(TilePos tile)
        {
            WorldState world = _simulation.World;
            PlayerId viewer = new PlayerId((byte)viewingPlayerId);
            if (world.FogOfWarEnabled && !world.Tile(tile).IsExploredBy(viewer))
                return new WorldHoverHit(WorldHoverKind.Tile, tile);
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            TileView shown = _worldView.TileAt(world, tile.X, tile.Y);
            var plane = new Plane(Vector3.up, new Vector3(0, shown.Solid ? DungeonScene.SolidHeight : DungeonScene.OpenHeight, 0));
            if (!plane.Raycast(ray, out float distance)) return new WorldHoverHit(WorldHoverKind.Tile, tile);
            WorldHoverHit hit = _scene.PickHoverEntity(ray, distance + .02f, tile, ray.GetPoint(distance));
            return new WorldHoverHit(hit.Kind, tile, hit.EntityId);
        }

        private void CaptureWorldHover()
        {
            HudVisibility visibility = CurrentHud;
            _hoverHud?.SetVisibility(visibility);
            _resourceHud?.SetVisibility(visibility);
            if (visibility.ShowNormalHud) _resourceHud?.Draw(_resources, default,
                unchecked((uint)(Time.unscaledTimeAsDouble * 1000)), _scriptedView.ResourceFlash);
            _hoverSnapshot = HoverTextSnapshot.Empty;
            Vector3 mouse = Input.mousePosition;
            bool inside = mouse.x >= 0 && mouse.y >= 0 && mouse.x < Screen.width && mouse.y < Screen.height;
            bool allowed = visibility.AllowOrdinaryInput && Application.isFocused && inside;
            HandCursorKind cursor = HandCursorKind.Point;
            TilePos? highlightedTile = null;
            _debugHoverTile = null;
            if (allowed && !PointerOverHud(mouse) && Camera.main != null && ScreenToTile(mouse) is TilePos tile)
            {
                WorldState world = _simulation.World;
                _debugHoverTile = tile;
                WorldHoverHit hit = ResolveWorldHover(tile);
                {
                    PlayerId viewer = new PlayerId((byte)viewingPlayerId);
                    string personalName = hit.Kind == WorldHoverKind.Creature && world.CreatureById(hit.EntityId) is CreatureState named
                        ? _identityNames.Name(new CreatureNameIndex(named.NameIndex)) : "";
                    _hoverSnapshot = _hoverText.Capture(world, hit, personalName);
                    bool pickup = false;
                    if (hit.Kind == WorldHoverKind.Creature && world.CreatureById(hit.EntityId) is CreatureState creature)
                        pickup = HandRules.CanPickUp(world, _simulation.Database, creature, viewer);
                    else if (hit.Kind == WorldHoverKind.Object && world.ObjectById(hit.EntityId) is ObjectState obj)
                        pickup = HandRules.CanPickUpObject(world, _simulation.Database, obj, viewer)
                            && HandRules.MayReachObject(world, _simulation.Database, obj, viewer);
                    bool dig = HandCursor.ShowsDig(world, _simulation.Database, tile, viewer);
                    if (ActiveTool == PlayerToolMode.Hand)
                        cursor = HandCursor.ForHover(hit.Kind == WorldHoverKind.Tile, dig, pickup);
                    if (cursor == HandCursorKind.Dig) highlightedTile = tile;
                    if (ActiveTool == PlayerToolMode.Hand) _scene.DrawHoverOverlay(hit);
                }
            }
            if (allowed && PointerOverHud(mouse))
            {
                _hoverSnapshot = CaptureHudHover();
                // Over the HUD DK2 shows the small pointing finger, state 2, which 0x0040BCB6 and
                // 0x0040BEFE set while interface +0x12B2 (pointer over the GUI) is set (LIKELY;
                // its producer at 0x00403D1C..0x00403D69 is partly read; owner-observed in play).
                if (ActiveTool == PlayerToolMode.Hand) cursor = HandCursorKind.SmallPoint;
            }
            bool holding = _simulation.World.Player(new PlayerId((byte)viewingPlayerId)).Hand.Count != 0;
            if (ActiveTool == PlayerToolMode.Hand && !_queryTool && !HudModal)
                cursor = _cursorFeedback.Resolve(cursor, holding, HoldingGold(), Input.GetMouseButton(0), (long)(Time.unscaledTimeAsDouble * 1000));
            _handCursor?.Update(cursor, allowed, Time.unscaledTimeAsDouble);
            Vector2 pointerGui = new Vector2(mouse.x, Screen.height - mouse.y);
            _handCursor?.Draw(pointerGui);
            float reach = cursor == HandCursorKind.Default ? 80
                : HandCursor.Sprite(cursor).FrameSize.X - HandCursor.Sprite(cursor).Hotspot.X;
            _hoverHud?.Draw(_hoverSnapshot, pointerGui, reach);
            _tileHighlight?.Update(highlightedTile, Camera.main, Time.unscaledTimeAsDouble);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                _handCursor?.Update(HandCursorKind.Default, false, Time.unscaledTimeAsDouble);
                _bottomPanel?.CancelPointerGesture(); _input.CancelPointerGesture();
                _cursorFeedback.Reset();
            }
        }

        private void OnDisable()
        {
            _bottomPanel?.CancelPointerGesture();
            _cursorFeedback.Reset();
            _handCursor?.Update(HandCursorKind.Default, false, 0);
        }

        private void OnGUI()
        {
            DrawCinematic();
            if (showDiagnostics) DrawDiagnostics();
        }

        private void DrawDiagnostics()
        {
            _frameOverlay.Draw();

            if (_failure != null)
            {
                GUI.Label(new Rect(10, 10, 900, 60), $"DK2 could not start: {_failure}");
                return;
            }

            if (_simulation == null) return;

            string art = _assets == null ? "" : _assets.Summarise();

            // Built in Update: the world may be mid-tick on the worker thread while OnGUI runs.
            float offset = _resourceHud?.Bottom ?? _hoverHud?.StatusBounds.yMax ?? 0;
            GUI.Label(new Rect(10, 10 + offset, 1200, 20), _diagnostics);

            GUI.Label(new Rect(10, 30 + offset, 1200, 20),
                $"art: {art}" + (_classic == null ? "" : $"   shader: {_classic.ShaderName}"));

            GUI.Label(
                new Rect(10, 50 + offset, 1200, 20),
                $"tool: {tool}" +
                (tool == DungeonToolMode.BuildRoom ? $" (room {selectedRoomId})" : "") +
                "   1/5 hand  3 build  4 sell  7 create imp  [/] room");

            GUI.Label(new Rect(10, 70 + offset, 1200, 20),
                "hand: left pick up creature / toggle excavation (drag rectangle); right drop held creature / slap");

            if (_loadedFrom != null)
                GUI.Label(new Rect(10, 110 + offset, 1200, 20), $"loaded from save: {_loadedFrom} — terrain and ownership only, no creatures or objects (Q182)");

            GUI.Label(
                new Rect(10, 90 + offset, 1200, 20),
                "tick rate is SimulationTiming.TicksPerSecond, the unmeasured Q1 placeholder — " +
                "tick counts are faithful, wall-clock speed is not a claim");
        }
    }
}
