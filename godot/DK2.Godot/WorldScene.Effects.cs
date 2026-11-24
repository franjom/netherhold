// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>The effects (Unity's <c>ClassicConstructionEffects</c>, plan step 3k) over the shared CPU particle simulation
/// (<see cref="EffectDrawList"/>): a sprite particle is an instance of its sheet's batch among the blended items
/// (<see cref="AddItem"/>), placed by its own distance; a mesh particle (an element's model, a KMF group's part, a type-6
/// model's frame) is a pooled model drawn with the element's tint added to its light, which Godot sorts by its box as an
/// object. Requests come from the simulation's events, the Heart's beat (its mana effect), a gem carrier's sparkle, the
/// loose level gems' idle effects and the Hand's local pickup dust, as Unity's runner wires them.</summary>
public partial class WorldScene : ISimulationEventSink
{
    private EffectDrawList? _effects;
    private IEffectSpriteProvider? _effectSprites;
    private ISimulationEventSink? _effectDownstream, _effectSink;
    private GameSimulation? _effectSimulation;
    // The frame's requests in Unity's runner order: the loose gems' idle effects, the scene's, then the advance.
    private EffectFrame? _effectFrame;
    private PlayerId _effectViewer;
    private readonly Dictionary<AssetRef, EffectSpriteBatch?> _effectBatches = new();
    private readonly EffectQuadBuffer _effectQuad = new();
    private readonly Dictionary<string, IReadOnlyList<EffectGroupPart>?> _groupParts = new();
    // A mesh particle's models by asset and group part entry, each with its own pool of drawn models.
    private readonly Dictionary<(AssetRef Key, string Entry), EffectMesh?> _effectMeshes = new();
    private readonly List<EffectMesh> _effectMeshList = new();
    private static readonly Shader?[] s_spriteShaders = new Shader?[3];
    private Rid _effectQuadMesh;

    /// <summary>Observe the real shot/effect requests before the first turn; camera entry itself emits nothing.</summary>
    public void OpenEffects(GameSimulation simulation, WorldView view, PlayerId viewer, ulong seed)
    {
        _effectSimulation = simulation;
        _effectDownstream = simulation.Events;
        _effectViewer = viewer;
        WorldState world = simulation.World;
        GameDatabase database = simulation.Database;
        _effects = new EffectDrawList(database, seed,
            clock: () => new EffectTime(_frameMilliseconds),
            visible: request => WorldEffectVisibility.Construction(world, view, request),
            effectVisible: request => WorldEffectVisibility.Effect(world, view, simulation.Navigation, viewer, request),
            floorHeight: point => view.GroundHeightAt(world, point),
            captureEdges: request => CaptureEffectLayout.Boundary(world, database, request),
            groupParts: GroupParts,
            attachment: (id, _) => EffectAttachment.AtPreviousBody(world.CreatureById(id),
                world.CreatureById(id) is CreatureState creature ? view.CreatureEffectGround(world, creature) : default),
            shotAttachment: id => ShotEffectAttachment.Resolve(world, simulation.Navigation, id, _lightFraction),
            cameraVisible: EffectInPlacedCamera,
            terrainFlags: tile => world.Contains(tile) ? database.Terrain[world.Tile(tile).TerrainId].Flags : 0,
            solidTile: tile => !world.Contains(tile) || NavBytes.IsSolid(simulation.Navigation.NavByte(tile.X, tile.Y)),
            shotVisible: shot => WorldEffectVisibility.Shot(world, view, shot),
            creatureGroundHeight: id => world.CreatureById(id.Value) is CreatureState creature
                ? view.CreatureEffectGround(world, creature) : null,
            viewer: viewer);
        // Unity's runner: the Hand's local pickup shows its dust at once, and its authoritative echo is then dropped.
        RoomConstructionParticles model = _effects.Model;
        OpenLightning(model, view, unchecked((uint)seed));
        view.LocalPickups.DustRequested += request => model.RequestPickupDust(request);
        _effectSink = new LocalPickupEffectSink(view.LocalPickups, model);
        _effectFrame = new EffectFrame(_effects, new LevelGemIdleEffects(database));
        simulation.ObserveWith(new VisibilityEventSink(view, world, viewer, this));
    }

    /// <summary>The level's sound (plan step 4): the Heart's beat and the creatures' steps the frame lets through, and the
    /// effects the model creates (a Casino party's pop), as Unity's runner wires its scene's and model's callbacks. Set after
    /// <see cref="OpenEffects"/>.</summary>
    public LevelSoundDriver? Sound
    {
        get => _sound;
        set
        {
            _sound = value;
            if (_effects != null) _effects.Model.EffectCreated = value == null ? null : value.EffectCreated;
        }
    }
    private LevelSoundDriver? _sound;

