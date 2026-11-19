// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;

namespace DK2.Presentation;

/// <summary>The stream's runs as drawables for a renderer that sorts its transparent pass by distance from the eye,
/// farthest first (Godot's, plan step 3q). DK2 draws every blended triangle from one back-to-front list (0x0058B9D0,
/// 0x00588D00); each run's key (<see cref="BlendedRunSlot{TMaterial}.Distance"/>) lets the renderer put the other blended
/// things between runs by their own distance. The key is the eye's distance to the run's nearest centroid, so a thing
/// draws over a run only when nearer than all of it (as <see cref="TriangleDepthOrderBuffer.RunsBehind"/> counts a
/// straddling run), made to fall by at least <see cref="KeyStep"/> down the stream so the runs keep its order. A tag
/// overlay's run is outside that chain: it draws before every other blended thing
/// (<see cref="BlendedTerrainStream.SortingOrder"/>), as a category the renderer gives it.
/// <para>A run draws from a slot of its material that keeps its vertices: the slot that last drew its first triangle when
/// free, else the material's first free one, so a camera move mostly rewrites indices only; a slot holding far more than
/// its run needs starts again, and a new collection of the geometry forgets every slot's vertices.</para></summary>
public sealed class BlendedRunSlots<TMaterial> where TMaterial : class
{
    /// <summary>The least fall of the key from one run to the next: far above a float's rounding at a level's distances
    /// (a 128-tile level stays under 2^8, whose step is 2^-15), and small against a tile.</summary>
    public const float KeyStep = 1f / 1024f;

    private readonly Dictionary<TMaterial, List<BlendedRunSlot<TMaterial>>> _pools = new();
    // By stream vertex, the slot that last drew it as a triangle's first corner, or -1.
    private int[] _owner = [];
    // The collected vertices, copied once a collection so the updates read an array.
    private BlendedVertex[] _vertices = [];
    private int _vertexCount;
    // This update's slot for each run, filled in two passes.
    private BlendedRunSlot<TMaterial>?[] _assigned = [];
    private readonly List<BlendedRunSlot<TMaterial>> _slots = new();
    private readonly List<BlendedRunSlot<TMaterial>> _drawn = new();
    private BlendedTerrainStream<TMaterial>? _stream;
    private long _revision = -1;

    /// <summary>Every slot made so far, by <see cref="BlendedRunSlot{TMaterial}.Id"/>; those not in <see cref="Drawn"/>
    /// are hidden.</summary>
    public IReadOnlyList<BlendedRunSlot<TMaterial>> Slots => _slots;

    /// <summary>The last <see cref="Update"/>'s slots, one per run of the stream, back to front.</summary>
    public IReadOnlyList<BlendedRunSlot<TMaterial>> Drawn => _drawn;

    /// <summary>The stream's vertices as of the last <see cref="Update"/>, which <see cref="BlendedRunSlot{TMaterial}.Vertices"/>
    /// number.</summary>
    public ReadOnlySpan<BlendedVertex> Vertices => new(_vertices, 0, _vertexCount);

