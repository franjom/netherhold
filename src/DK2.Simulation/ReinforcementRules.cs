// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/wall-corner-fill.md")]
public static class ReinforcementRules
{
    private static readonly TilePos[] Diagonals = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];

    public static bool IsFinished(GameDatabase database, in TileState tile)
    {
        TerrainDef terrain = database.TerrainById(tile.TerrainId);
        return tile.OwnerPlayerId != PlayerIds.Neutral && terrain.IsSolid
            && terrain.GoldValue == 0 && tile.Health == terrain.MaxHealth;
    }

    public static void FillCorners(SimulationContext context, TilePos at, PlayerId owner)
    {
        WorldState world = context.World;
        if (!world.Contains(at) || !IsFinished(context.Database, world.Tile(at))) return;
        foreach (TilePos offset in Diagonals)
        {
            var diagonal = new TilePos(at.X + offset.X, at.Y + offset.Y);
            if (!world.Contains(diagonal) || !IsFinished(context.Database, world.Tile(diagonal))) continue;
            var vertical = new TilePos(at.X, diagonal.Y);
            var horizontal = new TilePos(diagonal.X, at.Y);
            if (offset.X == 1 && offset.Y == 1)
            {
                Fill(horizontal, vertical);
                Fill(vertical, horizontal);
            }
            else
            {
                Fill(vertical, horizontal);
                Fill(horizontal, vertical);
            }
        }

        void Fill(TilePos wall, TilePos open)
        {
            ref TileState tile = ref world.Tile(wall);
            TerrainDef terrain = context.Database.TerrainById(tile.TerrainId);
            if (tile.OwnerPlayerId != PlayerIds.Neutral || !terrain.IsSolid || terrain.IsImpenetrable
                || terrain.GoldValue != 0 || tile.Health == terrain.MaxHealth) return;
            context.Navigation.Refresh();
            if (NavBytes.IsSolid(context.Navigation.NavByte(open.X, open.Y))) return;
            byte before = tile.TerrainId, ownerBefore = tile.OwnerPlayerId;
            bool changed = TileWorkRules.CompleteWall(context.Database, ref tile, owner);
            SlabChain.Changed(world, context.Database, wall.Y * world.Width + wall.X, before, ownerBefore);
            if (changed) TileWorkSystem.RequestCompletionEffect(context, before, wall, owner);
        }
    }
}
