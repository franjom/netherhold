// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The full map, the Godot counterpart of Unity's <c>ClassicFullMap</c>: <see cref="FullMapHud"/> (ADR-0805) drawn
/// by two <see cref="HudCanvas"/>es, the black backdrop under and the room icon, creature squares and legend over the map
/// image, which <see cref="FullMapImage"/> colours from the minimap's class grid (<see cref="ClassicMinimap.Model"/>, as
/// Unity's full map reads its minimap's) and this node only uploads (Unity blits the same image with a shader). The panel is in screen pixels. While it shows it takes every mouse press
/// from the panels under it; what a press does is the host's <see cref="HudPressRouter"/> (<see cref="FullMapHud.Press"/>).</summary>
public partial class ClassicFullMap : Node
{
    private readonly HudFrame _backFrame = new(), _frontFrame = new();
    private FullMapHud? _hud;
    private MinimapHud? _minimap;
    private FullMapImage? _pixels;
    /// <summary>The minimap captured since the image was last coloured.</summary>
    private bool _stale = true;
    private HudCanvas? _back, _front;
    private Node2D? _mapNode;
    private Rid _mapItem;
    private Image? _image;
    private ImageTexture? _texture;
    /// <summary>The map image's rect as last recorded; empty before the first draw.</summary>
    private Rect2 _mapRect;
    private SubViewport? _previewViewport;
    private Camera3D? _previewCamera;
    private TextureRect? _previewImage;

    public void PreparePreview(WorldScene world, Vector2 pointer)
    {
        world.PreviewPose = null;
        if (!Visible)
        {
            if (_previewViewport != null) _previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            if (_previewImage != null) _previewImage.Visible = false;
            return;
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        FullMapPreview preview = _hud!.Layout((int)size.X, (int)size.Y).Preview((int)size.X, (int)size.Y,
            new System.Numerics.Vector2(pointer.X, pointer.Y));
        if (_previewViewport == null)
        {
            _previewViewport = new SubViewport { Name = "FullMapPreview", World3D = world.GetWorld3D(), HandleInputLocally = false };
            AddChild(_previewViewport);
            world.PreviewRenderViewport = _previewViewport;
            if (world.MeasurePreviewRenderTime)
                RenderingServer.ViewportSetMeasureRenderTime(_previewViewport.GetViewportRid(), true);
            _previewCamera = new Camera3D { Current = true, Environment = world.CameraEnvironment, CullMask = uint.MaxValue & ~(1u << 18), Near = .05f, KeepAspect = Camera3D.KeepAspectEnum.Height };
            world.AttachOcclusion(_previewCamera);
            _previewViewport.AddChild(_previewCamera);
            _previewImage = new TextureRect { Name = "PreviewImage", MouseFilter = Control.MouseFilterEnum.Ignore,
                Texture = _previewViewport.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
            AddChild(_previewImage);
        }
        _previewViewport.Size = new Vector2I((int)preview.Size.X, (int)preview.Size.Y);
        _previewViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _previewImage!.Position = new Vector2(preview.Origin.X, preview.Origin.Y);
        _previewImage.Size = new Vector2(preview.Size.X, preview.Size.Y);
        _previewImage.Visible = true;
        RightHandedCamera basis = RightHandedFrame.Camera(preview.Pose);
        _previewCamera!.Transform = new Transform3D(new Basis(ToGodot(basis.X), ToGodot(basis.Y), ToGodot(basis.Z)), ToGodot(basis.Origin));
        _previewCamera.Fov = preview.Pose.VerticalFieldOfView;
        _previewCamera.Far = preview.Pose.FarClipDistance;
        world.PreviewPose = preview.Pose;
        world.PreviewViewport = new CameraViewport((int)preview.Size.X, (int)preview.Size.X, (int)preview.Size.Y);
    }
    private static Vector3 ToGodot(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    public bool Visible => _hud != null && _hud.Visible;

    /// <summary>Opens the map, hidden; call before it enters the tree.</summary>
    /// <param name="minimap">The class grid's source, which the minimap captures.</param>
    /// <param name="font">The legible bitmap font for a screen height (the legend).</param>
    /// <param name="playerName">A player's name by id (the legend).</param>
    /// <param name="image">A catalogue image by name (the room icons).</param>
    public void Open(MinimapHud minimap, Func<int, BitmapFont> font, Func<byte, string> playerName, Func<string, RgbaImage> image)
    {
        if (_hud != null) throw new InvalidOperationException("The full map is already open.");
        _minimap = minimap ?? throw new ArgumentNullException(nameof(minimap));
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

    /// <summary>The image and the markers, while the map shows; call after the minimap's capture, while the simulation is
    /// settled. The image is recoloured from the shared grid once after each capture, and uploaded only when it changed.</summary>
    /// <param name="gridCaptured">The minimap captured this frame (<see cref="ClassicMinimap.Capture"/>).</param>
    public void Capture(WorldState world, GameDatabase database, PlayerId viewer, bool gridCaptured, Vector2 pointer)
    {
        if (gridCaptured) _stale = true;
        if (_hud == null || _minimap == null || _pixels == null || _image == null || _texture == null || !_hud.Visible) return;
        if (_stale)
        {
            _stale = false;
            if (_pixels.Update(_minimap.Grid, _minimap.Palette))
            {
                _image.SetData(_pixels.Width, _pixels.Height, false, Image.Format.Rgba8, _pixels.Pixels);
                _texture.Update(_image);
            }
        }
        _hud.Capture(world, database, viewer, ResolvePointer(pointer));
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

    /// <summary>Whether the host's router takes this frame's presses for the open map (the live HUD state it routes on,
    /// not this node's drawn visibility, which updates only at the next draw); while it does, no panel under it gets one.</summary>
    public Func<bool>? HoldsPresses { get; set; }

    public override void _Input(InputEvent @event)
    {
        // Unity's runner gives every frame to the open map (QueueHudInput): the panels under it get no press. The router
        // reads the press from the window; this node and the router decide on the same state.
        if (@event is InputEventMouseButton { Pressed: true } && HoldsPresses?.Invoke() == true) GetViewport().SetInputAsHandled();
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
        HoldsPresses = null; _hud = null; _minimap = null; _pixels = null;
        // The canvas item draws the texture, so it goes first.
        if (_mapItem.IsValid) { RenderingServer.FreeRid(_mapItem); _mapItem = default; }
        _texture?.Dispose(); _image?.Dispose(); _texture = null; _image = null;
        _back = null; _front = null; _mapNode = null;
    }
}
