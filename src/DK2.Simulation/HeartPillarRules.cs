// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum HeartPillar { First, Second, Third }

public readonly record struct HeartPillarId(int Value)
{
    public static HeartPillarId For(WorldState world, HeartPillarTarget target) =>
        new(((target.Tile.Y * world.Width + target.Tile.X) * 3) + (int)target.Pillar);
}

public readonly record struct HeartPillarTarget(HeartPillar Pillar, TilePos Tile, Fp32 X, Fp32 Y);

/// <summary>The three Arch Pillars made by the Heart builder; furniture-placement.md §6.</summary>
public static class HeartPillarRules
{
    public static bool TryResolve(WorldState world, GameDatabase database, PlayerId owner, HeartPillarId id, out HeartPillarTarget target)
    {
        target = default;
        if (id.Value < 0 || id.Value / 3 >= world.TileCount) return false;
        bool standing = false;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (player.PlayerId == owner) standing = !player.HeartRuined && player.HeartHealth > 0;
        }
        if (!standing) return false;
        int tile = id.Value / 3;
        if (world.TileAtIndex(tile).OwnerPlayerId != owner
            || database.RoomByTerrain(world.TileAtIndex(tile).TerrainId)?.TypeId != RoomTypeId.DungeonHeart
            || RoomIndex.RoomAt(world, database, tile) is not { } room) return false;
        int[] chain = SlabChain.Order(world, room);
        int ordinal = id.Value % 3;
        int position = ordinal == 0 ? 7 : ordinal == 1 ? 16 : 18;
        if (position >= chain.Length || chain[position] != tile) return false;
        target = At(new TilePos(tile % world.Width, tile / world.Width), (HeartPillar)ordinal);
        return true;
    }

    public static HeartPillarTarget[] In(WorldState world, RoomInstance room)
    {
        int[] chain = SlabChain.Order(world, room);
        int count = chain.Length > 18 ? 3 : chain.Length > 16 ? 2 : chain.Length > 7 ? 1 : 0;
        var pillars = new HeartPillarTarget[count];
        for (int i = 0; i < count; i++)
        {
            int tile = chain[i == 0 ? 7 : i == 1 ? 16 : 18];
            pillars[i] = At(new TilePos(tile % world.Width, tile / world.Width), (HeartPillar)i);
        }
        return pillars;
    }

    public static HeartPillarTarget At(TilePos tile, HeartPillar pillar)
    {
        (int x, int y) = pillar switch
        {
            HeartPillar.First => (0x800, 0x4CD),
            HeartPillar.Second => (0x667, 0x19A),
            HeartPillar.Third => (0x999, 0x19A),
            _ => throw new ArgumentOutOfRangeException(nameof(pillar)),
        };
        return new(pillar, tile, new Fp32((tile.X << 12) + x), new Fp32((tile.Y << 12) + y));
    }
}
