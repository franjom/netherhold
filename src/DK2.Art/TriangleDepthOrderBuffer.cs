// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;

namespace DK2.Art;

/// <summary>Reusable stable depth sort. Returned runs remain valid until the next Sort call.</summary>
public sealed class TriangleDepthOrderBuffer
{
    public sealed class Run
    {
        public int MaterialIndex { get; internal set; }

        /// <summary>The camera depth (along the sort's forward) of the run's nearest triangle centroid.</summary>
        public float NearestDepth { get; internal set; }
        public IReadOnlyList<RenderTriangle> Triangles => Storage;
        internal List<RenderTriangle> Storage { get; } = [];
    }
    private readonly record struct Entry(RenderTriangle Triangle, int Order, float Depth);
    private Entry[] _entries = [];
    private Entry[] _scratch = [];
    private readonly List<Run> _runs = [];
    private readonly List<Run> _pool = [];

    private int _count;

    /// <summary>Sorts back to front and builds the runs; <paramref name="breaks"/> as in <see cref="Split"/>.</summary>
    public IReadOnlyList<Run> Sort(IReadOnlyList<Vector3> positions,
        IReadOnlyList<RenderTriangle> triangles, Vector3 cameraForward, ReadOnlySpan<float> breaks = default)
    {
        if (_entries.Length < triangles.Count)
        {
            int capacity = Math.Max(triangles.Count, _entries.Length * 2);
            Array.Resize(ref _entries, capacity);
            Array.Resize(ref _scratch, capacity);
        }
        for (int i = 0; i < triangles.Count; i++)
        {
            RenderTriangle t = triangles[i];
            _entries[i] = new Entry(t, i, Vector3.Dot(positions[t.A] + positions[t.B] + positions[t.C], cameraForward));
        }
        SortEntries(triangles.Count);
        _count = triangles.Count;
        return Split(breaks);
    }

    /// <summary>Rebuilds the runs of the last <see cref="Sort"/> without sorting again, also starting a
    /// run where the centroid depth first falls below each of <paramref name="breaks"/> (depths along the
    /// same forward, farthest first). An item drawn at a break depth then never shares a run with
    /// triangles on both sides of it, so <see cref="RunsBehind"/> places it exactly as DK2's one
    /// per-triangle blended list does (0x0058B9D0): a Prison fence of one material is otherwise one run
    /// that is either wholly before or wholly after a status flower inside it.</summary>
    public IReadOnlyList<Run> Split(ReadOnlySpan<float> breaks)
    {
        _runs.Clear();
        List<RenderTriangle>? current = null;
        int material = -1, next = 0;
        for (int i = 0; i < _count; i++)
        {
            RenderTriangle t = _entries[i].Triangle;
            float depth = _entries[i].Depth / 3f;
            bool crossed = false;
            while (next < breaks.Length && depth < breaks[next]) { next++; crossed = true; }
            if (current is null || material != t.MaterialIndex || crossed)
            {
                int slot = _runs.Count;
                material = t.MaterialIndex;
                if (slot == _pool.Count) _pool.Add(new Run());
                current = _pool[slot].Storage;
                current.Clear();
                _pool[slot].MaterialIndex = material;
                _runs.Add(_pool[slot]);
            }
            current.Add(t);
            _runs[^1].NearestDepth = depth;
        }
        return _runs;
    }

    /// <summary>How many leading runs of the last <see cref="Sort"/> lie wholly at or beyond
    /// <paramref name="depth"/> (centroid depth along the same forward): an item at that depth
    /// drawn after them and before the rest keeps DK2's one back-to-front blended list
    /// (0x0058B9D0, 0x00588D00). A run straddling the depth counts as in front.</summary>
    public int RunsBehind(float depth)
    {
        int low = 0, high = _runs.Count;
        while (low < high)
        {
            int middle = (low + high) >> 1;
            if (_runs[middle].NearestDepth >= depth) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private void SortEntries(int count)
    {
        // Merge into retained storage; Array.Sort's comparer adapter allocates per call.
        for (int width = 1; width < count; width *= 2)
        {
            for (int start = 0; start < count; start += width * 2)
            {
                int middle = Math.Min(start + width, count), end = Math.Min(start + width * 2, count);
                int a = start, b = middle;
                for (int output = start; output < end; output++)
                {
                    bool takeA = b == end || (a < middle && Compare(_entries[a], _entries[b]) <= 0);
                    _scratch[output] = takeA ? _entries[a++] : _entries[b++];
                }
            }
            (_entries, _scratch) = (_scratch, _entries);
        }
    }

    private static int Compare(Entry a, Entry b)
    {
        int depth = b.Depth.CompareTo(a.Depth);
        return depth != 0 ? depth : a.Order.CompareTo(b.Order);
    }
}
