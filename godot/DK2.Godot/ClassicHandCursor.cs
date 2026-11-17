// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The hand cursor, the Godot counterpart of Unity's <c>ClassicHandCursor</c> (plan step 3e): the kind's Sprite.WAD
/// sheet with its green keyed out (<see cref="HandCursorHud"/>), animated by <see cref="HandCursorAnimation"/>, drawn by a
/// <see cref="HudCanvas"/> on the top canvas layer over every panel and the placement cursor. The OS pointer is hidden while
/// it shows, unless <see cref="ManageSystemPointer"/> is off (a scripted pointer leaves the owner's pointer alone).</summary>
public partial class ClassicHandCursor : Node
{
    /// <summary>Over the panels (layer 0) and the placement cursor's layer, as Unity's sorting order 1000 over its 999.</summary>
    public const int CanvasLayerIndex = 101;

    private readonly HudFrame _frame = new();
    private readonly HandCursorAnimation _animation = new();
    private HudCanvas? _canvas;
    private HandCursorKind _kind;
    private int _sheetFrame;
    private bool _ownsPointer;

    /// <summary>The HUD scale percent (Unity's <c>UiScale</c>), which scales the hand about the pointer.</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>Whether the OS pointer is hidden while the hand shows.</summary>
    public bool ManageSystemPointer { get; set; } = true;

    /// <summary>Opens the cursor over <paramref name="sheets"/>, which the caller disposes, and decodes every kind's sheet
    /// now so no frame decodes one on first use (ADR-0688); call before it enters the tree.</summary>
    public void Open(InstallHandCursorSheets sheets)
    {
        if (_canvas != null) throw new InvalidOperationException("The hand cursor is already open.");
        if (sheets is null) throw new ArgumentNullException(nameof(sheets));
        var layer = new CanvasLayer { Name = "Layer", Layer = CanvasLayerIndex };
        var canvas = new HudCanvas { Name = "HudCanvas", Load = sheets.Load, Visible = false };
        layer.AddChild(canvas);
        try
        {
            foreach (HandCursorKind kind in Enum.GetValues<HandCursorKind>())
                if (kind != HandCursorKind.Default) canvas.Preload(HandCursorHud.Sheet(kind));
        }
        catch { layer.Free(); throw; }
        _canvas = canvas;
        AddChild(layer);
    }

    /// <summary>Unity's <c>Update</c>: shows <paramref name="kind"/> at its animation frame, or hides the hand (and gives the
    /// pointer back) when not <paramref name="visible"/> or the kind is the default.</summary>
    public void Update(HandCursorKind kind, bool visible, long milliseconds)
    {
        if (_canvas == null) return;
        if (!visible || kind == HandCursorKind.Default)
        {
            _kind = HandCursorKind.Default;
            _canvas.Visible = false;
            _animation.Reset();
            RestorePointer();
            return;
        }
        _kind = kind;
        _sheetFrame = _animation.Advance(kind, milliseconds);
        _canvas.Visible = true;
        if (ManageSystemPointer && Input.MouseMode == Input.MouseModeEnum.Visible)
        {
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            _ownsPointer = true;
        }
    }

    /// <summary>Places the shown frame with its anchor on <paramref name="pointer"/> (screen pixels from the top left).</summary>
    public void Draw(Vector2 pointer)
    {
        if (_canvas == null || _kind == HandCursorKind.Default) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        var screen = new HudScreen(width, height);
        _frame.Clear();
        HandCursorHud.Draw(_frame, _kind, _sheetFrame, new System.Numerics.Vector2(pointer.X, pointer.Y), screen,
            new HudScaleLayout(width, height, UiScale).Factor);
        _canvas.Present(_frame, Vector2.Zero, screen.Scale, 1);
    }

    private void RestorePointer()
    {
        if (!_ownsPointer) return;
        if (Input.MouseMode == Input.MouseModeEnum.Hidden) Input.MouseMode = Input.MouseModeEnum.Visible;
        _ownsPointer = false;
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: the pointer is given back.
        if (what != NotificationPredelete) return;
        RestorePointer();
        _canvas = null;
    }
}
