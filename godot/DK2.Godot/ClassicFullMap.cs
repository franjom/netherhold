// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The full map, the Godot counterpart of Unity's <c>ClassicFullMap</c>: <see cref="FullMapHud"/> (ADR-0805) drawn
/// by two <see cref="HudCanvas"/>es, the black backdrop under and the room icon, creature squares and legend over the map
/// image, which <see cref="FullMapImage"/> colours from <see cref="MinimapHud"/>'s class grid and this node only uploads
/// (Unity blits the same image with a shader). The panel is in screen pixels. While it shows it takes every mouse press
/// and raises <see cref="Pressed"/> with what the press does (<see cref="FullMapHud.Press"/>). Unity's camera preview at the
/// top right needs the 3D world and is not drawn.</summary>
public partial class ClassicFullMap : Node
{
    private readonly HudFrame _backFrame = new(), _frontFrame = new();
    private FullMapHud? _hud;
    private MinimapHud? _minimap;
    private WorldView? _view;
    private FullMapImage? _pixels;
    private HudCanvas? _back, _front;
    private Node2D? _mapNode;
    private Rid _mapItem;
    private Image? _image;
    private ImageTexture? _texture;
    /// <summary>The map image's rect as last recorded; empty before the first draw.</summary>
    private Rect2 _mapRect;

    /// <summary>A mouse press while the map shows, and the tile under it (Unity's runner sends the camera there on
    /// <see cref="FullMapPress.GoToTile"/>).</summary>
    public event Action<FullMapPress, TilePos>? Pressed;

    public bool Visible => _hud != null && _hud.Visible;

    /// <summary>The palette from the native colour strip's PNG (<see cref="IMinimapAssets.PalettePng"/>), decoded by Godot
    /// as Unity's <c>DungeonMinimap</c> decodes it (stored bytes: <see cref="PngChunks.WithoutColourSpace"/>), and read by
    /// <see cref="MinimapPalette.FromTopRow"/>.</summary>
    public static MinimapPalette DecodePalette(byte[] png)
    {
        using var strip = new Image();
        if (strip.LoadPngFromBuffer(PngChunks.WithoutColourSpace(png)) != Error.Ok) throw new InvalidOperationException("Minimap asset failed to decode.");
        strip.Convert(Image.Format.Rgba8);
        return MinimapPalette.FromTopRow(strip.GetData().AsSpan(0, strip.GetWidth() * 4));
    }

    /// <summary>Opens the map, hidden; call before it enters the tree.</summary>
    /// <param name="minimap">The class grid's source, captured here while the map shows.</param>
    /// <param name="view">The world view the grid's colours are read through.</param>
    /// <param name="font">The legible bitmap font for a screen height (the legend).</param>
    /// <param name="playerName">A player's name by id (the legend).</param>
    /// <param name="image">A catalogue image by name (the room icons).</param>
    public void Open(MinimapHud minimap, WorldView view, Func<int, BitmapFont> font, Func<byte, string> playerName,
        Func<string, RgbaImage> image)
    {
        if (_hud != null) throw new InvalidOperationException("The full map is already open.");
        _minimap = minimap ?? throw new ArgumentNullException(nameof(minimap));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        if (image is null) throw new ArgumentNullException(nameof(image));
        _hud = new FullMapHud(minimap.Palette, minimap.MapWidth, minimap.MapHeight);
        _hud.SetLegend(font, playerName, roomIcons: true);
        _pixels = new FullMapImage(minimap.MapWidth, minimap.MapHeight);
        _image = Image.CreateEmpty(minimap.MapWidth, minimap.MapHeight, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        _back = new HudCanvas { Name = "Back", Load = image, Visible = false };
        AddChild(_back);
        _mapNode = new Node2D { Name = "Map", Visible = false };
        AddChild(_mapNode);
        _front = new HudCanvas { Name = "Front", Load = image, Visible = false };
        AddChild(_front);
    }

    public void SetVisible(bool visible)
    {
        if (_hud == null || _back == null || _front == null || _mapNode == null) return;
        _hud.SetVisible(visible);
        _back.Visible = visible; _mapNode.Visible = visible; _front.Visible = visible;
    }

    /// <summary>The class grid, its image and the markers, while the map shows; call while the simulation is settled.
    /// The grid is read once per tick, without the minimap's markers (<see cref="MinimapHud.CaptureGrid"/>), and the image uploaded only when it changed.</summary>
    public void Capture(WorldState world, GameDatabase database, INavByteGrid navigation, PlayerId viewer)
    {
        if (_hud == null || _minimap == null || _view == null || _pixels == null || _image == null || _texture == null
            || !_hud.Visible) return;
        if (_minimap.CaptureGrid(world, _view, navigation, out _) && _pixels.Update(_minimap.Grid, _minimap.Palette))
        {
            _image.SetData(_pixels.Width, _pixels.Height, false, Image.Format.Rgba8, _pixels.Pixels);
            _texture.Update(_image);
        }
        _hud.Capture(world, database, viewer, ResolvePointer(GetViewport().GetMousePosition()));
    }

    /// <summary>The tile under a screen pixel (top-left origin).</summary>
    public TilePos ResolvePointer(Vector2 screenPosition)
    {
        if (_hud == null) return default;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.X <= 0 || size.Y <= 0) return default;
        return _hud.ResolvePointer((int)size.X, (int)size.Y, new System.Numerics.Vector2(screenPosition.X, screenPosition.Y));
    }

