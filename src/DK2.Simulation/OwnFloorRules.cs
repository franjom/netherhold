// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Whether a tile touches a keeper's own floor: DK2's <c>0x00453B30</c>, which both a
/// claim and a bridge span ask (imp-claim-path.md §4, rooms-and-gold.md, "Adjacency").</summary>
/// <remarks>Each of the four orthogonal neighbours counts when the player owns it and its terrain
/// is Ownable and not Solid, so a keeper's own walls count for nothing and an owned bridge counts
/// as floor. STRONGLY INFERRED from the binary; the owner confirmed that a room needs adjacent
/// land (Q33).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/rooms-and-gold.md")]
public static class OwnFloorRules
{
    private static readonly int[] Dx = [0, -1, 1, 0];
    private static readonly int[] Dy = [-1, 0, 0, 1];

    /// <summary>North, west, east, south; the order does not matter to a yes-or-no answer.</summary>
    public static ReadOnlySpan<int> NeighbourDx => Dx;
    public static ReadOnlySpan<int> NeighbourDy => Dy;

    /// <summary>One neighbour's test: owned by <paramref name="playerId"/>, Ownable, not Solid.</summary>
    public static bool IsOwnFloor(TerrainDef terrain, byte ownerPlayerId, byte playerId) =>
        ownerPlayerId == playerId && terrain.IsOwnable && !terrain.IsSolid;

    /// <summary>True when any in-bounds orthogonal neighbour of <paramref name="at"/> is
    /// <paramref name="playerId"/>'s own floor.</summary>
    public static bool Touches(GameDatabase database, WorldState world, TilePos at, byte playerId)
    {
        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            var neighbour = new TilePos(at.X + NeighbourDx[i], at.Y + NeighbourDy[i]);
            if (!world.Contains(neighbour)) continue;
            ref TileState tile = ref world.Tile(neighbour);
            if (IsOwnFloor(database.TerrainById(tile.TerrainId), tile.OwnerPlayerId, playerId)) return true;
        }
        return false;
    }
}
