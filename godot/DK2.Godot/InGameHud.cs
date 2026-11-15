// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
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
/// its save and load pages (writing and reading a save are not wired). Not wired (logic still in Unity's runner, or
/// needing the 3D world): the list is in docs/plans/2026-11-15-godot-migration.md. User arguments after <c>--</c>:
/// <c>--level &lt;name&gt;</c>, <c>--seed &lt;n&gt;</c>, <c>--query-first-creature</c> (queries the viewer's first creature),
/// <c>--stage-battle</c> (<see cref="StageBattle"/>), <c>--open-options [entry]</c> (opens the options menu, then presses
/// a <see cref="ClassicOptionsAction"/> entry by name, such as <c>RestartLevel</c> or <c>Save</c>) and <c>--saves
/// &lt;directory&gt;</c> (the saves directory, <c>user://Saves</c> by default).</summary>
public partial class InGameHud : Node, IHudHoverSources
{
    /// <summary>DungeonRunner's <c>maxTicksPerFrame</c> in the Dungeon scene.</summary>
    private const int MaxTicksPerFrame = 2;
    /// <summary>Unity's Time.maximumDeltaTime in this project (ProjectSettings/TimeManager.asset).</summary>
    private const double MaximumFrameSeconds = 1.0 / 3.0;
    private readonly List<PlayerCommand> _pending = new();
    private readonly PlayerInputTranslator _input = new();
    private readonly TickPacer _pacer = new();
    /// <summary>The default settings: the game speed and the key bindings (no settings file is read yet).</summary>
    private readonly GameSettings _settings = new();
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
        string levelName = Argument(args, "--level") ?? "Level1";
        ulong seed = ulong.TryParse(Argument(args, "--seed"), NumberStyles.None, CultureInfo.InvariantCulture, out ulong s) ? s : 1;
        CampaignLevelData level = CampaignLevelData.Load(install, levelName);
        _simulation = level.CreateSimulation(seed, fogOfWarEnabled: true);
        _simulation.World.LocalPlayerId = _viewer;

