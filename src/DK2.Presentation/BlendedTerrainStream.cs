// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A vertex colour as stored bytes.</summary>
public readonly record struct VertexColour(byte R, byte G, byte B, byte A)
{
    public static VertexColour White => new(255, 255, 255, 255);
}

/// <summary>One vertex of the blended stream, in the stream's local space of the renderers' frame.</summary>
public readonly record struct BlendedVertex(Vector3 Position, Vector3 Normal, Vector2 Uv, Vector2 LightAnchor, VertexColour Colour)
{
    /// <summary>Terrain tag/tint sources; unused for models and effects.</summary>
    public Vector2 StateSources { get; init; } = new(-1);
}

/// <summary>The stream's markers and its sorting-order rule, shared by every material type.</summary>
public static class BlendedTerrainStream
{
    public static class Markers
    {
        public static readonly SimulationMarker Sort = SimulationMarker.Named("TransparentTerrain.Sort");
        public static readonly SimulationMarker Cull = SimulationMarker.Named("TransparentTerrain.Cull");
        public static readonly SimulationMarker DepthOrder = SimulationMarker.Named("TransparentTerrain.DepthOrder");
    }

    /// <summary>The order run <paramref name="run"/> draws at, lowest first: a tag overlay's precedes every actor and
    /// flower (order 0) and all other blended terrain. The rest follow the stream from 1.</summary>
    public static int SortingOrder(int run, bool tagOverlay) => tagOverlay ? run - short.MaxValue : run + 1;
}

/// <summary>Every chunk's blended terrain triangles, and this frame's blended effect meshes, as one back-to-front stream
/// across chunks and materials: culled to the view, sorted by <see cref="TriangleDepthOrderBuffer"/> and split into runs
/// of one material. Status flowers join through
/// <see cref="PlaceItems"/>. A material is any reference the adapter draws with, told apart by its default equality.
/// The adapter uploads <see cref="Runs"/> over <see cref="Vertices"/> in <see cref="BlendedTerrainStream.SortingOrder"/>.</summary>
public sealed class BlendedTerrainStream<TMaterial> where TMaterial : class
{
    private sealed class Source(BlendedVertex[] vertices, TMaterial[] materials, int[][] triangles)
    {
        public readonly BlendedVertex[] Vertices = vertices;
        public readonly TMaterial[] Materials = materials;
        public readonly int[][] Triangles = triangles;
    }

    private sealed class Effect
    {
        public TMaterial? Material;
        public BlendedVertex[] Vertices = [];
        public int[] Indices = [];
    }

    private readonly Func<TMaterial, bool> _isTagOverlay;
    // By chunk, ascending; read by index, as enumerating a sorted dictionary allocates its walk.
    private readonly SortedList<int, Source> _sources = new();

    private int _chunk = -1;
    private IReadOnlyList<BlendedVertex>? _chunkVertices;
    private readonly Dictionary<int, int> _remap = new();
    private readonly List<BlendedVertex> _kept = new();
    private readonly List<TMaterial> _chunkMaterials = new();
    private readonly List<int[]> _chunkTriangles = new();

    private readonly List<Effect> _effects = new();
    private int _effectCount;

    private readonly List<BlendedVertex> _vertices = new();
    private readonly List<TMaterial> _materials = new();
    private readonly List<bool> _overlays = new();
    private readonly Dictionary<TMaterial, int> _slots = new();
    private readonly List<RenderTriangle> _triangles = new();
    private readonly List<RenderTriangle> _visible = new();
    private readonly List<Vector3> _world = new();
    private readonly TriangleDepthOrderBuffer _sort = new();
    private IReadOnlyList<TriangleDepthOrderBuffer.Run> _runs = Array.Empty<TriangleDepthOrderBuffer.Run>();

    private bool _dirty = true;
    private Vector3 _lastForward;
    private Matrix4x4 _lastMatrix;
    private Matrix4x4 _lastViewProjection;

    // Status-flower depths that split runs (PlaceItems), farthest first.
    private float[] _breaks = [];
    private float[] _scratchBreaks = [];
    private float[] _itemDepths = [];
    private int _breakCount, _itemCount;
    private bool _breaksChanged;

    /// <param name="isTagOverlay">Whether a material is a tag overlay (the dig tag's or the tutorial's additive pass);
    /// asked once per material each time the stream is collected.</param>
    public BlendedTerrainStream(Func<TMaterial, bool> isTagOverlay) => _isTagOverlay = isTagOverlay;

