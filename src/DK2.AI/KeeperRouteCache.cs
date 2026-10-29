// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.AI;

/// <summary>A keeper's last routing mesh, kept across turns while the navigation bytes it was
/// built from are unchanged. Its locate cache carries across queries and turns, so edge and
/// vertex answers may differ from a fresh mesh's (ADR-0486). That history is deterministic
/// because the cache belongs to one keeper. <see cref="KeeperStaging"/> shows how to restore
/// a reused mesh to the locate cache its whole build left when fresh-view answers are required.
/// </summary>
public sealed class KeeperRouteCache
{
    private byte[] _bytes = [];
    private int _width = -1;
    private MeshRouter? _router;

    /// <summary>How many meshes this cache has built.</summary>
    public int Builds { get; private set; }

    public MeshRouter RouterFor(INavByteGrid grid)
    {
        if (_router is null || !Matches(grid))
        {
            Capture(grid);
            _router = new MeshRouter(NavMesh.Build(grid));
            Builds++;
        }
        return _router;
    }

    private bool Matches(INavByteGrid grid)
    {
        if (grid.Width != _width || grid.Width * grid.Height != _bytes.Length) return false;
        int i = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (_bytes[i++] != grid.NavByte(x, y)) return false;
        return true;
    }

    private void Capture(INavByteGrid grid)
    {
        int count = grid.Width * grid.Height;
        if (_bytes.Length != count) _bytes = new byte[count];
        _width = grid.Width;
        int i = 0;
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                _bytes[i++] = grid.NavByte(x, y);
    }
}
