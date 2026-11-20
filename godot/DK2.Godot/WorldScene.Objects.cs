// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Objects, structures and wall torches (plan step 3f), as Unity's <c>DungeonScene</c> draws them: each install
/// model built once into a shared <see cref="ArrayMesh"/> (one surface per KMF material, <see cref="ModelMesh"/>), a
/// <see cref="MeshInstance3D"/> per thing with its surfaces' <see cref="ObjectMaterialLook"/>s, a type-6 clip played by the
/// shared <see cref="CreatureAnimationPlayer{TResources}"/> (<see cref="AnimatedModel"/>), the Heart's beat through
/// <see cref="HeartPulse"/>, torches bent by the lattice and their flames (<see cref="TorchFlames"/>). An environment pass
/// and the hover's red are folded into the submesh's world material, and an object's tint and fade and a structure's
/// tint are the instance's uniforms (step 3p, <see cref="ObjectSurfaceKey"/>).</summary>
public partial class WorldScene
{
    private readonly Dictionary<int, ModelSlot> _objects = new();
    private readonly Dictionary<int, AssetRef> _artless = new();
    private readonly Dictionary<StructureId, ModelSlot> _structures = new();
    private readonly Dictionary<StructureId, StructureView> _structureViews = new();
    private readonly Dictionary<int, TorchSlot> _torches = new();
    // The torch holders with a transparent surface: barriers among the blended items.
    private readonly List<TorchSlot> _transparentTorches = new();
    private readonly Dictionary<(string Entry, bool LastFrame), ModelMesh?> _models = new();
    private readonly Dictionary<string, RenderAnimation> _clips = new();
    private readonly Dictionary<string, ClipSet?> _clipSets = new();
    private readonly Dictionary<ObjectSurfaceKey, ShaderMaterial> _objectMaterials = new();
    // Per submesh, the environment pass a model's ApplyLooks found; reused.
    private ObjectMaterialLook?[] _environmentOf = new ObjectMaterialLook?[8];
    private readonly List<ObjectPass> _passes = new();
    private readonly HeartPulse _heartPulse = new();
    private LatticeDirtyTracker? _latticeDirty;
    private TorchFlames? _flames;
    private Func<EffectTime>? _effectClock;
    private byte[] _upload = [];
    private long _frameMilliseconds;
    // What the frames' object draws allocated, for the exit report: the first frames (clip installs, first materials), then after.
    private const int DrawWarmUp = 60;
    private long _drawWarmUpBytes, _drawLateBytes, _drawBendingBytes;
    private int _drawFrames, _drawBendingFrames;
    private readonly long[] _phaseBytes = new long[(int)DrawPhase.Count], _steadyPhaseBytes = new long[(int)DrawPhase.Count];
    private long _phaseStart;
    private bool _frameBending;

    /// <summary>A Heart's health change from the world's event (Unity's <c>QueueHeartHealthChange</c>); the next
    /// <see cref="DrawFrame"/> advances each Heart of the owner by its phase step.</summary>
    public void QueueHeartHealthChange(PlayerId owner, DungeonHeartVisual visual) => _heartPulse.QueueHealthChange(owner, visual);

    /// <summary>The draw clock's turn for the clips the coming present starts (Unity's <c>SetCreatureFrameTime</c>, called
    /// before its present).</summary>
    public void SetFrameTime(GameTick tick, float fraction, long milliseconds)
    {
        _frame.SetFrameTime(tick, fraction);
        _frameMilliseconds = milliseconds;
    }

    private void OpenObjects(InstallArtProvider art, GameDatabase database, int width, int height)
    {
        _latticeDirty = new LatticeDirtyTracker(width, height);
        _effectClock = () => new EffectTime(_frameMilliseconds);
        _flames = new TorchFlames(this, new InstallEffectSpriteProvider(database, art.LoadTexture));
    }

    public void CreateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
    {
        // With the install, an object it has no art for draws nothing, as DK2 draws nothing for an art not loaded
        // (0x0059B880): the Guard Post, the Heart's Obstacle.
        if (Model(mesh, lastFrame: false) is not ModelMesh model)
        {
            _artless[objectId] = mesh.Reference;
            return;
        }
        _artless.Remove(objectId);
        var slot = new ModelSlot(this, model, "Object" + objectId, SlotKind.Object);
        _objects[objectId] = slot;
        slot.Pose = instance;
        PlaceObject(slot, instance, 1f);
        ConfigureObjectClip(slot, instance, mesh);
        slot.ApplyLooks();
    }

    public void UpdateObject(int objectId, in ObjectView instance, in ResolvedAsset mesh)
    {
        if (!_objects.TryGetValue(objectId, out ModelSlot? slot))
        {
            if (!(_artless.TryGetValue(objectId, out AssetRef asked) && asked == mesh.Reference)) CreateObject(objectId, instance, mesh);
            return;
        }
        slot.Pose = instance;
        PlaceObject(slot, instance, 1f);
        ConfigureObjectClip(slot, instance, mesh);
        slot.ApplyLooks();
    }

    public void RemoveObject(int objectId)
    {
        if (_objects.Remove(objectId, out ModelSlot? slot)) slot.Free();
        _artless.Remove(objectId);
    }

    /// <summary>Unity's <c>ConfigureObjectClip</c>: a still object shows its model again; a playing one starts its clip once
    /// per <see cref="AnimationClipRequest.Identity(in ObjectView, AssetRef)"/>, else falls back to the model.</summary>
    private void ConfigureObjectClip(ModelSlot slot, in ObjectView pose, in ResolvedAsset mesh)
    {
        if (!pose.PlaysAnimation)
        {
            slot.HasClip = false;
            slot.StopAnimation();
            if (Model(mesh, lastFrame: false) is ModelMesh model) slot.ShowStatic(model);
            return;
        }
        ObjectClipIdentity identity = AnimationClipRequest.Identity(pose, mesh.Reference);
        if (slot.HasClip && slot.Clip == identity) return;
        slot.HasClip = true;
        slot.Clip = identity;
        double start = AnimationClipRequest.StartSeconds(pose.AnimationStartedAt, _frame.DrawSeconds);
        if (TryStartClip(slot, mesh, pose, start)) return;
        slot.StopAnimation();
        if (Model(mesh, lastFrame: false) is ModelMesh fallback) slot.ShowStatic(fallback);
    }

    /// <summary>Unity's <c>ClassicMeshes.ConfigureAnimation</c> for an object: the clip's segments (<see cref="ClipSetFor"/>),
    /// played on the draw clock with a Heart's phase step.</summary>
    private bool TryStartClip(ModelSlot slot, in ResolvedAsset asset, in ObjectView pose, double start)
    {
        if (ClipSetFor(asset) is not ClipSet set) return false;
        slot.Animation ??= new AnimatedModel(slot);
        slot.Animation.Player.Configure(set.Clips, set.Templates, set.Timing, pose.AnimationLoops, start,
            phaseStepOverride: pose.HeartVisual?.PhaseStep, initialPhase: pose.AnimationInitialPhase);
        return true;
    }