    /// <summary>DungeonRunner.IsPointInCamera: a one-tile sphere at <paramref name="point"/> (the view's space) against the
    /// placed camera's frustum; everything counts before the camera is placed.</summary>
    private bool PointInPlacedCamera(System.Numerics.Vector3 point)
    {
        if (View == null) return true;
        ViewFrustum frustum = PlacedFrustum;
        for (int i = 0; i < 6; i++)
            if (System.Numerics.Plane.DotCoordinate(frustum[i], point) < -1f) return false;
        return true;
    }

    /// <summary>Unity's runner after the turns before the first frame: the effect model runs one update.</summary>
    public void StartEffects() => _effects?.Model.UpdateNow();

    public void Emit(in SimEvent simEvent)
    {
        _effectDownstream?.Emit(simEvent);
        _effectSink?.Emit(simEvent);
    }

    /// <summary>The members of a KMF group an effect flings (art type 8), with their offsets (<see cref="EffectGroupParts.Of"/>),
    /// read once; null when the install has no such group (Unity's <c>ClassicMeshes.GroupParts</c>).</summary>
    private IReadOnlyList<EffectGroupPart>? GroupParts(string groupName)
    {
        if (_groupParts.TryGetValue(groupName, out IReadOnlyList<EffectGroupPart>? parts)) return parts;
        parts = _art != null && _art.TryLoadGroup(groupName, out KmfGroup group) ? EffectGroupParts.Of(group) : null;
        _groupParts.Add(groupName, parts);
        return parts;
    }

    private bool EffectInPlacedCamera(ViewEffectRequest request)
    {
        if (View == null) return true;
        var point = RenderWorld.Ground(request.Position.X.Raw / 4096f,
            request.Position.Y.Raw / 4096f, request.Position.Z.Raw / 4096f);
        ViewFrustum frustum = PlacedFrustum;
        // The same one-tile sphere as DungeonRunner.IsPointInCamera.
        for (int i = 0; i < 6; i++)
            if (System.Numerics.Plane.DotCoordinate(frustum[i], point) < -1f)
            {
                if (PreviewPose is not ClassicCameraPose preview) return false;
                ViewFrustum second = ViewFrustum.FromPose(preview, PreviewViewport.Width / (float)PreviewViewport.Height, .05f);
                for (int j = 0; j < 6; j++)
                    if (System.Numerics.Plane.DotCoordinate(second[j], point) < -1f) return false;
                return true;
            }
        return true;
    }

    /// <summary>A Heart's beat let through: its mana effect (its sound is <see cref="Sound"/>'s).</summary>
    private void RequestHeartEffect(in ViewEffectRequest request) => _effectFrame?.RequestFromScene(request);

    /// <summary>Step 1 of the frame's effect requests (<see cref="EffectFrame"/>), after the present and before the scene
    /// frame: the loose gems' idle effects for the turns crossed.</summary>
    private void PresentEffects()
    {
        if (_effectSimulation != null) _effectFrame?.AfterPresent(_effectSimulation.World);
    }

    /// <summary>Step 3 of the frame's effect requests (<see cref="EffectFrame"/>): the shots' bodies, then the updates due,
    /// whose lights the frame's lighting takes.</summary>
    private void AdvanceEffects()
    {
        if (_effectFrame == null || _effectSimulation == null) return;
        _worldLights?.UpdateEffects(_effectFrame.Advance(_effectSimulation.World.Shots, _lightFraction), _torchFlags.IsCheapTorch());
    }

