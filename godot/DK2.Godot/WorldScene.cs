// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The 3D world under <see cref="InGameHud"/> (plan step 3a): the terrain as <see cref="TerrainTileBuilder"/>
/// builds it, one <see cref="MeshInstance3D"/> per 16x16-tile chunk with a surface per <see cref="TerrainMaterialKey"/>,
/// unshaded, seen from the level-start camera. Everything crosses into Godot's right-handed frame through
/// <see cref="RightHandedFrame"/>. Not yet drawn: creatures, objects, structures and torches (no-ops here), the sight fog
/// and tag overlay variants, liquids and lighting; nothing is rebuilt after the first push (step 3c). The host moves the
/// camera's <see cref="Focus"/> through the shared rules and places it once a frame (<see cref="PlaceCamera"/>, step 3b).</summary>
public partial class WorldScene : Node3D, IRenderTarget
{
    private const int ChunkSize = 16;
    /// <summary>Unity's camera near plane (Dungeon.unity), which the frustum is sized at.</summary>
    private const float NearPlane = 0.3f;

    private static readonly StringName AlbedoParameter = "albedo_texture";

    private InstallArtProvider? _art;
    private ScenePresenter? _presenter;
    private TerrainTileBuilder? _builder;
    private readonly TerrainTileGeometry _geometry = new();
    private TileView[] _tiles = [];
    private bool[] _drawn = [];
    private bool[] _dirtyChunks = [];
    private MeshInstance3D?[] _chunks = [];
    private int _width, _height, _chunksX;

    private readonly Dictionary<TerrainMaterialKey, SurfaceBuffer> _surfaceOf = new();
    private readonly List<SurfaceBuffer> _surfaces = new();
    private readonly List<SurfaceBuffer> _spareSurfaces = new();
    private int[] _remap = new int[256], _stamp = new int[256];
    private int _generation;

