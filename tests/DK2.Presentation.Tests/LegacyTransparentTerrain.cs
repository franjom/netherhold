// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Art;

namespace DK2.Presentation.Tests;

/// <summary>A material for the stream tests: Unity's transparent render queue and DK2TagOverlay tag as two flags.</summary>
internal sealed class TestMaterial(string name, bool transparent = true, bool tagOverlay = false)
{
    public string Name { get; } = name;
    public bool Transparent { get; } = transparent;
    public bool TagOverlay { get; } = tagOverlay;
    public override string ToString() => Name;
}

/// <summary>The Unity adapter's <c>TransparentTerrain</c> as it stood at 58bc1806 (SetChunk, Clear, Collect,
/// AddEffectMesh, EndEffectFrame, PlaceItems, Refresh and the sorting order in Upload), transcribed before it moved to
/// <see cref="BlendedTerrainStream{TMaterial}"/>. Unity's vectors are System.Numerics ones with Unity's approximate
/// <c>==</c>, its matrices are <see cref="Matrix4x4"/> in Unity's layout, <c>GeometryUtility</c>'s planes are passed in,
/// and an upload is recorded (<see cref="Uploaded"/>) instead of drawn.</summary>
internal sealed class LegacyTransparentTerrain
{
    private sealed class Source
    {
        public Vector3[] Positions = [];
        public TestMaterial[] Materials = [];
        public int[][] Triangles = [];
    }

    private sealed class EffectMesh
    {
        public TestMaterial? Material;
        public Matrix4x4 Transform;
        public Vector3[] Positions = [];
        public int[] Indices = [];
    }

    public sealed record Upload(bool GeometryChanged, IReadOnlyList<(TestMaterial Material, int SortingOrder, RenderTriangle[] Triangles)> Runs,
        Vector3[] Positions);

    private readonly List<EffectMesh> _effects = new();
    private int _effectCount;
    private readonly SortedDictionary<int, Source> _sources = new();
    private readonly List<Vector3> _positions = new();
    private readonly List<TestMaterial> _materials = new();
    private readonly List<RenderTriangle> _triangles = new();
    private readonly List<RenderTriangle> _visibleTriangles = new();
    private readonly List<Vector3> _worldPositions = new();
    private readonly TriangleDepthOrderBuffer _sort = new();
    private bool _dirty = true;
    private Vector3 _lastForward;
    private Matrix4x4 _lastMatrix;
    private Matrix4x4 _lastCullingMatrix;
    private float[] _breaks = [];
    private float[] _scratchBreaks = [];
    private float[] _itemDepths = [];
    private int _breakCount;
    private bool _breaksChanged;

    public Upload? Uploaded { get; private set; }

    public void BeginEffectFrame() => _effectCount = 0;

    public void AddEffectMesh(Vector3[] positions, int[] indices, TestMaterial material, Matrix4x4 worldTransform)
    {
        if (_effectCount == _effects.Count) _effects.Add(new EffectMesh());
        EffectMesh effect = _effects[_effectCount++];
        if (effect.Material == material && effect.Transform.Equals(worldTransform)
            && positions.AsSpan().SequenceEqual(effect.Positions) && indices.AsSpan().SequenceEqual(effect.Indices)) return;
        effect.Material = material; effect.Transform = worldTransform;
        effect.Positions = positions.ToArray(); effect.Indices = indices.ToArray();
        _dirty = true;
    }

    public void EndEffectFrame()
    {
        if (_effectCount == _effects.Count) return;
        _effects.RemoveRange(_effectCount, _effects.Count - _effectCount);
        _dirty = true;
    }

