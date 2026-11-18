// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using System.Runtime.InteropServices;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The 3D world under <see cref="InGameHud"/> (plan steps 3a to 3c): the terrain as <see cref="TerrainTileBuilder"/>
/// builds it and <see cref="TerrainChunkScheduler"/> schedules it, one <see cref="MeshInstance3D"/> per 16x16-tile chunk
/// with a surface per <see cref="TerrainMaterialKey"/> drawn as <see cref="TerrainMaterialLook"/> describes it, and the
/// waving water and lava (<see cref="LiquidSurfaceField"/>), lit by DK2's vertex light (WorldScene.Lighting.cs), seen from the classic
/// camera; blended faces go back to front through <see cref="BlendedTerrainStream{TMaterial}"/> (step 3d, <see cref="RunSlot"/>).
/// Everything crosses into Godot's right-handed frame through <see cref="RightHandedFrame"/>. Objects, structures and
/// torches are step 3f's (WorldScene.Objects.cs), creatures step 3g's (WorldScene.Creatures.cs), entity hover step 3h's (WorldScene.Hover.cs), lighting step 3i's (WorldScene.Lighting.cs). The
/// host presents the world once a frame (<see cref="Present"/>), draws it (<see cref="DrawFrame"/>), moves the camera's
/// <see cref="Focus"/> and places it (<see cref="PlaceCamera"/>), then finishes it (<see cref="FinishFrame"/>).</summary>
public partial class WorldScene : Node3D, IRenderTarget, ITerrainChunkSink
{
    /// <summary>Unity's camera near plane (Dungeon.unity), which the frustum is sized at.</summary>
    private const float NearPlane = 0.3f;

    private static readonly StringName AlbedoParameter = "albedo_texture", ColourParameter = "base_colour",
        AddedLightParameter = "added_light";

    private InstallArtProvider? _art;
    private TerrainLattice? _lattice;
    private CageRibbon? _cage;
    private ScenePresenter? _presenter;
    private TerrainChunkScheduler? _terrain;
    private MeshInstance3D?[] _chunks = [];
    private LiquidSurfaceField? _liquids;
    private LiquidLayer? _water, _lava;
    private float _seconds;

    private readonly Dictionary<TerrainMaterialKey, SurfaceBuffer> _surfaceOf = new();
    private readonly List<SurfaceBuffer> _surfaces = new();
    private readonly List<SurfaceBuffer> _spareSurfaces = new();
    private int[] _remap = new int[256], _stamp = new int[256];
    private int _generation;

    // The blended stream: a chunk's blended faces over its tiles' vertices, per material in first-use order across
    // rebuilds (as Unity's TerrainChunks keeps one dictionary), and the run slots that draw the stream.
    private BlendedTerrainStream<ShaderMaterial>? _blended;
    private readonly List<BlendedVertex> _blendedVertices = new();
    private readonly Dictionary<ShaderMaterial, List<int>> _blendedByMaterial = new();
    private readonly HashSet<ShaderMaterial> _blendedMaterials = new(), _overlayMaterials = new();
    private readonly List<RunSlot> _runSlots = new();
    private readonly RunSlot.Scratch _runScratch = new();
    private Aabb _worldBox;
    // The re-sorting frames after the first (which pays the JIT): time, the most, and what they allocated.
    // The re-sorting frames, for the exit report: time after the first (which pays the JIT), the most, and what the
    // frames after the warm-up allocated.
    private const int BlendedWarmUp = 60;
    private ViewFrustum _frustum;
    private bool _frustumStale = true;
    private long _blendedTicks, _blendedMaxTicks, _blendedFirstTicks = -1, _blendedWarmUpBytes, _blendedLateBytes;
    private int _blendedRefreshes, _blendedMaxRuns, _blendedMaxTriangles;

    private readonly Dictionary<TerrainMaterialKey, ShaderMaterial> _keyMaterials = new();
    private readonly Dictionary<TerrainMaterialLook, ShaderMaterial> _lookMaterials = new();
    private readonly Dictionary<(KmfBlend, bool, bool, bool, bool), Shader> _shaders = new();
    private readonly Dictionary<string, Texture2D> _textures = new();
    private ImageTexture? _white;

    // Allocation on the frame's thread inside Present, split by whether the terrain changed that frame.
    private long _steadyBytes, _changeBytes;
    private int _steadyFrames, _changeFrames;
    private bool _changed;
    private readonly int[] _collectionsAtOpen = new int[3];

    private Camera3D? _camera;
    private ClassicCameraControls? _controls;
    private ClassicCameraPose _placed;
    private CameraViewport _placedViewport;

    /// <summary>The ordinary camera's controls over the map's focus bounds (<see cref="ClassicCameraSetup.Create"/>); null
    /// when the level has no dungeon camera preset.</summary>
    public ClassicCameraControls? Controls => _controls;

    /// <summary>The ordinary camera the controls move; null with them.</summary>
    public ClassicCamera? ClassicCamera { get; private set; }

    /// <summary>The ordinary camera's focus, in the renderers' frame; drawn at the next <see cref="PlaceCamera"/>.</summary>
    public NVector3 Focus { get; set; }

    /// <summary>The camera as last placed, as the minimap reads it; null before the first placement or without a camera.</summary>
    public MinimapCameraView? View { get; private set; }

