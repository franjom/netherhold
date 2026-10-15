// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Writes the explored layer, including the native whole-room construction cases.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/fog-reveal-sources.md")]
public static class DiscoveryRules
{
    public static void SetExplored(WorldState world, GameDatabase database, TilePos position,
        PlayerId player, bool explored = true)
    {
        if (!world.Contains(position)) return;
        int index = position.Y * world.Width + position.X;
        RoomDef? definition = database.RoomByTerrain(world.Tile(position).TerrainId);
        if (definition?.Construction is TileConstruction.ThreeByThree
            or TileConstruction.ThreeByThreeRotated or TileConstruction.FiveByFiveRotated)
        {
            RoomInstance? room = RoomIndex.RoomAt(world, database, index);
            if (room is not null)
            {
                foreach (int roomTile in room.TileIndices)
                    Set(ref world.TileAtIndex(roomTile), player, explored);
                return;
            }
        }
        Set(ref world.Tile(position), player, explored);
    }

    public static void Rectangle(WorldState world, GameDatabase database, ActionPointState point,
        bool explored)
    {
        for (int y = point.StartY; y <= point.EndY && y < world.Height; y++)
            for (int x = point.StartX; x <= point.EndX && x < world.Width; x++)
                foreach (PlayerState player in world.RawPlayers)
                    SetExplored(world, database, new TilePos(x, y), player.PlayerId, explored);
    }

    internal static void TerrainChanged(GameDatabase database, ref TileState tile, byte oldTerrain)
    {
        if ((database.TerrainById(oldTerrain).Flags & TerrainFlags.UnexploreIfDugByAnotherPlayer) != 0)
            tile.ExploredBy = PlayerMask.None;
        if (tile.OwnerPlayerId <= TileState.MaxTaggingPlayerId)
            tile.SetExploredBy(tile.OwnerPlayerId);
    }

    private static void Set(ref TileState tile, PlayerId player, bool explored)
    {
        if (explored) tile.SetExploredBy(player);
        else tile.ClearExploredBy(player);
    }
}