    public void SetChunk(int chunk, List<Vector3> positions, Dictionary<TestMaterial, List<int>> byMaterial)
    {
        var materials = new List<TestMaterial>();
        var triangles = new List<int[]>();
        var remap = new Dictionary<int, int>();
        var keptPositions = new List<Vector3>();
        foreach (KeyValuePair<TestMaterial, List<int>> entry in byMaterial)
            if (entry.Value.Count != 0 && entry.Key.Transparent)
            {
                int[] kept = new int[entry.Value.Count];
                for (int i = 0; i < kept.Length; i++)
                {
                    int vertex = entry.Value[i];
                    if (!remap.TryGetValue(vertex, out int local))
                    {
                        local = keptPositions.Count;
                        remap.Add(vertex, local);
                        keptPositions.Add(positions[vertex]);
                    }
                    kept[i] = local;
                }
                materials.Add(entry.Key);
                triangles.Add(kept);
            }
        if (triangles.Count == 0)
        {
            if (!_sources.Remove(chunk)) return;
        }
        else _sources[chunk] = new Source
        {
            Positions = keptPositions.ToArray(), Materials = materials.ToArray(), Triangles = triangles.ToArray(),
        };
        _dirty = true;
    }

    public void Clear()
    {
        _sources.Clear();
        _effects.Clear(); _effectCount = 0;
        _dirty = true;
    }

    private void Collect(Matrix4x4 worldToLocal)
    {
        _positions.Clear(); _materials.Clear(); _triangles.Clear();
        var slots = new Dictionary<TestMaterial, int>();
        foreach (Source source in _sources.Values)
        {
            int start = _positions.Count;
            _positions.AddRange(source.Positions);
            for (int material = 0; material < source.Materials.Length; material++)
            {
                if (!slots.TryGetValue(source.Materials[material], out int slot))
                {
                    slot = _materials.Count;
                    slots.Add(source.Materials[material], slot);
                    _materials.Add(source.Materials[material]);
                }
                int[] indices = source.Triangles[material];
                for (int i = 0; i < indices.Length; i += 3)
                    _triangles.Add(new RenderTriangle(start + indices[i], start + indices[i + 1], start + indices[i + 2], slot));
            }
        }
        foreach (EffectMesh effect in _effects)
        {
            int start = _positions.Count;
            Matrix4x4 local = UnityMultiply(worldToLocal, effect.Transform);
            for (int i = 0; i < effect.Positions.Length; i++) _positions.Add(MultiplyPoint3x4(local, effect.Positions[i]));
            if (!slots.TryGetValue(effect.Material!, out int slot))
            {
                slot = _materials.Count; slots.Add(effect.Material!, slot); _materials.Add(effect.Material!);
            }
            for (int i = 0; i < effect.Indices.Length; i += 3)
                _triangles.Add(new RenderTriangle(start + effect.Indices[i], start + effect.Indices[i + 1],
                    start + effect.Indices[i + 2], slot));
        }
    }

    public void PlaceItems(Vector3 forward, Matrix4x4 matrix, Matrix4x4 worldToLocal, Matrix4x4 culling, Plane[] frustum,
        List<Vector3> worlds, List<int> orders)
    {
        int count = worlds.Count;
        if (_itemDepths.Length < count) { _itemDepths = new float[NextPowerOfTwo(count)]; _scratchBreaks = new float[_itemDepths.Length]; }
        for (int i = 0; i < count; i++)
        {
            _itemDepths[i] = Dot(worlds[i], forward);
            _scratchBreaks[i] = _itemDepths[i];
        }
        Array.Sort(_scratchBreaks, 0, count);
        Array.Reverse(_scratchBreaks, 0, count);
        bool same = count == _breakCount;
        for (int i = 0; same && i < count; i++) same = _scratchBreaks[i] == _breaks[i];
        if (!same)
        {
            if (_breaks.Length < count) _breaks = new float[_scratchBreaks.Length];
            Array.Copy(_scratchBreaks, _breaks, count);
            _breakCount = count;
            _breaksChanged = true;
        }
        Refresh(forward, matrix, worldToLocal, culling, frustum);
        orders.Clear();
        for (int i = 0; i < count; i++) orders.Add(_sort.RunsBehind(_itemDepths[i]));
    }