    /// <summary>Fills a slot for each of <paramref name="stream"/>'s runs and keys it for <paramref name="eye"/>, in the
    /// stream's local space (the world, when its matrix is the identity). Call after each refresh that rebuilt the runs.
    /// Allocation-free once the slots have grown, and but for a new collection.</summary>
    public void Update(BlendedTerrainStream<TMaterial> stream, Vector3 eye)
    {
        if (stream.GeometryRevision != _revision || !ReferenceEquals(stream, _stream))
        {
            _revision = stream.GeometryRevision;
            _stream = stream;
            Collect(stream.Vertices);
            foreach (BlendedRunSlot<TMaterial> slot in _slots) slot.Forget();
        }
        foreach (BlendedRunSlot<TMaterial> slot in _slots) slot.Drawn = false;
        _drawn.Clear();
        IReadOnlyList<TriangleDepthOrderBuffer.Run> runs = stream.Runs;
        if (_assigned.Length < runs.Count) _assigned = new BlendedRunSlot<TMaterial>?[Math.Max(runs.Count, _assigned.Length * 2)];
        // First every run claims the slot that last drew its first corner, then the rest take free slots nobody claimed,
        // so a run appearing or splitting moves no other run off the vertices its slot holds.
        for (int i = 0; i < runs.Count; i++)
        {
            TriangleDepthOrderBuffer.Run run = runs[i];
            int id = _owner[run.Triangles[0].A];
            BlendedRunSlot<TMaterial>? owner = id >= 0 ? _slots[id] : null;
            if (owner is { Drawn: false } && ReferenceEquals(owner.Material, stream.Material(run.MaterialIndex)))
            {
                owner.Drawn = true;
                _assigned[i] = owner;
            }
            else _assigned[i] = null;
        }
        for (int i = 0; i < runs.Count; i++)
            _assigned[i] ??= TakeFree(stream.Material(runs[i].MaterialIndex));
        ReadOnlySpan<BlendedVertex> vertices = Vertices;
        float previous = float.PositiveInfinity;
        for (int i = 0; i < runs.Count; i++)
        {
            TriangleDepthOrderBuffer.Run run = runs[i];
            BlendedRunSlot<TMaterial> slot = _assigned[i]!;
            _assigned[i] = null;
            slot.TagOverlay = stream.IsTagOverlay(run.MaterialIndex);
            slot.Fill(run.Triangles, vertices, eye);
            // A clamped key differs from the run's own distance only where the distance off the view axis disagrees with
            // the stream's order along it, and by at most KeyStep a run (drift accumulates only over such a series).
            if (slot.TagOverlay) slot.Distance = slot.NearestDistance;
            else
            {
                slot.Distance = Math.Min(slot.NearestDistance, previous - KeyStep);
                previous = slot.Distance;
            }
            _drawn.Add(slot);
            for (int t = 0; t < run.Triangles.Count; t++) _owner[run.Triangles[t].A] = slot.Id;
        }
    }

    private void Collect(IReadOnlyList<BlendedVertex> vertices)
    {
        _vertexCount = vertices.Count;
        if (_vertices.Length < _vertexCount)
        {
            _vertices = new BlendedVertex[_vertexCount];
            _owner = new int[_vertexCount];
        }
        for (int i = 0; i < _vertexCount; i++) _vertices[i] = vertices[i];
        Array.Fill(_owner, -1, 0, _vertexCount);
    }

    private BlendedRunSlot<TMaterial> TakeFree(TMaterial material)
    {
        if (!_pools.TryGetValue(material, out List<BlendedRunSlot<TMaterial>>? pool))
        {
            pool = new List<BlendedRunSlot<TMaterial>>();
            _pools.Add(material, pool);
        }
        foreach (BlendedRunSlot<TMaterial> free in pool)
            if (!free.Drawn)
            {
                free.Drawn = true;
                return free;
            }
        var made = new BlendedRunSlot<TMaterial>(_slots.Count, material) { Drawn = true };
        _slots.Add(made);
        pool.Add(made);
        return made;
    }
}

/// <summary>One drawable of <see cref="BlendedRunSlots{TMaterial}"/>: a material's vertices, numbered in the order the
/// slot first needed them, and the indices of the run it draws now, back to front.</summary>
public sealed class BlendedRunSlot<TMaterial> where TMaterial : class
{
    /// <summary>How many vertices past twice the run's own a slot may hold before it starts again from the run's.</summary>
    public const int SpareVertices = 64;

    // Sized for a run of a few dozen triangles and its spare vertices, so a slot seldom grows once it exists.
    private readonly Dictionary<int, int> _slotVertexOf = new(256);
    private readonly List<int> _vertices = new(256);
    private int[] _indices = new int[384];
    private int[] _touched = new int[256];
    private int _epoch;
    private bool _forgotten = true;

    internal BlendedRunSlot(int id, TMaterial material)
    {
        Id = id;
        Material = material;
    }

    /// <summary>The slot's number, in the order <see cref="BlendedRunSlots{TMaterial}"/> made them.</summary>
    public int Id { get; }

    public TMaterial Material { get; }

    /// <summary>Whether the run is a tag overlay's, which draws before every other blended thing.</summary>
    public bool TagOverlay { get; internal set; }

