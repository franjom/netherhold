// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The 3D world under <see cref="InGameHud"/> (plan steps 3a to 3c): the terrain as <see cref="TerrainTileBuilder"/>
/// builds it and <see cref="TerrainChunkScheduler"/> schedules it, one mesh instance per 16x16-tile chunk
/// (WorldScene.ChunkUpload.cs) with a surface per <see cref="TerrainMaterialKey"/> drawn as <see cref="TerrainMaterialLook"/> describes it, and the
/// waving water and lava (<see cref="LiquidSurfaceField"/>), lit by DK2's vertex light (WorldScene.Lighting.cs), seen from the classic
/// camera; blended faces go back to front through <see cref="BlendedTerrainStream{TMaterial}"/> (step 3d, <see cref="RunSlot"/>).
/// Everything crosses into Godot's right-handed frame through <see cref="RightHandedFrame"/>. Objects, structures and
/// torches are step 3f's (WorldScene.Objects.cs), creatures step 3g's (WorldScene.Creatures.cs), entity hover step 3h's (WorldScene.Hover.cs), lighting step 3i's (WorldScene.Lighting.cs). The
/// host presents the world once a frame (<see cref="Present"/>), draws it (<see cref="DrawFrame"/>), moves the camera's
/// <see cref="Focus"/> and places it (<see cref="PlaceCamera"/>), then finishes it (<see cref="FinishFrame"/>).</summary>
public partial class WorldScene : Node3D, IRenderTarget, ITerrainChunkSink
{
    /// <summary>A tag overlay's render priority: under every other blended thing's 0, over the occlusion quad's -128.</summary>
    private const int OverlayPriority = -1;

    /// <summary>Unity's camera near plane (Dungeon.unity), which the frustum is sized at.</summary>
    private const float NearPlane = 0.3f;

    private static readonly StringName AlbedoParameter = "albedo_texture", ColourParameter = "base_colour",
        AddedLightParameter = "added_light", FullLightParameter = "full_light";

    private InstallArtProvider? _art;
    private TerrainLattice? _lattice;
    private CageRibbon? _cage;
    private ScenePresenter? _presenter;
    private TerrainChunkScheduler? _terrain;
    private MeshInstance3D?[] _terrainOverlayChunks = [];
    private readonly SurfaceBuffer _terrainOverlaySurface = new SurfaceBuffer();
    private LiquidSurfaceField? _liquids;
    private LiquidLayer? _water, _lava;
    private float _seconds;

    private int[] _remap = new int[256], _stamp = new int[256];
    private int _generation;

    // The blended stream: a chunk's blended faces over its tiles' vertices, per material in first-use order across
    // rebuilds, and the run slots that draw the stream.
    private BlendedTerrainStream<ShaderMaterial>? _blended;
    private readonly List<BlendedVertex> _blendedVertices = new List<BlendedVertex>();
    private readonly Dictionary<ShaderMaterial, List<int>> _blendedByMaterial = new Dictionary<ShaderMaterial, List<int>>();
    private readonly HashSet<ShaderMaterial> _blendedMaterials = new HashSet<ShaderMaterial>(), _overlayMaterials = new HashSet<ShaderMaterial>();
    private readonly BlendedRunSlots<ShaderMaterial> _runKeys = new BlendedRunSlots<ShaderMaterial>();
    private readonly List<RunSlot> _runSlots = new List<RunSlot>();
    private readonly RunSlot.Scratch _runScratch = new RunSlot.Scratch();
    private Aabb _worldBox;
    // The re-sorting frames, for the exit report: time after the first (which pays the JIT), the most, what the frames
    // after the warm-up allocated, and the bytes uploaded for the triangles drawn.
    private const int BlendedWarmUp = 60;
    private ViewFrustum _frustum;
    private bool _frustumStale = true;
    private long _blendedTicks, _blendedMaxTicks, _blendedFirstTicks = -1, _blendedWarmUpBytes, _blendedLateBytes;
    private long _blendedUploadBytes, _blendedUploadTriangles;
    private int _blendedRefreshes, _blendedMaxRuns, _blendedMaxTriangles;