    /// <summary>Lays the map out for the viewport and presents it.</summary>
    public void Draw()
    {
        if (_hud == null || _back == null || _front == null || _mapNode == null || _texture == null || !_hud.Visible) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        ClassicFullMapLayout layout = _hud.Layout(width, height);
        var rect = new Rect2(layout.Origin.X, layout.Origin.Y, layout.Size.X, layout.Size.Y);
        if (rect != _mapRect) RecordMap(rect);
        var screen = new HudScreen(width, height);
        _backFrame.Clear(); _frontFrame.Clear();
        _hud.Build(screen, _backFrame, _frontFrame);
        _back.Present(_backFrame, Vector2.Zero, screen.Scale, 1);
        _front.Present(_frontFrame, Vector2.Zero, screen.Scale, 1);
    }

    /// <summary>The map image stretched over its rect, point-sampled as Unity's render target is.</summary>
    private void RecordMap(Rect2 rect)
    {
        if (!_mapItem.IsValid)
        {
            _mapItem = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(_mapItem, _mapNode!.GetCanvasItem());
            RenderingServer.CanvasItemSetDefaultTextureFilter(_mapItem, RenderingServer.CanvasItemTextureFilter.Nearest);
        }
        RenderingServer.CanvasItemClear(_mapItem);
        RenderingServer.CanvasItemAddTextureRect(_mapItem, rect, _texture!.GetRid());
        _mapRect = rect;
    }

    public override void _Input(InputEvent @event)
    {
        if (_hud == null || !_hud.Visible || @event is not InputEventMouseButton { Pressed: true } button) return;
        // Unity's runner gives every press to the open map (QueueHudInput) and acts on the left and right ones.
        GetViewport().SetInputAsHandled();
        FullMapPress press = FullMapHud.Press(button.ButtonIndex == MouseButton.Left, button.ButtonIndex == MouseButton.Right);
        Pressed?.Invoke(press, ResolvePointer(button.Position));
    }

    public override void _ExitTree()
    {
        if (_mapItem.IsValid) { RenderingServer.FreeRid(_mapItem); _mapItem = default; }
        _mapRect = default;
    }

    public override void _Notification(int what)
    {
        // Freed, not merely removed from the tree: a reparented map keeps working.
        if (what != NotificationPredelete) return;
        Pressed = null; _hud = null; _minimap = null; _view = null; _pixels = null;
        // The canvas item draws the texture, so it goes first.
        if (_mapItem.IsValid) { RenderingServer.FreeRid(_mapItem); _mapItem = default; }
        _texture?.Dispose(); _image?.Dispose(); _texture = null; _image = null;
        _back = null; _front = null; _mapNode = null;
    }
}
