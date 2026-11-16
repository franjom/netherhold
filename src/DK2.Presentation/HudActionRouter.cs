// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>What a HUD action asks of the adapter: the camera's navigation, the minimap's zoom, and the GUI button
/// command (action 0x72, ADR-0670) the adapter posts.</summary>
public interface IHudActionHost
{
    void NavigatePortal();
    void NavigateHeart();
    void NavigateFight();
    /// <summary>The magnifier's press under the pointer (MinimapZoom), after its GUI button is posted.</summary>
    void ZoomMinimap(bool zoomIn, bool zoomOut);
    void PostGuiButton(GuiButtonTarget target);
}

/// <summary>A HUD action applied as Unity's DungeonRunner.ApplyHudAction applied it: behind
/// <see cref="ClassicHudControls.Accepts"/>, the world drag and the bottom panel's press are dropped; navigation goes to
/// the host, zoom and the query and sell corners post their GUI button first (<see cref="ClassicHudControlLayout.TryGuiButton"/>),
/// the corners then set the tool (<see cref="HudToolState.SelectCorner"/>), and every other action is the controls'.</summary>
public sealed class HudActionRouter
{
    private readonly ClassicHudControls _controls;
    private readonly HudToolState _tools;
    private readonly PlayerInputTranslator _input;
    private readonly IHudActionHost _host;

    public HudActionRouter(ClassicHudControls controls, HudToolState tools, PlayerInputTranslator input, IHudActionHost host)
    {
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <param name="bottomPanel">The adapter's bottom panel, whose press is cancelled; null when it has none.</param>
    /// <param name="panel">The bottom panel's model, which shows a corner's tool; needed only for the corners.</param>
    public void Apply(ClassicHudAction action, IPointerGestureHolder? bottomPanel, BottomPanelView? panel)
    {
        if (!_controls.Accepts(action)) return;
        _input.CancelPointerGesture(); bottomPanel?.CancelPointerGesture();
        switch (action)
        {
            case ClassicHudAction.NavigatePortal: _host.NavigatePortal(); return;
            case ClassicHudAction.NavigateHeart: _host.NavigateHeart(); return;
            case ClassicHudAction.NavigateFight: _host.NavigateFight(); return;
            case ClassicHudAction.ZoomIn:
            case ClassicHudAction.ZoomOut:
                // The magnifier's primary or secondary click, 0x00416511/0x00416611.
                if (ClassicHudControlLayout.TryGuiButton(action, out GuiButtonTarget zoomButton)) _host.PostGuiButton(zoomButton);
                _host.ZoomMinimap(action == ClassicHudAction.ZoomIn, action == ClassicHudAction.ZoomOut);
                return;
            case ClassicHudAction.SelectQuery:
            case ClassicHudAction.SelectSell:
                if (ClassicHudControlLayout.TryGuiButton(action, out GuiButtonTarget toolButton)) _host.PostGuiButton(toolButton);
                _tools.SelectCorner(action, _input, panel!);
                return;
        }
        _controls.Apply(action);
    }
}
