// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;
using DK2.Pathfinding;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The minimap, the Godot counterpart of Unity's <c>DungeonMinimap</c>: <see cref="MinimapHud"/> (ADR-0805) decides
/// the frame, corners, north sprite, hit areas and presses, and a <see cref="HudCanvas"/> draws them bottom-docked under
/// the HUD scale over the disc. The disc is <see cref="MinimapDiscImage"/>'s pixels (the CPU twin of Unity's
/// <c>DK2Minimap.shader</c>), drawn again only when a capture, the zoom, the view or the size changed, and uploaded into
/// one reused texture. A press is raised as <see cref="Pressed"/> with the action <see cref="MinimapHud.Press"/> gives,
/// and not taken from the panels under it: Unity's minimap canvas picks nothing. Not drawn: the camera footprint, which
/// needs the 3D camera.</summary>
public partial class ClassicMinimap : Node
{
    private static readonly Vector2 Away = new(-1e9f, -1e9f);
    private readonly HudFrame _frame = new();
    private MinimapHud? _hud;
    private MinimapDiscImage? _disc;
    private HudCanvas? _canvas;
    private Node2D? _discNode;
    private Rid _discItem;
    private Image? _image;
    private ImageTexture? _texture;
    /// <summary>The disc's screen rect as last recorded; empty before the first draw.</summary>
    private Rect2 _discRect;
    private HudScaleLayout _layout;
    private float _scale = 1;
    /// <summary>Screen pixel of the canvas's local (0, 0), rounded as drawn; the pointer is measured from it.</summary>
    private Vector2 _origin;
    private bool _pointerInWindow = true;
    /// <summary>The view the disc was last drawn from, which presses resolve against.</summary>
    private FpPosition3 _focus;
    private Dk2Angle _heading;
    private MinimapZoom _drawnZoom = MinimapZoom.WholeMap;
    /// <summary>A capture since the disc was last drawn (Unity's <c>MinimapGpuMap</c> dirty flag).</summary>
    private bool _captured = true;
    private TilePos? _heart;
    private GameTick _tick;

    /// <summary>The HUD scale percent (Unity's <c>UiScale</c>, the graphics setting).</summary>
    public int UiScale { get; set; } = 100;

    /// <summary>The model, whose class grid and palette the full map shares.</summary>
    public MinimapHud? Model => _hud;

    /// <summary>A press that acts (<see cref="MinimapHud.Press"/>: a corner, or a secondary press on the map) and where
    /// it was, in screen pixels.</summary>
    public event Action<ClassicHudAction, Vector2>? Pressed;

    /// <summary>A primary press on the map and the tile it resolves to (<see cref="MinimapHud.TryNavigate"/>), where
    /// Unity's runner sends the camera when no other HUD press took it. Raised whatever is open: the host drops it while a
    /// modal panel holds the screen or input is suppressed, reading that state when the press arrives.</summary>
    public event Action<TilePos>? Navigated;

    /// <summary>The palette from the native colour strip's PNG (<see cref="IMinimapAssets.PalettePng"/>), decoded by Godot
    /// as Unity's <c>DungeonMinimap</c> decodes it (stored bytes: <see cref="PngChunks.WithoutColourSpace"/>), and read by
    /// <see cref="MinimapPalette.FromTopRow"/>.</summary>
    public static MinimapPalette DecodePalette(byte[] png)
    {
        using Image strip = Decode(png);
        return MinimapPalette.FromTopRow(strip.GetData().AsSpan(0, strip.GetWidth() * 4));
    }

