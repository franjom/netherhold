// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using DK2.Core;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Classic world lighting: the scene's sources in a <see cref="ClassicWorldLightCache"/>,
/// the visible tiles' tables published by
/// <see cref="ClassicLightPublisher"/> into <see cref="LightTables"/>, and each lit vertex summing them in the world shader
/// (Shaders/WorldLight.gdshaderinc, <see cref="ClassicVertexLight"/>), plus DK2's ambient and Godot's own fill and
/// distance fade.</summary>
public partial class WorldScene
{
    // Godot's own fill (audit open point 2), off by default: the owner's saved Unity scene (Dungeon.unity) has native
    // ambient on, which skips the fill block, so DK2's own light, the ambient keys and the distance fade remain (owner,
    // 2026-11-18). The fill, on with --light-fill, is Unity's ADR-0522 look: its sky fitted to Unity's default-skybox
    // frames (least squares over the texture-divided pixels of Level1's start frame), the scene's sun at 1.1, the tint,
    // unlit share and floor. The fade is ADR-0522's 0.5 to 1 of the far plane in both.
    private static readonly Vector3 FillSky = new(0.42f, 0.52f, 0.64f), FillEquator = new(0.47f, 0.52f, 0.61f),
        FillGround = new(0.30f, 0.30f, 0.32f);
    private static readonly Vector3 FillSun = new(1.1f, 1.1f, 1.1f);
    /// <summary>Unity's light's direction toward it, (0.321, 0.766, -0.557) (Dungeon.unity's rotation), in Godot's frame.</summary>
    private static readonly Vector3 FillSunDirection = new Vector3(0.3214f, 0.7660f, 0.5567f).Normalized();
    private static readonly Vector3 FillTint = new(0.55f, 0.44f, 0.28f);
    private const float UnlitFill = 0.25f, FillFloor = 0.5f, FadeStart = 0.5f, FadeEnd = 1f;

    private static readonly StringName LightTablesGlobal = "dk2_light_tables", LightGridGlobal = "dk2_light_grid",
        AmbientGlobal = "dk2_ambient", FillSkyGlobal = "dk2_fill_sky", FillEquatorGlobal = "dk2_fill_equator",
        FillGroundGlobal = "dk2_fill_ground", FillSunGlobal = "dk2_fill_sun", FillSunDirectionGlobal = "dk2_fill_sun_direction",
        FillTintGlobal = "dk2_fill_tint", UnlitFillGlobal = "dk2_unlit_fill", FillFloorGlobal = "dk2_fill_floor",
        FadeGlobal = "dk2_fade", HoverRedGlobal = "dk2_hover_red";

    /// <summary>The hover's red pass: the overlay look's colour (RedTexture's), which the world shader adds.</summary>
    private static readonly NVector3 HoverRed = new(ObjectMaterialLook.HoverOverlay().Colour.X,
        ObjectMaterialLook.HoverOverlay().Colour.Y, ObjectMaterialLook.HoverOverlay().Colour.Z);

    private ClassicWorldLightCache? _worldLights;
    private ClassicLightPublisher? _lightPublisher;
    private LightTables? _lightTables;
    private readonly ClassicLightFlags _torchFlags = ClassicLightFlagsRules.Torch(cheapLighting: false);
    private ClassicPointLight? _handLight;
    private WorldView? _lightView;
    private WorldState? _lightWorld;
    private float _lightFraction = 1f;
    private float _fadeFar = -1;
    // What the lighting allocated each frame after the first 60, and its time, for the exit report.
    private long _lightBytes, _lightListBytes, _lightMovedBytes, _lightTicks, _lightMaxTicks;
    private int _lightFrames, _lightPublishes, _lightMovedFrames;
    private ClassicCameraPose _lightPose;
    private CameraViewport _lightViewport;

    /// <summary>Disable torch lights for the render probe (<c>--light-probe-no-torches</c>).</summary>
    public bool ProbeWithoutTorches { get; set; }

    /// <summary>Enable fill lighting with <c>--light-fill</c>.</summary>
    public bool Fill { get; set; }

    /// <summary>DK2's light evaluated per pixel (<c>--light-perpixel</c>): the same tables, formulas, clamp and pack as the
    /// classic per-vertex light, in the fragment step from the interpolated position and normals (DK2_PER_PIXEL in
    /// Shaders/World.gdshaderinc). Set before <see cref="Open"/>; off, the light is evaluated per vertex.</summary>
    public bool PerPixelLight { get; set; }