    /// <summary>Where to time the refresh's spans (<see cref="BlendedTerrainStream.Markers"/>).</summary>
    public ISimulationProfiler Profiler { get; set; } = NullSimulationProfiler.Instance;

    /// <summary>The runs of the last <see cref="Refresh"/> or <see cref="PlaceItems"/>, back to front.</summary>
    public IReadOnlyList<TriangleDepthOrderBuffer.Run> Runs => _runs;

    /// <summary>The collected vertices the runs index, chunks in ascending order and then the effects.</summary>
    public IReadOnlyList<BlendedVertex> Vertices => _vertices;

    /// <summary>How many materials the collected stream uses; a run's material index is below this.</summary>
    public int MaterialCount => _materials.Count;

    /// <summary>Counts the collections; it changes whenever <see cref="Vertices"/> may have.</summary>
    public long GeometryRevision { get; private set; }

    /// <summary>Whether the last <see cref="Refresh"/> that rebuilt the runs collected the geometry again.</summary>
    public bool GeometryChanged { get; private set; }

    public TMaterial Material(int index) => _materials[index];

    public bool IsTagOverlay(int materialIndex) => _overlays[materialIndex];

    /// <summary><see cref="BlendedTerrainStream.SortingOrder"/> of run <paramref name="run"/> of <see cref="Runs"/>.</summary>
    public int SortingOrder(int run) => BlendedTerrainStream.SortingOrder(run, _overlays[_runs[run].MaterialIndex]);

    /// <summary>Starts replacing <paramref name="chunk"/>'s blended triangles; <paramref name="vertices"/> are the whole
    /// chunk's, read only where a blended triangle uses them, until <see cref="EndChunk"/>.</summary>
    public void BeginChunk(int chunk, IReadOnlyList<BlendedVertex> vertices)
    {
        _chunk = chunk;
        _chunkVertices = vertices;
        // Only the vertices the blended triangles use, renumbered in first-use order: a chunk's opaque bulk would
        // otherwise be copied here and transformed on every camera turn. The depth sort breaks ties by triangle
        // order, not vertex number, so drawing is unchanged.
        _remap.Clear();
        _kept.Clear();
        _chunkMaterials.Clear();
        _chunkTriangles.Clear();
    }

    /// <summary>One blended material's triangles, as indices into the chunk's vertices; an empty list adds nothing.</summary>
    public void AddChunkTriangles(TMaterial material, IReadOnlyList<int> indices)
    {
        if (indices.Count == 0)
        {
            return;
        }

        IReadOnlyList<BlendedVertex> vertices = _chunkVertices ?? throw new InvalidOperationException("No chunk begun.");
        int[] kept = new int[indices.Count];
        for (int i = 0; i < kept.Length; i++)
        {
            int vertex = indices[i];
            if (!_remap.TryGetValue(vertex, out int local))
            {
                local = _kept.Count;
                _remap.Add(vertex, local);
                _kept.Add(vertices[vertex]);
            }
            kept[i] = local;
        }
        _chunkMaterials.Add(material);
        _chunkTriangles.Add(kept);
    }

    /// <summary>Stores the chunk's blended triangles; a chunk with none before or after changes nothing.</summary>
    public void EndChunk()
    {
        _chunkVertices = null;
        if (_chunkTriangles.Count == 0)
        {
            if (!_sources.Remove(_chunk))
            {
                return;
            }
        }
        else
        {
            _sources[_chunk] = new Source([.. _kept], [.. _chunkMaterials], [.. _chunkTriangles]);
        }

        _dirty = true;
    }

    /// <summary>Forgets every chunk and effect.</summary>
    public void Clear()
    {
        _sources.Clear();
        _effects.Clear();
        _effectCount = 0;
        _dirty = true;
    }

    public void BeginEffectFrame() => _effectCount = 0;

