// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using DK2.Core;
using DK2.Data;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Scene root of <c>InGameHud.tscn</c>. Loads a campaign level,
/// paces its simulation with <see cref="TickPacer"/>, and draws the HUD over the world.
/// The options menu pauses the level and takes input while it is open. The full map shares
/// the minimap grid and hides the normal HUD. Launch arguments are documented in godot/README.md.</summary>
public partial class InGameHud : Node, IHudHoverSources, IHudActionHost, IHudPressHost, IWorldPointerHost
{
    private readonly List<PlayerCommand> _pending = new();
    private LightningStaging? _lightningStage;
    private ColorRect? _topBar, _bottomBar;

    private void DrawLetterbox()
    {
        float bar = _world?.LetterboxBar ?? 0;
        if (bar <= 0)
        {
            if (_topBar != null) { _topBar.Visible = false; _bottomBar!.Visible = false; }
            return;
        }
        if (_topBar == null)
        {
            var layer = new CanvasLayer { Layer = 100 };
            AddChild(layer);
            _topBar = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
            _bottomBar = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(_topBar); layer.AddChild(_bottomBar);
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        _topBar.Visible = _bottomBar!.Visible = true;
        _topBar.Size = _bottomBar.Size = new Vector2(size.X, size.Y * bar);
        _bottomBar.Position = new Vector2(0, size.Y * (1 - bar));
    }
    private readonly PlayerInputTranslator _input = new();
    /// <summary>The tool, the panel's choice, the query tool and the selected corner.</summary>
    private readonly HudToolState _tools = new();
    /// <summary>Routes HUD actions to this scene.</summary>
    private HudActionRouter? _router;
    /// <summary>Where the press that raised the action being applied was, for the magnifier's zoom.</summary>
    private Vector2 _actionPointer;
    /// <summary>The overview and world pickups update the hand cursor's held state.</summary>
    private PickupFlow? _pickups;
    /// <summary>The hand cursor's transient states, shared by the pickups, the world
    /// pointer and the hover, whose sounds the mixer plays.</summary>
    private readonly HandCursorFeedback _cursorFeedback = new();
    /// <summary>The cursor, placement and cage to draw after the world hover pick.</summary>
    private HoverCursorResolver? _hoverCursor;
    /// <summary>The hand's icons the placement cursor draws while it holds things.</summary>
    private readonly HeldItemStack _heldStack = new();
    private ClassicHandCursor? _handCursor;
    private ClassicPlacementOverlay? _placement;
    private InstallHandCursorSheets? _cursorSheets;
    /// <summary>The world pick for the hover: the drawn entities (<see cref="WorldScene.PickHoverEntity"/>), else the tile under the pointer.</summary>
    private readonly WorldHoverPicker _picker = new();
    /// <summary>The last world point under the hand, used to position its sounds.</summary>
    private FpPosition2? _pointerWorld;
    /// <summary>The Hand's excavation drag's far end; off the map it keeps the last tile the pointer was on.</summary>
    private TilePos? _dragTo;
    /// <summary>The <c>--pointer-path</c> check (audit open point 10): a scripted pointer in place of the OS one, so a capture
    /// never moves the owner's cursor. Its frames count this scene's frames from 0.</summary>
    private PointerScript? _pointerScript;
    private int _scriptFrame = -1;
    private PointerScriptSample _scripted;
    private Vector2 _scriptPushed = new(float.NaN, float.NaN);
    /// <summary>Routes presses to the eligible HUD panels in priority order.</summary>
    private HudPressRouter? _presses;
    /// <summary>The query tool's and a held choice's world presses, on the tile
    /// <see cref="PointerTile"/> picks.</summary>
    private WorldClickCommands? _worldClicks;
    /// <summary>The window, whose input is read before any panel takes it.</summary>
    private Window? _window;
    /// <summary>The left and right buttons pressed since the last frame, read from the window before any panel marks the
    /// event handled.</summary>
    private bool _primaryDown, _secondaryDown, _primaryUp, _secondaryUp;
    /// <summary>The pointer and the HUD's visibility when this frame's presses are routed.</summary>
    private Vector2 _pressPointer;
    private HudVisibility _pressVisibility;
    private RoomNavigation? _roomNavigation;
    private ThingNavigation? _doorNavigation, _trapNavigation;
    private readonly List<RoomNavigationEntry> _roomEntries = new();
    private readonly List<ThingNavigationEntry> _thingEntries = new();
    private readonly TickPacer _pacer = new();
    /// <summary>The frame order around this scene's HUD and input.</summary>
    private FrameDriver? _frameDriver;
    /// <summary>The world pointer rules, applied to the tile <see cref="PointerTile"/> picks.</summary>
    private WorldPointerInput? _worldPointer;
    /// <summary>The input journal, enabled by <c>--input-journal &lt;dir&gt;</c>. Its
    /// keys are Godot physical keycodes (<see cref="FrameJournalSession.GodotKeyTable"/>).</summary>
    private readonly FrameJournalSession _journal = new(message => GD.Print(message), message => GD.PushWarning(message));
    private string? _journalRoot;
    private string _levelName = "";
    private ulong _seed;
    /// <summary>The journal's device sample since the last frame, kept only while it records.</summary>
    private readonly List<HostKey> _journalDown = new(), _journalUp = new(), _journalHeld = new();
    private FrameMouseButtons _journalButtonsDown, _journalButtonsUp;
    private float _journalWheelX, _journalWheelY;
    /// <summary>The applied settings, including the game speed, the key bindings and the
    /// HUD scale. Loaded from, and saved to, <see cref="_settingsPath"/>.</summary>
    private GameSettings _settings = new();
    private string _settingsPath = "";
    /// <summary>The settings pages' transactions.</summary>
    private OptionsPagesController? _optionsPages;
    private InGameOptionsController? _gameOptions;
    private ClassicGameOptionsPanel? _gameOptionsPanel;
    private readonly ClassicKeyboard _keyboard = new();
    /// <summary>The options menu's pages and the GUI state.</summary>
    private readonly ClassicHudControls _hudControls = new();
    private PlayerId _viewer = PlayerIds.FirstKeeper;
    private GameSimulation? _simulation;
    private InstallHudSources? _sources;
    private InstallBottomPanelArt? _art;
    private BottomPanelView? _view;
    private KeeperResourceView? _resourceView;
    private ClassicResourceHud? _resources;
    private ClassicBottomPanel? _panel;
    private BottomPanelHud? _model;
    private MessageTabsView? _messageView;
    private ClassicMessageTabs? _messages;
    private ClassicHoverHud? _hover;
    private HudHoverText? _hudHover;
    private CreatureOverview? _overviewView;
    private ClassicCreatureOverview? _overview;
    private CreatureQueryView? _queryView;
    private ClassicCreatureQueryPanel? _queryPanel;
    /// <summary>The sound driver's music observer supplies the battle records read by the battle tab.</summary>
    private MusicWorldObserver? _battles;
    /// <summary>The level's sound (plan step 4): Godot's players (<see cref="ClassicSoundOutput"/>) under the shared mixer and
    /// driver, which decide what plays.</summary>
    private ClassicSoundOutput? _soundOutput;
    private ClassicSoundMixer? _mixer;
    private LevelSoundDriver? _sound;
    private ClassicBattleTab? _battleTab;
    private ClassicOptionsPanel? _options;
    /// <summary>The save and load pages' list.</summary>
    private SaveSlotsView? _saves;
    /// <summary>The loaded save the next instance of this scene continues (<see cref="SaveLoadSubmitted"/>).
    /// <see cref="_Ready"/> takes and clears it.</summary>
    private static GameSessionRequest? s_resume;
    /// <summary>The live campaign profile, loaded on the first save or load from
    /// <see cref="_profilePath"/>.</summary>
    private CampaignProfileStore? _profiles;
    private string _profilePath = "";
    private SessionContext? _context;
    /// <summary>The level end: the carry, the statistics, the result and Space back to
    /// <c>Main.tscn</c>; it acts only for a level started from the campaign map.</summary>
    private CampaignResultFlow? _resultFlow;
    /// <summary>The level script's view requests: the camera path, the letterbox
    /// count and the flash, which hide the HUD, hold the options key and light the panel and the resources; Escape or Space
    /// skips a path (<see cref="FrameDriver"/>). Its speech plays through <see cref="_sound"/>, its subtitle in <c>InGameHud.Subtitles.cs</c>.</summary>
    private ScriptedViewDirector? _scriptedView;
    private bool _pathPlaying;
    /// <summary>The viewer's status last printed (<see cref="ReportPath"/>).</summary>
    private byte _status;
    /// <summary>The check arguments <c>--leave-result</c> (Space, once the result is recorded) and <c>--escape-result</c>
    /// (Escape once, then, if the result still stands, Space); a camera path holds both, as it holds the keys.</summary>
    private bool _leaveResult, _escapeResult;
    /// <summary>Set once the level end has asked for the front end; the scene does nothing more.</summary>
    private bool _leaving;
    private SessionSaver? _saver;
    private SessionLoader? _loader;
    /// <summary>The check arguments (godot/README.md): <c>--save-as</c> once the turn reaches <c>--save-tick</c>,
    /// <c>--load</c> on the first frame, and the state hash at turn <c>--hash-at</c>.</summary>
    private string? _saveAs, _loadName;
    private GameTick _saveTick;
    private GameTick? _hashTick;
    private ClassicSaveLoadPanel? _saveLoad;
    /// <summary>The full map, over the minimap's class grid.</summary>
    private ClassicFullMap? _fullMap;
    private ClassicMinimap? _minimap;
    /// <summary>The viewer's world state, supplying the minimap colours and markers.</summary>
    private WorldView? _worldView;
    /// <summary>The 3D world under the HUD (plan step 3a), drawn from the camera <see cref="UpdateCamera"/> moves.</summary>
    private WorldScene? _world;
    /// <summary>The accepted drops' hand animation, first in the event chain and
    /// drained after the frame's ticks.</summary>
    private HandCursorEventSink? _handCursorEvents;
    private Action<HeartHealthChange>? _heartHealthObserver;
    /// <summary>Action 26's focus glide, on this scene's engine-time clock.</summary>
    private readonly CameraGlide _glide = new();
    /// <summary>The camera pointer last frame, y up, and the wheel since then.</summary>
    private System.Numerics.Vector2 _cameraPointer;
    private float _cameraWheel;
    /// <summary>The <c>--glide-to x,y</c> check: a glide started one second into the scene, once no camera path plays.</summary>
    private TilePos? _glideTo;
    private BattleNavigation? _battleNavigation;
    /// <summary>The world hover text and the Creatures panel's type text.</summary>
    private WorldHoverText? _typeText;
    // Names used for the hovered creature's own name.
    private IClassicCreatureNames? _identityNames;
    // The picker's eligibility (HoverPickRules), made once so a frame's pick allocates nothing, and the objects-only key.
    private Func<WorldHoverKind, int, bool>? _renderedPick, _fallbackPick;
    private Func<WorldHoverHit, bool>? _resolvesPick;
    private bool _objectsOnlyPick;
    // What the world hover's pick and tint allocated (the exit report): frames over the world after the first 60, and
    // the bytes; the text capture is shared code that builds its text again when the turn changes, so it is not counted.
    private long _pickBytes;
    private int _pickFrames, _pickWarmUp = 60;
    private KeeperResourceSnapshot _resourceFacts;
    /// <summary>The pointer in screen pixels from the top left, sampled once per frame or supplied by a script.</summary>
    private Vector2 _pointer;
    private bool _pointerInWindow = true;
    private double _elapsed;
    /// <summary>DK2's ambient level, which the ambient keys step.</summary>
    private readonly ClassicAmbientLight _ambientLight = new();
    /// <summary>The Hand's light from the last frame's pick, drawn by the next frame.</summary>
    private ClassicPointLight? _handLight;
    /// <summary>The <c>--light-probe-*</c> checks print the frame each turn is first drawn on, for captures at a turn.</summary>
    private bool _reportTicks;
    private long _reportedTick = -1;
    /// <summary>The <c>--record-frames</c> telemetry. Null without the argument.</summary>
    private FrameRecorder? _recorder;

    /// <summary>The time <see cref="RunFrameTicks"/> accrues next under Movie Maker: the last frame's step.</summary>
    private double _lastDelta;
    private bool _movie;

    public override void _Ready()
    {
        if (_simulation != null || _failed)
        {
            return;   // re-entering the tree keeps the running level
        }

        RenderingServer.SetDefaultClearColor(Colors.Black);
        string[] args = OS.GetCmdlineUserArgs();
        string? install = LauncherSettingsStore.FindInstall(args);
        if (install is null)
        {
            Fail(new GameFailure(GameFailureKind.NoInstall, Path: LauncherSettingsStore.NamedInstall(args)), overFrame: false);
            return;
        }
        // If a level fails to load, show the error on screen.
        try
        {
            StartLevel(args, install);
        }
        catch (Exception ex) when (!_failed)
        {
            Fail(new GameFailure(GameFailureKind.LevelDidNotStart, ex.Message,
                _context?.LevelName ?? LaunchArguments.Parse(args, "--").Level), overFrame: false, ex);
        }
    }

    private void StartLevel(string[] args, string install)
    {
        // Level load, not the opening path: the code a first frame runs compiles on another thread while the level loads
        // (JitWarmUp), and the level's first ticks run on a throwaway simulation below (LevelWarmUp). --no-warm-up skips both.
        bool warmUp = Array.IndexOf(args, "--no-warm-up") < 0;
        if (warmUp)
        {
            StartJitWarmUp();
        }

        var launch = LaunchArguments.Parse(args, "--");
        // A save the previous instance loaded: its level, return page and restored simulation replace the arguments'.
        GameSessionRequest? resume = s_resume ?? (s_launch?.Resume != null ? s_launch : null);
        s_resume = null;
        _context = StartingSession(resume, launch.Level, args, install);
        if (_context == null)
        {
            return;
        }

        string levelName = _context.LevelName;
        ulong seed = launch.Seed ?? 1;
        GameFolders folders = UserFolders.FromArguments(args);
        _levelName = levelName;
        _seed = seed;
        // Start the input journal only when the launch argument names a folder.
        _journalRoot = LaunchArguments.Value(args, "--input-journal");
        CampaignLevelData level;
        try
        {
            level = CampaignLevelData.Load(install, levelName);
            _simulation = resume?.Resume?.Simulation ?? (_context.ImportedTerrain is { } imported
                ? GameSessionStarter.FromDk2Save(level.Database, imported, seed, fogOfWarEnabled: true)
                : level.CreateSimulation(seed, fogOfWarEnabled: true));
            if (resume?.Resume != null && _context.Mode == GameSessionMode.ImportedTerrain)
            {
                _viewer = GameSessionStarter.ImportedSaveViewer(_simulation.World, _simulation.World.LocalPlayerId);
            }
            else if (_context.ImportedTerrain != null)
            {
                _viewer = GameSessionStarter.ImportedSaveViewer(_simulation.World, _viewer);
                GD.Print("DK2.Godot imported terrain and tile ownership from a DK2 save for ", levelName,
                    ". The session starts fresh without saved entities, time or progress.");
            }
        }
        catch (Exception ex) when (_context.ImportedTerrain != null && IsLegacyImportError(ex))
        {
            ReportLegacyImportError(ex, LaunchArguments.Value(args, "--dk2-save"));
            return;
        }
        _simulation.World.LocalPlayerId = _viewer;
        // Level load, not the opening path: the first ticks compile on a throwaway simulation (LevelWarmUp).
        // A restart (no Resume) is a fresh start of the level; only a loaded save is resumed.
        bool resumed = resume?.Resume != null;
        if (!resumed && warmUp && _context.ImportedTerrain == null)
        {
            long warmStarted = Stopwatch.GetTimestamp();
            LevelWarmUp.Run(level, seed, fogOfWarEnabled: true);
            GD.Print("DK2.Godot: level warm-up ", Stopwatch.GetElapsedTime(warmStarted).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture), " ms.");
        }
        _profilePath = folders.Profile;
        _profiles = new CampaignProfileStore(InstalledCampaignRegistry.ReadCurrentUser, () => DateTime.Now);
        _saver = new SessionSaver(_profiles, LoadProfile, () => DateTime.UtcNow);
        _loader = new SessionLoader(_profiles, LoadProfile, name => CampaignLevelData.Load(install, name))
        { TerrainImportsSupported = true };
        // Before the first turn, record the current campaign slot if the level was launched from the map;
        // a restored save counts its carry as written, a fresh start brings the stored transfer creature in.
        _resultFlow = new CampaignResultFlow(_context, _profiles, LoadProfile, _hudControls)
        {
            LevelTicksPerSecond = level.Level.TicksPerSecond,
        };
        _resultFlow.Prepare(_simulation, _viewer, resumed);
        // Game settings live beside the campaign profile by default.
        _settingsPath = folders.Settings;
        _settings = LoadSettings(_settingsPath);
        _optionsPages = new OptionsPagesController(_settings) { CreatureShadowsSupported = true };
        _optionsPages.Applied += ApplySettings;
        _optionsPages.SaveRequested += SaveSettings;

        // First, so the tree reads world then HUD; the HUD's canvas draws over the 3D world whatever the order.
        _world = new WorldScene { Name = "World" };
        AddChild(_world);
        _sources = new InstallHudSources(install);
        _art = new InstallBottomPanelArt(_sources, level.Database);
        // Add the panel base first so it draws underneath the other controls.
        _resourceView = new KeeperResourceView(level.Database, _viewer);
        _resources = new ClassicResourceHud { Name = "ResourceHud" };
        _resources.Open(_sources);
        AddChild(_resources);
        _view = new BottomPanelView(level.Database, _viewer, _art.Name, _art.Order, campaignMode: true);
        // The tool starts as the hand.
        _tools.SelectKnownRoom(level.Database.Rooms.Keys);
        // A room build or sell drag posts no wider than the drawn cage.
        _input.RubberBandLimit = PlacementCursor.RubberBandLimit(level.Database);
        _router = new HudActionRouter(_hudControls, _tools, _input, this);
        _panel = new ClassicBottomPanel { Name = "BottomPanel" };
        _panel.Open(_art, _view);
        AddChild(_panel);
        _panel.OpenAtLevelStart();
        _model = _panel.Model;
        _model.Selected += SelectPanelChoice;
        _model.SpecialChosen += ActivateStoredSpecial;
        _model.GuiButton += PostGuiButton;
        // Right-clicking rooms, doors and traps on the panel starts a camera glide.
        _roomNavigation = new RoomNavigation(_viewer);
        _doorNavigation = new ThingNavigation(_viewer);
        _trapNavigation = new ThingNavigation(_viewer);
        _model.NavigateRoom += NavigateRoom;
        _model.NavigateDoor += NavigateDoor;
        _model.NavigateTrap += NavigateTrap;
        _model.CollapsedChanged += PanelCollapsed;
        _battleNavigation = new BattleNavigation(_viewer);
        // Add the overview between the bottom panel and the tabs so it draws between them.
        var overviewArt = new InstallCreatureOverviewArt(install, level.Database, _art.Image, _art.Text);
        _overviewView = new CreatureOverview(level.Database, _viewer, overviewArt.Name, overviewArt.Order);
        _overview = new ClassicCreatureOverview { Name = "CreatureOverview" };
        // Its presses are the router's (QueueInput): a pickup is posted, a focus moves the camera.
        _overview.Open(overviewArt, _overviewView);
        AddChild(_overview);
        // Draw the query details over the overview.
        _identityNames = new InstallClassicCreatureNames(install);
        _queryView = new CreatureQueryView(level.Database, _sources.Strings.Resolve, _viewer, _identityNames);
        _queryPanel = new ClassicCreatureQueryPanel { Name = "CreatureQuery" };
        _queryPanel.Open(_art.Font, _sources.Texture);
        AddChild(_queryPanel);
        // Draw the battle tab above the bottom and creature panels and below the tab strip's rail.
        _worldView = new WorldView(level.Database, _viewer);
        _pickups = new PickupFlow(_worldView, _cursorFeedback);
        _presses = new HudPressRouter(_hudControls, _tools, _overviewView, _view, _pickups, _viewer, this);
        _worldClicks = new WorldClickCommands(_tools, _input, _queryView, _view, _viewer, this);
        _hoverCursor = new HoverCursorResolver(_tools, _input, _hudControls, _view, _worldView, _cursorFeedback, _viewer);
        _worldPointer = new WorldPointerInput(_input, _tools, _presses, _worldClicks, _pickups, _worldView, _cursorFeedback,
            _hoverCursor, _journal, this);
        System.Numerics.Vector3 startFocus = ClassicCamera.LevelStartFocus(level.Players, _viewer,
            _simulation.World.Width, _simulation.World.Height);
        _minimap = new ClassicMinimap { Name = "Minimap" };
        _minimap.Open(new InstallMinimapAssets(install, _sources.Texture), _simulation.World.Width, _simulation.World.Height,
            _sources.Texture);
        AddChild(_minimap);
        // Draw the tabs over the bottom panel and route their mouse presses first.
        _messageView = new MessageTabsView(_viewer, level.Database);
        var messageText = new MessageTextTables(_sources, level.Database.Level?.TextTableId ?? 0, warning => GD.PushWarning(warning));
        InstallHudSources sources = _sources;
        GameSimulation simulation = _simulation;
        MessageTabsView messageView = _messageView;
        _messages = new ClassicMessageTabs { Name = "MessageTabs" };
        // 0x00415B30 submits game action 4 with the point's tile. Glide to that tile's centre.
        // DK2 drops off-map points before the action. This glide clamps them.
        _messages.Open(_art, _art.Font, _messageView,
            messageText.Resolve, messageText.Briefing, message => messageView.LocationOncePerTick(simulation.World, message),
            (x, y) => StartGlide(new TilePos((int)Math.Floor(x), (int)Math.Floor(y))), PlayerNames.From(level.Players));
        AddChild(_messages);
        // Decode likely clips on workers while the level loads and read them before the first tick.
        // Decode the rest on first use, also off this thread.
        _soundOutput = new ClassicSoundOutput { Name = "Sound", Spatial = true };
        AddChild(_soundOutput);
        _mixer = new ClassicSoundMixer(_soundOutput, new InstallClassicSoundAssets(install), 0);
        _sound = new LevelSoundDriver(_mixer, level.Database, _viewer);
        Task soundPrefetch = _mixer.Prefetch(LevelSoundPrefetch.Categories(level.Database, _simulation.World, _sound.LevelSpeech),
            Math.Max(1, System.Environment.ProcessorCount - 1));
        // Create the battle tab after the message tabs and above the bottom panel.
        _battles = _sound.Music;
        // --sound-log: each request as it starts on a source, with its sample (a check; nothing subscribes otherwise).
        if (Array.IndexOf(args, "--sound-log") >= 0)
        {
            GameSimulation logged = _simulation;
            _mixer.Started += (request, sample) => GD.Print("DK2.Godot sound: frame ", Engine.GetProcessFrames(), " tick ",
                logged.World.CurrentTick.Value, " ", request.Category.Name, " 0x", request.Id.Value.ToString("X", CultureInfo.InvariantCulture),
                request.Voice == SoundVoiceId.None ? "" : " voice 0x" + request.Voice.Value.ToString("X8", CultureInfo.InvariantCulture),
                request.World is System.Numerics.Vector3 at ? " at " + at.ToString("F2", CultureInfo.InvariantCulture) : "",
                " -> ", sample.Archive, ":", sample.Entry.Value);
        }
        _battleTab = new ClassicBattleTab { Name = "BattleTab" };
        _battleTab.Open(_art.Image, _art.Text);
        _battleTab.Clicked += BattleTabClicked;
        AddChild(_battleTab);
        // Draw the options menu over every panel it covers and route its mouse presses first.
        _options = new ClassicOptionsPanel { Name = "Options" };
        _options.Open(_sources.Text, _art.Font);
        _options.SetSupported(ClassicOptionsAction.Save, true);
        _options.SetSupported(ClassicOptionsAction.Load, true);
        _options.SetSupported(ClassicOptionsAction.GameOptions, true);
        // Its press swallow reads the router's own live state (ClassicHudControls), never the drawn menu's.
        _options.HoldsPresses = () => _hudControls.OptionsVisible;
        AddChild(_options);
        // Create the save browser after the menu. Only one shows at a time.
        _gameOptions = new InGameOptionsController(_hudControls, _optionsPages);
        _gameOptionsPanel = new ClassicGameOptionsPanel { Name = "GameOptions" };
        _gameOptionsPanel.Open(_gameOptions, _sources.Text, _art.Font, _sources.Texture);
        AddChild(_gameOptionsPanel);
        // Listing only reads: nothing is created there.
        string savesDirectory = folders.Saves;
        _saves = new SaveSlotsView(savesDirectory);
        // Draw the game options after the options menu.
        _saveLoad = new ClassicSaveLoadPanel { Name = "SaveLoad" };
        _saveLoad.Open(_sources.Text, _art.Font);
        _saveLoad.Submit += SaveLoadSubmitted;
        _saveLoad.HudAction += ApplyHudAction;
        AddChild(_saveLoad);
        // Its bar's canvas draws under the panels and its text's over them by z index (ClassicHoverHud), not tree order.
        _hover = new ClassicHoverHud { Name = "HoverHud" };
        _hover.Open(_sources);
        AddChild(_hover);
        // Draw the full map over every panel and give it first chance at mouse presses. Its grid
        // is the minimap's.
        _fullMap = new ClassicFullMap { Name = "FullMap" };
        _fullMap.Open(_minimap.Model!, _art.Font, PlayerNames.From(level.Players), _sources.Texture);
        _fullMap.HoldsPresses = () => _hudControls.FullMapVisible;
        AddChild(_fullMap);
        // Draw the placement cursor and hand on their own canvas layers above the HUD.
        // are a creature's Icon1Resource and an object's GuiIconResource (0x0040D450).
        _placement = new ClassicPlacementOverlay { Name = "PlacementCursor" };
        _placement.Open(_sources.Texture);
        InstallBottomPanelArt panelArt = _art;
        DK2.Art.Dk2ArtCatalogue heldArt = _sources.Catalogue;
        _placement.Model.SpellIconName = id => panelArt.Icon(new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, id));
        _placement.Model.CreatureIconName = id => heldArt.CreatureIcons.TryGetValue(id, out DK2.Art.Dk2ArtResource? icon) ? icon.Name : null;
        _placement.Model.ObjectIconName = id =>
            heldArt.Objects.TryGetValue(id, out IReadOnlyList<DK2.Art.Dk2ArtResource>? art) && art.Count > 1 ? art[1].Name : null;
        AddChild(_placement);
        _cursorSheets = new InstallHandCursorSheets(install, ClassicFrontEndMenuAssets.DecodePng);
        _handCursor = new ClassicHandCursor { Name = "HandCursor" };
        _handCursor.Open(_cursorSheets);
        AddChild(_handCursor);
        _window = GetWindow();
        _window.WindowInput += ReadPress;
        _hudHover = new HudHoverText(_sources.Strings.Resolve);
        // Only world hover reads the campaign slot that was played.
        _typeText = new WorldHoverText(level.Database, _sources.Strings.Resolve, _viewer, CampaignSlot.ForLevel(levelName));
        // Apply the loaded settings once the level is up.
        ApplySettings(_settings);
        // Observe simulation view requests before the first turn.
        _scriptedView = new ScriptedViewDirector(_viewer, EngineCameraPaths.Loader(install),
            LevelSubtitles.Open(install, level.Database.Level?.TextTableId ?? 0, warning => GD.PushWarning(warning)).Line);
        _scriptedView.GuiVisibilityRequested += _hudControls.SetGuiVisible;
        _scriptedView.ZoomRequested += ScriptZoom;
        // The thing sounds, scripted view and Level6a's torture speech (ADR-0809) receive events first. The effects
        // observe it next.
        _sound.WireSpeech(_scriptedView);
        OpenSubtitles();
        _simulation.ObserveWith(_sound.Observe(new Level6aTortureSpeech(level.Database, _messageView, _scriptedView)));
        // Record accepted drops for the hand cursor, then notify the view.
        _handCursorEvents = new HandCursorEventSink(_cursorFeedback, _viewer, _world.OpenEffects(_simulation, _worldView, _viewer, seed));
        _simulation.ObserveWith(_handCursorEvents);
        _world.Sound = _sound;
        _frameDriver = new FrameDriver(_pending, _worldView, _pacer, _journal);
        // A restored checkpoint already includes its starting turn.
        if (!resumed)
        {
            for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++)
            {
                _simulation.Tick(InputFrame.Empty);
            }
        }
        // The effect model runs one update before the first frame.
        _world.StartEffects();
        // The --light-probe-* checks render without torch lights or with a camera offset of (1, 0, 1).
        // --light-fill enables fill lighting.
        _world.ProbeWithoutTorches = Array.IndexOf(args, "--light-probe-no-torches") >= 0;
        _world.Fill = Array.IndexOf(args, "--light-fill") >= 0;
        // --light-perpixel: DK2's light per pixel instead of per vertex (WorldScene.PerPixelLight); classic is the default.
        _world.PerPixelLight = Array.IndexOf(args, "--light-perpixel") >= 0;
        // --light-shadows: real torch shadows on that per-pixel light (WorldScene.TorchShadows); nothing without it.
        _world.TorchShadows = Array.IndexOf(args, "--light-shadows") >= 0;
        // --light-post: ambient occlusion over DK2's light (WorldScene.PostOcclusion).
        _world.PostOcclusion = Array.IndexOf(args, "--light-post") >= 0;
        bool probeShift = Array.IndexOf(args, "--light-probe-shift") >= 0;
        _reportTicks = _world.ProbeWithoutTorches || probeShift || Array.IndexOf(args, "--light-probe") >= 0;
        // The first frame's terrain, built whole; each frame's Present pushes the changes since.
        _world.Open(install, level, _simulation.World, _worldView, _viewer, startFocus, _settings.ScrollSpeed);
        TabletMesh? tabletMesh = _world.LoadTabletMesh();
        OpenWinScreen(install, level.Database.Level, tabletMesh);
        OpenBattleFlyIn(tabletMesh);
        if (probeShift)
        {
            _world.Focus += new System.Numerics.Vector3(1, 0, 1);
        }

        _world.SetBrightness(DisplaySettings.BrightnessExponent(_settings.Gamma));
        OpenPossession(install, level, args);
        OpenPossessionHud(install, level);
        // --perf-report: the performance harness (PerformanceReport.cs); without it, no node.
        PerformanceReport.Attach(this, args, levelName, seed, _world, _simulation.World.Width, _simulation.World.Height);
        // --record-frames <dir> enables per-frame CSV telemetry.
        _recorder = FrameRecorder.Attach(this, args, levelName, seed);
        OpenDevOverlay(args, level);
        // Use the frame's real interval. Godot otherwise bends it toward its
        // 60 Hz physics clock (main_timer_sync.cpp), so the draw clock stepped evenly across uneven frames. No physics runs
        // here. Movie Maker keeps the engine's setting, whose fixed steps its captures were made with.
        _movie = Engine.GetWriteMoviePath().Length > 0;
        if (!_movie)
        {
            Engine.PhysicsJitterFix = 0;
        }
        // Queue the heart's health changes for the next frame's beat.
        int maximumHeartHealth = level.Database[VariableTypeId.DungeonHeartObjectHealth];
        WorldScene world = _world;
        _heartHealthObserver = change => world.QueueHeartHealthChange(change.Owner,
            DungeonHeartVisual.FromHealth(currentHealth: change.Health, maximumHealth: maximumHeartHealth));
        _simulation.World.HeartHealthChanged += _heartHealthObserver;
        _glideTo = LaunchArguments.GlideTarget(args, "--");
        _pointerScript = LaunchArguments.PointerPath(args, "--");
        OpenKeyScript(args);
        TakeCarriedScriptFrames();
        if (_pointerScript == null && LaunchArguments.Value(args, "--pointer-path") != null)
        {
            GD.PushWarning("DK2.Godot: --pointer-path is malformed (LaunchArguments.ParsePointerPath); the OS pointer is used.");
        }
        // A scripted pointer leaves the owner's pointer as it is, and the OS mouse stops at one gate added last.
        if (_pointerScript != null)
        {
            _handCursor.ManageSystemPointer = false;
            AddChild(new ScriptedPointerGate { Name = "ScriptedPointerGate" });
        }
        if (Array.IndexOf(args, "--query-first-creature") >= 0)
        {
            foreach (CreatureState creature in _simulation.World.OwnedCreatures(_viewer))
            {
                _queryView.Query(new CreatureQueryTarget(creature.Id));
                break;
            }
        }

        if (Array.IndexOf(args, "--stage-battle") >= 0)
        {
            StageBattle(_simulation);
        }

        if (LaunchArguments.StageLightning(args, "--"))
        {
            _lightningStage = LightningStaging.Prepare(_simulation, _viewer, _pending);
            if (_lightningStage is LightningStaging stage)
            {
                _world.ClassicCamera?.Rotate(stage.CameraTurn);
                _world.Focus = CameraFocus.Over(new Fp32(CreatureState.CentreOf(stage.Focus.X)),
                    new Fp32(CreatureState.CentreOf(stage.Focus.Y)), _world.Focus.Y);
                GD.Print("DK2.Godot: staged lightning caster=" + stage.Caster.Value + " target=" + stage.Target.Value
                    + " tile=" + stage.Focus + "; specials unlock lightning and fund a paid Thunderbolt after thirty turns.");
            }
            else
            {
                GD.PushWarning("DK2.Godot: --stage-lightning needs Mistress, Monk, heroes and three owned dry floor tiles.");
            }
        }
        // The check arguments --stage-win and --stage-loss: the viewer's verdict, set after the first turn.
        byte stageVerdict = Array.IndexOf(args, "--stage-win") >= 0 ? PlayerStatus.Won
            : Array.IndexOf(args, "--stage-loss") >= 0 ? PlayerStatus.Lost : (byte)0;
        if (stageVerdict != 0 && !VerdictRules.End(_simulation.World, _simulation.Database, _viewer, stageVerdict))
        {
            GD.PushWarning("DK2.Godot: --stage-win or --stage-loss found the viewer's game already ended.");
        }

        _leaveResult = Array.IndexOf(args, "--leave-result") >= 0;
        _escapeResult = Array.IndexOf(args, "--escape-result") >= 0;
        if (Array.IndexOf(args, "--open-full-map") >= 0)
        {
            ApplyHudAction(ClassicHudAction.ToggleFullMap);
        }

        if (Array.IndexOf(args, "--open-options") >= 0)
        {
            ApplyHudAction(ClassicHudAction.ToggleOptions);
            // Only an entry the menu takes a press on: an unsupported one is ignored, as a click.
            if (Enum.TryParse(LaunchArguments.Value(args, "--open-options"), out ClassicOptionsAction entry))
            {
                if (_options.Supported(entry))
                {
                    _presses.PressOptions(entry);
                }
                else
                {
                    GD.PushWarning("DK2.Godot: --open-options ignores " + entry + ", which the menu does not support.");
                }
            }
        }
        if (resume == null)
        {
            _saveAs = LaunchArguments.Value(args, "--save-as");
            _loadName = LaunchArguments.Value(args, "--load");
            if (long.TryParse(LaunchArguments.Value(args, "--save-tick"), NumberStyles.None, CultureInfo.InvariantCulture, out long saveTick))
            {
                _saveTick = new GameTick(saveTick);
            }
        }
        if (long.TryParse(LaunchArguments.Value(args, "--hash-at"), NumberStyles.None, CultureInfo.InvariantCulture, out long hashTick))
        {
            _hashTick = new GameTick(hashTick);
        }

        GD.Print("DK2.Godot in-game HUD: ", levelName, " seed ", seed.ToString(CultureInfo.InvariantCulture), ", saves in ", savesDirectory,
            ", settings in ", _settingsPath);
        if (resumed)
        {
            PrintHash("resumed " + resume!.Resume!.Session.Header.DisplayName + " at");
        }
        else if (resume != null)
        {
            PrintHash("restarted at");
        }
        // Wait for the sound prefetch before the first frame.
        try { soundPrefetch.Wait(); }
        catch (AggregateException e) { GD.PushWarning("DK2.Godot: sound prefetch failed: ", e.InnerException?.Message); }
        GD.Print("DK2.Godot: sound prefetch decoded ", _mixer.DecodedMegabytes.ToString("F1", CultureInfo.InvariantCulture),
            " MB of samples");
        // The first frame is not drawn until the compile is done, so it does not wait on a method the thread has yet to reach.
        _jitWarmUp?.Join(TimeSpan.FromSeconds(JitWarmUpSeconds));
        GD.Print("DK2.Godot: level ready ", Time.GetTicksMsec().ToString(CultureInfo.InvariantCulture), " ms after the engine started.");
    }

    /// <summary>The longest the level load waits for <see cref="JitWarmUp"/>; a thread still running then goes on beside the frames.</summary>
    private const double JitWarmUpSeconds = 5;

    private Thread? _jitWarmUp;

    private void StartJitWarmUp()
    {
        _jitWarmUp = new Thread(() =>
        {
            long started = Stopwatch.GetTimestamp();
            int methods = JitWarmUp.Prepare(
            [
                typeof(InGameHud).Assembly, typeof(JitWarmUp).Assembly, typeof(GameSimulation).Assembly, typeof(GameDatabase).Assembly,
                typeof(GameDatabaseLoader).Assembly, typeof(TilePos).Assembly, typeof(DK2.Pathfinding.ReachableTiles).Assembly,
                typeof(DK2.Art.Dk2ArtCatalogue).Assembly,
            ]);
            GD.Print("DK2.Godot: compiled ", methods.ToString(CultureInfo.InvariantCulture), " methods ahead of use in ",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture), " ms.");
        })
        { Name = "DK2 JIT warm-up", IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
        _jitWarmUp.Start();
    }

    public override void _Process(double delta)
    {
        if (_failed)
        {
            return;
        }

        if (_simulation == null || _view == null || _panel == null || _model == null || _resourceView == null
            || _resources == null || _messageView == null || _messages == null || _hover == null || _hudHover == null
            || _overviewView == null || _overview == null || _queryView == null || _queryPanel == null
            || _battles == null || _battleTab == null || _options == null || _saveLoad == null || _optionsPages == null
            || _gameOptionsPanel == null || _fullMap == null || _minimap == null || _worldView == null || _scriptedView == null
            || _resultFlow == null || _frameDriver == null || _world == null || _hoverCursor == null || _handCursor == null
            || _placement == null || _leaving)
        {
            return;
        }

        _recorder?.BeginFrame();
        _devTiming = _devOverlay?.Active;
        _devHoverTile = null;
        SamplePointer();
        PlayKeyScript();
        JournalBeginFrame(delta);
        // The ticks for the time since the last frame, at that frame's engine time.
        int tickCount = RunFrameTicks(delta, (long)(_elapsed * 1000), out double tickMs);
        if (_failed)
        {
            return;
        }
        // Engine time, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        long milliseconds = (long)(_elapsed * 1000);
        // Queue the effects whose clips finished decoding, then update speech.
        long started = PartStamp();
        _mixer?.PlayDecoded(milliseconds);
        _mixer?.UpdateSpeech(milliseconds);
        PartSpent(FrameTelemetryPart.Sound, started);
        if (_loadName != null || (_saveAs != null && _simulation.World.CurrentTick >= _saveTick))
        {
            RunSaveLoadCheck();
        }
        // Update thing sounds, voices and ambience around the tile under the screen's centre,
        // using the last frame's HUD. Update the speech subtitle and tick music once per settled turn.
        // The battle tab reads the music observer's battle records.
        started = PartStamp();
        if (_sound != null)
        {
            _sound.Update(_simulation.World, PointerTile(GetViewport().GetVisibleRect().Size * .5f, _pressVisibility), milliseconds);
        }
        else
        {
            _battles.Capture(_simulation.World, _simulation.Database, _simulation.World.CurrentTick);
        }

        PartSpent(FrameTelemetryPart.Sound, started);
        // Capture the minimap grid once per settled turn before drawing the HUD. The full map shares it.
        bool minimapCaptured = _minimap.Capture(_simulation.World, _worldView, _simulation.Navigation);
        // Advance the scripted view (FrameDriver) using the drawn turn plus the debt toward the next
        // (this scene ticks inline, so none is pending), then the level end, which may leave for the front end.
        started = PartStamp();
        _frameDriver.UpdateScript(_scriptedView, milliseconds, _simulation.World, _viewer, ticksPending: 0);
        ReportPath();
        bool checkKeys = _resultFlow.ResultRecorded && !_scriptedView.PathPlaying;
        bool escapeCheck = _escapeResult && checkKeys;
        bool? speechPlaying = _mixer != null && _mixer.SpeechEnabled ? _mixer.SpeechBusy : null;
        var result = new CampaignResultFrame(_viewer, _scriptedView.PathPlaying, milliseconds, speechPlaying,
            _keyboard.SpacePressed || (_leaveResult && checkKeys && !escapeCheck), _keyboard.EscapePressed || escapeCheck);
        bool leave = _resultFlow.Update(_simulation.World, _simulation.Database, result, _pending);
        PartSpent(FrameTelemetryPart.Script, started);
        if (leave)
        {
            LeaveForFrontEnd(_context?.ReturnPage ?? FrontEndPage.None, "the level ended");
            return;
        }
        if (escapeCheck)
        {
            ReportEscape();
        }

        started = PartStamp();
        // Handle local pickups, then Escape or Space ends a playing camera path
        // and posts SkipCameraPathCommand (0x00405362); the script's glide starts.
        _frameDriver.AdvancePickups(_simulation.World, _viewer, milliseconds);
        FrameScriptInput script = _frameDriver.TakeScriptedInput(_scriptedView, _viewer, _keyboard.EscapePressed || _keyboard.SpacePressed);
        if (script.Glide is TilePos glide)
        {
            StartGlide(glide);
        }
        // Handle options, full map, GUI visibility, and Heart, portal and fight glides. A camera path
        // holds the options key back that frame. While the
        // keyboard page captures a key, no shortcut fires.
        UpdatePossession(_scriptedView.PlayedHud(_hudControls, ClassicCameraMode.Dungeon), delta, milliseconds);
        HudVisibility camera = _scriptedView.HudFor(ClassicCameraMode.Dungeon);
        ClassicHudAction shortcut = _keyboard.Read(_settings).HudShortcut(
            PossessionHud.ShortcutContext(_hudControls.ShortcutContext(camera, script.AllowOptionsKey,
                capturingKey: _optionsPages.CapturingAction != null), _possession?.CameraHeld == true, _possession?.Possessing == true,
                firstPersonCameraSettled: _possession?.SettledPossessionCamera == true),
            _keyboard.EscapePressed);
        ApplyHudAction(shortcut);
        // A camera path, the letterbox, the result
        // and the full map hide the normal HUD.
        HudVisibility menuVisibility = _scriptedView.PlayedHud(_hudControls, ClassicCameraMode.Dungeon);
        HudVisibility visibility = PossessionHud.OrdinaryVisibility(menuVisibility, _possession);
        bool modal = _hudControls.Modal;
        _panel.SetVisibility(_hudControls.ModalInput(visibility));
        _options.SetVisible(_hudControls.OptionsMenuShown(menuVisibility));
        _gameOptionsPanel.Show(_hudControls.GameOptionsShown(menuVisibility));
        _fullMap.SetVisible(_hudControls.FullMapShown(camera));
        _minimap.SetVisibility(PossessionHud.MinimapVisibility(menuVisibility, _possession));
        _options.SetPage(_hudControls.OptionsPage, _hudControls.Confirmation);
        // Pass this frame's typed characters to the visible save browser.
        (bool saveLoadShown, bool saving) = _hudControls.SaveLoadPage(menuVisibility);
        _saveLoad.Show(_saves, saveLoadShown, saving, _keyboard.Typed);
        // Update the camera before routing the world input. The game options page polls
        // the keys before they end.
        UpdateCamera(visibility, delta);
        // The frame's one pose, which this frame's picks read; the follow below places it again only if it moved the focus.
        _scriptedView.SeekCamera(_simulation.World.CurrentTick.Value + Math.Min(_pacer.Debt, 1d));
        _world.ScriptedView = _scriptedView;
        _world.PlaceCamera();
        DrawLetterbox();
        DrawSubtitles();
        DrawResultPrompt();
        _fullMap.PreparePreview(_world, _pointer);
        if (visibility.AllowOrdinaryInput && !_hudControls.Modal)
        {
            ReadAmbientKeys();
        }

        QueueInput(visibility, milliseconds);
        // Preview the Hand's excavation tags before presenting the world. Off the map the far end keeps
        // the last tile the pointer was on.
        TilePos? dragFrom = _input.DragFrom(PlayerToolMode.Hand);
        _dragTo = dragFrom == null ? null : PointerTile(_pointer, visibility) ?? _dragTo ?? dragFrom;
        _worldView.PreviewTags(dragFrom, _dragTo, _input.ExcavationPreviewTagged ?? true);
        PartSpent(FrameTelemetryPart.Input, started);
        // Present the settled world's changes before the frame's ticks. The chunk build it
        // starts runs on the thread pool while they do (ADR-0696).
        // Draw objects at the drawn turn and fraction toward the next
        // far toward the next (this scene ticks inline, so the debt left by the last frame).
        float drawFraction = (float)Math.Min(_pacer.Debt, 1d);
        double renderTick = _simulation.World.CurrentTick.Value + (double)drawFraction;
        _world.SetFrameTime(_simulation.World.CurrentTick, drawFraction, milliseconds);
        _world.LatchPossession(_simulation.World, _viewer);
        started = PartStamp();
        _world.Present(_simulation.World, _elapsed);
        _world.PresentRoof(_simulation.World, _simulation.Database);
        PartSpent(FrameTelemetryPart.Present, started);
        started = PartStamp();
        _world.DrawFrame(_simulation.World.CurrentTick, drawFraction);
        PartSpent(FrameTelemetryPart.Animation, started);
        // Keep the queried creature where it
        // is drawn, so the view and the minimap below draw the same focus; the camera is placed again only if it moved,
        // then the blended draws are sorted for it.
        System.Numerics.Vector3 focus = _world.Focus;
        FollowQueriedCreature();
        if (_world.Focus != focus)
        {
            _world.PlaceCamera();
        }
        // The pick is last frame's. Resolve the new hover after drawing the HUD.
        started = PartStamp();
        _world.SetHandLight(_handLight);
        _world.FinishFrame();
        PartSpent(FrameTelemetryPart.Lights, started);
        if (_reportTicks && _simulation.World.CurrentTick.Value != _reportedTick)
        {
            _reportedTick = _simulation.World.CurrentTick.Value;
            GD.Print("DK2.Godot: tick ", _reportedTick, " first drawn on frame ", Engine.GetProcessFrames(), " at fraction ", drawFraction);
        }
        _keyboard.EndFrame();
        // Draw the HUD from the settled world before the next simulation advance.
        started = PartStamp();
        _resourceFacts = _resourceView.Capture(_simulation.World, displayHealth: visibility.ShowNormalHud);
        // Resolve FLASH_BUTTON before the resources draw. Drop the flash once its item is in use.
        // draw their flash.
        _model.FlashTarget = _scriptedView.PanelFlash(_tools, _model.OpenPanel);
        _resources.SetVisibility(visibility);
        if (visibility.ShowNormalHud)
        {
            _resources.Draw(_resourceFacts, unchecked((uint)milliseconds), _scriptedView.ResourceFlash);
        }

        _view.Capture(_simulation.World);
        _model.GamePaused = _hudControls.TicksPaused;
        _panel.Draw(milliseconds);
        _messageView.Capture(_simulation.World, milliseconds, visibility);
        // Play the sounds selected by the HUD controls.
        PlayHudSounds();
        // As the HUD shows; taking input unless the options menu holds it.
        HudVisibility tabs = _hudControls.ModalInput(visibility);
        _messages.Draw(tabs, _model.Collapsed,
            HudIndicatorSnapshot.Capture(_simulation.World, _simulation.Database.Level, _viewer),
            _hudControls.OptionsVisible, _hudControls.ObjectiveVisible);
        // Draw the battle tab after the message tabs.
        _battleTab.Draw(tabs, _model.Collapsed,
            BattleTabView.Capture(_simulation.World, _simulation.Database, _viewer, _battles.Battles), milliseconds);
        // Draw the creature overview after the battle tab. The options menu closes it.
        _overviewView.Capture(_simulation.World);
        _overview.Draw(visibility, modal ? null : _model.PanelArea(BottomPanelCategory.Creatures));
        // Draw the details after the overview, with the selected corner lit.
        MinimapCameraView? drawn = _world.View;
        _minimap.Draw(_tools.SelectedCorner, _hudControls.OptionsVisible, MinimapHud.FixedFocus(_world.Focus),
            drawn?.Heading ?? ClassicCamera.LevelStartYaw, drawn,
            _possession?.SettledPossessionCamera == true);
        // Draw the message tabs after the minimap.
        _options.Draw();
        // Capture the save browser after the bottom panel and draw it after the options menu.
        _fullMap.Capture(_simulation.World, _simulation.Database, _viewer, minimapCaptured, _pointer);
        _fullMap.Draw();
        // Draw the game options after the save browser. The two never show together.
        _gameOptionsPanel.Draw(_pointer);
        // Draw the full map last. The options menu hides it.
        _queryPanel.Draw(_queryView.Capture(_simulation.World), visibility, modal);
        DrawPossessionHud(milliseconds);
        // Resolve hover after drawing the HUD: over the HUD, use the panels' text. Over the world resolve
        // the pick and its text, the Hand's light, then the cursor, placement, tint, cage and hover sound (HoverCursorResolver).
        _hover.SetVisibility(visibility, milliseconds);
        // A failed pick with nothing hovered sends DK2's light to an edge tile, which blacks it out (0x0043BCB8); with
        // ordinary input off (a cutscene) it keeps its place.
        if (visibility.AllowOrdinaryInput)
        {
            _handLight = null;
        }

        bool inside = _pointerInWindow && GetViewport().GetVisibleRect().HasPoint(_pointer);
        bool allowed = visibility.AllowOrdinaryInput && Focused && inside && !PossessionPointerHeld;
        bool entryFeedback = PossessionHighlight.TransitionPointerFeedback(_possession, menuVisibility) && Focused && inside;
        bool feedbackAllowed = allowed || entryFeedback;
        bool overHud = PointerOverHud(_pointer, visibility);
        WorldHoverHit? pick = null;
        HoverTextSnapshot hover = HoverTextSnapshot.Empty;
        if (feedbackAllowed && !overHud && PointerTile(_pointer, visibility) is TilePos tile)
        {
            if (_devTiming != null)
            {
                _devHoverTile = tile;
            }

            _pointerWorld = PointerPosition(_pointer, tile)
                ?? new FpPosition2(new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)));
            long before = GC.GetAllocatedBytesForCurrentThread();
            WorldHoverHit hit = ResolveWorldHover(_pointer, tile, visibility);
            if (_pickWarmUp > 0)
            {
                _pickWarmUp--;
            }
            else { _pickBytes += GC.GetAllocatedBytesForCurrentThread() - before; _pickFrames++; }
            AimHandLight(_simulation.World, tile, hit);
            string personalName = hit.Kind == WorldHoverKind.Creature && _identityNames != null
                && _simulation.World.CreatureById(hit.EntityId) is CreatureState named
                ? _identityNames.Name(new CreatureNameIndex(named.NameIndex)) : "";
            hover = _typeText?.Capture(_simulation.World, hit, personalName) ?? HoverTextSnapshot.Empty;
            pick = hit;
        }
        if (allowed && overHud)
        {
            hover = _hudHover.Capture(this, _resourceFacts);
        }

        HoverFeedback feedback = _hoverCursor.Resolve(_simulation.World, _simulation.Database, _simulation.Navigation,
            new HoverCursorInput(pick, feedbackAllowed, overHud, PrimaryHeld, _resourceFacts.Gold.Value, milliseconds, _pointerWorld));
        long tintBefore = GC.GetAllocatedBytesForCurrentThread();
        if (feedback.Tinted && pick is WorldHoverHit tinted)
        {
            _world.DrawHoverOverlay(tinted, feedback.TintBlended);
        }

        DrawPossessionHighlight(pick, feedback, entryFeedback);
        DrawPossessionEntryBadge(milliseconds, pick, feedback, visibility.ShowNormalHud);
        _world.CommitHoverOverlay();
        if (_pickFrames > 0 && pick != null)
        {
            _pickBytes += GC.GetAllocatedBytesForCurrentThread() - tintBefore;
        }

        if (_mixer != null && feedback.HoverSound is SoundRequest hoverSound)
        {
            _mixer.Play(hoverSound);
        }

        if (_mixer != null)
        {
            _mixer.Play(_cursorFeedback.Sounds);
        }
        else
        {
            _cursorFeedback.Sounds.Clear();
        }

        _handCursor.Update(feedback.Cursor, allowed, milliseconds);
        _handCursor.Draw(_pointer);
        _hover.Draw(hover, _pointer, feedback.TooltipReach, milliseconds);
        PlacementFeedback placement = feedback.Placement;
        if (allowed && placement.Icon == PlacementIcon.Held)
        {
            _heldStack.Refresh(_simulation.World, _viewer, _worldView.LocalPickups);
        }

        _placement.Draw(allowed ? placement : PlacementFeedback.None, _pointer, overHud, _heldStack, feedback.Cursor);
        _world.DrawCage(placement.Cage, placement.Valid, milliseconds);
        // Every blended item is in (the effects and flames since FinishFrame, the flowers above), and every blended thing
        // Godot sorts on its own is placed and looked (the hover's looks, the cage): ordered among them and drawn.
        _world.DrawBlendedItems();
        DrawBattleFlyIn(visibility, _model.Collapsed, milliseconds);
        PartSpent(FrameTelemetryPart.Hud, started);

        // Record the drawn turn and fraction and the drawn camera's eye.
        _recorder?.EndFrame(tickCount, tickMs, renderTick, _world.View?.Position ?? default, _scriptedView.PathPlaying,
            _world.ChunkFlushMilliseconds);
        // Finish the frame's journal record.
        _journal.EndFrame(_pending, _pacer.Debt);
    }

    /// <summary>Advance simulation time through <see cref="FrameDriver"/>. Apply ready drops, then
    /// observe the tick gate. The gate prevents turns from running or accruing while it holds.
    /// Run at the top of the frame after the preceding frame has submitted its input. Returns the turns run.</summary>
    private int RunFrameTicks(double delta, long lastMilliseconds, out double tickMs)
    {
        GameSimulation simulation = _simulation!;
        // The interval that just ended (Godot's delta, unbent: _Ready). Under Movie Maker the last frame's step, the time
        // these ticks took when they ran last in that frame, so its captures keep every frame (its steps are not all equal
        // in the last bit). Clamp the interval to 1/3 s so a stall is not paid back.
        double elapsed = _movie ? _lastDelta : delta;
        _lastDelta = delta;
        if (_lightningStage?.QueueThunderboltWhenReady(simulation, _pending) == true)
        {
            GD.Print("DK2.Godot: staged paid Thunderbolt at turn " + simulation.World.CurrentTick.Value + ".");
        }
        // The possession posts its pose once a turn.
        FrameTicks ticks = _frameDriver!.TakeTicks(simulation.World, _viewer, Math.Min(elapsed, TickPacer.MaximumFrameSeconds), 1f,
            _settings.GameSpeed, _hudControls.TicksPaused, TickPacer.DefaultMaxTicksPerFrame, poses: _possession);
        _journal.Ticks(simulation.World.CurrentTick, ticks.Count);
        long started = PartStamp();
        for (int i = 0; i < ticks.Count; i++)
        {
            InputFrame input = ticks.InputFor(i);
            long tick = simulation.World.CurrentTick.Value;
            _journal.BeforeTick(tick, input, simulation);
            try
            {
                if (tick + 1 == _failTick)
                {
                    throw new InvalidOperationException("--fail-tick " + _failTick + " failed this turn on purpose.");
                }

                simulation.Tick(input);
            }
            catch (Exception ex)
            {
                // When a simulation turn throws, stop and show why over the last frame drawn. Nothing is
                // rethrown into Godot's loop, which would only log it and draw on.
                _journal.TickFailed(ex, simulation);
                Fail(new GameFailure(GameFailureKind.TurnFailed, ex.Message, _levelName, tick + 1), overFrame: true, ex);
                tickMs = 0;
                return i;
            }
            _journal.AfterTick(tick + 1, simulation);
            if (simulation.World.CurrentTick == _hashTick)
            {
                PrintHash("hash-at");
            }
        }
        tickMs = _recorder?.Elapsed(started) ?? 0;
        _devTiming?.AddTurns(ticks.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        // After the ticks, a drop the simulation accepted plays the hand's drop animation on the clock
        // this frame's cursor reads.
        _handCursorEvents?.Drain((long)((_elapsed + delta) * 1000));
        _frameDriver.AfterTicks(simulation.World, lastMilliseconds);
        return ticks.Count;
    }

    /// <summary>The journal opens on the first frame. While it records, each frame's
    /// raw sample (Godot's top-left pointer, its buttons, wheel and physical keys since the last frame) starts the record.</summary>
    private void JournalBeginFrame(double delta)
    {
        if (!_journal.Resolved)
        {
            _journal.Open(_journalRoot, FrameJournalSession.FolderName(DateTime.UtcNow, GameSessionMode.Campaign, _levelName), JournalHeader);
        }

        if (!_journal.Recording || _scriptedView == null)
        {
            return;
        }

        Vector2 pointer = _pointer;
        HudVisibility hud = _scriptedView.PlayedHud(_hudControls, ClassicCameraMode.Dungeon);
        _journal.BeginFrame(new FrameInputRecord
        {
            Frame = (long)Engine.GetProcessFrames(),
            UnscaledDeltaSeconds = delta,
            Mode = FrameInputMode.Game,
            PointerX = pointer.X,
            PointerY = pointer.Y,
            ButtonsHeld = (PrimaryHeld ? FrameMouseButtons.Primary : 0)
                | ((_pointerScript != null ? _scripted.SecondaryHeld : Input.IsMouseButtonPressed(MouseButton.Right)) ? FrameMouseButtons.Secondary : 0)
                | (_pointerScript == null && Input.IsMouseButtonPressed(MouseButton.Middle) ? FrameMouseButtons.Middle : 0),
            ButtonsDown = _journalButtonsDown,
            ButtonsUp = _journalButtonsUp,
            WheelX = _journalWheelX,
            WheelY = _journalWheelY,
            TypedText = new string(_keyboard.Typed),
            KeysDown = _journalDown.ToArray(),
            KeysUp = _journalUp.ToArray(),
            KeysHeld = _journalHeld.ToArray(),
            Hud = (hud.AllowOrdinaryInput ? FrameHudFlags.OrdinaryInput : 0) | (_hudControls.Modal ? FrameHudFlags.Modal : 0)
                | (_hudControls.OptionsVisible ? FrameHudFlags.Options : 0) | (_scriptedView.PathPlaying ? FrameHudFlags.CameraPath : 0),
            ActiveTool = _tools.ActiveTool,
        }, _pending);
        _journalDown.Clear(); _journalUp.Clear();
        _journalButtonsDown = _journalButtonsUp = FrameMouseButtons.None;
        _journalWheelX = _journalWheelY = 0;
    }

    /// <summary>The journal header: the keys both adapters write, Godot's physical key table and its version.</summary>
    private IReadOnlyDictionary<string, string> JournalHeader()
    {
        Vector2I size = GetWindow().Size;
        Dictionary<string, string> header = FrameJournalSession.Header("godot", FrameJournalSession.GodotKeyTable,
            GameSessionMode.Campaign, _levelName, _seed, _viewer, fogOfWar: true, tickOnWorker: false, size.X, size.Y);
        header["godot.version"] = Engine.GetVersionInfo()["string"].AsString();
        return header;
    }

    /// <summary>The <c>--escape-result</c> check: once, the commands Escape posted; Space follows on later frames.</summary>
    private void ReportEscape()
    {
        _escapeResult = false;
        if (_simulation == null)
        {
            return;
        }

        var posted = new System.Text.StringBuilder();
        foreach (PlayerCommand command in _pending)
        {
            posted.Append(' ').Append(command.GetType().Name);
        }

        GD.Print("DK2.Godot: Escape on the result at tick ", _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture),
            " posted", posted.ToString(), "; status ", _simulation.World.Player(_viewer).Status.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Prints the viewer's status changes and a camera path's start and end, the moments the HUD hides and comes
    /// back.</summary>
    private void ReportPath()
    {
        if (_scriptedView == null || _simulation == null)
        {
            return;
        }

        byte status = _simulation.World.Player(_viewer).Status;
        if (status != _status)
        {
            _status = status;
            GD.Print("DK2.Godot: the viewer's status is ", status.ToString(CultureInfo.InvariantCulture), " at tick ",
                _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture), "; the HUD's input is ",
                _hudControls.InputSuppressed ? "suppressed" : "open");
        }
        if (_scriptedView.PathPlaying == _pathPlaying)
        {
            return;
        }

        _pathPlaying = _scriptedView.PathPlaying;
        GD.Print("DK2.Godot: camera path ", _pathPlaying ? "started" : "ended", " at tick ",
            _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture), "; the HUD ", _pathPlaying ? "hides" : "shows");
    }

    /// <summary>End the session and open <c>Main.tscn</c> at <paramref name="page"/>, from the campaign
    /// result or the Return to Menu option. Defer the change to the end of the frame. After a level end the
    /// front end plays the won map's title card and cutscene first (<c>ClassicFrontEndMenu.Movies.cs</c>).</summary>
    private void LeaveForFrontEnd(FrontEndPage page, string why)
    {
        _leaving = true;
        _keyboard.EndFrame();
        Main.s_returnPage = page;
        GD.Print("DK2.Godot: ", why, "; leaving for the front end at page ", page.ToString(), ".");
        Callable.From(ChangeToFrontEnd).CallDeferred();
    }

    private void ChangeToFrontEnd()
    {
        Error error = GetTree().ChangeSceneToFile("res://Main.tscn");
        if (error == Error.Ok)
        {
            return;
        }

        Main.s_returnPage = FrontEndPage.None;
        _leaving = false;
        GD.PushWarning("DK2.Godot: the front end could not be opened (", error.ToString(), ").");
    }

    private void PrintHash(string what)
    {
        if (_simulation == null)
        {
            return;
        }

        GD.Print("DK2.Godot: ", what, " tick ", _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture),
            ", state hash ", _simulation.StateHash().ToString("X16", CultureInfo.InvariantCulture));
    }

    /// <summary>The <c>--save-as</c> and <c>--load</c> check arguments, through the options menu's own pages: open the
    /// menu, press Save or Load, name or select the save, submit as a press on the page does, then close the menu so the
    /// level runs on. Each acts once; a resumed instance ignores both.</summary>
    private void RunSaveLoadCheck()
    {
        if (_presses == null || _saves == null)
        {
            return;
        }

        bool saving = _loadName == null;
        string name = (saving ? _saveAs : _loadName)!;
        if (saving)
        {
            _saveAs = null;
        }
        else
        {
            _loadName = null;
        }

        if (!_hudControls.OptionsVisible)
        {
            ApplyHudAction(ClassicHudAction.ToggleOptions);
        }

        _presses.PressOptions(saving ? ClassicOptionsAction.Save : ClassicOptionsAction.Load);
        if (saving)
        {
            _saves.SetName(name);
            PrintHash("saving " + name + " at");
        }
        else
        {
            int index = -1;
            for (int i = 0; i < _saves.Entries.Count; i++)
            {
                if (_saves.Entries[i].DisplayName == name)
                {
                    index = i;
                }
            }

            if (!_saves.Select(index)) { GD.PushError("DK2.Godot: --load found no save named ", name, " in ", _saves.Directory); return; }
        }
        SaveLoadSubmitted();
        ApplyHudAction(ClassicHudAction.ToggleOptions);
    }

    /// <summary>The <c>--stage-battle</c> test argument: a Knight of the heroes on the tile of the viewer's first creature,
    /// spawned through <see cref="WorldState.TrySpawnCreature"/>; the fight that follows, and so the
    /// battle tab, is the simulation's own. Level1 has no early fight to show the tab.</summary>
    private void StageBattle(GameSimulation simulation)
    {
        WorldState world = simulation.World;
        var heroes = new PlayerId(PlayerIds.Good);
        if (!world.HasPlayer(heroes)) { GD.PushWarning("DK2.Godot: --stage-battle needs the heroes' player."); return; }
        CreatureDef? knight = null;
        foreach (CreatureDef creature in simulation.Database.Creatures.Values)
        {
            if (creature.Name == "Knight") { knight = creature; break; }
        }

        if (knight is null)
        {
            return;
        }

        foreach (CreatureState creature in world.OwnedCreatures(_viewer))
        {
            if (world.TrySpawnCreature(simulation.Database, new CreatureTypeId(knight.CreatureId), heroes, creature.Tile) is null)
            {
                GD.PushWarning("DK2.Godot: --stage-battle found the creature pool full.");
            }

            return;
        }
    }

    /// <summary>Apply Menu 43 presses through <see cref="BattleTabView.Click"/>, with its
    /// suppressed-input and modal gate): the cancel's left click posts; the zoom goes to the router (the fight navigation's
    /// glide), and the cancel's and the alarm's glides start here.</summary>
    private void BattleTabClicked(BattleTabButton button, int mouse)
    {
        if (_simulation == null || _trapNavigation == null)
        {
            return;
        }

        BattleTabClick click = BattleTabView.Click(button, mouse, _hudControls, _simulation.World, _simulation.Database, _viewer,
            _trapNavigation, _thingEntries);
        if (click.HudAction != ClassicHudAction.None)
        {
            ApplyHudAction(click.HudAction);
        }

        if (click.Command != null)
        {
            _pending.Add(click.Command);
        }

        if (click.Glide is TilePos tile)
        {
            StartGlide(tile);
        }
    }

    /// <summary>Navigate to the next of the viewer's rooms of a type (<see cref="RoomNavigation.Entries"/>).</summary>
    private void NavigateRoom(RoomTypeId type)
    {
        if (_simulation == null || _roomNavigation == null)
        {
            return;
        }

        RoomNavigation.Entries(_simulation.World, _simulation.Database, _roomEntries);
        if (_roomNavigation.Next(_roomEntries, type) is TilePos tile)
        {
            StartGlide(tile);
        }
    }

    /// <summary>Navigate to the next of the viewer's doors of a kind, newest first.</summary>
    private void NavigateDoor(byte kind)
    {
        if (_simulation == null || _doorNavigation == null)
        {
            return;
        }

        ThingNavigation.DoorEntries(_simulation.World.Doors, _thingEntries);
        if (_doorNavigation.Next(_thingEntries, kind) is TilePos tile)
        {
            StartGlide(tile);
        }
    }

    /// <summary>Navigate to the next of the viewer's traps of a kind, newest first.</summary>
    private void NavigateTrap(byte kind)
    {
        if (_simulation == null || _trapNavigation == null)
        {
            return;
        }

        ThingNavigation.TrapEntries(_simulation.World.Traps, _thingEntries);
        if (_trapNavigation.Next(_thingEntries, kind) is TilePos tile)
        {
            StartGlide(tile);
        }
    }

    /// <summary>Action 26 (game action 4, 0x00442650): the focus glides to the tile's
    /// centre, kept inside the map, angles unchanged (<see cref="CameraGlide"/>).</summary>
    private void StartGlide(TilePos tile)
    {
        if (_world == null || _simulation == null)
        {
            return;
        }

        _glide.Start(_world.Focus, tile, _simulation.World.Width, _simulation.World.Height, _elapsed * 1000d);
    }

    /// <summary>During a camera path, sample the pointer alone. Otherwise process the glide,
    /// scripted follow (game action 17), and <see cref="ClassicCameraInput.Apply"/> while ordinary
    /// input is allowed, no menu is modal and the camera is not the possession camera.</summary>
    private void UpdateCamera(HudVisibility visibility, double delta)
    {
        ClassicCameraControls? controls = _world?.Controls;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        Vector2 top = _pointer;
        // The camera uses a y-up pointer. A pointer outside the window does not edge-scroll.
        System.Numerics.Vector2 pointer = _pointerInWindow ? new(top.X, height - top.Y) : new(-1, -1);
        float wheel = _cameraWheel;
        _cameraWheel = 0;
        if (controls == null || _world == null || _scriptedView == null || _simulation == null) { _cameraPointer = pointer; return; }
        if (_glideTo is TilePos target && _elapsed >= 1 && !_scriptedView.PathPlaying)
        {
            _glideTo = null;
            StartGlide(target);
            GD.Print("DK2.Godot: --glide-to tile (", target.X, ", ", target.Y, "), focus (", _glide.Target.X, ", ", _glide.Target.Z, ")");
        }
        if (_scriptedView.PathPlaying)
        {
            controls.AdvancePointer(default, false, false);
            _cameraPointer = pointer;
            return;
        }
        bool gliding = _glide.Active;
        if (gliding)
        {
            _world.Focus = _glide.Advance(_world.Focus, _elapsed * 1000d);
        }

        if (gliding && !_glide.Active)
        {
            GD.Print("DK2.Godot: the glide ended at focus (", _world.Focus.X, ", ", _world.Focus.Z, ")");
        }

        if (_scriptedView.FollowedCreatureId != 0 && !_glide.Active
            && _simulation.World.CreatureById(_scriptedView.FollowedCreatureId) is CreatureState followed)
        {
            _world.Focus = CameraFocus.Over(new Fp32(followed.PosX), new Fp32(followed.PosY), _world.Focus.Y);
        }

        if (visibility.AllowOrdinaryInput && !_hudControls.Modal && !PossessionCameraHeld && width > 0 && height > 0)
        {
            var sample = new CameraPointerSample(pointer, width, height, Focused, PointerOverHud(top, visibility), wheel);
            _world.Focus = ClassicCameraInput.Apply(controls, _world.Focus, _keyboard.Read(_settings), sample, _cameraPointer,
                _settings.MouseSensitivity, delta);
        }
        else
        {
            controls.AdvancePointer(default, false, false);
        }

        _cameraPointer = pointer;
    }

    /// <summary>While <see cref="ClassicCreatureQueryRules.Follows"/>, the focus keeps
    /// the queried creature where the frame drew it (<see cref="WorldScene.TryGetCreaturePosition"/>, between turns), so
    /// the focus does not step against the model.</summary>
    private void FollowQueriedCreature()
    {
        if (_world == null || _queryView == null || _scriptedView == null)
        {
            return;
        }

        if (!ClassicCreatureQueryRules.Follows(_tools.QueryTool, _queryView.Target, _scriptedView.PathPlaying, _hudControls.Modal))
        {
            return;
        }

        if (_world.TryGetCreaturePosition(_queryView.Target, out System.Numerics.Vector3 drawn))
        {
            _world.Focus = new System.Numerics.Vector3(drawn.X, _world.Focus.Y, drawn.Z);
        }
    }

    /// <summary>The Hand's light follows the pick (<see cref="HoverCursorResolver.TryHandLightPoint"/>).
    /// on a tile, the pointer's hit on the tile's top plane, or the tile's centre when that misses.</summary>
    private void AimHandLight(WorldState world, TilePos tile, in WorldHoverHit hit)
    {
        if (_world == null || _worldView == null)
        {
            return;
        }

        if (!HoverCursorResolver.TryHandLightPoint(world, hit, out System.Numerics.Vector2 point))
        {
            point = _world.TryPickRay(_pointer, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction)
                ? TileTopPlane.HandLightPoint(origin, direction, tile, _worldView.TileAt(world, tile.X, tile.Y).Solid)
                : new System.Numerics.Vector2(tile.X + .5f, tile.Y + .5f);
        }

        _handLight = HoverCursorResolver.HandLight(world, _viewer, hit, point, cheapLighting: false);
    }

    /// <summary>The ambient keys step DK2's ambient light (<see cref="ClassicAmbientLight"/>).</summary>
    private void ReadAmbientKeys()
    {
        GameBindingInput keys = _keyboard.Read(_settings);
        bool changed = false;
        if (keys.Pressed(GameAction.AmbientUp)) { _ambientLight.Increase(); changed = true; }
        if (keys.Pressed(GameAction.AmbientDown)) { _ambientLight.Decrease(); changed = true; }
        if (changed)
        {
            _world?.SetAmbient(_ambientLight.Normalized);
        }
    }

    /// <summary>The bottom panel's collapse moves the view axis.</summary>
    private void PanelCollapsed(bool collapsed)
    {
        _world?.SetPanelCollapsed(collapsed);
    }

    /// <summary>Apply the script's camera zoom.</summary>
    private void ScriptZoom(int zoom, int unused)
    {
        _world?.ClassicCamera?.SetZoom(new Fp32(zoom));
    }

    /// <summary>A mouse press reaching the window before any panel can mark it handled.
    /// <see cref="QueueInput"/> routes it in the next frame.</summary>
    private void ReadPress(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion) { ReadPossessionMotion(motion); return; }
        // A scripted pointer's presses are its own (SamplePointer); the OS pointer's are left out.
        if (@event is not InputEventMouseButton button || _pointerScript != null)
        {
            return;
        }

        if (button.ButtonIndex == MouseButton.Left) { if (button.Pressed)
            {
                _primaryDown = true;
            }
            else
            {
                _primaryUp = true;
            }
        }
        else if (button.ButtonIndex == MouseButton.Right) { if (button.Pressed)
            {
                _secondaryDown = true;
            }
            else
            {
                _secondaryUp = true;
            }
        }
        // Wheel notches summed over the frame, positive away from the user.
        else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
        {
            _cameraWheel += button.Factor;
        }
        else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
        {
            _cameraWheel -= button.Factor;
        }

        if (!_journal.Recording)
        {
            return;
        }

        FrameMouseButtons journalled = button.ButtonIndex switch
        {
            MouseButton.Left => FrameMouseButtons.Primary,
            MouseButton.Right => FrameMouseButtons.Secondary,
            MouseButton.Middle => FrameMouseButtons.Middle,
            _ => FrameMouseButtons.None,
        };
        if (button.Pressed)
        {
            _journalButtonsDown |= journalled;
        }
        else
        {
            _journalButtonsUp |= journalled;
        }

        if (!button.Pressed)
        {
            return;
        }

        if (button.ButtonIndex == MouseButton.WheelUp)
        {
            _journalWheelY += button.Factor;
        }
        else if (button.ButtonIndex == MouseButton.WheelDown)
        {
            _journalWheelY -= button.Factor;
        }
        else if (button.ButtonIndex == MouseButton.WheelRight)
        {
            _journalWheelX += button.Factor;
        }
        else if (button.ButtonIndex == MouseButton.WheelLeft)
        {
            _journalWheelX -= button.Factor;
        }
    }

    /// <summary>Route the pointer through <see cref="WorldPointerInput"/>. While ordinary
    /// input is held the world drag is dropped. Otherwise route the HUD press (<see cref="HudPressRouter"/>),
    /// the query tool's and a choice's world press (<see cref="WorldClickCommands"/>), the minimap's map press
    /// (<see cref="IWorldPointerHost.MinimapPress"/>), the tool, and a press's intent and translation on the picked tile (plan
    /// step 3e), behind the possession gate.</summary>
    private void QueueInput(HudVisibility visibility, long milliseconds)
    {
        KeyModifiers modifiers = _keyboard.Modifiers;
        var press = new HudPress(_primaryDown, _secondaryDown, (modifiers & KeyModifiers.Shift) != 0,
            (modifiers & KeyModifiers.Control) != 0, (modifiers & KeyModifiers.Alt) != 0);
        bool primaryUp = _primaryUp, secondaryUp = _secondaryUp;
        _primaryDown = false; _secondaryDown = false; _primaryUp = false; _secondaryUp = false;
        if (_simulation == null || _worldPointer == null)
        {
            return;
        }

        _pressPointer = _pointer;
        _pressVisibility = visibility;
        _worldPointer.Queue(new WorldPointerFrame(_viewer, visibility.AllowOrdinaryInput, Possessing, _hudControls.Modal, press,
            primaryUp, secondaryUp, milliseconds), _simulation.World, _simulation.Database, _pending);
    }

    /// <summary>A minimap press glides to the tile unless a camera path is playing.</summary>
    void IWorldPointerHost.MinimapPress(in HudPress press)
    {
        if (_scriptedView != null && !_scriptedView.PathPlaying && _minimap != null && press.Primary
            && _minimap.TryNavigate(_pressPointer, out TilePos tile))
        {
            StartGlide(tile);
        }
    }

    FpPosition2? IWorldPointerHost.PointerPosition(TilePos tile)
    {
        return PointerPosition(_pressPointer, tile);
    }

    /// <summary>The tile under a screen pixel (top-left origin) by <see cref="TilePicker"/>, which
    /// meets the columns at the heights the scene draws; null over the HUD, off the map or before the camera is placed.</summary>
    private TilePos? PointerTile(Vector2 at, HudVisibility visibility)
    {
        if (PointerOverHud(at, visibility) || _world == null || _worldView == null || _simulation == null)
        {
            return null;
        }

        if (!_world.TryPickRay(at, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction))
        {
            return null;
        }
        // TilePicker works in DK2's plane, Y south; the renderers' Z runs north.
        return TilePicker.Pick(new PickRay(origin.X, origin.Y, -origin.Z, direction.X, direction.Y, -direction.Z),
            _worldView, _simulation.World);
    }

    /// <summary>The pointer's point on the tile's top plane, in map coordinates
    /// (<see cref="TileTopPlane.PositionOn"/>).</summary>
    private FpPosition2? PointerPosition(Vector2 at, TilePos tile)
    {
        if (_world == null || _worldView == null || _simulation == null)
        {
            return null;
        }

        if (!_world.TryPickRay(at, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction))
        {
            return null;
        }

        return TileTopPlane.PositionOn(origin, direction, tile, _worldView.TileAt(_simulation.World, tile.X, tile.Y).Solid);
    }

    /// <summary>An unexplored tile is the bare tile. For an explored tile, use the ray's hit on its top
    /// plane bounds the pick (<see cref="WorldHoverPicker.TryCastTerrain"/>), which the drawn things are offered to under
    /// <see cref="HoverPickRules"/> (<see cref="WorldScene.PickHoverEntity"/>), culled by the placed view.</summary>
    private WorldHoverHit ResolveWorldHover(Vector2 at, TilePos tile, HudVisibility visibility)
    {
        var bare = new WorldHoverHit(WorldHoverKind.Tile, tile);
        if (_simulation == null || _world == null || _worldView == null)
        {
            return bare;
        }

        WorldState world = _simulation.World;
        if (HoverCursorResolver.FogHit(world, _viewer, tile) is WorldHoverHit fogged)
        {
            return fogged;
        }

        if (!_world.TryPickRay(at, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction)
            || _world.View is not MinimapCameraView view)
        {
            return bare;
        }

        if (!WorldHoverPicker.TryCastTerrain(origin, direction, _worldView.TileAt(world, tile.X, tile.Y).Solid,
                out float terrainDistance, out System.Numerics.Vector3 ground))
        {
            return bare;
        }

        _objectsOnlyPick = HoverCursorResolver.ObjectsOnlyKeyCounts(visibility, _hudControls.Modal)
            && _keyboard.Read(_settings).Held(GameAction.ObjectsOnly, exactModifiers: true);
        _renderedPick ??= (kind, id) => HoverPickRules.Rendered(_simulation.World, _simulation.Database, kind, id, _objectsOnlyPick);
        _fallbackPick ??= (kind, id) => HoverPickRules.Fallback(_simulation.World, _simulation.Database, kind, id, _objectsOnlyPick);
        _resolvesPick ??= candidate => HoverPickRules.Resolves(_simulation.World, candidate, _viewer);
        _picker.Begin(new HoverPickRay(origin, direction, System.Numerics.Vector3.Normalize(view.Forward)), terrainDistance, tile,
            ground, _renderedPick, _fallbackPick, _resolvesPick, _objectsOnlyPick, _world.PickFrustum);
        WorldHoverHit hit = _world.PickHoverEntity(_picker, origin, direction);
        return WorldHoverPicker.OnPointerTile(hit, tile);
    }

    /// <summary>Whether the window counts as focused: always under a scripted pointer, which a capture must not lose to
    /// the owner's other windows.</summary>
    private bool Focused
    {
        get
        {
            return _pointerScript != null || GetWindow().HasFocus();
        }
    }

    /// <summary>The primary button held, the scripted pointer's or the OS's.</summary>
    private bool PrimaryHeld
    {
        get
        {
            return _pointerScript != null ? _scripted.PrimaryHeld : Input.IsMouseButtonPressed(MouseButton.Left);
        }
    }

    /// <summary>The frame's pointer: the OS pointer, or the script's sample, whose motion and presses are pushed into the
    /// viewport for the panels and whose presses and releases are this frame's (<see cref="QueueInput"/>), as
    /// <see cref="ReadPress"/> takes the OS's; the OS mouse stops at <see cref="ScriptedPointerGate"/>. The OS pointer is
    /// never moved. A pushed event is a new object, so a scripted
    /// frame that moves or presses allocates; the OS path does not.</summary>
    private void SamplePointer()
    {
        if (_pointerScript == null)
        {
            _pointer = GetViewport().GetMousePosition();
            return;
        }
        _scripted = _pointerScript.At(++_scriptFrame);
        _pointer = new Vector2(_scripted.Position.X, _scripted.Position.Y);
        MouseButtonMask held = (_scripted.PrimaryHeld ? MouseButtonMask.Left : 0) | (_scripted.SecondaryHeld ? MouseButtonMask.Right : 0);
        Viewport viewport = GetViewport();
        if (_pointer != _scriptPushed)
        {
            Vector2 relative = float.IsNaN(_scriptPushed.X) ? Vector2.Zero : _pointer - _scriptPushed;
            viewport.PushInput(new InputEventMouseMotion
            {
                Device = ScriptedPointerGate.Device, Position = _pointer, GlobalPosition = _pointer, Relative = relative, ButtonMask = held,
            });
            _scriptPushed = _pointer;
        }
        if (_scripted.PrimaryDown)
        {
            PushScriptedButton(viewport, MouseButton.Left, true, held);
        }

        if (_scripted.SecondaryDown)
        {
            PushScriptedButton(viewport, MouseButton.Right, true, held);
        }

        if (_scripted.PrimaryUp)
        {
            PushScriptedButton(viewport, MouseButton.Left, false, held);
        }

        if (_scripted.SecondaryUp)
        {
            PushScriptedButton(viewport, MouseButton.Right, false, held);
        }

        _primaryDown |= _scripted.PrimaryDown; _primaryUp |= _scripted.PrimaryUp;
        _secondaryDown |= _scripted.SecondaryDown; _secondaryUp |= _scripted.SecondaryUp;
        if (!_journal.Recording)
        {
            return;
        }

        _journalButtonsDown |= (_scripted.PrimaryDown ? FrameMouseButtons.Primary : 0) | (_scripted.SecondaryDown ? FrameMouseButtons.Secondary : 0);
        _journalButtonsUp |= (_scripted.PrimaryUp ? FrameMouseButtons.Primary : 0) | (_scripted.SecondaryUp ? FrameMouseButtons.Secondary : 0);
    }

    private void PushScriptedButton(Viewport viewport, MouseButton button, bool pressed, MouseButtonMask held)
    {
        viewport.PushInput(new InputEventMouseButton
        {
            Device = ScriptedPointerGate.Device, Position = _pointer, GlobalPosition = _pointer, ButtonIndex = button, Pressed = pressed,
            ButtonMask = held,
        });
        if (pressed)
        {
            GD.Print("DK2.Godot: scripted ", button.ToString(), " press at (", _pointer.X, ", ", _pointer.Y, ") on frame ", _scriptFrame,
                ", tick ", _simulation?.World.CurrentTick.Value ?? 0);
        }
    }

    /// <summary>Apply the options key and panel actions through <see cref="HudActionRouter"/>: the options
    /// menu's entries, the save browser's Back and Continue lines and the minimap's corners (zoom, and query and sell, which
    /// post their GUI button and set the tool) and right click, and the portal, Heart and fight navigation's glides.</summary>
    private void ApplyHudAction(ClassicHudAction action)
    {
        ApplyHudAction(action, _pointer);
    }

    /// <param name="pointer">Where the press was, in screen pixels: the magnifier zooms only under it.</param>
    private void ApplyHudAction(ClassicHudAction action, Vector2 pointer)
    {
        _actionPointer = pointer;
        _router?.Apply(action, _panel, _view);
    }

    void IHudActionHost.NavigatePortal()
    {
        NavigateRoom(RoomTypeId.Portal);
    }

    void IHudActionHost.NavigateHeart()
    {
        if (_worldView?.MinimapHeartTile is TilePos heart)
        {
            StartGlide(heart);
        }
    }

    void IHudActionHost.NavigateFight()
    {
        if (_battles != null && _battleNavigation?.Next(_battles.Battles) is TilePos battle)
        {
            StartGlide(battle);
        }
    }

    void IHudActionHost.ZoomMinimap(bool zoomIn, bool zoomOut)
    {
        _minimap?.HandleZoomPress(_actionPointer, zoomIn, zoomOut);
    }

    void IHudActionHost.PostGuiButton(GuiButtonTarget target)
    {
        PostGuiButton(target);
    }

    // What HudPressRouter asks of this scene, at this frame's pointer: the panels' hit tests and the camera; the session is
    // InGameHud.SessionActions.cs's.
    void IHudPressHost.ApplyHudAction(ClassicHudAction action)
    {
        ApplyHudAction(action, _pressPointer);
    }

    void IHudPressHost.GoToFullMapTile()
    {
        if (_fullMap == null)
        {
            return;
        }

        if (_world == null || _simulation == null)
        {
            return;
        }

        TilePos tile = _fullMap.ResolvePointer(_pressPointer);
        _world.Focus = CameraFocus.TileCentre(tile, _world.Focus.Y, _simulation.World.Width, _simulation.World.Height);
        _glide.Stop();
    }

    void IHudPressHost.PollGameOptionsKeyboard()
    {
        _gameOptionsPanel?.CaptureKey(_keyboard.Read(_settings));
    }

    ClassicOptionsAction IHudPressHost.OptionsActionAtPointer()
    {
        return _options?.ActionAt(_pressPointer) ?? ClassicOptionsAction.None;
    }

    void IHudPressHost.RefreshSaves()
    {
        _saves?.Refresh();
    }

    bool IHudPressHost.TryPressOverview(in HudPress press, out CreatureOverviewRequest request)
    {
        request = default;
        return _overview != null && _overview.TryPress(_pressPointer, press, out request);
    }

    void IHudPressHost.FocusCreature(FpPosition3 position)
    {
        if (_world == null)
        {
            return;
        }

        _world.Focus = CameraFocus.Over(position.X, position.Y, _world.Focus.Y);
        _glide.Stop();
    }

    ClassicHudAction IHudPressHost.PressMinimap(in HudPress press)
    {
        return _minimap?.HandlePress(_pressPointer, press.Primary, press.Secondary) ?? ClassicHudAction.None;
    }

    KeeperResourceKind IHudPressHost.ResourceAtPointer()
    {
        return _resources?.ResourceAt(_pressPointer) ?? KeeperResourceKind.None;
    }

    // What WorldClickCommands and WorldPointerInput ask of this scene, at this frame's press pointer.
    bool IWorldClickHost.PointerOverHud()
    {
        return PointerOverHud(_pressPointer, _pressVisibility);
    }

    TilePos? IWorldClickHost.PointerTile()
    {
        return PointerTile(_pressPointer, _pressVisibility);
    }

    WorldHoverHit IWorldClickHost.Hover(TilePos tile)
    {
        return ResolveWorldHover(_pressPointer, tile, _pressVisibility);
    }

    void IWorldClickHost.FollowQueriedCreature()
    {
        _glide.Stop();
        if (_world?.ClassicCamera is ClassicCamera camera)
        {
            ClassicCreatureQueryRules.TurnToFollowHeading(camera);
        }
    }

    /// <summary>Apply the settings this scene reads, including game speed and key bindings,
    /// and the HUD scale on the ported panels (ApplyHudScale), the camera's scroll speed, the gamma and the sound
    /// (ApplySoundSettings). The other rendering settings (shadows, water, environment mapping) need systems not ported
    /// yet: their values change, and nothing acts on them.</summary>
    private void ApplySettings(GameSettings settings)
    {
        _settings = settings;
        if (_mixer != null)
        {
            _mixer.SoundQuality = settings.SoundQuality;
            _mixer.SetVolumes(master: settings.MasterVolume, speech: settings.SpeechVolume, music: settings.MusicVolume,
                effects: settings.SoundEffectVolume, speechOn: settings.SpeechSwitch, musicOn: settings.MusicSwitch,
                effectsOn: settings.SfxSwitch);
        }
        if (_world?.Controls is ClassicCameraControls controls)
        {
            controls.ScrollSpeed = settings.ScrollSpeed;
        }
        // Apply the HUD scale to each panel that uses it.
        int scale = settings.UiScale;
        if (_minimap != null)
        {
            _minimap.UiScale = scale;
        }

        if (_panel != null)
        {
            _panel.UiScale = scale;
        }

        if (_resources != null)
        {
            _resources.UiScale = scale;
        }

        if (_hover != null)
        {
            _hover.UiScale = scale;
        }

        if (_messages != null)
        {
            _messages.UiScale = scale;
        }

        if (_battleTab != null)
        {
            _battleTab.UiScale = scale;
        }

        if (_overview != null)
        {
            _overview.UiScale = scale;
        }

        if (_queryPanel != null)
        {
            _queryPanel.UiScale = scale;
        }

        if (_handCursor != null)
        {
            _handCursor.UiScale = scale;
        }
        // Apply the brightness curve to the 3D view.
        _world?.SetBrightness(DisplaySettings.BrightnessExponent(settings.Gamma));
        // Ours, not DK2's: the 3D view's anti-aliasing and render scale (audit item 17), off by default.
        _world?.SetRenderQuality(settings);
        _world?.SetCreatureShadows(settings.ShadowLevel > 0);
    }

    /// <summary>The settings file as <see cref="GameSettingsFile"/> reads it; the defaults, with a warning, when it cannot
    /// be read.</summary>
    internal static GameSettings LoadSettings(string path)
    {
        try { return GameSettingsFile.Load(path); }
        catch (Exception e) when (IsFileError(e))
        {
            GD.PushWarning("DK2.Godot: cannot read the settings file ", path, " (", e.Message, "); using the defaults.");
            return new GameSettings();
        }
    }

    /// <summary>Write the settings pages' save request through <see cref="GameSettingsFile"/>. A
    /// failed write is a warning, and the settings stay applied.</summary>
    private void SaveSettings(GameSettings settings)
    {
        SaveSettings(_settingsPath, settings);
    }

    internal static void SaveSettings(string path, GameSettings settings)
    {
        try { GameSettingsFile.Save(path, settings); }
        catch (Exception e) when (IsFileError(e))
        {
            GD.PushWarning("DK2.Godot: cannot write the settings file ", path, " (", e.Message, ").");
        }
    }

    private static bool IsFileError(Exception e)
    {
        return e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    }

    /// <summary>Handle the save browser's Enter key and row or Load presses.
    /// <see cref="SessionSaver"/> writes the typed name, world checkpoint and campaign profile.
    /// <see cref="SessionLoader"/> rebuilds the chosen simulation, writes its profile and reloads
    /// this scene. A refused file shows on the page.</summary>
    private void SaveLoadSubmitted()
    {
        if (_simulation == null || _saves == null || _saveLoad == null || _context == null || _saver == null || _loader == null)
        {
            return;
        }

        try
        {
            if (_hudControls.OptionsPage == ClassicOptionsPage.Save)
            {
                string path = _saver.Save(_simulation, _simulation.World.Carry, _context, _saves);
                GD.Print("DK2.Godot: saved ", path);
            }
            // A load already handed over waits for its reload; a second submit in the same frame is ignored.
            else if (_saves.Selected != null && s_resume == null)
            {
                s_resume = _loader.Load(_saves.Selected.Path);
                GD.Print("DK2.Godot: loaded ", _saves.Selected.Path, "; reloading the scene to continue it.");
                // Defer the scene change until the frame ends. A reload
                // takes this node out of the tree at once, and a submit can come from inside this frame's _Process.
                Callable.From(ReloadForResume).CallDeferred();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException
            or NotSupportedException)
        {
            GD.PushWarning("DK2.Godot: the ", _hudControls.OptionsPage.ToString(), " page refused: ", ex.Message);
            _saveLoad.Error(ex.Message);
        }
    }

    /// <summary>Reloads this scene to continue <see cref="s_resume"/> (a loaded save or a restart); a reload the tree refuses
    /// drops the handover, so the running level goes on and the next load can try again.</summary>
    private void ReloadForResume()
    {
        CarryScriptFrames();
        Error error = GetTree().ReloadCurrentScene();
        if (error == Error.Ok)
        {
            return;
        }

        s_resume = null;
        s_carriedScriptFrames = null;
        GD.PushWarning("DK2.Godot: the scene could not be reloaded to continue the save (", error.ToString(), ").");
        _saveLoad?.Error("The scene could not be reloaded.");
    }

    /// <summary>Load the campaign profile for this session through <see cref="UserFolders.LoadProfile"/>.</summary>
    private void LoadProfile()
    {
        if (_profiles != null && _context != null)
        {
            UserFolders.LoadProfile(_profiles, _profilePath, _context.Mode, _context.ReturnPage);
        }
    }

    /// <summary>Forward the panel selection to <see cref="HudToolState.SelectPanelChoice"/>.</summary>
    private void SelectPanelChoice(BottomPanelSelection selection)
    {
        _tools.SelectPanelChoice(selection, _input, _queryView);
    }

    /// <summary>A stored special's slot: action 88, or targeting with the panel and the tool back on the hand
    /// (<see cref="HudToolState.ActivateStoredSpecial(PlayerId, BottomPanelChoice, PlayerInputTranslator, BottomPanelView)"/>).</summary>
    private void ActivateStoredSpecial(BottomPanelChoice special)
    {
        // 0x00411140 ignores a special's slot while the Options menu is open (+0x2722).
        if (_hudControls.OptionsVisible)
        {
            return;
        }

        if (_view != null && _tools.ActivateStoredSpecial(_viewer, special, _input, _view) is PlayerCommand command)
        {
            _pending.Add(command);
        }
    }

    /// <summary>Action 0x72, a GUI button (ADR-0670).</summary>
    private void PostGuiButton(GuiButtonTarget target)
    {
        _pending.Add(new GuiButtonCommand(_viewer, target));
    }

    /// <summary>Refresh the controls', panel's, view's, messages' and settings pages'
    /// requests, then the messages' speech; without a mixer they are dropped so they do not pile up.</summary>
    private void PlayHudSounds()
    {
        if (_model == null || _view == null || _messageView == null || _optionsPages == null)
        {
            return;
        }

        if (_mixer == null)
        {
            _hudControls.Sounds.Clear(); _model.Sounds.Clear(); _view.Sounds.Clear(); _messageView.Sounds.Clear();
            _optionsPages.Sounds.Clear(); _messageView.Speech.Clear();
            return;
        }
        _mixer.Play(_hudControls.Sounds);
        _mixer.Play(_model.Sounds);
        _mixer.Play(_view.Sounds);
        _mixer.Play(_messageView.Sounds);
        _mixer.Play(_optionsPages.Sounds);
        _mixer.QueueSpeech(_messageView.Speech);
    }

    /// <summary>Collect the panels this scene has through <see cref="HudPointerMap"/>.</summary>
    private bool PointerOverHud(Vector2 position, HudVisibility visibility)
    {
        return HudPointerMap.Over(new PointerPanels(this, position, visibility), fullMapVisible: _hudControls.FullMapVisible,
            queryHasTarget: _queryView != null && _queryView.Target != CreatureQueryTarget.None, modal: _hudControls.Modal);
    }

    /// <summary>This scene's panels at one pointer, for <see cref="HudPointerMap"/>; a struct, so a query allocates nothing.</summary>
    private readonly struct PointerPanels(InGameHud hud, Vector2 position, HudVisibility visibility) : IHudPointerPanels
    {
        public bool Contains(HudPointerPanel panel)
        {
            return panel switch
            {
                HudPointerPanel.Minimap => hud._minimap != null && hud._minimap.ContainsPointer(position),
                HudPointerPanel.HoverHud => hud._hover != null && hud._hover.ContainsPointer(position),
                HudPointerPanel.ResourceStrip => hud._resources != null && hud._resources.ContainsPointer(position),
                HudPointerPanel.GameOptions => hud._gameOptionsPanel != null && hud._gameOptionsPanel.ContainsPointer(position),
                HudPointerPanel.BottomPanel => hud._panel != null && hud._panel.ContainsPointer(position),
                HudPointerPanel.MessageTabs => hud._messages != null && hud._messages.ContainsPointer(position),
                HudPointerPanel.BattleTab => hud._battleTab != null && hud._battleTab.ContainsPointer(position),
                HudPointerPanel.CreatureOverview => hud._overview != null && hud._overview.ContainsPointer(position),
                HudPointerPanel.OptionsMenu => hud._options != null && hud._options.ContainsPointer(position),
                HudPointerPanel.CreatureQuery => hud._queryPanel != null && hud._queryPanel.ContainsPointer(position, visibility),
                _ => false,
            };
        }
    }

    // Report the panels under the pointer to HudHoverText.
    int IHudHoverSources.OverviewTextId()
    {
        return _overview?.HoveredTextId(_pointer) ?? 0;
    }

    CreatureOverviewType? IHudHoverSources.OverviewColumn()
    {
        return _overview?.HoveredColumnType(_pointer);
    }

    HoverTextSnapshot IHudHoverSources.CreatureTypeText(CreatureOverviewType column)
    {
        return _typeText != null && _simulation != null ? _typeText.CreatureType(_simulation.Database.CreatureById(column.Value)) : HoverTextSnapshot.Empty;
    }

    bool IHudHoverSources.OverMessageTabs()
    {
        return _messages != null && _messages.ContainsPointer(_pointer);
    }

    HoverTextSnapshot IHudHoverSources.MessageTabsText()
    {
        return _messages?.HoverText ?? HoverTextSnapshot.Empty;
    }

    bool IHudHoverSources.OverBattleTab()
    {
        return _battleTab != null && _battleTab.ContainsPointer(_pointer);
    }

    HoverTextSnapshot IHudHoverSources.BattleTabText()
    {
        return _battleTab?.HoverText(_pointer) ?? HoverTextSnapshot.Empty;
    }

    bool IHudHoverSources.OverBottomPanel()
    {
        return _panel != null && _panel.ContainsPointer(_pointer);
    }

    HoverTextSnapshot IHudHoverSources.BottomPanelText()
    {
        return _panel?.HoverText ?? HoverTextSnapshot.Empty;
    }

    ClassicHudAction IHudHoverSources.MinimapControl()
    {
        return _minimap?.HoveredControl(_pointer) ?? ClassicHudAction.None;
    }

    KeeperResourceKind IHudHoverSources.Resource()
    {
        return _resources?.ResourceAt(_pointer) ?? KeeperResourceKind.None;
    }

    /// <summary>Release the heart observer and sound player when the level closes.</summary>
    public override void _ExitTree()
    {
        if (_heartHealthObserver != null && _simulation != null)
        {
            _simulation.World.HeartHealthChanged -= _heartHealthObserver;
        }

        _heartHealthObserver = null;
        _mixer?.Dispose();
        CloseBattleFlyIn();
        CloseWinScreen();
        CloseSubtitles();
        ClosePossession();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key)
        {
            return;
        }

        _keyboard.Feed(key);
        if (!_journal.Recording || key.Echo)
        {
            return;
        }

        var journalled = new HostKey((int)key.PhysicalKeycode);
        if (key.Pressed)
        {
            _journalDown.Add(journalled);
            if (!_journalHeld.Contains(journalled))
            {
                _journalHeld.Add(journalled);
            }
        }
        else
        {
            _journalUp.Add(journalled);
            _journalHeld.Remove(journalled);
        }
    }

    public override void _Notification(int what)
    {
        // A window without focus misses releases. Clear the keys, the hand, the panel press and the world drag.
        if (what == NotificationApplicationFocusOut)
        {
            _keyboard.ReleaseAll();
            _journalHeld.Clear();
            // A scripted pointer keeps its gestures: the owner's other windows must not change a capture.
            if (_pointerScript != null)
            {
                return;
            }
            // Reset the hand, release the panel's press, then drop the world drag.
            _handCursor?.Update(HandCursorKind.Default, false, (long)(_elapsed * 1000));
            PointerGestures.CancelAll(_panel, _input, _cursorFeedback);
            return;
        }
        // Godot stops reporting the pointer when it leaves the window.
        if (what == NotificationWMMouseExit && _pointerScript == null) { _pointerInWindow = false; return; }
        if (what == NotificationWMMouseEnter) { _pointerInWindow = true; return; }
        // Freed, not merely removed from the tree: a reparented scene keeps its level.
        if (what != NotificationPredelete)
        {
            return;
        }
        // Closing the journal writes the final frame if a session switch left it open.
        _journal.Close("destroyed", _pending, _pacer.Debt, _simulation);
        GD.Print("DK2.Godot world hover: the pick and tint allocated ", _pickBytes, " B in ", _pickFrames,
            " frames over the world after the first 60");
        if (_model != null)
        {
            _model.Selected -= SelectPanelChoice; _model.SpecialChosen -= ActivateStoredSpecial; _model.GuiButton -= PostGuiButton;
            _model.NavigateRoom -= NavigateRoom; _model.NavigateDoor -= NavigateDoor; _model.NavigateTrap -= NavigateTrap;
            _model.CollapsedChanged -= PanelCollapsed;
        }
        if (_battleTab != null)
        {
            _battleTab.Clicked -= BattleTabClicked;
        }

        if (_window != null && IsInstanceValid(_window))
        {
            _window.WindowInput -= ReadPress;
        }

        _window = null; _presses = null; _worldClicks = null; _pickups = null; _worldPointer = null; _frameDriver = null;
        _hoverCursor = null; _handCursor = null; _placement = null; _cursorFeedback.Reset();
        _cursorSheets?.Dispose(); _cursorSheets = null;
        _roomNavigation = null; _doorNavigation = null; _trapNavigation = null; _battleNavigation = null; _world = null;
        if (_saveLoad != null)
        {
            _saveLoad.Submit -= SaveLoadSubmitted; _saveLoad.HudAction -= ApplyHudAction;
        }
        if (_optionsPages != null) { _optionsPages.Applied -= ApplySettings; _optionsPages.SaveRequested -= SaveSettings; }
        _battleTab = null; _battles = null; _options = null; _saveLoad = null; _saves = null; _fullMap = null;
        _minimap = null; _worldView = null;
        if (_scriptedView != null)
        {
            _scriptedView.GuiVisibilityRequested -= _hudControls.SetGuiVisible; _scriptedView.ZoomRequested -= ScriptZoom;
        }
        _saver = null; _loader = null; _context = null; _profiles = null; _resultFlow = null; _scriptedView = null;
        _gameOptionsPanel = null; _gameOptions = null; _optionsPages = null;
        _art?.Dispose(); _sources?.Dispose();
        _hover = null; _hudHover = null; _overview = null; _overviewView = null; _typeText = null; _identityNames = null; _queryPanel = null; _queryView = null;
        _panel = null; _model = null; _view = null; _messages = null; _messageView = null; _resources = null; _resourceView = null; _art = null; _sources = null; _simulation = null;
    }
}
