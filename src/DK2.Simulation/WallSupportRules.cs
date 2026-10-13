// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Releases walls which lost their supporting floor after hostile floor or room capture.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/wall-support-after-claim.md")]
public static class WallSupportRules
{
    private static readonly TilePos[] Orthogonal = [new(-1, 0), new(0, -1), new(1, 0), new(0, 1)];
    private static readonly TilePos[] Diagonal = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
    // The native table deliberately repeats the north/west pair in the opposite order.
    private static readonly (TilePos First, TilePos Second)[] RetainingPairs =
    [
        (new(0, -1), new(-1, 0)), (new(0, -1), new(1, 0)),
        (new(-1, 0), new(0, -1)), (new(-1, 0), new(0, 1)),
    ];

    public static void ReleaseUnsupported(WorldState world, GameDatabase database, TilePos changedFloor)
    {
        foreach (TilePos offset in Orthogonal)
        {
            TilePos at = Offset(changedFloor, offset);
            if (IsOwnedWall(world, database, at, requireFullNonGold: false)
                && !OwnFloorRules.Touches(database, world, at, world.Tile(at).OwnerPlayerId))
                TileWorkRules.ReleaseWall(world, database, at);
        }

        foreach (TilePos offset in Diagonal)
        {
            TilePos at = Offset(changedFloor, offset);
            if (!IsOwnedWall(world, database, at, requireFullNonGold: true)
                || OwnFloorRules.Touches(database, world, at, world.Tile(at).OwnerPlayerId)) continue;

            bool retained = false;
            foreach (var pair in RetainingPairs)
                if (IsOwnedWall(world, database, Offset(at, pair.First), requireFullNonGold: true)
                    && IsOwnedWall(world, database, Offset(at, pair.Second), requireFullNonGold: true))
                {
                    retained = true;
                    break;
                }
            if (!retained) TileWorkRules.ReleaseWall(world, database, at);
        }
    }

    private static TilePos Offset(TilePos at, TilePos delta) => new(at.X + delta.X, at.Y + delta.Y);

    private static bool IsOwnedWall(WorldState world, GameDatabase database, TilePos at, bool requireFullNonGold)
    {
        if (!world.Contains(at)) return false;
        ref TileState tile = ref world.Tile(at);
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        return tile.OwnerPlayerId != PlayerIds.Neutral && terrain.IsSolid
            && (!requireFullNonGold || (terrain.GoldValue == 0 && tile.Health == terrain.MaxHealth));
    }
}
