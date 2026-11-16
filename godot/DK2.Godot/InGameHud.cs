// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Scene root of <c>InGameHud.tscn</c>: a campaign level started as Unity's <c>DungeonRunner</c> starts one
/// (<see cref="CampaignLevelData"/>, keeper 3, seed 1, fog on, its first turn run before the first frame), paced by
/// <see cref="TickPacer"/> at normal speed, with the in-game HUD over a black screen and no 3D world. Ported (plan step
/// 1b): the resource strip, the bottom panel, the creature overview, the creature query details, the message tabs, the
/// battle tab, the hover HUD (<see cref="HudHoverText"/>), the options menu (<see cref="ClassicHudControls"/>, opened by
/// the options key; while it is open the level pauses and the other panels take no input, as Unity's <c>HudModal</c>) and
/// its save and load pages (writing and reading a save are not wired) and game options pages (<see cref="ClassicGameOptionsPanel"/>;
/// the settings are read from and saved to <c>user://game-settings.ini</c>) and the full map (<see cref="ClassicFullMap"/>, opened
/// by its key, closed by it, Escape or a left or right press; while it shows the normal HUD hides) and the minimap
/// (<see cref="ClassicMinimap"/>, drawn from the level-start camera's focus and heading, as no camera moves them here; its
/// grid is the full map's too). Not wired (logic still in Unity's runner, or needing the 3D world): the list is in
/// docs/plans/2026-11-15-godot-migration.md. User arguments after <c>--</c> (<c>--stage-battle</c> is <see cref="StageBattle"/>): godot/README.md.</summary>
public partial class InGameHud : Node, IHudHoverSources, IHudActionHost, IHudPressHost, IWorldClickHost
{
    private readonly List<PlayerCommand> _pending = new();
    private readonly PlayerInputTranslator _input = new();
    /// <summary>The tool, the panel's choice, the query tool and the selected corner, as Unity's runner keeps them.</summary>
    private readonly HudToolState _tools = new();
    /// <summary>The HUD actions, applied as Unity's runner applies them; this scene is its host.</summary>
    private HudActionRouter? _router;
    /// <summary>Where the press that raised the action being applied was, for the magnifier's zoom.</summary>
    private Vector2 _actionPointer;
    /// <summary>The overview's pickups, as Unity's runner's; no hand cursor is drawn here.</summary>
    private PickupFlow? _pickups;
    /// <summary>The HUD's presses in Unity's order (DungeonRunner.QueueHudInput); this scene is its host.</summary>
    private HudPressRouter? _presses;
    /// <summary>The query tool's and a held choice's world presses (DungeonRunner.QueuePanelWorldAction). Without picking
    /// (plan step 3) only the right-click cancels act; a left press finds no tile.</summary>
    private WorldClickCommands? _worldClicks;
    /// <summary>The window, whose input is read before any panel takes it.</summary>
    private Window? _window;
    /// <summary>The left and right buttons pressed since the last frame, read from the window before any panel marks the
    /// event handled, as Unity's <c>Input.GetMouseButtonDown</c> sees every press whatever the UI does with it.</summary>
    private bool _primaryDown, _secondaryDown;
    /// <summary>The pointer and the HUD's visibility for this frame's presses (Unity's <c>Input.mousePosition</c> and
    /// <c>CurrentHud</c> while the runner queues input).</summary>
    private Vector2 _pressPointer;
    private HudVisibility _pressVisibility;
    private RoomNavigation? _roomNavigation;
    private ThingNavigation? _doorNavigation, _trapNavigation;
    private readonly List<RoomNavigationEntry> _roomEntries = new();
    private readonly List<ThingNavigationEntry> _thingEntries = new();
    private readonly TickPacer _pacer = new();
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
    private ClassicSaveLoadPanel? _saveLoad;
    /// <summary>The full map, over the minimap's class grid, as Unity's runner shares it.</summary>
    private ClassicFullMap? _fullMap;
    private ClassicMinimap? _minimap;
    /// <summary>The world as the viewer's HUD sees it (Unity's runner's <c>_worldView</c>): the minimap's colours and markers.</summary>
    private WorldView? _worldView;
    /// <summary>The camera's focus and heading the minimap is drawn from: the level start's (<see cref="ClassicCamera.LevelStartFocus(IReadOnlyList{PlayerDef}, PlayerId, int, int)"/>,
    /// <see cref="ClassicCamera.LevelStartYaw"/>), which stay put because no camera is ported (plan step 3).</summary>
    private FpPosition3 _cameraFocus;
    private readonly Dk2Angle _cameraHeading = ClassicCamera.LevelStartYaw;
    /// <summary>Only its creature-type text is used here: world hover needs the 3D world.</summary>
    private WorldHoverText? _typeText;
    private KeeperResourceSnapshot _resourceFacts;
    /// <summary>The pointer in screen pixels from the top left, polled each frame as Unity reads
    /// <c>Input.mousePosition</c> (an event a panel handles never reaches this node).</summary>
    private Vector2 _pointer;
    private bool _pointerInWindow = true;
    /// <summary>The reach of the small pointing finger, the cursor Unity's runner shows over the HUD while nothing is held
    /// and no spell is armed. Valid only until the runner's cursor resolution (DungeonRunner.cs, held hand, slap and tag
    /// cursors; itself HYPOTHESIS over the HUD) moves into src in plan step 2; world hover and the hand cursor are not
    /// ported, so today a status line or tooltip only ever shows over the HUD with nothing held.</summary>
    private readonly float _cursorReach = HandCursor.Reach(HandCursorKind.SmallPoint);
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
        string levelName = launch.Level ?? "Level1";
        ulong seed = launch.Seed ?? 1;
        // Godot's root is user:// (%APPDATA%/Godot/app_userdata/DK2.Godot), never Unity's persistentDataPath.
        var folders = new GameFolders(ProjectSettings.GlobalizePath("user://"))
        {
            SettingsFile = LaunchArguments.Value(args, "--settings"), SavesDirectory = LaunchArguments.Value(args, "--saves"),
        };
        CampaignLevelData level = CampaignLevelData.Load(install, levelName);
        _simulation = level.CreateSimulation(seed, fogOfWarEnabled: true);
        _simulation.World.LocalPlayerId = _viewer;
        // DungeonRunner.InitializeSettings: game-settings.ini beside the campaign profile.
        _settingsPath = folders.Settings;
        _settings = LoadSettings(_settingsPath);
        _optionsPages = new OptionsPagesController(_settings);
        _optionsPages.Applied += ApplySettings;
        _optionsPages.SaveRequested += SaveSettings;

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
        // The panel's right clicks walk the rooms, doors and traps as Unity's runner does; the glide needs the camera.
        _roomNavigation = new RoomNavigation(_viewer);
        _doorNavigation = new ThingNavigation(_viewer);
        _trapNavigation = new ThingNavigation(_viewer);
        _model.NavigateRoom += NavigateRoom;
        _model.NavigateDoor += NavigateDoor;
        _model.NavigateTrap += NavigateTrap;
        // Added after the panel and before the tabs, so it draws between them, as Unity's sorting order 13 between 12 and 14.
        var overviewArt = new InstallCreatureOverviewArt(install, level.Database, _art.Image, _art.Text);
        _overviewView = new CreatureOverview(level.Database, _viewer, overviewArt.Name, overviewArt.Order);
        _overview = new ClassicCreatureOverview { Name = "CreatureOverview" };
        // Its presses are the router's (QueueInput): a pickup is posted, a focus needs the camera.
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
        _pickups = new PickupFlow(_worldView, cursor: null);
        _presses = new HudPressRouter(_hudControls, _tools, _overviewView, _view, _pickups, _viewer, this);
        _worldClicks = new WorldClickCommands(_tools, _input, _queryView, _view, _viewer, this);
        _cameraFocus = MinimapHud.FixedFocus(ClassicCamera.LevelStartFocus(level.Players, _viewer,
            _simulation.World.Width, _simulation.World.Height));
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
        // No camera yet: the goto button shows for a message with a location, as in Unity, but moves nothing.
        _messages.Open(_art, _art.Font, _messageView,
            messageText.Resolve, messageText.Briefing, message => messageView.LocationOncePerTick(simulation.World, message),
            goTo: null, PlayerNames.From(level.Players));
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
        _window = GetWindow();
        _window.WindowInput += ReadPress;
        _hudHover = new HudHoverText(_sources.Strings.Resolve);
        // The played campaign slot only world hover reads, passed as Unity's runner passes it.
        _typeText = new WorldHoverText(level.Database, _sources.Strings.Resolve, _viewer, CampaignSlot.ForLevel(levelName));
        // DungeonRunner applies the loaded settings once the level is up.
        ApplySettings(_settings);
        for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++) _simulation.Tick(InputFrame.Empty);
        if (Array.IndexOf(args, "--query-first-creature") >= 0)
            foreach (CreatureState creature in _simulation.World.OwnedCreatures(_viewer))
            {
                _queryView.Query(new CreatureQueryTarget(creature.Id));
                break;
            }
        if (Array.IndexOf(args, "--stage-battle") >= 0) StageBattle(_simulation);
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
        GD.Print("DK2.Godot in-game HUD: ", levelName, " seed ", seed.ToString(CultureInfo.InvariantCulture), ", saves in ", savesDirectory,
            ", settings in ", _settingsPath);
    }

    public override void _Process(double delta)
    {
        if (_simulation == null || _view == null || _panel == null || _model == null || _resourceView == null
            || _resources == null || _messageView == null || _messages == null || _hover == null || _hudHover == null
            || _overviewView == null || _overview == null || _queryView == null || _queryPanel == null
            || _battles == null || _battleTab == null || _options == null || _saveLoad == null || _optionsPages == null
            || _gameOptionsPanel == null || _fullMap == null || _minimap == null || _worldView == null) return;
        // Engine time, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        // Once per settled turn, before the HUD draws, as DungeonRunner.UpdateWorldSound captures it.
        _battles.Capture(_simulation.World, _simulation.Database, _simulation.World.CurrentTick);
        // Once per settled turn, as DungeonRunner captures it before the HUD draws; the full map reads the same grid.
        bool minimapCaptured = _minimap.Capture(_simulation.World, _worldView, _simulation.Navigation);
        // The HUD keys, as DungeonRunner.UpdateHudControls reads them. Only the options menu's and the full map's toggles
        // are applied: the other keys' GUI toggle and camera moves are not ported. AllowOptionsKey is always true: the
        // scripted camera path that holds the key back (DungeonRunner's pathWasPlaying) is not ported (plan step 2). While
        // the keyboard page captures a key, no shortcut fires. The camera's visibility is the scripted view's, not wired:
        // always shown, taking input.
        var camera = new HudVisibility(true, true);
        ClassicHudAction shortcut = _keyboard.Read(_settings).HudShortcut(
            _hudControls.ShortcutContext(camera, allowOptionsKey: true, capturingKey: _optionsPages.CapturingAction != null),
            _keyboard.EscapePressed);
        if (shortcut is ClassicHudAction.ToggleOptions or ClassicHudAction.ToggleFullMap) ApplyHudAction(shortcut);
        // DungeonRunner.CurrentHud (PlayedHud; the warm frames are not ported): the full map hides the normal HUD.
        HudVisibility visibility = _hudControls.Visibility(camera);
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
        long milliseconds = (long)(_elapsed * 1000);
        // DungeonRunner.QueueInput after the shortcuts, as there; its game options branch polls the keys before they end.
        QueueInput(visibility, milliseconds);
        _keyboard.EndFrame();
        // The HUD draws the settled world, then the frame's ticks run (DungeonRunner's order).
        // As DungeonRunner.DrawHudControls captures it and CaptureWorldHover shows and draws it.
        _resourceFacts = _resourceView.Capture(_simulation.World, displayHealth: visibility.ShowNormalHud);
        _resources.SetVisibility(visibility);
        if (visibility.ShowNormalHud) _resources.Draw(_resourceFacts, unchecked((uint)milliseconds));
        _view.Capture(_simulation.World);
        _model.GamePaused = _hudControls.TicksPaused;
        _panel.Draw(milliseconds);
        _messageView.Capture(_simulation.World, milliseconds);
        // No sound player yet: drop the requests so they do not pile up.
        _model.Sounds.Clear(); _view.Sounds.Clear(); _messageView.Sounds.Clear(); _messageView.Speech.Clear();
        _hudControls.Sounds.Clear(); _optionsPages.Sounds.Clear();
        // Visible; taking input unless the options menu holds it. The scripted view that would hide or hold the tabs is
        // not wired.
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
        _minimap.Draw(_tools.SelectedCorner, _hudControls.OptionsVisible, _cameraFocus, _cameraHeading);
        // After the minimap, as DungeonRunner.DrawHudControls draws it.
        _options.Draw();
        // DungeonRunner.DrawHudControls captures it after the bottom panel and draws it after the menu.
        _fullMap.Capture(_simulation.World, _simulation.Database, _viewer, minimapCaptured);
        _fullMap.Draw();
        // DungeonRunner.DrawHudControls draws it after the save browser; the two never show together.
        _gameOptionsPanel.Draw();
        // Last, as DungeonRunner.DrawHudControls captures and draws it; the options menu hides it.
        _queryPanel.Draw(_queryView.Capture(_simulation.World), visibility, modal);
        // The hover, after the HUD is drawn (DungeonRunner.CaptureWorldHover): over the HUD, the panels' text; elsewhere
        // nothing, because world hover needs the 3D world.
        _hover.SetVisibility(visibility, milliseconds);
        _pointer = GetViewport().GetMousePosition();
        bool inside = _pointerInWindow && GetViewport().GetVisibleRect().HasPoint(_pointer);
        bool allowed = visibility.AllowOrdinaryInput && GetWindow().HasFocus() && inside;
        HoverTextSnapshot hover = allowed && PointerOverHud(_pointer, visibility) ? _hudHover.Capture(this, _resourceFacts) : HoverTextSnapshot.Empty;
        _hover.Draw(hover, _pointer, _cursorReach, milliseconds);

        // Clamped as Unity's runner clamps it (Time.maximumDeltaTime, 1/3 s), so a stall is not paid back at the cap.
        // DungeonRunner's tick gate: no turn runs, and none accrues, while it holds.
        if (_hudControls.TicksPaused) return;
        _pacer.Accrue(Math.Min(delta, TickPacer.MaximumFrameSeconds), 1f, _settings.GameSpeed);
        int ticks = _pacer.Take(TickPacer.DefaultMaxTicksPerFrame);
        InputFrame first = TickPacer.FirstInput(ticks, _pending);
        if (first.Commands != null) _pending.Clear();
        for (int i = 0; i < ticks; i++) _simulation.Tick(TickPacer.InputFor(i, first));
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
    /// suppressed-input and modal gate): the cancel's left click posts; the zoom goes to the router, whose fight navigation
    /// needs the camera, and the cancel's and the alarm's glides only print (plan step 3).</summary>
    private void BattleTabClicked(BattleTabButton button, int mouse)
    {
        if (_simulation == null || _trapNavigation == null) return;
        BattleTabClick click = BattleTabView.Click(button, mouse, _hudControls, _simulation.World, _simulation.Database, _viewer,
            _trapNavigation, _thingEntries);
        if (click.HudAction != ClassicHudAction.None) ApplyHudAction(click.HudAction);
        if (click.Command != null) _pending.Add(click.Command);
        if (click.Glide is TilePos tile) NotWiredGlide("battle tab", tile);
    }

    /// <summary>DungeonRunner.NavigateRoom: the next of the viewer's rooms of a type (<see cref="RoomNavigation.Entries"/>).</summary>
    private void NavigateRoom(RoomTypeId type)
    {
        if (_simulation == null || _roomNavigation == null) return;
        RoomNavigation.Entries(_simulation.World, _simulation.Database, _roomEntries);
        if (_roomNavigation.Next(_roomEntries, type) is TilePos tile) NotWiredGlide("room", tile);
    }

    /// <summary>DungeonRunner.NavigateDoor: the next of the viewer's doors of a kind, newest first.</summary>
    private void NavigateDoor(byte kind)
    {
        if (_simulation == null || _doorNavigation == null) return;
        ThingNavigation.DoorEntries(_simulation.World.Doors, _thingEntries);
        if (_doorNavigation.Next(_thingEntries, kind) is TilePos tile) NotWiredGlide("door", tile);
    }

    /// <summary>DungeonRunner.NavigateTrap: the next of the viewer's traps of a kind, newest first.</summary>
    private void NavigateTrap(byte kind)
    {
        if (_simulation == null || _trapNavigation == null) return;
        ThingNavigation.TrapEntries(_simulation.World.Traps, _thingEntries);
        if (_trapNavigation.Next(_thingEntries, kind) is TilePos tile) NotWiredGlide("trap", tile);
    }

    private static void NotWiredGlide(string what, TilePos tile) =>
        GD.Print("DK2.Godot: the ", what, " glide to tile (", tile.X, ", ", tile.Y, ") is not wired yet: there is no camera.");

    /// <summary>A mouse press reaching the window, before any panel's <c>_Input</c> can mark it handled, as Unity's
    /// <c>Input.GetMouseButtonDown</c> sees every press; <see cref="QueueInput"/> routes it in the next frame.</summary>
    private void ReadPress(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        if (button.ButtonIndex == MouseButton.Left) _primaryDown = true;
        else if (button.ButtonIndex == MouseButton.Right) _secondaryDown = true;
    }

    /// <summary>DungeonRunner.QueueInput for what this scene has (plan step 2c): nothing while ordinary input is not allowed
    /// (the world drag is dropped), then the HUD's press in Unity's order (<see cref="HudPressRouter"/>: an overview pickup
    /// is posted through <see cref="PickupFlow"/>, a left press on the gold withdraws it), the query tool's and a choice's
    /// world press (<see cref="WorldClickCommands"/>: a right press cancels the query tool or a held tool; a left press finds
    /// no tile) and a left press on the minimap's map. Not wired: possession, the scripted path and whole-map gates, and the
    /// hand's own world press, which need the camera and picking (plan steps 2h and 3).</summary>
    private void QueueInput(HudVisibility visibility, long milliseconds)
    {
        KeyModifiers modifiers = _keyboard.Modifiers;
        var press = new HudPress(_primaryDown, _secondaryDown, (modifiers & KeyModifiers.Shift) != 0,
            (modifiers & KeyModifiers.Control) != 0, (modifiers & KeyModifiers.Alt) != 0);
        _primaryDown = false; _secondaryDown = false;
        if (_simulation == null || _presses == null || _worldClicks == null || _minimap == null) return;
        if (!visibility.AllowOrdinaryInput)
        {
            _input.CancelPointerGesture();
            return;
        }
        _pressPointer = GetViewport().GetMousePosition();
        _pressVisibility = visibility;
        if (_presses.Route(press, _simulation.World, _simulation.Database, milliseconds, _pending)
            || _worldClicks.Queue(press, _simulation.Database, _pending)) return;
        if (press.Primary && _minimap.TryNavigate(_pressPointer, out TilePos tile))
            GD.Print("DK2.Godot: the minimap's move to tile (", tile.X, ", ", tile.Y, ") is not wired yet.");
    }

    /// <summary>DungeonRunner.ApplyHudAction, through the shared <see cref="HudActionRouter"/>: the options key, the options
    /// menu's entries, the save browser's Back and Continue lines and the minimap's corners (zoom, and query and sell, which
    /// post their GUI button and set the tool) and right click. Navigation needs the camera and only prints (plan step 3).</summary>
    private void ApplyHudAction(ClassicHudAction action) => ApplyHudAction(action, _pointer);

    /// <param name="pointer">Where the press was, in screen pixels: the magnifier zooms only under it.</param>
    private void ApplyHudAction(ClassicHudAction action, Vector2 pointer)
    {
        _actionPointer = pointer;
        _router?.Apply(action, _panel, _view);
    }

    void IHudActionHost.NavigatePortal() => GD.Print("DK2.Godot: the portal navigation is not wired yet: there is no camera.");
    void IHudActionHost.NavigateHeart() => GD.Print("DK2.Godot: the heart navigation is not wired yet: there is no camera.");
    void IHudActionHost.NavigateFight() => GD.Print("DK2.Godot: the fight navigation is not wired yet: there is no camera.");
    void IHudActionHost.ZoomMinimap(bool zoomIn, bool zoomOut) => _minimap?.HandleZoomPress(_actionPointer, zoomIn, zoomOut);
    void IHudActionHost.PostGuiButton(GuiButtonTarget target) => PostGuiButton(target);

    // What HudPressRouter asks of this scene, at this frame's pointer: the panels' hit tests; the camera and the session are
    // not wired here.
    void IHudPressHost.ApplyHudAction(ClassicHudAction action) => ApplyHudAction(action, _pressPointer);

    void IHudPressHost.GoToFullMapTile()
    {
        if (_fullMap == null) return;
        TilePos tile = _fullMap.ResolvePointer(_pressPointer);
        GD.Print("DK2.Godot: the full map's move to tile (", tile.X, ", ", tile.Y, ") is not wired yet.");
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

    void IHudPressHost.FocusCreature(FpPosition3 position) =>
        GD.Print("DK2.Godot: the overview's focus is not wired yet: there is no camera.");

    ClassicHudAction IHudPressHost.PressMinimap(in HudPress press) =>
        _minimap?.HandlePress(_pressPointer, press.Primary, press.Secondary) ?? ClassicHudAction.None;

    KeeperResourceKind IHudPressHost.ResourceAtPointer() => _resources?.ResourceAt(_pressPointer) ?? KeeperResourceKind.None;

    // What WorldClickCommands asks of this scene. There is no 3D world to pick (plan step 3), so no press finds a tile.
    bool IWorldClickHost.PointerOverHud() => PointerOverHud(_pressPointer, _pressVisibility);
    TilePos? IWorldClickHost.PointerTile() => null;
    WorldHoverHit IWorldClickHost.Hover(TilePos tile) => new(WorldHoverKind.Tile, tile);
    void IWorldClickHost.FollowQueriedCreature() { }

    /// <summary>DungeonRunner.ApplyGameSettings for what this scene has: the settings it reads (game speed, key bindings)
    /// and the HUD scale on the ported panels (ApplyHudScale). The scroll speed and the camera, the rendering settings
    /// (gamma, shadows, water, environment mapping) and the sound volumes and switches need systems not ported yet:
    /// their values change, and nothing acts on them.</summary>
    private void ApplySettings(GameSettings settings)
    {
        _settings = settings;
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

    /// <summary>The save browser's Enter, or a press on its name or Load line. Unity's runner writes the session there
    /// (DungeonRunner.SaveSession: the campaign profile and its carry, the header, the world snapshot) or rebuilds the world
    /// and the session from the chosen save (LoadSession); both need the runner's session and campaign profile, so the
    /// request is dropped here (plan step 2), and nothing is written.</summary>
    private void SaveLoadSubmitted() =>
        GD.Print("DK2.Godot: the ", _hudControls.OptionsPage.ToString(), " page's submit is not wired yet.");

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
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key) _keyboard.Feed(key);
    }

    public override void _Notification(int what)
    {
        // A window without focus misses the releases: the keys, the bottom panel's press and the world drag (Unity's
        // runner's OnApplicationFocus; there is no hand cursor feedback here).
        if (what == NotificationApplicationFocusOut)
        {
            _keyboard.ReleaseAll();
            PointerGestures.CancelAll(_panel, _input, feedback: null);
            return;
        }
        // Unity's Input.mousePosition leaves the screen with the pointer; Godot stops reporting it instead.
        if (what == NotificationWMMouseExit) { _pointerInWindow = false; return; }
        if (what == NotificationWMMouseEnter) { _pointerInWindow = true; return; }
        // Freed, not merely removed from the tree: a reparented scene keeps its level.
        if (what != NotificationPredelete) return;
        if (_model != null)
        {
            _model.Selected -= SelectPanelChoice; _model.SpecialChosen -= ActivateStoredSpecial; _model.GuiButton -= PostGuiButton;
            _model.NavigateRoom -= NavigateRoom; _model.NavigateDoor -= NavigateDoor; _model.NavigateTrap -= NavigateTrap;
        }
        if (_battleTab != null) _battleTab.Clicked -= BattleTabClicked;
        if (_window != null && IsInstanceValid(_window)) _window.WindowInput -= ReadPress;
        _window = null; _presses = null; _worldClicks = null; _pickups = null;
        _roomNavigation = null; _doorNavigation = null; _trapNavigation = null;
        if (_saveLoad != null)
        {
            _saveLoad.Submit -= SaveLoadSubmitted; _saveLoad.HudAction -= ApplyHudAction;
        }
        if (_optionsPages != null) { _optionsPages.Applied -= ApplySettings; _optionsPages.SaveRequested -= SaveSettings; }
        _battleTab = null; _battles = null; _options = null; _saveLoad = null; _saves = null; _fullMap = null;
        _minimap = null; _worldView = null;
        _gameOptionsPanel = null; _gameOptions = null; _optionsPages = null;
        _art?.Dispose(); _sources?.Dispose();
        _hover = null; _hudHover = null; _overview = null; _overviewView = null; _typeText = null; _queryPanel = null; _queryView = null;
        _panel = null; _model = null; _view = null; _messages = null; _messageView = null; _resources = null; _resourceView = null; _art = null; _sources = null; _simulation = null;
    }
}
