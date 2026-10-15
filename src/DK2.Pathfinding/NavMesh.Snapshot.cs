// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>The mesh as simulation state (ADR-0461). Since the per-tile rebuild edits it in place,
/// the mesh depends on the history of edits and not on the bytes alone, so a save carries it and the
/// state hash folds it in: every slot, live or dead, both free lists in their order (the next
/// allocation pops the end), each point's triangle index and the locate cache, which decides
/// where a walk starts and so which holder of a vertex it reaches first (ADR-0486).</summary>
public sealed partial class NavMesh
{
    public void Write(BinaryWriter w)
    {
        if (w is null) throw new ArgumentNullException(nameof(w));
        w.Write(MinX);
        w.Write(MinY);
        w.Write(MaxX);
        w.Write(MaxY);
        w.Write(_pointCount);
        for (int p = 0; p < _pointCount; p++)
        {
            w.Write(_px[p]);
            w.Write(_py[p]);
            w.Write(_pointDead[p]);
            w.Write(_pointTri[p]);
        }
        w.Write(_pointFree.Count);
        foreach (int p in _pointFree) w.Write(p);
        w.Write(_triCount);
        for (int t = 0; t < _triCount; t++)
        {
            for (int i = 0; i < 3; i++) w.Write(_v[(3 * t) + i]);
            for (int i = 0; i < 3; i++) w.Write(_n[(3 * t) + i]);
            w.Write(_nav[t]);
            w.Write(_constraint[t]);
        }
        w.Write(_triFree.Count);
        foreach (int t in _triFree) w.Write(t);
        foreach (int c in _locateCache) w.Write(c);
    }

    public static NavMesh Read(BinaryReader r)
    {
        if (r is null) throw new ArgumentNullException(nameof(r));
        var mesh = new NavMesh();
        mesh.MinX = r.ReadInt32();
        mesh.MinY = r.ReadInt32();
        mesh.MaxX = r.ReadInt32();
        mesh.MaxY = r.ReadInt32();
        int points = r.ReadInt32();
        if (points < 0) throw new InvalidDataException("negative point count in a navigation mesh");
        int pointCapacity = Math.Max(64, points);
        mesh._px = new int[pointCapacity];
        mesh._py = new int[pointCapacity];
        mesh._pointDead = new bool[pointCapacity];
        mesh._pointTri = new int[pointCapacity];
        mesh._pointCount = points;
        for (int p = 0; p < points; p++)
        {
            mesh._px[p] = r.ReadInt32();
            mesh._py[p] = r.ReadInt32();
            mesh._pointDead[p] = r.ReadBoolean();
            mesh._pointTri[p] = r.ReadInt32();
        }
        int pointFree = r.ReadInt32();
        for (int i = 0; i < pointFree; i++) mesh._pointFree.Add(r.ReadInt32());
        int triangles = r.ReadInt32();
        if (triangles < 0) throw new InvalidDataException("negative triangle count in a navigation mesh");
        int triangleCapacity = Math.Max(64, triangles);
        mesh._v = new int[3 * triangleCapacity];
        mesh._n = new int[3 * triangleCapacity];
        mesh._nav = new byte[triangleCapacity];
        mesh._constraint = new byte[triangleCapacity];
        mesh._triCount = triangles;
        for (int t = 0; t < triangles; t++)
        {
            for (int i = 0; i < 3; i++) mesh._v[(3 * t) + i] = r.ReadInt32();
            for (int i = 0; i < 3; i++) mesh._n[(3 * t) + i] = r.ReadInt32();
            mesh._nav[t] = r.ReadByte();
            mesh._constraint[t] = r.ReadByte();
        }
        int triFree = r.ReadInt32();
        for (int i = 0; i < triFree; i++) mesh._triFree.Add(r.ReadInt32());
        for (int i = 0; i < mesh._locateCache.Length; i++) mesh._locateCache[i] = r.ReadInt32();
        return mesh;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(MinX);
        hash.Add(MinY);
        hash.Add(MaxX);
        hash.Add(MaxY);
        hash.Add(_pointCount);
        for (int p = 0; p < _pointCount; p++)
        {
            hash.Add(_px[p]);
            hash.Add(_py[p]);
            hash.Add(_pointDead[p]);
            hash.Add(_pointTri[p]);
        }
        hash.Add(_pointFree.Count);
        foreach (int p in _pointFree) hash.Add(p);
        hash.Add(_triCount);
        for (int t = 0; t < _triCount; t++)
        {
            for (int i = 0; i < 3; i++) hash.Add(_v[(3 * t) + i]);
            for (int i = 0; i < 3; i++) hash.Add(_n[(3 * t) + i]);
            hash.Add(_nav[t]);
            hash.Add(_constraint[t]);
        }
        hash.Add(_triFree.Count);
        foreach (int t in _triFree) hash.Add(t);
        foreach (int c in _locateCache) hash.Add(c);
    }
}
