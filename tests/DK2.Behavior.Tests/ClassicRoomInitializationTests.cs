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
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class ClassicRoomInitializationTests
{
    [InstallFact]
    public void AWalkingBodyRecordsASlideContactWithTheFloorTortureWheel()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Run(1);
        ObjectState wheel = world.Objects.Single(o => o.ObjectTypeId == 8
            && o.TileIndex == 13 * world.Width + 12);
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        var centre = new Fixed16Vector(CreatureState.CentreOf(12) << 4, CreatureState.CentreOf(13) << 4);
        var obstacles = new List<SteeringObstacle>();
        navigation.CollectObstacles(centre, Fixed16.One, obstacles);
        SteeringObstacle obstacle = Assert.Single(obstacles, o => o.Pos == centre);
        Assert.Equal(0x7330, obstacle.Size);
        Assert.True(obstacle.Solid);
        var body = new SteeringBody { Pos = centre + new Fixed16Vector(Fixed16.One / 4, 0),
            Radius = Fixed16.One / 8, MaxForce = SeparationRules.ShippedMaxForce };
        Fixed16Vector force = RepelObstacleRules.Compute(body, navigation);
        Assert.Equal(Fixed16Vector.Zero, force);
        Assert.Single(body.SolidContacts);

        wheel.InHand = true;
        obstacles.Clear();
        navigation.CollectObstacles(centre, Fixed16.One, obstacles);
        Assert.DoesNotContain(obstacles, o => o.Pos == centre);
    }

    [InstallFact]
    public void AWalkingDwarfGoesAroundTheFloorWheelOnItsWayAcrossTheRoom()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Run(1);
        foreach (CreatureState creature in world.Creatures.ToArray()) world.RemoveCreature(creature.Id);
        CreatureState dwarf = world.SpawnCreature(db, 14, PlayerIds.Good, new TilePos(13, 13));
        var simulation = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        Assert.True(navigation.TryRoute(dwarf, new TilePos(11, 13), MoveOptions.Plain));
        var centre = new Fixed16Vector(CreatureState.CentreOf(12) << 4, CreatureState.CentreOf(13) << 4);
        int closest = int.MaxValue, contacts = 0;
        for (int tick = 0; tick < 80 && !dwarf.Body.PathFinished; tick++)
        {
            simulation.Run(1);
            closest = Math.Min(closest, (dwarf.Body.Pos - centre).Length());
            contacts += dwarf.Body.SolidContacts.Count;
        }
        Assert.True(contacts > 0);
        Assert.True(closest > Fixed16.One / 3, $"walker crossed the disc at {closest / (double)Fixed16.One:F3} tiles");
        Assert.Equal(new TilePos(11, 13), dwarf.Tile);
    }

    [InstallFact]
    public void OrdinaryObstacleFlagsToggleInTileHeadOrderAndRestartForEachQuery()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        int tile = 13 * world.Width + 12;
        ObjectState wheel = world.SpawnObject(db, 8, PlayerIds.Good, tile);
        ObjectState chair = world.SpawnObject(db, 61, PlayerIds.Good, tile);
        world.SpawnObject(db, 62, PlayerIds.Good, tile);
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        var centre = new Fixed16Vector(CreatureState.CentreOf(12) << 4, CreatureState.CentreOf(13) << 4);
        var obstacles = new List<SteeringObstacle>();
        navigation.CollectObstacles(centre, Fixed16.One, obstacles);
        Assert.Equal(2, obstacles.Count);
        Assert.True(obstacles[0].Solid);
        Assert.False(obstacles[1].Solid);
        Assert.Equal((db.Objects[61].Width.Raw >> 1) << 4, obstacles[0].Size);
        chair.InHand = true;
        wheel.PosX = CreatureState.CentreOf(12) + 123;
        wheel.PosY = CreatureState.CentreOf(13) + 456;
        obstacles.Clear();
        navigation.CollectObstacles(centre, Fixed16.One, obstacles);
        SteeringObstacle remaining = Assert.Single(obstacles);
        Assert.True(remaining.Solid);
        Assert.Equal(new Fixed16Vector(wheel.PosX << 4, wheel.PosY << 4), remaining.Pos);
    }

    [InstallFact]
    public void AShippedDwarfCanWalkOntoWaterWithoutABridge()
    {
        (GameDatabase db, WorldState world) = LevelEight();
        Assert.True(db.CreatureById(14).Flags.HasFlag(CreatureFlags.CanWalkOnWater));
        foreach (CreatureState creature in world.Creatures.ToArray()) world.RemoveCreature(creature.Id);
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        TilePos land = default, water = default;
        bool found = false;
        for (int y = 0; y < world.Height && !found; y++)
            for (int x = 1; x < world.Width && !found; x++)
                if (navigation.NavByte(x - 1, y) == DK2.Pathfinding.NavBytes.Floor
                    && navigation.NavByte(x, y) == DK2.Pathfinding.NavBytes.Water)
                {
                    land = new TilePos(x - 1, y);
                    water = new TilePos(x, y);
                    found = true;
                }
        Assert.True(found);
        CreatureState dwarf = world.SpawnCreature(db, 14, PlayerIds.Good, land);
        Assert.True(navigation.TryRoute(dwarf, water, MoveOptions.Plain));
        new GameSimulation(db, world, 1, [new CreatureMovementSystem()]).Run(30);
        Assert.Equal(water, dwarf.Tile);
    }

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
        CreatureState wallVictim = victims.Single(c => c.Tile == new TilePos(12, 10));
        Assert.Equal((CreatureState.CentreOf(12), CreatureState.CentreOf(10) - 0x7C0),
            (wallVictim.PosX, wallVictim.PosY));
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