    /// <summary>Initialize the light tables for a <paramref name="width"/> by <paramref name="height"/> map.
    /// Start with zero ambient light, as in <c>RenderSink_Init</c>.</summary>
    private void OpenLighting(WorldState world, WorldView view, int width, int height)
    {
        _lightWorld = world;
        _lightView = view;
        _worldLights = new ClassicWorldLightCache(width, height);
        _lightPublisher = new ClassicLightPublisher(_worldLights, width, height,
            new PortalCentreLights(new EffectTime(_frameMilliseconds)), _torchFlags);
        _lightTables = new LightTables(width, height);
        Global(LightTablesGlobal, RenderingServer.GlobalShaderParameterType.Sampler2Darray, _lightTables.Texture);
        Global(LightGridGlobal, RenderingServer.GlobalShaderParameterType.Ivec2, new Vector2I(width, height));
        Global(AmbientGlobal, RenderingServer.GlobalShaderParameterType.Vec3, Vector3.Zero);
        // Set every fill term to zero while fill is disabled.
        Global(FillSkyGlobal, RenderingServer.GlobalShaderParameterType.Vec3, Fill ? FillSky : Vector3.Zero);
        Global(FillEquatorGlobal, RenderingServer.GlobalShaderParameterType.Vec3, Fill ? FillEquator : Vector3.Zero);
        Global(FillGroundGlobal, RenderingServer.GlobalShaderParameterType.Vec3, Fill ? FillGround : Vector3.Zero);
        Global(FillSunGlobal, RenderingServer.GlobalShaderParameterType.Vec3, Fill ? FillSun : Vector3.Zero);
        Global(FillSunDirectionGlobal, RenderingServer.GlobalShaderParameterType.Vec3, FillSunDirection);
        Global(FillTintGlobal, RenderingServer.GlobalShaderParameterType.Vec3, FillTint);
        Global(UnlitFillGlobal, RenderingServer.GlobalShaderParameterType.Float, UnlitFill);
        Global(FillFloorGlobal, RenderingServer.GlobalShaderParameterType.Float, FillFloor);
        Global(FadeGlobal, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(FadeStart, FadeEnd, 1f));
        // The hover's red pass (Shaders/World.gdshaderinc): RedTexture's colour, as the overlay material drew it.
        Global(HoverRedGlobal, RenderingServer.GlobalShaderParameterType.Vec3, ToGodot(HoverRed));
    }

    /// <summary>A global the shaders read, added once per run and set by every later scene (a reload); Godot's own list of
    /// globals is for the editor only.</summary>
    private static void Global(StringName name, RenderingServer.GlobalShaderParameterType type, Variant value)
    {
        if (Globals.Add(name.ToString()))
        {
            RenderingServer.GlobalShaderParameterAdd(name, type, value);
        }
        else
        {
            RenderingServer.GlobalShaderParameterSet(name, value);
        }
    }

    private static readonly HashSet<string> Globals = new();

    /// <summary>DK2's ambient level from the ambient keys (<see cref="ClassicAmbientLight.Normalized"/>), grey.</summary>
    public void SetAmbient(float level)
    {
        RenderingServer.GlobalShaderParameterSet(AmbientGlobal, new Vector3(level, level, level));
    }

    /// <summary>The Hand's light, drawn from the next <see cref="FinishFrame"/>.</summary>
    public void SetHandLight(ClassicPointLight? light)
    {
        _handLight = light;
    }

    /// <summary>A tile's terrain light and whether it takes the full fill.</summary>
    private void SetTileLight(int tileIndex, in TileView tile)
    {
        _worldLights?.SetTerrain(tile.Position, tile.PointLight);
        _lightTables?.SetFill(tileIndex, tile.LitAsExplored);
    }

    private void SetTorchLight(int floorTileIndex, in TorchView? torch)
    {
        if (_worldLights == null || _lightWorld == null)
        {
            return;
        }

        var tile = new TilePos(floorTileIndex % _lightWorld.Width, floorTileIndex / _lightWorld.Width);
        ClassicPointLight? light = torch is TorchView lit && !ProbeWithoutTorches ? ClassicPointLight.FromTorch(lit, _torchFlags) : null;
        _worldLights.SetTorch(tile, light);
        SetShadowTorch(floorTileIndex, light);
    }

