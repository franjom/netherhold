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
/// <see cref="TickPacer"/> at normal speed, with the in-game HUD over a black screen and no 3D world. Ported so far
/// (migration plan step 1b): the resource strip, the bottom panel, the creature overview in its Creatures panel, the
/// message tabs and the hover HUD, whose status line and tooltip show what <see cref="HudHoverText"/> picks from the
/// panels under the pointer. Not wired, because their logic is still Unity's runner (plan step 2) or needs the 3D world
/// (step 3): the overview's pickup (the hand) and focus (the camera), driving the shared
/// <c>ScriptedViewDirector</c> (the level's opening camera path, HUD hiding, FLASH_BUTTON, the resource flash), the tool a
/// selection sets, camera glides for room, door and trap navigation and for a message's goto button, the options menu's
/// effect on the tabs, sound, world hover (things and tiles under the pointer) and the hand cursor. User arguments after
/// <c>--</c>: <c>--level &lt;name&gt;</c>, <c>--seed &lt;n&gt;</c>.</summary>
public partial class InGameHud : Node, IHudHoverSources
{
    /// <summary>DungeonRunner's <c>maxTicksPerFrame</c> in the Dungeon scene.</summary>
    private const int MaxTicksPerFrame = 2;
    /// <summary>Unity's Time.maximumDeltaTime in this project (ProjectSettings/TimeManager.asset).</summary>
    private const double MaximumFrameSeconds = 1.0 / 3.0;
    private readonly List<PlayerCommand> _pending = new();
    private readonly PlayerInputTranslator _input = new();
    private readonly TickPacer _pacer = new();
    private readonly int _gameSpeed = new GameSettings().GameSpeed;
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
        // Its bar's canvas draws under the panels and its text's over them by z index (ClassicHoverHud), not tree order.
        _hover = new ClassicHoverHud { Name = "HoverHud" };
        _hover.Open(_sources);
        AddChild(_hover);
        _hudHover = new HudHoverText(_sources.Strings.Resolve);
        // Unity's runner also passes the played campaign slot, which only world hover reads.
        _typeText = new WorldHoverText(level.Database, _sources.Strings.Resolve, _viewer);
        for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++) _simulation.Tick(InputFrame.Empty);
        GD.Print("DK2.Godot in-game HUD: ", levelName, " seed ", seed.ToString(CultureInfo.InvariantCulture));
    }

    public override void _Process(double delta)
    {
        if (_simulation == null || _view == null || _panel == null || _model == null || _resourceView == null
            || _resources == null || _messageView == null || _messages == null || _hover == null || _hudHover == null
            || _overviewView == null || _overview == null) return;
        // Engine time, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        // The HUD draws the settled world, then the frame's ticks run (DungeonRunner's order).
        long milliseconds = (long)(_elapsed * 1000);
        // displayHealth follows the normal HUD's visibility in Unity; the scripted view that hides it is not wired.
        _resourceFacts = _resourceView.Capture(_simulation.World);
        _resources.Draw(_resourceFacts, unchecked((uint)milliseconds));
        _view.Capture(_simulation.World);
        _panel.Draw(milliseconds);
        _messageView.Capture(_simulation.World, milliseconds);
        // No sound player yet: drop the requests so they do not pile up.
        _model.Sounds.Clear(); _view.Sounds.Clear(); _messageView.Sounds.Clear(); _messageView.Speech.Clear();
        // Visible and taking input: the scripted view and the options menu that would hide or hold the tabs are not wired.
        var visibility = new HudVisibility(true, true);
        _messages.Draw(visibility, _model.Collapsed,
            HudIndicatorSnapshot.Capture(_simulation.World, _simulation.Database.Level, _viewer));
        // After the tabs, as DungeonRunner draws it; the options menu and full map that would close it are not wired.
        _overviewView.Capture(_simulation.World);
        _overview.Draw(visibility, _model.PanelArea(BottomPanelCategory.Creatures));
        // The hover, after the HUD is drawn (DungeonRunner.CaptureWorldHover): over the HUD, the panels' text; elsewhere
        // nothing, because world hover needs the 3D world.
        _hover.SetVisibility(visibility, milliseconds);
        _pointer = GetViewport().GetMousePosition();
        bool inside = _pointerInWindow && GetViewport().GetVisibleRect().HasPoint(_pointer);
        bool allowed = visibility.AllowOrdinaryInput && GetWindow().HasFocus() && inside;
        HoverTextSnapshot hover = allowed && PointerOverHud(_pointer) ? _hudHover.Capture(this, _resourceFacts) : HoverTextSnapshot.Empty;
        _hover.Draw(hover, _pointer, _cursorReach, milliseconds);

        // Clamped as Unity's runner clamps it (Time.maximumDeltaTime, 1/3 s), so a stall is not paid back at the cap.
        _pacer.Accrue(Math.Min(delta, MaximumFrameSeconds), 1f, _gameSpeed);
        int ticks = _pacer.Take(MaxTicksPerFrame);
        InputFrame first = TickPacer.FirstInput(ticks, _pending);
        if (first.Commands != null) _pending.Clear();
        for (int i = 0; i < ticks; i++) _simulation.Tick(TickPacer.InputFor(i, first));
    }

    /// <summary>DungeonRunner.SelectPanelChoice's pointer resets; the tool it then sets lives in the runner (plan step 2).</summary>
    private void SelectPanelChoice(BottomPanelSelection selection)
    {
        _input.CancelPointerGesture();
        _input.CancelTool();
    }

    /// <summary>A stored special's slot: action 88, or targeting with the panel back on the hand.</summary>
    private void ActivateStoredSpecial(BottomPanelChoice special)
    {
        if (_view != null && _input.ChooseStoredSpecial(_viewer, special, _view) is PlayerCommand command) _pending.Add(command);
    }

    /// <summary>Action 0x72, a GUI button (ADR-0670).</summary>
    private void PostGuiButton(GuiButtonTarget target) => _pending.Add(new GuiButtonCommand(_viewer, target));

    /// <summary>The panels this scene has, as DungeonRunner.PointerOverHud asks its own (the minimap, the battle tab, the
    /// creature query, the options and full map are not ported).</summary>
    private bool PointerOverHud(Vector2 position) =>
        (_hover != null && _hover.ContainsPointer(position)) || (_resources != null && _resources.ContainsPointer(position))
        || (_panel != null && _panel.ContainsPointer(position)) || (_messages != null && _messages.ContainsPointer(position))
        || (_overview != null && _overview.ContainsPointer(position));

    // The panels under the pointer for HudHoverText, answered as Unity's runner answers; the ones not ported give the
    // empty answer.
    int IHudHoverSources.OverviewTextId() => _overview?.HoveredTextId(_pointer) ?? 0;
    CreatureOverviewType? IHudHoverSources.OverviewColumn() => _overview?.HoveredColumnType(_pointer);
    HoverTextSnapshot IHudHoverSources.CreatureTypeText(CreatureOverviewType column) =>
        _typeText != null && _simulation != null ? _typeText.CreatureType(_simulation.Database.CreatureById(column.Value)) : HoverTextSnapshot.Empty;
    bool IHudHoverSources.OverMessageTabs() => _messages != null && _messages.ContainsPointer(_pointer);
    HoverTextSnapshot IHudHoverSources.MessageTabsText() => _messages?.HoverText ?? HoverTextSnapshot.Empty;
    bool IHudHoverSources.OverBattleTab() => false;
    HoverTextSnapshot IHudHoverSources.BattleTabText() => HoverTextSnapshot.Empty;
    bool IHudHoverSources.OverBottomPanel() => _panel != null && _panel.ContainsPointer(_pointer);
    HoverTextSnapshot IHudHoverSources.BottomPanelText() => _panel?.HoverText ?? HoverTextSnapshot.Empty;
    ClassicHudAction IHudHoverSources.MinimapControl() => ClassicHudAction.None;
    KeeperResourceKind IHudHoverSources.Resource() => _resources?.ResourceAt(_pointer) ?? KeeperResourceKind.None;

    private static string? Argument(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    public override void _Notification(int what)
    {
        // Unity's Input.mousePosition leaves the screen with the pointer; Godot stops reporting it instead.
        if (what == NotificationWMMouseExit) { _pointerInWindow = false; return; }
        if (what == NotificationWMMouseEnter) { _pointerInWindow = true; return; }
        // Freed, not merely removed from the tree: a reparented scene keeps its level.
        if (what != NotificationPredelete) return;
        if (_model != null)
        {
            _model.Selected -= SelectPanelChoice; _model.SpecialChosen -= ActivateStoredSpecial; _model.GuiButton -= PostGuiButton;
        }
        _art?.Dispose(); _sources?.Dispose();
        _hover = null; _hudHover = null; _overview = null; _overviewView = null; _typeText = null;
        _panel = null; _model = null; _view = null; _messages = null; _messageView = null; _resources = null; _resourceView = null; _art = null; _sources = null; _simulation = null;
    }
}
