// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;
namespace DK2.Presentation;

/// <summary>The entity and effect draw policy over the possession tile mask, independent of the renderer.</summary>
public readonly ref struct PossessionDrawVisibility
{
    private readonly int _width;
    private readonly bool _enabled;
    private readonly CreatureId _hiddenCreature;

    public PossessionDrawVisibility(ReadOnlySpan<byte> mask, int width, bool enabled, CreatureId hiddenCreature)
    {
        _width = width;
        _enabled = enabled;
        _hiddenCreature = hiddenCreature;
        _mask = mask;
    }

    private readonly ReadOnlySpan<byte> _mask;
    public bool Tile(TilePos tile)
    {
        return !_enabled || ((uint)tile.X < (uint)_width
        && (uint)tile.Y < (uint)(_mask.Length / _width) && Floor(tile.Y * _width + tile.X));
    }

    public bool Floor(int tileIndex)
    {
        return !_enabled || ((uint)tileIndex < (uint)_mask.Length && _mask[tileIndex] != 0);
    }

    public bool Creature(CreatureId id, TilePos tile)
    {
        return id != _hiddenCreature && Tile(tile);
    }

    public bool Object(TilePos tile, byte alpha)
    {
        return ObjectMaterialLook.IsDrawn(alpha) && Tile(tile);
    }
}