    /// <summary>Once the camera is placed, publish lights for entrances, objects at the drawn fraction,
    /// and the Hand from the last frame's pick. Build the visible tiles' tables from the placed pose
    /// and the distance fade's far plane. AdvanceEffects
    /// has already published effect lights. Allocation-free once warm while the camera holds still.</summary>
    private void PrepareLighting()
    {
        if (_lightPublisher == null || _worldLights == null || _lightTables == null || _lightView == null || _lightWorld == null
            || View == null)
        {
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp(), before = GC.GetAllocatedBytesForCurrentThread();
        var now = new EffectTime(_frameMilliseconds);
        _lightPublisher.PublishPortalLights(_lightView.PortalLightTiles(_lightWorld), now);
        long listed = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<ObjectView> lit = _lightView.LightObjects(_lightWorld);
        listed = GC.GetAllocatedBytesForCurrentThread() - listed;
        _worldLights.UpdateObjects(lit, _lightFraction, _torchFlags.IsCheapTorch());
        _worldLights.UpdateHand(_handLight);
        ClassicCameraPose placed = _placed;
        var pose = new ClassicCameraPose(placed.Position, placed.Forward, placed.Up, placed.VerticalFieldOfView,
            placed.FarClipDistance, 0, 0);
        float viewDistance = ScriptedView?.Pose is CameraPathPose path ? path.ViewDistance
            : FrontEndPose is CameraPathPose station ? station.ViewDistance : ClassicCamera?.NativeViewDistance ?? 0;
        // A moved camera finds its visible tiles again (ClassicLightVisibility), which allocates; counted apart.
        bool moved = !pose.Equals(_lightPose) || _placedViewport != _lightViewport;
        _lightPose = pose;
        _lightViewport = _placedViewport;
        switch (_lightPublisher.Prepare(pose, _placedViewport, viewDistance, PreviewPose, PreviewViewport))
        {
            case ClassicLightPublication.Unchanged:
                _lightTables.Flush();
                break;
            case ClassicLightPublication.Changed:
                _lightPublisher.Publish(_lightTables);
                _lightPublishes++;
                break;
        }
        UpdatePossessionRendering(placed);
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started, bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (_lightFrames++ >= DrawWarmUp && moved) { _lightMovedBytes += bytes; _lightMovedFrames++; }
        else if (_lightFrames > DrawWarmUp)
        {
            _lightBytes += bytes;
            _lightListBytes += listed;
            _lightTicks += ticks;
            _lightMaxTicks = Math.Max(_lightMaxTicks, ticks);
        }
    }

    private void ReportLighting()
    {
        GD.Print("DK2.Godot lighting: ", _lightFrames, " frames, ", _lightPublishes, " published, ", _lightTables?.RowsUploaded ?? 0,
            " table rows uploaded (in ", Milliseconds(_lightTables?.UploadTicks ?? 0), " ms), ", _worldMaterialsMade,
            " world materials; after the first ", DrawWarmUp, " frames ", _lightBytes, " B allocated in ",
            Math.Max(0, _lightFrames - DrawWarmUp - _lightMovedFrames), " with the camera still (", _lightListBytes,
            " B of it by WorldView.LightObjects) and ", _lightMovedBytes, " B in ", _lightMovedFrames, " that moved it; ",
            Milliseconds(_lightTicks / Math.Max(1, _lightFrames - DrawWarmUp - _lightMovedFrames)), " ms mean and ",
            Milliseconds(_lightMaxTicks), " ms max with it still");
    }

    /// <summary>The shader light tables: a float texture array with a layer per row of tiles,
    /// <see cref="TexelsPerTile"/> texels a tile: the count and
    /// the fill weight, then two texels a source (position in Godot's frame and radius squared; colour and the packed
    /// intensity and flags, <see cref="ClassicTileLightSource"/>). A layer wraps every <see cref="TilesPerTextureRow"/>
    /// tiles onto a new texel row (<see cref="ClassicLightTableLayout"/>), so it stays within the 4,096 texels Vulkan
    /// guarantees. A changed layer is uploaded alone at the next flush.</summary>
    private sealed class LightTables : ITileLightSink
    {
        public const int TexelsPerTile = ClassicLightTableLayout.TexelsPerTile;
        public const int TilesPerTextureRow = ClassicLightTableLayout.TilesPerTextureRow;
        private readonly int _width, _height, _rowFloats, _layerWidth, _layerHeight;
        private readonly float[] _texels;
        private readonly bool[] _dirty;
        private readonly Image _row;
        private bool _anyDirty;

        public LightTables(int width, int height)
        {
            _width = width;
            _height = height;
            _layerWidth = ClassicLightTableLayout.LayerWidth(width);
            _layerHeight = ClassicLightTableLayout.LayerHeight(width);
            _rowFloats = _layerWidth * _layerHeight * 4;
            _texels = new float[_rowFloats * height];
            _dirty = new bool[height];
            var layers = new Godot.Collections.Array<Image>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    _texels[Base(x, y) + 1] = 1f;
                }

                layers.Add(Image.CreateFromData(_layerWidth, _layerHeight, false, Image.Format.Rgbaf,
                    MemoryMarshal.AsBytes(_texels.AsSpan(y * _rowFloats, _rowFloats)).ToArray()));
            }
            _row = Image.CreateEmpty(_layerWidth, _layerHeight, false, Image.Format.Rgbaf);
            Texture = new Texture2DArray();
            Texture.CreateFromImages(layers);
        }

