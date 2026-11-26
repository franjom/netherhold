// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The options menu's game options pages, the Godot counterpart of Unity's <c>ClassicGameOptionsPanel</c>:
/// <see cref="GameOptionsHud"/> (ADR-0805) decides every draw, hit, slider drag and captured key, and a
/// <see cref="HudCanvas"/> draws its frame over the whole screen at the screen's own scale (1920 virtual units high, as the
/// options menu). While it shows it takes every mouse press from the panels under it, as Unity's runner gives every press
/// to the open menu; a left press goes to the pages, a press that holds (a slider, the key list's arrow or thumb) follows
/// the pointer until the left button is released.</summary>
public partial class ClassicGameOptionsPanel : Node
{
    private readonly HudFrame _frame = new();
    private GameOptionsHud? _hud;
    private HudCanvas? _canvas;
    /// <summary>Local units per virtual unit (the screen height over 1920) at the last draw; 0 before it.</summary>
    private float _scale;

    /// <summary>Opens the pages, hidden; call before it enters the tree.</summary>
    /// <param name="text">A <c>Text.str</c> string by id, empty when missing.</param>
    /// <param name="font">The legible bitmap font for a screen height.</param>
    /// <param name="image">A catalogue image by name (the scroll bars).</param>
    public void Open(InGameOptionsController model, Func<uint, string> text, Func<int, BitmapFont> font, Func<string, RgbaImage> image)
    {
        if (_hud != null) throw new InvalidOperationException("The game options pages are already open.");
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (image is null) throw new ArgumentNullException(nameof(image));
        _hud = new GameOptionsHud(model, text, font, ClassicKeyNames.Name);
        _canvas = new HudCanvas { Name = "HudCanvas", Load = image, Visible = false };
        AddChild(_canvas);
        foreach (HudImage art in GameOptionsHud.Images()) _canvas.Preload(art);
    }

    /// <summary>Shows or hides the pages; showing opens them on their root page, hiding accepts the page's edits
    /// (<see cref="GameOptionsHud.Show"/>).</summary>
    public void Show(bool visible)
    {
        if (_hud == null || _canvas == null) return;
        _hud.Show(visible); _canvas.Visible = visible;
    }

    /// <summary>Binds this frame's key to the row being captured (<see cref="GameOptionsHud.CaptureKey"/>).</summary>
    public void CaptureKey(GameBindingInput keys) => _hud?.CaptureKey(keys);

    /// <summary>Whether the pages show: they hold the whole screen, as Unity's facade answers.</summary>
    public bool ContainsPointer(Vector2 screenPosition) => _hud != null && _hud.Visible;

    /// <summary>Lays the page out for the viewport and presents it, the label under the pointer lit.</summary>
    public void Draw(Vector2 pointer)
    {
        if (_hud == null || _canvas == null || !_hud.Visible) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        var screen = new HudScreen(width, height);
        _scale = screen.Scale;
        _frame.Clear();
        _hud.Build(screen, (pointer.X / _scale, pointer.Y / _scale), _frame);
        _canvas.Present(_frame, Vector2.Zero, _scale, 1);
    }

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || !_hud.Visible || _scale <= 0) return;
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true } press:
                // Unity's runner gives every press to the menu while it is open (QueueHudInput); the pages act on the left one.
                GetViewport().SetInputAsHandled();
                _hud.PointerDown(press.Position.X / _scale, press.Position.Y / _scale, press.ButtonIndex == MouseButton.Left ? 0 : 1);
                return;
            // Only the left button begins a hold, so only its release ends one (the wheel sends synthetic releases).
            case InputEventMouseButton { ButtonIndex: MouseButton.Left }: _hud.PointerUp(); return;
            // Unity sees the moves only while the canvas holds the pointer.
            case InputEventMouseMotion motion when _hud.Holding:
                _hud.PointerMove(motion.Position.X / _scale, motion.Position.Y / _scale);
                return;
        }
    }

    public override void _Notification(int what)
    {
        // A release the window never sees (focus lost mid-drag) must not leave the press held, as Unity's PointerCaptureOutEvent.
        if (what == NotificationApplicationFocusOut) { _hud?.PointerUp(); return; }
        // Freed, not merely removed from the tree: a reparented panel keeps working.
        if (what == NotificationPredelete) { _hud = null; _canvas = null; }
    }
}
