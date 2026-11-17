// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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

/// <summary>Scene root of <c>InGameHud.tscn</c>: a campaign level started as Unity's <c>DungeonRunner</c> starts one
/// (<see cref="CampaignLevelData"/>, keeper 3, seed 1, fog on, its first turn run before the first frame), paced by
/// <see cref="TickPacer"/> at normal speed, with the in-game HUD over the level's terrain (<see cref="_world"/>). Ported
/// (plan step 1b): the resource strip, the bottom panel, the creature overview, the creature query details, the message tabs, the
/// battle tab, the hover HUD (<see cref="HudHoverText"/>), the options menu (<see cref="ClassicHudControls"/>, opened by
/// the options key; while it is open the level pauses and the other panels take no input, as Unity's <c>HudModal</c>) and
/// its save and load pages (<see cref="SaveLoadSubmitted"/>) and game options pages (<see cref="ClassicGameOptionsPanel"/>;
/// the settings are read from and saved to <c>user://game-settings.ini</c>) and the full map (<see cref="ClassicFullMap"/>, opened
/// by its key, closed by it, Escape or a left or right press; while it shows the normal HUD hides) and the minimap
/// (<see cref="ClassicMinimap"/>, from the live camera, whose moves are <see cref="UpdateCamera"/>'s; its grid is the full
/// map's too). Not wired (logic still in Unity's runner, or needing the 3D world): the list is in
/// docs/plans/2026-11-15-godot-migration.md. User arguments after <c>--</c> (<c>--stage-battle</c> is <see cref="StageBattle"/>): godot/README.md.</summary>
public partial class InGameHud : Node, IHudHoverSources, IHudActionHost, IHudPressHost, IWorldPointerHost
{
    private readonly List<PlayerCommand> _pending = new();
    private readonly PlayerInputTranslator _input = new();
    /// <summary>The tool, the panel's choice, the query tool and the selected corner, as Unity's runner keeps them.</summary>
    private readonly HudToolState _tools = new();
    /// <summary>The HUD actions, applied as Unity's runner applies them; this scene is its host.</summary>
    private HudActionRouter? _router;
    /// <summary>Where the press that raised the action being applied was, for the magnifier's zoom.</summary>
    private Vector2 _actionPointer;
    /// <summary>The pickups (the overview's; the Hand's world pickup is step 3h), as Unity's runner's, playing the cursor's
    /// held state.</summary>
    private PickupFlow? _pickups;
    /// <summary>The hand cursor's transient states (Unity's runner's <c>_cursorFeedback</c>), shared by the pickups, the world
    /// pointer and the hover; their sounds are dropped, as no sound plays here.</summary>
    private readonly HandCursorFeedback _cursorFeedback = new();
    /// <summary>The cursor, placement and cage the pointer shows (DungeonRunner.CaptureWorldHover after the pick).</summary>
    private HoverCursorResolver? _hoverCursor;
    /// <summary>The hand's icons the placement cursor draws while it holds things.</summary>
    private readonly HeldItemStack _heldStack = new();
    private ClassicHandCursor? _handCursor;
    private ClassicPlacementOverlay? _placement;
    private InstallHandCursorSheets? _cursorSheets;
    /// <summary>The world pick for the hover: entities join in step 3h, until then a pick is the tile under the pointer.</summary>
    private readonly WorldHoverPicker _picker = new();
    /// <summary>Where the hand last pointed on the map (Unity's runner's <c>_pointerWorld</c>), for the cursor's sounds.</summary>
    private FpPosition2? _pointerWorld;
    /// <summary>The Hand's excavation drag's far end; off the map it keeps the last tile the pointer was on.</summary>
    private TilePos? _dragTo;
    /// <summary>The <c>--pointer-path</c> check (audit open point 10): a scripted pointer in place of the OS one, so a capture
    /// never moves the owner's cursor. Its frames count this scene's frames from 0.</summary>
    private PointerScript? _pointerScript;
    private int _scriptFrame = -1;
    private PointerScriptSample _scripted;
    private Vector2 _scriptPushed = new(float.NaN, float.NaN);
    /// <summary>The HUD's presses in Unity's order (DungeonRunner.QueueHudInput); this scene is its host.</summary>
    private HudPressRouter? _presses;
    /// <summary>The query tool's and a held choice's world presses (DungeonRunner.QueuePanelWorldAction), on the tile
    /// <see cref="PointerTile"/> picks.</summary>
    private WorldClickCommands? _worldClicks;
    /// <summary>The window, whose input is read before any panel takes it.</summary>
    private Window? _window;
    /// <summary>The left and right buttons pressed since the last frame, read from the window before any panel marks the
    /// event handled, as Unity's <c>Input.GetMouseButtonDown</c> sees every press whatever the UI does with it.</summary>
    private bool _primaryDown, _secondaryDown, _primaryUp, _secondaryUp;
    /// <summary>The pointer and the HUD's visibility for this frame's presses (Unity's <c>Input.mousePosition</c> and
    /// <c>CurrentHud</c> while the runner queues input).</summary>
    private Vector2 _pressPointer;
    private HudVisibility _pressVisibility;
    private RoomNavigation? _roomNavigation;
    private ThingNavigation? _doorNavigation, _trapNavigation;
    private readonly List<RoomNavigationEntry> _roomEntries = new();
    private readonly List<ThingNavigationEntry> _thingEntries = new();
    private readonly TickPacer _pacer = new();
    /// <summary>The frame order around this scene's HUD and input, as Unity's runner runs it (plan step 2h).</summary>
    private FrameDriver? _frameDriver;
    /// <summary>The world pointer's gates (Unity's runner's QueueInput), on the tile <see cref="PointerTile"/> picks.</summary>
    private WorldPointerInput? _worldPointer;
    /// <summary>The input journal, off unless <c>--input-journal &lt;dir&gt;</c> names a folder (not Unity's variable); its
    /// keys are Godot physical keycodes (<see cref="FrameJournalSession.GodotKeyTable"/>).</summary>
    private readonly FrameJournalSession _journal = new(message => GD.Print(message), message => GD.PushWarning(message));
    private string? _journalRoot;
    private string _levelName = "";
    private ulong _seed;
    /// <summary>The journal's device sample since the last frame, kept only while it records.</summary>
    private readonly List<HostKey> _journalDown = new(), _journalUp = new(), _journalHeld = new();
    private FrameMouseButtons _journalButtonsDown, _journalButtonsUp;
    private float _journalWheelX, _journalWheelY;
    /// <summary>The applied settings, as Unity's runner keeps them: read here for the game speed, the key bindings and the
    /// HUD scale. Loaded from, and saved to, <see cref="_settingsPath"/>.</summary>
    private GameSettings _settings = new();
    private string _settingsPath = "";
    /// <summary>The settings pages' transactions (Unity's runner's <c>_optionsPages</c>).</summary>
    private OptionsPagesController? _optionsPages;
    private InGameOptionsController? _gameOptions;
    private ClassicGameOptionsPanel? _gameOptionsPanel;
    private readonly ClassicKeyboard _keyboard = new();
    /// <summary>The options menu's pages and the GUI state, as Unity's runner keeps them.</summary>
    private readonly ClassicHudControls _hudControls = new();
    private readonly PlayerId _viewer = PlayerIds.FirstKeeper;
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
    /// <summary>The battle records the battle tab reads (Unity's runner keeps it for the music; no music is played here).</summary>
    private MusicWorldObserver? _battles;
    private ClassicBattleTab? _battleTab;
    private ClassicOptionsPanel? _options;
    /// <summary>The save and load pages' list, as Unity's runner keeps it.</summary>
    private SaveSlotsView? _saves;
    /// <summary>The loaded save the next instance of this scene continues (<see cref="SaveLoadSubmitted"/>), as Unity's
    /// runner hands its pending resume to the runner that replaces it; taken and cleared by <see cref="_Ready"/>.</summary>
    private static GameSessionRequest? s_resume;
    /// <summary>The live campaign profile, loaded on the first save or load (Unity's runner's <c>_profiles</c>) from
    /// <see cref="_profilePath"/>.</summary>
    private CampaignProfileStore? _profiles;
    private string _profilePath = "";
    private SessionContext? _context;
    /// <summary>The level end (Unity's runner's level end): the carry, the statistics, the result and Space back to
    /// <c>Main.tscn</c>; it acts only for a level started from the campaign map.</summary>
    private CampaignResultFlow? _resultFlow;
    /// <summary>The level script's view requests (Unity's runner's <c>_scriptedView</c>): the camera path, the letterbox
    /// count and the flash, which hide the HUD, hold the options key and light the panel and the resources; Escape or Space
    /// skips a path (<see cref="FrameDriver"/>). The path's camera, letterbox, subtitles and speech are not wired (plan steps 3
    /// and 4).</summary>
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
    /// <summary>The full map, over the minimap's class grid, as Unity's runner shares it.</summary>
    private ClassicFullMap? _fullMap;
    private ClassicMinimap? _minimap;
    /// <summary>The world as the viewer's HUD sees it (Unity's runner's <c>_worldView</c>): the minimap's colours and markers.</summary>
    private WorldView? _worldView;
    /// <summary>The 3D world under the HUD (plan step 3a): the start frame's terrain from the level-start camera, which
    /// does not move yet.</summary>
    private WorldScene? _world;
    private Action<HeartHealthChange>? _heartHealthObserver;
    /// <summary>Action 26's focus glide (Unity's runner's <c>_glide</c>), on this scene's engine-time clock.</summary>
    private readonly CameraGlide _glide = new();
    /// <summary>The camera pointer last frame, y up (Unity's runner's <c>_cameraPointer</c>), and the wheel since then.</summary>
    private System.Numerics.Vector2 _cameraPointer;
    private float _cameraWheel;
    /// <summary>The <c>--glide-to x,y</c> check: a glide started one second into the scene, once no camera path plays.</summary>
    private TilePos? _glideTo;
    private BattleNavigation? _battleNavigation;
    /// <summary>Only its creature-type text is used here: world hover needs the 3D world.</summary>
    private WorldHoverText? _typeText;
    private KeeperResourceSnapshot _resourceFacts;
    /// <summary>The pointer in screen pixels from the top left, sampled once at the frame's start as Unity reads
    /// <c>Input.mousePosition</c> (an event a panel handles never reaches this node), or the scripted pointer's.</summary>
    private Vector2 _pointer;
    private bool _pointerInWindow = true;
    private double _elapsed;

