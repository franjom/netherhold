// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The in-game options menu over the paused level, the Godot counterpart of Unity's <c>ClassicOptionsPanel</c>:
/// <see cref="OptionsHud"/> (ADR-0805) decides every draw and hit, and a <see cref="HudCanvas"/> draws its frame over the
/// whole screen at the screen's own scale (1920 virtual units high; the HUD scale does not apply, as in Unity). While it
/// shows it takes every mouse press from the panels under it; the host's <see cref="HudPressRouter"/> asks
/// <see cref="ActionAt"/> on a left press, and what it does is <see cref="OptionsHud.HudAction"/>.</summary>
public partial class ClassicOptionsPanel : Node
{
    /// <summary>The canvas's image source: the menu draws set text only, as Unity's facade loads nothing.</summary>
    private static readonly RgbaImage BlankImage = new(1, 1, new byte[4]);
    private static readonly Func<string, RgbaImage> Blank = _ => BlankImage;
    private readonly HudFrame _frame = new();
    private OptionsHud? _hud;
    private HudCanvas? _canvas;
    /// <summary>Local units per virtual unit (the screen height over 1920) at the last draw; 0 before it.</summary>
    private float _scale;

    /// <summary>Opens the menu, hidden; call before it enters the tree.</summary>
    /// <param name="text">A <c>Text.str</c> string by id, empty when missing.</param>
    /// <param name="font">The legible bitmap font for a screen height.</param>
    public void Open(Func<uint, string> text, Func<int, BitmapFont> font)
    {
        if (_hud != null) throw new InvalidOperationException("The options menu is already open.");
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (font is null) throw new ArgumentNullException(nameof(font));
        // No catalogue art to preload: the labels are set from the font when first drawn at a screen height.
        _hud = new OptionsHud(text, font);
        _canvas = new HudCanvas { Name = "HudCanvas", Load = Blank, Visible = false };
        AddChild(_canvas);
    }

    /// <summary>An unsupported action's entry is greyed and takes no press.</summary>
    public void SetSupported(ClassicOptionsAction action, bool supported) => _hud?.SetSupported(action, supported);

    /// <summary>Whether an entry takes a press (<see cref="OptionsHud.Supported"/>).</summary>
    public bool Supported(ClassicOptionsAction action) => _hud != null && _hud.Supported(action);

    public void SetVisible(bool visible)
    {
        if (_hud == null || _canvas == null) return;
        _hud.SetVisible(visible); _canvas.Visible = visible;
    }

    public void SetPage(ClassicOptionsPage page, ClassicSessionAction confirmation) => _hud?.SetPage(page, confirmation);

    /// <summary>Whether the menu shows: it holds the whole screen, as Unity's facade answers.</summary>
    public bool ContainsPointer(Vector2 screenPosition) => _hud != null && _hud.Visible;

    /// <summary>Lays the page out for the viewport and presents it, the entry under the pointer lit.</summary>
    public void Draw()
    {
        if (_hud == null || _canvas == null || !_hud.Visible) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        var screen = new HudScreen(width, height);
        _scale = screen.Scale;
        (float x, float y) = ToVirtual(GetViewport().GetMousePosition());
        _frame.Clear();
        _hud.Build(screen, (x, y), _frame);
        _canvas.Present(_frame, Vector2.Zero, _scale, 1);
    }

    /// <summary>The supported entry under a screen pixel (Unity's facade's <c>ActionAt</c>), None off the entries or
    /// before the first draw.</summary>
    public ClassicOptionsAction ActionAt(Vector2 screenPosition)
    {
        if (_hud == null || _scale <= 0) return ClassicOptionsAction.None;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ActionAt(x, y);
    }

    /// <summary>Whether the host's router takes this frame's presses for the open menu (the live HUD state it routes on,
    /// not this node's drawn visibility, which updates only at the next draw); while it does, no panel under it gets one.</summary>
    public Func<bool>? HoldsPresses { get; set; }

    public override void _Input(InputEvent @event)
    {
        // Unity's runner gives every frame to the open menu (QueueHudInput): the panels under it get no press. The router
        // reads the press from the window; this node and the router decide on the same state.
        if (@event is InputEventMouseButton { Pressed: true } && HoldsPresses?.Invoke() == true) GetViewport().SetInputAsHandled();
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented menu keeps working.
        if (what == NotificationPredelete) { HoldsPresses = null; _hud = null; _canvas = null; }
    }

    /// <summary>A screen pixel (top-left origin) in virtual units: Unity's <c>Virtual</c>, 1920 over the screen height.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen) => (screen.X / _scale, screen.Y / _scale);
}
