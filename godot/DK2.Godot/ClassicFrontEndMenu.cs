// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The front-end menu, the Godot counterpart of Unity's <c>ClassicFrontEndMenu</c> plus the menu part of
/// <c>DungeonRunner.FrontEnd</c>: <see cref="FrontEndMenuHud"/> (ADR-0805) decides every label, colour, hover and
/// page, and a <see cref="HudCanvas"/> draws its frame. This node runs the session's clock, converts the pointer to
/// 640 x 480 units and forwards it. No station scene, movies, campaign, saves or settings pages are wired yet, so the
/// model leaves their buttons disabled.</summary>
public partial class ClassicFrontEndMenu : Node
{
    private readonly HudFrame _frame = new();
    private ClassicFrontEndMenuAssets? _assets;
    private FrontEndSession? _session;
    private FrontEndMenuHud? _hud;
    private HudCanvas? _canvas;
    private double _elapsed;
    private Vector2 _pointer;
    private int _wheel;

    private bool _useReplacement;

    public FrontEndMenuController? Model => _hud?.Model;

    /// <summary>Draw labels with the replacement TrueType face where it can set them (<see cref="Open"/> turns it on, as
    /// Unity does); without a face on this machine the labels stay the BF4 bitmap.</summary>
    public bool UseReplacementFont
    {
        get => _useReplacement;
        set
        {
            _useReplacement = value;
            if (_canvas == null || _assets == null) return;
            FontFile? face = value ? _assets.ReplacementFont : null;
            if (value && face == null) GD.Print("DK2.Godot: no replacement font found; the menu uses the BF4 bitmap font.");
            _canvas.ReplacementFont = face;
        }
    }

    /// <summary>Whether the last presented frame drew any label with the replacement font.</summary>
    public bool UsingReplacementFont => _canvas?.UsingReplacementFont ?? false;

    /// <summary>A widget's pointer region in 640 x 480 units.</summary>
    public FrontEndRect HitBounds(FrontEndWidget widget) =>
        (_hud ?? throw new InvalidOperationException("The menu is not open.")).HitBounds(widget);

    /// <summary>Opens the menu over an install; call before the node enters the tree.</summary>
    public void Open(string install)
    {
        if (_hud != null) throw new InvalidOperationException("The menu is already open.");
        _assets = new ClassicFrontEndMenuAssets(install);
        Func<int, IReadOnlyList<KcsFrame>?> paths = EngineCameraPaths.Loader(install);
        _session = new FrontEndSession(path => paths((int)path));
        _hud = new FrontEndMenuHud(_session, _assets, HudFit, KeyName);
        _hud.Model.QuitRequested += Quit;
        _session.PageChanged += PageChanged;
        _canvas = new HudCanvas { Name = "HudCanvas", Load = _assets.Load };
        AddChild(_canvas);
        UseReplacementFont = true;
        _session.Update(TimeSpan.Zero);
    }

    public override void _Input(InputEvent @event)
    {
        // Only the wheel comes from events; the position is polled each frame, as Unity reads Input.mousePosition.
        if (@event is InputEventMouseButton button)
        {
            if (!button.Pressed) return;
            if (button.ButtonIndex == MouseButton.WheelUp) _wheel++;
            else if (button.ButtonIndex == MouseButton.WheelDown) _wheel--;
        }
    }

    public override void _Process(double delta)
    {
        if (_hud == null || _session == null || _canvas == null) return;
        // Engine time rather than the wall clock, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        _session.Update(TimeSpan.FromSeconds(_elapsed));
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        bool sized = width > 0 && height > 0;
        _hud.Sync();
        FrontEndPointer? pointer = null;
        if (sized)
        {
            _pointer = GetViewport().GetMousePosition();
            FrontEndPoint at = FrontEndBox.ToUnits(_pointer.X, _pointer.Y, width, height);
            pointer = new FrontEndPointer(at.X, at.Y, Input.IsMouseButtonPressed(MouseButton.Left));
        }
        _hud.Advance(pointer, sized ? _wheel : 0, _elapsed);
        _wheel = 0;
        // No sound player yet: drop the requests so they do not pile up.
        _hud.Model.Sounds.Clear();
        _hud.Sync();
        if (!sized) return;
        _frame.Clear();
        _hud.Build(_frame);
        float pixelsPerUnit = FrontEndBox.Scale(width, height);
        (float left, float top) = FrontEndBox.Origin(width, height);
        var origin = new Vector2(MathF.Floor(left + .5f), MathF.Floor(top + .5f));
        _canvas.Present(_frame, origin, 1f / FrontEndBox.VirtualPerUnit, pixelsPerUnit);
    }

    /// <summary>The HUD scale the window fits for a requested percent; read only by the graphics settings page.</summary>
    private float HudFit(int percent)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        return new HudScaleLayout((int)size.X, (int)size.Y, percent).EffectivePercent;
    }

    /// <summary>A key's name for the controls settings page, which is not wired yet (no options controller).</summary>
    private static string KeyName(KeyScanCode scan) => scan.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void PageChanged(FrontEndPage page) => GD.Print("DK2.Godot front end: page ", page.ToString());

    private void Quit() => GetTree().Quit();

    public override void _ExitTree()
    {
        if (_session != null) _session.PageChanged -= PageChanged;
        _hud?.Dispose(); _hud = null;
        _assets?.Dispose(); _assets = null;
        _session = null; _canvas = null;
    }
}
