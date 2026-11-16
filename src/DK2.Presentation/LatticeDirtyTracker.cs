// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Which wall torches must be bent again after tiles were pushed. A pushed tile makes the
/// <see cref="TerrainLattice"/> forget the cells within <see cref="ForgetReach"/> of it, and a torch reads the cells
/// within one of its wall (<see cref="TerrainLattice.Deform"/>), so only a torch within <see cref="TorchReach"/> of a
/// pushed tile can bend differently.</summary>
public sealed class LatticeDirtyTracker
{
    /// <summary>How far a pushed tile's change reaches in the lattice (<see cref="TerrainLattice.Invalidate"/>).</summary>
    public const int ForgetReach = 1;

    /// <summary>How far from a pushed tile a torch's wall may stand and still need bending again.</summary>
    public const int TorchReach = ForgetReach + 1;

    private readonly bool[] _touched;
    private readonly int _width, _height;

    public LatticeDirtyTracker(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width;
        _height = height;
        _touched = new bool[width * height];
    }

    /// <summary>Whether any tile was pushed since the last <see cref="Clear"/>.</summary>
    public bool AnyTouched { get; private set; }

    /// <summary>Records a pushed tile by its grid index.</summary>
    public void Touch(int tileIndex)
    {
        _touched[tileIndex] = true;
        AnyTouched = true;
    }

    /// <summary>Whether a tile within <see cref="TorchReach"/> of <paramref name="wall"/> (clamped to the map) was pushed.</summary>
    public bool TouchedNear(TilePos wall)
    {
        for (int y = Math.Max(0, wall.Y - TorchReach); y <= Math.Min(_height - 1, wall.Y + TorchReach); y++)
            for (int x = Math.Max(0, wall.X - TorchReach); x <= Math.Min(_width - 1, wall.X + TorchReach); x++)
                if (_touched[(y * _width) + x]) return true;
        return false;
    }

    /// <summary>Forgets every pushed tile, once the torches were bent again.</summary>
    public void Clear()
    {
        Array.Clear(_touched, 0, _touched.Length);
        AnyTouched = false;
    }
}