    /// <summary>Unity's Refresh; <paramref name="matrix"/> is the stream's localToWorld and <paramref name="worldToLocal"/>
    /// its inverse (Unity's <c>transform.worldToLocalMatrix</c>).</summary>
    public void Refresh(Vector3 forward, Matrix4x4 matrix, Matrix4x4 worldToLocal, Matrix4x4 culling, Plane[] frustum)
    {
        Uploaded = null;
        bool moved = _dirty || !Same(forward, _lastForward) || !ViewFrustum.ApproximatelyEqual(matrix, _lastMatrix)
            || !ViewFrustum.ApproximatelyEqual(culling, _lastCullingMatrix);
        if (!moved && !_breaksChanged) return;
        _breaksChanged = false;
        if (!moved)
        {
            Record(_sort.Split(new ReadOnlySpan<float>(_breaks, 0, _breakCount)), geometryChanged: false);
            return;
        }
        bool matrixMoved = !ViewFrustum.ApproximatelyEqual(matrix, _lastMatrix);
        bool geometryChanged = _dirty || (_effects.Count != 0 && matrixMoved);
        if (geometryChanged) Collect(worldToLocal);
        if (geometryChanged || matrixMoved)
        {
            _worldPositions.Clear();
            foreach (Vector3 local in _positions) _worldPositions.Add(MultiplyPoint3x4(matrix, local));
        }
        _visibleTriangles.Clear();
        foreach (RenderTriangle triangle in _triangles)
        {
            Vector3 a = _worldPositions[triangle.A], b = _worldPositions[triangle.B], c = _worldPositions[triangle.C];
            bool outside = false;
            foreach (Plane plane in frustum)
            {
                Vector3 n = plane.Normal;
                if (n.X * a.X + n.Y * a.Y + n.Z * a.Z + plane.D < -.0001f
                    && n.X * b.X + n.Y * b.Y + n.Z * b.Z + plane.D < -.0001f
                    && n.X * c.X + n.Y * c.Y + n.Z * c.Z + plane.D < -.0001f)
                { outside = true; break; }
            }
            if (!outside) _visibleTriangles.Add(triangle);
        }
        IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered = _sort.Sort(_worldPositions, _visibleTriangles, forward,
            new ReadOnlySpan<float>(_breaks, 0, _breakCount));
        Record(ordered, geometryChanged);
        _dirty = false;
        _lastForward = forward;
        _lastMatrix = matrix;
        _lastCullingMatrix = culling;
    }

    private void Record(IReadOnlyList<TriangleDepthOrderBuffer.Run> ordered, bool geometryChanged)
    {
        var runs = new List<(TestMaterial, int, RenderTriangle[])>();
        for (int i = 0; i < ordered.Count; i++)
        {
            TestMaterial material = _materials[ordered[i].MaterialIndex];
            int sortingOrder = material.TagOverlay ? i - short.MaxValue : i + 1;
            runs.Add((material, sortingOrder, ordered[i].Triangles.ToArray()));
        }
        Uploaded = new Upload(geometryChanged, runs, _positions.ToArray());
    }

    private static bool Same(Vector3 a, Vector3 b)
    {
        float x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return x * x + y * y + z * z < 9.99999944E-11f;
    }

    private static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3 MultiplyPoint3x4(in Matrix4x4 m, Vector3 p) => new(m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z + m.M14,
        m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z + m.M24, m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z + m.M34);

    // Unity's Matrix4x4 * Matrix4x4, each element a sum of four products in index order.
    public static Matrix4x4 UnityMultiply(in Matrix4x4 a, in Matrix4x4 b)
    {
        Span<float> l = stackalloc float[16], r = stackalloc float[16], o = stackalloc float[16];
        Write(a, l); Write(b, r);
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                o[row * 4 + column] = l[row * 4] * r[column] + l[row * 4 + 1] * r[4 + column] + l[row * 4 + 2] * r[8 + column]
                    + l[row * 4 + 3] * r[12 + column];
        return new Matrix4x4(o[0], o[1], o[2], o[3], o[4], o[5], o[6], o[7], o[8], o[9], o[10], o[11], o[12], o[13], o[14], o[15]);
    }

    private static void Write(in Matrix4x4 m, Span<float> into)
    {
        into[0] = m.M11; into[1] = m.M12; into[2] = m.M13; into[3] = m.M14;
        into[4] = m.M21; into[5] = m.M22; into[6] = m.M23; into[7] = m.M24;
        into[8] = m.M31; into[9] = m.M32; into[10] = m.M33; into[11] = m.M34;
        into[12] = m.M41; into[13] = m.M42; into[14] = m.M43; into[15] = m.M44;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value) power <<= 1;
        return power;
    }
}
