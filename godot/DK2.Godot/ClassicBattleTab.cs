// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Menu 43, the Battle tab right of the message tabs, the Godot counterpart of Unity's <c>ClassicBattleTab</c>:
/// <see cref="BattleTabHud"/> (ADR-0805) decides every draw, hit and press, and a <see cref="HudCanvas"/> draws its frame
/// bottom-docked under the HUD scale. A press on a shown button is taken from the panels under it and raised as
/// <see cref="Clicked"/>; what it does is <see cref="BattleTabView.Action"/>. Not built: the blades' sound 0x30F (Q775).</summary>
public partial class ClassicBattleTab : Node
{
    private readonly HudFrame _frame = new();
    private BattleTabHud? _hud;
    private HudCanvas? _canvas;
    private HudScaleLayout _layout;
    private float _scale = 1;
    /// <summary>Screen pixel of the canvas's local (0, 0), rounded as drawn; the pointer is measured from it.</summary>
    private Vector2 _origin;

    /// <summary>The HUD scale percent (Unity's <c>UiScale</c>, the graphics setting).</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>A button and the mouse button (0 left, 1 right) that pressed it.</summary>
    public event Action<BattleTabButton, int>? Clicked;

    /// <summary>Opens the menu; call before it enters the tree.</summary>
    /// <param name="image">A HUD catalogue image by name (the buttons' art).</param>
    /// <param name="text">A <c>Text.str</c> string by id (the blades' hover text).</param>
    public void Open(Func<string, RgbaImage> image, Func<uint, string> text)
    {
        if (_hud != null) throw new InvalidOperationException("The battle tab is already open.");
        if (image is null) throw new ArgumentNullException(nameof(image));
        if (text is null) throw new ArgumentNullException(nameof(text));
        var hud = new BattleTabHud(text);
        var canvas = new HudCanvas { Name = "HudCanvas", Load = image, Visible = false };
        // Decoded now (Unity's facade decodes each frame's art on its first draw), before the canvas is kept, so a failed
        // decode leaves the menu unopened and retryable.
        try { foreach (HudImage art in BattleTabHud.Images()) canvas.Preload(art); }
        catch { canvas.Free(); throw; }
        hud.Clicked += Raise;
        _hud = hud; _canvas = canvas;
        AddChild(_canvas);
    }

    /// <summary>The hover text for the button under a screen pixel (top-left origin): the blades' only.</summary>
    public HoverTextSnapshot HoverText(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return HoverTextSnapshot.Empty;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.HoverText(x, y);
    }

    /// <summary>Whether a screen pixel (top-left origin) is a shown button taking input, so a host keeps it from the world.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return false;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ContainsPointer(x, y);
    }

    /// <summary>Lays the menu out for the viewport and presents it.</summary>
    /// <param name="collapsed">Whether the bottom panel is collapsed, which drops the menu with it.</param>
    /// <param name="milliseconds">The real clock, which the frames step on whatever the game speed.</param>
    public void Draw(HudVisibility visibility, bool collapsed, BattleTabSnapshot shown, long milliseconds)
    {
        if (_hud == null || _canvas == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        _layout = new HudScaleLayout(width, height, UiScale);
        _scale = _layout.Screen.Scale;
        // Bottom-docked (ClassicHudScaling.Apply), as the bottom panel: the panel's bottom stays on the screen's bottom.
        _origin = new Vector2(0, MathF.Floor(_layout.BottomOffset * _layout.Factor + .5f));
        _frame.Clear();
        _hud.Build(visibility, collapsed, shown, milliseconds, _frame);
        _canvas.Visible = _frame.Count > 0;
        _canvas.Present(_frame, _origin, _scale, _layout.Factor);
    }

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || _layout.Height <= 0) return;
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        int index = button.ButtonIndex switch { MouseButton.Left => 0, MouseButton.Right => 1, MouseButton.Middle => 2, _ => -1 };
        if (index < 0) return;
        (float x, float y) = ToVirtual(button.Position);
        // Unity's image stops the press's propagation (any button) once it takes input.
        if (_hud.PointerDown(x, y, index)) GetViewport().SetInputAsHandled();
    }

    private void Raise(BattleTabButton button, int mouse) => Clicked?.Invoke(button, mouse);

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented menu keeps working.
        if (what != NotificationPredelete) return;
        if (_hud != null) _hud.Clicked -= Raise;
        Clicked = null; _hud = null; _canvas = null;
    }

    /// <summary>A screen pixel in the model's virtual units (Unity's <c>LocalPointer</c> then <c>ToVirtual</c>), from the
    /// same rounded origin the drawing uses, so hits land on the art at any HUD scale.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen) =>
        ((screen.X - _origin.X) / _layout.Factor / _scale, (screen.Y - _origin.Y) / _layout.Factor / _scale);
}