    /// <summary>The frame's particles: each sprite an item of its sheet's batch at its quad's centre, each mesh a pooled
    /// model; a particle on a tile the possession view hides is not drawn.</summary>
    private void DrawEffects()
    {
        if (!_previewPass) foreach (EffectMesh mesh in _effectMeshList) mesh.Used = 0;
        if (_effects != null && _art != null && _camera != null)
        {
            _effectSprites ??= new InstallEffectSpriteProvider(_effectSimulation!.Database, _art.LoadTexture);
            _effects.TileLights ??= _worldLights!.TryGetVisibleTable;
            // The draw list is in RenderWorld's left-handed frame; reflect only at the upload boundary.
            System.Numerics.Vector3 right = RightHandedFrame.Point(new(_camera.GlobalBasis.X.X, _camera.GlobalBasis.X.Y, _camera.GlobalBasis.X.Z));
            System.Numerics.Vector3 up = RightHandedFrame.Point(new(_camera.GlobalBasis.Y.X, _camera.GlobalBasis.Y.Y, _camera.GlobalBasis.Y.Z));
            if (_previewPass && PreviewPose is ClassicCameraPose preview)
            {
                right = NVector3.Normalize(NVector3.Cross(preview.Up, preview.Forward));
                up = NVector3.Cross(preview.Forward, right);
            }
            float fraction = _effects.Fraction;
            EffectParticleStream particles = _effects.Particles;
            for (int i = 0; i < particles.Count; i++)
            {
                ConstructionParticle particle = particles[i];
                if (!PossessionTileVisible(new TilePos(particle.XRaw >> 12, particle.YRaw >> 12))) continue;
                AssetRef key = _effects.GroupKey(particle);
                EffectGroupKind kind = EffectDrawList.Classify(particle.Definition, particle.MeshEntry);
                if (kind != EffectGroupKind.Sprite)
                {
                    if (!_previewPass) DrawEffectMesh(particle, key, kind, fraction);
                    continue;
                }
                if (!_effectBatches.TryGetValue(key, out EffectSpriteBatch? batch))
                {
                    long loading = GC.GetAllocatedBytesForCurrentThread();
                    batch = EffectSpriteBatch.Create(this, _effectSprites, key, particle.Definition);
                    _effectBatches.Add(key, batch);
                    _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - loading;
                }
                if (batch == null) continue;
                _effectQuad.Clear();
                if (batch.Shape.IsFlat) _effects.AddFlatQuad(_effectQuad, particle, fraction, batch.Frames);
                else
                {
                    var at = EffectDrawList.Position(particle, fraction);
                    EffectBillboard billboard = _effects.Billboard(particle, at, batch.Shape, right, up);
                    EffectDrawList.AddBillboardQuad(_effectQuad, particle, billboard, right, up, batch.Frames);
                }
                batch.Add(this, _effectQuad);
            }
        }
        // Only the models shown last frame and not this one are hidden.
        if (!_previewPass) foreach (EffectMesh mesh in _effectMeshList)
        {
            for (int i = mesh.Used; i < mesh.Shown; i++) mesh.Slots[i].Node.Visible = false;
            mesh.Shown = mesh.Used;
        }
    }

    /// <summary>Unity's mesh branch: the model (a type-6 one at the particle's frame) at the particle's drawn point, turn and
    /// scale, its surfaces lit by the element's tint more (<see cref="ObjectMaterialLook.InstanceTinted"/>; an additive one is
    /// at full white and takes none).</summary>
    private void DrawEffectMesh(ConstructionParticle particle, AssetRef key, EffectGroupKind kind, float fraction)
    {
        string entry = particle.MeshEntry ?? "";
        long loading = GC.GetAllocatedBytesForCurrentThread();
        if (!_effectMeshes.TryGetValue((key, entry), out EffectMesh? mesh))
        {
            mesh = EffectMesh.Load(this, particle, key, kind);
            _effectMeshes.Add((key, entry), mesh);
            if (mesh != null) _effectMeshList.Add(mesh);
        }
        if (mesh == null) return;
        ModelMesh? shown = mesh.Frame(this, mesh.Frames == null ? 0
            : EffectDrawList.MeshFrame(particle, mesh.Frames.Length, mesh.FramesPerSecond));
        if (shown == null) return;
        // A mesh keeps its own models, so a model changes its looks only with a type-6 model's frame.
        if (mesh.Used == mesh.Slots.Count)
        {
            var made = new ModelSlot(this, shown, "Effect" + mesh.Slots.Count, SlotKind.Effect);
            made.ApplyLooks();
            mesh.Slots.Add(made);
        }
        // Loading a model, building a frame or growing the pool is charged as growth (the exit report's split).
        _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - loading;
        ModelSlot slot = mesh.Slots[mesh.Used++];
        slot.Show(shown, shown.Mesh);
        EffectColour tint = EffectDrawList.Tint(particle);
        slot.SetEffectTint(ObjectMaterialLook.InstanceTintLight(tint.Red, tint.Green, tint.Blue));
        float size = EffectDrawList.Size(particle);
        slot.Place(EffectDrawList.Position(particle, fraction), MeshOrientation.Particle(particle, fraction), new Vector3(size, size, size));
        if (mesh.Used > mesh.Shown) slot.Node.Visible = true;
    }

