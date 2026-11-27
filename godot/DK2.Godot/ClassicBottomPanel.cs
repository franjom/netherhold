// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Draws the keeper's bottom panel from <see cref="BottomPanelHud"/> and forwards pointer input in its virtual units.</summary>
public partial class ClassicBottomPanel : Node, IPointerGestureHolder
{
    private const float Tall = 1920f;
    private readonly HudFrame _frame = new();
    private BottomPanelHud? _hud;
    private HudCanvas? _canvas;
    private HudScaleLayout _layout;
    private float _scale = 1;
    /// <summary>Screen pixel of the canvas's local (0, 0), rounded as drawn; the pointer is measured from it.</summary>
    private Vector2 _origin;
    /// <summary>A pointer press that the panel is still holding.</summary>
    private bool _holding;

    /// <summary>The panel model; its events are the clicks a host turns into commands.</summary>
    public BottomPanelHud Model
    {
        get
        {
            return _hud ?? throw new InvalidOperationException("The panel is not open.");
        }
    }

    /// <summary>The HUD scale percent.</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>Opens the panel over <paramref name="art"/> and <paramref name="view"/>; call before it enters the tree.</summary>
    public void Open(InstallBottomPanelArt art, BottomPanelView view)
    {
        if (_hud != null)
        {
            throw new InvalidOperationException("The panel is already open.");
        }

        if (art is null)
        {
            throw new ArgumentNullException(nameof(art));
        }

        if (view is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        _hud = new BottomPanelHud(art, view);
        _canvas = new HudCanvas { Name = "HudCanvas", Load = art.Image };
        AddChild(_canvas);
    }

    public void SetVisibility(HudVisibility visibility)
    {
        Model.SetVisibility(visibility); SyncCapture();
        if (_canvas != null)
        {
            _canvas.Visible = visibility.ShowNormalHud;
        }
    }

    /// <summary>The hovered widget's texts, or the open panel's description, for the status line.</summary>
    public HoverTextSnapshot HoverText
    {
        get
        {
            return _hud?.HoverText ?? HoverTextSnapshot.Empty;
        }
    }

    /// <summary>Whether a screen pixel (top-left origin) is the panel's, so a host keeps it from the world.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0)
        {
            return false;
        }

        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ContainsPointer(x, y);
    }

    public void CancelPointerGesture() { _hud?.CancelPointerGesture(); SyncCapture(); }

    /// <summary>The panel as a level starts (<see cref="BottomPanelHud.OpenAtLevelStart"/>).</summary>
    public void OpenAtLevelStart() { Model.OpenAtLevelStart(); SyncCapture(); }

    /// <summary>Lays the panel out for the viewport and presents it; <paramref name="milliseconds"/> drives its blinks.</summary>
    public void Draw(long milliseconds)
    {
        if (_hud == null || _canvas == null)
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
        _scale = height / Tall;
        _frame.Clear();
        _hud.Build(MathF.Round(_layout.LogicalWidth) / _scale, milliseconds, _frame);
        // Bottom-docked (ClassicHudScaling.Apply): the canvas's local top is BottomOffset local units below the screen's
        // top, which is above it once the scale passes 100%, so the panel's bottom stays on the screen's bottom.
        _origin = new Vector2(0, _layout.BottomOriginPixels);
        _canvas.Present(_frame, _origin, _scale, _layout.Factor);
        _hud.Repick();
    }

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || _layout.Height <= 0)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            (float x, float y) = ToVirtual(motion.Position);
            _hud.PointerMove(x, y);
            return;
        }
        if (@event is not InputEventMouseButton button)
        {
            return;
        }

        (float bx, float by) = ToVirtual(button.Position);
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            // UI Toolkit's wheel delta is positive downwards; the model pages forward on a positive delta.
            if (button.Pressed && _hud.Wheel(bx, by, button.ButtonIndex == MouseButton.WheelDown ? 1 : -1))
            {
                Handled();
            }

            return;
        }
        int index = button.ButtonIndex switch { MouseButton.Left => 0, MouseButton.Right => 1, MouseButton.Middle => 2, _ => -1 };
        if (index < 0)
        {
            return;
        }

        if (button.Pressed)
        {
            if (!_hud.PointerDown(bx, by, index))
            {
                return;
            }

            _holding = true; Handled();
            return;
        }
        if (!_holding)
        {
            return;
        }

        try
        {
            if (_hud.PointerUp(bx, by, index, button.ShiftPressed, button.CtrlPressed, button.AltPressed))
            {
                Handled();
            }
        }
        finally
        {
            // Release the pointer even if a click handler throws.
            SyncCapture();
        }
        // Hover follows a release away from the pressed widget at once, not at the next move.
        _hud.PointerMove(bx, by);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMMouseExit)
        {
            _hud?.PointerLeave();
        }
        // Losing focus clears the held pointer without dispatching a release.
        else if (what == NotificationApplicationFocusOut) { _holding = false; _hud?.CaptureLost(); }
        // Freed, not merely removed from the tree: a reparented panel keeps working.
        else if (what == NotificationPredelete) { _hud?.CancelPointerGesture(); _holding = false; _hud = null; _canvas = null; }
    }

    /// <summary>A screen pixel in the model's virtual units, from the
    /// same rounded origin the drawing uses, so hits land on the art at any HUD scale.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen)
    {
        return ((screen.X - _origin.X) / _layout.Factor / _scale, (screen.Y - _origin.Y) / _layout.Factor / _scale);
    }

    private void Handled()
    {
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Drops the held press once the model has let go of it.</summary>
    private void SyncCapture()
    {
        if (_hud == null || _hud.HasPointerCapture || !_holding)
        {
            return;
        }

        _holding = false;
    }
}