    /// <summary>Opens the minimap over the install's art; call before it enters the tree.</summary>
    public void Open(IMinimapAssets assets, int mapWidth, int mapHeight)
    {
        if (_hud != null) throw new InvalidOperationException("The minimap is already open.");
        if (assets is null) throw new ArgumentNullException(nameof(assets));
        RgbaImage backdrop;
        using (Image rock = Decode(assets.BackdropPng)) backdrop = new RgbaImage(rock.GetWidth(), rock.GetHeight(), rock.GetData());
        var hud = new MinimapHud(DecodePalette(assets.PalettePng), mapWidth, mapHeight);
        var art = new Dictionary<string, RgbaImage>(StringComparer.Ordinal)
        {
            [MinimapHud.FrameArt] = assets.Frame,
            [MinimapHud.NorthArt] = assets.North,
        };
        for (int i = 0; i < 4; i++)
            for (int active = 0; active < 2; active++)
                art[MinimapHud.CornerArt((MinimapCorner)i, active != 0)] = assets.Corner((MinimapCorner)i, active != 0);
        var canvas = new HudCanvas { Name = "HudCanvas", Load = path => art[path], Visible = false };
        // Decoded now, as Unity's facade preloads them, and before the canvas is kept, so a failed decode leaves the
        // minimap unopened and retryable.
        try { foreach (HudImage image in MinimapHud.Images()) canvas.Preload(image); }
        catch { canvas.Free(); throw; }
        _hud = hud; _disc = new MinimapDiscImage(backdrop); _canvas = canvas;
        // Under the frame, as Unity's disc image is under its canvas.
        _discNode = new Node2D { Name = "Disc", Visible = false };
        AddChild(_discNode);
        AddChild(_canvas);
    }

    /// <summary>Reads the class grid and the markers once per tick (<see cref="MinimapHud.Capture"/>); call while the
    /// simulation is settled.</summary>
    /// <returns>Whether anything was read, which the full map's image follows.</returns>
    public bool Capture(WorldState world, WorldView view, INavByteGrid navigation)
    {
        if (_hud == null || !_hud.Capture(world, view, navigation, out _)) return false;
        _heart = view.MinimapHeartTile; _tick = world.CurrentTick;
        _captured = true;
        return true;
    }

    public void SetVisibility(HudVisibility visibility) => _hud?.SetVisibility(visibility);

    /// <summary>A press on the magnifier at a screen pixel zooms (<see cref="MinimapHud.ZoomPress"/>).</summary>
    public void HandleZoomPress(Vector2 screenPosition, bool primary, bool secondary)
    {
        if (_hud == null || _layout.Height <= 0) return;
        (float x, float y) = ToVirtual(screenPosition);
        _hud.ZoomPress(x, y, primary, secondary);
    }

    /// <summary>The corner control under a screen pixel, for its tooltip; None off the corners.</summary>
    public ClassicHudAction HoveredControl(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return ClassicHudAction.None;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.HoveredControl(x, y);
    }

    /// <summary>Whether a screen pixel (top-left origin) is the minimap's, so a host keeps it from the world.</summary>
    public bool ContainsPointer(Vector2 screenPosition)
    {
        if (_hud == null || _layout.Height <= 0) return false;
        (float x, float y) = ToVirtual(screenPosition);
        return _hud.ContainsPointer(x, y);
    }

