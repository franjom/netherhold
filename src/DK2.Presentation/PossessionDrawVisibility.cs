// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;
namespace DK2.Presentation;

/// <summary>The entity and effect draw policy over the possession tile mask, independent of the renderer.</summary>
public readonly ref struct PossessionDrawVisibility(ReadOnlySpan<byte> mask, int width, bool enabled, CreatureId hiddenCreature)
{
    private readonly ReadOnlySpan<byte> _mask = mask;
    public bool Tile(TilePos tile) => !enabled || ((uint)tile.X < (uint)width
        && (uint)tile.Y < (uint)(_mask.Length / width) && Floor(tile.Y * width + tile.X));
    public bool Floor(int tileIndex) => !enabled || ((uint)tileIndex < (uint)_mask.Length && _mask[tileIndex] != 0);
    public bool Creature(CreatureId id, TilePos tile) => id != hiddenCreature && Tile(tile);
    public bool Object(TilePos tile, byte alpha) => ObjectMaterialLook.IsDrawn(alpha) && Tile(tile);
}
