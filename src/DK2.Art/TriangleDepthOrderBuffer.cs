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
        public IReadOnlyList<RenderTriangle> Triangles => Storage;
        internal List<RenderTriangle> Storage { get; } = [];
    }
    private readonly record struct Entry(RenderTriangle Triangle, int Order, float Depth);
    private Entry[] _entries = [];
    private Entry[] _scratch = [];
    private readonly List<Run> _runs = [];
    private readonly List<Run> _pool = [];

    public IReadOnlyList<Run> Sort(IReadOnlyList<Vector3> positions,
        IReadOnlyList<RenderTriangle> triangles, Vector3 cameraForward)
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
        _runs.Clear();
        List<RenderTriangle>? current = null;
        int material = -1;
        for (int i = 0; i < triangles.Count; i++)
        {
            RenderTriangle t = _entries[i].Triangle;
            if (current is null || material != t.MaterialIndex)
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
        }
        return _runs;
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