    private readonly Dictionary<ClassicMaterialRecipe, ShaderMaterial> _materials = new();
    private readonly Dictionary<string, Texture2D> _textures = new();
    private Shader? _opaque, _alphaBlend, _additive;
    private ImageTexture? _white;

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
        _width = world.Width;
        _height = world.Height;
        _chunksX = (_width + ChunkSize - 1) / ChunkSize;
        int chunks = _chunksX * ((_height + ChunkSize - 1) / ChunkSize);
        _tiles = new TileView[_width * _height];
        _drawn = new bool[_width * _height];
        _dirtyChunks = new bool[chunks];
        _chunks = new MeshInstance3D?[chunks];
        _art = InstallArtProvider.Open(install, level.Database);
        // DungeonRunner's defaults: the lattice bends the terrain, with DK2's high walls.
        var lattice = new TerrainLattice(level.Database, world, viewer) { HighWalls = true };
        _builder = new TerrainTileBuilder(new InstallTerrainPieces(_art), _width, _height, lattice);
        _opaque = NewShader("render_mode unshaded, cull_back, depth_draw_opaque;", "ALBEDO = texture(albedo_texture, UV).rgb;");
        _alphaBlend = NewShader("render_mode unshaded, cull_back, blend_mix, depth_draw_never;",
            "vec4 c = texture(albedo_texture, UV); ALBEDO = c.rgb; ALPHA = c.a;");
        // ONE/ONE: blend_add weights the source by its alpha, held at 1.
        _additive = NewShader("render_mode unshaded, cull_back, blend_add, depth_draw_never;",
            "ALBEDO = texture(albedo_texture, UV).rgb; ALPHA = 1.0;");
        using (Image white = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
        {
            white.Fill(Colors.White);
            _white = ImageTexture.CreateFromImage(white);
        }

        // The install, then the placeholder, as Unity's chain without its optional folders.
        _presenter = new ScenePresenter(view, AssetProviderChain.Standard(install: _art), this);
        _presenter.Present(world);

        CameraThing? profile = ClassicCameraSetup.Preset(
            GameDatabaseLoader.LoadCameraPresets(install, level.Name), ClassicCameraMode.Dungeon);
        if (profile == null)
        {
            GD.PushError("DK2.Godot: the level has no dungeon camera preset; no 3D camera.");
            return;
        }
        ClassicCameraSetup setup = ClassicCameraSetup.Create(profile, ClassicCameraSetup.DefaultZoomOutLimitScale,
            GameSessionStarter.CameraTicksPerSecond(level), scrollSpeed, level.Players, viewer, _width, _height);
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
        bool draws = TerrainTileBuilder.Draws(tile);
        if (draws || _drawn[tileIndex]) _dirtyChunks[ChunkOf(tileIndex)] = true;
        _tiles[tileIndex] = tile;
        _drawn[tileIndex] = draws;
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

    /// <summary>Rebuilds every chunk a pushed tile marked.</summary>
    public void Flush()
    {
        for (int chunk = 0; chunk < _dirtyChunks.Length; chunk++)
        {
            if (!_dirtyChunks[chunk]) continue;
            _dirtyChunks[chunk] = false;
            RebuildChunk(chunk);
        }
    }

    private int ChunkOf(int tileIndex) => (tileIndex / _width / ChunkSize * _chunksX) + (tileIndex % _width / ChunkSize);

    /// <summary>Each drawn tile of the chunk built and its triangles gathered per key, each key's vertices copied once
    /// into its own surface.</summary>
    private void RebuildChunk(int chunk)
    {
        foreach (SurfaceBuffer surface in _surfaces) _spareSurfaces.Add(surface);
        _surfaces.Clear();
        _surfaceOf.Clear();
        int x0 = chunk % _chunksX * ChunkSize, y0 = chunk / _chunksX * ChunkSize;
        for (int y = y0; y < Math.Min(y0 + ChunkSize, _height); y++)
            for (int x = x0; x < Math.Min(x0 + ChunkSize, _width); x++)
            {
                if (!_drawn[(y * _width) + x]) continue;
                _builder!.Prepare(x, y, _tiles, _geometry);
                _builder.Build(_geometry);
                GatherTile();
            }

        MeshInstance3D? instance = _chunks[chunk];
        var mesh = instance?.Mesh as ArrayMesh ?? new ArrayMesh();
        mesh.ClearSurfaces();
        foreach (SurfaceBuffer surface in _surfaces)
        {
            ShaderMaterial? material = MaterialFor(surface.Key);
            if (material == null || surface.IndexCount == 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface.ToArrays());
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        if (instance == null && mesh.GetSurfaceCount() > 0)
        {
            instance = new MeshInstance3D { Name = "Chunk" + chunk, Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(instance);
            _chunks[chunk] = instance;
        }
    }

    private void GatherTile()
    {
        if (_remap.Length < _geometry.VertexCount)
        {
            _remap = new int[_geometry.VertexCount * 2];
            _stamp = new int[_geometry.VertexCount * 2];
        }
        for (int s = 0; s < _geometry.KeyCount; s++)
        {
            TerrainMaterialKey key = _geometry.Keys[s];
            if (!_surfaceOf.TryGetValue(key, out SurfaceBuffer? surface))
            {
                surface = TakeSurface(key);
                _surfaceOf.Add(key, surface);
                _surfaces.Add(surface);
            }
            _generation++;
            surface.Append(_geometry, _geometry.Triangles[s], _remap, _stamp, _generation);
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

    /// <summary>The material a key draws with here: the recipe's own. The variants and the tag overlays wait for
    /// step 3c and draw nothing yet.</summary>
    private ShaderMaterial? MaterialFor(in TerrainMaterialKey key)
    {
        if (key.Variant is TerrainMaterialVariant.Fog or TerrainMaterialVariant.TagOverlay or TerrainMaterialVariant.RedTagOverlay)
            return null;
        ClassicMaterialRecipe recipe = key.Recipe;
        if (_materials.TryGetValue(recipe, out ShaderMaterial? material)) return material;
        material = new ShaderMaterial
        {
            Shader = recipe.Blend switch
            {
                KmfBlend.Opaque => _opaque,
                KmfBlend.AlphaBlend => _alphaBlend,
                _ => _additive,
            },
        };
        material.SetShaderParameter(AlbedoParameter, TextureFor(recipe.Texture ?? ""));
        _materials.Add(recipe, material);
        return material;
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

    /// <summary>Clamped, as Unity's terrain textures are: no shipped KMF coordinate leaves 0..1, and repeating let
    /// filtering pull the opposite edge into seams (ClassicMeshes.ToTexture).</summary>
    private static Shader NewShader(string renderMode, string fragment) => new()
    {
        Code = "shader_type spatial;\n" + renderMode + "\n"
            + "uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_disable;\n"
            + "void fragment() { " + fragment + " }\n",
    };

    private static Vector3 ToGodot(NVector3 v) => new(v.X, v.Y, v.Z);

    public override void _ExitTree()
    {
        if (_camera != null) GetViewport().SizeChanged -= PlaceCamera;
        _art?.Dispose();
        _art = null;
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