    /// <summary>Unity's <c>ClassicMeshes.ConfigureAnimation</c> up to the player: a type-6 clip's segments (start, main,
    /// end by the resource's flags), each with its template model, built once per install entry and shared by every
    /// player, so a clip started again allocates no arrays; null when the asset has no complete clip.</summary>
    private ClipSet? ClipSetFor(in ResolvedAsset asset)
    {
        if (_art == null || Model(asset, lastFrame: false) is null) return null;
        string entry = asset.InstallEntry ?? "";
        if (_clipSets.TryGetValue(entry, out ClipSet? set)) return set;
        set = null;
        if (_art.TryGetResource(asset, out Dk2ArtResource resource) && resource.Type == 6
            && _art.TryLoadModel(asset, out KmfModel model) && model.Animation is { IsComplete: true })
        {
            var clips = new RenderAnimation?[3];
            var templates = new ModelMesh?[3];
            bool complete = true;
            for (AnimationSegment segment = AnimationSegment.Start; segment <= AnimationSegment.End && complete; segment++)
            {
                if (!CreatureAnimationArt.HasSegment(resource, segment)) continue;
                if (!_art.TryResolveSegment(asset, segment, out ResolvedAsset part) || !_art.TryLoadModel(part, out KmfModel partModel)
                    || partModel.Animation is not { IsComplete: true } || Model(part, lastFrame: false) is not ModelMesh template)
                {
                    complete = false;
                    continue;
                }
                if (!_clips.TryGetValue(part.InstallEntry ?? "", out RenderAnimation? clip))
                    _clips.Add(part.InstallEntry ?? "", clip = new RenderAnimation(partModel));
                clips[(int)segment] = clip;
                templates[(int)segment] = template;
            }
            if (complete) set = new ClipSet(clips, templates, CreatureAnimationArt.Timing(resource), resource);
        }
        _clipSets.Add(entry, set);
        return set;
    }

    /// <summary>A clip's segments and templates by <see cref="AnimationSegment"/>, its timing and its resource.</summary>
    private sealed record ClipSet(RenderAnimation?[] Clips, ModelMesh?[] Templates, CreatureAnimationTiming Timing,
        Dk2ArtResource Resource);

    /// <summary>Unity's <c>PositionObject</c> and its per-frame pose: the object's point and height between its last two
    /// turns (<see cref="PoseInterpolator"/>), its turns (<see cref="MeshOrientation"/>) and its scale, a Heart's taller.</summary>
    private void PlaceObject(ModelSlot slot, in ObjectView pose, float fraction)
    {
        DrawPoint drawn = PoseInterpolator.Position(pose, fraction);
        slot.Place(new NVector3(drawn.X, drawn.Height, -drawn.Y), PoseInterpolator.ObjectTurn(pose, fraction),
            new Vector3(pose.MeshScale, pose.MeshScaleY, pose.MeshScale));
    }

    public void CreateStructure(StructureId id, in StructureView structure, in ResolvedAsset mesh)
    {
        if (Model(mesh, structure.LastFrame) is not ModelMesh model) return;
        var slot = new ModelSlot(this, model, structure.Id.Kind.ToString() + structure.Id.Value, SlotKind.Structure) { Structure = structure };
        _structures[id] = slot;
        _structureViews[id] = structure;
        slot.Place(new NVector3(structure.PosX / (float)Fp32.One, structure.Height / (float)Fp32.One, -structure.PosY / (float)Fp32.One),
            new RenderEuler(MeshOrientation.Heading(structure.MeshYaw), 0f, 0f), Vector3.One);
        slot.ApplyLooks();
        // The door's lock, at the door's own place and turn, with its plain materials.
        if (!structure.LockMesh.IsEmpty && _art != null && _art.TryResolve(structure.LockMesh, out ResolvedAsset lockAsset)
            && Model(lockAsset, lastFrame: false) is ModelMesh lockModel)
        {
            slot.Lock = new ModelSlot(this, lockModel, "Lock", SlotKind.Plain, slot.Node);
            slot.Lock.ApplyLooks();
        }
    }

    public void UpdateStructure(StructureId id, in StructureView structure, in ResolvedAsset mesh)
    {
        // A blueprint's blink changes only its plan colour each turn: the materials, not the model.
        if (_structureViews.TryGetValue(id, out StructureView last) && structure.IsBlinkOf(last)
            && _structures.TryGetValue(id, out ModelSlot? built))
        {
            built.Structure = structure;
            _structureViews[id] = structure;
            built.ApplyLooks();
            return;
        }
        RemoveStructure(id);
        CreateStructure(id, structure, mesh);
    }

    public void RemoveStructure(StructureId id)
    {
        if (_structures.Remove(id, out ModelSlot? slot)) slot.Free();
        _structureViews.Remove(id);
    }

