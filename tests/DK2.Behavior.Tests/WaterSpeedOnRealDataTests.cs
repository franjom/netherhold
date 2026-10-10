// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class WaterSpeedOnRealDataTests
{
    [InstallFact]
    public void MovementUsesWaterSpeedAndRecoversDrySpeedOnAShippedBridge()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level8")!;
        WorldState world = WorldState.FromMap(db, map, [0, 1, 2, 3, 4]);
        TilePos[] tiles = Enumerable.Range(0, world.Width * world.Height)
            .Select(i => new TilePos(i % world.Width, i / world.Width)).ToArray();
        TilePos water = tiles.First(p => db.TerrainById(world.Tile(p).TerrainId).IsWater);
        TilePos bridge = tiles.First(p => db.RoomByTerrain(world.Tile(p).TerrainId)?.TypeId is
            RoomTypeId.WoodenBridge or RoomTypeId.StoneBridge or RoomTypeId.HeroStoneBridge);
        CreatureState creature = world.SpawnCreature(db, 1, new PlayerId(3), water);
        var simulation = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        int dry = CreatureMovementSystem.MaxSpeedOf(db, db.CreatureById(1), creature.Level);
        simulation.Tick();
        Assert.Equal(((dry >> 4) / 2) << 4, creature.Body.MaxSpeed);
        creature.PosX = CreatureState.CentreOf(bridge.X);
        creature.PosY = CreatureState.CentreOf(bridge.Y);
        simulation.Tick();
        Assert.Equal(dry, creature.Body.MaxSpeed);
    }

    [InstallTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public void WaterHalvesAnImpsCurrentSpeedAtEveryLevel(int level)
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        CreatureDef imp = db.CreatureById(1);
        var creature = new CreatureState { CreatureTypeId = imp.CreatureId, Level = level };
        int cap = CreatureMovementSystem.MaxSpeedOf(db, imp, level);
        Assert.True(imp.Flags.HasFlag(CreatureFlags.CanWalkOnWater));
        TerrainDef water = db.Terrain.Values.Single(t => t.IsWater);
        int waterCap = WaterSpeedRules.SpeedCap(water, imp, creature, cap);
        Assert.Equal(((cap >> 4) / 2) << 4, waterCap);
    }

    [InstallFact]
    public void BridgesKeepTheDrySpeedAndFlyingCreaturesIgnoreTheWaterModifier()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        CreatureDef imp = db.CreatureById(1);
        var creature = new CreatureState { CreatureTypeId = imp.CreatureId };
        int cap = CreatureMovementSystem.MaxSpeedOf(db, imp, 1);
        foreach (RoomDef bridge in db.Rooms.Values.Where(r => r.TypeId is
            RoomTypeId.WoodenBridge or RoomTypeId.StoneBridge or RoomTypeId.HeroStoneBridge))
        {
            TerrainDef terrain = db.TerrainById(bridge.TerrainId);
            Assert.False(terrain.IsWater);
            Assert.Equal(cap, WaterSpeedRules.SpeedCap(terrain, imp, creature, cap));
        }
        TerrainDef water = db.Terrain.Values.Single(t => t.IsWater);
        CreatureDef flyer = db.Creatures.Values.First(c => c.Flags.HasFlag(CreatureFlags.CanFly)
            && !c.Flags.HasFlag(CreatureFlags.IsWorker));
        Assert.Equal(cap, WaterSpeedRules.SpeedCap(water, flyer, creature, cap));
        creature.Dk2StateId = 218;
        Assert.Equal(((cap >> 4) / 2) << 4, WaterSpeedRules.SpeedCap(water, flyer, creature, cap));
    }

    [InstallFact]
    public void WaterHalvesTheHastedCapAndDropsAnOddEntitySpeedRemainder()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        CreatureDef imp = db.CreatureById(1);
        TerrainDef water = db.Terrain.Values.Single(t => t.IsWater);
        var creature = new CreatureState { CreatureTypeId = imp.CreatureId,
            SpeedEffectTicks = 60, SpeedPercent = 150 };
        int baseCap = CreatureMovementSystem.MaxSpeedOf(db, imp, 1);
        int hasteCap = StatusEffectRules.SpeedCap(db, imp, creature, baseCap);
        Assert.True(hasteCap > baseCap);
        Assert.Equal(((hasteCap >> 4) / 2) << 4,
            WaterSpeedRules.SpeedCap(water, imp, creature, hasteCap));
        Assert.Equal(60, creature.SpeedEffectTicks);
        Assert.Equal(150, creature.SpeedPercent);
        Assert.Equal(0x7FF0, WaterSpeedRules.SpeedCap(water, imp, creature, 0xFFF0));
    }
}
