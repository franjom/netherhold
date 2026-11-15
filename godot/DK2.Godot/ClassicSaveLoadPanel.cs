// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The options menu's save and load pages, the Godot counterpart of Unity's <c>ClassicSaveLoadPanel</c>:
/// <see cref="SaveLoadHud"/> (ADR-0805) decides every draw, hit and typed character, and a <see cref="HudCanvas"/> draws
/// its frame over the whole screen at the screen's own scale (1920 virtual units high, as the options menu). While it shows
/// it takes every mouse press from the panels under it, as Unity's runner gives every press to the open menu; a left
/// press goes to the browser, and the wheel scrolls its list a line a notch.</summary>
public partial class ClassicSaveLoadPanel : Node
{
    /// <summary>The canvas's image source: the browser draws set text only, as Unity's facade loads nothing.</summary>
    private static readonly RgbaImage BlankImage = new(1, 1, new byte[4]);
    private static readonly Func<string, RgbaImage> Blank = _ => BlankImage;
    private readonly HudFrame _frame = new();
    private SaveLoadHud? _hud;
    private HudCanvas? _canvas;
    /// <summary>Local units per virtual unit (the screen height over 1920) at the last draw; 0 before it.</summary>
    private float _scale;
    /// <summary>Wheel notches since the last <see cref="Show"/>, positive up (Unity's <c>Input.mouseScrollDelta.y</c>).</summary>
    private int _wheel;

    /// <summary>Enter in the name, or a press on the name or the Load line.</summary>
    public event Action? Submit;
    /// <summary>A press on the Back or Continue line, with the options menu's action it applies
    /// (<see cref="SaveLoadHud.HudAction"/>).</summary>
    public event Action<ClassicHudAction>? HudAction;

    /// <summary>Opens the browser, hidden; call before it enters the tree.</summary>
    /// <param name="text">A <c>Text.str</c> string by id, empty when missing.</param>
    /// <param name="font">The legible bitmap font for a screen height.</param>
    public void Open(Func<uint, string> text, Func<int, BitmapFont> font)
    {
        if (_hud != null) throw new InvalidOperationException("The save browser is already open.");
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (font is null) throw new ArgumentNullException(nameof(font));
        _hud = new SaveLoadHud(text, font);
        _hud.Submit += RaiseSubmit; _hud.HudAction += RaiseHudAction;
        _canvas = new HudCanvas { Name = "HudCanvas", Load = Blank, Visible = false };
        AddChild(_canvas);
    }

    /// <summary>Shows or hides the browser and, while it shows, feeds it this frame's typing and wheel and presents it.</summary>
    /// <param name="save">The save page: the name is typed in; otherwise the selected save loads.</param>
    /// <param name="typed">This frame's typed characters (<see cref="ClassicKeyboard.Typed"/>).</param>
    public void Show(SaveSlotsView? view, bool visible, bool save, ReadOnlySpan<char> typed)
    {
        if (_hud == null || _canvas == null) return;
        _hud.Show(view, visible, save);
        _canvas.Visible = _hud.Visible;
        int wheel = _wheel;
        _wheel = 0;
        if (!_hud.Visible) return;
        _hud.Type(typed);
        _hud.Wheel(wheel);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        var screen = new HudScreen(width, height);
        _scale = screen.Scale;
        _frame.Clear();
        _hud.Build(screen, _frame);
        _canvas.Present(_frame, Vector2.Zero, _scale, 1);
    }

    /// <summary>Shows why a save or load failed under the list.</summary>
    public void Error(string message) => _hud?.Error(message);

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || !_hud.Visible || _scale <= 0) return;
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        GetViewport().SetInputAsHandled();
        // Two presses in one frame both see the page as last drawn: a press that changes the page (Back, Continue) is
        // applied by the host, and this browser hears of it at the next Show. Unity polls the same way, once a frame.
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp: _wheel++; return;
            case MouseButton.WheelDown: _wheel--; return;
            // Unity's canvas passes the button and the browser acts on the primary one only.
            case MouseButton.Left: _hud.PointerDown(button.Position.X / _scale, button.Position.Y / _scale, 0); return;
        }
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented browser keeps working.
        if (what != NotificationPredelete) return;
        if (_hud != null) { _hud.Submit -= RaiseSubmit; _hud.HudAction -= RaiseHudAction; }
        Submit = null; HudAction = null; _hud = null; _canvas = null;
    }

    private void RaiseSubmit() => Submit?.Invoke();
    private void RaiseHudAction(ClassicHudAction action) => HudAction?.Invoke(action);
}