        _sources = new InstallHudSources(install);
        _art = new InstallBottomPanelArt(_sources, level.Database);
        // Added first so it draws under the panel, as Unity's sorting orders 11 and 12.
        _resourceView = new KeeperResourceView(level.Database, _viewer);
        _resources = new ClassicResourceHud { Name = "ResourceHud" };
        _resources.Open(_sources);
        AddChild(_resources);
        _view = new BottomPanelView(level.Database, _viewer, _art.Name, _art.Order, campaignMode: true);
        _panel = new ClassicBottomPanel { Name = "BottomPanel" };
        _panel.Open(_art, _view);
        AddChild(_panel);
        _panel.OpenAtLevelStart();
        _model = _panel.Model;
        _model.Selected += SelectPanelChoice;
        _model.SpecialChosen += ActivateStoredSpecial;
        _model.GuiButton += PostGuiButton;
        // Added after the panel and before the tabs, so it draws between them, as Unity's sorting order 13 between 12 and 14.
        var overviewArt = new InstallCreatureOverviewArt(install, level.Database, _art.Image, _art.Text);
        _overviewView = new CreatureOverview(level.Database, _viewer, overviewArt.Name, overviewArt.Order);
        _overview = new ClassicCreatureOverview { Name = "CreatureOverview" };
        // Its Requested (a pickup or a focus) is not handled: the hand and the camera are not ported.
        _overview.Open(overviewArt, _overviewView);
        AddChild(_overview);
        // Unity gives it the overview's sorting order 13 and creates it after the overview; added after it here.
        _queryView = new CreatureQueryView(level.Database, _sources.Strings.Resolve, _viewer, new InstallClassicCreatureNames(install));
        _queryPanel = new ClassicCreatureQueryPanel { Name = "CreatureQuery" };
        _queryPanel.Open(_art.Font, _sources.Texture);
        AddChild(_queryPanel);
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
        // After every panel it covers, so it draws over them and takes the mouse first, as Unity's sorting order 15. Its
        // game options entry stays greyed: that page is another panel, not ported yet.
        _options = new ClassicOptionsPanel { Name = "Options" };
        _options.Open(_sources.Text, _art.Font);
        _options.SetSupported(ClassicOptionsAction.Save, true);
        _options.SetSupported(ClassicOptionsAction.Load, true);
        _options.Pressed += OptionsPressed;
        AddChild(_options);
        // Unity's runner lists Application.persistentDataPath/Saves unless savedSessionDirectory names another; Godot's
        // counterpart of that path is user://Saves. Listing only reads: nothing is created there.
        string savesDirectory = Argument(args, "--saves") ?? ProjectSettings.GlobalizePath("user://Saves");
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
        _hudHover = new HudHoverText(_sources.Strings.Resolve);
        // Unity's runner also passes the played campaign slot, which only world hover reads.
        _typeText = new WorldHoverText(level.Database, _sources.Strings.Resolve, _viewer);
        for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++) _simulation.Tick(InputFrame.Empty);
        if (Array.IndexOf(args, "--query-first-creature") >= 0)
            foreach (CreatureState creature in _simulation.World.OwnedCreatures(_viewer))
            {
                _queryView.Query(new CreatureQueryTarget(creature.Id));
                break;
            }
        if (Array.IndexOf(args, "--stage-battle") >= 0) StageBattle(_simulation);
        if (Array.IndexOf(args, "--open-options") >= 0)
        {
            ApplyHudAction(ClassicHudAction.ToggleOptions);
            // Only an entry the menu takes a press on: an unsupported one (game options) is ignored, as a click.
            if (Enum.TryParse(Argument(args, "--open-options"), out ClassicOptionsAction entry))
            {
                if (_options.Supported(entry)) OptionsPressed(entry);
                else GD.PushWarning("DK2.Godot: --open-options ignores " + entry + ", which the menu does not support.");
            }
        }
        GD.Print("DK2.Godot in-game HUD: ", levelName, " seed ", seed.ToString(CultureInfo.InvariantCulture), ", saves in ", savesDirectory);
    }

    public override void _Process(double delta)
    {
        if (_simulation == null || _view == null || _panel == null || _model == null || _resourceView == null
            || _resources == null || _messageView == null || _messages == null || _hover == null || _hudHover == null
            || _overviewView == null || _overview == null || _queryView == null || _queryPanel == null
            || _battles == null || _battleTab == null || _options == null || _saveLoad == null) return;
        // Engine time, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        // Once per settled turn, before the HUD draws, as DungeonRunner.UpdateWorldSound captures it.
        _battles.Capture(_simulation.World, _simulation.Database, _simulation.World.CurrentTick);
        // The options key, as DungeonRunner.UpdateHudControls reads it. Only the menu's toggle is applied: the other HUD
        // keys' panels and camera are not ported. AllowOptionsKey is always true: the scripted camera path that holds the
        // key back (DungeonRunner's pathWasPlaying) is not ported (plan step 2). No key is being captured: the controls
        // page is not ported.
        var visibility = new HudVisibility(true, true);
        ClassicHudAction shortcut = _keyboard.Read(_settings).HudShortcut(
            _hudControls.ShortcutContext(visibility, allowOptionsKey: true, capturingKey: false), _keyboard.EscapePressed);
        if (shortcut == ClassicHudAction.ToggleOptions) ApplyHudAction(shortcut);
        bool modal = _hudControls.Modal;
        _panel.SetVisibility(_hudControls.ModalInput(visibility));
        _options.SetVisible(visibility.ShowNormalHud && _hudControls.OptionsMenuVisible);
        _options.SetPage(_hudControls.OptionsPage, _hudControls.Confirmation);
        // DungeonRunner.DrawSavePanel's in-game branch, with this frame's typing (Unity reads Input.inputString there).
        (bool saveLoadShown, bool saving) = _hudControls.SaveLoadPage(visibility);
        _saveLoad.Show(_saves, saveLoadShown, saving, _keyboard.Typed);
        _keyboard.EndFrame();
        // The HUD draws the settled world, then the frame's ticks run (DungeonRunner's order).
        long milliseconds = (long)(_elapsed * 1000);
        // displayHealth follows the normal HUD's visibility in Unity; the scripted view that hides it is not wired.
        _resourceFacts = _resourceView.Capture(_simulation.World);
        _resources.Draw(_resourceFacts, unchecked((uint)milliseconds));
        _view.Capture(_simulation.World);
        _model.GamePaused = _hudControls.TicksPaused;
        _panel.Draw(milliseconds);
        _messageView.Capture(_simulation.World, milliseconds);
        // No sound player yet: drop the requests so they do not pile up.
        _model.Sounds.Clear(); _view.Sounds.Clear(); _messageView.Sounds.Clear(); _messageView.Speech.Clear();
        _hudControls.Sounds.Clear();
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
        // After the overview, as DungeonRunner.DrawHudControls draws it (the minimap and full map are not ported).
        _options.Draw();
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
        _pacer.Accrue(Math.Min(delta, MaximumFrameSeconds), 1f, _settings.GameSpeed);
        int ticks = _pacer.Take(MaxTicksPerFrame);
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

    /// <summary>Menu 43's presses, as DungeonRunner.BattleTabClicked maps them (<see cref="BattleTabView.Action"/>). Only the
    /// cancel's left click acts here; the zoom and the two glides need the camera (plan step 3), and the runner's
    /// suppressed-input gate is not wired (the modal gate is <see cref="ClassicHudControls.ModalInput"/> on the tabs' visibility).</summary>
    private void BattleTabClicked(BattleTabButton button, int mouse)
    {
        if (BattleTabView.Action(button, mouse) == BattleTabAction.CancelCallToArms) _pending.Add(new RemoveCallToArmsCommand(_viewer));
    }

    /// <summary>DungeonRunner.ApplyHudAction for the actions this scene applies: the options key, the options menu's
    /// entries and the save browser's Back and Continue lines. The camera's and the minimap's actions (navigation, zoom, the query and sell corners) are not applied.</summary>
    private void ApplyHudAction(ClassicHudAction action)
    {
        if (action == ClassicHudAction.None || _hudControls.InputSuppressed) return;
        _input.CancelPointerGesture(); _panel?.CancelPointerGesture();
        _hudControls.Apply(action);
    }

    /// <summary>A press on an options menu entry, as DungeonRunner.QueueHudInput applies it
    /// (<see cref="OptionsHud.HudAction"/>), the save list reread first for Save and Load. The session a confirmed restart,
    /// return or quit asks for is taken and dropped: loading a level and leaving the game are the runner's (plan step 2).
    /// Game Options is unsupported here, so the menu never raises it and <c>--open-options</c> refuses it.</summary>
    private void OptionsPressed(ClassicOptionsAction action)
    {
        if (OptionsHud.RefreshesSaves(action)) _saves?.Refresh();
        ApplyHudAction(OptionsHud.HudAction(action));
        ClassicSessionAction session = _hudControls.TakeSessionAction();
        if (session != ClassicSessionAction.None) GD.Print("DK2.Godot: the options menu's ", session.ToString(), " is not wired yet.");
    }

    /// <summary>The save browser's Enter, or a press on its name or Load line. Unity's runner writes the session there
    /// (DungeonRunner.SaveSession: the campaign profile and its carry, the header, the world snapshot) or rebuilds the world
    /// and the session from the chosen save (LoadSession); both need the runner's session and campaign profile, so the
    /// request is dropped here (plan step 2), and nothing is written.</summary>
    private void SaveLoadSubmitted() =>
        GD.Print("DK2.Godot: the ", _hudControls.OptionsPage.ToString(), " page's submit is not wired yet.");

    /// <summary>DungeonRunner.SelectPanelChoice's pointer resets and its query clear; the tool it then sets lives in the
    /// runner (plan step 2).</summary>
    private void SelectPanelChoice(BottomPanelSelection selection)
    {
        _input.CancelPointerGesture();
        _input.CancelTool();
        _queryView?.Clear();
    }

    /// <summary>A stored special's slot: action 88, or targeting with the panel back on the hand.</summary>
    private void ActivateStoredSpecial(BottomPanelChoice special)
    {
        if (_view != null && _input.ChooseStoredSpecial(_viewer, special, _view) is PlayerCommand command) _pending.Add(command);
    }

    /// <summary>Action 0x72, a GUI button (ADR-0670).</summary>
    private void PostGuiButton(GuiButtonTarget target) => _pending.Add(new GuiButtonCommand(_viewer, target));

    /// <summary>The panels this scene has, as DungeonRunner.PointerOverHud asks its own (the minimap and full map are not
    /// ported).</summary>
    private bool PointerOverHud(Vector2 position, HudVisibility visibility) =>
        (_hover != null && _hover.ContainsPointer(position)) || (_resources != null && _resources.ContainsPointer(position))
        || (_panel != null && _panel.ContainsPointer(position)) || (_messages != null && _messages.ContainsPointer(position))
        || (_battleTab != null && _battleTab.ContainsPointer(position))
        || (_overview != null && _overview.ContainsPointer(position))
        || (_options != null && _options.ContainsPointer(position))
        || (_queryPanel != null && _queryView != null && _queryView.Target != CreatureQueryTarget.None
            && _queryPanel.ContainsPointer(position, visibility));

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
    ClassicHudAction IHudHoverSources.MinimapControl() => ClassicHudAction.None;
    KeeperResourceKind IHudHoverSources.Resource() => _resources?.ResourceAt(_pointer) ?? KeeperResourceKind.None;

    private static string? Argument(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    /// <summary>Every key event reaches the keyboard, as Unity's <c>Input.GetKey</c> reads the keys whatever the UI holds.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key) _keyboard.Feed(key);
    }

    public override void _Notification(int what)
    {
        // A window without focus misses the releases.
        if (what == NotificationApplicationFocusOut) { _keyboard.ReleaseAll(); return; }
        // Unity's Input.mousePosition leaves the screen with the pointer; Godot stops reporting it instead.
        if (what == NotificationWMMouseExit) { _pointerInWindow = false; return; }
        if (what == NotificationWMMouseEnter) { _pointerInWindow = true; return; }
        // Freed, not merely removed from the tree: a reparented scene keeps its level.
        if (what != NotificationPredelete) return;
        if (_model != null)
        {
            _model.Selected -= SelectPanelChoice; _model.SpecialChosen -= ActivateStoredSpecial; _model.GuiButton -= PostGuiButton;
        }
        if (_battleTab != null) _battleTab.Clicked -= BattleTabClicked;
        if (_options != null) _options.Pressed -= OptionsPressed;
        if (_saveLoad != null)
        {
            _saveLoad.Submit -= SaveLoadSubmitted; _saveLoad.HudAction -= ApplyHudAction;
        }
        _battleTab = null; _battles = null; _options = null; _saveLoad = null; _saves = null;
        _art?.Dispose(); _sources?.Dispose();
        _hover = null; _hudHover = null; _overview = null; _overviewView = null; _typeText = null; _queryPanel = null; _queryView = null;
        _panel = null; _model = null; _view = null; _messages = null; _messageView = null; _resources = null; _resourceView = null; _art = null; _sources = null; _simulation = null;
    }
}
