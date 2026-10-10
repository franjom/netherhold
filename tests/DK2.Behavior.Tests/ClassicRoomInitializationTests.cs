// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class ClassicRoomInitializationTests
{
    private static (GameDatabase Database, WorldState World) LevelEight()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level8")!;
        WorldState world = WorldState.FromMap(db, map, [0, 1, 2, 3, 4]);
        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "Level8")!);
        return (db, world);
    }

    [InstallFact]
    public void PlacedCaptivesEnterTheirCellsWithoutLosingTheirStartingHealth()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        CreatureState[] captives = world.Creatures.Where(c => c.Tile.X == 17 && c.Tile.Y is 21 or 22).ToArray();
        Assert.Equal(2, captives.Length);
        int[] health = captives.Select(c => c.Health).ToArray();
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Run(1);
        for (int i = 0; i < captives.Length; i++)
        {
            Assert.True(PrisonRules.IsImprisoned(captives[i]));
            Assert.Equal(world.Tile(captives[i].Tile).OwnerPlayerId, captives[i].PrisonOwnerPlayerId);
            Assert.Equal(health[i], captives[i].Health);
            Assert.False(CombatRules.IsAttackable(captives[i]));
        }
    }

    [InstallFact]
    public void PlacedTortureVictimsUseTheWallAndFloorWheels()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        CreatureState[] victims = world.Creatures.Where(c => (c.Flags & 0x40) != 0
            && db.RoomByTerrain(world.Tile(c.Tile).TerrainId)?.TypeId == RoomTypeId.TortureChamber).ToArray();
        Assert.Equal(3, victims.Length);
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Run(1);
        Assert.All(victims, c =>
        {
            Assert.True(TortureRules.IsBeingTortured(c));
            Assert.False(CombatRules.IsAttackable(c));
            Assert.Contains(world.Objects, o => TortureRules.IsDevice(o.ObjectTypeId)
                && o.TileIndex == c.Tile.Y * world.Width + c.Tile.X);
        });
        Assert.Contains(victims, c => c.Tile == new TilePos(12, 10));
    }

    [InstallFact]
    public void WorkshopWallMachinesStandAtTheBinaryWallInset()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Run(1);
        int checkedMachines = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, db).Where(r => r.Definition.TypeId == RoomTypeId.Workshop))
        foreach (ObjectState machine in world.Objects.Where(o => room.Contains(o.TileIndex) && WorkshopStep.IsMachine(o.ObjectTypeId)))
        {
            int side = AutoFurnitureRules.WallSide(world, db, room, machine.TileIndex);
            if (side < 0) continue;
            int x = CreatureState.CentreOf(machine.TileIndex % world.Width);
            int y = CreatureState.CentreOf(machine.TileIndex / world.Width);
            (int expectedX, int expectedY) = side switch
            {
                0 => (x, y - 0x700), 1 => (x + 0x700, y),
                2 => (x, y + 0x700), _ => (x - 0x700, y),
            };
            Assert.Equal((expectedX, expectedY), AutoFurnitureRules.FurniturePosition(world, db, room, machine));
            checkedMachines++;
        }
        Assert.True(checkedMachines > 0);
    }

    [InstallFact]
    public void OwnPlacedMistressUsesHerDeviceWithoutBecomingHerOwnCaptive()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        CreatureState victim = world.Creatures.First(c => c.Tile == new TilePos(12, 10) && (c.Flags & 0x40) != 0);
        PlayerId owner = world.Tile(victim.Tile).OwnerPlayerId;
        world.ChangeCreatureOwner(victim, owner);
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Run(1);
        Assert.True(TortureRules.IsBeingTortured(victim));
        Assert.Equal(PrisonRules.NoPrison, victim.PrisonOwnerPlayerId);
    }

    [InstallFact]
    public void RuinedAtLoadHeartDoesNotGenerateImpsInsideRockOrOutsideTheMap()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        Assert.True(world.Player(4).HeartRuined);
        new GameSimulation(db, world, 1, [new ImpSupplySystem()]).Run(41);
        Assert.Equal(0, ImpSupplyRules.ImpCount(world, db, new PlayerId(4)));
        Assert.True(world.Player(4).NextFreeImpTick.Value > 40);
    }

    [InstallFact]
    public void DoubleQuadInteriorBlocksOrdinaryRoutesButLetsAReleasedCaptiveOut()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        var steering = new WorldSteering(db, world);
        steering.Refresh();
        Assert.Equal(4, steering.NavByte(17, 21));
        Assert.Equal(0, steering.NavByte(17, 20));
        CreatureState mover = world.Creatures.First(c => c.Tile == new TilePos(17, 21));
        Assert.False(steering.TryRoute(mover, new TilePos(17, 20), MoveOptions.Loose));
        Assert.True(steering.TryRoute(mover, new TilePos(17, 20), MoveOptions.CaptiveSpot));
    }
}
