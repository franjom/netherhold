// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the frame built by <see cref="HoverHud"/> (ADR-0805). The bar lies beneath other panels and the status line and tooltip lie above them. The host checks whether the pointer is over the bar.</summary>
public partial class ClassicHoverHud : Node
{
    private readonly HudFrame _frame = new(), _barFrame = new();
    private HoverHud? _hud;
    private HudCanvas? _canvas, _barCanvas;
    private HudScaleLayout _layout;

    /// <summary>The HUD scale percent.</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>Whether a creature's name pops up (the options' creature tooltip setting).</summary>
    public bool ShowCreatureTooltip
    {
        get
        {
            return _hud?.ShowCreatureTooltip ?? true;
        }

        set
        {
            if (_hud != null)
            {
                _hud.ShowCreatureTooltip = value;
            }
        }
    }

    /// <summary>Opens the hover HUD over <paramref name="sources"/>, which the caller disposes after this node;
    /// call before it enters the tree. Its fonts are read from the sources as the screen height picks them.</summary>
    public void Open(InstallHudSources sources)
    {
        if (_hud != null)
        {
            throw new InvalidOperationException("The hover HUD is already open.");
        }

        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }
        // Read the status bar image once for its size and texture.
        RgbaImage bar = sources.Texture(HoverHud.Bar.Path!);
        string barPath = HoverHud.Bar.Path!;
        Func<string, RgbaImage> load = path => path == barPath ? bar : sources.Texture(path);
        var canvas = new HudCanvas { Name = "HudCanvas", Load = load, ZIndex = 1 };
        var barCanvas = new HudCanvas { Name = "BarCanvas", Load = load, ZIndex = -1 };
        // Decoded now, and before the canvases are kept, so a failed decode leaves the HUD
        // unopened and retryable.
        try
        {
            foreach (HudImage image in HoverHud.Images())
            {
                if (image == HoverHud.Bar || image == HoverHud.Cap)
                {
                    barCanvas.Preload(image);
                }
                else
                {
                    canvas.Preload(image);
                }
            }
        }
        catch { canvas.Free(); barCanvas.Free(); throw; }
        _hud = new HoverHud(sources.Font, (bar.Width, bar.Height));
        _canvas = canvas; _barCanvas = barCanvas;
        AddChild(_barCanvas);
        AddChild(_canvas);
    }

    /// <summary>Hides or shows both frames; hiding also resets the tooltip's wait (<see cref="HoverHud.SetVisibility"/>).</summary>
    /// <param name="milliseconds">Unscaled time, as <see cref="Draw"/>'s.</param>
    public void SetVisibility(HudVisibility visibility, long milliseconds)
    {
        if (_hud == null || _canvas == null || _barCanvas == null)
        {
            return;
        }

        _hud.SetVisibility(visibility, milliseconds);
        _canvas.Visible = _barCanvas.Visible = visibility.ShowNormalHud;
    }

    /// <summary>Whether a screen pixel (top-left origin) is over the bar as last drawn, which keeps it from the world.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0)
        {
            return false;
        }

        HudScreen screen = _layout.Screen;
        return _hud.ContainsPointer(screen.Virtual(screenPosition.X / _layout.Factor), screen.Virtual(screenPosition.Y / _layout.Factor));
    }

    /// <summary>Lays the HUD out for the viewport and presents it.</summary>
    /// <param name="snapshot">What the status line and tooltip show.</param>
    /// <param name="cursor">The pointer in screen pixels from the top left.</param>
    /// <param name="cursorReach">How far the drawn cursor reaches right of the pointer, in DK2's 640x480 units
    /// (<see cref="HandCursor.Reach"/>); the tooltip sits 18 beyond it.</param>
    /// <param name="milliseconds">Unscaled time: the status line's scroll and the tooltip's wait run on it.</param>
    public void Draw(in HoverTextSnapshot snapshot, Vector2 cursor, float cursorReach, long milliseconds)
    {
        if (_hud == null || _canvas == null || _barCanvas == null)
        {
            return;
        }

        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _layout = new HudScaleLayout(width, height, UiScale);
        HudScreen screen = _layout.Screen;
        float toVirtual = _layout.Factor * screen.Scale;
        _frame.Clear(); _barFrame.Clear();
        _hud.Build(screen, height / toVirtual, snapshot, (cursor.X / toVirtual, cursor.Y / toVirtual), cursorReach,
            milliseconds, _barFrame, _frame);
        // Top-docked: local (0, 0) is the top-left screen pixel.
        _barCanvas.Present(_barFrame, Vector2.Zero, screen.Scale, _layout.Factor);
        _canvas.Present(_frame, Vector2.Zero, screen.Scale, _layout.Factor);
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented HUD keeps working.
        if (what == NotificationPredelete) { _hud = null; _canvas = null; _barCanvas = null; }
    }
}
