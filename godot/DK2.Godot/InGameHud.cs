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
/// <see cref="TickPacer"/> at normal speed, with the in-game HUD over a black screen and no 3D world. Only the bottom
/// panel is ported so far (migration plan step 1b). Not wired, because their logic is still Unity's runner (plan
/// step 2): the scripted view (the level's opening camera path, HUD hiding, FLASH_BUTTON), the tool a selection sets,
/// camera glides for room, door and trap navigation, and sound. User arguments after <c>--</c>:
/// <c>--level &lt;name&gt;</c>, <c>--seed &lt;n&gt;</c>.</summary>
public partial class InGameHud : Node
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
    private ClassicBottomPanel? _panel;
    private BottomPanelHud? _model;
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
        _view = new BottomPanelView(level.Database, _viewer, _art.Name, _art.Order, campaignMode: true);
        _panel = new ClassicBottomPanel { Name = "BottomPanel" };
        _panel.Open(_art, _view);
        AddChild(_panel);
        _panel.OpenAtLevelStart();
        _model = _panel.Model;
        _model.Selected += SelectPanelChoice;
        _model.SpecialChosen += ActivateStoredSpecial;
        _model.GuiButton += PostGuiButton;
        for (int i = 0; i < CampaignLevelData.TicksBeforeFirstFrame; i++) _simulation.Tick(InputFrame.Empty);
        GD.Print("DK2.Godot in-game HUD: ", levelName, " seed ", seed.ToString(CultureInfo.InvariantCulture));
    }

    public override void _Process(double delta)
    {
        if (_simulation == null || _view == null || _panel == null || _model == null) return;
        // Engine time, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        // The HUD draws the settled world, then the frame's ticks run (DungeonRunner's order).
        _view.Capture(_simulation.World);
        _panel.Draw((long)(_elapsed * 1000));
        // No sound player yet: drop the requests so they do not pile up.
        _model.Sounds.Clear(); _view.Sounds.Clear();

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

    private static string? Argument(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented scene keeps its level.
        if (what != NotificationPredelete) return;
        if (_model != null)
        {
            _model.Selected -= SelectPanelChoice; _model.SpecialChosen -= ActivateStoredSpecial; _model.GuiButton -= PostGuiButton;
        }
        _art?.Dispose(); _sources?.Dispose();
        _panel = null; _model = null; _view = null; _art = null; _sources = null; _simulation = null;
    }
}
