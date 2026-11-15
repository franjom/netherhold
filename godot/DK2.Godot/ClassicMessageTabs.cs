// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The message tabs above the bottom panel, the message panel they open, the level's indicators and the
/// objective panel: the Godot counterpart of Unity's <c>ClassicMessageTabs</c>. <see cref="MessageTabsHud"/> (ADR-0805)
/// decides every draw and click, a <see cref="HudCanvas"/> draws its frame bottom-docked under the HUD scale, and this
/// node forwards the mouse in virtual units (1920 high). Like Unity's, clicks act on the press, and the message panel's
/// scroll bar polls the pointer and the left button each draw.</summary>
public partial class ClassicMessageTabs : Node
{
    private readonly HudFrame _frame = new();
    private MessageTabsHud? _hud;
    private HudCanvas? _canvas;
    private HudScaleLayout _layout;
    private float _scale = 1;
    /// <summary>Screen pixel of the canvas's local (0, 0), rounded as drawn; the pointer is measured from it.</summary>
    private Vector2 _origin;

    /// <summary>The HUD scale percent (Unity's <c>UiScale</c>, the graphics setting); 100 while the options menu is open.</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>Opens the tabs over <paramref name="art"/>, which the caller disposes; call before it enters the tree.</summary>
    /// <param name="font">The legible bitmap font for a screen height.</param>
    /// <param name="resolve">A message string reference to its text (<see cref="MessageTextTables.Resolve(int)"/>).</param>
    /// <param name="briefing">A line of the level's briefing table, or null.</param>
    /// <param name="locate">A message's goto location in tiles, or null to hide the button.</param>
    /// <param name="goTo">Moves the view to a location in tiles.</param>
    /// <param name="playerName">A player's name, for the Current Holder indicator.</param>
    public void Open(InstallBottomPanelArt art, Func<int, BitmapFont> font, MessageTabsView view, Func<int, string?> resolve,
        Func<int, string?>? briefing, Func<PlayerMessage, (float X, float Y)?>? locate = null, Action<float, float>? goTo = null,
        Func<byte, string>? playerName = null)
    {
        if (_hud != null) throw new InvalidOperationException("The message tabs are already open.");
        if (art is null) throw new ArgumentNullException(nameof(art));
        var hud = new MessageTabsHud(art, view, font, resolve, briefing, locate, goTo, playerName);
        var canvas = new HudCanvas { Name = "HudCanvas", Load = art.Image };
        // Decoded now, as Unity's facade does, and before the canvas is kept, so a failed decode leaves the tabs
        // unopened and retryable.
        try { foreach (HudImage image in MessageTabsHud.Images()) canvas.Preload(image); }
        catch { canvas.Free(); throw; }
        _hud = hud; _canvas = canvas;
        AddChild(_canvas);
    }

    /// <summary>The hovered tab's message text, or the Review Objective button's own texts, for the status line.</summary>
    public HoverTextSnapshot HoverText => _hud?.HoverText ?? HoverTextSnapshot.Empty;

    /// <summary>Whether a screen pixel (top-left origin) is the panel's, so a host keeps it from the world.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return false;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ContainsPointer(x, y);
    }

    /// <summary>Lays the tabs out for the viewport and presents them.</summary>
    /// <param name="collapsed">Whether the bottom panel is collapsed, which drops the tabs with it.</param>
    /// <param name="indicators">The level's indicators this frame; any shown lifts the message panel.</param>
    public void Draw(HudVisibility visibility, bool collapsed, in HudIndicatorSnapshot indicators,
        bool optionsOpen = false, bool optionsObjective = false)
    {
        if (_hud == null || _canvas == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        _layout = new HudScaleLayout(width, height, optionsOpen ? 100 : UiScale);
        HudScreen screen = _layout.Screen;
        _scale = screen.Scale;
        _canvas.Visible = visibility.ShowNormalHud;
        // Bottom-docked (ClassicHudScaling.Apply), as the bottom panel: the panel's bottom stays on the screen's bottom.
        _origin = new Vector2(0, _layout.BottomOriginPixels);
        (float px, float py) = ToVirtual(GetViewport().GetMousePosition());
        _frame.Clear();
        _hud.Build(screen, width, visibility, collapsed, indicators,
            (px, py, Input.IsMouseButtonPressed(MouseButton.Left)), _frame, optionsOpen, optionsObjective);
        _canvas.Present(_frame, _origin, _scale, _layout.Factor);
        _hud.Repick();
    }

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || _layout.Height <= 0) return;
        if (@event is InputEventMouseMotion motion)
        {
            (float x, float y) = ToVirtual(motion.Position);
            _hud.PointerMove(x, y);
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        int index = button.ButtonIndex switch { MouseButton.Left => 0, MouseButton.Right => 1, MouseButton.Middle => 2, _ => -1 };
        if (index < 0) return;
        (float bx, float by) = ToVirtual(button.Position);
        if (_hud.PointerDown(bx, by, index)) GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMMouseExit) _hud?.PointerLeave();
        // Freed, not merely removed from the tree: reparented tabs keep working.
        else if (what == NotificationPredelete) { _hud = null; _canvas = null; }
    }

    /// <summary>A screen pixel in the model's virtual units (Unity's <c>LocalPointer</c> then <c>ToVirtual</c>), from the
    /// same rounded origin the drawing uses, so hits land on the art at any HUD scale.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen) =>
        ((screen.X - _origin.X) / _layout.Factor / _scale, (screen.Y - _origin.Y) / _layout.Factor / _scale);
}
