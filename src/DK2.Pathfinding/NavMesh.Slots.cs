// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Pathfinding;

/// <summary>The original's view of our storage, for routines transcribed in its slot numbers
/// (ADR-0486). It stores a triangle clockwise in cross-product terms and ours stores the same
/// corners the other way round, with the two after slot 0 swapped: its vertex slots 0, 1, 2 are
/// ours 0, 2, 1, and its edge s, from its vertex s to its next(s), is our edge 2 − s reversed, so
/// its neighbour words and constraint bits follow the edges. Its slot tables are
/// <see cref="Next"/> and <see cref="Prev"/>. A routine written through these helpers reads like
/// the decompile and walks the same slots.</summary>
public sealed partial class NavMesh
{
    private static int DkV(int s) => s == 0 ? 0 : 3 - s;

    private static int DkE(int s) => 2 - s;

    private int DkVertex(int t, int s) => _v[(3 * t) + DkV(s)];

    private void DkSetVertex(int t, int s, int p) => SetVertex(t, DkV(s), p);

    private int DkNeighbour(int t, int s) => _n[(3 * t) + DkE(s)];

    private void DkSetNeighbour(int t, int s, int u) => _n[(3 * t) + DkE(s)] = u;

    private void DkSetConstraint(int t, int s, bool on) => SetConstraint(t, DkE(s), on);

    /// <summary>The original's three-way compare: the slot of <c>u</c> whose neighbour is
    /// <c>t</c>, or −1.</summary>
    private int DkBack(int u, int t)
    {
        if (DkNeighbour(u, 0) == t) return 0;
        if (DkNeighbour(u, 1) == t) return 1;
        return DkNeighbour(u, 2) == t ? 2 : -1;
    }

    /// <summary>A new triangle slot holding a copy of <paramref name="t"/>: vertices, neighbours,
    /// byte and bits, as the original's splits start from (`0x004CA500`, `0x004CA820`).</summary>
    private int NewTriangleCopy(int t)
    {
        int n = AllocTriangle();
        for (int i = 0; i < 3; i++)
        {
            _v[(3 * n) + i] = _v[(3 * t) + i];
            _n[(3 * n) + i] = _n[(3 * t) + i];
        }
        _nav[n] = _nav[t];
        _constraint[n] = _constraint[t];
        return n;
    }

    /// <summary>Points every vertex of <paramref name="t"/> at it, so that a split that moved a
    /// vertex out of its old triangle leaves the fan index naming a holder.</summary>
    private void IndexVertices(int t)
    {
        for (int i = 0; i < 3; i++) _pointTri[_v[(3 * t) + i]] = t;
    }
}
