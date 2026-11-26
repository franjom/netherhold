// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the top-docked resource strip from <see cref="KeeperResourceHud"/> (ADR-0805). The host tests pointer coverage; the strip has no input handlers.</summary>
public partial class ClassicResourceHud : Node
{
    private readonly KeeperResourceHud _hud = new();
    private readonly HudFrame _frame = new();
    private HudCanvas? _canvas;
    private HudScaleLayout _layout;

    /// <summary>The HUD scale percent.</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>The strip's lower edge in local pixels, as last laid out; before the first draw, laid out for the current
    /// viewport.</summary>
    public float Bottom
    {
        get
        {
            if (_layout.Height > 0 || !IsInsideTree())
            {
                return KeeperResourceHud.Bottom(_layout.Screen);
            }

            Vector2 size = GetViewport().GetVisibleRect().Size;
            return KeeperResourceHud.Bottom(new HudScaleLayout((int)size.X, (int)size.Y, UiScale).Screen);
        }
    }

    /// <summary>Opens the strip over <paramref name="sources"/>, which the caller disposes; call before it enters the tree.</summary>
    public void Open(InstallHudSources sources)
    {
        if (_canvas != null)
        {
            throw new InvalidOperationException("The strip is already open.");
        }

        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var canvas = new HudCanvas { Name = "HudCanvas", Load = sources.Texture };
        // Decoded now rather than by the first draw with the HUD visible, which comes after the opening camera path
        // (ADR-0688); before the canvas is kept, so a failed decode leaves the strip unopened and retryable.
        try { foreach (HudImage image in KeeperResourceHud.Images())
            {
                canvas.Preload(image);
            }
        }
        catch { canvas.Free(); throw; }
        _canvas = canvas;
        AddChild(_canvas);
    }

    public void SetVisibility(HudVisibility visibility)
    {
        _hud.SetVisibility(visibility);
        if (_canvas != null)
        {
            _canvas.Visible = visibility.ShowNormalHud;
        }
    }

    /// <summary>Lays the strip out for the viewport and presents it; <paramref name="milliseconds"/> drives its count-up
    /// and flashes on the unscaled clock.</summary>
    public void Draw(KeeperResourceSnapshot snapshot, uint milliseconds, KeeperResourceFlash flash = KeeperResourceFlash.None)
    {
        if (_canvas == null)
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
        _frame.Clear();
        _hud.Build(screen, snapshot, milliseconds, flash, _frame);
        // Top-docked (ClassicHudScaling.Apply with bottomDocked false): local (0, 0) is the screen's top left.
        _canvas.Present(_frame, Vector2.Zero, screen.Scale, _layout.Factor);
    }

    /// <summary>Whether a screen pixel (top-left origin) is over the strip and the strip takes ordinary input.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_layout.Height <= 0)
        {
            return false;
        }

        HudScreen screen = _layout.Screen;
        return _hud.ContainsPointer(screen, screen.Virtual(screenPosition.X / _layout.Factor),
            screen.Virtual(screenPosition.Y / _layout.Factor));
    }

    /// <summary>The resource widget under a screen pixel (top-left origin), for the hover text.</summary>
    public KeeperResourceKind ResourceAt(Vector2 screenPosition)
    {
        if (_layout.Height <= 0)
        {
            return KeeperResourceKind.None;
        }

        HudScreen screen = _layout.Screen;
        return _hud.ResourceAt(screen, screen.Virtual(screenPosition.X / _layout.Factor),
            screen.Virtual(screenPosition.Y / _layout.Factor));
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented strip keeps working.
        if (what == NotificationPredelete)
        {
            _canvas = null;
        }
    }
}