    /// <summary>Builds the level's terrain and the camera at <paramref name="focus"/> (the level start's), from the
    /// install's models and textures (<see cref="InstallArtProvider"/>).</summary>
    public void Open(string install, CampaignLevelData level, WorldState world, WorldView view, PlayerId viewer,
        NVector3 focus, int scrollSpeed)
    {
        _art = InstallArtProvider.Open(install, level.Database);
        // DungeonRunner's defaults: the lattice bends the terrain, with DK2's high walls.
        var lattice = new TerrainLattice(level.Database, world, viewer) { HighWalls = true };
        _lattice = lattice;
        _terrain = new TerrainChunkScheduler(new InstallTerrainPieces(_art), world.Width, world.Height, lattice);
        _chunks = new MeshInstance3D?[_terrain.ChunkCount];
        _liquids = new LiquidSurfaceField(world.Width, world.Height);
        _water = new LiquidLayer(_liquids.Water);
        _lava = new LiquidLayer(_liquids.Lava);
        _blended = new BlendedTerrainStream<ShaderMaterial>(_overlayMaterials.Contains);
        // Every run instance sorts from the origin; the box only keeps Godot from culling it (the stream culls).
        _worldBox = new Aabb(new Vector3(-1, -4, -world.Height - 1), new Vector3(world.Width + 2, 16, world.Height + 2));
        _cage = new CageRibbon(GetWorld3D().Scenario, _worldBox);
        // Before any material: the lit shaders read the lighting's globals.
        OpenLighting(world, view, world.Width, world.Height);
        OpenShadows();
        using (Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
        {
            white.Fill(Colors.White);
            _white = ImageTexture.CreateFromImage(white);
        }

        // The install, then the placeholder, as Unity's chain without its optional folders. The first present builds
        // every chunk inline, so the level appears whole.
        OpenObjects(_art, level.Database, world.Width, world.Height);
        _presenter = new ScenePresenter(view, AssetProviderChain.Standard(install: _art), this, new TorchLayout(level.Database));
        _presenter.Present(world);
        for (int generation = 0; generation < _collectionsAtOpen.Length; generation++)
            _collectionsAtOpen[generation] = GC.CollectionCount(generation);

        CameraThing? profile = ClassicCameraSetup.Preset(
            GameDatabaseLoader.LoadCameraPresets(install, level.Name), ClassicCameraMode.Dungeon);
        if (profile == null)
        {
            GD.PushError("DK2.Godot: the level has no dungeon camera preset; no 3D camera.");
            return;
        }
        ClassicCameraSetup setup = ClassicCameraSetup.Create(profile, ClassicCameraSetup.DefaultZoomOutLimitScale,
            GameSessionStarter.CameraTicksPerSecond(level), scrollSpeed, level.Players, viewer, world.Width, world.Height);
        _controls = setup.Controls;
        ClassicCamera = setup.Camera;
        Focus = focus;
        _camera = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Frustum,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = NearPlane,
        };
        _camera.Environment = GammaOutput();
        AddChild(_camera);
        _camera.MakeCurrent();
        GetViewport().SizeChanged += PlaceCamera;
        PlaceCamera();
    }

    /// <summary>The bottom panel's collapse moves the view axis (Unity's runner's <c>CollapsedChanged</c> handler,
    /// <see cref="ClassicCamera.ProjectionCentreFromTop"/>); drawn at the next <see cref="PlaceCamera"/>.</summary>
    public void SetPanelCollapsed(bool collapsed)
    {
        if (ClassicCamera == null) return;
        ClassicCamera.ProjectionCentre = ClassicCamera.ProjectionCentreFromTop(
            collapsed ? ClassicCamera.CollapsedPanelTop : ClassicCamera.ExpandedPanelTop);
    }

    /// <summary>The classic camera's pose at <see cref="Focus"/> for the current viewport, through the frame change, with
    /// Unity's off-centre projection (<c>projection[1,2] = -shift</c>) as a frustum offset; the camera is written only
    /// when the pose changed. Allocation-free.</summary>
    public void PlaceCamera()
    {
        if (_camera == null || ClassicCamera == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0) return;
        var viewport = new CameraViewport(width, width, height);
        ClassicCameraPose pose = ClassicCamera.Pose(Focus, viewport);
        if (View != null && pose == _placed && viewport == _placedViewport) return;
        _placed = pose; _placedViewport = viewport;
        _frustumStale = true;
        View = MinimapCameraView.Of(pose, viewport);
        (float frustumHeight, float offset) = RightHandedFrame.Frustum(pose.VerticalFieldOfView, NearPlane, pose.ProjectionCentreShift);
        _camera.Size = frustumHeight;
        _camera.FrustumOffset = new Vector2(0, offset);
        _camera.Far = Math.Max(NearPlane + 0.01f, pose.FarClipDistance);
        RightHandedCamera basis = RightHandedFrame.Camera(pose);
        _camera.Transform = new Transform3D(new Basis(ToGodot(basis.X), ToGodot(basis.Y), ToGodot(basis.Z)), ToGodot(basis.Origin));
    }

    /// <summary>The ray through a screen pixel (top-left origin), in the renderers' frame, as Unity's <c>ScreenPointToRay</c>
    /// casts it through the off-centre frustum (<see cref="MinimapCameraView.RayDirection"/>), from the camera as last
    /// placed (<see cref="PlaceCamera"/>); false before the first placement. Not <see cref="Camera3D.ProjectRayNormal"/>:
    /// 4.8's assumes a symmetric frustum and drops the frustum offset (<c>Projection::get_viewport_half_extents</c>, c51ff06e8).</summary>
    public bool TryPickRay(Vector2 screen, out NVector3 origin, out NVector3 direction)
    {
        origin = direction = default;
        if (View is not MinimapCameraView view || _placedViewport.Height <= 0) return false;
        origin = view.Position;
        direction = view.RayDirection(screen.X / _placedViewport.Width, 1f - screen.Y / _placedViewport.Height);
        return true;
    }

    /// <summary>The selection cage over <paramref name="area"/> (<see cref="TileHoverOutline.Build"/>), turned to the placed
    /// camera; null hides it. Allocation-free once its buffers have grown.</summary>
    public void DrawCage(TileRectangle? area, bool blue, long milliseconds)
    {
        if (_cage == null) return;
        if (area is not TileRectangle shown || View is not MinimapCameraView view) { _cage.Hide(); return; }
        _cage.Show(shown, blue, view.Position, _lattice, milliseconds);
        // After FinishFrame placed this frame's eye, so the key is this frame's.
        _cage.Sort(this);
    }

