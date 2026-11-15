// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The creature query details over the bottom panel, the Godot counterpart of Unity's
/// <c>ClassicCreatureQueryPanel</c>: <see cref="CreatureQueryHud"/> (ADR-0805) decides every draw, and a
/// <see cref="HudCanvas"/> draws its frame bottom-docked under the HUD scale. It takes no input; a host asks
/// <see cref="ContainsPointer"/> to keep the pointer from the world.</summary>
public partial class ClassicCreatureQueryPanel : Node
{
    private readonly HudFrame _frame = new();
    private CreatureQueryHud? _hud;
    private HudCanvas? _canvas;
    private HudScaleLayout _layout;
    private float _scale = 1;
    /// <summary>Screen pixel of the canvas's local (0, 0), rounded as drawn; the pointer is measured from it.</summary>
    private Vector2 _origin;

    /// <summary>The HUD scale percent (Unity's <c>UiScale</c>, the graphics setting).</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>Opens the details; call before it enters the tree.</summary>
    /// <param name="font">The legible bitmap font for a screen height.</param>
    /// <param name="image">A HUD catalogue image by name (the scroll bar's parts).</param>
    public void Open(Func<int, BitmapFont> font, Func<string, RgbaImage> image)
    {
        if (_hud != null) throw new InvalidOperationException("The creature query details are already open.");
        if (font is null) throw new ArgumentNullException(nameof(font));
        if (image is null) throw new ArgumentNullException(nameof(image));
        var hud = new CreatureQueryHud(font);
        var canvas = new HudCanvas { Name = "HudCanvas", Load = image, Visible = false };
        // Decoded now, as Unity's facade does, and before the canvas is kept, so a failed decode leaves the details
        // unopened and retryable.
        try { foreach (HudImage part in CreatureQueryHud.Images()) canvas.Preload(part); }
        catch { canvas.Free(); throw; }
        _hud = hud; _canvas = canvas;
        AddChild(_canvas);
    }

    /// <summary>Lays the details out for the viewport and presents them; nothing shows for an empty snapshot.</summary>
    /// <param name="otherPanelOpen">A modal panel is open, which hides the details.</param>
    public void Draw(CreatureQuerySnapshot snapshot, HudVisibility visibility, bool otherPanelOpen = false)
    {
        if (_hud == null || _canvas == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        _layout = new HudScaleLayout(width, height, UiScale);
        HudScreen screen = _layout.Screen;
        _scale = screen.Scale;
        // Bottom-docked (ClassicHudScaling.Apply), as the bottom panel: the panel's bottom stays on the screen's bottom.
        _origin = new Vector2(0, _layout.BottomOriginPixels);
        _frame.Clear();
        _hud.Build(screen, snapshot, visibility, otherPanelOpen, _frame);
        _canvas.Visible = _frame.Count > 0;
        _canvas.Present(_frame, _origin, _scale, _layout.Factor);
    }

    /// <summary>Whether the last drawn details showed and a screen pixel (top-left origin) is over them.</summary>
    public bool ContainsPointer(Vector2 screenPosition, HudVisibility visibility)
    {
        if (_hud == null || _layout.Height <= 0) return false;
        // Unity's LocalPointer then ToVirtual, from the same rounded origin the drawing uses.
        float x = (screenPosition.X - _origin.X) / _layout.Factor / _scale, y = (screenPosition.Y - _origin.Y) / _layout.Factor / _scale;
        return _hud.ContainsPointer(x, y, visibility);
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented panel keeps working.
        if (what == NotificationPredelete) { _hud = null; _canvas = null; }
    }
}
