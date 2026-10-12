// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A visual-only request for the build wave's effect81 at a tile centre.
/// Source and timing boundary: room-construction-effects.md.</summary>
public readonly record struct RoomConstructionEffect(PlayerId Owner, TilePos Tile)
{
    public const ushort DefinitionId = 81;
    public Fp32 Height => new(0x1800);
}