    /// <summary>One blended effect mesh for this frame, in the stream's local space (the adapter fills in absent
    /// normals as up, UVs and light anchors as zero, colours as white); a frame that repeats the last one's effects in
    /// the same order keeps the collected stream.</summary>
    public void AddEffect(TMaterial material, ReadOnlySpan<BlendedVertex> vertices, ReadOnlySpan<int> indices)
    {
        if (_effectCount == _effects.Count)
        {
            _effects.Add(new Effect());
        }

        Effect effect = _effects[_effectCount++];
        if (ReferenceEquals(effect.Material, material) && vertices.SequenceEqual(new ReadOnlySpan<BlendedVertex>(effect.Vertices))
            && indices.SequenceEqual(new ReadOnlySpan<int>(effect.Indices)))
        {
            return;
        }

        effect.Material = material;
        if (effect.Vertices.Length != vertices.Length)
        {
            effect.Vertices = new BlendedVertex[vertices.Length];
        }

        vertices.CopyTo(effect.Vertices);
        if (effect.Indices.Length != indices.Length)
        {
            effect.Indices = new int[indices.Length];
        }

        indices.CopyTo(effect.Indices);
        _dirty = true;
    }

    /// <summary>The next effect of this frame is the one in the same place last frame, unchanged; the adapter's own
    /// comparison of its inputs found nothing new. Needs that effect to exist.</summary>
    public void RepeatEffect()
    {
        if (_effectCount == _effects.Count)
        {
            throw new InvalidOperationException("No effect to repeat.");
        }

        _effectCount++;
    }

    public void EndEffectFrame()
    {
        if (_effectCount == _effects.Count)
        {
            return;
        }

        _effects.RemoveRange(_effectCount, _effects.Count - _effectCount);
        _dirty = true;
    }

    /// <summary>Whether <see cref="Refresh"/> would rebuild the runs for this camera, without building its frustum.</summary>
    public bool NeedsRefresh(Vector3 forward, in Matrix4x4 localToWorld, in Matrix4x4 viewProjection) =>
        _dirty || _breaksChanged || !Close(forward, _lastForward) || !ViewFrustum.ApproximatelyEqual(localToWorld, _lastMatrix)
        || !ViewFrustum.ApproximatelyEqual(viewProjection, _lastViewProjection);

    /// <summary>Rebuilds <see cref="Runs"/> when anything changed since the last call: the geometry, the forward, the
    /// stream's <paramref name="localToWorld"/> or the view (each compared as Unity's <c>==</c> compares), or the items
    /// of <see cref="PlaceItems"/> (then only re-split). True when the runs were rebuilt.</summary>
    /// <param name="forward">The camera's unit forward, which the depth is measured along.</param>
    public bool Refresh(Vector3 forward, in Matrix4x4 localToWorld, in ViewFrustum frustum)
    {
        bool matrixMoved = !ViewFrustum.ApproximatelyEqual(localToWorld, _lastMatrix);
        bool moved = _dirty || !Close(forward, _lastForward) || matrixMoved
            || !ViewFrustum.ApproximatelyEqual(frustum.ViewProjection, _lastViewProjection);
        if (!moved && !_breaksChanged)
        {
            return false;
        }

        _breaksChanged = false;
        if (!moved)
        {
            // Only the items moved: re-split the last order, no collect, cull or sort.
            using (Profiler.Scope(BlendedTerrainStream.Markers.DepthOrder))
            {
                _runs = _sort.Split(new ReadOnlySpan<float>(_breaks, 0, _breakCount));
            }

            GeometryChanged = false;
            return true;
        }
        using ProfileScope scope = Profiler.Scope(BlendedTerrainStream.Markers.Sort);
        bool geometryChanged = _dirty || (_effects.Count != 0 && matrixMoved);
        if (geometryChanged)
        {
            Collect();
            GeometryRevision++;
        }
        if (geometryChanged || matrixMoved)
        {
            _world.Clear();
            Matrix4x4 m = localToWorld;
            foreach (BlendedVertex vertex in _vertices)
            {
                // Unity's Matrix4x4.MultiplyPoint3x4, term by term.
                Vector3 p = vertex.Position;
                _world.Add(new Vector3(m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z + m.M14,
                    m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z + m.M24, m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z + m.M34));
            }
        }
        using (Profiler.Scope(BlendedTerrainStream.Markers.Cull))
        {
            _visible.Clear();
            foreach (RenderTriangle triangle in _triangles)
            {
                if (!frustum.Excludes(_world[triangle.A], _world[triangle.B], _world[triangle.C]))
                {
                    _visible.Add(triangle);
                }
            }
        }
        using (Profiler.Scope(BlendedTerrainStream.Markers.DepthOrder))
        {
            _runs = _sort.Sort(_world, _visible, forward, new ReadOnlySpan<float>(_breaks, 0, _breakCount));
        }

        GeometryChanged = geometryChanged;
        _dirty = false;
        _lastForward = forward;
        _lastMatrix = localToWorld;
        _lastViewProjection = frustum.ViewProjection;
        return true;
    }

