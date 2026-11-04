// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Ruins (or restores) a keeper's fixture Heart as the heart frame does: the keeper's flag and the
/// room's own ruin on its tiles (ADR-0662).</summary>
internal static class HeartRuin
{
    public static void Set(WorldState world, PlayerId player, bool ruined)
    {
        world.Player(player).HeartRuined = ruined;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId == player.Value && tile.TerrainId == Dk2TerrainFixture.DungeonHeartTerrain)
                tile.RoomRuined = ruined;
        }
    }
}
