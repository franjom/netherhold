// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using DK2.Art.Textures;
using DK2.Core;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>A shader disc and ordered marker quads. Each quad explicitly rounds its composed colour to bytes,
/// retaining the independent oracle's alpha and overlap order before ordinary HUD texture filtering.</summary>
public partial class MinimapGpuDisc : Node
{
    private static readonly StringName DiameterName = "diameter", TargetName = "target_size", TurnName = "turn", ZoomName = "zoom",
        OriginName = "raster_origin", FitName = "map_fit", GridName = "grid_size", ClassesName = "classes",
        PaletteName = "palette", BackdropName = "backdrop", MarkerDataName = "marker_data";
    private readonly ShaderMaterial _base = Material("MinimapBase"), _markers = Material("MinimapMarkers");
    private readonly SubViewport _disc = Viewport(true);
    private readonly ColorRect _baseQuad = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly MultiMesh _mesh = new()
    {
        TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, UseCustomData = true,
        Mesh = new QuadMesh { Size = Vector2.One },
    };
    private readonly MinimapDiscGeometry _geometry = new();
    private Image? _classesImage, _paletteImage, _markerImage;
    private ImageTexture? _classesTexture, _paletteTexture, _rockTexture, _markerTexture;
    private byte[] _classes = [], _palette = [], _markerBytes = [];
    private MinimapZoom _geometryZoom;
    private FpPosition3 _geometryFocus;
    private float[] _instances = [];
    private int _capacity;
    private long _renders, _classUploads, _paletteUploads, _classBytes, _markerUploads;
    public int Diameter { get; private set; }
    public Texture2D Texture => _disc.GetTexture();

    public MinimapGpuDisc()
    {
        Name = "GpuDisc";
        _baseQuad.Material = _base;
        _disc.AddChild(_baseQuad);
        _disc.AddChild(new MultiMeshInstance2D { Multimesh = _mesh, Material = _markers });
        AddChild(_disc);
    }

    public override void _EnterTree()
    {
        if (Diameter > 0) _disc.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    public void Open(RgbaImage rock)
    {
        using var image = Image.CreateFromData(rock.Width, rock.Height, false, Image.Format.Rgba8, rock.Pixels);
        _rockTexture = ImageTexture.CreateFromImage(image);
        SetMapParameter(BackdropName, _rockTexture);
    }

    public void Render(int diameter, MinimapClassGrid grid, MinimapPalette palette, IReadOnlyList<MinimapMarker> markers,
        MinimapZoom zoom, FpPosition3 focus, Dk2Angle heading, TilePos? heart, GameTick tick, bool capture)
    {
        if (diameter <= 0 || diameter > 4096) throw new ArgumentOutOfRangeException(nameof(diameter));
        if (Diameter != diameter)
        {
            Diameter = diameter;
            // SubViewport requires 2x2; a logical one-pixel disc repeats that pixel across the target.
            int target = Math.Max(diameter, 2);
            _disc.Size = new Vector2I(target, target);
            _baseQuad.Size = new Vector2(target, target);
            _mesh.CustomAabb = new Aabb(Vector3.Zero, new Vector3(target, target, 1));
            SetMapParameter(TargetName, target);
            SetMapParameter(DiameterName, diameter);
        }
        if (capture || _classesImage == null) Capture(grid, palette);
        float angle = heading.Value * (MathF.PI * 2 / 2048);
        var turn = new Vector2(MathF.Sin(angle), MathF.Cos(angle));
        SetMapParameter(TurnName, turn);
        var fit = MinimapMapBounds.Fit(grid.Width, grid.Height);
        SetMapParameter(FitName, new Vector4I(fit.Left, fit.Top, fit.Width, fit.Height));
        SetMapParameter(GridName, new Vector2I(grid.Width, grid.Height));
        MinimapRasterPoint origin = zoom.IsWholeMap ? default : zoom.RasterOrigin(focus);
        SetMapParameter(OriginName, new Vector2I(origin.X, origin.Y));
        SetMapParameter(ZoomName, zoom.Level);
        if (capture || _markerTexture == null || zoom != _geometryZoom || (!zoom.IsWholeMap && focus != _geometryFocus))
        {
            _geometry.Build(grid, markers, zoom, focus, heart, tick);
            UploadMarkers(); _geometryZoom = zoom; _geometryFocus = focus;
        }
        _disc.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _renders++;
    }

    private void Capture(MinimapClassGrid grid, MinimapPalette palette)
    {
        int length = checked(grid.Width * grid.Height * 4);
        bool changed = _classes.Length != length || _classesImage == null
            || _classesImage.GetWidth() != grid.Width || _classesImage.GetHeight() != grid.Height;
        if (_classes.Length != length) _classes = new byte[length];
        ReadOnlySpan<MinimapColourClass> cells = grid.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            ushort value = cells[i].Index;
            changed |= _classes[i * 4] != (byte)value || _classes[i * 4 + 1] != (byte)(value >> 8);
            _classes[i * 4] = (byte)value; _classes[i * 4 + 1] = (byte)(value >> 8);
            _classes[i * 4 + 3] = 255;
        }
        if (changed || _classesImage == null)
        {
            Upload(ref _classesImage, ref _classesTexture, grid.Width, grid.Height, _classes);
            SetMapParameter(ClassesName, _classesTexture!);
            _classUploads++; _classBytes += length;
        }
        length = MinimapColourClass.Count * 4;
        changed = _palette.Length != length;
        if (changed) _palette = new byte[length];
        ReadOnlySpan<uint> colours = palette.Colours;
        for (int i = 0; i < colours.Length; i++)
        {
            uint colour = colours[i];
            byte r = (byte)(colour >> 16), g = (byte)(colour >> 8), b = (byte)colour;
            changed |= _palette[i * 4] != r || _palette[i * 4 + 1] != g || _palette[i * 4 + 2] != b;
            _palette[i * 4] = r; _palette[i * 4 + 1] = g; _palette[i * 4 + 2] = b; _palette[i * 4 + 3] = 255;
        }
        if (changed || _paletteImage == null)
        {
            Upload(ref _paletteImage, ref _paletteTexture, MinimapColourClass.Count, 1, _palette);
            SetMapParameter(PaletteName, _paletteTexture!); _paletteUploads++;
        }
    }