    /// <summary>Joins blended items (status flowers) at <paramref name="worlds"/> into this frame's stream:
    /// <see cref="SetItems"/>, <see cref="Refresh"/> (splitting any run an item sits inside,
    /// <see cref="TriangleDepthOrderBuffer.Split"/>) and <see cref="ItemOrders"/>. Call once a frame after the camera is
    /// placed and before the render; the render's own refresh then keeps these runs. True when the runs were rebuilt.</summary>
    public bool PlaceItems(Vector3 forward, in Matrix4x4 localToWorld, in ViewFrustum frustum, IReadOnlyList<Vector3> worlds,
        List<int> orders)
    {
        SetItems(forward, worlds);
        bool rebuilt = Refresh(forward, localToWorld, frustum);
        ItemOrders(orders);
        return rebuilt;
    }

    /// <summary>The items' depths along <paramref name="forward"/>; the next <see cref="Refresh"/> re-splits the runs
    /// at them when they changed.</summary>
    public void SetItems(Vector3 forward, IReadOnlyList<Vector3> worlds)
    {
        int count = worlds.Count;
        if (_itemDepths.Length < count)
        {
            _itemDepths = new float[NextPowerOfTwo(count)];
            _scratchBreaks = new float[_itemDepths.Length];
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 world = worlds[i];
            // Unity's Vector3.Dot, term by term.
            _itemDepths[i] = world.X * forward.X + world.Y * forward.Y + world.Z * forward.Z;
            _scratchBreaks[i] = _itemDepths[i];
        }
        Array.Sort(_scratchBreaks, 0, count);
        Array.Reverse(_scratchBreaks, 0, count);
        _itemCount = count;
        bool same = count == _breakCount;
        for (int i = 0; same && i < count; i++)
        {
            same = _scratchBreaks[i] == _breaks[i];
        }

        if (same)
        {
            return;
        }

        if (_breaks.Length < count)
        {
            _breaks = new float[_scratchBreaks.Length];
        }

        Array.Copy(_scratchBreaks, _breaks, count);
        _breakCount = count;
        _breaksChanged = true;
    }

    /// <summary>Each item of the last <see cref="SetItems"/>: its sorting order, the runs wholly behind it
    /// (<see cref="TriangleDepthOrderBuffer.RunsBehind"/>).</summary>
    public void ItemOrders(List<int> orders)
    {
        orders.Clear();
        for (int i = 0; i < _itemCount; i++)
        {
            orders.Add(_sort.RunsBehind(_itemDepths[i]));
        }
    }

    private void Collect()
    {
        _vertices.Clear();
        _materials.Clear();
        _overlays.Clear();
        _triangles.Clear();
        _slots.Clear();
        IList<Source> sources = _sources.Values;
        for (int s = 0; s < sources.Count; s++)
        {
            Source source = sources[s];
            int start = _vertices.Count;
            _vertices.AddRange(source.Vertices);
            for (int material = 0; material < source.Materials.Length; material++)
            {
                int slot = SlotOf(source.Materials[material]);
                int[] indices = source.Triangles[material];
                for (int i = 0; i < indices.Length; i += 3)
                {
                    _triangles.Add(new RenderTriangle(start + indices[i], start + indices[i + 1], start + indices[i + 2], slot));
                }
            }
        }
        foreach (Effect effect in _effects)
        {
            int start = _vertices.Count;
            _vertices.AddRange(effect.Vertices);
            int slot = SlotOf(effect.Material!);
            for (int i = 0; i < effect.Indices.Length; i += 3)
            {
                _triangles.Add(new RenderTriangle(start + effect.Indices[i], start + effect.Indices[i + 1],
                    start + effect.Indices[i + 2], slot));
            }
        }
    }

    private int SlotOf(TMaterial material)
    {
        if (_slots.TryGetValue(material, out int slot))
        {
            return slot;
        }

        slot = _materials.Count;
        _slots.Add(material, slot);
        _materials.Add(material);
        _overlays.Add(_isTagOverlay(material));
        return slot;
    }

    // Unity's Vector3 ==: the squared length of the difference below kEpsilon squared.
    private static bool Close(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return x * x + y * y + z * z < 9.99999944E-11f;
    }

    // Unity's Mathf.NextPowerOfTwo for a positive count.
    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }
}
