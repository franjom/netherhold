// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the placement cursor beside the hand and layers it between the panels and the hand cursor.</summary>
public partial class ClassicPlacementOverlay : Node
{
    /// <summary>Between the panels and the hand cursor.</summary>
    public const int CanvasLayerIndex = 100;

    private readonly PlacementCursorHud _hud = new PlacementCursorHud();
    private readonly HudFrame _frame = new HudFrame();
    private HudCanvas? _canvas;

    /// <summary>The cursor's rules, for the host to set its icon names (<see cref="PlacementCursorHud.SpellIconName"/> and
    /// the held icons').</summary>
    public PlacementCursorHud Model
    {
        get
        {
            return _hud;
        }
    }

    /// <summary>Opens the cursor over the HUD's catalogue images (<paramref name="load"/>); call before it enters the tree.</summary>
    public void Open(Func<string, RgbaImage> load)
    {
        if (_canvas != null)
        {
            throw new InvalidOperationException("The placement cursor is already open.");
        }

        if (load is null)
        {
            throw new ArgumentNullException(nameof(load));
        }

        var layer = new CanvasLayer { Name = "Layer", Layer = CanvasLayerIndex };
        var canvas = new HudCanvas { Name = "HudCanvas", Load = load };
        layer.AddChild(canvas);
        try { foreach (HudImage image in PlacementCursorHud.Images())
            {
                canvas.Preload(image);
            }
        }
        catch { layer.Free(); throw; }
        _canvas = canvas;
        AddChild(layer);
    }

    /// <param name="pointer">The pointer in screen pixels from the top left.</param>
    /// <param name="overHud">Moves the icon left by 40 reference units over the GUI.</param>
    /// <param name="held">The hand's icons, read when <paramref name="feedback"/> shows <see cref="PlacementIcon.Held"/>.</param>
    public void Draw(in PlacementFeedback feedback, Vector2 pointer, bool overHud, HeldItemStack? held, HandCursorKind handKind)
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

        _frame.Clear();
        _hud.Build(_frame, feedback, new System.Numerics.Vector2(pointer.X, pointer.Y), width, height, overHud, held, handKind);
        _canvas.Present(_frame, Vector2.Zero, new HudScreen(width, height).Scale, 1);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            _canvas = null;
        }
    }
}
