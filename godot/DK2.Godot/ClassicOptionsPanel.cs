// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the in-game options menu over the paused level at the screen's own scale. While open, it takes pointer presses before the panels underneath.</summary>
public partial class ClassicOptionsPanel : Node
{
    /// <summary>The canvas's image source. The menu draws set text only.</summary>
    private static readonly RgbaImage BlankImage = new RgbaImage(1, 1, new byte[4]);
    private static readonly Func<string, RgbaImage> Blank = _ => BlankImage;
    private readonly HudFrame _frame = new HudFrame();
    private OptionsHud? _hud;
    private HudCanvas? _canvas;
    /// <summary>Local units per virtual unit (the screen height over 1920) at the last draw; 0 before it.</summary>
    private float _scale;

    /// <summary>Opens the menu, hidden; call before it enters the tree.</summary>
    /// <param name="text">A <c>Text.str</c> string by id, empty when missing.</param>
    /// <param name="font">The legible bitmap font for a screen height.</param>
    public void Open(Func<uint, string> text, Func<int, BitmapFont> font)
    {
        if (_hud != null)
        {
            throw new InvalidOperationException("The options menu is already open.");
        }

        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (font is null)
        {
            throw new ArgumentNullException(nameof(font));
        }
        // No catalogue art to preload: the labels are set from the font when first drawn at a screen height.
        _hud = new OptionsHud(text, font);
        _canvas = new HudCanvas { Name = "HudCanvas", Load = Blank, Visible = false };
        AddChild(_canvas);
    }

    /// <summary>An unsupported action's entry is greyed and takes no press.</summary>
    public void SetSupported(ClassicOptionsAction action, bool supported)
    {
        _hud?.SetSupported(action, supported);
    }

    /// <summary>Whether an entry takes a press (<see cref="OptionsHud.Supported"/>).</summary>
    public bool Supported(ClassicOptionsAction action)
    {
        return _hud != null && _hud.Supported(action);
    }

    public void SetVisible(bool visible)
    {
        if (_hud == null || _canvas == null)
        {
            return;
        }

        _hud.SetVisible(visible); _canvas.Visible = visible;
    }

    public void SetPage(ClassicOptionsPage page, ClassicSessionAction confirmation)
    {
        _hud?.SetPage(page, confirmation);
    }

    /// <summary>Whether the full-screen options menu is open.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        return _hud != null && _hud.Visible;
    }

    /// <summary>Lays the page out for the viewport and presents it, the entry under the pointer lit.</summary>
    public void Draw()
    {
        if (_hud == null || _canvas == null || !_hud.Visible)
        {
            return;
        }

        if (HudCanvas.ScreenForViewport(GetViewport()) is not { } screen)
        {
            return;
        }
        _scale = screen.Scale;
        (float x, float y) = ToVirtual(GetViewport().GetMousePosition());
        _frame.Clear();
        _hud.Build(screen, (x, y), _frame);
        _canvas.Present(_frame, Vector2.Zero, _scale, 1);
    }

    /// <summary>The supported entry under a screen pixel, None off the entries or
    /// before the first draw.</summary>
    public ClassicOptionsAction ActionAt(Vector2 screenPosition)
    {
        if (_hud == null || _scale <= 0)
        {
            return ClassicOptionsAction.None;
        }

        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ActionAt(x, y);
    }

    /// <summary>Whether the host's router takes this frame's presses for the open menu (the live HUD state it routes on,
    /// not this node's drawn visibility, which updates only at the next draw); while it does, no panel under it gets one.</summary>
    public Func<bool>? HoldsPresses { get; set; }

    public override void _Input(InputEvent @event)
    {
        // While the options menu is open, panels beneath it get no presses. The router
        // reads the press from the window; this node and the router decide on the same state.
        if (@event is InputEventMouseButton { Pressed: true } && HoldsPresses?.Invoke() == true)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented menu keeps working.
        if (what == NotificationPredelete) { HoldsPresses = null; _hud = null; _canvas = null; }
    }

    /// <summary>Convert a screen pixel with top-left origin to virtual units, 1920 per screen height.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen)
    {
        return (screen.X / _scale, screen.Y / _scale);
    }
}
