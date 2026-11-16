// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The 3D world under <see cref="InGameHud"/> (plan steps 3a to 3c): the terrain as <see cref="TerrainTileBuilder"/>
/// builds it and <see cref="TerrainChunkScheduler"/> schedules it, one <see cref="MeshInstance3D"/> per 16x16-tile chunk
/// with a surface per <see cref="TerrainMaterialKey"/> drawn as <see cref="TerrainMaterialLook"/> describes it, and the
/// waving water and lava (<see cref="LiquidSurfaceField"/>), unshaded under a stand-in light, seen from the classic
/// camera. Everything crosses into Godot's right-handed frame through <see cref="RightHandedFrame"/>. Not yet drawn:
/// creatures, objects, structures and torches (no-ops here) and lighting. The host presents the world once a frame
/// (<see cref="Present"/>), moves the camera's <see cref="Focus"/> and places it (<see cref="PlaceCamera"/>).</summary>
public partial class WorldScene : Node3D, IRenderTarget, ITerrainChunkSink
{
    /// <summary>Unity's camera near plane (Dungeon.unity), which the frustum is sized at.</summary>
    private const float NearPlane = 0.3f;

    /// <summary>The vertex light every lit material is drawn under until the classic lighting is ported (step 3i):
    /// below 1, so the light the tagged, red and tinted twins add shows before DK2's clamp, as it does where DK2's lights
    /// leave headroom. The remake's choice, not DK2's.</summary>
    private const float StandInLight = 0.5f;

    private static readonly StringName AlbedoParameter = "albedo_texture", ColourParameter = "base_colour",
        AddedLightParameter = "added_light";

    private InstallArtProvider? _art;
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

    private readonly Dictionary<TerrainMaterialKey, ShaderMaterial> _keyMaterials = new();
    private readonly Dictionary<TerrainMaterialLook, ShaderMaterial> _lookMaterials = new();
    private readonly Dictionary<(KmfBlend, bool), Shader> _shaders = new();
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
        _terrain = new TerrainChunkScheduler(new InstallTerrainPieces(_art), world.Width, world.Height, lattice);
        _chunks = new MeshInstance3D?[_terrain.ChunkCount];
        _liquids = new LiquidSurfaceField(world.Width, world.Height);
        _water = new LiquidLayer(_liquids.Water);
        _lava = new LiquidLayer(_liquids.Lava);
        using (Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
        {
            white.Fill(Colors.White);
            _white = ImageTexture.CreateFromImage(white);
        }

        // The install, then the placeholder, as Unity's chain without its optional folders. The first present builds
        // every chunk inline, so the level appears whole.
        _presenter = new ScenePresenter(view, AssetProviderChain.Standard(install: _art), this);
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
        View = MinimapCameraView.Of(pose, viewport);
        (float frustumHeight, float offset) = RightHandedFrame.Frustum(pose.VerticalFieldOfView, NearPlane, pose.ProjectionCentreShift);
        _camera.Size = frustumHeight;
        _camera.FrustumOffset = new Vector2(0, offset);
        _camera.Far = Math.Max(NearPlane + 0.01f, pose.FarClipDistance);
        RightHandedCamera basis = RightHandedFrame.Camera(pose);
        _camera.Transform = new Transform3D(new Basis(ToGodot(basis.X), ToGodot(basis.Y), ToGodot(basis.Z)), ToGodot(basis.Origin));
    }