        public Texture2DArray Texture { get; }
        public long RowsUploaded { get; private set; }
        public long UploadTicks { get; private set; }

        public void Set(ClassicLightTable table)
        {
            int y = table.Tile.Y;
            int at = Base(table.Tile.X, y);
            ReadOnlySpan<ClassicPointLight> lights = table.Lights;
            bool changed = Write(at, lights.Length);
            for (int slot = 0; slot < ClassicLightTable.MaximumSources; slot++)
            {
                int texel = at + 4 + (slot * 8);
                if (slot < lights.Length)
                {
                    var source = ClassicTileLightSource.Of(lights[slot]);
                    // Godot's frame: Z negated (RightHandedFrame).
                    changed |= Write(texel, source.PositionRadiusSquared.X) | Write(texel + 1, source.PositionRadiusSquared.Y)
                        | Write(texel + 2, -source.PositionRadiusSquared.Z) | Write(texel + 3, source.PositionRadiusSquared.W)
                        | Write(texel + 4, source.Colour.X) | Write(texel + 5, source.Colour.Y) | Write(texel + 6, source.Colour.Z)
                        | Write(texel + 7, source.IntensityFlags);
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        changed |= Write(texel + i, 0f);
                    }
                }
            }
            if (changed)
            {
                MarkDirty(y);
            }
        }

        public void Remove(TilePos tile)
        {
            int at = Base(tile.X, tile.Y);
            if (_texels[at] == 0f)
            {
                return;
            }

            _texels[at] = 0f;
            Array.Clear(_texels, at + 4, ClassicLightTable.MaximumSources * 8);
            MarkDirty(tile.Y);
        }

        public void SetFill(int tileIndex, bool lit)
        {
            int y = tileIndex / _width;
            if (Write(Base(tileIndex % _width, y) + 1, lit ? 1f : 0f))
            {
                MarkDirty(y);
            }
        }

        public void Flush()
        {
            if (!_anyDirty)
            {
                return;
            }

            _anyDirty = false;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int y = 0; y < _height; y++)
            {
                if (!_dirty[y])
                {
                    continue;
                }

                _dirty[y] = false;
                _row.SetData(_layerWidth, _layerHeight, false, Image.Format.Rgbaf,
                    MemoryMarshal.AsBytes(new ReadOnlySpan<float>(_texels, y * _rowFloats, _rowFloats)));
                Texture.UpdateLayer(_row, y);
                RowsUploaded++;
            }
            UploadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        }

        private int Base(int x, int y)
        {
            return ClassicLightTableLayout.FloatIndex(new TilePos(x, y), 0, _width);
        }

        private bool Write(int index, float value)
        {
            if (_texels[index] == value)
            {
                return false;
            }

            _texels[index] = value;
            return true;
        }

        private void MarkDirty(int y)
        {
            _dirty[y] = true;
            _anyDirty = true;
        }
    }

    /// <summary>A gamma table of <paramref name="exponent"/> over display values, using <c>pow(c, 30 / Gamma)</c>.
    /// Fold it into the camera's colour correction table so the 3D view's brightness changes with the HUD's
    /// canvas untouched, at no cost a frame; null is the default gamma.</summary>
    public void SetBrightness(float? exponent)
    {
        float value = exponent ?? 1f;
        if (_camera == null || value == _brightness)
        {
            return;
        }

        _brightness = value;
        _camera.Environment.AdjustmentColorCorrection = GammaTable(value);
    }

    private float _brightness = 1f;
}
