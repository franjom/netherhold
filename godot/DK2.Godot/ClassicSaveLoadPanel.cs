// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the options menu's save and load pages. While open, it takes pointer presses before other panels. The browser handles primary-button presses.</summary>
public partial class ClassicSaveLoadPanel : Node
{
    /// <summary>The canvas's image source. The browser draws set text only.</summary>
    private static readonly RgbaImage BlankImage = new(1, 1, new byte[4]);
    private static readonly Func<string, RgbaImage> Blank = _ => BlankImage;
    private readonly HudFrame _frame = new();
    private SaveLoadHud? _hud;
    private HudCanvas? _canvas;
    /// <summary>Local units per virtual unit (the screen height over 1920) at the last draw; 0 before it.</summary>
    private float _scale;
    /// <summary>Wheel notches since the last <see cref="Show"/>, positive up.</summary>
    private int _wheel;
    private bool _frontEnd;
    private Vector2 _origin;

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
        int wheel = _wheel;
        _wheel = 0;
        Present(view, visible, save, typed, wheel, frontEnd: false);
    }

    internal void ShowFrontEnd(SaveSlotsView? view, bool visible, int wheel) =>
        Present(view, visible, save: false, default, wheel, frontEnd: true);

    internal void FrontEndPointerDown(Vector2 point)
    {
        if (_frontEnd && _scale > 0 && _hud is { Visible: true })
        {
            Vector2 local = (point - _origin) / _scale;
            _hud.PointerDown(local.X, local.Y, 0);
        }
    }

    private void Present(SaveSlotsView? view, bool visible, bool save, ReadOnlySpan<char> typed, int wheel, bool frontEnd)
    {
        if (_hud == null || _canvas == null) return;
        _frontEnd = frontEnd;
        _hud.Show(view, visible, save, frontEnd);
        _canvas.Visible = _hud.Visible;
        if (!_hud.Visible) return;
        _hud.Type(typed);
        _hud.Wheel(wheel);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        var screen = new HudScreen(width, height);
        _origin = Vector2.Zero;
        _scale = screen.Scale;
        if (frontEnd)
        {
            float scale = FrontEndBox.Scale(width, height);
            (float left, float top) = FrontEndBox.Origin(width, height);
            _origin = new Vector2(MathF.Floor(left + .5f), MathF.Floor(top + .5f));
            _scale = scale / FrontEndBox.VirtualPerUnit;
            screen = new HudScreen((int)(scale * FrontEndBox.Width), (int)(scale * FrontEndBox.Height));
        }
        _frame.Clear();
        _hud.Build(screen, _frame);
        _canvas.Present(_frame, _origin, _scale, 1);
    }

    /// <summary>Shows why a save or load failed under the list.</summary>
    public void Error(string message) => _hud?.Error(message);

    public override void _Input(InputEvent @event)
    {
        if (_frontEnd || _hud == null || !_hud.Visible || _scale <= 0) return;
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        GetViewport().SetInputAsHandled();
        // Two presses in one frame both see the page as last drawn: a press that changes the page (Back, Continue) is
        // applied by the host, and this browser reads it at the next Show.
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp: _wheel++; return;
            case MouseButton.WheelDown: _wheel--; return;
            // The browser acts only on a primary-button press.
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
