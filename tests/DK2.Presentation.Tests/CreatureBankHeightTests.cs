// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class CreatureBankHeightTests
{
    [InstallTheory]
    [InlineData(2, 1, 0xC00, 0x800, 0xD33)]
    [InlineData(0, 1, 0x400, 0x800, 0xD33)]
    [InlineData(1, 0, 0x800, 0x400, 0xD33)]
    [InlineData(1, 2, 0x800, 0xC00, 0xD33)]
    [InlineData(2, 0, 0xC00, 0x600, 0xBCC)]
    [InlineData(2, 1, 0xC00, 0x600, 0xD33)]
    [InlineData(1, 0, 0x800, 0xC00, 0xA66)]
    public void GroundedCreatureRisesOnlyTowardItsBank(int landX, int landY,
        int offsetX, int offsetY, int expectedHeight)
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(landX, landY).TerrainId = database.Terrain.Values.First(t => !t.IsSolid && !t.IsWater && !t.IsLava
            && !database.RoomsByTerrain.ContainsKey(t.TerrainId)).TerrainId;
        creature.PosX = Fp32.One + offsetX;
        creature.PosY = Fp32.One + offsetY;
        CreatureView rendered = new WorldView(database).Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(expectedHeight, rendered.Height);
    }

    [InstallFact]
    public void ShoreCrossingRetainsBothGroundHeightsForInterpolation()
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(2, 1).TerrainId = database.Room(RoomTypeId.Lair).TerrainId;
        creature.LastPosX = Fp32.One + 0xC00;
        creature.LastPosY = Fp32.One + 0x800;
        creature.PosX = 2 * Fp32.One + 0x400;
        creature.PosY = creature.LastPosY;
        CreatureView rendered = new WorldView(database).Creatures(world).Single(c => c.Id == creature.Id);
        Assert.Equal(0xD33, rendered.PreviousHeight);
        Assert.Equal(Fp32.One, rendered.Height);
    }

    [InstallFact]
    public void ABridgeOverTheSameLiquidDoesNotBecomeAShoreBank()
    {
        var (database, world, creature) = WaterPatch();
        world.Tile(2, 1).TerrainId = database.Room(RoomTypeId.StoneBridge).TerrainId;
        world.Tile(2, 1).Flags = BridgeRules.SpansWater;
        creature.PosX = Fp32.One + 0xC00;
        Assert.Equal(0xA66, new WorldView(database).Creatures(world).Single(c => c.Id == creature.Id).Height);
    }

    private static (GameDatabase, WorldState, CreatureState) WaterPatch()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        WorldState world = WorldState.FromMap(database,
            GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, [2, 3]);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                world.Tile(x, y).TerrainId = database.WaterTerrain!.TerrainId;
        CreatureState creature = world.SpawnCreature(database,
            database.CreatureByName("Dwarf")!.CreatureId, 2, new TilePos(1, 1));
        return (database, world, creature);
    }
}
