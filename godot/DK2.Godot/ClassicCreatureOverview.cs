// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The creature overview in the Creatures panel, the Godot counterpart of Unity's <c>ClassicCreatureOverview</c>:
/// <see cref="CreatureOverviewHud"/> (ADR-0805) decides every draw and press, and a <see cref="HudCanvas"/> draws its frame
/// bottom-docked under the HUD scale. Like Unity's runner, a press goes to the model without being taken from the other
/// panels: a mode button or a page strip changes the overview, and a press on a worker row or a column raises
/// <see cref="Requested"/> with the pickup or focus it asks for.</summary>
public partial class ClassicCreatureOverview : Node
{
    /// <summary>A pointer no widget contains, for while it is outside the window.</summary>
    private static readonly Vector2 Away = new(-1e9f, -1e9f);
    private readonly HudFrame _frame = new();
    private CreatureOverviewHud? _hud;
    private HudCanvas? _canvas;
    private HudScaleLayout _layout;
    private float _scale = 1;
    /// <summary>Screen pixel of the canvas's local (0, 0), rounded as drawn; the pointer is measured from it.</summary>
    private Vector2 _origin;
    private bool _allowInput, _pointerInWindow = true;

    /// <summary>The HUD scale percent (Unity's <c>UiScale</c>, the graphics setting).</summary>
    public int UiScale { get; set; } = 100;

    public CreatureOverviewMode Mode => _hud?.Mode ?? CreatureOverviewMode.Total;

    /// <summary>A press on a worker row or a column: the pickup or focus it asks for, which a host turns into the hand's
    /// pickup or a camera move.</summary>
    public event Action<CreatureOverviewRequest>? Requested;

    /// <summary>Opens the overview over <paramref name="art"/> and <paramref name="view"/>; call before it enters the tree.</summary>
    public void Open(InstallCreatureOverviewArt art, CreatureOverview view)
    {
        if (_hud != null) throw new InvalidOperationException("The creature overview is already open.");
        if (art is null) throw new ArgumentNullException(nameof(art));
        var hud = new CreatureOverviewHud(art.Portrait, view);
        var canvas = new HudCanvas { Name = "HudCanvas", Load = art.Image, Visible = false };
        // Decoded now, as Unity's facade does, and before the canvas is kept, so a failed decode leaves the overview
        // unopened and retryable. Portraits load as their types appear.
        try { foreach (HudImage image in CreatureOverviewHud.Images()) canvas.Preload(image); }
        catch { canvas.Free(); throw; }
        _hud = hud; _canvas = canvas;
        AddChild(_canvas);
    }

    /// <summary>Lays the overview out for the viewport and presents it.</summary>
    /// <param name="area">The Creatures panel's place (<see cref="BottomPanelHud.PanelArea"/>), null while it is closed.</param>
    public void Draw(HudVisibility visibility, BottomPanelRect? area)
    {
        if (_hud == null || _canvas == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        _layout = new HudScaleLayout(width, height, UiScale);
        HudScreen screen = _layout.Screen;
        _scale = screen.Scale;
        _allowInput = visibility.AllowOrdinaryInput;
        _canvas.Visible = visibility.ShowNormalHud && area.HasValue;
        // Bottom-docked (ClassicHudScaling.Apply), as the bottom panel: the panel's bottom stays on the screen's bottom.
        _origin = new Vector2(0, _layout.BottomOriginPixels);
        (float px, float py) = ToVirtual(_pointerInWindow ? GetViewport().GetMousePosition() : Away);
        _frame.Clear();
        _hud.Build(screen, width, visibility, area, (px, py), _frame);
        _canvas.Present(_frame, _origin, _scale, _layout.Factor);
    }

    /// <summary>The hovered widget's text id (the worker row icons and the mode selectors), 0 for none.</summary>
    public int HoveredTextId(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return 0;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.HoveredTextId(x, y);
    }

    /// <summary>The creature type whose column is hovered, for the status line.</summary>
    public CreatureOverviewType? HoveredColumnType(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return null;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.HoveredColumnType(x, y);
    }

    /// <summary>Whether a screen pixel (top-left origin) is the overview's, so a host keeps it from the world.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return false;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ContainsPointer(x, y);
    }

    public override void _Input(InputEvent @event)
    {
        // Unity's runner asks only while ordinary input is allowed (DungeonRunner.QueueInput).
        if (_hud == null || _layout.Height <= 0 || !_allowInput) return;
        if (@event is not InputEventMouseButton { Pressed: true } button) return;
        bool primary = button.ButtonIndex == MouseButton.Left, secondary = button.ButtonIndex == MouseButton.Right;
        if (!primary && !secondary) return;
        bool shift = button.ShiftPressed, control = button.CtrlPressed, alt = button.AltPressed;
        (float x, float y) = ToVirtual(button.Position);
        // Not marked handled: Unity's overview canvas picks nothing, so the panels under it still get the press.
        if (_hud.TryPress(x, y, primary, secondary, shift, control, alt, out CreatureOverviewRequest request))
            Requested?.Invoke(request);
    }

    public override void _Notification(int what)
    {
        // Unity's Input.mousePosition leaves the screen with the pointer; Godot stops reporting it instead.
        if (what == NotificationWMMouseExit) _pointerInWindow = false;
        else if (what == NotificationWMMouseEnter) _pointerInWindow = true;
        // Freed, not merely removed from the tree: a reparented overview keeps working.
        else if (what == NotificationPredelete) { Requested = null; _hud = null; _canvas = null; }
    }

    /// <summary>A screen pixel in the model's virtual units (Unity's <c>LocalPointer</c> then <c>ToVirtual</c>), from the
    /// same rounded origin the drawing uses, so hits land on the art at any HUD scale.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen) =>
        ((screen.X - _origin.X) / _layout.Factor / _scale, (screen.Y - _origin.Y) / _layout.Factor / _scale);
}