    // SceneFrame's step 8: a gem carrier's sparkle (0x0043A8E0), owned by the viewer as Unity's runner takes it.
    bool ISceneFrameHost.TakesGemSparkles => _effects != null;

    void ISceneFrameHost.RequestGemSparkle(FpPosition3 point) =>
        _effectFrame?.RequestFromScene(new ViewEffectRequest(new EffectId(CreatureView.GemDustSparkle.EffectId), _effectViewer, point));

    private void FreeEffects()
    {
        GD.Print("DK2.Godot effects: ", _effectBatches.Count, " sprite sheets, ", _effectMeshes.Count, " meshes, ",
            _effectFrames.Count, " model frames built, ", PooledEffectModels(), " pooled models");
        // The draws first: they hold the sheets' materials and the quad.
        FreeItems();
        FreeLightning();
        FreeFlowers();
        foreach (EffectSpriteBatch? batch in _effectBatches.Values) batch?.Free();
        _effectBatches.Clear();
        foreach (EffectMesh mesh in _effectMeshList)
            foreach (ModelSlot slot in mesh.Slots) slot.Free();
        _effectMeshList.Clear();
        _effectMeshes.Clear();
        _effectFrames.Clear();
        _groupParts.Clear();
        if (_effectQuadMesh.IsValid) RenderingServer.FreeRid(_effectQuadMesh);
        _effectQuadMesh = default;
    }

    private int PooledEffectModels()
    {
        int count = 0;
        foreach (EffectMesh mesh in _effectMeshList) count += mesh.Slots.Count;
        return count;
    }

    /// <summary>A mesh particle's models: the element's model or group part, or each frame of a type-6 one, built on first
    /// use, with its authored rate (Unity's <c>MeshGroup</c>), and the drawn models this frame takes in turn.</summary>
    private sealed class EffectMesh
    {
        public readonly List<ModelSlot> Slots = new();
        public int Used, Shown;
        private readonly KmfModel? _animated;
        private readonly string _entry = "";
        private readonly ModelMesh? _still;
        public ModelMesh?[]? Frames { get; }
        public int FramesPerSecond { get; }

        private EffectMesh(ModelMesh still, int framesPerSecond) { _still = still; FramesPerSecond = framesPerSecond; }

        private EffectMesh(KmfModel animated, string entry, int frameCount, int framesPerSecond)
        {
            _animated = animated;
            _entry = entry;
            Frames = new ModelMesh?[frameCount];
            FramesPerSecond = framesPerSecond;
        }

        public static EffectMesh? Load(WorldScene scene, ConstructionParticle particle, AssetRef key, EffectGroupKind kind)
        {
            InstallArtProvider? art = scene._art;
            if (art == null) return null;
            int rate = EffectDrawList.AuthoredFramesPerSecond(particle.Definition);
            if (kind == EffectGroupKind.AnimatedMesh)
            {
                if (!art.TryResolve(key, out ResolvedAsset asset) || !art.TryLoadModel(asset, out KmfModel model)
                    || model.Animation is not { IsComplete: true } animation || animation.Frames.Count == 0) return null;
                return new EffectMesh(model, asset.InstallEntry ?? "", animation.Frames.Count, rate);
            }
            ResolvedAsset still;
            bool found = particle.MeshEntry != null ? art.TryResolveEntry(particle.MeshEntry, out still) : art.TryResolve(key, out still);
            return found && scene.Model(still, lastFrame: false) is ModelMesh mesh ? new EffectMesh(mesh, rate) : null;
        }

        /// <summary>The model at <paramref name="frame"/>, built the first time it is drawn.</summary>
        public ModelMesh? Frame(WorldScene scene, int frame)
        {
            if (Frames == null) return _still;
            frame = Math.Clamp(frame, 0, Frames.Length - 1);
            return Frames[frame] ??= scene.EffectFrame(_animated!, _entry, frame);
        }
    }

    private readonly Dictionary<(string Entry, int Frame), ModelMesh> _effectFrames = new();

    /// <summary>A type-6 model's frame as a model (Unity's <c>ClassicMeshes.TryGetMeshFrame</c>), shared by entry.</summary>
    private ModelMesh EffectFrame(KmfModel model, string entry, int frame)
    {
        if (!_effectFrames.TryGetValue((entry, frame), out ModelMesh? built))
            _effectFrames.Add((entry, frame), built = new ModelMesh(model, RenderMesh.FromModel(model, frame)));
        return built;
    }