    public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture) => SetTile(tileIndex, tile);

    public void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture) => SetTile(tileIndex, tile);

    private void SetTile(int tileIndex, in TileView tile)
    {
        SetTileLight(tileIndex, tile);
        _liquids!.Set(tileIndex, tile);
        _latticeDirty?.Touch(tileIndex);
        _terrain!.Set(tileIndex, tile);
    }

    /// <summary>Pushes the settled world's changes (<see cref="ScenePresenter.Present"/>) and waves the liquids at
    /// <paramref name="seconds"/> of engine time; a changed tile's chunks are built on the thread pool and uploaded by a
    /// later present within the flush budget. Counts what it allocates, reported when the node leaves the tree.</summary>
    public void Present(WorldState world, double seconds)
    {
        if (_presenter == null || _terrain == null) return;
        _seconds = (float)seconds;
        _changed = _terrain.PendingChunks > 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        _presenter.Present(world);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (_changed || _presenter.TilesPushed > 0)
        {
            _changeFrames++;
            _changeBytes += bytes;
        }
        else
        {
            _steadyFrames++;
            _steadyBytes += bytes;
        }
    }

    /// <summary>The scheduler's flush (the <see cref="ITerrainChunkSink"/> calls below upload), then the liquids' wave; the
    /// blended stream is sorted by <see cref="FinishFrame"/>, once the frame's camera is placed.</summary>
    public void Flush()
    {
        _terrain!.Flush(this);
        _liquids!.Animate(_seconds);
        _water!.Upload(this);
        _lava!.Upload(this);
    }

    void ITerrainChunkSink.BeginChunk(int chunk)
    {
        _changed = true;
        foreach (SurfaceBuffer surface in _surfaces) _spareSurfaces.Add(surface);
        _surfaces.Clear();
        _surfaceOf.Clear();
        _blendedVertices.Clear();
        foreach (List<int> list in _blendedByMaterial.Values) list.Clear();
    }

    void ITerrainChunkSink.AddTile(int tileIndex, TerrainTileGeometry geometry) => GatherTile(geometry);

    /// <summary>Each opaque key's gathered vertices as one surface of the chunk's mesh; the blended ones join the stream.</summary>
    void ITerrainChunkSink.EndChunk(int chunk)
    {
        _blended!.BeginChunk(chunk, _blendedVertices);
        foreach (KeyValuePair<ShaderMaterial, List<int>> entry in _blendedByMaterial)
            if (entry.Value.Count != 0) _blended.AddChunkTriangles(entry.Key, entry.Value);
        _blended.EndChunk();

        MeshInstance3D? instance = _chunks[chunk];
        var mesh = instance?.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        foreach (SurfaceBuffer surface in _surfaces)
        {
            if (surface.IndexCount == 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface.ToArrays(), null, null, TerrainCustomFormat);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, MaterialFor(surface.Key));
        }
        if (instance == null && mesh.GetSurfaceCount() > 0)
        {
            instance = new MeshInstance3D { Name = "Chunk" + chunk, Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            if (TorchShadowsActive) instance.Layers = ShadowCasterLayers;
            AddChild(instance);
            _chunks[chunk] = instance;
        }
    }

    private void GatherTile(TerrainTileGeometry geometry)
    {
        if (_remap.Length < geometry.VertexCount)
        {
            _remap = new int[geometry.VertexCount * 2];
            _stamp = new int[geometry.VertexCount * 2];
        }
        int blendedStart = -1;
        for (int s = 0; s < geometry.KeyCount; s++)
        {
            TerrainMaterialKey key = geometry.Keys[s];
            ShaderMaterial material = MaterialFor(key);
            if (_blendedMaterials.Contains(material))
            {
                // Unity's chunk: the tile's vertices in the renderers' frame once, its blended triangles numbered from them.
                if (blendedStart < 0)
                {
                    blendedStart = _blendedVertices.Count;
                    for (int i = 0; i < geometry.VertexCount; i++)
                        _blendedVertices.Add(new BlendedVertex(geometry.Positions[i], geometry.Normals[i], geometry.Uvs[i],
                            geometry.LightAnchor, VertexColour.White));
                }
                if (!_blendedByMaterial.TryGetValue(material, out List<int>? list)) _blendedByMaterial.Add(material, list = new List<int>());
                TerrainIndexList triangles = geometry.Triangles[s];
                for (int i = 0; i < triangles.Count; i++) list.Add(blendedStart + triangles.Items[i]);
                continue;
            }
            if (!_surfaceOf.TryGetValue(key, out SurfaceBuffer? surface))
            {
                surface = TakeSurface(key);
                _surfaceOf.Add(key, surface);
                _surfaces.Add(surface);
            }
            _generation++;
            surface.Append(geometry, geometry.Triangles[s], _remap, _stamp, _generation);
        }
    }

    private SurfaceBuffer TakeSurface(in TerrainMaterialKey key)
    {
        SurfaceBuffer surface;
        if (_spareSurfaces.Count > 0)
        {
            surface = _spareSurfaces[^1];
            _spareSurfaces.RemoveAt(_spareSurfaces.Count - 1);
        }
        else surface = new SurfaceBuffer();
        surface.Reset(key);
        return surface;
    }

    /// <summary>The material a key draws with: one per <see cref="TerrainMaterialLook"/>, shared by every key that looks
    /// the same.</summary>
    private ShaderMaterial MaterialFor(in TerrainMaterialKey key)
    {
        if (_keyMaterials.TryGetValue(key, out ShaderMaterial? material)) return material;
        TerrainMaterialLook look = TerrainMaterialLook.For(key);
        if (!_lookMaterials.TryGetValue(look, out material))
        {
            material = new ShaderMaterial { Shader = ShaderFor(look.Blend, look.FullLight, look.WritesDepth, model: false, environment: false) };
            material.SetShaderParameter(AlbedoParameter, TextureFor(look.Texture));
            material.SetShaderParameter(ColourParameter, new Color(look.Colour.X, look.Colour.Y, look.Colour.Z, look.Colour.W));
            material.SetShaderParameter(AddedLightParameter, ToGodot(look.AddedLight));
            _lookMaterials.Add(look, material);
            // Unity's split: render queue Transparent, which every non-opaque look takes.
            if (look.Blend != KmfBlend.Opaque) _blendedMaterials.Add(material);
        }
        // Unity's DK2TagOverlay tag: the dig tag's and the tutorial's additive pass.
        if (key.IsOverlay) _overlayMaterials.Add(material);
        _keyMaterials.Add(key, material);
        return material;
    }

    /// <summary>The custom attribute layouts: a terrain vertex's table anchor (CUSTOM0 as two floats), a model's bound sphere
    /// and its anchor and light kind (CUSTOM0 and CUSTOM1 as four), as Unity's TEXCOORD1-3.</summary>
    private const Mesh.ArrayFormat TerrainCustomFormat =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
    private const Mesh.ArrayFormat ModelCustomFormat =
        (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift));

    /// <summary>One shader per blend, light, vertex kind and environment pass: the texture times the colour, times the
    /// vertex's DK2 light (<see cref="LitVertex"/>) unless at full light. The colour buffer holds the stored bytes over 255
    /// and blends them there, as DK2 and Unity's gamma space do (<see cref="GammaOutput"/>). Opaque writes depth; blended
    /// draws do not, but for a fading object's (<see cref="ObjectMaterialLook.Faded"/>).</summary>
    private Shader ShaderFor(KmfBlend blend, bool fullLight, bool writesDepth, bool model, bool environment)
    {
        if (_shaders.TryGetValue((blend, fullLight, writesDepth, model, environment), out Shader? shader)) return shader;
        string mode = blend switch
        {
            KmfBlend.Opaque => "blend_mix, depth_draw_opaque",
            KmfBlend.AlphaBlend when writesDepth => "blend_mix, depth_draw_always",
            KmfBlend.AlphaBlend => "blend_mix, depth_draw_never",
            // ONE/ONE: blend_add weights the source by its alpha, held at 1.
            _ => "blend_add, depth_draw_never",
        };
        string alpha = blend switch { KmfBlend.Opaque => "", KmfBlend.AlphaBlend => "ALPHA = c.a;\n", _ => "ALPHA = 1.0;\n" };
        shader = new Shader
        {
            Code = "shader_type spatial;\nrender_mode unshaded, cull_back, skip_vertex_transform, " + mode + ";\n"
                + "uniform sampler2D albedo_texture : filter_linear_mipmap, repeat_disable;\n"
                // A Color set on a plain vec4 is turned linear (material_storage.cpp, _fill_std140_variant_ubo_value).
                + "uniform vec4 base_colour : source_color, color_conversion_disabled = vec4(1.0);\n"
                + "uniform vec3 added_light = vec3(0.0);\n"
                + (fullLight ? PlacedVertex
                    : TorchShadowsActive ? ShadowLightFunctions + PerPixelVaryings + ShadowCasterDeclarations(model)
                        + PerPixelVertex(model, environment)
                    : PerPixelLight ? LightFunctions + PerPixelVaryings + PerPixelVertex(model, environment)
                    : LightFunctions + LitVertex(model, environment))
                + "void fragment() {\n" + (TorchShadowsActive ? ShadowFragment(blend == KmfBlend.Opaque && !fullLight, model) : "")
                + "vec4 c = texture(albedo_texture, UV) * base_colour;\n"
                + (fullLight ? "" : PerPixelLight ? LitFragment + "c.rgb *= dk2_diffuse_px;\n" : "c.rgb *= dk2_diffuse;\n")
                + "ALBEDO = c.rgb;\n" + alpha + (TorchShadowsActive ? "}\n" : "") + "}\n",
        };
        _shaders.Add((blend, fullLight, writesDepth, model, environment), shader);
        return shader;
    }

    /// <summary>How many entries <see cref="GammaOutput"/>'s table has: the half-texel clamp at either end costs at most
    /// 2.4 x 0.5 / 4096 of the range, under a tenth of a byte. Half floats (linear filtering of 32-bit float textures is
    /// optional in Vulkan) round by at most 1/4096 near 1, under a tenth of a byte.</summary>
    private const int GammaTableWidth = 4096;

    /// <summary>The camera's output: the colour buffer holds gamma-space values (the stored bytes over 255), which Godot's
    /// tonemap still encodes as if linear (<c>linear_to_srgb</c> in tonemap.glsl at 4.8 beta 3, c51ff06e8). A 1D colour
    /// correction table of <c>srgb_to_linear</c>, applied after that encoding, undoes it, so the window shows the buffer's
    /// values and every blend happened in gamma space, as in Unity's gamma colour space. Linear tonemap, exposure 1.</summary>
    private static Godot.Environment GammaOutput() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.ClearColor,
        TonemapMode = Godot.Environment.ToneMapper.Linear,
        TonemapExposure = 1f,
        TonemapWhite = 1f,
        AdjustmentEnabled = true,
        AdjustmentBrightness = 1f,
        AdjustmentContrast = 1f,
        AdjustmentSaturation = 1f,
        AdjustmentColorCorrection = GammaTable(1f),
    };

    /// <summary><see cref="GammaOutput"/>'s table, each display value then raised to <paramref name="exponent"/> (the
    /// brightness curve, <see cref="SetBrightness"/>; 1 leaves it).</summary>
    private static ImageTexture GammaTable(float exponent)
    {
        using Image table = Image.CreateEmpty(GammaTableWidth, 1, false, Image.Format.Rgbah);
        for (int i = 0; i < GammaTableWidth; i++)
        {
            // A linear-filtered texel i is sampled whole at (i + 0.5) / width.
            double encoded = (i + 0.5) / GammaTableWidth;
            double stored = encoded < 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            float value = (float)(exponent == 1f ? stored : Math.Pow(stored, exponent));
            table.SetPixel(i, 0, new Color(value, value, value, 1f));
        }
        return ImageTexture.CreateFromImage(table);
    }

    /// <summary>The placed camera's frustum (<see cref="ViewFrustum.FromPose"/>), built again only after the camera moved
    /// (<see cref="PlaceCamera"/>); read only once <see cref="View"/> is set.</summary>
    private ViewFrustum PlacedFrustum
    {
        get
        {
            if (_frustumStale)
            {
                _frustum = ViewFrustum.FromPose(_placed, _placedViewport.Width / (float)_placedViewport.Height, NearPlane);
                _frustumStale = false;
            }
            return _frustum;
        }
    }

    /// <summary>Re-sorts the blended stream for the placed camera when it or the stream changed, and uploads the runs
    /// (<see cref="RunSlot"/>); times the frames that re-sort.</summary>
    private void DrawBlended()
    {
        if (_blended == null || _camera == null || View == null) return;
        ViewFrustum frustum = PlacedFrustum;
        NVector3 forward = NVector3.Normalize(_placed.Forward);
        if (!_blended.NeedsRefresh(forward, NMatrix4x4.Identity, frustum.ViewProjection)) return;
        long start = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        _blended.Refresh(forward, NMatrix4x4.Identity, frustum);
        IReadOnlyList<TriangleDepthOrderBuffer.Run> runs = _blended.Runs;
        int triangles = 0;
        for (int i = 0; i < runs.Count; i++)
        {
            if (i == _runSlots.Count) _runSlots.Add(new RunSlot(GetWorld3D().Scenario, _worldBox));
            _runSlots[i].Show(_blended.Material(runs[i].MaterialIndex), _blended.SortingOrder(i), runs[i].Triangles,
                _blended.Vertices, _blended.GeometryRevision, _runScratch);
            triangles += runs[i].Triangles.Count;
        }
        for (int i = runs.Count; i < _runSlots.Count; i++) _runSlots[i].Hide();
        long ticks = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        _blendedMaxRuns = Math.Max(_blendedMaxRuns, runs.Count);
        _blendedMaxTriangles = Math.Max(_blendedMaxTriangles, triangles);
        if (_blendedFirstTicks < 0) _blendedFirstTicks = ticks;
        else
        {
            _blendedTicks += ticks;
            _blendedMaxTicks = Math.Max(_blendedMaxTicks, ticks);
        }
        if (_blendedRefreshes++ < BlendedWarmUp) _blendedWarmUpBytes += bytes;
        else _blendedLateBytes += bytes;
    }

    private Texture2D TextureFor(string name)
    {
        if (name.Length == 0) return _white!;
        if (_textures.TryGetValue(name, out Texture2D? texture)) return texture;
        try
        {
            RgbaImage source = _art!.LoadTexture(name);
            // Rows top first, as Godot's V counts down from the top, which is the KMF's V (RenderMesh).
            using Image image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, source.Pixels);
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
        }
        catch (FileNotFoundException)
        {
            GD.PushWarning("DK2.Godot: engine texture '" + name + "' not found; drawing it untextured.");
            texture = _white!;
        }
        _textures.Add(name, texture);
        return texture;
    }

    private static Vector3 ToGodot(NVector3 v) => new(v.X, v.Y, v.Z);

    private static string Milliseconds(long ticks) =>
        (Math.Max(0, ticks) * 1000.0 / Stopwatch.Frequency).ToString("0.000", CultureInfo.InvariantCulture);

    public override void _ExitTree()
    {
        if (_camera != null) GetViewport().SizeChanged -= PlaceCamera;
        _terrain?.JoinBuild();
        if (_presenter != null)
            GD.Print("DK2.Godot world: ", _steadyFrames, " frames without a terrain change allocated ", _steadyBytes,
                " B in Present, ", _changeFrames, " with one ", _changeBytes, " B; collections when the level opened ",
                _collectionsAtOpen[0], "/", _collectionsAtOpen[1], "/", _collectionsAtOpen[2], " (gen0/1/2), since then: ",
                GC.CollectionCount(0) - _collectionsAtOpen[0], " gen0, ", GC.CollectionCount(1) - _collectionsAtOpen[1], " gen1, ",
                GC.CollectionCount(2) - _collectionsAtOpen[2], " gen2; water ", _liquids!.Water.TileCount, " tiles, lava ",
                _liquids.Lava.TileCount, "; blended stream: ", _blendedRefreshes, " re-sorts, the first ", Milliseconds(_blendedFirstTicks),
                " ms, the rest ", Milliseconds(_blendedTicks / Math.Max(1, _blendedRefreshes - 1)), " ms mean and ",
                Milliseconds(_blendedMaxTicks), " ms max; allocated ", _blendedWarmUpBytes, " B in the first ", BlendedWarmUp,
                " and ", _blendedLateBytes, " B after; up to ", _blendedMaxRuns, " runs and ", _blendedMaxTriangles, " triangles");
        FreeObjects();
        foreach (RunSlot slot in _runSlots) slot.Free();
        _runSlots.Clear();
        _cage?.Free();
        _cage = null;
        _art?.Dispose();
        _art = null;
    }

    /// <summary>One liquid's surface: a mesh built when its tiles change, whose positions and normals (the wave's, as Unity's
    /// <c>LiquidSurfaces</c> uploads them) are rewritten in place every frame from reused buffers: Godot's vertex stream
    /// holds the positions as one block and the octahedral normals after it (<see cref="RenderingServer.MeshSurfaceGetFormatOffset"/>,
    /// rendering_server.cpp at c51ff06e8). Each vertex carries its tile's table anchor.</summary>
    private sealed class LiquidLayer(LiquidSurfaceWave wave)
    {
        private int _version = -1, _normalOffset;
        private MeshInstance3D? _instance;
        private ArrayMesh? _mesh;
        private byte[] _stream = [], _normalStream = [];
        private bool _checked;

        public void Upload(WorldScene scene)
        {
            if (_version != wave.Version) Rebuild(scene);
            if (_mesh == null || wave.VertexCount == 0) return;
            int count = wave.VertexCount;
            Span<float> positions = MemoryMarshal.Cast<byte, float>(_stream.AsSpan(0, count * 12));
            for (int i = 0; i < count; i++)
            {
                NVector3 p = RightHandedFrame.Point(wave.Positions[i]);
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
            }
            _mesh.SurfaceUpdateVertexRegion(0, 0, _stream.AsSpan(0, count * 12));
            EncodeNormals(count);
            _mesh.SurfaceUpdateVertexRegion(0, _normalOffset, _normalStream.AsSpan(0, count * 4));
        }

        /// <summary>The wave's normals as Godot stores them: <see cref="Vector3.OctahedronEncode"/>, each half as an unsigned
        /// 16-bit fraction, truncated (<c>_surface_set_data</c>).</summary>
        private void EncodeNormals(int count)
        {
            Span<ushort> normals = MemoryMarshal.Cast<byte, ushort>(_normalStream.AsSpan(0, count * 4));
            for (int i = 0; i < count; i++)
            {
                Vector2 o = ToGodot(RightHandedFrame.Normal(wave.Normals[i])).OctahedronEncode();
                normals[i * 2] = (ushort)Math.Clamp(o.X * 65535f, 0f, 65535f);
                normals[(i * 2) + 1] = (ushort)Math.Clamp(o.Y * 65535f, 0f, 65535f);
            }
        }

        private void Rebuild(WorldScene scene)
        {
            _version = wave.Version;
            _mesh?.ClearSurfaces();
            int count = wave.VertexCount;
            if (count == 0) return;
            var positions = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            var anchors = new float[count * 2];
            for (int i = 0; i < count; i++)
            {
                positions[i] = ToGodot(RightHandedFrame.Point(wave.Positions[i]));
                normals[i] = ToGodot(RightHandedFrame.Normal(wave.Normals[i]));
                uvs[i] = new Vector2(wave.Uvs[i].X, wave.Uvs[i].Y);
                anchors[i * 2] = wave.LightAnchors[i].X;
                anchors[(i * 2) + 1] = wave.LightAnchors[i].Y;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Custom0] = anchors;
            arrays[(int)Mesh.ArrayType.Index] = wave.Triangles.AsSpan(0, wave.IndexCount).ToArray();
            _mesh ??= new ArrayMesh();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
                Mesh.ArrayFormat.FlagUseDynamicUpdate | TerrainCustomFormat);
            _mesh.SurfaceSetMaterial(0, scene.MaterialFor(
                new TerrainMaterialKey(new ClassicMaterialRecipe(wave.Texture, KmfBlend.Opaque, 0f), TerrainMaterialVariant.Plain)));
            var format = (RenderingServer.ArrayFormat)_mesh.SurfaceGetFormat(0);
            _normalOffset = (int)RenderingServer.MeshSurfaceGetFormatOffset(format, count, (int)Mesh.ArrayType.Normal);
            if (RenderingServer.MeshSurfaceGetFormatVertexStride(format, count) != 12 || _normalOffset != count * 12)
                GD.PushError("DK2.Godot: a liquid's vertex stream is not positions then normals; it draws wrong.");
            if (_stream.Length < count * 12) _stream = new byte[count * 12];
            if (_normalStream.Length < count * 4) _normalStream = new byte[count * 4];
            if (!_checked) CheckNormals(count);
            if (_instance == null)
            {
                _instance = new MeshInstance3D { Name = wave.Kind + "Surface", Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                scene.AddChild(_instance);
            }
            // The wave moves each vertex within twice its amplitude of the rest height; the box covers every frame.
            Aabb box = _mesh.GetAabb();
            float reach = 2f * Math.Max(LiquidSurfaceWave.WaterAmplitude, LiquidSurfaceWave.LavaAmplitude);
            _instance.CustomAabb = new Aabb(box.Position - new Vector3(0, reach, 0), box.Size + new Vector3(0, 2 * reach, 0));
        }

        /// <summary>Once: the normals encoded here against the bytes Godot built from the same normals.</summary>
        private void CheckNormals(int count)
        {
            _checked = true;
            byte[] built = RenderingServer.MeshGetSurface(_mesh!.GetRid(), 0)["vertex_data"].AsByteArray();
            EncodeNormals(count);
            bool same = built.Length >= _normalOffset + (count * 4)
                && built.AsSpan(_normalOffset, count * 4).SequenceEqual(_normalStream.AsSpan(0, count * 4));
            GD.Print("DK2.Godot: ", wave.Kind, "'s ", count, " normals encoded ", same ? "as Godot encodes them" : "UNLIKE Godot; the wave draws wrong");
        }
    }

    /// <summary>The cage as Unity's <c>ClassicTileHoverOutline</c> draws it (plan step 3e): <see cref="TileHoverRibbon"/>'s
    /// triangles unindexed in one dynamic surface, zero-area triangles past the ribbon, additive, no depth write, both faces.
    /// The fragment is Unity's <c>ClassicHoverOutline.shader</c>: the colour times pow(centre, 2.15) x 1.6 across the band,
    /// owner-matched (2026-10-19). Its key (<see cref="Sort"/>) puts it where Unity's sorting order 0 did: after the tag
    /// overlay runs (negative orders), under the other blended runs (<see cref="BlendedTerrainStream{TMaterial}.SortingOrder"/>),
    /// and among the blended objects by the distance to its centre.</summary>
    private sealed class CageRibbon
    {
        private const RenderingServer.ArrayFormat Format = RenderingServer.ArrayFormat.FormatVertex
            | RenderingServer.ArrayFormat.FormatTexUV | RenderingServer.ArrayFormat.FlagUseDynamicUpdate;
        private const int PositionStride = 12, UvStride = 8;
        private static readonly StringName ColourParameter = "colour";

        private readonly Rid _instance, _mesh;
        private readonly ShaderMaterial _material;
        private readonly TileHoverRibbon _ribbon = new();
        private byte[] _positions = [], _uvs = [];
        private int _capacity, _written;
        private bool _visible = true;
        private NVector3 _colour = new(-1, -1, -1);
        private Vector3 _centre;
        private int _sortStamp = -1;

        public CageRibbon(Rid scenario, Aabb box)
        {
            _material = new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = "shader_type spatial;\nrender_mode unshaded, blend_add, depth_draw_never, cull_disabled;\n"
                        + "uniform vec4 colour : source_color, color_conversion_disabled = vec4(0.0, 0.0, 0.0, 1.0);\n"
                        + "void fragment() {\n"
                        // Kept non-negative, as Unity's saturate, where MSAA extrapolates the UV past the ribbon.
                        + "float centre = clamp(1.0 - abs(UV.y * 2.0 - 1.0), 0.0, 1.0);\n"
                        + "ALBEDO = colour.rgb * pow(centre, 2.15) * 1.6;\nALPHA = 1.0;\n}\n",
                },
            };
            _mesh = RenderingServer.MeshCreate();
            _instance = RenderingServer.InstanceCreate2(_mesh, scenario);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(_instance, RenderingServer.ShadowCastingSetting.Off);
            RenderingServer.InstanceSetCustomAabb(_instance, box);
            RenderingServer.InstanceGeometrySetMaterialOverride(_instance, _material.GetRid());
            SortFromOrigin(_instance, 0);
            Hide();
        }

        public void Show(TileRectangle area, bool blue, NVector3 eye, TerrainLattice? lattice, long milliseconds)
        {
            TileHoverOutline.Build(_ribbon, area, blue, eye, lattice, milliseconds);
            int count = _ribbon.IndexCount;
            if (count > _capacity) Grow(count);
            if (count == 0) { Hide(); return; }
            int vertexCount = Math.Max(count, _written);
            Span<float> positions = MemoryMarshal.Cast<byte, float>(_positions.AsSpan(0, vertexCount * PositionStride));
            Span<float> uvs = MemoryMarshal.Cast<byte, float>(_uvs.AsSpan(0, vertexCount * UvStride));
            ReadOnlySpan<int> indices = _ribbon.Indices;
            ReadOnlySpan<NVector3> from = _ribbon.Positions;
            ReadOnlySpan<NVector2> along = _ribbon.Uvs;
            NVector3 low = new(float.MaxValue), high = new(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                NVector3 at = RightHandedFrame.Point(from[indices[i]]);
                low = NVector3.Min(low, at);
                high = NVector3.Max(high, at);
                positions[i * 3] = at.X; positions[(i * 3) + 1] = at.Y; positions[(i * 3) + 2] = at.Z;
                uvs[i * 2] = along[indices[i]].X; uvs[(i * 2) + 1] = along[indices[i]].Y;
            }
            Vector3 centre = ToGodot((low + high) * 0.5f);
            if (centre != _centre) { _centre = centre; _sortStamp = -1; }
            positions[(count * 3)..].Clear();
            uvs[(count * 2)..].Clear();
            RenderingServer.MeshSurfaceUpdateVertexRegion(_mesh, 0, 0, _positions.AsSpan(0, vertexCount * PositionStride));
            RenderingServer.MeshSurfaceUpdateAttributeRegion(_mesh, 0, 0, _uvs.AsSpan(0, vertexCount * UvStride));
            _written = count;
            if (_ribbon.Colour != _colour)
            {
                _colour = _ribbon.Colour;
                _material.SetShaderParameter(ColourParameter, new Color(_colour.X, _colour.Y, _colour.Z, 1f));
            }
            if (!_visible) { RenderingServer.InstanceSetVisible(_instance, true); _visible = true; }
        }

        public void Hide()
        {
            if (!_visible) return;
            RenderingServer.InstanceSetVisible(_instance, false);
            _visible = false;
        }

        /// <summary>The cage's order-0 key (<see cref="ZeroOrderOffset"/>) from its bounds centre, as Unity sorts its sorting
        /// order 0 among the blended objects by distance; again when it or the camera moved. Called after each
        /// <see cref="Show"/>, which comes after the frame's <see cref="FinishFrame"/>.</summary>
        public void Sort(WorldScene scene)
        {
            if (!_visible || _sortStamp == scene._sortStamp) return;
            _sortStamp = scene._sortStamp;
            RenderingServer.InstanceSetPivotData(_instance, scene.ZeroOrderOffset(Vector3.Zero, _centre), false);
        }

        public void Free()
        {
            RenderingServer.FreeRid(_instance);
            RenderingServer.FreeRid(_mesh);
            _material.Dispose();
        }

        /// <summary>A new zeroed surface of a power-of-two vertex capacity, as the ribbon outgrows it.</summary>
        private void Grow(int count)
        {
            int capacity = 384;
            while (capacity < count) capacity <<= 1;
            _capacity = capacity;
            if (RenderingServer.MeshSurfaceGetFormatVertexStride(Format, capacity) != PositionStride
                || RenderingServer.MeshSurfaceGetFormatAttributeStride(Format, capacity) != UvStride)
                GD.PushError("DK2.Godot: the cage's vertex layout is not positions then UVs; it draws wrong.");
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[capacity];
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[capacity];
            RenderingServer.MeshClear(_mesh);
            RenderingServer.MeshAddSurfaceFromArrays(_mesh, RenderingServer.PrimitiveType.Triangles, arrays, null, null,
                RenderingServer.ArrayFormat.FlagUseDynamicUpdate);
            _positions = new byte[capacity * PositionStride];
            _uvs = new byte[capacity * UvStride];
            _written = 0;
        }
    }

    /// <summary>Every instance of the blended pass sorts by its origin's distance minus <paramref name="order"/>: a new
    /// instance's pivot is its AABB centre (<c>use_aabb_center</c> true, renderer_scene_cull.h at c51ff06e8), which would
    /// sort it by real distance among the runs. The one place the runs set their sort key; the cage takes an order-0 key.</summary>
    private static void SortFromOrigin(Rid instance, float order) => RenderingServer.InstanceSetPivotData(instance, order, false);

    /// <summary>One run of the blended stream: an instance of its own one-surface mesh (positions, normals, UVs and table
    /// anchors, as a terrain chunk's), a triangle list in the run's order
    /// (a surface draws its primitives in order), with zero-area triangles past the run. Godot 4.8 sorts the transparent
    /// pass by material priority, then by camera distance minus the instance's sorting offset, with an unstable sort
    /// (<c>SortByReverseDepthAndPriority</c>, <c>SortArray</c> in render_forward_clustered.h at c51ff06e8): every run
    /// instance sits at the origin, sorted by its origin, with the stream's sorting order as its offset, so runs draw in
    /// the stream's order and the triangles within one in theirs.</summary>
    private sealed class RunSlot
    {
        /// <summary>The upload buffers every slot shares: positions and normals (the vertex stream's two blocks) and each
        /// vertex's UV and table anchor (the attribute stream).</summary>
        public sealed class Scratch
        {
            public byte[] Positions = [], Normals = [], Uvs = [];
        }

        private const RenderingServer.ArrayFormat Format = RenderingServer.ArrayFormat.FormatVertex
            | RenderingServer.ArrayFormat.FormatNormal | RenderingServer.ArrayFormat.FormatTexUV
            | RenderingServer.ArrayFormat.FormatCustom0 | RenderingServer.ArrayFormat.FlagUseDynamicUpdate
            | (RenderingServer.ArrayFormat)((long)RenderingServer.ArrayCustomFormat.RgFloat << (int)RenderingServer.ArrayFormat.FormatCustom0Shift);
        private const int PositionStride = 12, NormalStride = 4, UvStride = 16;

        private readonly Rid _instance, _mesh;
        private readonly List<RenderTriangle> _triangles = new();
        private ShaderMaterial? _material;
        private int _capacity, _written, _order = int.MinValue;
        private long _revision = -1;
        private bool _visible = true;

        public RunSlot(Rid scenario, Aabb box)
        {
            _mesh = RenderingServer.MeshCreate();
            _instance = RenderingServer.InstanceCreate2(_mesh, scenario);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(_instance, RenderingServer.ShadowCastingSetting.Off);
            RenderingServer.InstanceSetCustomAabb(_instance, box);
        }

        public void Show(ShaderMaterial material, int order, IReadOnlyList<RenderTriangle> triangles,
            IReadOnlyList<BlendedVertex> vertices, long revision, Scratch scratch)
        {
            if (!_visible) { RenderingServer.InstanceSetVisible(_instance, true); _visible = true; }
            if (!ReferenceEquals(material, _material))
            {
                RenderingServer.InstanceGeometrySetMaterialOverride(_instance, material.GetRid());
                _material = material;
            }
            if (order != _order)
            {
                SortFromOrigin(_instance, order);
                _order = order;
            }
            if (revision == _revision && Same(triangles)) return;
            _revision = revision;
            _triangles.Clear();
            for (int i = 0; i < triangles.Count; i++) _triangles.Add(triangles[i]);
            int count = triangles.Count;
            if (count > _capacity) Grow(count);
            int vertexCount = Math.Max(count, _written) * 3;
            if (scratch.Positions.Length < vertexCount * PositionStride)
            {
                scratch.Positions = new byte[_capacity * 3 * PositionStride];
                scratch.Normals = new byte[_capacity * 3 * NormalStride];
                scratch.Uvs = new byte[_capacity * 3 * UvStride];
            }
            Span<float> positions = MemoryMarshal.Cast<byte, float>(scratch.Positions.AsSpan(0, vertexCount * PositionStride));
            Span<ushort> normals = MemoryMarshal.Cast<byte, ushort>(scratch.Normals.AsSpan(0, vertexCount * NormalStride));
            Span<float> uvs = MemoryMarshal.Cast<byte, float>(scratch.Uvs.AsSpan(0, vertexCount * UvStride));
            int p = 0, n = 0, u = 0;
            for (int t = 0; t < count; t++)
            {
                RenderTriangle triangle = triangles[t];
                Corner(vertices[triangle.A], positions, normals, uvs, ref p, ref n, ref u);
                Corner(vertices[triangle.B], positions, normals, uvs, ref p, ref n, ref u);
                Corner(vertices[triangle.C], positions, normals, uvs, ref p, ref n, ref u);
            }
            positions[p..].Clear();
            normals[n..].Clear();
            uvs[u..].Clear();
            RenderingServer.MeshSurfaceUpdateVertexRegion(_mesh, 0, 0, scratch.Positions.AsSpan(0, vertexCount * PositionStride));
            RenderingServer.MeshSurfaceUpdateVertexRegion(_mesh, 0, _capacity * 3 * PositionStride,
                scratch.Normals.AsSpan(0, vertexCount * NormalStride));
            RenderingServer.MeshSurfaceUpdateAttributeRegion(_mesh, 0, 0, scratch.Uvs.AsSpan(0, vertexCount * UvStride));
            _written = count;
        }

        public void Hide()
        {
            if (!_visible) return;
            RenderingServer.InstanceSetVisible(_instance, false);
            _visible = false;
        }

        public void Free()
        {
            RenderingServer.FreeRid(_instance);
            RenderingServer.FreeRid(_mesh);
        }

        private static void Corner(in BlendedVertex vertex, Span<float> positions, Span<ushort> normals, Span<float> uvs,
            ref int p, ref int n, ref int u)
        {
            NVector3 at = RightHandedFrame.Point(vertex.Position);
            positions[p++] = at.X;
            positions[p++] = at.Y;
            positions[p++] = at.Z;
            // Godot's own normal encoding (LiquidLayer.EncodeNormals).
            Vector2 o = ToGodot(RightHandedFrame.Normal(vertex.Normal)).OctahedronEncode();
            normals[n++] = (ushort)Math.Clamp(o.X * 65535f, 0f, 65535f);
            normals[n++] = (ushort)Math.Clamp(o.Y * 65535f, 0f, 65535f);
            uvs[u++] = vertex.Uv.X;
            uvs[u++] = vertex.Uv.Y;
            uvs[u++] = vertex.LightAnchor.X;
            uvs[u++] = vertex.LightAnchor.Y;
        }

        private bool Same(IReadOnlyList<RenderTriangle> triangles)
        {
            if (triangles.Count != _triangles.Count) return false;
            for (int i = 0; i < triangles.Count; i++)
                if (triangles[i] != _triangles[i]) return false;
            return true;
        }

        /// <summary>A new surface of a power-of-two triangle capacity, zeroed; the only allocation, as a run outgrows it.</summary>
        private void Grow(int count)
        {
            int capacity = 64;
            while (capacity < count) capacity <<= 1;
            _capacity = capacity;
            int vertexCount = capacity * 3;
            if (RenderingServer.MeshSurfaceGetFormatVertexStride(Format, vertexCount) != PositionStride
                || RenderingServer.MeshSurfaceGetFormatNormalTangentStride(Format, vertexCount) != NormalStride
                || RenderingServer.MeshSurfaceGetFormatOffset(Format, vertexCount, (int)Mesh.ArrayType.Normal) != vertexCount * PositionStride
                || RenderingServer.MeshSurfaceGetFormatAttributeStride(Format, vertexCount) != UvStride
                || RenderingServer.MeshSurfaceGetFormatOffset(Format, vertexCount, (int)Mesh.ArrayType.Custom0) != 8)
                GD.PushError("DK2.Godot: the blended run's vertex layout is not positions, normals, then UV and anchor; runs draw wrong.");
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[vertexCount];
            var up = new Vector3[vertexCount];
            Array.Fill(up, Vector3.Up);
            arrays[(int)Mesh.ArrayType.Normal] = up;
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[vertexCount];
            arrays[(int)Mesh.ArrayType.Custom0] = new float[vertexCount * 2];
            RenderingServer.MeshClear(_mesh);
            RenderingServer.MeshAddSurfaceFromArrays(_mesh, RenderingServer.PrimitiveType.Triangles, arrays, null, null,
                Format & ~(RenderingServer.ArrayFormat.FormatVertex | RenderingServer.ArrayFormat.FormatNormal
                    | RenderingServer.ArrayFormat.FormatTexUV | RenderingServer.ArrayFormat.FormatCustom0));
            _written = 0;
        }
    }

    /// <summary>One key's surface in the chunk being built: the vertices its triangles use, each copied once, in
    /// Godot's frame, and its triangles renumbered to them.</summary>
    private sealed class SurfaceBuffer
    {
        private NVector3[] _positions = new NVector3[256], _normals = new NVector3[256];
        private NVector2[] _uvs = new NVector2[256], _anchors = new NVector2[256];
        private int[] _indices = new int[512];
        private int _vertexCount;

        public TerrainMaterialKey Key { get; private set; }
        public int IndexCount { get; private set; }

        public void Reset(in TerrainMaterialKey key)
        {
            Key = key;
            _vertexCount = 0;
            IndexCount = 0;
        }

        public void Append(TerrainTileGeometry tile, TerrainIndexList triangles, int[] remap, int[] stamp, int generation)
        {
            int[] items = triangles.Items;
            if (_indices.Length < IndexCount + triangles.Count) Array.Resize(ref _indices, Math.Max(IndexCount + triangles.Count, _indices.Length * 2));
            for (int i = 0; i < triangles.Count; i++)
            {
                int vertex = items[i];
                if (stamp[vertex] != generation)
                {
                    stamp[vertex] = generation;
                    remap[vertex] = _vertexCount;
                    if (_vertexCount == _positions.Length)
                    {
                        Array.Resize(ref _positions, _vertexCount * 2);
                        Array.Resize(ref _normals, _vertexCount * 2);
                        Array.Resize(ref _uvs, _vertexCount * 2);
                        Array.Resize(ref _anchors, _vertexCount * 2);
                    }
                    _positions[_vertexCount] = RightHandedFrame.Point(tile.Positions[vertex]);
                    _normals[_vertexCount] = RightHandedFrame.Normal(tile.Normals[vertex]);
                    _uvs[_vertexCount] = tile.Uvs[vertex];
                    _anchors[_vertexCount] = tile.LightAnchor;
                    _vertexCount++;
                }
                _indices[IndexCount++] = remap[vertex];
            }
        }

        /// <summary>The surface as Godot's exact-length arrays, with each vertex's table anchor (<see cref="TerrainCustomFormat"/>);
        /// the index order is kept (<see cref="RightHandedFrame"/>).</summary>
        public Godot.Collections.Array ToArrays()
        {
            var positions = new Vector3[_vertexCount];
            var normals = new Vector3[_vertexCount];
            var uvs = new Vector2[_vertexCount];
            var anchors = new float[_vertexCount * 2];
            for (int i = 0; i < _vertexCount; i++)
            {
                positions[i] = ToGodot(_positions[i]);
                normals[i] = ToGodot(_normals[i]);
                uvs[i] = new Vector2(_uvs[i].X, _uvs[i].Y);
                anchors[i * 2] = _anchors[i].X;
                anchors[(i * 2) + 1] = _anchors[i].Y;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Custom0] = anchors;
            arrays[(int)Mesh.ArrayType.Index] = _indices.AsSpan(0, IndexCount).ToArray();
            return arrays;
        }
    }
}