    public void CreateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture) => SetTile(tileIndex, tile);

    public void UpdateTile(int tileIndex, in TileView tile, in ResolvedAsset ground, in ResolvedAsset furniture) => SetTile(tileIndex, tile);

    private void SetTile(int tileIndex, in TileView tile)
    {
        _liquids!.Set(tileIndex, tile);
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

    public void CreateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation) { }
    public void UpdateCreature(int creatureId, in CreatureView creature, in ResolvedAsset mesh, in ResolvedAsset animation) { }
    public void RemoveCreature(int creatureId) { }
    public void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh) { }
    public void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh) { }
    public void RemoveObject(int objectId) { }
    public void CreateStructure(StructureId id, in StructureView structure, in ResolvedAsset mesh) { }
    public void UpdateStructure(StructureId id, in StructureView structure, in ResolvedAsset mesh) { }
    public void RemoveStructure(StructureId id) { }
    public void CreateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh) { }
    public void UpdateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh) { }
    public void RemoveTorch(int floorTileIndex) { }

    /// <summary>The scheduler's flush (the <see cref="ITerrainChunkSink"/> calls below upload), then the liquids' wave.</summary>
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
    }

    void ITerrainChunkSink.AddTile(int tileIndex, TerrainTileGeometry geometry) => GatherTile(geometry);

    /// <summary>Each key's gathered vertices as one surface of the chunk's mesh.</summary>
    void ITerrainChunkSink.EndChunk(int chunk)
    {
        MeshInstance3D? instance = _chunks[chunk];
        var mesh = instance?.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        foreach (SurfaceBuffer surface in _surfaces)
        {
            if (surface.IndexCount == 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface.ToArrays());
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, MaterialFor(surface.Key));
        }
        if (instance == null && mesh.GetSurfaceCount() > 0)
        {
            instance = new MeshInstance3D { Name = "Chunk" + chunk, Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
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
        for (int s = 0; s < geometry.KeyCount; s++)
        {
            TerrainMaterialKey key = geometry.Keys[s];
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
            material = new ShaderMaterial { Shader = ShaderFor(look.Blend, look.FullLight) };
            material.SetShaderParameter(AlbedoParameter, TextureFor(look.Texture));
            material.SetShaderParameter(ColourParameter, new Color(look.Colour.X, look.Colour.Y, look.Colour.Z, look.Colour.W));
            material.SetShaderParameter(AddedLightParameter, ToGodot(look.AddedLight));
            _lookMaterials.Add(look, material);
        }
        _keyMaterials.Add(key, material);
        return material;
    }

    /// <summary>One shader per blend and light: the texture times the colour, times the stand-in light plus the added
    /// light packed to bytes and clamped (Unity's <c>DK2PackedDiffuse</c>) unless at full light. The arithmetic is on the
    /// stored bytes, as DK2 and Unity's gamma space do it, and the result is turned linear so Godot's output stores those
    /// bytes again; the blending itself is Godot's, in linear space. Opaque writes depth; blended draws do not.</summary>
    private Shader ShaderFor(KmfBlend blend, bool fullLight)
    {
        if (_shaders.TryGetValue((blend, fullLight), out Shader? shader)) return shader;
        string mode = blend switch
        {
            KmfBlend.Opaque => "blend_mix, depth_draw_opaque",
            KmfBlend.AlphaBlend => "blend_mix, depth_draw_never",
            // ONE/ONE: blend_add weights the source by its alpha, held at 1.
            _ => "blend_add, depth_draw_never",
        };
        string light = fullLight ? ""
            : "c.rgb *= min(floor(max(vec3(" + StandInLight.ToString("0.0###", CultureInfo.InvariantCulture)
                + ") + added_light, vec3(0.0)) * 255.0 + 0.00001), vec3(255.0)) / 255.0;\n";
        string alpha = blend switch { KmfBlend.Opaque => "", KmfBlend.AlphaBlend => "ALPHA = c.a;\n", _ => "ALPHA = 1.0;\n" };
        shader = new Shader
        {
            Code = "shader_type spatial;\nrender_mode unshaded, cull_back, " + mode + ";\n"
                + "uniform sampler2D albedo_texture : filter_linear_mipmap, repeat_disable;\n"
                + "uniform vec4 base_colour = vec4(1.0);\n"
                + "uniform vec3 added_light = vec3(0.0);\n"
                + "vec3 to_linear(vec3 c) { return mix(pow((c + vec3(0.055)) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045))); }\n"
                + "void fragment() {\nvec4 c = texture(albedo_texture, UV) * base_colour;\n" + light
                + "ALBEDO = to_linear(c.rgb);\n" + alpha + "}\n",
        };
        _shaders.Add((blend, fullLight), shader);
        return shader;
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

    public override void _ExitTree()
    {
        if (_camera != null) GetViewport().SizeChanged -= PlaceCamera;
        _terrain?.JoinBuild();
        if (_presenter != null)
            GD.Print("DK2.Godot world: ", _steadyFrames, " frames without a terrain change allocated ", _steadyBytes,
                " B in Present, ", _changeFrames, " with one ", _changeBytes, " B; collections since the level opened: ",
                GC.CollectionCount(0) - _collectionsAtOpen[0], " gen0, ", GC.CollectionCount(1) - _collectionsAtOpen[1], " gen1, ",
                GC.CollectionCount(2) - _collectionsAtOpen[2], " gen2; water ", _liquids!.Water.TileCount, " tiles, lava ",
                _liquids.Lava.TileCount);
        _art?.Dispose();
        _art = null;
    }

    /// <summary>One liquid's surface: a mesh built when its tiles change, whose positions are rewritten in place every
    /// frame from one reused buffer, the head of Godot's vertex stream. The normals stay as built: every material is
    /// unshaded until step 3i, which uploads the wave's normals here (octahedral, after the positions, interleaved with
    /// the tangents; <see cref="RenderingServer.MeshSurfaceGetFormatOffset"/>).</summary>
    private sealed class LiquidLayer(LiquidSurfaceWave wave)
    {
        private int _version = -1;
        private MeshInstance3D? _instance;
        private ArrayMesh? _mesh;
        private byte[] _stream = [];

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
            for (int i = 0; i < count; i++)
            {
                positions[i] = ToGodot(RightHandedFrame.Point(wave.Positions[i]));
                normals[i] = ToGodot(RightHandedFrame.Normal(wave.Normals[i]));
                uvs[i] = new Vector2(wave.Uvs[i].X, wave.Uvs[i].Y);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = wave.Triangles.AsSpan(0, wave.IndexCount).ToArray();
            _mesh ??= new ArrayMesh();
            _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, Mesh.ArrayFormat.FlagUseDynamicUpdate);
            _mesh.SurfaceSetMaterial(0, scene.MaterialFor(
                new TerrainMaterialKey(new ClassicMaterialRecipe(wave.Texture, KmfBlend.Opaque, 0f), TerrainMaterialVariant.Plain)));
            if (_stream.Length < count * 12) _stream = new byte[count * 12];
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
    }

    /// <summary>One key's surface in the chunk being built: the vertices its triangles use, each copied once, in
    /// Godot's frame, and its triangles renumbered to them.</summary>
    private sealed class SurfaceBuffer
    {
        private NVector3[] _positions = new NVector3[256], _normals = new NVector3[256];
        private NVector2[] _uvs = new NVector2[256];
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
                    }
                    _positions[_vertexCount] = RightHandedFrame.Point(tile.Positions[vertex]);
                    _normals[_vertexCount] = RightHandedFrame.Normal(tile.Normals[vertex]);
                    _uvs[_vertexCount] = tile.Uvs[vertex];
                    _vertexCount++;
                }
                _indices[IndexCount++] = remap[vertex];
            }
        }

        /// <summary>The surface as Godot's exact-length arrays; the index order is kept (<see cref="RightHandedFrame"/>).</summary>
        public Godot.Collections.Array ToArrays()
        {
            var positions = new Vector3[_vertexCount];
            var normals = new Vector3[_vertexCount];
            var uvs = new Vector2[_vertexCount];
            for (int i = 0; i < _vertexCount; i++)
            {
                positions[i] = ToGodot(_positions[i]);
                normals[i] = ToGodot(_normals[i]);
                uvs[i] = new Vector2(_uvs[i].X, _uvs[i].Y);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = _indices.AsSpan(0, IndexCount).ToArray();
            return arrays;
        }
    }
}