    private readonly Dictionary<TerrainMaterialKey, ShaderMaterial> _keyMaterials = new Dictionary<TerrainMaterialKey, ShaderMaterial>();
    private readonly Dictionary<(TerrainMaterialLook Look, bool Fog), ShaderMaterial> _lookMaterials = new Dictionary<(TerrainMaterialLook Look, bool Fog), ShaderMaterial>();
    // The world shader per light mode (per vertex, per pixel, with shadows) and render state, made on first use and kept
    // for the run, so a level reload compiles none again.
    private static readonly Shader?[,] WorldShaders = new Shader?[3, 5];
    private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
    private ImageTexture? _white;
    // The world ShaderMaterials made so far, for the performance report.
    private int _worldMaterialsMade;

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
    public ClassicCameraControls? Controls
    {
        get
        {
            return _controls;
        }
    }

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
        OpenWorld(install, level, world, view, viewer, StatusFlowerArt.Entries);
        CameraThing? profile = ClassicCameraSetup.Preset(
            GameDatabaseLoader.LoadCameraPresets(install, level.Name), ClassicCameraMode.Dungeon);
        if (profile == null)
        {
            GD.PushError("DK2.Godot: the level has no dungeon camera preset; no 3D camera.");
            return;
        }
        var setup = ClassicCameraSetup.Create(profile, ClassicCameraSetup.DefaultZoomOutLimitScale,
            GameSessionStarter.CameraTicksPerSecond(level), scrollSpeed, level.Players, viewer, world.Width, world.Height);
        _controls = setup.Controls;
        ClassicCamera = setup.Camera;
        Focus = focus;
        OpenCamera();
    }

    /// <summary>The world without its camera: terrain, liquids, objects, lights, shadows and the blended stream, with the
    /// install's art and <paramref name="additionalArt"/> (the front end's lands and markers).</summary>
    private void OpenWorld(string install, CampaignLevelData level, WorldState world, WorldView view, PlayerId viewer,
        IEnumerable<ArtEntry>? additionalArt)
    {
        _art = InstallArtProvider.Open(install, level.Database, additionalArt);
        // Bend the terrain with the lattice and draw DK2's high walls by default.
        var lattice = new TerrainLattice(level.Database, world, viewer) { HighWalls = true };
        _lattice = lattice;
        _terrain = new TerrainChunkScheduler(new InstallTerrainPieces(_art), world.Width, world.Height, lattice, stateTexture: true);
        OpenChunkUpload(_terrain);
        _terrainOverlayChunks = new MeshInstance3D?[_terrain.ChunkCount];
        _liquids = new LiquidSurfaceField(world.Width, world.Height);
        _water = new LiquidLayer(_liquids.Water);
        _lava = new LiquidLayer(_liquids.Lava);
        _blended = new BlendedTerrainStream<ShaderMaterial>(_overlayMaterials.Contains);
        // Every run instance sorts from the origin; the box, in Godot's frame, only keeps Godot from culling it (the
        // stream culls): one in the renderers' frame lies off the map, and a short far plane culls every run by it.
        _worldBox = WorldBox(world.Width, world.Height);
        OpenPossessionRendering();
        OpenPossessionVisibility(level.Database, world.Width, world.Height);
        _cage = new CageRibbon(GetWorld3D().Scenario, _worldBox);
        OpenItems();
        // Before any material: the lit shaders read the lighting's globals.
        OpenLighting(world, view, world.Width, world.Height);
        OpenShadows();
        OpenTerrainState(world.Width, world.Height);
        using (var white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
        {
            white.Fill(Colors.White);
            _white = ImageTexture.CreateFromImage(white);
        }

        // Look up the install art, then the placeholder. The first present builds
        // every chunk inline, so the level appears whole.
        OpenObjects(_art, level.Database, world.Width, world.Height);
        _presenter = new ScenePresenter(view, AssetProviderChain.Standard(install: _art), this, new TorchLayout(level.Database));
        _presenter.Present(world);
        for (int generation = 0; generation < _collectionsAtOpen.Length; generation++)
        {
            _collectionsAtOpen[generation] = GC.CollectionCount(generation);
        }
    }

    /// <summary>Place the scene's camera with the default near plane, gamma output and post effects.</summary>
    private void OpenCamera()
    {
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
        OpenPostEffects();
        GetViewport().SizeChanged += PlaceCamera;
        PlaceCamera();
    }

    /// <summary>The bottom panel's collapse moves the view axis (<see cref="ClassicCamera.ProjectionCentreFromTop"/>).
    /// Drawn at the next <see cref="PlaceCamera"/>.</summary>
    public void SetPanelCollapsed(bool collapsed)
    {
        if (ClassicCamera == null)
        {
            return;
        }

        ClassicCamera.ProjectionCentre = ClassicCamera.ProjectionCentreFromTop(
            collapsed ? ClassicCamera.CollapsedPanelTop : ClassicCamera.ExpandedPanelTop);
    }

    /// <summary>The classic camera's pose at <see cref="Focus"/> for the current viewport, through the frame change, with
    /// the off-centre projection as a frustum offset. Write the camera only
    /// when the pose changed. Allocation-free.</summary>
    public ScriptedViewDirector? ScriptedView { get; set; }
    public float LetterboxBar
    {
        get
        {
            return ScriptedView?.LetterboxBar ?? 0;
        }
    }

    public ClassicCameraPose? PreviewPose { get; set; }
    public Godot.Environment? CameraEnvironment
    {
        get
        {
            return _camera?.Environment;
        }
    }

    public bool MeasurePreviewRenderTime { get; set; }
    public CameraViewport PreviewViewport { get; set; }
    private const uint MainBlendedLayer = 1u << 18, PreviewBlendedLayer = 1u << 19;
    private readonly BlendedRunSlots<ShaderMaterial> _previewRunKeys = new BlendedRunSlots<ShaderMaterial>();
    private readonly List<RunSlot> _previewRunSlots = new List<RunSlot>();

    private void DrawPreviewTerrain()
    {
        if (_blended == null || PreviewPose is not ClassicCameraPose pose)
        {
            foreach (RunSlot slot in _previewRunSlots)
            {
                slot.Hide();
            }

            return;
        }
        var frustum = ViewFrustum.FromPose(pose, PreviewViewport.Width / (float)PreviewViewport.Height, .05f);
        _blended.Refresh(NVector3.Normalize(pose.Forward), NMatrix4x4.Identity, frustum);
        _previewRunKeys.Update(_blended, pose.Position);
        _previewItems.Order.ClearRuns();
        foreach (BlendedRunSlot<ShaderMaterial> run in _previewRunKeys.Drawn)
        {
            if (!run.TagOverlay)
            {
                _previewItems.Order.AddRun(run.Distance);
            }

            while (_previewRunSlots.Count <= run.Id)
            {
                _previewRunSlots.Add(new RunSlot(GetWorld3D().Scenario, _worldBox, PreviewBlendedLayer));
            }

            _previewRunSlots[run.Id].Show(run, _previewRunKeys.Vertices, pose.Position.Length(), _runScratch);
        }
        for (int i = 0; i < _previewRunSlots.Count; i++)
        {
            if (!_previewRunKeys.Slots[i].Drawn)
            {
                _previewRunSlots[i].Hide();
            }
        }
    }

    public void PlaceCamera()
    {
        if (_camera == null || (ClassicCamera == null && FrontEndPose == null))
        {
            return;
        }

        _camera.CullMask = uint.MaxValue & ~PreviewBlendedLayer;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var viewport = new CameraViewport(width, width, height);
        if (ClassicCamera == null)
        {
            PlaceFrontEndCamera(viewport);
            return;
        }
        CameraViewport drawnViewport = LetterboxBar > 0
            ? new CameraViewport(width, width, Math.Max(1, (int)(height * (1 - 2 * LetterboxBar)))) : viewport;
        ClassicCameraPose? held = PossessionPose(drawnViewport);
        ClassicCameraPose pose = held ?? ClassicCamera.Pose(Focus, drawnViewport);
        if (ScriptedView?.Pose is CameraPathPose path)
        {
            pose = new ClassicCameraPose(path.Position, path.Forward, path.Up,
                path.VerticalFieldOfView(width, Math.Max(1, (int)(height * (1 - 2 * LetterboxBar)))),
                path.ViewDistance, 0, 0);
        }

        if (ScriptedView != null)
        {
            pose = pose with { Position = pose.Position + ScriptedView.RenderCameraJitter };
        }

        if (LetterboxBar > 0)
        {
            float scale = 1 - 2 * LetterboxBar;
            pose = pose with
            {
                VerticalFieldOfView = 2 * MathF.Atan(MathF.Tan(pose.VerticalFieldOfView * MathF.PI / 360) / scale) * 180 / MathF.PI,
                ProjectionCentreShift = pose.ProjectionCentreShift * scale
            };
        }
        PlacePose(pose, viewport, held == null ? NearPlane : PossessionCamera.NearClipDistance);
    }

    /// <summary>Writes <paramref name="pose"/> to the camera when it or the viewport changed: the frustum with the pose's
    /// centre shift as its offset, the far plane, and the view the picks and the minimap read.</summary>
    private void PlacePose(in ClassicCameraPose pose, CameraViewport viewport, float near)
    {
        if (_camera == null || (View != null && pose == _placed && viewport == _placedViewport))
        {
            return;
        }

        _near = near;
        _camera.Near = _near;
        _placed = pose; _placedViewport = viewport;
        _frustumStale = true;
        View = MinimapCameraView.Of(pose, viewport);
        (float frustumHeight, float offset) = RightHandedFrame.Frustum(pose.VerticalFieldOfView, _near, pose.ProjectionCentreShift);
        _camera.Size = frustumHeight;
        _camera.FrustumOffset = new Vector2(0, offset);
        _camera.Far = Math.Max(_near + 0.01f, pose.FarClipDistance);
        RightHandedCamera basis = RightHandedFrame.Camera(pose);
        _camera.Transform = new Transform3D(new Basis(ToGodot(basis.X), ToGodot(basis.Y), ToGodot(basis.Z)), ToGodot(basis.Origin));
    }

    /// <summary>A box over the whole map in Godot's frame, a tile past each edge and from 4 below the floor to 12 above it:
    /// the renderers' corners (-1, -4, 1) and (width + 1, 12, -height - 1) turned by <see cref="RightHandedFrame.Point"/>.</summary>
    private static Aabb WorldBox(int width, int height)
    {
        Vector3 a = ToGodot(RightHandedFrame.Point(new NVector3(-1, -4, 1)));
        Vector3 b = ToGodot(RightHandedFrame.Point(new NVector3(width + 1, 12, -height - 1)));
        Vector3 low = a.Min(b);
        return new Aabb(low, a.Max(b) - low);
    }

    /// <summary>Cast a ray from a screen pixel with top-left origin through the off-centre frustum
    /// (<see cref="MinimapCameraView.RayDirection"/>), from the camera as last
    /// placed (<see cref="PlaceCamera"/>); false before the first placement. Not <see cref="Camera3D.ProjectRayNormal"/>:
    /// 4.8's assumes a symmetric frustum and drops the frustum offset (<c>Projection::get_viewport_half_extents</c>, c51ff06e8).</summary>
    public bool TryPickRay(Vector2 screen, out NVector3 origin, out NVector3 direction)
    {
        origin = direction = default;
        if (View is not MinimapCameraView view || _placedViewport.Height <= 0)
        {
            return false;
        }

        if (screen.Y < _placedViewport.Height * LetterboxBar || screen.Y >= _placedViewport.Height * (1 - LetterboxBar))
        {
            return false;
        }

        origin = view.Position;
        direction = view.RayDirection(screen.X / _placedViewport.Width, 1f - screen.Y / _placedViewport.Height);
        return true;
    }

    /// <summary>The selection cage over <paramref name="area"/> (<see cref="TileHoverOutline.Build"/>), turned to the placed
    /// camera; null hides it. Allocation-free once its buffers have grown.</summary>
    public void DrawCage(TileRectangle? area, bool blue, long milliseconds)
    {
        if (_cage == null)
        {
            return;
        }

        if (area is not TileRectangle shown || View is not MinimapCameraView view) { _cage.Hide(); return; }
        _cage.Show(shown, blue, view.Position, _lattice, milliseconds);
    }

    public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
    {
        SetTile(tileIndex, tile);
    }

    public void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture)
    {
        SetTile(tileIndex, tile);
    }

    private void SetTile(int tileIndex, in TileView tile)
    {
        SetTileLight(tileIndex, tile);
        bool geometryChanged = SetTerrainState(tileIndex, tile);
        if (geometryChanged)
        {
            _liquids!.Set(tileIndex, tile);
            _latticeDirty?.Touch(tileIndex);
            _roof?.TileChanged(tile.Position.X, tile.Position.Y);
        }
        _terrain!.Set(tileIndex, tile);
    }

    /// <summary>Pushes the settled world's changes (<see cref="ScenePresenter.Present"/>) and waves the liquids at
    /// <paramref name="seconds"/> of engine time; a changed tile's chunks are built on the thread pool and uploaded by a
    /// later present within the flush budget. Counts what it allocates, reported when the node leaves the tree.</summary>
    public void Present(WorldState world, double seconds)
    {
        if (_presenter == null || _terrain == null)
        {
            return;
        }

        _seconds = (float)seconds;
        ChunkFlushMilliseconds = 0;
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

    /// <summary>The chunk upload of this frame's <see cref="Flush"/> in milliseconds (<see cref="FrameRecorder"/>'s
    /// <c>chunk_rebuild_ms</c>); zero when <see cref="Present"/> flushed nothing.</summary>
    public double ChunkFlushMilliseconds { get; private set; }

    /// <summary>The scheduler's flush (the <see cref="ITerrainChunkSink"/> calls below upload), then the liquids' wave; the
    /// blended stream is sorted by <see cref="FinishFrame"/>, once the frame's camera is placed.</summary>
    public void Flush()
    {
        FlushTerrainState();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _terrain!.Flush(this);
        ChunkFlushMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _liquids!.Refresh();
        int frame = LiquidSurfaceWave.Frame(_seconds);
        _water!.Show(this, frame);
        _lava!.Show(this, frame);
    }

    void ITerrainChunkSink.BeginChunk(int chunk)
    {
        _changed = true;
        _terrainOverlaySurface.Reset(default);
        _blendedVertices.Clear();
        foreach (List<int> list in _blendedByMaterial.Values)
        {
            list.Clear();
        }
    }

    void ITerrainChunkSink.AddTile(int tileIndex, TerrainTileGeometry geometry)
    {
        GatherTile(geometry);
    }

    /// <summary>The opaque keys' surfaces the build task packed as the chunk's mesh (WorldScene.ChunkUpload.cs); the
    /// blended ones join the stream.</summary>
    void ITerrainChunkSink.EndChunk(int chunk)
    {
        UploadTerrainOverlayChunk(chunk);
        _blended!.BeginChunk(chunk, _blendedVertices);
        foreach (KeyValuePair<ShaderMaterial, List<int>> entry in _blendedByMaterial)
        {
            if (entry.Value.Count != 0)
            {
                _blended.AddChunkTriangles(entry.Key, entry.Value);
            }
        }

        _blended.EndChunk();
        UploadChunkSurfaces(chunk);
    }

    /// <summary>A tile's overlay and blended faces; every key's material is looked up in the tile's order, so the
    /// materials are made in the order they were when this thread gathered the opaque faces too.</summary>
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
            if (PostOcclusion || key.Recipe.Blend != KmfBlend.Opaque || key.Variant == TerrainMaterialVariant.Fog)
            {
                _generation++;
                _terrainOverlaySurface.Append(geometry, geometry.Triangles[s], _remap, _stamp, _generation);
            }
            if (_blendedMaterials.Contains(material))
            {
                // Write each tile's render vertices once and index its blended triangles into them.
                if (blendedStart < 0)
                {
                    blendedStart = _blendedVertices.Count;
                    for (int i = 0; i < geometry.VertexCount; i++)
                    {
                        _blendedVertices.Add(new BlendedVertex(geometry.Positions[i], geometry.Normals[i], geometry.Uvs[i],
                            geometry.LightAnchor, VertexColour.White) { StateSources = geometry.StateSources[i] });
                    }
                }
                if (!_blendedByMaterial.TryGetValue(material, out List<int>? list))
                {
                    _blendedByMaterial.Add(material, list = new List<int>());
                }

                TerrainIndexList triangles = geometry.Triangles[s];
                for (int i = 0; i < triangles.Count; i++)
                {
                    list.Add(blendedStart + triangles.Items[i]);
                }
            }
        }
    }

    /// <summary>The material a key draws with, shared by keys with the same look and sight-fog semantics.</summary>
    private ShaderMaterial MaterialFor(in TerrainMaterialKey key)
    {
        if (_keyMaterials.TryGetValue(key, out ShaderMaterial? material))
        {
            return material;
        }

        var look = TerrainMaterialLook.For(key);
        (TerrainMaterialLook look, bool) shaderKey = (look, key.Variant == TerrainMaterialVariant.Fog);
        if (!_lookMaterials.TryGetValue(shaderKey, out material))
        {
            material = WorldMaterial(look.Texture, look.Blend, look.WritesDepth, look.FullLight, look.Colour, look.AddedLight);
            ConfigureTerrainMaterial(material, key, look);
            _lookMaterials.Add(shaderKey, material);
            // Every non-opaque look goes to the transparent queue.
            if (look.Blend != KmfBlend.Opaque)
            {
                _blendedMaterials.Add(material);
            }
        }
        // The dig tag and tutorial additive pass draw with the opaque base
        // before every depth-sorted triangle (tag-highlight.md section 3), so before every other blended thing here.
        if (key.IsOverlay)
        {
            _overlayMaterials.Add(material);
        }

        _keyMaterials.Add(key, material);
        return material;
    }

    /// <summary>The custom attribute layouts: a terrain vertex's table anchor and state sources (CUSTOM0 as four floats), a model's bound sphere
    /// and its anchor and light kind (CUSTOM0 and CUSTOM1 as four), as Unity's TEXCOORD1-3.</summary>
    private const Mesh.ArrayFormat TerrainStateCustomFormat =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
    private const Mesh.ArrayFormat TerrainCustomFormat =
        (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
    private const Mesh.ArrayFormat ModelCustomFormat =
        (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | ((long)Mesh.ArrayCustomFormat.RFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift));

    /// <summary>The world shader's four render states (Shaders/World*.gdshader): what a uniform cannot switch; then the
    /// opaque state waving the liquids (<see cref="LiquidShaderState"/>).</summary>
    private static readonly string[] WorldShaderPaths =
    [
        "res://Shaders/WorldOpaque.gdshader", "res://Shaders/WorldBlend.gdshader", "res://Shaders/WorldBlendDepth.gdshader",
        "res://Shaders/WorldAdd.gdshader", "res://Shaders/WorldLiquid.gdshader",
    ];

    /// <summary>The index in <see cref="WorldShaderPaths"/> of Shaders/WorldLiquid.gdshader.</summary>
    private const int LiquidShaderState = 4;

    /// <summary>The line of each World*.gdshader that <see cref="ShaderFor"/> puts the light mode's defines before.</summary>
    private const string LightModeMarker = "// DK2_LIGHT_MODE";

    /// <summary>The one world shader (Shaders/World.gdshaderinc) in the render state a look draws with: opaque writes depth;
    /// blended draws do not, but for a fading object's (<see cref="ObjectMaterialLook.Faded"/>); additive is ONE, ONE.
    /// The default per-vertex light is the resource as it ships; <see cref="PerPixelLight"/> and
    /// <see cref="TorchShadowsActive"/> add their defines to its text, once a run. Everything else a look varies is a
    /// uniform (<see cref="WorldMaterial"/>) or, per drawn thing, an instance uniform (<see cref="ModelSlot.ApplyLooks"/>).</summary>
    private Shader ShaderFor(KmfBlend blend, bool writesDepth)
    {
        return ShaderFor(blend switch
        {
            KmfBlend.Opaque => 0,
            KmfBlend.AlphaBlend when writesDepth => 2,
            KmfBlend.AlphaBlend => 1,
            _ => 3,
        });
    }

    /// <summary>The world shader of <paramref name="state"/>, an index in <see cref="WorldShaderPaths"/>, in the run's
    /// light mode.</summary>
    private Shader ShaderFor(int state)
    {
        int mode = WorldLightMode;
        if (WorldShaders[mode, state] is Shader shader)
        {
            return shader;
        }

        if (mode == 0)
        {
            shader = GD.Load<Shader>(WorldShaderPaths[state]);
        }
        else
        {
            // The file's text, not its resource, so the per-vertex shader is not compiled as well.
            string code = Godot.FileAccess.GetFileAsString(WorldShaderPaths[state]);
            string defines = mode == 2 ? "#define DK2_PER_PIXEL\n#define DK2_TORCH_SHADOWS\n" : "#define DK2_PER_PIXEL\n";
            if (!code.Contains(LightModeMarker, StringComparison.Ordinal))
            {
                GD.PushError("DK2.Godot: ", WorldShaderPaths[state], " has no light-mode line; it draws with the per-vertex light.");
            }

            shader = new Shader { Code = code.Replace(LightModeMarker, defines + LightModeMarker, StringComparison.Ordinal) };
        }
        WorldShaders[mode, state] = shader;
        return shader;
    }

    /// <summary>The world shader's light mode: 0 per vertex, 1 per pixel, 2 per pixel with the torch shadows.</summary>
    private int WorldLightMode
    {
        get
        {
            return TorchShadowsActive ? 2 : PerPixelLight ? 1 : 0;
        }
    }

    /// <summary>A world material: the texture times <paramref name="colour"/>, lit by DK2's light plus
    /// <paramref name="addedLight"/> unless at <paramref name="fullLight"/>; a terrain vertex's light inputs unless the
    /// caller sets <c>model_vertex</c>. Uniforms left at the shader's defaults are not set.</summary>
    private ShaderMaterial WorldMaterial(string texture, KmfBlend blend, bool writesDepth, bool fullLight,
        System.Numerics.Vector4 colour, NVector3 addedLight)
    {
        return WorldMaterial(ShaderFor(blend, writesDepth), texture, fullLight, colour, addedLight);
    }

    private ShaderMaterial WorldMaterial(Shader shader, string texture, bool fullLight, System.Numerics.Vector4 colour,
        NVector3 addedLight)
    {
        var material = new ShaderMaterial { Shader = shader };
        _worldMaterialsMade++;
        material.SetShaderParameter(AlbedoParameter, TextureFor(texture));
        material.SetShaderParameter(ColourParameter, new Color(colour.X, colour.Y, colour.Z, colour.W));
        material.SetShaderParameter(AddedLightParameter, ToGodot(addedLight));
        if (fullLight)
        {
            material.SetShaderParameter(FullLightParameter, true);
        }

        return material;
    }

    /// <summary>How many entries <see cref="GammaOutput"/>'s table has: the half-texel clamp at either end costs at most
    /// 2.4 x 0.5 / 4096 of the range, under a tenth of a byte. Half floats (linear filtering of 32-bit float textures is
    /// optional in Vulkan) round by at most 1/4096 near 1, under a tenth of a byte.</summary>
    private const int GammaTableWidth = 4096;

    /// <summary>The camera's output: the colour buffer holds gamma-space values (the stored bytes over 255), which Godot's
    /// tonemap still encodes as if linear (<c>linear_to_srgb</c> in tonemap.glsl at 4.8 beta 3, c51ff06e8). A 1D colour
    /// correction table of <c>srgb_to_linear</c>, applied after that encoding, undoes it, so the window shows the buffer's
    /// values and every blend happened in gamma space, as in Unity's gamma colour space. Linear tonemap, exposure 1.</summary>
    private static Godot.Environment GammaOutput()
    {
        return new Godot.Environment()
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
    }

    /// <summary><see cref="GammaOutput"/>'s table, each display value then raised to <paramref name="exponent"/> (the
    /// brightness curve, <see cref="SetBrightness"/>; 1 leaves it).</summary>
    private static ImageTexture GammaTable(float exponent)
    {
        using var table = Image.CreateEmpty(GammaTableWidth, 1, false, Image.Format.Rgbah);
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
                _frustum = ViewFrustum.FromPose(_placed, _placedViewport.Width / (float)_placedViewport.Height, _near);
                _frustumStale = false;
            }
            return _frustum;
        }
    }

    /// <summary>Re-sorts the blended stream for the placed camera when it or the stream changed, keys each run by its real
    /// distance (<see cref="BlendedRunSlots{TMaterial}"/>), hands the keys to the blended items (<see cref="SetItemRuns"/>)
    /// and uploads what changed (<see cref="RunSlot"/>); times the frames that re-sort.</summary>
    private void DrawBlended()
    {
        if (_blended == null || _camera == null || View is not MinimapCameraView view)
        {
            return;
        }

        ViewFrustum frustum = PlacedFrustum;
        var forward = NVector3.Normalize(_placed.Forward);
        if (!_blended.NeedsRefresh(forward, NMatrix4x4.Identity, frustum.ViewProjection))
        {
            return;
        }

        long start = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        _blended.Refresh(forward, NMatrix4x4.Identity, frustum);
        // The stream is in the renderers' frame, the camera's distances the same in Godot's (a reflection keeps them).
        _runKeys.Update(_blended, view.Position);
        float eyeDistance = view.Position.Length();
        IReadOnlyList<BlendedRunSlot<ShaderMaterial>> drawn = _runKeys.Drawn;
        SetItemRuns(drawn);
        int triangles = 0;
        for (int i = 0; i < drawn.Count; i++)
        {
            BlendedRunSlot<ShaderMaterial> run = drawn[i];
            while (_runSlots.Count <= run.Id)
            {
                _runSlots.Add(new RunSlot(GetWorld3D().Scenario, _worldBox));
            }

            _blendedUploadBytes += _runSlots[run.Id].Show(run, _runKeys.Vertices, eyeDistance, _runScratch);
            triangles += run.IndexCount / 3;
        }
        IReadOnlyList<BlendedRunSlot<ShaderMaterial>> slots = _runKeys.Slots;
        for (int i = 0; i < _runSlots.Count; i++)
        {
            if (!slots[i].Drawn)
            {
                _runSlots[i].Hide();
            }
        }

        _blendedUploadTriangles += triangles;
        long ticks = Stopwatch.GetTimestamp() - start, bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        _blendedMaxRuns = Math.Max(_blendedMaxRuns, drawn.Count);
        _blendedMaxTriangles = Math.Max(_blendedMaxTriangles, triangles);
        if (_blendedFirstTicks < 0)
        {
            _blendedFirstTicks = ticks;
        }
        else
        {
            _blendedTicks += ticks;
            _blendedMaxTicks = Math.Max(_blendedMaxTicks, ticks);
        }
        if (_blendedRefreshes++ < BlendedWarmUp)
        {
            _blendedWarmUpBytes += bytes;
        }
        else
        {
            _blendedLateBytes += bytes;
        }
    }

    private Texture2D TextureFor(string name)
    {
        if (name.Length == 0)
        {
            return _white!;
        }

        if (_textures.TryGetValue(name, out Texture2D? texture))
        {
            return texture;
        }

        try
        {
            RgbaImage source = _art!.LoadTexture(name);
            // Rows top first, as Godot's V counts down from the top, which is the KMF's V (RenderMesh).
            using var image = Image.CreateFromData(source.Width, source.Height, false, Image.Format.Rgba8, source.Pixels);
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

    private static Vector3 ToGodot(NVector3 v)
    {
        return new Vector3(v.X, v.Y, v.Z);
    }

    private static string Milliseconds(long ticks)
    {
        return (Math.Max(0, ticks) * 1000.0 / Stopwatch.Frequency).ToString("0.000", CultureInfo.InvariantCulture);
    }

    public override void _ExitTree()
    {
        if (_camera != null)
        {
            GetViewport().SizeChanged -= PlaceCamera;
        }

        _terrain?.JoinBuild();
        if (_presenter != null)
        {
            GD.Print("DK2.Godot world: ", _steadyFrames, " frames without a terrain change allocated ", _steadyBytes,
                " B in Present, ", _changeFrames, " with one ", _changeBytes, " B; collections when the level opened ",
                _collectionsAtOpen[0], "/", _collectionsAtOpen[1], "/", _collectionsAtOpen[2], " (gen0/1/2), since then: ",
                GC.CollectionCount(0) - _collectionsAtOpen[0], " gen0, ", GC.CollectionCount(1) - _collectionsAtOpen[1], " gen1, ",
                GC.CollectionCount(2) - _collectionsAtOpen[2], " gen2; water ", _liquids!.Water.TileCount, " tiles, lava ",
                _liquids.Lava.TileCount, "; blended stream: ", _blendedRefreshes, " re-sorts, the first ", Milliseconds(_blendedFirstTicks),
                " ms, the rest ", Milliseconds(_blendedTicks / Math.Max(1, _blendedRefreshes - 1)), " ms mean and ",
                Milliseconds(_blendedMaxTicks), " ms max; allocated ", _blendedWarmUpBytes, " B in the first ", BlendedWarmUp,
                " and ", _blendedLateBytes, " B after; up to ", _blendedMaxRuns, " runs and ", _blendedMaxTriangles, " triangles, ", _runSlots.Count,
                " run instances; uploaded ", _blendedUploadBytes, " B for ", _blendedUploadTriangles, " triangles drawn");
        }

        FreeChunkUpload();
        FreeEffects();
        FreeCreatureShadows();
        FreeObjects();
        foreach (RunSlot slot in _previewRunSlots)
        {
            slot.Free();
        }

        foreach (RunSlot slot in _runSlots)
        {
            slot.Free();
        }

        _runSlots.Clear();
        _cage?.Free();
        _cage = null;
        _terrainStateTexture?.Dispose();
        _terrainStateImage?.Dispose();
        _art?.Dispose();
        _art = null;
    }

    private const Mesh.ArrayFormat LiquidCustomFormat = TerrainCustomFormat
        | (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
    private static readonly StringName LiquidSinesParameter = "liquid_sines", LiquidPhaseParameter = "liquid_phase",
        LiquidRestParameter = "liquid_rest", LiquidAmplitudeParameter = "liquid_amplitude", LiquidFrameParameter = "liquid_frame";

    /// <summary>A liquid's material: the plain terrain look of its texture (<see cref="TerrainMaterialLook"/>) on
    /// Shaders/WorldLiquid.gdshader, with <see cref="LiquidSurfaceWave"/>'s sine table, phase steps, rest height and
    /// signed amplitude, by which the shader waves each vertex.</summary>
    private ShaderMaterial LiquidMaterial(LiquidSurfaceWave wave)
    {
        var look = TerrainMaterialLook.For(
            new TerrainMaterialKey(new ClassicMaterialRecipe(wave.Texture, KmfBlend.Opaque, 0f), TerrainMaterialVariant.Plain));
        ShaderMaterial material = WorldMaterial(ShaderFor(LiquidShaderState), look.Texture, look.FullLight, look.Colour, look.AddedLight);
        float[] sines = new float[LiquidSurfaceWave.MaximumSines];
        LiquidSurfaceWave.Sines(wave.Kind).CopyTo(sines);
        material.SetShaderParameter(LiquidSinesParameter, sines);
        material.SetShaderParameter(LiquidPhaseParameter, new Vector3I(LiquidSurfaceWave.PhaseTileStep(wave.Kind),
            LiquidSurfaceWave.PhaseVertexStep(wave.Kind), LiquidSurfaceWave.PhaseMask(wave.Kind)));
        material.SetShaderParameter(LiquidRestParameter, LiquidSurfaceWave.Rest(wave.Kind));
        material.SetShaderParameter(LiquidAmplitudeParameter, LiquidSurfaceWave.SignedAmplitude(wave.Kind));
        return material;
    }

    /// <summary>One liquid's surface: a static mesh built when its tiles change, each vertex carrying its tile's table
    /// anchor (CUSTOM0) and its grid point (CUSTOM1, <see cref="LiquidSurfaceWave.Points"/>), waved on the GPU by
    /// Shaders/WorldLiquid.gdshader at DK2's frame, the one uniform set when the frame moves. Nothing is uploaded after
    /// the build, and the mesh carries no normals: the shader computes them.</summary>
    private sealed class LiquidLayer
    {
        private readonly LiquidSurfaceWave _wave;

        public LiquidLayer(LiquidSurfaceWave wave)
        {
            _wave = wave;
        }

        private int _version = -1, _frame = -1;
        private MeshInstance3D? _instance;
        private ArrayMesh? _mesh;
        private ShaderMaterial? _material;

        public void Show(WorldScene scene, int frame)
        {
            if (_version != _wave.Version)
            {
                Rebuild(scene);
            }

            if (_material == null || frame == _frame)
            {
                return;
            }

            _frame = frame;
            _material.SetShaderParameter(LiquidFrameParameter, frame);
        }

        private void Rebuild(WorldScene scene)
        {
            _version = _wave.Version;
            _mesh?.ClearSurfaces();
            int count = _wave.VertexCount;
            if (count == 0)
            {
                return;
            }
            // The positions only bound the mesh (the shader places every vertex): the wave at frame 0, from SetTiles.
            var positions = new Vector3[count];
            var uvs = new Vector2[count];
            float[] anchors = new float[count * 2];
            float[] points = new float[count * 4];
            for (int i = 0; i < count; i++)
            {
                positions[i] = ToGodot(RightHandedFrame.Point(_wave.Positions[i]));
                uvs[i] = new Vector2(_wave.Uvs[i].X, _wave.Uvs[i].Y);
                anchors[i * 2] = _wave.LightAnchors[i].X;
                anchors[(i * 2) + 1] = _wave.LightAnchors[i].Y;
                LiquidGridPoint point = _wave.Points[i];
                points[i * 4] = point.Tile.X;
                points[(i * 4) + 1] = point.Tile.Y;
                points[(i * 4) + 2] = point.Column;
                points[(i * 4) + 3] = point.Row;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Custom0] = anchors;
            arrays[(int)Mesh.ArrayType.Custom1] = points;
            arrays[(int)Mesh.ArrayType.Index] = _wave.Triangles.AsSpan(0, _wave.IndexCount).ToArray();
            _mesh ??= new ArrayMesh();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, LiquidCustomFormat);
            _material ??= scene.LiquidMaterial(_wave);
            _mesh.SurfaceSetMaterial(0, _material);
            if (_instance == null)
            {
                _instance = new MeshInstance3D { Name = _wave.Kind + "Surface", Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                scene.AddChild(_instance);
            }
            // The wave moves each vertex within twice its amplitude of the rest height; the box covers every frame.
            Aabb box = _mesh.GetAabb();
            float reach = 2f * Math.Max(LiquidSurfaceWave.WaterAmplitude, LiquidSurfaceWave.LavaAmplitude);
            _instance.CustomAabb = new Aabb(box.Position - new Vector3(0, reach, 0), box.Size + new Vector3(0, 2 * reach, 0));
        }
    }

    /// <summary>Draw the cage with <see cref="TileHoverRibbon"/>'s triangles unindexed in a dynamic surface.
    /// Pad with zero-area triangles. Its box is the ribbon's, so Godot keys it by the distance to the
    /// ribbon's centre among the blended runs and objects (<see cref="RunSlot"/>).</summary>
    private sealed class CageRibbon
    {
        private const RenderingServer.ArrayFormat Format = RenderingServer.ArrayFormat.FormatVertex
            | RenderingServer.ArrayFormat.FormatTexUV | RenderingServer.ArrayFormat.FlagUseDynamicUpdate;
        private const int PositionStride = 12, UvStride = 8;
        private static readonly StringName ColourParameter = "colour";

        private readonly Rid _instance, _mesh;
        private readonly ShaderMaterial _material;
        private readonly TileHoverRibbon _ribbon = new TileHoverRibbon();
        private byte[] _positions = [], _uvs = [];
        private int _capacity, _written;
        private bool _visible = true;
        private NVector3 _colour = new NVector3(-1, -1, -1);
        private Aabb _box;

        public CageRibbon(Rid scenario, Aabb box)
        {
            _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Cage.gdshader") };
            _mesh = RenderingServer.MeshCreate();
            _instance = RenderingServer.InstanceCreate2(_mesh, scenario);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(_instance, RenderingServer.ShadowCastingSetting.Off);
            RenderingServer.InstanceSetCustomAabb(_instance, box);
            RenderingServer.InstanceGeometrySetMaterialOverride(_instance, _material.GetRid());
            Hide();
        }

        public void Show(TileRectangle area, bool blue, NVector3 eye, TerrainLattice? lattice, long milliseconds)
        {
            TileHoverOutline.Build(_ribbon, area, blue, eye, lattice, milliseconds);
            int count = _ribbon.IndexCount;
            if (count > _capacity)
            {
                Grow(count);
            }

            if (count == 0) { Hide(); return; }
            int vertexCount = Math.Max(count, _written);
            Span<float> positions = MemoryMarshal.Cast<byte, float>(_positions.AsSpan(0, vertexCount * PositionStride));
            Span<float> uvs = MemoryMarshal.Cast<byte, float>(_uvs.AsSpan(0, vertexCount * UvStride));
            ReadOnlySpan<int> indices = _ribbon.Indices;
            ReadOnlySpan<NVector3> from = _ribbon.Positions;
            ReadOnlySpan<NVector2> along = _ribbon.Uvs;
            NVector3 low = new NVector3(float.MaxValue), high = new NVector3(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                NVector3 at = RightHandedFrame.Point(from[indices[i]]);
                low = NVector3.Min(low, at);
                high = NVector3.Max(high, at);
                positions[i * 3] = at.X; positions[(i * 3) + 1] = at.Y; positions[(i * 3) + 2] = at.Z;
                uvs[i * 2] = along[indices[i]].X; uvs[(i * 2) + 1] = along[indices[i]].Y;
            }
            // The ribbon's bounds: Godot culls by them and sorts by their centre (use_aabb_center, the default).
            var box = new Aabb(ToGodot(low), ToGodot(high - low));
            if (box != _box)
            {
                _box = box;
                RenderingServer.InstanceSetCustomAabb(_instance, box);
            }
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
            if (!_visible)
            {
                return;
            }

            RenderingServer.InstanceSetVisible(_instance, false);
            _visible = false;
        }

        /// <summary>Whether the ribbon is drawn, and its box (Godot sorts it by the centre): a blended item's barrier.</summary>
        public bool Visible
        {
            get
            {
                return _visible && _written > 0;
            }
        }

        public Aabb Box
        {
            get
            {
                return _box;
            }
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
            while (capacity < count)
            {
                capacity <<= 1;
            }

            _capacity = capacity;
            if (RenderingServer.MeshSurfaceGetFormatVertexStride(Format, capacity) != PositionStride
                || RenderingServer.MeshSurfaceGetFormatAttributeStride(Format, capacity) != UvStride)
            {
                GD.PushError("DK2.Godot: the cage's vertex layout is not positions then UVs; it draws wrong.");
            }

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

    /// <summary>One slot of the blended stream (<see cref="BlendedRunSlot{TMaterial}"/>): an instance of its own indexed
    /// one-surface mesh (positions, normals, UVs and table anchors, as a terrain chunk's) whose vertices stay put while a
    /// re-sort rewrites only the indices, in the run's order (a surface draws its primitives in order), with zero-area
    /// triangles past the run. Godot 4.8 sorts the transparent pass by material priority, then by camera distance minus the
    /// instance's sorting offset, farthest first, with an unstable sort (<c>SortByReverseDepthAndPriority</c>,
    /// <c>SortArray</c> in render_forward_clustered.h, <c>_fill_render_list</c> in render_forward_clustered.cpp at
    /// c51ff06e8): with a perspective camera the depth is the straight-line distance from the camera to the pivot, not the
    /// depth along the view (docs/TRAPS.md). The instance sorts from the origin (<c>use_aabb_center</c> false) with the
    /// offset that makes its depth the run's <see cref="BlendedRunSlot{TMaterial}.Distance"/>, so the runs keep the
    /// stream's order and the blended objects, flames, the cage and the effect batches, keyed by their own AABB centres in
    /// the same measure, fall between them. A tag overlay's material draws first by its priority
    /// (<see cref="OverlayPriority"/>).</summary>
    private sealed class RunSlot
    {
        /// <summary>The upload buffers every slot shares: positions and normals with their tangents (the vertex stream's two
        /// blocks), each vertex's UV and table anchor (the attribute stream), and the indices.</summary>
        public sealed class Scratch
        {
            public byte[] Positions = [], Normals = [], Uvs = [], Indices = [];
        }

        private const RenderingServer.ArrayFormat Format = RenderingServer.ArrayFormat.FormatVertex
            | RenderingServer.ArrayFormat.FormatNormal | RenderingServer.ArrayFormat.FormatTexUV
            | RenderingServer.ArrayFormat.FormatCustom0 | RenderingServer.ArrayFormat.FormatIndex
            | RenderingServer.ArrayFormat.FlagUseDynamicUpdate
            | (RenderingServer.ArrayFormat)((long)RenderingServer.ArrayCustomFormat.RgbaFloat << (int)RenderingServer.ArrayFormat.FormatCustom0Shift);
        private const int PositionStride = 12, NormalSize = 4, UvStride = 24;

        private readonly Rid _instance, _mesh;
        private ShaderMaterial? _material;
        private int _vertexCapacity, _indexCapacity, _indexStride, _written;

        /// <summary>How many surfaces the slot has built, for the exit report.</summary>
        public int Grows { get; private set; }
        // The normal-tangent block as the surface holds it: Godot 4.8 adds a tangent to a surface with normals and none of
        // its own (format bit 2), so a vertex there is 8 bytes, not the 4 of the format asked for; the tangent as built.
        private int _normalBlock, _normalStride, _normalAt;
        private static RenderingServer.ArrayFormat s_builtFormat;
        // The tangent Godot built for an up normal, written unchanged beside every normal: no world shader reads TANGENT.
        private static uint s_tangent;
        private float _offset = float.NaN;
        private bool _visible = true;

        public RunSlot(Rid scenario, Aabb box, uint layers = MainBlendedLayer)
        {
            _mesh = RenderingServer.MeshCreate();
            _instance = RenderingServer.InstanceCreate2(_mesh, scenario);
            RenderingServer.InstanceSetLayerMask(_instance, layers);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(_instance, RenderingServer.ShadowCastingSetting.Off);
            // The world box only keeps Godot from culling the run (the stream culls); the pivot is the origin.
            RenderingServer.InstanceSetCustomAabb(_instance, box);
        }

        /// <summary>Draws <paramref name="run"/> at its key for an eye <paramref name="eyeDistance"/> from the origin, uploading
        /// the vertices the slot has not held and the indices when they changed; returns the bytes uploaded.</summary>
        public long Show(BlendedRunSlot<ShaderMaterial> run, ReadOnlySpan<BlendedVertex> vertices, float eyeDistance, Scratch scratch)
        {
            if (!_visible) { RenderingServer.InstanceSetVisible(_instance, true); _visible = true; }
            if (!ReferenceEquals(run.Material, _material))
            {
                RenderingServer.InstanceGeometrySetMaterialOverride(_instance, run.Material.GetRid());
                _material = run.Material;
            }
            // The tag overlay's category, which the shared slot decides, as its material's priority.
            int priority = run.TagOverlay ? OverlayPriority : 0;
            if (run.Material.RenderPriority != priority)
            {
                run.Material.RenderPriority = priority;
            }
            // Godot's depth is the origin's distance minus the offset: the run's distance. (A box centred on the run's
            // nearest centroid, which leaves Godot to measure it, measured no better on Level7 and was not kept.)
            float offset = eyeDistance - run.Distance;
            if (offset != _offset)
            {
                RenderingServer.InstanceSetPivotData(_instance, offset, false);
                _offset = offset;
            }
            bool all = false;
            if (run.VertexCount > _vertexCapacity || run.IndexCount > _indexCapacity)
            {
                Grow(run.VertexCount, run.IndexCount);
                all = true;
            }
            long bytes = 0;
            int from = all ? 0 : run.FirstNewVertex;
            if (run.VertexCount > from)
            {
                bytes += UploadVertices(run, vertices, from, scratch);
            }

            if (all || run.IndicesChanged)
            {
                bytes += UploadIndices(run, scratch);
            }

            return bytes;
        }

        public void Hide()
        {
            if (!_visible)
            {
                return;
            }

            RenderingServer.InstanceSetVisible(_instance, false);
            _visible = false;
        }

        public void Free()
        {
            RenderingServer.FreeRid(_instance);
            RenderingServer.FreeRid(_mesh);
        }

        /// <summary>Slot vertices [<paramref name="from"/>, count) into the vertex and attribute streams.</summary>
        private long UploadVertices(BlendedRunSlot<ShaderMaterial> run, ReadOnlySpan<BlendedVertex> vertices, int from, Scratch scratch)
        {
            int count = run.VertexCount - from;
            if (scratch.Positions.Length < count * PositionStride || scratch.Normals.Length < count * _normalStride)
            {
                int capacity = Math.Max(count, scratch.Positions.Length / PositionStride * 2);
                scratch.Positions = new byte[capacity * PositionStride];
                scratch.Normals = new byte[capacity * _normalStride];
                scratch.Uvs = new byte[capacity * UvStride];
            }
            Span<float> positions = MemoryMarshal.Cast<byte, float>(scratch.Positions.AsSpan(0, count * PositionStride));
            Span<byte> normals = scratch.Normals.AsSpan(0, count * _normalStride);
            Span<float> uvs = MemoryMarshal.Cast<byte, float>(scratch.Uvs.AsSpan(0, count * UvStride));
            IReadOnlyList<int> held = run.Vertices;
            for (int i = 0; i < count; i++)
            {
                BlendedVertex vertex = vertices[held[from + i]];
                NVector3 at = RightHandedFrame.Point(vertex.Position);
                positions[i * 3] = at.X;
                positions[(i * 3) + 1] = at.Y;
                positions[(i * 3) + 2] = at.Z;
                // Godot's own normal encoding (_surface_set_data), then the tangent Godot built, when it holds one.
                Vector2 o = ToGodot(RightHandedFrame.Normal(vertex.Normal)).OctahedronEncode();
                Span<byte> slot = normals.Slice(i * _normalStride, _normalStride);
                if (_normalStride > NormalSize)
                {
                    MemoryMarshal.Write(slot[(NormalSize - _normalAt)..], in s_tangent);
                }

                Span<ushort> normal = MemoryMarshal.Cast<byte, ushort>(slot.Slice(_normalAt, NormalSize));
                normal[0] = (ushort)Math.Clamp(o.X * 65535f, 0f, 65535f);
                normal[1] = (ushort)Math.Clamp(o.Y * 65535f, 0f, 65535f);
                uvs[i * 6] = vertex.Uv.X;
                uvs[(i * 6) + 1] = vertex.Uv.Y;
                uvs[(i * 6) + 2] = vertex.LightAnchor.X;
                uvs[(i * 6) + 3] = vertex.LightAnchor.Y;
                uvs[(i * 6) + 4] = vertex.StateSources.X;
                uvs[(i * 6) + 5] = vertex.StateSources.Y;
            }
            RenderingServer.MeshSurfaceUpdateVertexRegion(_mesh, 0, from * PositionStride, scratch.Positions.AsSpan(0, count * PositionStride));
            RenderingServer.MeshSurfaceUpdateVertexRegion(_mesh, 0, _normalBlock + (from * _normalStride), normals);
            RenderingServer.MeshSurfaceUpdateAttributeRegion(_mesh, 0, from * UvStride, scratch.Uvs.AsSpan(0, count * UvStride));
            return (long)count * (PositionStride + _normalStride + UvStride);
        }

        /// <summary>The run's indices, and zeros over what the last upload wrote past them.</summary>
        private long UploadIndices(BlendedRunSlot<ShaderMaterial> run, Scratch scratch)
        {
            int count = Math.Max(run.IndexCount, _written);
            if (scratch.Indices.Length < count * _indexStride)
            {
                scratch.Indices = new byte[Math.Max(count * _indexStride, scratch.Indices.Length * 2)];
            }

            ReadOnlySpan<int> indices = run.Indices;
            if (_indexStride == 2)
            {
                Span<ushort> into = MemoryMarshal.Cast<byte, ushort>(scratch.Indices.AsSpan(0, count * 2));
                for (int i = 0; i < indices.Length; i++)
                {
                    into[i] = (ushort)indices[i];
                }

                into[indices.Length..].Clear();
            }
            else
            {
                Span<int> into = MemoryMarshal.Cast<byte, int>(scratch.Indices.AsSpan(0, count * 4));
                indices.CopyTo(into);
                into[indices.Length..].Clear();
            }
            RenderingServer.MeshSurfaceUpdateIndexRegion(_mesh, 0, 0, scratch.Indices.AsSpan(0, count * _indexStride));
            _written = run.IndexCount;
            return (long)count * _indexStride;
        }

        /// <summary>A new zeroed surface of power-of-two vertex and triangle capacities; the only allocation, as a run outgrows it.</summary>
        private void Grow(int vertexCount, int indexCount)
        {
            Grows++;
            // Room for a slot's spare vertices (BlendedRunSlot.SpareVertices) from the start, so few slots grow again.
            int vertices = Math.Max(256, _vertexCapacity), triangles = Math.Max(128, _indexCapacity / 3);
            while (vertices < vertexCount)
            {
                vertices <<= 1;
            }

            while (triangles * 3 < indexCount)
            {
                triangles <<= 1;
            }

            _vertexCapacity = vertices;
            _indexCapacity = triangles * 3;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[vertices];
            var up = new Vector3[vertices];
            Array.Fill(up, Vector3.Up);
            arrays[(int)Mesh.ArrayType.Normal] = up;
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[vertices];
            arrays[(int)Mesh.ArrayType.Custom0] = new float[vertices * 4];
            arrays[(int)Mesh.ArrayType.Index] = new int[_indexCapacity];
            RenderingServer.MeshClear(_mesh);
            RenderingServer.MeshAddSurfaceFromArrays(_mesh, RenderingServer.PrimitiveType.Triangles, arrays, null, null,
                Format & ~(RenderingServer.ArrayFormat.FormatVertex | RenderingServer.ArrayFormat.FormatNormal
                    | RenderingServer.ArrayFormat.FormatTexUV | RenderingServer.ArrayFormat.FormatCustom0
                    | RenderingServer.ArrayFormat.FormatIndex));
            // The layout as built: the surface's own format, read back by the first slot of the process, since Godot adds
            // the tangent; every slot's surface has the same format.
            if (s_builtFormat == 0)
            {
                Godot.Collections.Dictionary built = RenderingServer.MeshGetSurface(_mesh, 0);
                s_builtFormat = (RenderingServer.ArrayFormat)built["format"].AsInt64();
                if ((s_builtFormat & RenderingServer.ArrayFormat.FormatTangent) != 0)
                {
                    int at = (int)RenderingServer.MeshSurfaceGetFormatOffset(s_builtFormat, vertices, (int)Mesh.ArrayType.Tangent);
                    s_tangent = MemoryMarshal.Read<uint>(built["vertex_data"].AsByteArray().AsSpan(at, 4));
                }
            }
            RenderingServer.ArrayFormat format = s_builtFormat;
            _indexStride = (int)RenderingServer.MeshSurfaceGetFormatIndexStride(format, vertices);
            _normalStride = (int)RenderingServer.MeshSurfaceGetFormatNormalTangentStride(format, vertices);
            _normalBlock = (int)RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)Mesh.ArrayType.Normal);
            _normalAt = (format & RenderingServer.ArrayFormat.FormatTangent) != 0
                && RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)Mesh.ArrayType.Tangent) == _normalBlock ? NormalSize : 0;
            if (RenderingServer.MeshSurfaceGetFormatVertexStride(format, vertices) != PositionStride
                || _normalBlock != vertices * PositionStride
                || (_normalStride != NormalSize && _normalStride != 2 * NormalSize)
                || RenderingServer.MeshSurfaceGetFormatAttributeStride(format, vertices) != UvStride
                || RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)Mesh.ArrayType.Custom0) != 8
                || (_indexStride != 2 && _indexStride != 4))
            {
                GD.PushError("DK2.Godot: the blended run's buffer layout is not positions, normals (and tangents), then UV and anchor, with 16 or 32-bit indices; runs draw wrong.");
            }

            _written = 0;
        }
    }

    /// <summary>One key's surface in the chunk being built: the vertices its triangles use, each copied once, in
    /// Godot's frame, and its triangles renumbered to them. The overlay's on the frame's thread, the opaque keys' on the
    /// build task (<see cref="ChunkPacker"/>).</summary>
    private sealed class SurfaceBuffer
    {
        private NVector3[] _positions = new NVector3[256], _normals = new NVector3[256];
        private NVector2[] _uvs = new NVector2[256], _anchors = new NVector2[256], _stateSources = new NVector2[256];
        private int[] _indices = new int[512];
        private int _vertexCount;

        public TerrainMaterialKey Key { get; private set; }
        public int IndexCount { get; private set; }
        public int VertexCount
        {
            get
            {
                return _vertexCount;
            }
        }

        public ReadOnlySpan<NVector3> Positions
        {
            get
            {
                return _positions.AsSpan(0, _vertexCount);
            }
        }

        public ReadOnlySpan<NVector3> Normals
        {
            get
            {
                return _normals.AsSpan(0, _vertexCount);
            }
        }

        public ReadOnlySpan<NVector2> Uvs
        {
            get
            {
                return _uvs.AsSpan(0, _vertexCount);
            }
        }

        public ReadOnlySpan<NVector2> Anchors
        {
            get
            {
                return _anchors.AsSpan(0, _vertexCount);
            }
        }

        public ReadOnlySpan<NVector2> StateSources
        {
            get
            {
                return _stateSources.AsSpan(0, _vertexCount);
            }
        }

        public ReadOnlySpan<int> Indices
        {
            get
            {
                return _indices.AsSpan(0, IndexCount);
            }
        }

        public void Reset(in TerrainMaterialKey key)
        {
            Key = key;
            _vertexCount = 0;
            IndexCount = 0;
        }

        public void Append(TerrainTileGeometry tile, TerrainIndexList triangles, int[] remap, int[] stamp, int generation)
        {
            int[] items = triangles.Items;
            if (_indices.Length < IndexCount + triangles.Count)
            {
                Array.Resize(ref _indices, Math.Max(IndexCount + triangles.Count, _indices.Length * 2));
            }

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
                        Array.Resize(ref _stateSources, _vertexCount * 2);
                    }
                    _positions[_vertexCount] = RightHandedFrame.Point(tile.Positions[vertex]);
                    _normals[_vertexCount] = RightHandedFrame.Normal(tile.Normals[vertex]);
                    _uvs[_vertexCount] = tile.Uvs[vertex];
                    _anchors[_vertexCount] = tile.LightAnchor;
                    _stateSources[_vertexCount] = tile.StateSources[vertex];
                    _vertexCount++;
                }
                _indices[IndexCount++] = remap[vertex];
            }
        }

        /// <summary>The surface as Godot's exact-length arrays, with each vertex's table anchor and state sources (<see cref="TerrainStateCustomFormat"/>);
        /// the index order is kept (<see cref="RightHandedFrame"/>).</summary>
        public Godot.Collections.Array ToArrays()
        {
            var positions = new Vector3[_vertexCount];
            var normals = new Vector3[_vertexCount];
            var uvs = new Vector2[_vertexCount];
            float[] anchors = new float[_vertexCount * 4];
            for (int i = 0; i < _vertexCount; i++)
            {
                positions[i] = ToGodot(_positions[i]);
                normals[i] = ToGodot(_normals[i]);
                uvs[i] = new Vector2(_uvs[i].X, _uvs[i].Y);
                anchors[i * 4] = _anchors[i].X;
                anchors[(i * 4) + 1] = _anchors[i].Y;
                anchors[(i * 4) + 2] = _stateSources[i].X;
                anchors[(i * 4) + 3] = _stateSources[i].Y;
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
