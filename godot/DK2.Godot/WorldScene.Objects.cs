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
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Objects, structures and wall torches (plan step 3f), as Unity's <c>DungeonScene</c> draws them: each install
/// model built once into a shared <see cref="ArrayMesh"/> (one surface per KMF material, <see cref="ModelMesh"/>), a
/// <see cref="MeshInstance3D"/> per thing with its surfaces' <see cref="ObjectMaterialLook"/>s, a type-6 clip played by the
/// shared <see cref="CreatureAnimationPlayer{TResources}"/> (<see cref="AnimatedModel"/>), the Heart's beat through
/// <see cref="HeartPulse"/>, torches bent by the lattice and their flames (<see cref="TorchFlames"/>). The environment pass
/// waits for the lighting (step 3i), which carries its coordinates.</summary>
public partial class WorldScene
{
    /// <summary>The distance in tiles over which <see cref="ZeroOrderOffset"/> spreads its keys; any positive value keeps
    /// the order, this one keeps the key steps above float precision across a level.</summary>
    private const float ZeroOrderSpread = 64f;

    private readonly Dictionary<int, ModelSlot> _objects = new();
    private readonly Dictionary<int, AssetRef> _artless = new();
    private readonly Dictionary<StructureId, ModelSlot> _structures = new();
    private readonly Dictionary<StructureId, StructureView> _structureViews = new();
    private readonly Dictionary<int, TorchSlot> _torches = new();
    private readonly Dictionary<(string Entry, bool LastFrame), ModelMesh?> _models = new();
    private readonly Dictionary<string, RenderAnimation> _clips = new();
    private readonly Dictionary<string, ClipSet?> _clipSets = new();
    private readonly Dictionary<ObjectMaterialLook, ShaderMaterial> _objectMaterials = new();
    private readonly List<ObjectPass> _passes = new();
    private readonly HeartPulse _heartPulse = new();
    private LatticeDirtyTracker? _latticeDirty;
    private TorchFlames? _flames;
    private Func<EffectTime>? _effectClock;
    private byte[] _upload = [];
    private long _frameMilliseconds;
    // The camera as the order-0 keys last read it, in Godot's frame; a new stamp re-keys every blended thing.
    private NVector3 _eye;
    private float _originDistance;
    private int _sortStamp;
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
    }

    public void UpdateTorch(int floorTileIndex, in TorchView torch, in ResolvedAsset mesh) => CreateTorch(floorTileIndex, torch, mesh);

    public void RemoveTorch(int floorTileIndex)
    {
        if (_torches.Remove(floorTileIndex, out TorchSlot? slot)) slot.Free();
        _flames?.Remove(floorTileIndex);
    }

    /// <summary>Unity's <c>DrawCreatureFrame</c> in <see cref="SceneFrame"/>'s order (this scene is its host): the Hearts'
    /// queued health steps, each creature's drawn place, clip and head, each moving object's drawn pose, each clip's frame
    /// and a Heart's beat, the torches near a changed tile bent again; then the flames advanced. The beat's sound and mana
    /// effect are not drawn yet (step 3k and step 4). <see cref="FinishFrame"/> sorts once the camera is placed.
    /// Allocation-free.</summary>
    public void DrawFrame(GameTick tick, float fraction)
    {
        _frameBending = _latticeDirty is { AnyTouched: true };
        _phaseStart = GC.GetAllocatedBytesForCurrentThread();
        _frame.Draw(tick, fraction);
        _drawn = null;
        _flames?.Draw(_frameMilliseconds);
        Mark(DrawPhase.Flames);
    }

    /// <summary>After the frame's camera is placed (the query follow reads the drawn creatures): the blended stream sorted
    /// for it and every blended thing's order-0 key (<see cref="ZeroOrderOffset"/>). Allocation-free.</summary>
    public void FinishFrame()
    {
        DrawBlended();
        _phaseStart = GC.GetAllocatedBytesForCurrentThread();
        if (View is MinimapCameraView view)
        {
            NVector3 eye = RightHandedFrame.Point(view.Position);
            if (eye != _eye || _sortStamp == 0)
            {
                _eye = eye;
                _originDistance = eye.Length();
                _sortStamp++;
            }
        }
        foreach (KeyValuePair<int, CreatureSlot> entry in _creatures) entry.Value.Slot.Sort();
        foreach (KeyValuePair<int, ModelSlot> entry in _objects) entry.Value.Sort();
        foreach (KeyValuePair<StructureId, ModelSlot> entry in _structures) entry.Value.Sort();
        _flames?.Sort();
        Mark(DrawPhase.Sort);
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

    private enum DrawPhase { Hearts, Creatures, Objects, Bend, Flames, Sort, Count }

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
        if (_frustumStale && View != null)
        {
            _frustum = ViewFrustum.FromPose(_placed, _placedViewport.Width / (float)_placedViewport.Height, NearPlane);
            _frustumStale = false;
        }
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
            bool visible = View != null && !_frustum.Excludes(at, at, at);
            if (_heartPulse.TryBeat(pose, beatPending: true, visible, _effectClock!, tick).Consumed) animated.Player.ConsumeBeat();
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

    /// <summary>The sorting offset that puts a blended instance among Unity's sorting order 0, back to front by the distance
    /// to <paramref name="centre"/> (Unity's bounds centre): Godot keys a transparent by distance(camera, pivot) minus the
    /// offset, the runs sit at the origin keyed by their order (<see cref="SortFromOrigin"/>), so order 0 is the open
    /// interval one below the origin's distance, which these keys fill without reaching either end.</summary>
    private float ZeroOrderOffset(Vector3 pivot, Vector3 centre)
    {
        var eye = new Vector3(_eye.X, _eye.Y, _eye.Z);
        float distance = eye.DistanceTo(centre);
        float key = _originDistance - 0.99f + (0.98f * distance / (distance + ZeroOrderSpread));
        return eye.DistanceTo(pivot) - key;
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

    /// <summary>The material an object draw looks like, one per <see cref="ObjectMaterialLook"/>; the shaders are the
    /// terrain's (<see cref="ShaderFor"/>).</summary>
    private ShaderMaterial MaterialFor(in ObjectMaterialLook look)
    {
        if (_objectMaterials.TryGetValue(look, out ShaderMaterial? material)) return material;
        material = new ShaderMaterial { Shader = ShaderFor(look.Blend, look.FullLight, look.WritesDepth) };
        material.SetShaderParameter(AlbedoParameter, TextureFor(look.Texture));
        material.SetShaderParameter(ColourParameter, new Color(look.Colour.X, look.Colour.Y, look.Colour.Z, look.Colour.W));
        material.SetShaderParameter(AddedLightParameter, ToGodot(look.AddedLight));
        _objectMaterials.Add(look, material);
        return material;
    }

    /// <summary>Rewrites each surface's positions in place from <paramref name="vertices"/> (the renderers' frame, by
    /// source vertex) through one reused buffer: a dynamic surface's vertex stream is its positions alone (no normals
    /// until step 3i), checked when an animated copy is made.</summary>
    private void UploadPositions(ArrayMesh mesh, ModelMesh model, ReadOnlySpan<NVector3> vertices)
    {
        for (int surface = 0; surface < model.SurfaceVertices.Length; surface++)
        {
            int[] used = model.SurfaceVertices[surface];
            int bytes = used.Length * 12;
            if (_upload.Length < bytes) _upload = new byte[bytes * 2];
            Span<float> positions = MemoryMarshal.Cast<byte, float>(_upload.AsSpan(0, bytes));
            for (int i = 0; i < used.Length; i++)
            {
                NVector3 p = RightHandedFrame.Point(vertices[used[i]]);
                positions[i * 3] = p.X;
                positions[(i * 3) + 1] = p.Y;
                positions[(i * 3) + 2] = p.Z;
            }
            mesh.SurfaceUpdateVertexRegion(surface, 0, _upload.AsSpan(0, bytes));
        }
    }

    // The clip copies alive (each a dynamic ArrayMesh, a 12-byte position and an 8-byte UV per vertex), and their peak.
    private int _copies, _peakCopies;
    private long _copyBytes, _peakCopyBytes;

    private void CountCopy(ModelMesh template, int sign)
    {
        long vertices = 0;
        foreach (int[] surface in template.SurfaceVertices) vertices += surface.Length;
        _copies += sign;
        _copyBytes += sign * vertices * 20;
        _peakCopies = Math.Max(_peakCopies, _copies);
        _peakCopyBytes = Math.Max(_peakCopyBytes, _copyBytes);
    }

    private void FreeObjects()
    {
        GD.Print("DK2.Godot clip copies: ", _copies, " alive (", _copyBytes, " vertex bytes), at most ", _peakCopies, " (",
            _peakCopyBytes, " B)");
        int playing = 0;
        foreach (ModelSlot slot in _objects.Values) if (slot.Animation?.Player.Clock != null) playing++;
        GD.Print("DK2.Godot objects: ", _objects.Count, " drawn (", playing, " playing a clip), ", _artless.Count, " without art, ", _structures.Count,
            " structures, ", _torches.Count, " torches; the object draws allocated ", _drawWarmUpBytes, " B in the first ",
            DrawWarmUp, " frames, then ", _drawLateBytes, " B in ", Math.Max(0, _drawFrames - DrawWarmUp - _drawBendingFrames),
            " and ", _drawBendingBytes, " B in ", _drawBendingFrames, " that bent torches again; steady by phase: hearts ",
            _steadyPhaseBytes[(int)DrawPhase.Hearts], " B, creatures ", _steadyPhaseBytes[(int)DrawPhase.Creatures],
            " B, objects and clips ", _steadyPhaseBytes[(int)DrawPhase.Objects],
            " B, torch bend ", _steadyPhaseBytes[(int)DrawPhase.Bend], " B, flames ", _steadyPhaseBytes[(int)DrawPhase.Flames],
            " B, sort ", _steadyPhaseBytes[(int)DrawPhase.Sort], " B");
        FreeCreatures();
        foreach (ModelSlot slot in _objects.Values) slot.Free();
        foreach (ModelSlot slot in _structures.Values) slot.Free();
        foreach (TorchSlot slot in _torches.Values) slot.Free();
        _objects.Clear(); _structures.Clear(); _torches.Clear();
        _flames?.Free();
        _flames = null;
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
                Mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, Arrays(source.Positions, used, indices));
            }
            SurfaceVertices = [.. vertices];
            Box = Mesh.GetSurfaceCount() > 0 ? Mesh.GetAabb() : new Aabb();
        }

        public KmfModel Model { get; }
        public RenderMesh Source { get; }
        public ArrayMesh Mesh { get; }
        public int SubmeshCount { get; }

        /// <summary>A submesh's surface, -1 where it has no triangles.</summary>
        public int[] SurfaceOf { get; }

        /// <summary>Per surface, the source vertex of each of its vertices.</summary>
        public int[][] SurfaceVertices { get; }

        public Aabb Box { get; }

        /// <summary>A surface's arrays at <paramref name="positions"/> (the renderers' frame), for a copy with its own vertices.</summary>
        public Godot.Collections.Array Arrays(IReadOnlyList<NVector3> positions, int surface)
        {
            int[] used = SurfaceVertices[surface];
            int submesh = Array.IndexOf(SurfaceOf, surface);
            int[] triangles = Source.Triangles[submesh];
            var local = new Dictionary<int, int>(used.Length);
            for (int i = 0; i < used.Length; i++) local[used[i]] = i;
            var indices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++) indices[i] = local[triangles[i]];
            return Arrays(positions, used, indices);
        }

        private Godot.Collections.Array Arrays(IReadOnlyList<NVector3> positions, IReadOnlyList<int> used, int[] indices)
        {
            var points = new Vector3[used.Count];
            var uvs = new Vector2[used.Count];
            for (int i = 0; i < used.Count; i++)
            {
                points[i] = ToGodot(RightHandedFrame.Point(positions[used[i]]));
                System.Numerics.Vector2 uv = Source.Uvs[used[i]];
                uvs[i] = new Vector2(uv.X, uv.Y);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Godot.Mesh.ArrayType.Max);
            arrays[(int)Godot.Mesh.ArrayType.Vertex] = points;
            arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
            return arrays;
        }
    }

    /// <summary>What a thing's draw takes its looks from: an object's pose, a structure's view, or a creature's, torch's or
    /// lock's plain model.</summary>
    private enum SlotKind { Object, Structure, Creature, Plain }

    /// <summary>One drawn model: a node showing the shared model or a clip's segment, its surfaces' materials and, when one
    /// of them blends, its order-0 key.</summary>
    private sealed class ModelSlot
    {
        private readonly WorldScene _scene;
        private ModelMesh _shown;
        private ModelMesh _static;
        private int _sortStamp = -1;
        private bool _blended;
        private Vector3 _centre;
        private StructureView _structure;
        private ObjectView _pose;

        public ModelSlot(WorldScene scene, ModelMesh model, string name, SlotKind kind, Node3D? parent = null)
        {
            _scene = scene;
            _shown = _static = model;
            Kind = kind;
            Node = new MeshInstance3D { Name = name, Mesh = model.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            (parent ?? (Node3D)scene).AddChild(Node);
        }

        public MeshInstance3D Node { get; }
        public WorldScene Scene => _scene;
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
            _sortStamp = -1;
        }

        /// <summary>The model at <paramref name="position"/> (the renderers' frame), its turn and scale kept.</summary>
        public void MoveTo(NVector3 position)
        {
            RenderPosition = position;
            Node.Position = ToGodot(RightHandedFrame.Point(position));
            _sortStamp = -1;
        }

        /// <summary>A model-local point (<see cref="RenderMesh"/>'s frame) where it is drawn, in the renderers' frame: Unity's
        /// <c>Transform.TransformPoint</c>.</summary>
        public NVector3 ToRender(NVector3 local) => RenderPosition + NVector3.Transform(local * RenderScale, RenderTurn);

        public void ShowStatic(ModelMesh model)
        {
            _static = model;
            Show(model, model.Mesh);
        }

        public void Show(ModelMesh model, Mesh mesh)
        {
            bool changed = !ReferenceEquals(_shown, model) || !ReferenceEquals(Node.Mesh, mesh);
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
            _blended = false;
            foreach (ObjectPass pass in _scene._passes)
            {
                // The environment pass waits for the lighting (step 3i), which carries its coordinates.
                if (pass.Look.EnvironmentMapped) continue;
                int surface = _shown.SurfaceOf[pass.Submesh];
                if (surface < 0) continue;
                ObjectMaterialLook look = pass.Look;
                if (Kind == SlotKind.Object) look = look.Tinted(pose.AddedLight).Faded(alpha);
                else if (Kind == SlotKind.Structure && Structure.Plan != BlueprintTint.None)
                    look = ObjectMaterialLook.Blueprint(Structure.Plan == BlueprintTint.Red);
                else if (Kind == SlotKind.Structure)
                    look = look.InstanceTinted(Structure.InstanceTint, Structure.InstanceTint, Structure.InstanceTint);
                _blended |= look.Blend != KmfBlend.Opaque;
                Node.SetSurfaceOverrideMaterial(surface, _scene.MaterialFor(look));
            }
            if (_blended) Node.SortingUseAabbCenter = false;
            _sortStamp = -1;
        }

        /// <summary>A blended model's order-0 key, again when it or the camera moved (<see cref="ZeroOrderOffset"/>).</summary>
        public void Sort()
        {
            Lock?.Sort();
            if (!_blended || _sortStamp == _scene._sortStamp) return;
            _sortStamp = _scene._sortStamp;
            Transform3D transform = Node.GlobalTransform;
            // A clip's centre is its first frame's box, which a re-key does not follow while the clip plays.
            _centre = transform * _shown.Box.GetCenter();
            Node.SortingOffset = _scene.ZeroOrderOffset(transform.Origin, _centre);
        }

        public void Free()
        {
            Lock = null;
            Animation?.Free();
            Node.QueueFree();
        }
    }

    /// <summary>Godot's side of <see cref="CreatureAnimationPlayer{TResources}"/> for an object (Unity's
    /// <c>ClassicCreatureAnimation</c>): a copy of each segment's model whose positions are rewritten in place each draw.</summary>
    private sealed class AnimatedModel : ICreatureAnimationSurface<ModelMesh?[]>
    {
        private readonly ModelSlot _slot;
        private RenderAnimation?[] _clips = [];
        private ModelMesh?[] _templates = [];
        private readonly ArrayMesh?[] _meshes = new ArrayMesh?[3];
        // Each template's own copy, kept for the model's life so a clip started again (a creature's walk after its dig)
        // makes none: bounded by the distinct clip templates the creature's or object's entries name. Unity's
        // ClassicCreatureAnimation keeps its copies only while the clip set is the same and releases them on another.
        private readonly Dictionary<ModelMesh, ArrayMesh> _copies = new();
        private AnimationSegment _shown = AnimationSegment.Main;

        public AnimatedModel(ModelSlot slot)
        {
            _slot = slot;
            Player = new CreatureAnimationPlayer<ModelMesh?[]>(this);
        }

        public CreatureAnimationPlayer<ModelMesh?[]> Player { get; }

        /// <summary>Whether the main segment shows, the one a texture phase dresses.</summary>
        public bool ShowsMain => Player.Clock == null || _shown == AnimationSegment.Main;

        void ICreatureAnimationSurface<ModelMesh?[]>.Install(RenderAnimation?[] clips, ModelMesh?[] templates)
        {
            if (Same(clips, templates)) return;
            _clips = clips;
            _templates = templates;
            for (int segment = 0; segment < _meshes.Length; segment++)
            {
                _meshes[segment] = null;
                if (segment >= clips.Length || clips[segment] is not RenderAnimation clip || templates[segment] is not ModelMesh template)
                    continue;
                if (!_copies.TryGetValue(template, out ArrayMesh? mesh))
                {
                    mesh = new ArrayMesh();
                    for (int surface = 0; surface < template.SurfaceVertices.Length; surface++)
                        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, template.Arrays(clip.Mesh.Positions, surface), null,
                            null, Mesh.ArrayFormat.FlagUseDynamicUpdate);
                    if (mesh.GetSurfaceCount() > 0 && RenderingServer.MeshSurfaceGetFormatVertexStride(
                            (RenderingServer.ArrayFormat)mesh.SurfaceGetFormat(0), template.SurfaceVertices[0].Length) != 12)
                        GD.PushError("DK2.Godot: an animated model's vertex stream is not positions alone; it draws wrong.");
                    _copies.Add(template, mesh);
                    _slot.Scene.CountCopy(template, +1);
                }
                _meshes[segment] = mesh;
            }
        }

        void ICreatureAnimationSurface<ModelMesh?[]>.Show(AnimationSegment segment)
        {
            _shown = segment;
            if (_templates[(int)segment] is ModelMesh template && _meshes[(int)segment] is ArrayMesh mesh)
            {
                _slot.Show(template, mesh);
                // The clip's vertices leave the first frame's box, which Godot culls by; Unity recomputes its bounds each upload.
                Aabb box = template.Box;
                _slot.Node.CustomAabb = box.Grow(Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z)));
            }
        }

        void ICreatureAnimationSurface<ModelMesh?[]>.Upload(AnimationSegment segment, NVector3[] vertices)
        {
            if (_templates[(int)segment] is ModelMesh template && _meshes[(int)segment] is ArrayMesh mesh)
                _slot.Scene.UploadPositions(mesh, template, vertices);
        }

        private bool Same(RenderAnimation?[] clips, ModelMesh?[] templates)
        {
            if (clips.Length != _clips.Length || templates.Length != _templates.Length) return false;
            for (int i = 0; i < clips.Length; i++)
                if (!ReferenceEquals(clips[i], _clips[i]) || !ReferenceEquals(templates[i], _templates[i])) return false;
            return true;
        }

        public void Free()
        {
            for (int i = 0; i < _meshes.Length; i++) _meshes[i] = null;
            foreach (ModelMesh template in _copies.Keys) _slot.Scene.CountCopy(template, -1);
            _copies.Clear();
            _clips = [];
            _templates = [];
        }
    }

    /// <summary>A wall torch's model (Unity's <c>ClassicTorchGeometry</c>): its vertices placed by
    /// <see cref="TorchGeometry.Vertex"/>, turned, at the mount and bent by the wall's lattice, built again when the torch
    /// changes or a tile near its wall does.</summary>
    private sealed class TorchSlot
    {
        private readonly WorldScene _scene;
        private readonly ModelMesh? _model;
        private readonly MeshInstance3D? _node;
        private readonly NVector3[] _bent;

        public TorchSlot(WorldScene scene, ModelMesh? model, int floorTileIndex)
        {
            _scene = scene;
            _model = model;
            _bent = new NVector3[model?.Source.Positions.Count ?? 0];
            if (model == null) return;
            _node = new MeshInstance3D { Name = "Torch" + floorTileIndex, Mesh = new ArrayMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            scene.AddChild(_node);
        }

        public TorchView Torch { get; private set; }

        public void Set(in TorchView torch)
        {
            Torch = torch;
            if (_model == null || _node == null) return;
            NVector3 low = new(float.MaxValue), high = new(float.MinValue);
            for (int i = 0; i < _bent.Length; i++)
            {
                _bent[i] = TorchGeometry.Vertex(torch, _model.Source.Positions[i], _scene._lattice);
                NVector3 p = RightHandedFrame.Point(_bent[i]);
                low = NVector3.Min(low, p);
                high = NVector3.Max(high, p);
            }
            // A vertex region update keeps the first build's box; the bent extent replaces it so Godot culls by it.
            if (_bent.Length > 0) _node.CustomAabb = new Aabb(ToGodot(low), ToGodot(high - low));
            var mesh = (ArrayMesh)_node.Mesh;
            if (mesh.GetSurfaceCount() == 0)
            {
                for (int surface = 0; surface < _model.SurfaceVertices.Length; surface++)
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _model.Arrays(_bent, surface), null, null,
                        Mesh.ArrayFormat.FlagUseDynamicUpdate);
                ObjectModelPasses.Build(_model.Model.Materials, _model.SubmeshCount, null, _scene._passes);
                foreach (ObjectPass pass in _scene._passes)
                    if (!pass.Look.EnvironmentMapped && _model.SurfaceOf[pass.Submesh] >= 0)
                        mesh.SurfaceSetMaterial(_model.SurfaceOf[pass.Submesh], _scene.MaterialFor(pass.Look));
                return;
            }
            // Bent again in place, as Unity's ClassicTorchGeometry.Refresh rewrites its vertices.
            _scene.UploadPositions(mesh, _model, _bent);
        }

        public void Free() => _node?.QueueFree();
    }

    /// <summary>The wall flames (Unity's <c>ClassicTorchFlames</c>): the install's flame sprite as camera-facing quads,
    /// additive (source alpha, one), unshaded, both faces, no depth write, a quarter tile wide; every flame on one clock
    /// (<see cref="ClassicTorchFlameAnimation"/>) at its own phase (<see cref="ClassicTorchFlamePhases"/>), at
    /// <see cref="ClassicTorchFlame.RenderPoint"/>. One <see cref="RenderingServer"/> instance a flame, its frame a material.</summary>
    private sealed class TorchFlames
    {
        private readonly WorldScene _scene;
        private readonly Rid _quad;
        private readonly List<ShaderMaterial> _frames = new();
        private readonly Dictionary<int, Flame> _drawn = new();
        private readonly ClassicTorchFlamePhases _phases = new();
        private readonly ClassicTorchFlameAnimation _animation = new();
        private readonly Aabb _box;
        private long _started = -1;

        private sealed class Flame
        {
            public Rid Instance;
            public TorchView Torch;
            public int PhaseOffsetRaw, Frame = -1, SortStamp = -1;
            public Vector3 Point;
        }

        public TorchFlames(WorldScene scene, IEffectSpriteProvider provider)
        {
            _scene = scene;
            if (!provider.TryResolve(ClassicTorchFlame.Asset, out ResolvedAsset asset)
                || !provider.TryGetSprite(asset, out EffectSpriteResource sprite)) return;
            var shader = new Shader
            {
                Code = "shader_type spatial;\nrender_mode unshaded, blend_add, depth_draw_never, cull_disabled;\n"
                    + "uniform sampler2D frame_texture : filter_linear, repeat_disable;\n"
                    // Turned as the camera is, as Unity sets the sprite's rotation to the camera's.
                    + "void vertex() {\nMODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], "
                    + "INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);\n}\n"
                    + "void fragment() {\nvec4 c = texture(frame_texture, UV);\nALBEDO = c.rgb;\nALPHA = c.a;\n}\n",
            };
            int width = 0, height = 0;
            for (int frame = 0; frame < sprite.StoredFrameCount; frame++)
            {
                if (!provider.TryLoadFrame(asset, frame, out DK2.Art.Textures.RgbaImage image)) break;
                if (frame == 0) (width, height) = (image.Width, image.Height);
                else if (image.Width * height != image.Height * width)
                    GD.PushWarning("DK2.Godot: the wall flame's frames differ in shape; every frame is drawn at the first's.");
                // Rows top first, as the frame stands with its tip up.
                using Image pixels = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels);
                var material = new ShaderMaterial { Shader = shader };
                material.SetShaderParameter("frame_texture", ImageTexture.CreateFromImage(pixels));
                _frames.Add(material);
            }
            if (_frames.Count == 0) return;
            // A sprite as wide as the flame's size: SizeRaw / 4096 tiles, its height by the frame's shape.
            float w = ClassicTorchFlame.SizeRaw / 4096f, h = w * height / width;
            _quad = RenderingServer.MeshCreate();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0),
                new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, -h / 2, 0) };
            arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
            RenderingServer.MeshAddSurfaceFromArrays(_quad, RenderingServer.PrimitiveType.Triangles, arrays);
            // The quad turns with the camera in the shader: the box covers every turn.
            float r = Math.Max(w, h);
            _box = new Aabb(new Vector3(-r, -r, -r), new Vector3(2 * r, 2 * r, 2 * r));
        }

        public void Set(in TorchView torch)
        {
            if (torch.Mesh.IsEmpty)
            {
                Remove(torch.FloorTileIndex);
                return;
            }
            if (_frames.Count == 0) return;
            if (!_drawn.TryGetValue(torch.FloorTileIndex, out Flame? flame))
            {
                flame = new Flame
                {
                    Instance = RenderingServer.InstanceCreate2(_quad, _scene.GetWorld3D().Scenario),
                    PhaseOffsetRaw = _phases.OffsetOf(torch.FloorTileIndex),
                };
                RenderingServer.InstanceGeometrySetCastShadowsSetting(flame.Instance, RenderingServer.ShadowCastingSetting.Off);
                RenderingServer.InstanceSetCustomAabb(flame.Instance, _box);
                _drawn.Add(torch.FloorTileIndex, flame);
            }
            flame.Torch = torch;
            flame.Point = ToGodot(RightHandedFrame.Point(ClassicTorchFlame.RenderPoint(torch, _scene._lattice)));
            RenderingServer.InstanceSetTransform(flame.Instance, new Transform3D(Basis.Identity, flame.Point));
            flame.SortStamp = -1;
        }

        public void Remove(int floorTileIndex)
        {
            if (_drawn.Remove(floorTileIndex, out Flame? flame)) RenderingServer.FreeRid(flame.Instance);
            _phases.Remove(floorTileIndex);
        }

        /// <summary>Unity's <c>AdvanceTorchAnimation</c> and <c>Draw</c>: the shared clock to now, each flame's frame.</summary>
        public void Draw(long milliseconds)
        {
            if (_frames.Count == 0) return;
            if (_started < 0) _started = milliseconds;
            _animation.AdvanceTo(new EffectTime(milliseconds - _started));
            foreach (KeyValuePair<int, Flame> entry in _drawn)
            {
                Flame flame = entry.Value;
                int frame = _animation.Frame(_frames.Count, flame.PhaseOffsetRaw);
                if (frame == flame.Frame) continue;
                flame.Frame = frame;
                RenderingServer.InstanceGeometrySetMaterialOverride(flame.Instance, _frames[frame].GetRid());
            }
        }

        public void Sort()
        {
            foreach (KeyValuePair<int, Flame> entry in _drawn)
            {
                Flame flame = entry.Value;
                if (flame.SortStamp == _scene._sortStamp) continue;
                flame.SortStamp = _scene._sortStamp;
                RenderingServer.InstanceSetPivotData(flame.Instance, _scene.ZeroOrderOffset(flame.Point, flame.Point), false);
            }
        }

        public void Free()
        {
            foreach (Flame flame in _drawn.Values) RenderingServer.FreeRid(flame.Instance);
            _drawn.Clear();
            if (_quad.IsValid) RenderingServer.FreeRid(_quad);
        }
    }
}