    private void UploadMarkers()
    {
        int count = _geometry.Quads.Count;
        if (_capacity < Math.Max(count, 1))
        {
            _capacity = Math.Max(count, Math.Max(16, _capacity * 2));
            _instances = new float[_capacity * 12]; _markerBytes = new byte[_capacity * 32];
            _mesh.InstanceCount = _capacity;
        }
        Span<float> data = MemoryMarshal.Cast<byte, float>(_markerBytes.AsSpan());
        for (int i = 0; i < count; i++)
        {
            MinimapDiscQuad q = _geometry.Quads[i]; int p = i * 12, d = i * 8;
            _instances[p] = 1; _instances[p + 5] = 1; _instances[p + 8] = i;
            data[d] = q.Centre.X; data[d + 1] = q.Centre.Y; data[d + 2] = q.Size.X; data[d + 3] = q.Size.Y;
            data[d + 4] = (q.ColourArgb >> 16) & 255; data[d + 5] = (q.ColourArgb >> 8) & 255;
            data[d + 6] = q.ColourArgb & 255; data[d + 7] = (int)q.Kind;
        }
        _mesh.VisibleInstanceCount = count;
        Upload(ref _markerImage, ref _markerTexture, 2, _capacity, _markerBytes, Image.Format.Rgbaf);
        _markers.SetShaderParameter(MarkerDataName, _markerTexture!);
        if (count > 0) { _mesh.Buffer = _instances; _markerUploads++; }
    }

    private static void Upload(ref Image? image, ref ImageTexture? texture, int width, int height, byte[] bytes, Image.Format format = Image.Format.Rgba8)
    {
        if (image == null || image.GetWidth() != width || image.GetHeight() != height)
        {
            image?.Dispose(); image = Image.CreateFromData(width, height, false, format, bytes);
            if (texture == null) texture = ImageTexture.CreateFromImage(image); else texture.SetImage(image);
        }
        else { image.SetData(width, height, false, format, bytes); texture!.Update(image); }
    }

    private static SubViewport Viewport(bool transparent) => new()
    {
        Size = new Vector2I(2, 2), Disable3D = true, TransparentBg = transparent, World2D = new World2D(),
        RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, UseHdr2D = false,
        CanvasItemDefaultTextureFilter = Godot.Viewport.DefaultCanvasItemTextureFilter.Nearest,
    };

    private static ShaderMaterial Material(string name) => new() { Shader = GD.Load<Shader>("res://Shaders/" + name + ".gdshader") };

    private void SetMapParameter(StringName name, Variant value)
    {
        _base.SetShaderParameter(name, value); _markers.SetShaderParameter(name, value);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        GD.Print("DK2.Godot minimap GPU: ", _renders, " renders, diameter ", Diameter, "; 0 CPU disc rasters, 0 disc uploads; ",
            _classUploads, " class uploads (", _classBytes, " B), ", _paletteUploads, " palette uploads, ", _markerUploads, " marker uploads");
        _classesTexture?.Dispose(); _paletteTexture?.Dispose(); _rockTexture?.Dispose();
        _classesImage?.Dispose(); _paletteImage?.Dispose(); _markerImage?.Dispose(); _markerTexture?.Dispose();
    }
}