    /// <summary>Lays the minimap out for the viewport and presents it.</summary>
    /// <param name="selected">The selected tool's corner, which stays lit.</param>
    /// <param name="optionsVisible">The options menu is open, which keeps its corner lit.</param>
    /// <param name="focus">The camera's focus (<see cref="MinimapHud.FixedFocus"/>) and <paramref name="heading"/>, which
    /// the disc is drawn from and presses resolve against.</param>
    public void Draw(ClassicHudAction selected, bool optionsVisible, FpPosition3 focus, Dk2Angle heading)
    {
        if (_hud == null || _disc == null || _canvas == null || _discNode == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        _layout = new HudScaleLayout(width, height, UiScale);
        HudScreen screen = _layout.Screen;
        _scale = screen.Scale;
        // Bottom-docked (ClassicHudScaling.Apply), as the bottom panel: the panel's bottom stays on the screen's bottom.
        _origin = new Vector2(0, _layout.BottomOriginPixels);
        _hud.SetControls(selected, optionsVisible);
        bool shown = _hud.Visibility.ShowNormalHud;
        _canvas.Visible = shown; _discNode.Visible = shown;
        if (!shown) return;
        (float px, float py) = ToVirtual(_pointerInWindow ? GetViewport().GetMousePosition() : Away);
        _frame.Clear();
        _hud.Build(screen, (px, py), heading, _frame);
        _canvas.Present(_frame, _origin, _scale, _layout.Factor);
        bool resized = DrawDisc(MinimapHud.DiscPixels(height, _layout.Factor), focus, heading);
        Rect2 rect = _canvas.ScreenRect(MinimapHud.Disc);
        if (resized || rect != _discRect) RecordDisc(rect);
    }

    /// <summary>Draws and uploads the disc when a capture, the zoom, the zoomed focus, the heading or the size changed.</summary>
    /// <returns>Whether the texture was replaced at a new size.</returns>
    private bool DrawDisc(int diameter, FpPosition3 focus, Dk2Angle heading)
    {
        MinimapZoom zoom = _hud!.Zoom;
        bool resized = diameter != _disc!.Diameter;
        if (!_captured && !resized && zoom == _drawnZoom && heading == _heading && (zoom.IsWholeMap || focus == _focus))
        {
            _focus = focus;
            return false;
        }
        _focus = focus; _heading = heading; _drawnZoom = zoom; _captured = false;
        _disc.Render(diameter, _hud.Grid, _hud.Palette, _hud.Markers, zoom, focus, heading, _heart, _tick);
        if (_image == null || resized)
        {
            _image?.Dispose();
            _image = Image.CreateEmpty(diameter, diameter, false, Image.Format.Rgba8);
        }
        _image.SetData(diameter, diameter, false, Image.Format.Rgba8, _disc.Pixels);
        if (_texture == null) _texture = ImageTexture.CreateFromImage(_image);
        else if (resized) _texture.SetImage(_image);
        else _texture.Update(_image);
        return resized;
    }

    /// <summary>The disc texture stretched over its rect, as Unity's image stretches its render target.</summary>
    private void RecordDisc(Rect2 rect)
    {
        if (!_discItem.IsValid)
        {
            _discItem = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(_discItem, _discNode!.GetCanvasItem());
        }
        RenderingServer.CanvasItemClear(_discItem);
        RenderingServer.CanvasItemAddTextureRect(_discItem, rect, _texture!.GetRid());
        _discRect = rect;
    }

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || _layout.Height <= 0 || @event is not InputEventMouseButton { Pressed: true } button) return;
        bool primary = button.ButtonIndex == MouseButton.Left, secondary = button.ButtonIndex == MouseButton.Right;
        if (!primary && !secondary) return;
        (float x, float y) = ToVirtual(button.Position);
        // Not marked handled: Unity's minimap canvas picks nothing, so the panels under it still get the press.
        ClassicHudAction action = _hud.Press(x, y, primary, secondary, _focus, _heading);
        if (action != ClassicHudAction.None) Pressed?.Invoke(action, button.Position);
        else if (primary && _hud.TryNavigate(x, y, _focus, _heading, out TilePos tile)) Navigated?.Invoke(tile);
    }

    public override void _ExitTree()
    {
        if (_discItem.IsValid) { RenderingServer.FreeRid(_discItem); _discItem = default; }
        _discRect = default;
    }

    public override void _Notification(int what)
    {
        // Unity's Input.mousePosition leaves the screen with the pointer; Godot stops reporting it instead.
        if (what == NotificationWMMouseExit) { _pointerInWindow = false; return; }
        if (what == NotificationWMMouseEnter) { _pointerInWindow = true; return; }
        // Freed, not merely removed from the tree: a reparented minimap keeps working.
        if (what != NotificationPredelete) return;
        Pressed = null; Navigated = null; _hud = null; _disc = null;
        // The canvas item draws the texture, so it goes first.
        if (_discItem.IsValid) { RenderingServer.FreeRid(_discItem); _discItem = default; }
        _texture?.Dispose(); _image?.Dispose(); _texture = null; _image = null;
        _canvas = null; _discNode = null;
    }

    /// <summary>A PNG as Godot RGBA8, its stored bytes (no colour-space chunk applied: docs/TRAPS.md).</summary>
    private static Image Decode(byte[] png)
    {
        var image = new Image();
        if (image.LoadPngFromBuffer(PngChunks.WithoutColourSpace(png)) != Error.Ok)
        {
            image.Dispose();
            throw new InvalidOperationException("Minimap asset failed to decode.");
        }
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>A screen pixel in the model's virtual units (Unity's <c>Virtual</c>), from the same rounded origin the
    /// drawing uses, so hits land on the art at any HUD scale.</summary>
    private (float X, float Y) ToVirtual(Vector2 screen) =>
        ((screen.X - _origin.X) / _layout.Factor / _scale, (screen.Y - _origin.Y) / _layout.Factor / _scale);
}