    public void CreateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh)
    {
        if (!_torches.TryGetValue(floorTileIndex, out TorchSlot? slot))
            _torches.Add(floorTileIndex, slot = new TorchSlot(this, Model(mesh, lastFrame: false), floorTileIndex));
        slot.Set(torch);
        _flames?.Set(torch);
        SetTorchLight(floorTileIndex, torch);
    }

    public void UpdateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh) => CreateTorch(floorTileIndex, torch, mesh);

    public void RemoveTorch(int floorTileIndex)
    {
        if (_torches.Remove(floorTileIndex, out TorchSlot? slot)) slot.Free();
        _flames?.Remove(floorTileIndex);
        SetTorchLight(floorTileIndex, null);
    }

    /// <summary>Unity's <c>DrawCreatureFrame</c> in <see cref="SceneFrame"/>'s order (this scene is its host): the Hearts'
    /// queued health steps, each creature's drawn place, clip and head, each moving object's drawn pose, each clip's frame
    /// and a Heart's beat (its mana effect requested; its sound waits for step 4), the torches near a changed tile bent
    /// again; then the flames advanced. <see cref="FinishFrame"/> sorts once the camera is placed.
    /// Allocation-free.</summary>
    public void DrawFrame(GameTick tick, float fraction)
    {
        _frameBending = _latticeDirty is { AnyTouched: true };
        _lightFraction = fraction;
        _phaseStart = GC.GetAllocatedBytesForCurrentThread();
        PresentEffects();
        BeginLightning();
        _frame.Draw(tick, fraction);
        _drawn = null;
        _flames?.Draw(_frameMilliseconds);
        Mark(DrawPhase.Flames);
    }

    /// <summary>After the frame's camera is placed (the query follow reads the drawn creatures): the frame's lights
    /// (<see cref="PrepareLighting"/>), the blended stream sorted and keyed for it (<see cref="DrawBlended"/>), then the
    /// effects and the flames added as blended items (<see cref="DrawEffects"/>), which <see cref="DrawBlendedItems"/> orders
    /// among the runs once the flowers are in. Blended objects and the cage need no key of their own, as Godot sorts them by
    /// their AABB centres. Allocation-free.</summary>
    public void FinishFrame()
    {
        PreparePossessionVisibility();
        AdvanceEffects();
        PrepareLighting();
        PrepareShadows();
        DrawBlended();
        DrawPreviewTerrain();
        _phaseStart = GC.GetAllocatedBytesForCurrentThread();
        Mark(DrawPhase.Sort);
        BeginItems();
        long started = System.Diagnostics.Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        DrawEffects();
        DrawLightning();
        started = ItemTime(ItemStep.Effects, started);
        _flames?.AddItems();
        if (PreviewPose.HasValue)
        {
            SwapPreviewItems();
            DrawEffects(); _flames?.AddItems();
            SwapPreviewItems();
        }
        ItemTime(ItemStep.Flames, started);
        ItemBytes(allocated);
        Mark(DrawPhase.Effects);
        long bytes = 0;
        for (int i = 0; i < _phaseBytes.Length; i++) bytes += _phaseBytes[i];
        bool steady = _drawFrames++ >= DrawWarmUp && !_frameBending;
        for (int i = 0; i < _phaseBytes.Length; i++)
        {
            if (steady) _steadyPhaseBytes[i] += _phaseBytes[i];
            _phaseBytes[i] = 0;
        }
        if (_drawFrames <= DrawWarmUp) _drawWarmUpBytes += bytes;
        else if (_frameBending) { _drawBendingBytes += bytes; _drawBendingFrames++; }
        else _drawLateBytes += bytes;
    }

    /// <summary>Charges what the frame allocated since the last mark to <paramref name="phase"/> (the exit report's split).</summary>
    private void Mark(DrawPhase phase)
    {
        long now = GC.GetAllocatedBytesForCurrentThread();
        _phaseBytes[(int)phase] += now - _phaseStart;
        _phaseStart = now;
    }

    private enum DrawPhase { Hearts, Creatures, Objects, Bend, Flames, Sort, Effects, Count }

    void ISceneFrameHost.AdvanceHeartHealth()
    {
        while (_heartPulse.TryTakeHealthAdvance(out PlayerId owner, out Fp32 step))
            foreach (KeyValuePair<int, ModelSlot> heart in _objects)
                if (HeartPulse.IsHeartOf(heart.Value.Pose, owner)) heart.Value.Animation?.Player.AdvanceHealthPhase(step);
        Mark(DrawPhase.Hearts);
    }

    void ISceneFrameHost.DrawObjects(GameTick tick, float fraction)
    {
        _drawn = null;
        Mark(DrawPhase.Creatures);
        foreach (KeyValuePair<int, ModelSlot> entry in _objects)
        {
            ModelSlot slot = entry.Value;
            if (!slot.Moving && slot.Animation == null) continue;
            ObjectView pose = slot.Pose;
            if (slot.Moving) PlaceObject(slot, pose, fraction);
            if (slot.Animation is not AnimatedModel animated) continue;
            animated.Player.Draw(_frame.DrawSeconds, phaseStepOverride: pose.HeartVisual?.PhaseStep);
            if (!pose.HeartVisual.HasValue || !animated.Player.BeatPending) continue;
            NVector3 at = slot.RenderPosition;
            bool visible = View != null && !PlacedFrustum.Excludes(at, at, at);
            HeartBeat beat = _heartPulse.TryBeat(pose, beatPending: true, visible, _effectClock!, tick);
            if (!beat.Consumed) continue;
            animated.Player.ConsumeBeat();
            if (beat.Effect is ViewEffectRequest mana) RequestHeartEffect(mana);
        }
        Mark(DrawPhase.Objects);
    }

    void ISceneFrameHost.RefreshTorches()
    {
        if (_latticeDirty is { AnyTouched: true })
        {
            foreach (KeyValuePair<int, TorchSlot> torch in _torches)
                if (_latticeDirty.TouchedNear(torch.Value.Torch.Wall))
                {
                    torch.Value.Set(torch.Value.Torch);
                    _flames?.Set(torch.Value.Torch);
                }
            _latticeDirty.Clear();
        }
        Mark(DrawPhase.Bend);
    }

    /// <summary>The model an install entry draws, built once (Unity's <c>ClassicMeshes.TryBuild</c>): its first frame, or
    /// its last for <paramref name="lastFrame"/> (a door); null for an asset the install did not answer or a group.</summary>
    private ModelMesh? Model(in ResolvedAsset asset, bool lastFrame)
    {
        if (_art == null || asset.Source != AssetSource.InstallEntry || asset.ProviderName != _art.Name) return null;
        string entry = asset.InstallEntry ?? "";
        if (_models.TryGetValue((entry, lastFrame), out ModelMesh? built)) return built;
        built = null;
        if (_art.TryLoadModel(asset, out KmfModel model) && (model.Mesh != null || model.Animation is { IsComplete: true }))
            built = new ModelMesh(model, RenderMesh.FromModel(model,
                lastFrame && model.Animation is { IsComplete: true } animation ? animation.Frames.Count - 1 : 0));
        _models.Add((entry, lastFrame), built);
        return built;
    }

    /// <summary>What a model surface's material is made of (<see cref="MaterialFor(in ObjectSurfaceKey)"/>): its look,
    /// the environment pass folded into it, whether the drawn thing's tint and fade apply (not to a hover twin or a plan),
    /// whether its KMF material blends (which of the hover's reds it takes), and, for an alpha-blended look, whether its
    /// environment pass or red pass draws as its next pass (<see cref="Extras"/>). The tint, fade and red themselves are
    /// the instance's (<see cref="ModelInstanceLook"/>, <see cref="ModelSlot.ApplyLooks"/>), so they make no material.</summary>
    private readonly record struct ObjectSurfaceKey(ObjectMaterialLook Look, ObjectMaterialLook? Environment, bool InstanceLook,
        bool KmfBlended, bool Extras, VertexClip? Animation = null);

    private static readonly StringName ModelVertexParameter = "model_vertex", InstanceLookParameter = "instance_look",
        KmfBlendedParameter = "kmf_blended", EnvironmentPassParameter = "environment_pass",
        EnvironmentTextureParameter = "environment_texture", EnvironmentColourParameter = "environment_colour",
        ExtrasOnlyParameter = "extras_only", TintParameter = "dk2_tint", AlphaParameter = "dk2_alpha",
        EnvironmentDrawnParameter = "dk2_environment", RedParameter = "dk2_red";

    // An alpha-blended surface's additive next pass, one per environment pass and KMF blend (Extras).
    private readonly Dictionary<(ObjectMaterialLook? Environment, bool KmfBlended, VertexClip? Animation), ShaderMaterial> _extrasMaterials = new();

    /// <summary>The world material of a model surface, one per <see cref="ObjectSurfaceKey"/>; the shader is the terrain's
    /// (<see cref="ShaderFor"/>), lit through a model's light inputs.</summary>
    private ShaderMaterial MaterialFor(in ObjectSurfaceKey key)
    {
        if (_objectMaterials.TryGetValue(key, out ShaderMaterial? material)) return material;
        ObjectMaterialLook look = key.Look;
        material = ModelMaterial(look.Texture, look.Blend, look.WritesDepth, look.FullLight, look.Colour, look.AddedLight,
            key.KmfBlended, key.Environment);
        if (key.InstanceLook) material.SetShaderParameter(InstanceLookParameter, true);
        key.Animation?.Bind(material);
        if (key.Extras) material.NextPass = Extras(key.Environment, key.KmfBlended, key.Animation);
        _objectMaterials.Add(key, material);
        return material;
    }

    /// <summary>A world material on a model's light inputs, with its KMF blend and environment pass.</summary>
    private ShaderMaterial ModelMaterial(string texture, KmfBlend blend, bool writesDepth, bool fullLight,
        System.Numerics.Vector4 colour, System.Numerics.Vector3 addedLight, bool kmfBlended, ObjectMaterialLook? environment)
    {
        ShaderMaterial material = WorldMaterial(texture, blend, writesDepth, fullLight, colour, addedLight);
        material.SetShaderParameter(ModelVertexParameter, true);
        if (kmfBlended) material.SetShaderParameter(KmfBlendedParameter, true);
        if (environment is ObjectMaterialLook pass)
        {
            material.SetShaderParameter(EnvironmentPassParameter, true);
            material.SetShaderParameter(EnvironmentTextureParameter, TextureFor(pass.Texture));
            material.SetShaderParameter(EnvironmentColourParameter,
                new Color(pass.Colour.X, pass.Colour.Y, pass.Colour.Z, pass.Colour.W));
        }
        return material;
    }

    /// <summary>An alpha-blended surface's next pass: its environment pass and the hover's red added over the whole
    /// triangle, as DK2 and Unity draw them after the material, rather than weighed by the texture's alpha in its own pass.</summary>
    private ShaderMaterial Extras(ObjectMaterialLook? environment, bool kmfBlended, VertexClip? animation)
    {
        if (_extrasMaterials.TryGetValue((environment, kmfBlended, animation), out ShaderMaterial? material)) return material;
        material = ModelMaterial("", KmfBlend.Additive, false, true, System.Numerics.Vector4.One, default, kmfBlended, environment);
        material.SetShaderParameter(ExtrasOnlyParameter, true);
        animation?.Bind(material);
        _extrasMaterials.Add((environment, kmfBlended, animation), material);
        return material;
    }

    /// <summary>The cleared per-submesh environment passes for a model of <paramref name="submeshes"/> submeshes.</summary>
    private ObjectMaterialLook?[] EnvironmentPasses(int submeshes)
    {
        if (_environmentOf.Length < submeshes) _environmentOf = new ObjectMaterialLook?[submeshes * 2];
        Array.Clear(_environmentOf, 0, submeshes);
        return _environmentOf;
    }

    /// <summary>What the hover overlay asks of one model this frame: the gold texture swap, and the red pass over its
    /// materials made from opaque and from blended KMF ones (<see cref="HoverOverlayPlan.Submesh"/>).</summary>
    private readonly record struct HoverLooks(bool Swap, bool RedOpaque, bool RedBlended)
    {
        public bool Any => Swap || RedOpaque || RedBlended;

        /// <summary>This and <paramref name="other"/>: a creature red twice in a frame draws red once
        /// (<see cref="HoverOverlayPlan"/>), so the two never ask for the same material.</summary>
        public HoverLooks With(HoverLooks other) =>
            new(Swap || other.Swap, RedOpaque || other.RedOpaque, RedBlended || other.RedBlended);
    }

    /// <summary>Rewrites bent torch positions through one reused buffer, by source vertex in the renderers' frame.
    /// Dynamic surfaces store positions before normals (rendering_server.cpp at c51ff06e8).
    /// <paramref name="box"/> encloses the surface vertices in the renderers' frame.</summary>
    private void UploadPositions(ArrayMesh mesh, ModelMesh model, ReadOnlySpan<NVector3> vertices, out PickBox box)
    {
        NVector3 low = new(float.MaxValue), high = new(float.MinValue);
        for (int surface = 0; surface < model.SurfaceVertices.Length; surface++)
        {
            int[] used = model.SurfaceVertices[surface];
            int bytes = used.Length * 12;
            if (_upload.Length < bytes) _upload = new byte[bytes * 2];
            Span<float> positions = MemoryMarshal.Cast<byte, float>(_upload.AsSpan(0, bytes));
            for (int i = 0; i < used.Length; i++)
            {
                NVector3 v = vertices[used[i]];
                low = NVector3.Min(low, v);
                high = NVector3.Max(high, v);
                NVector3 p = RightHandedFrame.Point(v);
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
            }
            mesh.SurfaceUpdateVertexRegion(surface, 0, _upload.AsSpan(0, bytes));
        }
        box = low.X <= high.X ? new PickBox(low, high) : default;
    }

    private void FreeObjects()
    {
        GD.Print("DK2.Godot vertex animation: ", _vertexClips.Count, " shared clips, ", _vertexTextureBytes,
            " texture bytes, ", _animationPoseUpdates, " pose updates; 0 instance mesh copies, 0 animation vertex uploads");
        int playing = 0;
        foreach (ModelSlot slot in _objects.Values) if (slot.Animation?.Player.Clock != null) playing++;
        GD.Print("DK2.Godot objects: ", _objects.Count, " drawn (", playing, " playing a clip), ", _artless.Count, " without art, ", _structures.Count,
            " structures, ", _torches.Count, " torches; the object draws allocated ", _drawWarmUpBytes, " B in the first ",
            DrawWarmUp, " frames, then ", _drawLateBytes, " B in ", Math.Max(0, _drawFrames - DrawWarmUp - _drawBendingFrames),
            " and ", _drawBendingBytes, " B in ", _drawBendingFrames, " that bent torches again; steady by phase: hearts ",
            _steadyPhaseBytes[(int)DrawPhase.Hearts], " B, creatures ", _steadyPhaseBytes[(int)DrawPhase.Creatures],
            " B, objects and clips ", _steadyPhaseBytes[(int)DrawPhase.Objects],
            " B, torch bend ", _steadyPhaseBytes[(int)DrawPhase.Bend], " B, flames ", _steadyPhaseBytes[(int)DrawPhase.Flames],
            " B, sort ", _steadyPhaseBytes[(int)DrawPhase.Sort], " B, effects ", _steadyPhaseBytes[(int)DrawPhase.Effects], " B");
        FreeCreatures();
        foreach (ModelSlot slot in _objects.Values) slot.Free();
        foreach (ModelSlot slot in _structures.Values) slot.Free();
        foreach (TorchSlot slot in _torches.Values) slot.Free();
        _objects.Clear(); _structures.Clear(); _torches.Clear();
        _flames?.Free();
        _flames = null;
        _vertexClips.Clear();
        FreeVertexStaging();
        _objectMaterials.Clear();
        _extrasMaterials.Clear();
        ReportLighting();
    }

    /// <summary>A KMF model as Godot surfaces: one per material that has triangles, each holding the vertices its triangles
    /// use (in Godot's frame through <see cref="RightHandedFrame"/>, indices in their order), with the source vertex of each.</summary>
    private sealed class ModelMesh
    {
        public ModelMesh(KmfModel model, RenderMesh source)
        {
            Model = model;
            Source = source;
            SubmeshCount = source.Triangles.Count;
            SurfaceOf = new int[SubmeshCount];
            Sphere = PickSphere.Of(model);
            var vertices = new List<int[]>();
            Mesh = new ArrayMesh();
            var remap = new Dictionary<int, int>();
            for (int submesh = 0; submesh < SubmeshCount; submesh++)
            {
                int[] triangles = source.Triangles[submesh];
                if (triangles.Length == 0) { SurfaceOf[submesh] = -1; continue; }
                remap.Clear();
                var used = new List<int>();
                var indices = new int[triangles.Length];
                for (int i = 0; i < triangles.Length; i++)
                {
                    if (!remap.TryGetValue(triangles[i], out int local)) { remap.Add(triangles[i], local = used.Count); used.Add(triangles[i]); }
                    indices[i] = local;
                }
                SurfaceOf[submesh] = vertices.Count;
                vertices.Add([.. used]);
                Mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, Arrays(source.Positions, source.Normals, used, indices,
                    instance: true), null, null, ModelCustomFormat);
            }
            SurfaceVertices = [.. vertices];
            Box = Mesh.GetSurfaceCount() > 0 ? Mesh.GetAabb() : new Aabb();
            PickBox = PickBox.Around(source.Positions);
        }

        /// <summary>The KMF sphere the Hand's pick measures (Unity reads it from UV2); null for a model without one.</summary>
        public PickSphere? Sphere { get; }

        /// <summary>The model's own box in the renderers' frame (Unity's mesh bounds).</summary>
        public PickBox PickBox { get; }

        public KmfModel Model { get; }
        public RenderMesh Source { get; }
        public ArrayMesh Mesh { get; }
        public int SubmeshCount { get; }

        /// <summary>A submesh's surface, -1 where it has no triangles.</summary>
        public int[] SurfaceOf { get; }

        /// <summary>Per surface, the source vertex of each of its vertices.</summary>
        public int[][] SurfaceVertices { get; }

        public Aabb Box { get; }

        /// <summary>A surface's arrays at <paramref name="positions"/> (the renderers' frame), for a copy with its own vertices:
        /// the model's normals unless <paramref name="normals"/> is given, lit as an instance unless <paramref name="instance"/>
        /// is false (a torch: Unity's <c>LightingMode.TerrainPiece</c>).</summary>
        public Godot.Collections.Array Arrays(IReadOnlyList<NVector3> positions, int surface, IReadOnlyList<NVector3>? normals = null,
            bool instance = true)
        {
            int[] used = SurfaceVertices[surface];
            int submesh = Array.IndexOf(SurfaceOf, surface);
            int[] triangles = Source.Triangles[submesh];
            var local = new Dictionary<int, int>(used.Length);
            for (int i = 0; i < used.Length; i++) local[used[i]] = i;
            var indices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++) indices[i] = local[triangles[i]];
            return Arrays(positions, normals ?? Source.Normals, used, indices, instance);
        }

        /// <summary>The vertices with Unity's light inputs (<see cref="ClassicMeshes"/>' <c>SetLightBounds</c>): the KMF sphere
        /// (CUSTOM0, its centre in Godot's frame) and the anchor and light kind (CUSTOM1: no anchor, so the origin's tile;
        /// kind x 1 for an instance, y 1 for the bound skip); a model without a sphere gets zeros, as Unity's missing UVs.</summary>
        private Godot.Collections.Array Arrays(IReadOnlyList<NVector3> positions, IReadOnlyList<NVector3> normals,
            IReadOnlyList<int> used, int[] indices, bool instance)
        {
            var points = new Vector3[used.Count];
            var turned = new Vector3[used.Count];
            var uvs = new Vector2[used.Count];
            var bounds = new float[used.Count * 4];
            var kinds = new float[used.Count * 4];
            var sourceIndices = new float[used.Count];
            NVector3 centre = Sphere is PickSphere sphere ? RightHandedFrame.Point(sphere.Centre) : default;
            float radius = Sphere?.Radius ?? 0f, kindX = Sphere != null && instance ? 1f : 0f, kindY = Sphere != null ? 1f : 0f;
            for (int i = 0; i < used.Count; i++)
            {
                sourceIndices[i] = used[i];
                points[i] = ToGodot(RightHandedFrame.Point(positions[used[i]]));
                turned[i] = ToGodot(RightHandedFrame.Normal(normals[used[i]]));
                System.Numerics.Vector2 uv = Source.Uvs[used[i]];
                uvs[i] = new Vector2(uv.X, uv.Y);
                bounds[i * 4] = centre.X; bounds[(i * 4) + 1] = centre.Y; bounds[(i * 4) + 2] = centre.Z; bounds[(i * 4) + 3] = radius;
                kinds[(i * 4) + 2] = kindX; kinds[(i * 4) + 3] = kindY;
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = points;
            arrays[(int)Godot.Mesh.ArrayType.Normal] = turned;
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Godot.Mesh.ArrayType.Custom0] = bounds;
            arrays[(int)Godot.Mesh.ArrayType.Custom1] = kinds;
            arrays[(int)Godot.Mesh.ArrayType.Custom2] = sourceIndices;
            arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
            return arrays;
        }
    }

    /// <summary>What a thing's draw takes its looks from: an object's pose, a structure's view, a creature's, torch's or
    /// lock's plain model, or an effect's model under its element's tint (<see cref="ModelSlot.SetEffectTint"/>).</summary>
    private enum SlotKind { Object, Structure, Creature, Plain, Effect }

    /// <summary>One drawn model: a node showing the shared model or a clip's segment and its surfaces' materials; a blended
    /// one sorts by its AABB centre, Godot's default, among the blended runs (<see cref="RunSlot"/>).</summary>
    private sealed class ModelSlot
    {
        private readonly WorldScene _scene;
        private ModelMesh _shown;
        private ModelMesh _static;
        private StructureView _structure;
        private ObjectView _pose;

        public ModelSlot(WorldScene scene, ModelMesh model, string name, SlotKind kind, Node3D? parent = null)
        {
            _scene = scene;
            _shown = _static = model;
            Kind = kind;
            Node = new MeshInstance3D { Name = name, Mesh = model.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            // An effect's model casts no shadow, as Unity draws it with shadows off.
            if (scene.TorchShadowsActive && kind != SlotKind.Effect) Node.Layers = ShadowCasterLayers;
            (parent ?? (Node3D)scene).AddChild(Node);
        }

        public MeshInstance3D Node { get; }
        public WorldScene Scene => _scene;
        /// <summary>The fade distance per shadowed torch slot last given its instance (<see cref="PrepareShadows"/>).</summary>
        public Vector4 ShadowFades;
        /// <summary>Where DK2's shadow selection measures from: the shown model's bound-sphere centre, else its origin.</summary>
        public NVector3 ShadowCentre => _shown.Sphere is PickSphere sphere ? ToRender(sphere.Centre) : RenderPosition;
        public SlotKind Kind { get; private set; }
        /// <summary>An object's view; whether it moves between turns is read once here.</summary>
        public ObjectView Pose
        {
            get => _pose;
            set
            {
                _pose = value;
                Moving = Kind == SlotKind.Object && (value.PreviousPosX != value.PosX || value.PreviousPosY != value.PosY
                    || value.PreviousHeight != value.Height || value.PreviousHeading != value.Heading
                    || value.PreviousPitch != value.Pitch || value.PreviousRoll != value.Roll);
            }
        }
        public bool HasClip { get; set; }
        public ObjectClipIdentity Clip { get; set; }
        public AnimatedModel? Animation { get; set; }
        public ModelSlot? Lock { get; set; }
        public NVector3 RenderPosition { get; private set; }

        /// <summary>The turn and scale as last placed, in the renderers' frame.</summary>
        public System.Numerics.Quaternion RenderTurn { get; private set; } = System.Numerics.Quaternion.Identity;
        public NVector3 RenderScale { get; private set; } = NVector3.One;

        /// <summary>A structure's view: its plan colour or its tint decide its looks.</summary>
        public StructureView Structure
        {
            get => _structure;
            set { _structure = value; Kind = SlotKind.Structure; }
        }

        /// <summary>Whether the pose changes between turns, so each frame draws it between them.</summary>
        public bool Moving { get; private set; }

        public void Place(NVector3 position, in RenderEuler turn, Vector3 scale)
        {
            RenderPosition = position;
            RenderTurn = RenderRotation.Of(turn);
            RenderScale = new NVector3(scale.X, scale.Y, scale.Z);
            System.Numerics.Quaternion q = RightHandedFrame.Rotation(RenderTurn);
            var basis = new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)) * Basis.FromScale(scale);
            Node.Transform = new Transform3D(basis, ToGodot(RightHandedFrame.Point(position)));
        }

        /// <summary>The model at <paramref name="position"/> (the renderers' frame), its turn and scale kept.</summary>
        public void MoveTo(NVector3 position)
        {
            RenderPosition = position;
            Node.Position = ToGodot(RightHandedFrame.Point(position));
        }

        /// <summary>A model-local point (<see cref="RenderMesh"/>'s frame) where it is drawn, in the renderers' frame: Unity's
        /// <c>Transform.TransformPoint</c>.</summary>
        public NVector3 ToRender(NVector3 local) => RenderPosition + NVector3.Transform(local * RenderScale, RenderTurn);

        public void ShowStatic(ModelMesh model)
        {
            _static = model;
            Show(model, model.Mesh);
        }

        private VertexClip? _vertexClip;

        public void Show(ModelMesh model, Mesh mesh, VertexClip? clip = null)
        {
            bool changed = !ReferenceEquals(_shown, model) || !ReferenceEquals(Node.Mesh, mesh) || !ReferenceEquals(_vertexClip, clip);
            _vertexClip = clip;
            Node.CustomAabb = clip?.Box ?? model.Box;
            if (clip == null) Node.SetInstanceShaderParameter(AnimationPhaseParameter, -1f);
            _shown = model;
            if (!ReferenceEquals(Node.Mesh, mesh)) Node.Mesh = mesh;
            if (changed) ApplyLooks();
        }

        public void StopAnimation()
        {
            if (Animation == null) return;
            Animation.Player.Stop();
            Show(_static, _static.Mesh);
        }

        /// <summary>Each surface's look (Unity's <c>RestoreModel</c>, <c>ConfigureTexturePhase</c>, <c>SetAlpha</c>,
        /// <c>Blueprint</c> and <c>InstanceTinted</c>): an object's texture phase (the main segment or the model), terrain
        /// light and fade; a structure's plan or tint; a torch's or lock's own. Allocation-free once the materials exist.</summary>
        public void ApplyLooks()
        {
            ObjectView pose = Pose;
            bool phased = Kind == SlotKind.Object && (Animation?.ShowsMain ?? true);
            ObjectModelPasses.Build(_shown.Model.Materials, _shown.SubmeshCount, phased ? pose.DrawnTexturePhase : null, _scene._passes);
            byte alpha = Kind == SlotKind.Object ? pose.Alpha : byte.MaxValue;
            Node.Visible = ObjectMaterialLook.IsDrawn(alpha);
            bool plan = Kind == SlotKind.Structure && Structure.Plan != BlueprintTint.None;
            ObjectMaterialLook?[] environment = _scene.EnvironmentPasses(_shown.SubmeshCount);
            // A submesh's environment pass draws after its own, also over a plan (structure-drawing.md section 5, where
            // Unity draws the plan twice); folded into its material, and not drawn while the object fades.
            foreach (ObjectPass pass in _scene._passes)
                if (pass.Look.EnvironmentMapped) environment[pass.Submesh] = pass.Look;
            ModelInstanceLook instance = Kind == SlotKind.Object ? ModelInstanceLook.Object(pose.AddedLight, alpha)
                : Kind == SlotKind.Structure ? ModelInstanceLook.Structure(Structure.InstanceTint, plan)
                : Kind == SlotKind.Effect ? new ModelInstanceLook(_effectTint, 1f)
                : ModelInstanceLook.None;
            _alphaBlended = false;
            bool transparent = false;
            TransparentOrdered = false;
            foreach (ObjectPass pass in _scene._passes)
            {
                if (pass.Look.EnvironmentMapped) continue;
                int surface = _shown.SurfaceOf[pass.Submesh];
                if (surface < 0) continue;
                // The red pass skips a material made from a blended KMF one, whatever a fade or plan made of it (Unity's
                // IsKmfBlended; a plan's materials are not KMF ones, so it reads opaque).
                bool kmfBlended = !plan && pass.Look.Blend != KmfBlend.Opaque;
                ObjectMaterialLook look = plan ? ObjectMaterialLook.Blueprint(Structure.Plan == BlueprintTint.Red) : instance.Shape(pass.Look);
                bool own = !plan;
                // A hovered gold object draws its material's twin (Unity draws the twin over it, one queue later).
                if (Hover.Swap && pass.Submesh < _shown.Model.Materials.Count
                    && ObjectMaterialLook.HoverTwin(_shown.Model.Materials[pass.Submesh]) is ObjectMaterialLook twin)
                {
                    look = twin with { QueueOffset = 0 };
                    own = false;
                }
                bool alphaBlended = look.Blend == KmfBlend.AlphaBlend;
                _alphaBlended |= alphaBlended;
                transparent |= look.Blend != KmfBlend.Opaque;
                TransparentOrdered |= look.Blend == KmfBlend.AlphaBlend || (look.Blend == KmfBlend.Additive && look.WritesDepth);
                ObjectMaterialLook? environmentPass = environment[pass.Submesh];
                bool extras = alphaBlended && ((environmentPass != null && instance.EnvironmentDrawn)
                    || (kmfBlended ? Hover.RedBlended : Hover.RedOpaque));
                Node.SetSurfaceOverrideMaterial(surface,
                    _scene.MaterialFor(new ObjectSurfaceKey(look, environmentPass, InstanceLook: own, KmfBlended: kmfBlended,
                        Extras: extras, Animation: _vertexClip)));
            }
            if (transparent != Transparent)
            {
                Transparent = transparent;
                _scene.ListTransparent(this, transparent);
            }
            SetInstanceLook(instance);
            SetRed(Hover);
            _appliedHover = Hover;
        }

        /// <summary>The drawn thing's instance uniforms (Shaders/World.gdshaderinc) from <paramref name="instance"/>, each
        /// written only when it changed.</summary>
        private void SetInstanceLook(in ModelInstanceLook instance)
        {
            if (instance.Tint != _instance.Tint) Node.SetInstanceShaderParameter(TintParameter, ToGodot(instance.Tint));
            if (instance.Alpha != _instance.Alpha) Node.SetInstanceShaderParameter(AlphaParameter, instance.Alpha);
            if (instance.EnvironmentDrawn != _instance.EnvironmentDrawn)
                Node.SetInstanceShaderParameter(EnvironmentDrawnParameter, instance.EnvironmentDrawn ? 1f : 0f);
            _instance = instance;
        }

        /// <summary>The hover's red on the surfaces made from opaque (x) and blended (y) KMF materials.</summary>
        private void SetRed(HoverLooks hover)
        {
            var red = new Vector2(hover.RedOpaque ? 1f : 0f, hover.RedBlended ? 1f : 0f);
            if (red != _red) { _red = red; Node.SetInstanceShaderParameter(RedParameter, red); }
        }

        /// <summary>An effect's model lit by its element's tint more (Unity's <c>ClassicMeshes.ElementTinted</c>): the light
        /// added to each surface that takes added light, as an instance uniform; an additive surface is at full white.</summary>
        public void SetEffectTint(NVector3 tint)
        {
            if (tint == _effectTint) return;
            _effectTint = tint;
            SetInstanceLook(new ModelInstanceLook(tint, 1f));
        }

        private NVector3 _effectTint;
        private ModelInstanceLook _instance = ModelInstanceLook.None;
        private Vector2 _red;
        // Whether a surface is alpha-blended: its red draws as a next pass, which a material change brings.
        private bool _alphaBlended;

        /// <summary>Whether a surface draws in the transparent pass (an alpha-blended or additive look), where Godot sorts
        /// the model by its box among the blended items (<see cref="AddItemBarriers"/>); listed in the scene's
        /// <c>_transparentSlots</c> while it does.</summary>
        public bool Transparent { get; private set; }

        /// <summary>Whether a transparent surface's order matters: alpha-blended, or additive writing depth. Else every
        /// one is ONE, ONE without a depth write, which an additive item passes.</summary>
        public bool TransparentOrdered { get; private set; }

        /// <summary>The hover overlay this frame asks for (<see cref="WorldScene.DrawHoverOverlay"/>); applied by
        /// <see cref="CommitHover"/>.</summary>
        public HoverLooks Hover;
        private HoverLooks _appliedHover;

        /// <summary>Whether the slot was freed; a hover list drops it.</summary>
        public bool Freed { get; private set; }

        /// <summary>The asked-for hover overlay on the surfaces, when it changed; allocation-free once the materials exist.</summary>
        public void CommitHover()
        {
            if (Freed || Hover == _appliedHover) return;
            // Only the gold swap, or a red on an alpha-blended surface (its next pass), changes a material.
            if (Hover.Swap != _appliedHover.Swap || _alphaBlended) ApplyLooks();
            else
            {
                SetRed(Hover);
                _appliedHover = Hover;
            }
        }

        /// <summary>The shown model's posed vertices and their box: the clip's last sample, else the model's own.</summary>
        private IReadOnlyList<NVector3> Positions(out PickBox box)
        {
            NVector3[] posed = [];
            box = default;
            if (Animation != null && Animation.TryGetSample(out posed, out box)) return posed;
            box = _shown.PickBox;
            return _shown.Source.Positions;
        }

        /// <summary>The shown model's KMF sphere placed in the drawn world, available before sampling the pose.</summary>
        public PickSphere? Sphere => _shown.Sphere is PickSphere own && own.Usable
            ? own.Placed(RenderPosition, RenderTurn, RenderScale) : null;

        /// <summary>The exact posed box and KMF sphere where drawn; samples the current clip lazily.</summary>
        public void Measure(out PickBox bounds, out PickSphere? sphere)
        {
            Positions(out PickBox local);
            bounds = local.Placed(RenderPosition, RenderTurn, RenderScale);
            sphere = _shown.Sphere is PickSphere own && own.Usable ? own.Placed(RenderPosition, RenderTurn, RenderScale) : null;
        }

        /// <summary>The ray distance to the nearest triangle of the shown model it crosses before <paramref name="limit"/>
        /// (<see cref="PickTriangles"/>), or null.</summary>
        public float? NearestTriangle(NVector3 origin, NVector3 direction, float limit)
        {
            IReadOnlyList<NVector3> positions = Positions(out PickBox _);
            PickTriangles.ToLocal(RenderPosition, RenderTurn, RenderScale, ref origin, ref direction);
            float best = limit;
            bool found = false;
            IReadOnlyList<int[]> triangles = _shown.Source.Triangles;
            for (int submesh = 0; submesh < triangles.Count; submesh++)
                found |= PickTriangles.TryNearest(positions, triangles[submesh], origin, direction, ref best);
            return found ? best : null;
        }

        public void Free()
        {
            Freed = true;
            if (Transparent) _scene.ListTransparent(this, false);
            Lock = null;
            Animation?.Free();
            Node.QueueFree();
        }
    }

    /// <summary>Godot's clip surface: shared geometry, an instance phase, and exact lazy CPU samples for picking.</summary>
    private sealed class AnimatedModel : ICreatureAnimationSurface<ModelMesh?[]>, ICreatureAnimationPoseSurface
    {
        private readonly ModelSlot _slot;
        private ModelMesh?[] _templates = [];
        private readonly VertexClip?[] _segments = new VertexClip?[3];
        private AnimationSegment _shown = AnimationSegment.Main;

        public AnimatedModel(ModelSlot slot)
        {
            _slot = slot;
            Player = new CreatureAnimationPlayer<ModelMesh?[]>(this);
        }

        public CreatureAnimationPlayer<ModelMesh?[]> Player { get; }
        public bool ShowsMain => Player.Clock == null || _shown == AnimationSegment.Main;

        public bool TryGetSample(out NVector3[] vertices, out PickBox box)
        {
            bool sampled = Player.TryGetSample(out vertices);
            NVector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
            if (sampled && _templates[(int)_shown] is ModelMesh template)
                foreach (int[] surface in template.SurfaceVertices)
                    foreach (int vertex in surface)
                    {
                        low = NVector3.Min(low, vertices[vertex]);
                        high = NVector3.Max(high, vertices[vertex]);
                    }
            box = sampled && low.X <= high.X ? new PickBox(low, high) : default;
            return sampled;
        }

        void ICreatureAnimationSurface<ModelMesh?[]>.Install(RenderAnimation?[] clips, ModelMesh?[] templates)
        {
            _templates = templates;
            for (int i = 0; i < _segments.Length; i++)
                _segments[i] = i < clips.Length && i < templates.Length && clips[i] is RenderAnimation clip
                    && templates[i] is ModelMesh template ? _slot.Scene.VertexAnimation(clip, template) : null;
        }

        void ICreatureAnimationSurface<ModelMesh?[]>.Show(AnimationSegment segment)
        {
            _shown = segment;
            if (_templates[(int)segment] is ModelMesh template && _segments[(int)segment] is VertexClip clip)
                _slot.Show(template, template.Mesh, clip);
        }

        public void Pose(AnimationSegment segment, float phase)
        {
            _slot.Node.SetInstanceShaderParameter(AnimationPhaseParameter, phase);
            _slot.Scene._animationPoseUpdates++;
        }

        void ICreatureAnimationSurface<ModelMesh?[]>.Upload(AnimationSegment segment, NVector3[] vertices) =>
            throw new InvalidOperationException("A GPU animation surface receives phases, not CPU uploads.");

        public void Free()
        {
            Player.Stop();
            Array.Clear(_segments);
            _templates = [];
        }
    }

    /// <summary>A wall torch's model (Unity's <c>ClassicTorchGeometry</c>): its vertices placed by
    /// <see cref="TorchGeometry.Vertex"/>, turned, at the mount and bent by the wall's lattice, relative to the torch's point
    /// where the node stands (the origin whose tile's table lights it, as Unity's torch object), built again when the torch
    /// changes or a tile near its wall does; its normals turned by the heading, lit as a terrain piece.</summary>
    private sealed class TorchSlot
    {
        private readonly WorldScene _scene;
        private readonly ModelMesh? _model;
        private readonly MeshInstance3D? _node;
        private readonly NVector3[] _bent, _turned;
        private int _builtYaw = int.MinValue;

        public TorchSlot(WorldScene scene, ModelMesh? model, int floorTileIndex)
        {
            _scene = scene;
            _model = model;
            _bent = new NVector3[model?.Source.Positions.Count ?? 0];
            _turned = new NVector3[model?.Source.Normals.Count ?? 0];
            if (model == null) return;
            _node = new MeshInstance3D { Name = "Torch" + floorTileIndex, Mesh = new ArrayMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            scene.AddChild(_node);
        }

        public TorchView Torch { get; private set; }

        /// <summary>Whether a holder surface draws in the transparent pass (a barrier among the blended items), and whether
        /// its order matters (see <see cref="ModelSlot.TransparentOrdered"/>).</summary>
        public bool Transparent { get; private set; }
        public bool TransparentOrdered { get; private set; }
        public MeshInstance3D? Node => _node;

        public void Set(in TorchView torch)
        {
            Torch = torch;
            if (_model == null || _node == null) return;
            var origin = new NVector3(torch.PosX / (float)Fp32.One, torch.Height / (float)Fp32.One, -torch.PosY / (float)Fp32.One);
            _node.Position = ToGodot(RightHandedFrame.Point(origin));
            NVector3 low = new(float.MaxValue), high = new(float.MinValue);
            for (int i = 0; i < _bent.Length; i++)
            {
                _bent[i] = TorchGeometry.Vertex(torch, _model.Source.Positions[i], _scene._lattice) - origin;
                NVector3 p = RightHandedFrame.Point(_bent[i]);
                low = NVector3.Min(low, p);
                high = NVector3.Max(high, p);
            }
            // A vertex region update keeps the first build's box; the bent extent replaces it so Godot culls by it.
            if (_bent.Length > 0) _node.CustomAabb = new Aabb(ToGodot(low), ToGodot(high - low));
            var mesh = (ArrayMesh)_node.Mesh;
            if (mesh.GetSurfaceCount() == 0 || torch.Yaw != _builtYaw)
            {
                mesh.ClearSurfaces();
                _builtYaw = torch.Yaw;
                // Type-5 light meshes turn by yaw without the ordinary object's half-turn (wall-torches.md §4).
                System.Numerics.Quaternion turn = RenderRotation.Heading(MeshOrientation.Heading(torch.Yaw));
                for (int i = 0; i < _turned.Length; i++) _turned[i] = NVector3.Transform(_model.Source.Normals[i], turn);
                for (int surface = 0; surface < _model.SurfaceVertices.Length; surface++)
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _model.Arrays(_bent, surface, _turned, instance: false),
                        null, null, Mesh.ArrayFormat.FlagUseDynamicUpdate | ModelCustomFormat);
                ObjectModelPasses.Build(_model.Model.Materials, _model.SubmeshCount, null, _scene._passes);
                ObjectMaterialLook?[] environment = _scene.EnvironmentPasses(_model.SubmeshCount);
                foreach (ObjectPass pass in _scene._passes)
                    if (pass.Look.EnvironmentMapped) environment[pass.Submesh] = pass.Look;
                bool wasTransparent = Transparent;
                Transparent = TransparentOrdered = false;
                foreach (ObjectPass pass in _scene._passes)
                    if (!pass.Look.EnvironmentMapped && _model.SurfaceOf[pass.Submesh] >= 0)
                    {
                        Transparent |= pass.Look.Blend != KmfBlend.Opaque;
                        TransparentOrdered |= pass.Look.Blend == KmfBlend.AlphaBlend
                            || (pass.Look.Blend == KmfBlend.Additive && pass.Look.WritesDepth);
                    }
                if (Transparent && !wasTransparent) _scene._transparentTorches.Add(this);
                else if (!Transparent && wasTransparent) _scene._transparentTorches.Remove(this);
                foreach (ObjectPass pass in _scene._passes)
                    if (!pass.Look.EnvironmentMapped && _model.SurfaceOf[pass.Submesh] >= 0)
                        mesh.SurfaceSetMaterial(_model.SurfaceOf[pass.Submesh],
                            _scene.MaterialFor(new ObjectSurfaceKey(pass.Look, environment[pass.Submesh], InstanceLook: true, KmfBlended: false,
                                Extras: pass.Look.Blend == KmfBlend.AlphaBlend && environment[pass.Submesh] != null)));
                return;
            }
            // Bent again in place, as Unity's ClassicTorchGeometry.Refresh rewrites its vertices.
            _scene.UploadPositions(mesh, _model, _bent, out _);
        }

        public void SetPossessionVisibility()
        {
            if (_node != null) _node.Visible = _scene.PossessionFloorVisible(Torch.FloorTileIndex);
        }
        public void Free()
        {
            if (Transparent) _scene._transparentTorches.Remove(this);
            _node?.QueueFree();
        }
    }

    /// <summary>The wall flames (Unity's <c>ClassicTorchFlames</c>, audit item 6): the install's flame sprite as camera-facing
    /// quads, additive (source alpha, one), unshaded, both faces, no depth write, a quarter tile wide; every flame on one
    /// clock (<see cref="ClassicTorchFlameAnimation"/>) at its own phase (<see cref="ClassicTorchFlamePhases"/>), at
    /// <see cref="ClassicTorchFlame.RenderPoint"/>. The frames are one texture array and every flame an instance of one
    /// blended-item batch, its frame the instance's custom data, each placed among the runs by its own distance.</summary>
    private sealed class TorchFlames
    {
        private readonly WorldScene _scene;
        private readonly Rid _quad;
        private readonly Texture2DArray? _frames;
        private readonly ShaderMaterial? _material;
        private readonly ItemBatch? _items;
        private readonly int _frameCount;
        // Half the quad's diagonal: the camera-facing quad stays within it of its point.
        private readonly float _radius;
        private readonly Dictionary<int, Flame> _drawn = new();
        private readonly ClassicTorchFlamePhases _phases = new();
        private readonly ClassicTorchFlameAnimation _animation = new();
        private long _started = -1;

        private sealed class Flame
        {
            public int PhaseOffsetRaw, Frame;
            public Vector3 Point;
            public bool Visible = true;
        }

        public TorchFlames(WorldScene scene, IEffectSpriteProvider provider)
        {
            _scene = scene;
            if (!provider.TryResolve(ClassicTorchFlame.Asset, out ResolvedAsset asset)
                || !provider.TryGetSprite(asset, out EffectSpriteResource sprite)) return;
            var images = new Godot.Collections.Array<Image>();
            int width = 0, height = 0;
            for (int frame = 0; frame < sprite.StoredFrameCount; frame++)
            {
                if (!provider.TryLoadFrame(asset, frame, out DK2.Art.Textures.RgbaImage image)) break;
                // Rows top first, as the frame stands with its tip up.
                Image pixels = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels);
                if (frame == 0) (width, height) = (image.Width, image.Height);
                else if (image.Width != width || image.Height != height)
                {
                    // A texture array's layers share one size; every frame is drawn at the first's shape anyway.
                    GD.PushWarning("DK2.Godot: the wall flame's frames differ in size; a frame is scaled to the first's.");
                    pixels.Resize(width, height, Image.Interpolation.Bilinear);
                }
                images.Add(pixels);
            }
            if (images.Count == 0) return;
            _frameCount = images.Count;
            _frames = new Texture2DArray();
            _frames.CreateFromImages(images);
            foreach (Image image in images) image.Dispose();
            _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/TorchFlame.gdshader") };
            _material.SetShaderParameter("frames", _frames);
            // A sprite as wide as the flame's size: SizeRaw / 4096 tiles, its height by the frame's shape.
            float w = ClassicTorchFlame.SizeRaw / 4096f, h = w * height / width;
            _radius = 0.5f * MathF.Sqrt(w * w + h * h);
            _quad = RenderingServer.MeshCreate();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0),
                new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, -h / 2, 0) };
            arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
            RenderingServer.MeshAddSurfaceFromArrays(_quad, RenderingServer.PrimitiveType.Triangles, arrays);
            _items = scene.CreateItemBatch(_quad, _material.GetRid(), BlendedItemBlend.Additive);
        }

        /// <summary>The flames placed, and the frames in the array.</summary>
        public int Count => _drawn.Count;
        public int FrameCount => _frameCount;

        public void Set(in TorchView torch)
        {
            if (torch.Mesh.IsEmpty)
            {
                Remove(torch.FloorTileIndex);
                return;
            }
            if (_items == null) return;
            if (!_drawn.TryGetValue(torch.FloorTileIndex, out Flame? flame))
            {
                flame = new Flame { PhaseOffsetRaw = _phases.OffsetOf(torch.FloorTileIndex) };
                _drawn.Add(torch.FloorTileIndex, flame);
            }
            flame.Point = ToGodot(RightHandedFrame.Point(ClassicTorchFlame.RenderPoint(torch, _scene._lattice)));
        }

        public void Remove(int floorTileIndex)
        {
            _drawn.Remove(floorTileIndex);
            _phases.Remove(floorTileIndex);
        }

        /// <summary>Unity's <c>AdvanceTorchAnimation</c>: the shared clock to now, each flame's frame.</summary>
        public void Draw(long milliseconds)
        {
            if (_items == null) return;
            if (_started < 0) _started = milliseconds;
            _animation.AdvanceTo(new EffectTime(milliseconds - _started));
            foreach (KeyValuePair<int, Flame> entry in _drawn)
                entry.Value.Frame = _animation.Frame(_frameCount, entry.Value.PhaseOffsetRaw);
        }

        /// <summary>Each shown flame as a blended item at its point, its frame the layer.</summary>
        public void AddItems()
        {
            if (_items == null) return;
            foreach (KeyValuePair<int, Flame> entry in _drawn)
            {
                Flame flame = entry.Value;
                if (!flame.Visible) continue;
                _scene.AddItem(_items, flame.Point, _radius, new Transform3D(Basis.Identity, flame.Point), Colors.White,
                    new Color(flame.Frame, 0f, 0f, 0f));
            }
        }

        public void SetPossessionVisibility()
        {
            foreach (KeyValuePair<int, Flame> entry in _drawn) entry.Value.Visible = _scene.PossessionFloorVisible(entry.Key);
        }

        public void Free()
        {
            _drawn.Clear();
            _material?.Dispose();
            _frames?.Dispose();
            if (_quad.IsValid) RenderingServer.FreeRid(_quad);
        }
    }
}