    /// <summary>The run's sort key: its distance from the eye, which the renderer draws farthest first.</summary>
    public float Distance { get; internal set; }

    /// <summary>The distance from the eye to the run's nearest centroid; <see cref="Distance"/> is this distance unless
    /// the stream's order clamped it.</summary>
    public float NearestDistance { get; private set; }

    /// <summary>Whether the slot draws a run of the last update.</summary>
    public bool Drawn { get; internal set; }

    /// <summary>The stream vertex each slot vertex holds.</summary>
    public IReadOnlyList<int> Vertices => _vertices;

    public int VertexCount => _vertices.Count;

    /// <summary>The first slot vertex the last update added: the renderer uploads from here to <see cref="VertexCount"/>
    /// (0 when the slot started again).</summary>
    public int FirstNewVertex { get; private set; }

    /// <summary>The run's triangles as slot vertex indices, three a triangle, in the run's order.</summary>
    public ReadOnlySpan<int> Indices => new(_indices, 0, IndexCount);

    public int IndexCount { get; private set; }

    /// <summary>Whether <see cref="Indices"/> differ from the update before, or the slot started again.</summary>
    public bool IndicesChanged { get; private set; }

    /// <summary>Drops every vertex: the stream's numbering changed.</summary>
    internal void Forget()
    {
        _slotVertexOf.Clear();
        _vertices.Clear();
        _forgotten = true;
    }

    /// <summary>Takes the run's triangles and measures its centroid nearest to <paramref name="eye"/>.</summary>
    internal void Fill(IReadOnlyList<RenderTriangle> triangles, ReadOnlySpan<BlendedVertex> vertices, Vector3 eye)
    {
        int count = triangles.Count * 3;
        if (_indices.Length < count)
        {
            int capacity = Math.Max(48, _indices.Length);
            while (capacity < count) capacity <<= 1;
            Array.Resize(ref _indices, capacity);
        }
        bool changed = _forgotten || count != IndexCount;
        _forgotten = false;
        int first = _vertices.Count;
        int own = Index(triangles, ref changed);
        if (_vertices.Count > (2 * own) + SpareVertices)
        {
            // Holding far more than the run needs: start again from the run's own vertices.
            _slotVertexOf.Clear();
            _vertices.Clear();
            first = 0;
            changed = true;
            Index(triangles, ref changed);
        }
        FirstNewVertex = first;
        IndexCount = count;
        IndicesChanged = changed;
        float nearest = float.PositiveInfinity;
        for (int t = 0; t < triangles.Count; t++)
        {
            RenderTriangle triangle = triangles[t];
            Vector3 centroid = (vertices[triangle.A].Position + vertices[triangle.B].Position + vertices[triangle.C].Position) / 3f;
            nearest = Math.Min(nearest, Vector3.DistanceSquared(centroid, eye));
        }
        NearestDistance = MathF.Sqrt(nearest);
    }

    /// <summary>Writes the indices, adding the vertices the slot lacks, and returns how many distinct vertices the run uses.</summary>
    private int Index(IReadOnlyList<RenderTriangle> triangles, ref bool changed)
    {
        _epoch++;
        int own = 0;
        for (int t = 0; t < triangles.Count; t++)
        {
            RenderTriangle triangle = triangles[t];
            changed |= Put((t * 3) + 0, SlotVertex(triangle.A, ref own));
            changed |= Put((t * 3) + 1, SlotVertex(triangle.B, ref own));
            changed |= Put((t * 3) + 2, SlotVertex(triangle.C, ref own));
        }
        return own;
    }

    private bool Put(int at, int index)
    {
        if (_indices[at] == index) return false;
        _indices[at] = index;
        return true;
    }

    private int SlotVertex(int streamVertex, ref int own)
    {
        if (!_slotVertexOf.TryGetValue(streamVertex, out int index))
        {
            index = _vertices.Count;
            _slotVertexOf.Add(streamVertex, index);
            _vertices.Add(streamVertex);
            if (_touched.Length <= index) Array.Resize(ref _touched, Math.Max(64, _touched.Length * 2));
        }
        if (_touched[index] != _epoch)
        {
            _touched[index] = _epoch;
            own++;
        }
        return index;
    }
}