    /// <summary>One sprite sheet's batch (Unity's sprite group): its frames side by side in one texture, its shape and its
    /// blend (<see cref="EffectDrawList.Blend"/>: ONE, ONE; SRCALPHA, INVSRCALPHA; or ONE, ZERO), drawn as blended items.</summary>
    private sealed class EffectSpriteBatch
    {
        private readonly ShaderMaterial _material;
        private readonly ImageTexture _texture;
        private readonly ItemBatch _items;
        public EffectSpriteShape Shape { get; }
        public int Frames { get; }

        private EffectSpriteBatch(WorldScene scene, ImageTexture texture, EffectSpriteResource sprite,
            EffectElementDef definition, int frames)
        {
            _texture = texture;
            Shape = EffectSpriteShape.Of(definition, sprite);
            Frames = frames;
            int blend = sprite.Blend == EffectSpriteBlend.Additive ? 0 : sprite.Blend == EffectSpriteBlend.Alpha ? 1 : 2;
            s_spriteShaders[blend] ??= GD.Load<Shader>(blend == 0 ? "res://Shaders/EffectSpriteAdd.gdshader"
                : blend == 1 ? "res://Shaders/EffectSpriteMix.gdshader" : "res://Shaders/EffectSpriteOpaque.gdshader");
            _material = new ShaderMaterial { Shader = s_spriteShaders[blend] };
            _material.SetShaderParameter("frame_texture", texture);
            _items = scene.CreateItemBatch(scene.EffectQuadMesh(), _material.GetRid(),
                blend == 0 ? BlendedItemBlend.Additive : BlendedItemBlend.Ordered);
        }

        public static EffectSpriteBatch? Create(WorldScene scene, IEffectSpriteProvider provider, AssetRef key, EffectElementDef definition)
        {
            if (!provider.TryResolve(key, out ResolvedAsset asset) || !provider.TryGetSprite(asset, out EffectSpriteResource sprite)
                || !provider.TryLoadFrame(asset, 0, out var first)) return null;
            int frames = sprite.StoredFrameCount, width = first.Width, height = first.Height;
            byte[] atlas = new byte[width * frames * height * 4];
            for (int frame = 0; frame < frames; frame++)
            {
                if (!provider.TryLoadFrame(asset, frame, out var image) || image.Width != width || image.Height != height)
                    throw new InvalidOperationException("Effect sprite frames must have equal dimensions.");
                for (int row = 0; row < height; row++)
                    Buffer.BlockCopy(image.Pixels, row * width * 4, atlas, (row * width * frames + frame * width) * 4, width * 4);
            }
            using Image pixels = Image.CreateFromData(width * frames, height, false, Image.Format.Rgba8, atlas);
            return new EffectSpriteBatch(scene, ImageTexture.CreateFromImage(pixels), sprite, definition, frames);
        }

        /// <summary>One quad of <see cref="EffectQuadBuffer"/> (corners 0, 1 and 3 span it) as an instance at its centre.</summary>
        public void Add(WorldScene scene, EffectQuadBuffer quad)
        {
            var vertices = quad.Vertices;
            Vector3 origin = ToGodot(RightHandedFrame.Point(vertices[0]));
            Vector3 x = ToGodot(RightHandedFrame.Point(vertices[1])) - origin;
            Vector3 y = ToGodot(RightHandedFrame.Point(vertices[3])) - origin;
            if (x.LengthSquared() <= 0 || y.LengthSquared() <= 0) return;
            EffectColour tint = quad.Colours[0];
            scene.AddItem(_items, origin + (x + y) * 0.5f, 0.5f * Mathf.Sqrt(x.LengthSquared() + y.LengthSquared()), new Transform3D(new Basis(x, y, x.Cross(y).Normalized()), origin),
                new Color(tint.Red / 255f, tint.Green / 255f, tint.Blue / 255f),
                new Color(quad.Uvs[0].X, quad.Uvs[1].X, quad.Uvs[0].Y, quad.Uvs[3].Y));
        }

        public void Free()
        {
            _material.Dispose();
            _texture.Dispose();
        }
    }

    /// <summary>The unit quad every effect sprite instance stretches, its corner at the origin (UV v up).</summary>
    private Rid EffectQuadMesh()
    {
        if (_effectQuadMesh.IsValid) return _effectQuadMesh;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[] { Vector3.Zero, Vector3.Right, Vector3.Right + Vector3.Up, Vector3.Up };
        arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        _effectQuadMesh = RenderingServer.MeshCreate();
        RenderingServer.MeshAddSurfaceFromArrays(_effectQuadMesh, RenderingServer.PrimitiveType.Triangles, arrays);
        return _effectQuadMesh;
    }
}
