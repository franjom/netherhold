// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DK2.Art;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Camera;
using DK2.Data.Model;
using DK2.Data.Settings;
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
        [SerializeField] private GameSessionMode sessionMode = GameSessionMode.Campaign;
        [SerializeField] private FrontEndPage frontEndReturnPage = FrontEndPage.None;
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
        // The live tool (DK2.Presentation): the two fields above are its start, and LoadLevel copies it back into them.
        private readonly HudToolState _toolState = new HudToolState();

        [Header("Running")]
        [Tooltip("Multiplies the simulation's own tick rate. 0 pauses; 1 is the placeholder rate (Q1).")]
        [SerializeField] private float speed = 1f;
        [SerializeField] private bool showDiagnostics = true;

        /// <summary>Ticks run on a worker thread after the frame has been presented, while Unity
        /// renders it, so a tick costs the next frame only what it exceeds the frame's own time by
        /// (ADR-0458). The view shows a tick one frame after it ran. Off, ticks run inside Update
        /// before the present, as they did.</summary>
        [SerializeField] private bool tickOnWorkerThread = true;

        /// <summary>Whole ticks one frame may run (<see cref="TickPacer.DefaultMaxTicksPerFrame"/>).</summary>
        [SerializeField] private int maxTicksPerFrame = TickPacer.DefaultMaxTicksPerFrame;

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
        [Tooltip("Tint on Unity's sky and sun fill: DK2's fill is dimmer and warmer (ADR-0522, owner's SHOT0065-0078).")]
        [SerializeField] private Color classicFillTint = new Color(0.55f, 0.44f, 0.28f);
        [Tooltip("The share of the view distance (from the eye) where the fade to black starts.")]
        [Range(0f, 1f)]
        [SerializeField] private float classicFadeStart = 0.5f;
        [Tooltip("The share of the view distance where the scene is black; at or below the start the fade is off.")]
        [Range(0f, 1f)]
        [SerializeField] private float classicFadeEnd = 1f;
        [Tooltip("The share of the fill an unexplored, unrevealed tile keeps: DK2 shows it dark but not black.")]
        [Range(0f, 1f)]
        [SerializeField] private float classicUnlitFill = 0.25f;
        [Tooltip("The brightness of DK2's own light at which the fill has faded out: the fill lifts only what DK2's lights leave dark. 0 adds it everywhere.")]
        [Range(0f, 2f)]
        [SerializeField] private float classicFillFloor = 0.5f;
        [Tooltip("Projected 32-pixel silhouettes from each creature's current animated mesh.")]
        [SerializeField] private bool classicCreatureShadows = true;

        [Tooltip("Bend the classic terrain as DK2 does (the terrain lattice, Q72).")]
        [SerializeField] private bool bendTerrain = true;

        [Tooltip("DK2's HighWalls display option, on by default; off rests walls at about half height. " +
                 "Kept for parity with DK2's config key; not offered to players in the options menu (owner, 2026-11-15).")]
        [SerializeField] private bool highWalls = true;

        [Header("Camera")]
        [Tooltip("Original camera profile used when Play starts.")]
        [SerializeField] private ClassicCameraMode cameraPreset = ClassicCameraMode.Dungeon;
        [Tooltip("Diagnostic whole-map overview instead of the original playing camera.")]
        [SerializeField] private bool frameWholeMap;
        [Tooltip("Pitch for the diagnostic whole-map overview.")]
        [Range(20f, 90f)]
        [SerializeField] private float cameraPitch = 55f;
        [Tooltip("Pan speed for the diagnostic fallback camera.")]
        [SerializeField] private float cameraPanSpeed = 12f;
        [Tooltip("DK2's Scroll Speed preference; the original default is 10.")]
        [Range(5, 20)] [SerializeField] private int classicScrollSpeed = 10;
        [SerializeField] private float cameraZoomFraction = 0.15f;
        private const float MinCameraHeight = TileHeights.Floor + 0.5f;
        private ClassicCamera _classicCamera;
        private ClassicCameraControls _classicCameraControls;
        private readonly ClassicAmbientLight _ambientLight = new ClassicAmbientLight();
        private ClassicPointLight? _handLight;
        internal Func<EffectTime> EffectClock { get; set; }
        private Vector3 _cameraFocus;
        private Vector2 _cameraBounds;

        // With TickMarker and PresentMarker below, so the Profiler separates input, the
        // position snapshot and the tick itself (whose systems UnitySimulationProfiler shows).
        private static readonly ProfilerMarker InputMarker = new ProfilerMarker("DK2.Frame.Input");
        private static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("DK2.Simulation.CaptureObjectPositions");
        private static readonly ProfilerMarker CopyWorldMarker = new ProfilerMarker("DK2.Simulation.CopyWorld");

        private GameSimulation _simulation;
        private ScenePresenter _presenter;
        private WorldView _worldView;
        /// <summary>The hand's local pickups and deferred drops, over <see cref="_worldView"/> (migration plan step 2c).</summary>
        private PickupFlow _pickupFlow;
        private float _shotDrawFraction = 1f;
        private readonly Plane[] _effectCameraPlanes = new Plane[6];
        private readonly object _effectCameraLock = new object();
        private bool _effectCameraReady;
        private DungeonMinimap _minimap;
        private ClassicHandCursor _handCursor;
        private ClassicPlacementOverlay _placementOverlay;
        private readonly HeldItemStack _heldStack = new HeldItemStack();
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
        // The Portal Gem's idle sparkle (0x004A3C30), collected once a frame for the turns run.
        private LevelGemIdleEffects _gemIdleEffects;
        private readonly List<ViewEffectRequest> _gemIdleRequests = new List<ViewEffectRequest>();
        private ClassicLightning _lightning;
        private Action<HeartHealthChange> _heartHealthObserver;
        private WorldState _heartHealthWorld;
        private readonly PlayerInputTranslator _input = new PlayerInputTranslator();
        private TilePos? _dragTo;
        private HandCursorEventSink _handCursorEvents;
        private readonly List<PlayerCommand> _pendingCommands = new List<PlayerCommand>();
        private TerrainLattice _terrainLattice;
        private ScriptedViewDirector _scriptedView;
        private Vector3 _appliedCameraJitter;
        /// <summary>Action 26's focus glide (<see cref="CameraGlide"/>, migration plan step 2i).</summary>
        private readonly CameraGlide _glide = new CameraGlide();
        private GUIStyle _subtitleStyle, _titleStyle;
        private string _textFolder;
        private string _subtitleFontFile;
        private ClassicBitmapText _subtitleFont;
        private readonly TickPacer _tickPacer = new TickPacer();
        private double _tickDebt => _tickPacer.Debt;
        /// <summary>The frame order around this runner's camera, HUD and world input (migration plan step 2h).</summary>
        private FrameDriver _frameDriver;
        /// <summary>The world pointer's gates, from the HUD press to the world command (migration plan step 2h).</summary>
        private WorldPointerInput _worldPointer;

        // The presented world (ADR-0694): the frame reads this copy while the worker ticks the live
        // one. The worker copies live into the spare after its batch; the frame takes the spare over,
        // in place, once the draw clock reaches the batch, so the frame neither waits nor clamps.
        private WorldState _presented, _spare;
        private DeferredEventSink _deferredEvents;
        private int _ticksPending;
        private long _tickStartedStamp;
        private bool _tickObserved;
        private double _pendingTickMs;

        /// <summary>The world this frame reads: the presented copy on the worker path, the simulation's
        /// own on the main-thread path and in the front end.</summary>
        private WorldState World => _presented ?? _simulation.World;
        private string _failure;
        private string _loadedFrom;
        private Task _tickTask;
        // The background decode of the later art (ADR-0688): cancelled and joined before the art
        // provider is disposed. The method compilation on a worker runs once per process.
        private Task _laterPrefetch;
        private CancellationTokenSource _laterPrefetchCancel;
        private static Task _jitWarmup;
        private ulong _stateHash;
        private string _diagnostics = "";
        private readonly DebugFrameOverlay _frameOverlay = new DebugFrameOverlay();
        private FrameRecorder _recorder;
        // The worker's time in the last frame's ticks, read by the frame that joins them.
        private double _recordedTickMs;
        private InstallHudSources _hudSources;
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

        /// <summary>A player started by a script (the tester package, docs/PERFORMANCE.md) names its
        /// level, seed and install on the command line; otherwise the Inspector's values stand.</summary>
        private void ApplyLaunchArguments()
        {
            string[] args = Environment.GetCommandLineArgs();
            LaunchArguments launch = LaunchArguments.Parse(args, "-");
            if (launch.Level != null)
            {
                sessionMode = GameSessionMode.Campaign;
                levelName = launch.Level;
                saveFile = "";
            }
            if (launch.Seed is ulong value) seed = value;
            if (launch.Install != null) installDirectory = launch.Install;
            // A capture names its frame cap and VSync, so a run on other hardware is comparable.
            string cap = LaunchArguments.Value(args, "-frameCap");
            if (cap != null && int.TryParse(cap, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out int frameCap))
                Application.targetFrameRate = frameCap;
            string vsync = LaunchArguments.Value(args, "-vsync");
            if (vsync != null && int.TryParse(vsync, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int vSyncCount))
                QualitySettings.vSyncCount = vSyncCount;
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
            InitializeSettings();
            ApplyLaunchArguments();
            _recorder = FrameRecorder.TryCreate();
            bool frontEnd = sessionMode == GameSessionMode.FrontEnd;
            if (frontEnd)
            {
                levelName = GameSessionRequest.FrontEnd().LevelName;
                saveFile = "";
            }
            _toolState.StartWith(tool, selectedRoomId);
            _context = new SessionContext(levelName, sessionMode, frontEndReturnPage);
            _resultFlow = new CampaignResultFlow(_context, _profiles, LoadFrontEndProfile, _hudControls);

            string install = string.IsNullOrWhiteSpace(installDirectory)
                ? Dk2Install.Find()
                : installDirectory;

            if (install == null)
                throw new InvalidOperationException(
                    "no Dungeon Keeper 2 installation found. Set the Install Directory field, " +
                    "or the DK2_DIR environment variable.");
            _startup.Step("install");

            bool campaignLevel = string.IsNullOrWhiteSpace(saveFile);
            // A campaign level's records are the shared loader's (the Godot adapter starts levels the same way).
            CampaignLevelData campaign = campaignLevel ? CampaignLevelData.Load(install, levelName) : null;
            GameDatabase database = campaign?.Database ?? GameDatabaseLoader.Load(install);
            _startup.Step("database");

            Func<GameSimulation> warmupCopy = null;
            LevelMap map = campaign?.Map ?? LoadSave(install);

            int cameraTicksPerSecond = GameSessionStarter.CameraTicksPerSecond(campaign);
            if (campaignLevel)
            {
                _resultFlow.LevelTicksPerSecond = campaign.Level.TicksPerSecond;
                _simulation = GameSessionStarter.Campaign(campaign, _resumeSimulation, frontEnd, seed, fogOfWar);
                if (!frontEnd && _resumeSimulation == null)
                    warmupCopy = () => campaign.CreateSimulation(seed, fogOfWar);
            }
            else _simulation = GameSessionStarter.FromDk2Save(database, map, seed, fogOfWar);
            _simulation.ProfileWith(new UnitySimulationProfiler());
#if !ENABLE_IL2CPP
            // A second copy of the level ticks on a worker while this one loads, so the live ticks
            // run compiled code (ADR-0689). Mono only: compiled ahead, it cost more loading than it saved.
            if (warmupCopy != null)
                SimulationWarmup.Start(warmupCopy).ContinueWith(done => Debug.Log(done.IsFaulted
                    ? $"DK2: simulation warm-up failed: {done.Exception?.GetBaseException().Message}"
                    : $"DK2: simulation warm-up ticked {SimulationWarmup.DefaultTicks} in {done.Result.TotalMilliseconds:F0} ms"));
#endif
            WorldState world = _simulation.World;
            if (!frontEnd) world.LocalPlayerId = new PlayerId((byte)viewingPlayerId);
            if (!frontEnd) PrepareCampaignResult(resumed: _resumeSession != null);
            if (frontEnd)
            {
                LoadFrontEndProfile();
                _frontEndScenery = FrontEndScenery.Create(world, database, _profiles.Profile, new SimRandom(seed), _profiles.SecretFiveAvailable)
                    .Concat(FrontEndScenery.CreateCampaignMarkers(database)).ToArray();
                _frontEndBaseScenery = _frontEndScenery;
            }
            _toolState.SelectKnownRoom(database.Rooms.Keys);
            _startup.Step("level");

            // The chain the plan specifies (§7.2), with the install read in place as the
            // converted tier (ADR-0408). Both roots are relative to the project directory and
            // normally missing; with the install off too, everything draws as boxes.
            if (classicMeshes)
            {
                _installArt = InstallArtProvider.Open(install, database, frontEnd
                    ? FrontEndArt.Entries(database).Concat(StatusFlowerArt.Entries) : StatusFlowerArt.Entries);
                _classic = new ClassicMeshes(_installArt, classicVertexLighting, nativeInitialAmbient);
            }
            _assets = AssetProviderChain.Standard(
                replacementRoot: "art-pack",
                convertedRoot: "build/converted",
                install: _installArt);
            _startup.Step("art");

            // The first frame's art decodes on workers while the scene is built and the first tick
            // runs; Present waits for it. The keys are read here, before anything else reads the world.
            // What the first tick adds, and the clips creatures change to, decode in the background
            // after the tick (the later keys below).
            Task prefetch = null;
            IReadOnlyCollection<AssetRef> firstFrame = null;
            InstallArtProvider art = _installArt;
            int workers = PrefetchWorkers(); // SystemInfo is main-thread only
            if (art != null && prefetchFirstFrameArt)
            {
                firstFrame = FirstFrameArt.Keys(database, world, new PlayerId((byte)viewingPlayerId), _frontEndScenery);
                prefetch = Task.Run(() => art.Prefetch(firstFrame, workers));
                _startup.Step("prefetch.keys");
            }

            TerrainLattice lattice = _classic != null && bendTerrain
                ? new TerrainLattice(database, world, (byte)viewingPlayerId) { HighWalls = highWalls }
                : null;
            _terrainLattice = lattice;
            _scene = new DungeonScene(transform, world.Width, world.Height, _classic, lattice, classicCreatureShadows, cheapLighting);
            if (EffectClock != null) _scene.HeartEffectClock = EffectClock;
            _worldView = new WorldView(database, (byte)viewingPlayerId);
            _pickupFlow = new PickupFlow(_worldView, _cursorFeedback);
            if (_frontEndScenery != null) _worldView.FrontEndScenery = _frontEndScenery;
            _presenter = new ScenePresenter(
                _worldView, _assets, _scene, new TorchLayout(database)) { Profiler = new UnitySimulationProfiler("DK2.") };
            _startup.Step("scene");
            if (_installArt != null)
            {
                var sprites = new InstallEffectSpriteProvider(database, _installArt.LoadTexture);
                _scene.AttachTorchSprites(sprites, EffectClock);
                _constructionEffects = new ClassicConstructionEffects(transform, database, sprites, seed,
                    request => _worldView.CanShowTile(world, request.Tile),
                    request => _worldView.CanShowTile(world, request.Tile)
                        && !DK2.Pathfinding.NavBytes.IsSolid(_simulation.Navigation.NavByte(request.Tile.X, request.Tile.Y))
                        // DK2 asks for the Heart's opening effects only for the local player (0x004D9A60).
                        && (!HeartIntroRules.IsViewerOnly(request.EffectId.Value) || request.Owner.Value == viewingPlayerId),
                    point => _worldView.GroundHeightAt(world, point), meshes: _classic,
                    captureEdges: request => CaptureEffectLayout.Boundary(world, database, request),
                    attachment: (id, point) => ResolveCreatureEffect(world, new CreatureId(id), point), clock: EffectClock,
                    shotAttachment: id => ShotEffectAttachment.Resolve(world, _simulation.Navigation, id, _shotDrawFraction),
                    cameraVisible: IsEffectInCamera,
                    terrainFlags: tile => world.Contains(tile)
                        ? database.Terrain[world.Tile(tile).TerrainId].Flags : 0,
                    solidTile: tile => !world.Contains(tile)
                        || DK2.Pathfinding.NavBytes.IsSolid(_simulation.Navigation.NavByte(tile.X, tile.Y)),
                    shotVisible: shot => _worldView.CanShowTile(world, new TilePos(shot.PosX >> 12, shot.PosY >> 12)),
                    creatureGroundHeight: id => world.CreatureById(id.Value) is CreatureState creature
                        ? _worldView.CreatureEffectGround(world, creature) : (Fp32?)null,
                    viewer: new PlayerId((byte)viewingPlayerId));
                _scene.HeartEffectRequested = request => _constructionEffects.Model.Request(request);
                _lightning = new ClassicLightning(transform, _installArt.LoadTexture, _constructionEffects.Model, unchecked((uint)seed));
                _scene.Lightning = _lightning;
                if (_scene.HasClassicLighting) _constructionEffects.TileLights = _scene.TryGetTileLights;
                _gemIdleEffects = new LevelGemIdleEffects(database);
                // 0x0043A8E0's owner is the render sink's word +0xC, whose writer is unread; taken as the viewer.
                _scene.GemSparkleRequested = point => _constructionEffects.Model.Request(new ViewEffectRequest(
                    new EffectId(CreatureView.GemDustSparkle.EffectId), new PlayerId((byte)viewingPlayerId), point));
            }
            _startup.Step("effects");
            _soundPlayer = new ClassicSoundPlayer(transform, new InstallClassicSoundAssets(install));
            // The level's likely sound clips decode on workers beside the art; the rest decode on first use, also off
            // this thread, so no frame decodes a clip. Read before the first tick, like the art keys.
            Task soundPrefetch = frontEnd ? null : _soundPlayer.Prefetch(LevelSoundPrefetch.Categories(database, world,
                new DK2.Data.Media.SoundCategory(database.Level?.SoundCategory.ToLowerInvariant()
                    ?? DK2.Data.Media.SoundCategory.Mentor.Name)), workers);
            // DK2 plays a Casino party's pop only when it created the party's effect (0x004D65E9).
            if (_constructionEffects != null)
                _constructionEffects.Model.EffectCreated = effect =>
                {
                    SoundRequest? pop = PartyPopSound.For(effect);
                    if (pop.HasValue) _soundPlayer.Play(pop.Value);
                };
            string heartCategory = HeartBeatSound.CategoryOf(database);
            _scene.HeartBeatRequested = pose =>
            {
                SoundRequest? beat = HeartBeatSound.For(heartCategory, pose);
                if (beat.HasValue) _soundPlayer.Play(beat.Value);
            };
            // The footstep marks are DK2's (0x0043CD84); the player's throttle and voice rules judge each request.
            _scene.CreatureFootstepRequested = (id, pose, crossing, position) =>
            {
                // The pass draws only what the camera sees; ours draws every revealed creature, so the sound is gated the same way.
                if (!IsPointInCamera(position)) return;
                SoundRequest? step = CreatureFootsteps.For(pose.SoundCategory, id, crossing, pose.Wading,
                    new System.Numerics.Vector3(position.x, position.y, position.z));
                if (step.HasValue) _soundPlayer.Play(step.Value);
            };
            ApplySoundSettings(_gameSettings);
            if (frontEnd)
            {
                WaitForPrefetch(prefetch);
                StartFrontEnd(install, world);
                Debug.Log(_startup.Summary(_installArt));
                return;
            }
            _textFolder = LevelSubtitles.TextFolder(install);
            _messageView = new MessageTabsView(new PlayerId((byte)viewingPlayerId), database);
            ISimulationEventSink effects = (ISimulationEventSink)_constructionEffects?.Model ?? NullEventSink.Instance;
            effects = new Level6aTortureSpeech(database, _messageView, effects);
            if (_constructionEffects != null)
            {
                _worldView.LocalPickups.DustRequested += request => _constructionEffects.Model.RequestPickupDust(request);
                effects = new LocalPickupEffectSink(_worldView.LocalPickups, effects);
            }
            _scriptedView = new ScriptedViewDirector(new PlayerId((byte)viewingPlayerId),
                EngineCameraPaths.Loader(install),
                LevelSubtitles.Open(install, database.Level?.TextTableId ?? 0, message => Debug.LogWarning(message)).Line,
                effects);
            _scriptedView.GuiVisibilityRequested += _hudControls.SetGuiVisible;
            _frameDriver = new FrameDriver(_pendingCommands, _worldView, _tickPacer, _journal);
            WireSpeech(database);
            PrepareWinScreen(database);
            _worldSounds = new WorldSoundDirector(_scriptedView, new PlayerId((byte)viewingPlayerId));
            _handCursorEvents = new HandCursorEventSink(_cursorFeedback, new PlayerId((byte)viewingPlayerId),
                new VisibilityEventSink(_worldView, () => World, new PlayerId((byte)viewingPlayerId), _worldSounds));
            // On the worker path the sinks write the view, so the tick thread only queues the events
            // and the frame replays them when it takes the batch over (ADR-0694).
            _deferredEvents = !frontEnd && tickOnWorkerThread ? new DeferredEventSink(_handCursorEvents) : null;
            _simulation.ObserveWith((ISimulationEventSink)_deferredEvents ?? _handCursorEvents);

            int maximumHeartHealth = database[VariableTypeId.DungeonHeartObjectHealth];
            _heartHealthObserver = change => _scene.QueueHeartHealthChange(change.Owner,
                DungeonHeartVisual.FromHealth(currentHealth: change.Health, maximumHealth: maximumHeartHealth));
            world.HeartHealthChanged += _heartHealthObserver;
            _heartHealthWorld = world;
            _startup.Step("script");
            // The level's first turn runs before its first frame (CampaignLevelData.TicksBeforeFirstFrame), and
            // the effect model one update; a restored checkpoint already includes its starting turn.
            if (_resumeSession == null) RunTicks(CampaignLevelData.TicksBeforeFirstFrame, InputFrame.Empty);
            if (!frontEnd && tickOnWorkerThread)
            {
                _presented = WorldState.CopyOf(_simulation.World);
                _spare = WorldState.CopyOf(_simulation.World);
                _worldView.PresentObjectPositions(_presented);
                _deferredEvents.Replay();
            }
            _constructionEffects?.Model.UpdateNow();
            _startup.Step("tick");
            if (firstFrame != null)
            {
                // Decoded in the background during the first seconds of play; a frame that gets there
                // first still decodes on request (ADR-0688).
                IReadOnlyCollection<AssetRef> later = FirstFrameArt.LaterKeys(database, world, new PlayerId((byte)viewingPlayerId), firstFrame);
                int spare = Math.Max(1, workers - 1);
                _laterPrefetchCancel = new CancellationTokenSource();
                CancellationToken cancellation = _laterPrefetchCancel.Token;
                _laterPrefetch = Task.Run(() => art.Prefetch(later, spare, cancellation));
                _startup.Step("prefetch.later");
            }
            WaitForPrefetch(prefetch);
            _startup.Step("prefetch.wait");
            WaitForPrefetch(soundPrefetch, "sound");
            _startup.Step("sound.prefetch");
            Debug.Log($"DK2: sound prefetch decoded {_soundPlayer.DecodedMegabytes:F1} MB of samples");
            double[] artBefore = _classic?.Breakdown.Snapshot();
            _presenter.Present(world);
            _startup.Step("present");
            _startup.Breakdown("present", artBefore, _classic?.Breakdown.Snapshot());
            IReadOnlyList<CameraThing> presets = GameDatabaseLoader.LoadCameraPresets(install, campaignLevel ? levelName : null);
            CameraThing profile = ClassicCameraSetup.Preset(presets, cameraPreset);
            _possession = new PossessionSession(database, ClassicCameraSetup.PossessionPreset(presets));
            if (classicMeshes && profile != null)
            {
                ClassicCameraSetup setup = ClassicCameraSetup.Create(profile, ClassicCameraSetup.DefaultZoomOutLimitScale, cameraTicksPerSecond,
                    classicScrollSpeed, campaignLevel ? campaign.Players : null, new PlayerId((byte)viewingPlayerId),
                    world.Width, world.Height);
                _classicCamera = setup.Camera;
                _classicCameraControls = setup.Controls;
                _cameraBounds = new Vector2(world.Width, world.Height);
                _cameraFocus = ToUnity(setup.StartFocus);
            }
            if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
            else LookAt(world);
            ApplyGameSettings(_gameSettings);
            _startup.Step("camera");
            var minimapAssets = new InstallMinimapAssets(install, _installArt != null ? _installArt.LoadTexture : null);
            _startup.Step("minimap.assets");
            _minimap = new DungeonMinimap(transform, minimapAssets, world.Width, world.Height);
            _startup.Step("minimap.build");
            _minimap.Capture(world, _worldView, _simulation.Navigation);
            _startup.Step("minimap.capture");
            _minimap.Draw(Camera.main, _cameraFocus);
            _startup.Step("minimap.draw");
            _hudSources = new InstallHudSources(install, _installArt);
            // Parsed on workers while the HUD is built, so no first draw parses a font (ADR-0688).
            _hudSources.PrefetchFonts(InterfaceFonts.HudFileNames(Screen.height));
            _handCursor = new ClassicHandCursor(new InstallHandCursorAssets(install));
            _handCursor.Prepare();
            _startup.Step("hud.cursor");
            _tileHighlight = new ClassicTileHoverOutline(transform, lattice);
            _placementOverlay = new ClassicPlacementOverlay(transform, new InstallClassicResourceAssets(_hudSources));
            _input.RubberBandLimit = PlacementCursor.RubberBandLimit(_simulation.Database);
            _startup.Step("hud.outline");
            _hoverHud = new ClassicHoverHud(new InstallClassicHoverAssets(_hudSources));
            _startup.Step("hud.hover");
            _resourceHud = new ClassicResourceHud(transform, new InstallClassicResourceAssets(_hudSources));
            _resourceView = new KeeperResourceView(_simulation.Database, new PlayerId((byte)viewingPlayerId));
            _startup.Step("hud.resources");
            InitializeHudControls(install, database, campaignLevel);
            _hoverText = new WorldHoverText(database, _hudSources.Strings.Resolve, new PlayerId((byte)viewingPlayerId),
                CampaignSlot.ForLevel(levelName));
            _startup.Step("hud.text");
            _scene.SetPortalLights(_worldView.PortalLightTiles(World));
            _scene.SetObjectLights(_worldView.LightObjects(World), 1);
            if (_constructionEffects != null) _scene.SetEffectLights(_constructionEffects.Advance());
            if (_classicCamera != null)
                _scene.PrepareClassicLighting(Camera.main, _classicCamera.NativeViewDistance);
            _startup.Step("lights");
            Debug.Log(_startup.Summary(_installArt));
            // The adapter's and the presentation's methods compile on a worker during the first
            // seconds of play, so no first-called path pays Mono's JIT in a frame (ADR-0688: not
            // during loading, and not the simulation, which cost load time for nothing).
            _jitWarmup ??= JitWarmup.Run(typeof(DungeonRunner).Assembly, typeof(WorldView).Assembly);
            if (!frontEnd) BeginHudWarmUp(2);
        }

        /// <summary>Threads the first frame's art decodes on, at most; fewer on a machine with fewer cores.</summary>
        private const int MaxPrefetchWorkers = 8;

        private static int PrefetchWorkers() => Math.Max(1, Math.Min(SystemInfo.processorCount - 1, MaxPrefetchWorkers));

        // What the game probe reads each frame, typed rather than by reflection.
        internal DungeonScene Scene => _scene;
        internal ScriptedViewDirector ScriptedView => _scriptedView;
        internal bool TicksOnWorker => tickOnWorkerThread;
        /// <summary>Without joining a worker tick, unlike <see cref="Simulation"/>: a probe that
        /// asked every frame moved the join out of <see cref="Update"/>, where the game pays it.</summary>
        internal bool HasStarted => _simulation != null;

        /// <summary>Waits for the art prefetch. A failure is only a warning: the art then loads on
        /// request, which reports a real problem as it always has.</summary>
        private static void WaitForPrefetch(Task prefetch, string what = "art")
        {
            if (prefetch == null) return;
            try
            {
                prefetch.Wait();
            }
            catch (AggregateException ex)
            {
                Debug.LogWarning($"DK2: {what} prefetch failed — {ex.GetBaseException().Message}");
            }
        }

        private HitchReport _hitches;

        /// <summary>After startup, a frame slower than <see cref="Threshold"/> logs the art loaded on first use in that
        /// frame, by kind, so a stall in play (a cast's first effects, a creature's first clip) is named, not guessed.
        /// Allocation-free on ordinary frames.</summary>
        private sealed class HitchReport : IDisposable
        {
            private const double Threshold = 50;
            // The frame's own time in each of these, from the profiler (development and editor
            // players record them), so a hitch names where the frame went, not only first-use art.
            private static readonly string[] Markers =
            {
                "DK2.Simulation.WaitForTick", "DK2.Simulation.Tick", "DK2.Simulation.CaptureObjectPositions", "DK2.Simulation.CopyWorld",
                "DK2.Scene.Present", "DK2.Scene.AnimatedFrame", "DK2.Scene.CreateCreature", "DK2.Scene.PrepareLighting",
                "DK2.TerrainChunks.Rebuild", "DK2.TerrainChunks.Geometry", "DK2.TerrainChunks.Prepare",
                "DK2.TerrainChunks.Join", "DK2.TerrainChunks.Upload",
                "DK2.TransparentTerrain.SetChunk", "DK2.TransparentTerrain.Sort", "DK2.TransparentTerrain.Cull",
                "DK2.TransparentTerrain.DepthOrder", "DK2.TransparentTerrain.UploadRuns",
                "DK2.Animation.Sample", "DK2.Animation.MeshUpload", "DK2.Effects.Draw",
                "DK2.Frame.Hover", "DK2.Frame.Hud", "DK2.Frame.Flowers", "DK2.Frame.Minimap", "DK2.Frame.WorldSound",
                "DK2.Shadows.Selection", "DK2.Shadows.Silhouette", "DK2.Shadows.ReceiverUpload",
                "GC.Collect",
            };
            private readonly global::Unity.Profiling.ProfilerRecorder[] _recorders = new global::Unity.Profiling.ProfilerRecorder[Markers.Length];
            private double[] _before = new double[(int)ClassicMeshes.LoadBreakdown.Work.Count];
            private double[] _after = new double[(int)ClassicMeshes.LoadBreakdown.Work.Count];
            private bool _primed;
            private int _models, _images;

            public HitchReport()
            {
                for (int i = 0; i < Markers.Length; i++)
                    _recorders[i] = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Scripts, Markers[i]);
            }

            public void Dispose()
            {
                for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
            }

            public string Frame(double milliseconds, ClassicMeshes.LoadBreakdown breakdown, InstallArtProvider art, long tick)
            {
                breakdown.Snapshot(_after);
                int models = art?.ModelsDecoded ?? 0, images = art?.ImagesDecoded ?? 0;
                string line = null;
                if (_primed && milliseconds > Threshold)
                {
                    var text = new System.Text.StringBuilder("DK2 hitch: ");
                    text.Append(milliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                        .Append(" ms at tick ").Append(tick).Append("; first-use loads:");
                    for (var work = ClassicMeshes.LoadBreakdown.Work.Terrain; work < ClassicMeshes.LoadBreakdown.Work.Count; work++)
                    {
                        double spent = _after[(int)work] - _before[(int)work];
                        if (spent >= 0.5)
                            text.Append(' ').Append(work.ToString().ToLowerInvariant()).Append('=')
                                .Append(spent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    text.Append("; decoded ").Append(models - _models).Append(" models, ").Append(images - _images).Append(" images; spent:");
                    for (int i = 0; i < _recorders.Length; i++)
                    {
                        if (!_recorders[i].Valid) continue;
                        double spent = _recorders[i].LastValue / 1e6;
                        if (spent >= 2)
                            text.Append(' ').Append(Markers[i]).Append('=')
                                .Append(spent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    line = text.ToString();
                }
                (_before, _after) = (_after, _before);
                _models = models; _images = images; _primed = true;
                return line;
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

        private bool _runtimeReleased;
        private void OnDestroy() => ReleaseRuntimeResources();

        private void ReleaseRuntimeResources()
        {
            if (_runtimeReleased) return;
            _recorder?.Write(levelName, seed); _recorder = null;
            _hitches?.Dispose(); _hitches = null;
            _runtimeReleased = true;
            WaitForTick();
            _presented = _spare = null;
            EndPossessionView();
            _deferredEvents = null;
            _ticksPending = 0;
            CloseInputJournals("destroyed");
            DisposeHudControls();
            _soundPlayer?.Dispose(); _soundPlayer = null;
            _frontEndMenu?.Dispose();
            if (_frontEndMovie != null) Destroy(_frontEndMovie.gameObject);
            _campaignMap?.Dispose(); _campaignMap = null;
            _minimap?.Dispose();
            _handCursor?.Dispose();
            _tileHighlight?.Dispose();
            _placementOverlay?.Dispose();
            _settingsRendering?.Dispose();
            _hoverHud?.Dispose();
            _resourceHud?.Dispose();
            if (_heartHealthWorld != null && _heartHealthObserver != null)
                _heartHealthWorld.HeartHealthChanged -= _heartHealthObserver;
            // Users of the shared art first, then the art (performance audit L1/L3): the effects and
            // every scene object, chunk and liquid mesh stop drawing before ClassicMeshes destroys the
            // meshes, materials and textures they drew with. Destroy is deferred to the frame's end,
            // but nothing renders between, and this order holds if any of it becomes immediate.
            _constructionEffects?.Dispose();
            _lightning?.Dispose();
            _scene?.Dispose();
            _classic?.Release();
            _subtitleFont?.Dispose();
            _subtitleFont = null;
            _subtitleFontFile = null;
            _laterPrefetchCancel?.Cancel();
            WaitForPrefetch(_laterPrefetch, "later art");
            _laterPrefetchCancel?.Dispose();
            _laterPrefetchCancel = null;
            _laterPrefetch = null;
            _installArt?.Dispose();
            _installArt = null;
        }

        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("DK2.Simulation.Tick");
        private static readonly ProfilerMarker WaitTickMarker = new ProfilerMarker("DK2.Simulation.WaitForTick");
        private static readonly ProfilerMarker PresentMarker = new ProfilerMarker("DK2.Scene.Present");
        // The rest of the frame's own work, so no long stretch of Update is unaccounted for.
        private static readonly ProfilerMarker FrameSoundMarker = new ProfilerMarker("DK2.Frame.WorldSound");
        private static readonly ProfilerMarker FrameSpeechMarker = new ProfilerMarker("DK2.Frame.Speech");
        private static readonly ProfilerMarker FrameReportMarker = new ProfilerMarker("DK2.Frame.StartupReport");
        private static readonly ProfilerMarker FrameScriptMarker = new ProfilerMarker("DK2.Frame.Script");
        private static readonly ProfilerMarker FrameMinimapMarker = new ProfilerMarker("DK2.Frame.Minimap");
        private static readonly ProfilerMarker FrameLightsMarker = new ProfilerMarker("DK2.Frame.Lights");
        private static readonly ProfilerMarker FrameEffectsAdvanceMarker = new ProfilerMarker("DK2.Frame.EffectsAdvance");
        private static readonly ProfilerMarker FrameLookMarker = new ProfilerMarker("DK2.Frame.Look");
        private static readonly ProfilerMarker FrameHudMarker = new ProfilerMarker("DK2.Frame.Hud");
        private static readonly ProfilerMarker FrameHoverMarker = new ProfilerMarker("DK2.Frame.Hover");
        private static readonly ProfilerMarker FrameFlowersMarker = new ProfilerMarker("DK2.Frame.Flowers");
        private static readonly ProfilerMarker FrameJournalMarker = new ProfilerMarker("DK2.Frame.Journal");
        // Inside the hover and input passes, one per stage.
        private static readonly ProfilerMarker HoverResourcesMarker = new ProfilerMarker("DK2.Hover.Resources");
        private static readonly ProfilerMarker HoverPickMarker = new ProfilerMarker("DK2.Hover.Pick");
        private static readonly ProfilerMarker HoverCursorMarker = new ProfilerMarker("DK2.Hover.Cursor");
        private static readonly ProfilerMarker HoverHudMarker = new ProfilerMarker("DK2.Hover.Hud");
        private static readonly ProfilerMarker HoverPlacementMarker = new ProfilerMarker("DK2.Hover.Placement");
        private static readonly ProfilerMarker InputHudMarker = new ProfilerMarker("DK2.Input.Hud");
        private static readonly ProfilerMarker InputCameraMarker = new ProfilerMarker("DK2.Input.Camera");
        private static readonly ProfilerMarker InputQueueMarker = new ProfilerMarker("DK2.Input.Queue");
        private readonly FrameElapsedClock _frameElapsed = new FrameElapsedClock();
        // On the worker path the draw clock runs one frame behind the tick clock (ADR-0687).
        private readonly WorkerDrawLag _drawLag = new WorkerDrawLag();

        private EffectAttachment ResolveCreatureEffect(WorldState world, CreatureId id, uint point)
        {
            if (point != 0) return _scene.CreatureAttachment(id.Value, point);
            CreatureState creature = world.CreatureById(id.Value);
            if (creature == null) return EffectAttachment.Gone;
            var at = new FpPosition3(new Fp32(creature.LastPosX), new Fp32(creature.LastPosY), new Fp32(Fp32.One));
            return EffectAttachment.AtPreviousBody(creature, _worldView.GroundHeightAt(world, at));
        }

        private void RefreshEffectCamera()
        {
            Camera camera = Camera.main;
            lock (_effectCameraLock)
            {
                _effectCameraReady = camera != null;
                if (camera != null) GeometryUtility.CalculateFrustumPlanes(camera, _effectCameraPlanes);
            }
        }

        private bool IsEffectInCamera(ViewEffectRequest request)
        {
            return IsPointInCamera(DungeonScene.Ground(request.Position.X.Raw / 4096f,
                request.Position.Y.Raw / 4096f, request.Position.Z.Raw / 4096f));
        }

        private bool IsPointInCamera(Vector3 point)
        {
            lock (_effectCameraLock)
            {
                if (!_effectCameraReady) return true;
                // The native +80 camera query uses radius4096; use a one-tile frustum sphere.
                foreach (Plane plane in _effectCameraPlanes)
                    if (plane.GetDistanceToPoint(point) < -1f) return false;
                return true;
            }
        }

        private void Update()
        {
            JournalBeginFrame();
            _frameOverlay.Sample(showDiagnostics);
            if (_simulation == null || _failure != null) return;
            _recorder?.BeginFrame();
            if (Camera.main != null) Camera.main.transform.position -= _appliedCameraJitter;
            _appliedCameraJitter = Vector3.zero;
            long soundStarted = _recorder?.Stamp() ?? 0;
            using (FrameSoundMarker.Auto()) _soundPlayer?.PlayDecoded();
            using (FrameSpeechMarker.Auto()) _soundPlayer?.UpdateSpeech((long)(Time.unscaledTimeAsDouble * 1000));
            _recorder?.Spent(FrameRecorder.Part.Sound, soundStarted);
            if (_frontEnd != null) { if (!UpdateStartup() && !UpdateCutscene()) UpdateFrontEnd(); return; }
            FrameReportMarker.Begin();
            string settled = _startup?.Frame(Time.unscaledDeltaTime * 1000d, _installArt);
            if (settled != null) Debug.Log(settled);
            else if (_classic?.Breakdown != null)
            {
                _hitches ??= new HitchReport();
                string hitch = _hitches.Frame(Time.unscaledDeltaTime * 1000d, _classic.Breakdown, _installArt, World.CurrentTick.Value);
                if (hitch != null) Debug.LogWarning(hitch);
            }
            FrameReportMarker.End();

            // The tick started at the end of the last frame, if any, has the live world until it is
            // done; this frame reads the presented copy and takes the batch over when the draw clock
            // reaches it (ADR-0694). The wait column records that hand-over and any blocking join.
            long waitStarted = _recorder?.Stamp() ?? 0;
            PresentTick();
            _recorder?.Spent(FrameRecorder.Part.Wait, waitStarted);
            if (_simulation == null) return;
            soundStarted = _recorder?.Stamp() ?? 0;
            using (FrameSoundMarker.Auto()) UpdateWorldSound(World, _simulation.Database);
            _recorder?.Spent(FrameRecorder.Part.Sound, soundStarted);
            double elapsed = _frameElapsed.Sample(Time.realtimeSinceStartupAsDouble,
                fallbackDeltaSeconds: Time.deltaTime, maximumDeltaSeconds: Time.maximumDeltaTime, timeScale: Time.timeScale);

            // The path is placed by the last tick run plus the debt toward the next, the clock
            // condition 72 counts, so the script's next path follows it. On the worker path that
            // is the world this frame draws; on the main thread this frame's ticks come after.
            long scriptStarted = _recorder?.Stamp() ?? 0;
            FrameScriptMarker.Begin();
            _frameDriver.UpdateScript(_scriptedView, Time.unscaledTimeAsDouble * 1000d, World, new PlayerId((byte)viewingPlayerId), _ticksPending);
            UpdateWinScreenPreview();
            // ClassicHudControls.UpdateResult (0x004039A0) is fed only for a level launched from the front
            // end, where Space leaves the result; elsewhere suppressing input would strand the player.
            bool result = UpdateCampaignResult();
            FrameScriptMarker.End();
            _recorder?.Spent(FrameRecorder.Part.Script, scriptStarted);
            if (result) return;
            long inputStarted = _recorder?.Stamp() ?? 0;
            using (InputMarker.Auto())
            {
                _frameDriver.AdvancePickups(World, new PlayerId((byte)viewingPlayerId), (long)(Time.unscaledTimeAsDouble * 1000));
                ApplyLetterbox();
                // 0x00405362: Escape or Space ends the path; the simulation hears it next tick.
                FrameScriptInput script = _frameDriver.TakeScriptedInput(_scriptedView, new PlayerId((byte)viewingPlayerId),
                    Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space));
                if (script.Glide is TilePos glideTile) StartGlide(glideTile);
                using (InputHudMarker.Auto()) UpdateHudControls(allowOptionsKey: script.AllowOptionsKey);
                HudVisibility hud = CurrentHud;
                _minimap?.SetVisibility(hud);
                _hoverHud?.SetVisibility(hud);
                _resourceHud?.SetVisibility(hud);
                UpdatePossession(hud.AllowOrdinaryInput && !HudModal && !_scriptedView.PathPlaying);
                if (_scriptedView.PathPlaying)
                {
                    _classicCameraControls?.AdvancePointer(default, false, false);
                    _cameraPointer = Input.mousePosition;
                    ApplyPathCamera();
                }
                else using (InputCameraMarker.Auto())
                {
                    Glide();
                    FollowScriptedCreature();
                    if (hud.AllowOrdinaryInput && !HudModal && !PossessionCameraHeld) ControlCamera();
                    else
                    {
                        _classicCameraControls?.AdvancePointer(default, false, false);
                        _cameraPointer = Input.mousePosition;
                        if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
                    }
                }
                if (hud.AllowOrdinaryInput && !HudModal) ReadAmbientKeys();
                using (InputQueueMarker.Auto()) QueueInput();
            }
            _recorder?.Spent(FrameRecorder.Part.Input, inputStarted);
            if (_pendingSession != null) { LoadLevel(_pendingSession); return; }
            FrameTicks frameTicks = _frameDriver.TakeTicks(World, new PlayerId((byte)viewingPlayerId), elapsed, speed,
                _gameSettings.GameSpeed, _hudControls.TicksPaused, maxTicksPerFrame, _possession);
            if (frameTicks.Accrued) _drawLag.Observe(frameTicks.Advance);
            int ticks = frameTicks.Count;
            InputFrame firstInput = frameTicks.FirstInput;

            if (ticks > 0 && !tickOnWorkerThread) RunTicks(ticks, firstInput);
            _handCursorEvents?.Drain((long)(Time.unscaledTimeAsDouble * 1000));
            // Retain released tag previews until the command's tick has actually completed,
            // including the frame before a worker tick starts.
            _frameDriver.AfterTicks(World, (long)(Time.unscaledTimeAsDouble * 1000d));

            // How far the drawn world is from its last presented tick toward its next, for the
            // creature interpolation and the camera path. On the worker path the ticks started and
            // not yet presented are ahead of the drawn world, and the draw clock runs a tick's wall
            // time and a frame behind the tick clock so it never reaches poses that are not there
            // yet (ADR-0687, ADR-0694).
            float fraction = (float)(tickOnWorkerThread ? _drawLag.Fraction(_tickDebt, _ticksPending + ticks) : Math.Min(_tickDebt, 1d));

            // Off the map, a Hand excavation preview keeps the last tile the pointer was on.
            TilePos? dragFrom = _input.DragFrom(PlayerToolMode.Hand);
            if (dragFrom == null) _dragTo = null;
            else _dragTo = ScreenToTile(Input.mousePosition) ?? _dragTo ?? dragFrom;
            _worldView.PreviewTags(dragFrom, _dragTo, _input.ExcavationPreviewTagged ?? true);
            _scene.SetCreatureFrameTime(World.CurrentTick, fraction);
            bool possessionView = World.Player(new PlayerId((byte)viewingPlayerId)).PossessedCreatureId != 0;
            _worldView.SetPossessionView(possessionView);
            // The same latch rests the walls at full height; the view's change re-presents every tile, which rebuilds the chunks.
            if (_terrainLattice != null) _terrainLattice.Possession = possessionView;
            _scene.HideCreature(PossessionHiddenCreatureId);
            long presentStarted = _recorder?.Stamp() ?? 0;
            using (PresentMarker.Auto()) _presenter.Present(World);
            // DK2 draws the roof in camera modes 2, 10 and 11: the span the possessed model is hidden for.
            _scene.SetCeilingVisible(PossessionHiddenCreatureId != 0, World, _simulation.Database);
            _recorder?.Spent(FrameRecorder.Part.Present, presentStarted);
            if (_gemIdleEffects != null)
            {
                _gemIdleRequests.Clear();
                _gemIdleEffects.Collect(World, _gemIdleRequests);
                for (int i = 0; i < _gemIdleRequests.Count; i++) _constructionEffects.Model.Request(_gemIdleRequests[i]);
            }
            using (FrameMinimapMarker.Auto()) _minimap?.Capture(World, _worldView, _simulation.Navigation);
            bool drawingPath = _scriptedView.PathPlaying;
            _scriptedView.SeekCamera(World.CurrentTick.Value + fraction);
            if (_scriptedView.PathPlaying) ApplyPathCamera();
            else if (drawingPath)
            {
                if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
                else LookAt(World);
            }
            _lightning?.BeginFrame(Camera.main);
            long animationStarted = _recorder?.Stamp() ?? 0;
            _scene.DrawCreatureFrame(World.CurrentTick, fraction);
            _recorder?.Spent(FrameRecorder.Part.Animation, animationStarted);
            _shotDrawFraction = fraction;
            _constructionEffects?.CaptureShots(World.Shots, fraction);
            FollowQueriedCreature();
            if (Camera.main != null && !_scriptedView.PathPlaying)
            {
                FpPosition3 jitter = _scriptedView.CameraJitter;
                _appliedCameraJitter = new Vector3(jitter.X.Raw / (float)Fp32.One, 0, -jitter.Y.Raw / (float)Fp32.One);
                Camera.main.transform.position += _appliedCameraJitter;
            }
            RefreshEffectCamera();
            long lightsStarted = _recorder?.Stamp() ?? 0;
            FrameLightsMarker.Begin();
            _scene.SetPortalLights(_worldView.PortalLightTiles(World));
            _scene.SetObjectLights(_worldView.LightObjects(World), fraction);
            FrameLightsMarker.End();
            using (FrameEffectsAdvanceMarker.Auto())
                if (_constructionEffects != null) _scene.SetEffectLights(_constructionEffects.Advance());
            // The pick is last frame's: the hover is captured after the HUD is drawn, below.
            _scene.SetHandLight(_handLight);
            // After the camera is placed, so a path's first and last frames take the right fade.
            FrameLookMarker.Begin();
            ApplyClassicLook();
            _fullMap?.PreparePreview();
            FrameLookMarker.End();
            if (_classicCamera != null)
                _scene.PrepareClassicLighting(Camera.main,
                    _scriptedView.Pose is CameraPathPose path ? path.ViewDistance : _classicCamera.NativeViewDistance,
                    _fullMap?.PreviewCamera);
            _scene.AdvanceTorchAnimation();
            _scene.DrawCreatureShadows(Camera.main, _worldView, World);
            _constructionEffects?.Draw();
            _lightning?.DrawShots(World, _simulation.Database, _worldView, fraction);
            _lightning?.DrawEffects();
            _lightning?.EndFrame();
            _recorder?.Spent(FrameRecorder.Part.Lights, lightsStarted);
            long hudStarted = _recorder?.Stamp() ?? 0;
            _minimap?.SetVisibility(CurrentHud);
            using (FrameMinimapMarker.Auto()) _minimap?.Draw(Camera.main, _scriptedView.PathPlaying ? (Vector3?)null : _cameraFocus);
            using (FrameHudMarker.Auto()) DrawHudControls();
            using (FrameHoverMarker.Auto()) CaptureWorldHover();
            using (FrameFlowersMarker.Auto()) DrawStatusFlowers();
            _recorder?.Spent(FrameRecorder.Part.Hud, hudStarted);
            if (showDiagnostics) RefreshDiagnostics();

            // Nothing on this thread reads the world again before the next Update's wait: OnGUI
            // reads the strings built above, and Simulation's getter waits.
            using (FrameJournalMarker.Auto()) JournalTicks(ticks);
            if (ticks > 0 && tickOnWorkerThread)
            {
                // A batch still running when the next is due is joined here: the one wait left.
                WaitForTick();
                int count = ticks;
                InputFrame input = firstInput;
                _ticksPending = ticks;
                _tickStartedStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _tickObserved = false;
                _tickTask = StartTickThread(count, input);
            }
            if (_recorder != null)
            {
                _recorder.EndFrame(ticks, _recordedTickMs, _scene.DrawSeconds * SimulationTiming.TicksPerSecond,
                    Camera.main != null ? Camera.main.transform.position : default, _scriptedView.PathPlaying, _scene.TerrainFlushMilliseconds);
                _recordedTickMs = 0;
            }
            EndHudWarmFrame();
        }

        /// <summary>The ticks of one frame, the first with the frame's commands. Capturing the
        /// object positions first is what the view's interpolation reads.</summary>
        private void RunTicks(int ticks, InputFrame firstInput)
        {
            long started = _recorder?.Stamp() ?? 0;
            for (int i = 0; i < ticks; i++)
            {
                InputFrame input = TickPacer.InputFor(i, firstInput);
                using (TickMarker.Auto())
                {
                    // On the worker path the capture is the main thread's, from the presented world,
                    // when the batch is taken over: the view's caches are the frame's alone.
                    if (_presented == null)
                        using (CaptureMarker.Auto())
                            _worldView.CaptureObjectPositions(_simulation.World);
                    long tick = _simulation.World.CurrentTick.Value;
                    _journal.BeforeTick(tick, input, _simulation);
                    try
                    {
                        _simulation.Tick(input);
                    }
                    catch (Exception ex)
                    {
                        _journal.TickFailed(ex, _simulation);
                        throw;
                    }
                    _journal.AfterTick(tick + 1, _simulation);
                }
            }
            // The hash is for the diagnostics only, and costs a few milliseconds; here it is off the
            // frame when the ticks are. Turned on at run time, it shows 0 until the next tick.
            if (showDiagnostics) _stateHash = _simulation.StateHash();
            // Inline ticks are read by this frame's EndFrame, worker ticks by the frame that presents.
            double tickMs = _recorder?.Elapsed(started) ?? 0;
            if (_presented != null)
            {
                using (CopyWorldMarker.Auto()) _spare.CopyFrom(_simulation.World);
                _pendingTickMs = tickMs;
            }
            else
            {
                _worldView.PresentObjectPositions(_simulation.World);
                _recordedTickMs += tickMs;
            }
        }

        /// <summary>Takes a finished batch over when the draw clock has reached its last tick, so the
        /// interpolation runs straight through the boundary; a stall's catch-up batch at once. The
        /// batch's wall time, seen once, is what the draw clock lags by (<see cref="WorkerDrawLag"/>).</summary>
        private void PresentTick()
        {
            Task task = _tickTask;
            if (task == null || !task.IsCompleted) return;
            if (!_tickObserved)
            {
                _tickObserved = true;
                _drawLag.ObserveTick((System.Diagnostics.Stopwatch.GetTimestamp() - _tickStartedStamp)
                    / (double)System.Diagnostics.Stopwatch.Frequency * SimulationTiming.TicksPerSecond);
            }
            // The debt is this frame's before its advance is added; the frame's usual advance stands in.
            if (_ticksPending > 1 || _tickDebt + _drawLag.Frame >= _drawLag.Lag) WaitForTick();
        }

        /// <summary>The ticks on a thread of their own, above normal priority: on the thread pool
        /// beside Unity's main and render threads the same tick took 5–9 ms in a window against
        /// 3–4 ms headless (presented-world plan, 2026-11-06). The task carries completion and
        /// any exception to <see cref="WaitForTick"/> as before.</summary>
        private static Task StartTickThread(int count, InputFrame input, Action<int, InputFrame> run)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { run(count, input); done.SetResult(true); }
                catch (Exception ex) { done.SetException(ex); }
            }) { Name = "DK2 tick", IsBackground = true, Priority = System.Threading.ThreadPriority.AboveNormal };
            thread.Start();
            return done.Task;
        }

        private Task StartTickThread(int count, InputFrame input) => StartTickThread(count, input, RunTicks);

        /// <summary>Joins the worker tick, if one is running. A tick that threw stops the game and
        /// shows the failure, as a throw out of Update used to.</summary>
        private void WaitForTick()
        {
            Task task = _tickTask;
            if (task == null) return;
            using var waitScope = WaitTickMarker.Auto();
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
                return;
            }
            if (_presented == null) return;
            // The worker's copy of the live world becomes the presented one, in place: every reader
            // keeps the instances it holds, and the live world is the worker's again. The object
            // poses of the world being replaced are what the next frames interpolate from.
            using (CaptureMarker.Auto()) _worldView.CaptureObjectPositions(_presented);
            using (CopyWorldMarker.Auto()) _presented.CopyFrom(_spare);
            _worldView.PresentObjectPositions(_presented);
            _deferredEvents.Replay();
            _ticksPending = 0;
            _recordedTickMs += _pendingTickMs;
            _pendingTickMs = 0;
        }

        private TilePos? _debugHoverTile;

        private void RefreshDiagnostics()
        {
            string hover = "";
            if (_debugHoverTile is TilePos at && World.Contains(at))
            {
                TileState state = World.Tile(at);
                var viewer = new PlayerId((byte)viewingPlayerId);
                hover = Environment.NewLine + $"hover tile ({at.X},{at.Y})  {_simulation.Database.TerrainById(state.TerrainId).Name}  owner {state.OwnerPlayerId}"
                    + $"  explored {state.IsExploredBy(viewer)}  scanned {state.IsDiscoveredBy(viewer)}";
            }
            _diagnostics =
                $"tick {World.CurrentTick.Value}   " +
                $"state {_stateHash:x16}   " +
                $"{_scene.TileObjects} tile objects   {_scene.CreatureObjects} creatures   " +
                $"pushed {_presenter.TilesPushed} tiles / {_presenter.CreaturesPushed} creatures this frame" + hover;
        }

        /// <summary>The frame's world pointer through <see cref="WorldPointerInput"/>'s gates.</summary>
        private void QueueInput() => _worldPointer.Queue(new WorldPointerFrame(new PlayerId((byte)viewingPlayerId),
            CurrentHud.AllowOrdinaryInput, Possessing, HudModal, ReadHudPress(), Input.GetMouseButtonUp(0), Input.GetMouseButtonUp(1),
            (long)(Time.unscaledTimeAsDouble * 1000)), World, _simulation.Database, _pendingCommands);

        void IWorldPointerHost.MinimapPress(in HudPress press)
        {
            if (!_scriptedView.PathPlaying && !frameWholeMap && _minimap != null && press.Primary
                && _minimap.TryNavigate(Input.mousePosition, Camera.main, _cameraFocus, out TilePos navigationTile))
                StartGlide(navigationTile);
        }

        FpPosition2? IWorldPointerHost.PointerPosition(TilePos tile) => ScreenToPosition(Input.mousePosition, tile);

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
                World);
        }

        private FpPosition2? ScreenToPosition(Vector3 screenPosition, TilePos tile)
        {
            Camera camera = Camera.main;
            if (camera == null) return null;
            Ray ray = camera.ScreenPointToRay(screenPosition);
            return TileTopPlane.PositionOn(ToNumerics(ray.origin), ToNumerics(ray.direction), tile,
                _worldView.TileAt(World, tile.X, tile.Y).Solid);
        }

        private PlayerToolMode ActiveTool => _toolState.ActiveTool;

        private Vector3 _cameraPointer;
        private void ControlCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            if (_classicCamera != null && !frameWholeMap)
            {
                // The edges, keys, wheel and held buttons: ClassicCameraInput (migration plan step 2i).
                GameBindingInput input = ClassicKeyBindings.Read(_gameSettings);
                Vector3 pointer = Input.mousePosition;
                var sample = new CameraPointerSample(new System.Numerics.Vector2(pointer.x, pointer.y), Screen.width, Screen.height,
                    Application.isFocused, PointerOverHud(pointer), Input.mouseScrollDelta.y);
                _cameraFocus = ToUnity(ClassicCameraInput.Apply(_classicCameraControls, ToNumerics(_cameraFocus), input, sample,
                    new System.Numerics.Vector2(_cameraPointer.x, _cameraPointer.y), _gameSettings.MouseSensitivity,
                    Time.unscaledDeltaTime));
                _cameraPointer = pointer;
                ApplyClassicCamera();
                return;
            }

            ClassicCameraKeys panKeys = ClassicKeyBindings.Read(_gameSettings).CameraKeys;
            float horizontal = ((panKeys & ClassicCameraKeys.Right) != 0 ? 1 : 0) - ((panKeys & ClassicCameraKeys.Left) != 0 ? 1 : 0);
            float vertical = ((panKeys & ClassicCameraKeys.Up) != 0 ? 1 : 0) - ((panKeys & ClassicCameraKeys.Down) != 0 ? 1 : 0);
            var pan = new Vector3(horizontal, 0f, vertical);
            if (pan.sqrMagnitude > 1f) pan.Normalize();
            camera.transform.position += pan * cameraPanSpeed * Time.unscaledDeltaTime;

            float scroll = PointerOverHud(Input.mousePosition) ? 0 : Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            // A step in proportion to the height, so the wheel is as useful over a whole level
            // as beside a third-of-a-tile imp.
            float height = camera.transform.position.y - TileHeights.Floor;
            Vector3 next = camera.transform.position +
                (camera.transform.forward * scroll * cameraZoomFraction * height);
            if (next.y > MinCameraHeight) camera.transform.position = next;
        }

        /// <summary>The fill tint and distance fade the classic shader reads; set every frame so the
        /// Inspector tunes them live (ADR-0522).</summary>
        private void ApplyClassicLook()
        {
            Shader.SetGlobalVector("_DK2FillTint", new Vector4(classicFillTint.r, classicFillTint.g, classicFillTint.b, 1));
            Shader.SetGlobalVector("_DK2DistanceFade", new Vector4(classicFadeStart, classicFadeEnd, 0, 0));
            Shader.SetGlobalFloat("_DK2UnlitFill", classicUnlitFill);
            Shader.SetGlobalFloat("_DK2FillFloor", classicFillFloor);
        }

        private void ReadAmbientKeys()
        {
            if (!classicVertexLighting) return;
            bool changed = false;
            if (ClassicKeyBindings.Pressed(_gameSettings, GameAction.AmbientUp))
            { _ambientLight.Increase(); changed = true; }
            if (ClassicKeyBindings.Pressed(_gameSettings, GameAction.AmbientDown))
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
            float bar = _hudWarmFrames > 0 ? 0f : _scriptedView.LetterboxBar;
            camera.rect = new Rect(0f, bar, 1f, 1f - 2f * bar);
        }

        /// <summary>Action 26 (game action 4, 0x00442650): the focus glides to the point's
        /// centre, kept inside the bounds, angles unchanged (<see cref="CameraGlide"/>).</summary>
        private void StartGlide(TilePos tile) =>
            _glide.Start(ToNumerics(_cameraFocus), tile, _cameraBounds.x, _cameraBounds.y, Time.unscaledTimeAsDouble * 1000d);

        private void Glide()
        {
            if (_glide.Active) _cameraFocus = ToUnity(_glide.Advance(ToNumerics(_cameraFocus), Time.unscaledTimeAsDouble * 1000d));
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 value) => new Vector3(value.X, value.Y, value.Z);

        private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new System.Numerics.Vector3(value.x, value.y, value.z);

        private void ApplyPathCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || camera.pixelHeight <= 0 || _scriptedView.Pose is not CameraPathPose pose) return;
            int screenWidth = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            if (screenWidth <= 0) return;
            camera.orthographic = false;
            camera.ResetProjectionMatrix();
            camera.fieldOfView = pose.VerticalFieldOfView(screenWidth, camera.pixelHeight);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, pose.ViewDistance);
            camera.transform.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            camera.transform.rotation = Quaternion.LookRotation(
                new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z),
                new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z));
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
            DrawSubtitleText(SubtitleLayout.SpeechBox(Screen.width, Screen.height), subtitle);
        }

        /// <summary>Centred subtitle text in the owner's font order (2026-10-17): Bullfrog's
        /// DungeonKeeperII TrueType face first, DK2's own bitmap font second, Unity's default last.
        /// <paramref name="scale"/> enlarges it over the subtitle size, as the title card does.</summary>
        private void DrawSubtitleText(HudRect box, string subtitle, float scale = 1f)
        {
            Font trueType = Resources.Load<Font>("Fonts/DungeonKeeperII");
            if (trueType == null)
            {
                ClassicBitmapText bitmap = SubtitleBitmapFont(Screen.height);
                if (bitmap != null)
                {
                    bitmap.DrawCentred(new Rect(box.X, box.Y, box.Width, box.Height), subtitle, scale);
                    return;
                }
            }
            GUIStyle style = scale == 1f ? _subtitleStyle : _titleStyle;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    font = trueType != null ? trueType : GUI.skin.label.font,
                    alignment = TextAnchor.MiddleCenter, wordWrap = true,
                    fontSize = SubtitleLayout.TrueTypePixelSize(Screen.height, scale),
                    normal = { textColor = Color.white },
                };
                if (scale == 1f) _subtitleStyle = style; else _titleStyle = style;
            }
            HudRect label = SubtitleLayout.TrueTypeLabel(box, Screen.width);
            GUI.Label(new Rect(label.X, label.Y, label.Width, label.Height), subtitle, style);
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
            if (PossessionCameraHeld && ApplyPossessionCamera()) return;
            Camera camera = Camera.main;
            if (camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
            int screenWidth = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            if (screenWidth <= 0) return;
            ClassicCameraPose pose = _classicCamera.Pose(
                new System.Numerics.Vector3(_cameraFocus.x, _cameraFocus.y, _cameraFocus.z),
                new CameraViewport(screenWidth, camera.pixelWidth, camera.pixelHeight));
            ApplyCameraPose(camera, pose);
        }

        private static void ApplyCameraPose(Camera camera, in ClassicCameraPose pose)
        {
            camera.orthographic = false;
            camera.ResetProjectionMatrix();
            camera.fieldOfView = pose.VerticalFieldOfView;
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, pose.FarClipDistance);
            if (pose.ProjectionCentreShift != 0)
            {
                // An off-centre frustum: the view axis lands above the middle, as in DK2.
                Matrix4x4 projection = camera.projectionMatrix;
                projection[1, 2] = -pose.ProjectionCentreShift;
                camera.projectionMatrix = projection;
            }
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

            Vector3 centre = DungeonScene.Ground(world.Width / 2f, world.Height / 2f, TileHeights.Floor);

            float radius = 0.5f * Mathf.Sqrt(
                (world.Width * (float)world.Width) + (world.Height * (float)world.Height));

            // Unity's fieldOfView is the vertical one. A wide game view has a
            // wider horizontal field, so fitting the sphere vertically always
            // fits it horizontally too.
            float halfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / Mathf.Max(0.01f, Mathf.Sin(halfFov));

            camera.ResetProjectionMatrix();
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

        private bool PointerOverHud(Vector3 position) =>
            HudPointerMap.Over(new PointerPanels(this, position), fullMapVisible: _hudControls.FullMapVisible,
                queryHasTarget: _queryView != null && _queryView.Target != CreatureQueryTarget.None, modal: HudModal);

        /// <summary>This runner's panels at one pointer, for <see cref="HudPointerMap"/>; a struct, so a query allocates nothing.</summary>
        private readonly struct PointerPanels : IHudPointerPanels
        {
            private readonly DungeonRunner _runner;
            private readonly Vector3 _position;

            public PointerPanels(DungeonRunner runner, Vector3 position) { _runner = runner; _position = position; }

            public bool Contains(HudPointerPanel panel)
            {
                DungeonRunner r = _runner;
                switch (panel)
                {
                    case HudPointerPanel.Minimap: return r._minimap != null && r._minimap.ContainsPointer(_position);
                    case HudPointerPanel.HoverHud: return r._hoverHud != null && r._hoverHud.ContainsPointer(_position);
                    case HudPointerPanel.ResourceStrip: return r._resourceHud != null && r._resourceHud.ContainsPointer(_position);
                    case HudPointerPanel.GameOptions: return r._gameOptionsPanel != null && r._gameOptionsPanel.ContainsPointer(_position);
                    case HudPointerPanel.BottomPanel: return r._bottomPanel != null && r._bottomPanel.ContainsPointer(_position);
                    case HudPointerPanel.MessageTabs: return r._messageTabs != null && r._messageTabs.ContainsPointer(_position);
                    case HudPointerPanel.BattleTab: return r._battleTab != null && r._battleTab.ContainsPointer(_position);
                    case HudPointerPanel.CreatureOverview: return r._overviewPanel != null && r._overviewPanel.ContainsPointer(_position);
                    case HudPointerPanel.OptionsMenu: return r._optionsPanel != null && r._optionsPanel.ContainsPointer(_position);
                    case HudPointerPanel.CreatureQuery: return r._queryPanel != null && r._queryPanel.ContainsPointer(_position, r.CurrentHud);
                    default: return false;
                }
            }
        }

        /// <summary>The Hand's light follows the pick (<see cref="HoverCursorResolver.TryHandLightPoint"/>); on a tile, the
        /// pointer's hit on the tile's top plane, or the tile's centre when that misses.</summary>
        private void AimHandLight(WorldState world, TilePos tile, in WorldHoverHit hit)
        {
            if (!HoverCursorResolver.TryHandLightPoint(world, hit, out System.Numerics.Vector2 point))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                point = TileTopPlane.HandLightPoint(ToNumerics(ray.origin), ToNumerics(ray.direction), tile,
                    _worldView.TileAt(world, tile.X, tile.Y).Solid);
            }
            _handLight = HoverCursorResolver.HandLight(world, new PlayerId((byte)viewingPlayerId), hit, point, cheapLighting);
        }

        private WorldHoverHit ResolveWorldHover(TilePos tile)
        {
            WorldState world = World;
            PlayerId viewer = new PlayerId((byte)viewingPlayerId);
            if (HoverCursorResolver.FogHit(world, viewer, tile) is WorldHoverHit fogged) return fogged;
            Camera view = Camera.main;
            Ray ray = view.ScreenPointToRay(Input.mousePosition);
            TileView shown = _worldView.TileAt(world, tile.X, tile.Y);
            if (!WorldHoverPicker.TryCastTerrain(ToNumerics(ray.origin), ToNumerics(ray.direction), shown.Solid,
                    out float terrainDistance, out System.Numerics.Vector3 ground))
                return new WorldHoverHit(WorldHoverKind.Tile, tile);
            _objectsOnlyPick = HoverCursorResolver.ObjectsOnlyKeyCounts(CurrentHud, HudModal)
                && ClassicKeyBindings.Held(_gameSettings, GameAction.ObjectsOnly, exactModifiers: true);
            _renderedPick ??= (kind, id) => HoverPickRules.Rendered(World, _simulation.Database, kind, id, _objectsOnlyPick);
            _fallbackPick ??= (kind, id) => HoverPickRules.Fallback(World, _simulation.Database, kind, id, _objectsOnlyPick);
            _resolvesPick ??= candidate => HoverPickRules.Resolves(World, candidate, new PlayerId((byte)viewingPlayerId));
            GeometryUtility.CalculateFrustumPlanes(view, _pickFrustum);
            WorldHoverHit hit = _scene.PickHoverEntity(ray, view.transform.forward, terrainDistance, tile,
                new Vector3(ground.X, ground.Y, ground.Z), _renderedPick, _fallbackPick, _resolvesPick, _pickFrustum, _objectsOnlyPick);
            return WorldHoverPicker.OnPointerTile(hit, tile);
        }

        // The picker's eligibility (HoverPickRules), made once so a frame's pick allocates nothing.
        private System.Func<WorldHoverKind, int, bool> _renderedPick, _fallbackPick;
        private bool _objectsOnlyPick;
        private readonly Plane[] _pickFrustum = new Plane[6];
        private System.Func<WorldHoverHit, bool> _resolvesPick;

        private void CaptureWorldHover()
        {
            HudVisibility visibility = CurrentHud;
            _hoverHud?.SetVisibility(visibility);
            _resourceHud?.SetVisibility(visibility);
            using (HoverResourcesMarker.Auto())
                if (visibility.ShowNormalHud) _resourceHud?.Draw(_resources, default,
                    unchecked((uint)(Time.unscaledTimeAsDouble * 1000)), _scriptedView.ResourceFlash);
            _hoverSnapshot = HoverTextSnapshot.Empty;
            Vector3 mouse = Input.mousePosition;
            bool inside = mouse.x >= 0 && mouse.y >= 0 && mouse.x < Screen.width && mouse.y < Screen.height;
            bool allowed = visibility.AllowOrdinaryInput && Application.isFocused && inside && !_possessionPointerLocked;
            bool overHud = PointerOverHud(mouse);
            _debugHoverTile = null;
            // A failed pick with nothing hovered sends DK2's light to (0,0,0), an edge tile, which
            // blacks it out (0x0043BCB8); with ordinary input off (a cutscene) it keeps its place.
            if (visibility.AllowOrdinaryInput) _handLight = null;
            WorldHoverHit? hover = null;
            using (HoverPickMarker.Auto())
            if (allowed && !overHud && Camera.main != null && ScreenToTile(mouse) is TilePos tile)
            {
                WorldState world = World;
                _debugHoverTile = tile;
                _pointerWorld = ScreenToPosition(mouse, tile) ?? new FpPosition2(
                    new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)));
                WorldHoverHit hit = ResolveWorldHover(tile);
                AimHandLight(world, tile, hit);
                string personalName = hit.Kind == WorldHoverKind.Creature && world.CreatureById(hit.EntityId) is CreatureState named
                    ? _identityNames.Name(new CreatureNameIndex(named.NameIndex)) : "";
                _hoverSnapshot = _hoverText.Capture(world, hit, personalName);
                hover = hit;
            }
            if (allowed && overHud) _hoverSnapshot = CaptureHudHover();
            // The cursor, placement, tint and hover sound: HoverCursorResolver.
            long cursorMs = (long)(Time.unscaledTimeAsDouble * 1000);
            HoverFeedback feedback = _hoverCursor.Resolve(World, _simulation.Database, _simulation.Navigation,
                new HoverCursorInput(hover, Allowed: allowed, OverHud: overHud, PrimaryHeld: Input.GetMouseButton(0),
                    Gold: _resources.Gold.Value, Milliseconds: cursorMs, PointerWorld: _pointerWorld));
            HandCursorKind cursor = feedback.Cursor;
            PlacementFeedback placement = feedback.Placement;
            _hoveredCreature = feedback.HoveredCreature;
            if (feedback.Tinted && hover is WorldHoverHit tinted) _scene.DrawHoverOverlay(tinted, feedback.TintBlended);
            if (_soundPlayer != null && feedback.HoverSound is SoundRequest hoverSound) _soundPlayer.Play(hoverSound);
            if (_soundPlayer != null) _soundPlayer.Play(_cursorFeedback.Sounds); else _cursorFeedback.Sounds.Clear();
            Vector2 pointerGui = new Vector2(mouse.x, Screen.height - mouse.y);
            using (HoverCursorMarker.Auto())
            {
                _handCursor?.Update(cursor, allowed, Time.unscaledTimeAsDouble);
                _handCursor?.Draw(pointerGui);
            }
            using (HoverHudMarker.Auto()) _hoverHud?.Draw(_hoverSnapshot, pointerGui, feedback.TooltipReach);
            using (HoverPlacementMarker.Auto())
            {
                if (allowed && placement.Icon == PlacementIcon.Held) _heldStack.Refresh(World, new PlayerId((byte)viewingPlayerId), _worldView.LocalPickups);
                _placementOverlay?.Draw(allowed ? placement : PlacementFeedback.None, pointerGui, overHud, _heldStack, cursor);
                _tileHighlight?.Update(placement.Cage, placement.Valid, Camera.main, Time.unscaledTimeAsDouble);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                _handCursor?.Update(HandCursorKind.Default, false, Time.unscaledTimeAsDouble);
                PointerGestures.CancelAll(_bottomPanel, _input, _cursorFeedback);
            }
        }

        private void OnDisable()
        {
            _bottomPanel?.CancelPointerGesture();
            _cursorFeedback.Reset();
            _handCursor?.Update(HandCursorKind.Default, false, 0);
            // Unset is neutral; editor probes rendered after Play must not inherit the look.
            Shader.SetGlobalVector("_DK2FillTint", Vector4.zero);
            Shader.SetGlobalVector("_DK2DistanceFade", Vector4.zero);
            Shader.SetGlobalFloat("_DK2UnlitFill", 0);
            Shader.SetGlobalFloat("_DK2FillFloor", 0);
        }

        private void OnGUI()
        {
            if (_hudWarmFrames > 0) GUI.color = new Color(1f, 1f, 1f, 0f);
            if (_frontEnd != null)
            {
                DrawCutsceneTitle();
                DrawStartupTitle();
                if (showDiagnostics) DrawFrontEndDiagnostics();
                return;
            }
            DrawCinematic();
            DrawCampaignResultPrompt();
            DrawWinScreenPreview();
            DrawBattleFlyIn();   // last, above the rest, as 0x0042C390 ends DK2's overlay pass
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
                $"tool: {_toolState.Tool}" +
                (_toolState.Tool == DungeonToolMode.BuildRoom ? $" (room {_toolState.SelectedRoomId})" : "") +
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