    public override void _Ready()
    {
        if (_simulation != null) return;   // re-entering the tree keeps the running level
        RenderingServer.SetDefaultClearColor(Colors.Black);
        string? install = Dk2Install.Find();
        if (install is null)
        {
            GD.PushError("DK2.Godot: no Dungeon Keeper 2 install found; set DK2_DIR.");
            return;
        }
        string[] args = OS.GetCmdlineUserArgs();
        LaunchArguments launch = LaunchArguments.Parse(args, "--");
        // A save the previous instance loaded: its level, return page and restored simulation replace the arguments'.
        GameSessionRequest? resume = s_resume;
        s_resume = null;
        string levelName = resume?.LevelName ?? launch.Level ?? "Level1";
        ulong seed = launch.Seed ?? 1;
        GameFolders folders = UserFolders.FromArguments(args);
        _levelName = levelName;
        _seed = seed;
        // Only the argument: DK2_INPUT_JOURNAL is Unity's switch, and a global one must not make every Godot run write a session.
        _journalRoot = LaunchArguments.Value(args, "--input-journal");
        CampaignLevelData level = CampaignLevelData.Load(install, levelName);
        _simulation = resume?.Resume?.Simulation ?? level.CreateSimulation(seed, fogOfWarEnabled: true);
        _simulation.World.LocalPlayerId = _viewer;
        // DungeonRunner's session: a direct start returns to no page, as Unity's runner started from its scene does;
        // --from-map starts it as the campaign map launches a level (GameSessionRequest.Campaign).
        FrontEndPage returnPage = Array.IndexOf(args, "--from-map") >= 0
            ? GameSessionRequest.Campaign(levelName).ReturnPage : FrontEndPage.None;
        _context = new SessionContext(levelName, GameSessionMode.Campaign, resume?.ReturnPage ?? returnPage);
        _profilePath = folders.Profile;
        _profiles = new CampaignProfileStore(InstalledCampaignRegistry.ReadCurrentUser, () => DateTime.Now);
        _saver = new SessionSaver(_profiles, LoadProfile, () => DateTime.UtcNow);
        _loader = new SessionLoader(_profiles, LoadProfile, name => CampaignLevelData.Load(install, name));
        // DungeonRunner.PrepareCampaignResult, before the first turn: a level started from the campaign map plays its slot;
        // a restored save counts its carry as written, a fresh start brings the stored transfer creature in.
        _resultFlow = new CampaignResultFlow(_context, _profiles, LoadProfile, _hudControls)
        {
            LevelTicksPerSecond = level.Level.TicksPerSecond,
        };
        _resultFlow.Prepare(_simulation, _viewer, resumed: resume != null);
        // DungeonRunner.InitializeSettings: game-settings.ini beside the campaign profile.
        _settingsPath = folders.Settings;
        _settings = LoadSettings(_settingsPath);
        _optionsPages = new OptionsPagesController(_settings);
        _optionsPages.Applied += ApplySettings;
        _optionsPages.SaveRequested += SaveSettings;

        // First, so the tree reads world then HUD; the HUD's canvas draws over the 3D world whatever the order.
        _world = new WorldScene { Name = "World" };
        AddChild(_world);
        _sources = new InstallHudSources(install);
        _art = new InstallBottomPanelArt(_sources, level.Database);
        // Added first so it draws under the panel, as Unity's sorting orders 11 and 12.
        _resourceView = new KeeperResourceView(level.Database, _viewer);
        _resources = new ClassicResourceHud { Name = "ResourceHud" };
        _resources.Open(_sources);
        AddChild(_resources);
        _view = new BottomPanelView(level.Database, _viewer, _art.Name, _art.Order, campaignMode: true);
        // DungeonRunner.StartGame's room check; the tool starts as the Inspector's default, the hand.
        _tools.SelectKnownRoom(level.Database.Rooms.Keys);
        _router = new HudActionRouter(_hudControls, _tools, _input, this);
        _panel = new ClassicBottomPanel { Name = "BottomPanel" };
        _panel.Open(_art, _view);
        AddChild(_panel);
        _panel.OpenAtLevelStart();
        _model = _panel.Model;
        _model.Selected += SelectPanelChoice;
        _model.SpecialChosen += ActivateStoredSpecial;
        _model.GuiButton += PostGuiButton;
        // The panel's right clicks walk the rooms, doors and traps as Unity's runner does, each a camera glide.
        _roomNavigation = new RoomNavigation(_viewer);
        _doorNavigation = new ThingNavigation(_viewer);
        _trapNavigation = new ThingNavigation(_viewer);
        _model.NavigateRoom += NavigateRoom;
        _model.NavigateDoor += NavigateDoor;
        _model.NavigateTrap += NavigateTrap;
        _model.CollapsedChanged += PanelCollapsed;
        _battleNavigation = new BattleNavigation(_viewer);
        // Added after the panel and before the tabs, so it draws between them, as Unity's sorting order 13 between 12 and 14.
        var overviewArt = new InstallCreatureOverviewArt(install, level.Database, _art.Image, _art.Text);
        _overviewView = new CreatureOverview(level.Database, _viewer, overviewArt.Name, overviewArt.Order);
        _overview = new ClassicCreatureOverview { Name = "CreatureOverview" };
        // Its presses are the router's (QueueInput): a pickup is posted, a focus moves the camera.
        _overview.Open(overviewArt, _overviewView);
        AddChild(_overview);
        // Unity gives it the overview's sorting order 13 and creates it after the overview; added after it here.
        _queryView = new CreatureQueryView(level.Database, _sources.Strings.Resolve, _viewer, new InstallClassicCreatureNames(install));
        _queryPanel = new ClassicCreatureQueryPanel { Name = "CreatureQuery" };
        _queryPanel.Open(_art.Font, _sources.Texture);
        AddChild(_queryPanel);
        // Unity's sorting order 14, over the bottom panel (12) and the creature panels (13), as DK2 draws it over the tab
        // strip's rail; created before the tabs, as DungeonRunner creates it first.
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
        _minimap.Open(new InstallMinimapAssets(install, _sources.Texture), _simulation.World.Width, _simulation.World.Height);
        AddChild(_minimap);
        // Added after the panel, so it draws over it and takes the mouse first, as Unity's sorting order 14 over 12.
        _messageView = new MessageTabsView(_viewer, level.Database);
        var messageText = new MessageTextTables(_sources, level.Database.Level?.TextTableId ?? 0, warning => GD.PushWarning(warning));
        InstallHudSources sources = _sources;
        GameSimulation simulation = _simulation;
        MessageTabsView messageView = _messageView;
        _messages = new ClassicMessageTabs { Name = "MessageTabs" };
        // 0x00415B30 submits game action 4 with the point's tile: the focus glides to that tile's centre, as Unity's runner
        // wires it (DK2 first drops an off-map point; ours clamps in the glide).
        _messages.Open(_art, _art.Font, _messageView,
            messageText.Resolve, messageText.Briefing, message => messageView.LocationOncePerTick(simulation.World, message),
            (x, y) => StartGlide(new TilePos((int)Math.Floor(x), (int)Math.Floor(y))), PlayerNames.From(level.Players));
        AddChild(_messages);
        // Created after the tabs, as DungeonRunner creates it, with their sorting order 14 over the panel's 12.
        _battles = new MusicWorldObserver(_viewer, new MusicDirector());
        _battleTab = new ClassicBattleTab { Name = "BattleTab" };
        _battleTab.Open(_art.Image, _art.Text);
        _battleTab.Clicked += BattleTabClicked;
        AddChild(_battleTab);
        // After every panel it covers, so it draws over them and takes the mouse first, as Unity's sorting order 15.
        _options = new ClassicOptionsPanel { Name = "Options" };
        _options.Open(_sources.Text, _art.Font);
        _options.SetSupported(ClassicOptionsAction.Save, true);
        _options.SetSupported(ClassicOptionsAction.Load, true);
        _options.SetSupported(ClassicOptionsAction.GameOptions, true);
        // Its press swallow reads the router's own live state (ClassicHudControls), never the drawn menu's.
        _options.HoldsPresses = () => _hudControls.OptionsVisible;
        AddChild(_options);
        // Created after the menu, as DungeonRunner creates it; the two never show together.
        _gameOptions = new InGameOptionsController(_hudControls, _optionsPages);
        _gameOptionsPanel = new ClassicGameOptionsPanel { Name = "GameOptions" };
        _gameOptionsPanel.Open(_gameOptions, _sources.Text, _art.Font, _sources.Texture);
        AddChild(_gameOptionsPanel);
        // Listing only reads: nothing is created there.
        string savesDirectory = folders.Saves;
        _saves = new SaveSlotsView(savesDirectory);
        // After the menu, as Unity's sorting order 16 over its 15.
        _saveLoad = new ClassicSaveLoadPanel { Name = "SaveLoad" };
        _saveLoad.Open(_sources.Text, _art.Font);
        _saveLoad.Submit += SaveLoadSubmitted;
        _saveLoad.HudAction += ApplyHudAction;
        AddChild(_saveLoad);
        // Its bar's canvas draws under the panels and its text's over them by z index (ClassicHoverHud), not tree order.
        _hover = new ClassicHoverHud { Name = "HoverHud" };
        _hover.Open(_sources);
        AddChild(_hover);
        // Last, so it draws over every panel and takes the mouse first, as Unity's sorting order 20 over the rest. Its grid
        // is the minimap's.
        _fullMap = new ClassicFullMap { Name = "FullMap" };
        _fullMap.Open(_minimap.Model!, _art.Font, PlayerNames.From(level.Players), _sources.Texture);
        _fullMap.HoldsPresses = () => _hudControls.FullMapVisible;
        AddChild(_fullMap);
        // On their own canvas layers over every panel: the placement cursor, then the hand over it (Unity's sorting orders
        // 999 and 1000). The cursor's spell icon is the panel's own resource (0x0040D310 reads the same art); the held icons
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
        // The played campaign slot only world hover reads, passed as Unity's runner passes it.
        _typeText = new WorldHoverText(level.Database, _sources.Strings.Resolve, _viewer, CampaignSlot.ForLevel(levelName));
        // DungeonRunner applies the loaded settings once the level is up.
        ApplySettings(_settings);
        // DungeonRunner.StartGame's scripted view, observing the simulation before its first turn.
        _scriptedView = new ScriptedViewDirector(_viewer, EngineCameraPaths.Loader(install),
            LevelSubtitles.Open(install, level.Database.Level?.TextTableId ?? 0, warning => GD.PushWarning(warning)).Line);
        _scriptedView.GuiVisibilityRequested += _hudControls.SetGuiVisible;
        _scriptedView.ZoomRequested += ScriptZoom;
        _simulation.ObserveWith(_scriptedView);
        _frameDriver = new FrameDriver(_pending, _worldView, _pacer, _journal);
        // A restored checkpoint already includes its starting turn (DungeonRunner.StartGame).
        if (resume == null)
            for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++) _simulation.Tick(InputFrame.Empty);
        // The first frame's terrain, built whole; each frame's Present pushes the changes since.
        _world.Open(install, level, _simulation.World, _worldView, _viewer, startFocus, _settings.ScrollSpeed);
        // DungeonRunner's heart health observer: each change queued for the next frame's beat.
        int maximumHeartHealth = level.Database[VariableTypeId.DungeonHeartObjectHealth];
        WorldScene world = _world;
        _heartHealthObserver = change => world.QueueHeartHealthChange(change.Owner,
            DungeonHeartVisual.FromHealth(currentHealth: change.Health, maximumHealth: maximumHeartHealth));
        _simulation.World.HeartHealthChanged += _heartHealthObserver;
        _glideTo = LaunchArguments.GlideTarget(args, "--");
        _pointerScript = LaunchArguments.PointerPath(args, "--");
        if (_pointerScript == null && LaunchArguments.Value(args, "--pointer-path") != null)
            GD.PushWarning("DK2.Godot: --pointer-path is malformed (LaunchArguments.ParsePointerPath); the OS pointer is used.");
        // A scripted pointer leaves the owner's pointer as it is, and the OS mouse stops at one gate added last.
        if (_pointerScript != null)
        {
            _handCursor.ManageSystemPointer = false;
            AddChild(new ScriptedPointerGate { Name = "ScriptedPointerGate" });
        }
        if (Array.IndexOf(args, "--query-first-creature") >= 0)
            foreach (CreatureState creature in _simulation.World.OwnedCreatures(_viewer))
            {
                _queryView.Query(new CreatureQueryTarget(creature.Id));
                break;
            }
        if (Array.IndexOf(args, "--stage-battle") >= 0) StageBattle(_simulation);
        // The check arguments --stage-win and --stage-loss: the viewer's verdict, set after the first turn.
        byte stageVerdict = Array.IndexOf(args, "--stage-win") >= 0 ? PlayerStatus.Won
            : Array.IndexOf(args, "--stage-loss") >= 0 ? PlayerStatus.Lost : (byte)0;
        if (stageVerdict != 0 && !VerdictRules.End(_simulation.World, _simulation.Database, _viewer, stageVerdict))
            GD.PushWarning("DK2.Godot: --stage-win or --stage-loss found the viewer's game already ended.");
        _leaveResult = Array.IndexOf(args, "--leave-result") >= 0;
        _escapeResult = Array.IndexOf(args, "--escape-result") >= 0;
        if (Array.IndexOf(args, "--open-full-map") >= 0) ApplyHudAction(ClassicHudAction.ToggleFullMap);
        if (Array.IndexOf(args, "--open-options") >= 0)
        {
            ApplyHudAction(ClassicHudAction.ToggleOptions);
            // Only an entry the menu takes a press on: an unsupported one is ignored, as a click.
            if (Enum.TryParse(LaunchArguments.Value(args, "--open-options"), out ClassicOptionsAction entry))
            {
                if (_options.Supported(entry)) _presses.PressOptions(entry);
                else GD.PushWarning("DK2.Godot: --open-options ignores " + entry + ", which the menu does not support.");
            }
        }
        if (resume == null)
        {
            _saveAs = LaunchArguments.Value(args, "--save-as");
            _loadName = LaunchArguments.Value(args, "--load");
            if (long.TryParse(LaunchArguments.Value(args, "--save-tick"), NumberStyles.None, CultureInfo.InvariantCulture, out long saveTick))
                _saveTick = new GameTick(saveTick);
        }
        if (long.TryParse(LaunchArguments.Value(args, "--hash-at"), NumberStyles.None, CultureInfo.InvariantCulture, out long hashTick))
            _hashTick = new GameTick(hashTick);
        GD.Print("DK2.Godot in-game HUD: ", levelName, " seed ", seed.ToString(CultureInfo.InvariantCulture), ", saves in ", savesDirectory,
            ", settings in ", _settingsPath);
        if (resume != null) PrintHash("resumed " + resume.Resume!.Session.Header.DisplayName + " at");
    }

    public override void _Process(double delta)
    {
        if (_simulation == null || _view == null || _panel == null || _model == null || _resourceView == null
            || _resources == null || _messageView == null || _messages == null || _hover == null || _hudHover == null
            || _overviewView == null || _overview == null || _queryView == null || _queryPanel == null
            || _battles == null || _battleTab == null || _options == null || _saveLoad == null || _optionsPages == null
            || _gameOptionsPanel == null || _fullMap == null || _minimap == null || _worldView == null || _scriptedView == null
            || _resultFlow == null || _frameDriver == null || _world == null || _hoverCursor == null || _handCursor == null
            || _placement == null || _leaving) return;
        SamplePointer();
        JournalBeginFrame(delta);
        // Engine time, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        if (_loadName != null || (_saveAs != null && _simulation.World.CurrentTick >= _saveTick)) RunSaveLoadCheck();
        // Once per settled turn, before the HUD draws, as DungeonRunner.UpdateWorldSound captures it.
        _battles.Capture(_simulation.World, _simulation.Database, _simulation.World.CurrentTick);
        // Once per settled turn, as DungeonRunner captures it before the HUD draws; the full map reads the same grid.
        bool minimapCaptured = _minimap.Capture(_simulation.World, _worldView, _simulation.Navigation);
        long milliseconds = (long)(_elapsed * 1000);
        // DungeonRunner.Update's script step (FrameDriver): the scripted view on the drawn turn plus the debt toward the next
        // (this scene ticks inline, so none is pending), then the level end, which may leave for the front end.
        _frameDriver.UpdateScript(_scriptedView, milliseconds, _simulation.World, _viewer, ticksPending: 0);
        ReportPath();
        bool checkKeys = _resultFlow.ResultRecorded && !_scriptedView.PathPlaying;
        bool escapeCheck = _escapeResult && checkKeys;
        var result = new CampaignResultFrame(_viewer, _scriptedView.PathPlaying, milliseconds, SpeechPlaying: null,
            _keyboard.SpacePressed || (_leaveResult && checkKeys && !escapeCheck), _keyboard.EscapePressed || escapeCheck);
        if (_resultFlow.Update(_simulation.World, _simulation.Database, result, _pending))
        {
            LeaveForFrontEnd();
            return;
        }
        if (escapeCheck) ReportEscape();
        // DungeonRunner.Update's input step (FrameDriver): the local pickups, then Escape or Space ends a playing camera path
        // and posts SkipCameraPathCommand (0x00405362); the script's glide starts.
        _frameDriver.AdvancePickups(_simulation.World, _viewer, milliseconds);
        FrameScriptInput script = _frameDriver.TakeScriptedInput(_scriptedView, _viewer, _keyboard.EscapePressed || _keyboard.SpacePressed);
        if (script.Glide is TilePos glide) StartGlide(glide);
        // The HUD keys, as DungeonRunner.UpdateHudControls reads them. Only the options menu's and the full map's toggles
        // are applied: the other keys' GUI toggle and camera moves are not ported. A camera path holds the options key
        // back that frame. While the keyboard page captures a key, no shortcut fires.
        HudVisibility camera = _scriptedView.HudFor(ClassicCameraMode.Dungeon);
        ClassicHudAction shortcut = _keyboard.Read(_settings).HudShortcut(
            _hudControls.ShortcutContext(camera, script.AllowOptionsKey, capturingKey: _optionsPages.CapturingAction != null),
            _keyboard.EscapePressed);
        if (shortcut is ClassicHudAction.ToggleOptions or ClassicHudAction.ToggleFullMap) ApplyHudAction(shortcut);
        // DungeonRunner.CurrentHud (PlayedHud; the warm frames are not ported): a camera path, the letterbox, the result
        // and the full map hide the normal HUD.
        HudVisibility visibility = _scriptedView.PlayedHud(_hudControls, ClassicCameraMode.Dungeon);
        bool modal = _hudControls.Modal;
        _panel.SetVisibility(_hudControls.ModalInput(visibility));
        _options.SetVisible(_hudControls.OptionsMenuShown(visibility));
        _gameOptionsPanel.Show(_hudControls.GameOptionsShown(visibility));
        _fullMap.SetVisible(_hudControls.FullMapShown(camera));
        _minimap.SetVisibility(visibility);
        _options.SetPage(_hudControls.OptionsPage, _hudControls.Confirmation);
        // DungeonRunner.DrawSavePanel's in-game branch, with this frame's typing (Unity reads Input.inputString there).
        (bool saveLoadShown, bool saving) = _hudControls.SaveLoadPage(visibility);
        _saveLoad.Show(_saves, saveLoadShown, saving, _keyboard.Typed);
        // DungeonRunner.Update's camera step, then QueueInput after the shortcuts, as there; its game options branch polls
        // the keys before they end.
        UpdateCamera(visibility, delta);
        // The frame's one pose, which this frame's picks read; the follow below places it again only if it moved the focus.
        _world.PlaceCamera();
        QueueInput(visibility, milliseconds);
        // The query follow (DungeonRunner.FollowQueriedCreature, after the ticks there) runs on the settled world here, so the
        // view and the minimap below draw the same focus; then the camera is placed once.
        System.Numerics.Vector3 focus = _world.Focus;
        FollowQueriedCreature();
        if (_world.Focus != focus) _world.PlaceCamera();
        // DungeonRunner.Update before its present: the Hand's excavation drag previews its tags; off the map the far end keeps
        // the last tile the pointer was on.
        TilePos? dragFrom = _input.DragFrom(PlayerToolMode.Hand);
        _dragTo = dragFrom == null ? null : PointerTile(_pointer, visibility) ?? _dragTo ?? dragFrom;
        _worldView.PreviewTags(dragFrom, _dragTo, _input.ExcavationPreviewTagged ?? true);
        // DungeonRunner.Update's present: the settled world's changes, before the frame's ticks; the chunk build it
        // starts runs on the thread pool while they do (ADR-0696).
        // DungeonRunner's frame time before its present, then DrawCreatureFrame's objects after it: the drawn turn and how
        // far toward the next (this scene ticks inline, so the debt left by the last frame).
        float drawFraction = (float)Math.Min(_pacer.Debt, 1d);
        _world.SetFrameTime(_simulation.World.CurrentTick, drawFraction, milliseconds);
        _world.Present(_simulation.World, _elapsed);
        _world.DrawFrame(_simulation.World.CurrentTick, drawFraction);
        _keyboard.EndFrame();
        // The HUD draws the settled world, then the frame's ticks run (DungeonRunner's order).
        // As DungeonRunner.DrawHudControls captures it and CaptureWorldHover shows and draws it.
        _resourceFacts = _resourceView.Capture(_simulation.World, displayHealth: visibility.ShowNormalHud);
        // FLASH_BUTTON, dropped once its item is in use, as DungeonRunner.DrawHudControls resolves it before the resources
        // draw their flash.
        _model.FlashTarget = _scriptedView.PanelFlash(_tools, _model.OpenPanel);
        _resources.SetVisibility(visibility);
        if (visibility.ShowNormalHud) _resources.Draw(_resourceFacts, unchecked((uint)milliseconds), _scriptedView.ResourceFlash);
        _view.Capture(_simulation.World);
        _model.GamePaused = _hudControls.TicksPaused;
        _panel.Draw(milliseconds);
        _messageView.Capture(_simulation.World, milliseconds, visibility);
        // No sound player yet: drop the requests so they do not pile up.
        _model.Sounds.Clear(); _view.Sounds.Clear(); _messageView.Sounds.Clear(); _messageView.Speech.Clear();
        _hudControls.Sounds.Clear(); _optionsPages.Sounds.Clear();
        // As the HUD shows; taking input unless the options menu holds it.
        HudVisibility tabs = _hudControls.ModalInput(visibility);
        _messages.Draw(tabs, _model.Collapsed,
            HudIndicatorSnapshot.Capture(_simulation.World, _simulation.Database.Level, _viewer),
            _hudControls.OptionsVisible, _hudControls.ObjectiveVisible);
        // After the tabs, as DungeonRunner draws it. The fly-in it starts (BattleFlyIn, the reward tablet) is not ported.
        _battleTab.Draw(tabs, _model.Collapsed,
            BattleTabView.Capture(_simulation.World, _simulation.Database, _viewer, _battles.Battles), milliseconds);
        // After the battle tab, as DungeonRunner draws it; the options menu closes it.
        _overviewView.Capture(_simulation.World);
        _overview.Draw(visibility, modal ? null : _model.PanelArea(BottomPanelCategory.Creatures));
        // After the overview, as DungeonRunner.DrawHudControls updates it, with the selected corner lit.
        MinimapCameraView? drawn = _world.View;
        _minimap.Draw(_tools.SelectedCorner, _hudControls.OptionsVisible, MinimapHud.FixedFocus(_world.Focus),
            drawn?.Heading ?? ClassicCamera.LevelStartYaw, drawn);
        // After the minimap, as DungeonRunner.DrawHudControls draws it.
        _options.Draw();
        // DungeonRunner.DrawHudControls captures it after the bottom panel and draws it after the menu.
        _fullMap.Capture(_simulation.World, _simulation.Database, _viewer, minimapCaptured);
        _fullMap.Draw();
        // DungeonRunner.DrawHudControls draws it after the save browser; the two never show together.
        _gameOptionsPanel.Draw();
        // Last, as DungeonRunner.DrawHudControls captures and draws it; the options menu hides it.
        _queryPanel.Draw(_queryView.Capture(_simulation.World), visibility, modal);
        // The hover, after the HUD is drawn (DungeonRunner.CaptureWorldHover): over the HUD, the panels' text; over the world
        // the pick, then the cursor, placement and cage (HoverCursorResolver). The world's hover text, the Hand's tint and
        // light, the hovered creature's sound and flower stamp come with entity hover (step 3h) and lighting (3i).
        _hover.SetVisibility(visibility, milliseconds);
        bool inside = _pointerInWindow && GetViewport().GetVisibleRect().HasPoint(_pointer);
        bool allowed = visibility.AllowOrdinaryInput && Focused && inside;
        bool overHud = PointerOverHud(_pointer, visibility);
        WorldHoverHit? pick = null;
        if (allowed && !overHud && PointerTile(_pointer, visibility) is TilePos tile)
        {
            _pointerWorld = PointerPosition(_pointer, tile)
                ?? new FpPosition2(new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)));
            pick = ResolveWorldHover(_pointer, tile);
        }
        HoverTextSnapshot hover = allowed && overHud ? _hudHover.Capture(this, _resourceFacts) : HoverTextSnapshot.Empty;
        HoverFeedback feedback = _hoverCursor.Resolve(_simulation.World, _simulation.Database, _simulation.Navigation,
            new HoverCursorInput(pick, allowed, overHud, PrimaryHeld, _resourceFacts.Gold.Value, milliseconds, _pointerWorld));
        // No sound player yet: the hover's and the cursor's requests are dropped.
        _cursorFeedback.Sounds.Clear();
        _handCursor.Update(feedback.Cursor, allowed, milliseconds);
        _handCursor.Draw(_pointer);
        _hover.Draw(hover, _pointer, feedback.TooltipReach, milliseconds);
        PlacementFeedback placement = feedback.Placement;
        if (allowed && placement.Icon == PlacementIcon.Held) _heldStack.Refresh(_simulation.World, _viewer, _worldView.LocalPickups);
        _placement.Draw(allowed ? placement : PlacementFeedback.None, _pointer, overHud, _heldStack, feedback.Cursor);
        _world.DrawCage(placement.Cage, placement.Valid, milliseconds);

        // DungeonRunner.Update's tick step (FrameDriver): ready drops after the input, then the tick gate (no turn runs, and
        // none accrues, while it holds), the hand-off and the clear. The frame is clamped as Unity's runner clamps it
        // (Time.maximumDeltaTime, 1/3 s), so a stall is not paid back at the cap. No possession here, so no pose.
        FrameTicks ticks = _frameDriver.TakeTicks(_simulation.World, _viewer, Math.Min(delta, TickPacer.MaximumFrameSeconds), 1f,
            _settings.GameSpeed, _hudControls.TicksPaused, TickPacer.DefaultMaxTicksPerFrame, poses: null);
        _journal.Ticks(_simulation.World.CurrentTick, ticks.Count);
        for (int i = 0; i < ticks.Count; i++)
        {
            InputFrame input = ticks.InputFor(i);
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
            if (_simulation.World.CurrentTick == _hashTick) PrintHash("hash-at");
        }
        _frameDriver.AfterTicks(_simulation.World, milliseconds);
        // DungeonRunner.Present's seek: the path is placed on the turn now drawn, without taking requests.
        _scriptedView.SeekCamera(_simulation.World.CurrentTick.Value + Math.Min(_pacer.Debt, 1d));
        // Unity's LateUpdate: the frame's journal record.
        _journal.EndFrame(_pending, _pacer.Debt);
    }

    /// <summary>Unity's runner's JournalBeginFrame: the journal opens on the first frame, and while it records, each frame's
    /// raw sample (Godot's top-left pointer, its buttons, wheel and physical keys since the last frame) starts the record.</summary>
    private void JournalBeginFrame(double delta)
    {
        if (!_journal.Resolved)
            _journal.Open(_journalRoot, FrameJournalSession.FolderName(DateTime.UtcNow, GameSessionMode.Campaign, _levelName), JournalHeader);
        if (!_journal.Recording || _scriptedView == null) return;
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
        if (_simulation == null) return;
        var posted = new System.Text.StringBuilder();
        foreach (PlayerCommand command in _pending) posted.Append(' ').Append(command.GetType().Name);
        GD.Print("DK2.Godot: Escape on the result at tick ", _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture),
            " posted", posted.ToString(), "; status ", _simulation.World.Player(_viewer).Status.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Prints the viewer's status changes and a camera path's start and end, the moments the HUD hides and comes
    /// back.</summary>
    private void ReportPath()
    {
        if (_scriptedView == null || _simulation == null) return;
        byte status = _simulation.World.Player(_viewer).Status;
        if (status != _status)
        {
            _status = status;
            GD.Print("DK2.Godot: the viewer's status is ", status.ToString(CultureInfo.InvariantCulture), " at tick ",
                _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture), "; the HUD's input is ",
                _hudControls.InputSuppressed ? "suppressed" : "open");
        }
        if (_scriptedView.PathPlaying == _pathPlaying) return;
        _pathPlaying = _scriptedView.PathPlaying;
        GD.Print("DK2.Godot: camera path ", _pathPlaying ? "started" : "ended", " at tick ",
            _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture), "; the HUD ", _pathPlaying ? "hides" : "shows");
    }

    /// <summary>DungeonRunner.UpdateCampaignResult's Space: the session ends and the front end opens at the session's return
    /// page (<c>Main.tscn</c>, as Unity's runner loads <c>GameSessionRequest.FrontEnd(returnPage)</c>), deferred to the
    /// frame's end as a load's reload is. The won map's title card and cutscene that Unity plays first need media (plan
    /// step 4).</summary>
    private void LeaveForFrontEnd()
    {
        _leaving = true;
        _keyboard.EndFrame();
        Main.s_returnPage = _context?.ReturnPage ?? FrontEndPage.None;
        GD.Print("DK2.Godot: the level ended; leaving for the front end at page ", Main.s_returnPage.ToString(), ".");
        Callable.From(ChangeToFrontEnd).CallDeferred();
    }

    private void ChangeToFrontEnd()
    {
        Error error = GetTree().ChangeSceneToFile("res://Main.tscn");
        if (error == Error.Ok) return;
        Main.s_returnPage = FrontEndPage.None;
        _leaving = false;
        GD.PushWarning("DK2.Godot: the front end could not be opened (", error.ToString(), ").");
    }

    private void PrintHash(string what)
    {
        if (_simulation == null) return;
        GD.Print("DK2.Godot: ", what, " tick ", _simulation.World.CurrentTick.Value.ToString(CultureInfo.InvariantCulture),
            ", state hash ", _simulation.StateHash().ToString("X16", CultureInfo.InvariantCulture));
    }

    /// <summary>The <c>--save-as</c> and <c>--load</c> check arguments, through the options menu's own pages: open the
    /// menu, press Save or Load, name or select the save, submit as a press on the page does, then close the menu so the
    /// level runs on. Each acts once; a resumed instance ignores both.</summary>
    private void RunSaveLoadCheck()
    {
        if (_presses == null || _saves == null) return;
        bool saving = _loadName == null;
        string name = (saving ? _saveAs : _loadName)!;
        if (saving) _saveAs = null; else _loadName = null;
        if (!_hudControls.OptionsVisible) ApplyHudAction(ClassicHudAction.ToggleOptions);
        _presses.PressOptions(saving ? ClassicOptionsAction.Save : ClassicOptionsAction.Load);
        if (saving)
        {
            _saves.SetName(name);
            PrintHash("saving " + name + " at");
        }
        else
        {
            int index = -1;
            for (int i = 0; i < _saves.Entries.Count; i++) if (_saves.Entries[i].DisplayName == name) index = i;
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
            if (creature.Name == "Knight") { knight = creature; break; }
        if (knight is null) return;
        foreach (CreatureState creature in world.OwnedCreatures(_viewer))
        {
            if (world.TrySpawnCreature(simulation.Database, new CreatureTypeId(knight.CreatureId), heroes, creature.Tile) is null)
                GD.PushWarning("DK2.Godot: --stage-battle found the creature pool full.");
            return;
        }
    }

    /// <summary>Menu 43's presses, as DungeonRunner.BattleTabClicked applies them (<see cref="BattleTabView.Click"/>, with its
    /// suppressed-input and modal gate): the cancel's left click posts; the zoom goes to the router (the fight navigation's
    /// glide), and the cancel's and the alarm's glides start here.</summary>
    private void BattleTabClicked(BattleTabButton button, int mouse)
    {
        if (_simulation == null || _trapNavigation == null) return;
        BattleTabClick click = BattleTabView.Click(button, mouse, _hudControls, _simulation.World, _simulation.Database, _viewer,
            _trapNavigation, _thingEntries);
        if (click.HudAction != ClassicHudAction.None) ApplyHudAction(click.HudAction);
        if (click.Command != null) _pending.Add(click.Command);
        if (click.Glide is TilePos tile) StartGlide(tile);
    }

    /// <summary>DungeonRunner.NavigateRoom: the next of the viewer's rooms of a type (<see cref="RoomNavigation.Entries"/>).</summary>
    private void NavigateRoom(RoomTypeId type)
    {
        if (_simulation == null || _roomNavigation == null) return;
        RoomNavigation.Entries(_simulation.World, _simulation.Database, _roomEntries);
        if (_roomNavigation.Next(_roomEntries, type) is TilePos tile) StartGlide(tile);
    }

    /// <summary>DungeonRunner.NavigateDoor: the next of the viewer's doors of a kind, newest first.</summary>
    private void NavigateDoor(byte kind)
    {
        if (_simulation == null || _doorNavigation == null) return;
        ThingNavigation.DoorEntries(_simulation.World.Doors, _thingEntries);
        if (_doorNavigation.Next(_thingEntries, kind) is TilePos tile) StartGlide(tile);
    }

    /// <summary>DungeonRunner.NavigateTrap: the next of the viewer's traps of a kind, newest first.</summary>
    private void NavigateTrap(byte kind)
    {
        if (_simulation == null || _trapNavigation == null) return;
        ThingNavigation.TrapEntries(_simulation.World.Traps, _thingEntries);
        if (_trapNavigation.Next(_thingEntries, kind) is TilePos tile) StartGlide(tile);
    }

    /// <summary>Action 26 (game action 4, 0x00442650), Unity's runner's <c>StartGlide</c>: the focus glides to the tile's
    /// centre, kept inside the map, angles unchanged (<see cref="CameraGlide"/>).</summary>
    private void StartGlide(TilePos tile)
    {
        if (_world == null || _simulation == null) return;
        _glide.Start(_world.Focus, tile, _simulation.World.Width, _simulation.World.Height, _elapsed * 1000d);
    }

    /// <summary>DungeonRunner.Update's camera step: during a camera path only the pointer is taken (the path's camera is
    /// step 3n); otherwise the glide, the script's follow (game action 17), then <see cref="ClassicCameraInput.Apply"/>
    /// while ordinary input is allowed and no menu is modal, else only the pointer (DungeonRunner.ControlCamera).</summary>
    private void UpdateCamera(HudVisibility visibility, double delta)
    {
        ClassicCameraControls? controls = _world?.Controls;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        Vector2 top = _pointer;
        // Unity's Input.mousePosition is y up; off the window the edge scroll never acts.
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
        if (gliding) _world.Focus = _glide.Advance(_world.Focus, _elapsed * 1000d);
        if (gliding && !_glide.Active)
            GD.Print("DK2.Godot: the glide ended at focus (", _world.Focus.X, ", ", _world.Focus.Z, ")");
        if (_scriptedView.FollowedCreatureId != 0 && !_glide.Active
            && _simulation.World.CreatureById(_scriptedView.FollowedCreatureId) is CreatureState followed)
            _world.Focus = CameraFocus.Over(new Fp32(followed.PosX), new Fp32(followed.PosY), _world.Focus.Y);
        if (visibility.AllowOrdinaryInput && !_hudControls.Modal && width > 0 && height > 0)
        {
            var sample = new CameraPointerSample(pointer, width, height, Focused, PointerOverHud(top, visibility), wheel);
            _world.Focus = ClassicCameraInput.Apply(controls, _world.Focus, _keyboard.Read(_settings), sample, _cameraPointer,
                _settings.MouseSensitivity, delta);
        }
        else controls.AdvancePointer(default, false, false);
        _cameraPointer = pointer;
    }

    /// <summary>DungeonRunner.FollowQueriedCreature: while <see cref="ClassicCreatureQueryRules.Follows"/>, the focus keeps
    /// the queried creature, read from the simulation (no creature is drawn before step 3g).</summary>
    private void FollowQueriedCreature()
    {
        if (_world == null || _simulation == null || _queryView == null || _scriptedView == null) return;
        if (!ClassicCreatureQueryRules.Follows(_tools.QueryTool, _queryView.Target, _scriptedView.PathPlaying, _hudControls.Modal)) return;
        if (_simulation.World.CreatureById(_queryView.Target.Value) is CreatureState creature)
            _world.Focus = CameraFocus.Over(new Fp32(creature.PosX), new Fp32(creature.PosY), _world.Focus.Y);
    }

    /// <summary>The bottom panel's collapse moves the view axis, as Unity's runner's handler does.</summary>
    private void PanelCollapsed(bool collapsed) => _world?.SetPanelCollapsed(collapsed);

    /// <summary>The script's camera zoom (Unity's runner's <c>ZoomRequested</c> handler).</summary>
    private void ScriptZoom(int zoom, int unused) => _world?.ClassicCamera?.SetZoom(new Fp32(zoom));

    /// <summary>A mouse press reaching the window, before any panel's <c>_Input</c> can mark it handled, as Unity's
    /// <c>Input.GetMouseButtonDown</c> sees every press; <see cref="QueueInput"/> routes it in the next frame.</summary>
    private void ReadPress(InputEvent @event)
    {
        // A scripted pointer's presses are its own (SamplePointer); the OS pointer's are left out.
        if (@event is not InputEventMouseButton button || _pointerScript != null) return;
        if (button.ButtonIndex == MouseButton.Left) { if (button.Pressed) _primaryDown = true; else _primaryUp = true; }
        else if (button.ButtonIndex == MouseButton.Right) { if (button.Pressed) _secondaryDown = true; else _secondaryUp = true; }
        // Unity's Input.mouseScrollDelta.y, summed over the frame: positive away from the user.
        else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp) _cameraWheel += button.Factor;
        else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown) _cameraWheel -= button.Factor;
        if (!_journal.Recording) return;
        FrameMouseButtons journalled = button.ButtonIndex switch
        {
            MouseButton.Left => FrameMouseButtons.Primary,
            MouseButton.Right => FrameMouseButtons.Secondary,
            MouseButton.Middle => FrameMouseButtons.Middle,
            _ => FrameMouseButtons.None,
        };
        if (button.Pressed) _journalButtonsDown |= journalled; else _journalButtonsUp |= journalled;
        if (!button.Pressed) return;
        if (button.ButtonIndex == MouseButton.WheelUp) _journalWheelY += button.Factor;
        else if (button.ButtonIndex == MouseButton.WheelDown) _journalWheelY -= button.Factor;
        else if (button.ButtonIndex == MouseButton.WheelRight) _journalWheelX += button.Factor;
        else if (button.ButtonIndex == MouseButton.WheelLeft) _journalWheelX -= button.Factor;
    }

    /// <summary>DungeonRunner.QueueInput through the shared <see cref="WorldPointerInput"/> (plan step 2h): nothing while ordinary
    /// input is not allowed (the world drag is dropped), then the HUD's press in Unity's order (<see cref="HudPressRouter"/>),
    /// the query tool's and a choice's world press (<see cref="WorldClickCommands"/>), the minimap's map press
    /// (<see cref="IWorldPointerHost.MinimapPress"/>), the tool, and a press's intent and translation on the picked tile (plan
    /// step 3e). No possession here, so the possession gate never holds.</summary>
    private void QueueInput(HudVisibility visibility, long milliseconds)
    {
        KeyModifiers modifiers = _keyboard.Modifiers;
        var press = new HudPress(_primaryDown, _secondaryDown, (modifiers & KeyModifiers.Shift) != 0,
            (modifiers & KeyModifiers.Control) != 0, (modifiers & KeyModifiers.Alt) != 0);
        bool primaryUp = _primaryUp, secondaryUp = _secondaryUp;
        _primaryDown = false; _secondaryDown = false; _primaryUp = false; _secondaryUp = false;
        if (_simulation == null || _worldPointer == null) return;
        _pressPointer = _pointer;
        _pressVisibility = visibility;
        _worldPointer.Queue(new WorldPointerFrame(_viewer, visibility.AllowOrdinaryInput, Possessing: false, _hudControls.Modal, press,
            primaryUp, secondaryUp, milliseconds), _simulation.World, _simulation.Database, _pending);
    }

    /// <summary>Unity's runner's minimap press: a glide to the tile, off while a camera path plays.</summary>
    void IWorldPointerHost.MinimapPress(in HudPress press)
    {
        if (_scriptedView != null && !_scriptedView.PathPlaying && _minimap != null && press.Primary
            && _minimap.TryNavigate(_pressPointer, out TilePos tile))
            StartGlide(tile);
    }

    FpPosition2? IWorldPointerHost.PointerPosition(TilePos tile) => PointerPosition(_pressPointer, tile);

    /// <summary>DungeonRunner.ScreenToTile: the tile under a screen pixel (top-left origin) by <see cref="TilePicker"/>, which
    /// meets the columns at the heights the scene draws; null over the HUD, off the map or before the camera is placed.</summary>
    private TilePos? PointerTile(Vector2 at, HudVisibility visibility)
    {
        if (PointerOverHud(at, visibility) || _world == null || _worldView == null || _simulation == null) return null;
        if (!_world.TryPickRay(at, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction)) return null;
        // TilePicker works in DK2's plane, Y south; the renderers' Z runs north.
        return TilePicker.Pick(new PickRay(origin.X, origin.Y, -origin.Z, direction.X, direction.Y, -direction.Z),
            _worldView, _simulation.World);
    }

    /// <summary>DungeonRunner.ScreenToPosition: the pointer's point on the tile's top plane, map coordinates
    /// (<see cref="TileTopPlane.PositionOn"/>).</summary>
    private FpPosition2? PointerPosition(Vector2 at, TilePos tile)
    {
        if (_world == null || _worldView == null || _simulation == null) return null;
        if (!_world.TryPickRay(at, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction)) return null;
        return TileTopPlane.PositionOn(origin, direction, tile, _worldView.TileAt(_simulation.World, tile.X, tile.Y).Solid);
    }

    /// <summary>DungeonRunner.ResolveWorldHover: an unexplored tile is the bare tile; else the ray's hit on the tile's top
    /// plane bounds the pick (<see cref="WorldHoverPicker.TryCastTerrain"/>). Entities are offered to the picker in step 3h;
    /// until then the rendered stage is empty and the sweep finds the tile.</summary>
    private WorldHoverHit ResolveWorldHover(Vector2 at, TilePos tile)
    {
        var bare = new WorldHoverHit(WorldHoverKind.Tile, tile);
        if (_simulation == null || _world == null || _worldView == null) return bare;
        WorldState world = _simulation.World;
        if (HoverCursorResolver.FogHit(world, _viewer, tile) is WorldHoverHit fogged) return fogged;
        if (!_world.TryPickRay(at, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction)
            || _world.View is not MinimapCameraView view) return bare;
        if (!WorldHoverPicker.TryCastTerrain(origin, direction, _worldView.TileAt(world, tile.X, tile.Y).Solid,
                out float terrainDistance, out System.Numerics.Vector3 ground))
            return bare;
        _picker.Begin(new HoverPickRay(origin, direction, System.Numerics.Vector3.Normalize(view.Forward)), terrainDistance, tile, ground);
        WorldHoverHit hit = _picker.TryRenderedResult(out WorldHoverHit rendered) ? rendered : _picker.SweepResult();
        return WorldHoverPicker.OnPointerTile(hit, tile);
    }

    /// <summary>Whether the window counts as focused: always under a scripted pointer, which a capture must not lose to
    /// the owner's other windows.</summary>
    private bool Focused => _pointerScript != null || GetWindow().HasFocus();

    /// <summary>The primary button held, the scripted pointer's or the OS's.</summary>
    private bool PrimaryHeld => _pointerScript != null ? _scripted.PrimaryHeld : Input.IsMouseButtonPressed(MouseButton.Left);

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
        if (_scripted.PrimaryDown) PushScriptedButton(viewport, MouseButton.Left, true, held);
        if (_scripted.SecondaryDown) PushScriptedButton(viewport, MouseButton.Right, true, held);
        if (_scripted.PrimaryUp) PushScriptedButton(viewport, MouseButton.Left, false, held);
        if (_scripted.SecondaryUp) PushScriptedButton(viewport, MouseButton.Right, false, held);
        _primaryDown |= _scripted.PrimaryDown; _primaryUp |= _scripted.PrimaryUp;
        _secondaryDown |= _scripted.SecondaryDown; _secondaryUp |= _scripted.SecondaryUp;
        if (!_journal.Recording) return;
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
            GD.Print("DK2.Godot: scripted ", button.ToString(), " press at (", _pointer.X, ", ", _pointer.Y, ") on frame ", _scriptFrame,
                ", tick ", _simulation?.World.CurrentTick.Value ?? 0);
    }

    /// <summary>DungeonRunner.ApplyHudAction, through the shared <see cref="HudActionRouter"/>: the options key, the options
    /// menu's entries, the save browser's Back and Continue lines and the minimap's corners (zoom, and query and sell, which
    /// post their GUI button and set the tool) and right click, and the portal, Heart and fight navigation's glides.</summary>
    private void ApplyHudAction(ClassicHudAction action) => ApplyHudAction(action, _pointer);

    /// <param name="pointer">Where the press was, in screen pixels: the magnifier zooms only under it.</param>
    private void ApplyHudAction(ClassicHudAction action, Vector2 pointer)
    {
        _actionPointer = pointer;
        _router?.Apply(action, _panel, _view);
    }

    void IHudActionHost.NavigatePortal() => NavigateRoom(RoomTypeId.Portal);

    void IHudActionHost.NavigateHeart()
    {
        if (_worldView?.MinimapHeartTile is TilePos heart) StartGlide(heart);
    }

    void IHudActionHost.NavigateFight()
    {
        if (_battles != null && _battleNavigation?.Next(_battles.Battles) is TilePos battle) StartGlide(battle);
    }

    void IHudActionHost.ZoomMinimap(bool zoomIn, bool zoomOut) => _minimap?.HandleZoomPress(_actionPointer, zoomIn, zoomOut);
    void IHudActionHost.PostGuiButton(GuiButtonTarget target) => PostGuiButton(target);

    // What HudPressRouter asks of this scene, at this frame's pointer: the panels' hit tests and the camera; the session is
    // not wired here.
    void IHudPressHost.ApplyHudAction(ClassicHudAction action) => ApplyHudAction(action, _pressPointer);

    void IHudPressHost.GoToFullMapTile()
    {
        if (_fullMap == null) return;
        if (_world == null || _simulation == null) return;
        TilePos tile = _fullMap.ResolvePointer(_pressPointer);
        _world.Focus = CameraFocus.TileCentre(tile, _world.Focus.Y, _simulation.World.Width, _simulation.World.Height);
        _glide.Stop();
    }

    void IHudPressHost.PollGameOptionsKeyboard() => _gameOptionsPanel?.CaptureKey(_keyboard.Read(_settings));

    ClassicOptionsAction IHudPressHost.OptionsActionAtPointer() => _options?.ActionAt(_pressPointer) ?? ClassicOptionsAction.None;

    void IHudPressHost.RefreshSaves() => _saves?.Refresh();

    /// <summary>Loading a level and leaving the game are the runner's (plan step 2): the session is dropped.</summary>
    void IHudPressHost.StartSession(ClassicSessionAction session) =>
        GD.Print("DK2.Godot: the options menu's ", session.ToString(), " is not wired yet.");

    bool IHudPressHost.TryPressOverview(in HudPress press, out CreatureOverviewRequest request)
    {
        request = default;
        return _overview != null && _overview.TryPress(_pressPointer, press, out request);
    }

    void IHudPressHost.FocusCreature(FpPosition3 position)
    {
        if (_world == null) return;
        _world.Focus = CameraFocus.Over(position.X, position.Y, _world.Focus.Y);
        _glide.Stop();
    }

    ClassicHudAction IHudPressHost.PressMinimap(in HudPress press) =>
        _minimap?.HandlePress(_pressPointer, press.Primary, press.Secondary) ?? ClassicHudAction.None;

    KeeperResourceKind IHudPressHost.ResourceAtPointer() => _resources?.ResourceAt(_pressPointer) ?? KeeperResourceKind.None;

    // What WorldClickCommands and WorldPointerInput ask of this scene, at this frame's press pointer.
    bool IWorldClickHost.PointerOverHud() => PointerOverHud(_pressPointer, _pressVisibility);
    TilePos? IWorldClickHost.PointerTile() => PointerTile(_pressPointer, _pressVisibility);
    WorldHoverHit IWorldClickHost.Hover(TilePos tile) => ResolveWorldHover(_pressPointer, tile);
    void IWorldClickHost.FollowQueriedCreature()
    {
        _glide.Stop();
        if (_world?.ClassicCamera is ClassicCamera camera) ClassicCreatureQueryRules.TurnToFollowHeading(camera);
    }

    /// <summary>DungeonRunner.ApplyGameSettings for what this scene has: the settings it reads (game speed, key bindings)
    /// and the HUD scale on the ported panels (ApplyHudScale), and the camera's scroll speed. The rendering settings
    /// (gamma, shadows, water, environment mapping) and the sound volumes and switches need systems not ported yet:
    /// their values change, and nothing acts on them.</summary>
    private void ApplySettings(GameSettings settings)
    {
        _settings = settings;
        if (_world?.Controls is ClassicCameraControls controls) controls.ScrollSpeed = settings.ScrollSpeed;
        // The twin of DungeonRunner.ApplyHudScale's panel list: the two change together while both adapters exist.
        int scale = settings.UiScale;
        if (_minimap != null) _minimap.UiScale = scale;
        if (_panel != null) _panel.UiScale = scale;
        if (_resources != null) _resources.UiScale = scale;
        if (_hover != null) _hover.UiScale = scale;
        if (_messages != null) _messages.UiScale = scale;
        if (_battleTab != null) _battleTab.UiScale = scale;
        if (_overview != null) _overview.UiScale = scale;
        if (_queryPanel != null) _queryPanel.UiScale = scale;
        if (_handCursor != null) _handCursor.UiScale = scale;
    }

    /// <summary>The settings file as <see cref="GameSettingsFile"/> reads it; the defaults, with a warning, when it cannot
    /// be read.</summary>
    private static GameSettings LoadSettings(string path)
    {
        try { return GameSettingsFile.Load(path); }
        catch (Exception e) when (IsFileError(e))
        {
            GD.PushWarning("DK2.Godot: cannot read the settings file ", path, " (", e.Message, "); using the defaults.");
            return new GameSettings();
        }
    }

    /// <summary>The settings pages' save request, written as Unity's runner writes it (<see cref="GameSettingsFile"/>); a
    /// failed write is a warning, and the settings stay applied.</summary>
    private void SaveSettings(GameSettings settings)
    {
        try { GameSettingsFile.Save(_settingsPath, settings); }
        catch (Exception e) when (IsFileError(e))
        {
            GD.PushWarning("DK2.Godot: cannot write the settings file ", _settingsPath, " (", e.Message, ").");
        }
    }

    private static bool IsFileError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    /// <summary>The save browser's Enter, or a press on its name or Load line, as DungeonRunner.SaveSession and LoadSession
    /// apply it. Save writes the session under the typed name (<see cref="SessionSaver"/>: the carry into the profile, the
    /// header, the world checkpoint, the profile); Load rebuilds the chosen save's simulation (<see cref="SessionLoader"/>,
    /// which writes the saved profile to the profile file) and reloads this scene to continue it, as Unity's runner
    /// replaces itself. This scene ticks inline, so no tick is in flight here. A refused file shows on the page.</summary>
    private void SaveLoadSubmitted()
    {
        if (_simulation == null || _saves == null || _saveLoad == null || _context == null || _saver == null || _loader == null) return;
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
                // Deferred to the frame's end, as Unity's runner switches only once the frame is done with it: a reload
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

    /// <summary>Reloads this scene to continue <see cref="s_resume"/>; a reload the tree refuses drops the handover, so the
    /// running level goes on and the next load can try again.</summary>
    private void ReloadForResume()
    {
        Error error = GetTree().ReloadCurrentScene();
        if (error == Error.Ok) return;
        s_resume = null;
        GD.PushWarning("DK2.Godot: the scene could not be reloaded to continue the save (", error.ToString(), ").");
        _saveLoad?.Error("The scene could not be reloaded.");
    }

    /// <summary>DungeonRunner.LoadFrontEndProfile for this session, with the front end's fallback (<see cref="UserFolders.LoadProfile"/>).</summary>
    private void LoadProfile()
    {
        if (_profiles != null && _context != null) UserFolders.LoadProfile(_profiles, _profilePath, _context.Mode, _context.ReturnPage);
    }

    /// <summary>DungeonRunner.SelectPanelChoice: <see cref="HudToolState.SelectPanelChoice"/>.</summary>
    private void SelectPanelChoice(BottomPanelSelection selection) => _tools.SelectPanelChoice(selection, _input, _queryView);

    /// <summary>A stored special's slot: action 88, or targeting with the panel and the tool back on the hand
    /// (<see cref="HudToolState.ActivateStoredSpecial(PlayerId, BottomPanelChoice, PlayerInputTranslator, BottomPanelView)"/>).</summary>
    private void ActivateStoredSpecial(BottomPanelChoice special)
    {
        // As DungeonRunner.ActivateStoredSpecial: 0x00411140 ignores a special's slot while the Options menu is open (+0x2722).
        if (_hudControls.OptionsVisible) return;
        if (_view != null && _tools.ActivateStoredSpecial(_viewer, special, _input, _view) is PlayerCommand command) _pending.Add(command);
    }

    /// <summary>Action 0x72, a GUI button (ADR-0670).</summary>
    private void PostGuiButton(GuiButtonTarget target) => _pending.Add(new GuiButtonCommand(_viewer, target));

    /// <summary>The panels this scene has, asked as DungeonRunner.PointerOverHud asks its own (<see cref="HudPointerMap"/>).</summary>
    private bool PointerOverHud(Vector2 position, HudVisibility visibility) =>
        HudPointerMap.Over(new PointerPanels(this, position, visibility), fullMapVisible: _hudControls.FullMapVisible,
            queryHasTarget: _queryView != null && _queryView.Target != CreatureQueryTarget.None, modal: _hudControls.Modal);

    /// <summary>This scene's panels at one pointer, for <see cref="HudPointerMap"/>; a struct, so a query allocates nothing.</summary>
    private readonly struct PointerPanels(InGameHud hud, Vector2 position, HudVisibility visibility) : IHudPointerPanels
    {
        public bool Contains(HudPointerPanel panel) => panel switch
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

    // The panels under the pointer for HudHoverText, answered as Unity's runner answers; the ones not ported give the
    // empty answer.
    int IHudHoverSources.OverviewTextId() => _overview?.HoveredTextId(_pointer) ?? 0;
    CreatureOverviewType? IHudHoverSources.OverviewColumn() => _overview?.HoveredColumnType(_pointer);
    HoverTextSnapshot IHudHoverSources.CreatureTypeText(CreatureOverviewType column) =>
        _typeText != null && _simulation != null ? _typeText.CreatureType(_simulation.Database.CreatureById(column.Value)) : HoverTextSnapshot.Empty;
    bool IHudHoverSources.OverMessageTabs() => _messages != null && _messages.ContainsPointer(_pointer);
    HoverTextSnapshot IHudHoverSources.MessageTabsText() => _messages?.HoverText ?? HoverTextSnapshot.Empty;
    bool IHudHoverSources.OverBattleTab() => _battleTab != null && _battleTab.ContainsPointer(_pointer);
    HoverTextSnapshot IHudHoverSources.BattleTabText() => _battleTab?.HoverText(_pointer) ?? HoverTextSnapshot.Empty;
    bool IHudHoverSources.OverBottomPanel() => _panel != null && _panel.ContainsPointer(_pointer);
    HoverTextSnapshot IHudHoverSources.BottomPanelText() => _panel?.HoverText ?? HoverTextSnapshot.Empty;
    ClassicHudAction IHudHoverSources.MinimapControl() => _minimap?.HoveredControl(_pointer) ?? ClassicHudAction.None;
    KeeperResourceKind IHudHoverSources.Resource() => _resources?.ResourceAt(_pointer) ?? KeeperResourceKind.None;

    /// <summary>Every key event reaches the keyboard, as Unity's <c>Input.GetKey</c> reads the keys whatever the UI holds.</summary>
    /// <summary>Unity's runner removes its heart health observer when the level goes; so does this scene.</summary>
    public override void _ExitTree()
    {
        if (_heartHealthObserver != null && _simulation != null) _simulation.World.HeartHealthChanged -= _heartHealthObserver;
        _heartHealthObserver = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key) return;
        _keyboard.Feed(key);
        if (!_journal.Recording || key.Echo) return;
        var journalled = new HostKey((int)key.PhysicalKeycode);
        if (key.Pressed)
        {
            _journalDown.Add(journalled);
            if (!_journalHeld.Contains(journalled)) _journalHeld.Add(journalled);
        }
        else
        {
            _journalUp.Add(journalled);
            _journalHeld.Remove(journalled);
        }
    }

    public override void _Notification(int what)
    {
        // A window without focus misses the releases: the keys, the hand, the bottom panel's press and the world drag
        // (Unity's runner's OnApplicationFocus).
        if (what == NotificationApplicationFocusOut)
        {
            _keyboard.ReleaseAll();
            _journalHeld.Clear();
            // A scripted pointer keeps its gestures: the owner's other windows must not change a capture.
            if (_pointerScript != null) return;
            // Unity's runner's OnApplicationFocus order: the hand back to the default, the panel's press, then the world drag.
            _handCursor?.Update(HandCursorKind.Default, false, (long)(_elapsed * 1000));
            PointerGestures.CancelAll(_panel, _input, _cursorFeedback);
            return;
        }
        // Unity's Input.mousePosition leaves the screen with the pointer; Godot stops reporting it instead.
        if (what == NotificationWMMouseExit && _pointerScript == null) { _pointerInWindow = false; return; }
        if (what == NotificationWMMouseEnter) { _pointerInWindow = true; return; }
        // Freed, not merely removed from the tree: a reparented scene keeps its level.
        if (what != NotificationPredelete) return;
        // Unity's runner closes its journal when it is destroyed, writing the frame a session switch left open.
        _journal.Close("destroyed", _pending, _pacer.Debt, _simulation);
        if (_model != null)
        {
            _model.Selected -= SelectPanelChoice; _model.SpecialChosen -= ActivateStoredSpecial; _model.GuiButton -= PostGuiButton;
            _model.NavigateRoom -= NavigateRoom; _model.NavigateDoor -= NavigateDoor; _model.NavigateTrap -= NavigateTrap;
            _model.CollapsedChanged -= PanelCollapsed;
        }
        if (_battleTab != null) _battleTab.Clicked -= BattleTabClicked;
        if (_window != null && IsInstanceValid(_window)) _window.WindowInput -= ReadPress;
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
        _hover = null; _hudHover = null; _overview = null; _overviewView = null; _typeText = null; _queryPanel = null; _queryView = null;
        _panel = null; _model = null; _view = null; _messages = null; _messageView = null; _resources = null; _resourceView = null; _art = null; _sources = null; _simulation = null;
    }
}
