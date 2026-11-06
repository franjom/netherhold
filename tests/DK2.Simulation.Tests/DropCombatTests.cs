// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class DropCombatTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = Keeper + 1;
    private static readonly TilePos Landing = new(9, 10);
    private static readonly TilePos DoorTile = new(10, 10);

    [Theory]
    [InlineData(Dk2TerrainFixture.GoblinCreatureId, false)]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, false)]
    [InlineData(Dk2TerrainFixture.WarlockCreatureId, true)]
    public void AHandDroppedCreatureAcquiresTheDoorAndDamagesIt(byte type, bool spell)
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState creature = world.SpawnCreature(db, type, Keeper, new TilePos(7, 10));
        DoorState door = world.Doors[0];
        var combat = new CombatSystem();
        var sim = new GameSimulation(db, world, 7,
            [new HandSystem(), new CreatureUpdateSystem(combat, new CreatureStatusSystem(), combat.States,
                new CreatureMovementSystem()), new ShotSystem(), new DoorRemovalSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Landing)));
        if (type == Dk2TerrainFixture.ImpCreatureId)
            Assert.Equal(FightTargetKind.Door, creature.FightTargetKind);
        else Assert.Equal(DropLandingRules.StunnedState, creature.Dk2StateId);

        for (int i = 0; i < 150 && door.Health == db.Doors[1].Health; i++) sim.Tick();

        Assert.True(door.Health < db.Doors[1].Health);
        Assert.Contains(sink.Events, e => e.Kind == (spell ? "shot_door_hit" : "door_hit"));
    }

    [Theory]
    [InlineData(Rival, 0, 10, 10, true)]
    [InlineData(PlayerIds.Neutral, 0, 10, 10, true)]
    [InlineData(Keeper, 0, 10, 10, false)]
    [InlineData(Rival, 2, 10, 10, false)]
    [InlineData(Rival, 0, 10, 11, false)]
    [InlineData(Rival, 0, 11, 10, false)]
    public void GetUpChecksOnlyCompletedNonAlliedCardinalDoors(byte owner, byte flags, int x, int y, bool attacks)
    {
        (GameDatabase db, WorldState world) = Setup(owner, flags, new TilePos(x, y));
        CreatureState creature = GettingUp(db, world);
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();

        Assert.Equal(attacks, creature.AttackAbnormalTargets);
        Assert.Equal(attacks ? FightTargetKind.Door : FightTargetKind.None, creature.FightTargetKind);
        Assert.Equal(attacks ? 199 : DropLandingRules.StandingState, creature.Dk2StateId);
    }

    [Fact]
    public void AnAlliedDoorIsSkipped()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState creature = GettingUp(db, world);
        var sim = new GameSimulation(db, world, 7, [new AllianceSystem(), new CreatureStatusSystem()]);
        sim.Tick(InputFrame.Of(new SetAllianceCommand(Keeper, Rival, true)));
        Assert.False(creature.AttackAbnormalTargets);
        Assert.Equal(DropLandingRules.StandingState, creature.Dk2StateId);
    }

    [Fact]
    public void AFailedForcedSelectionStillSuppressesTheRoomFallback()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState priest = world.SpawnCreature(db, Dk2TerrainFixture.PriestCreatureId, Keeper, Landing);
        StatePlacement.Place(priest, DropLandingRules.GettingUpState);
        CreatureState ally = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(9, 7));
        StatePlacement.Place(ally, 199);
        ally.Health = 1;
        for (int x = 0; x < world.Width; x++) world.Tile(new TilePos(x, 8)).TerrainId = Dk2TerrainFixture.Lava;
        Assert.False(new DK2.Simulation.Navigation.WorldSteering(db, world).TryRoute(priest, ally.Tile,
            DK2.Simulation.Navigation.MoveOptions.Plain));

        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();

        Assert.True(priest.AttackAbnormalTargets);
        Assert.Equal(world.Doors[0].Id, priest.AttackingId);
        Assert.Equal(FightTargetKind.None, priest.FightTargetKind);
        Assert.Equal(DropLandingRules.GettingUpState, priest.Dk2StateId);
    }

    [Fact]
    public void WorkerCombatPrecedesHandDisposalOnItsHeartCentre()
    {
        (GameDatabase db, WorldState world) = Setup();
        for (int y = 9; y <= 11; y++)
            for (int x = 8; x <= 10; x++) world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
        Assert.True(DisposalRules.Disposes(world, db, db.CreatureById(Dk2TerrainFixture.ImpCreatureId), Landing, Keeper));
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 10));
        var sim = new GameSimulation(db, world, 7, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, Landing)));
        Assert.False(imp.InHand);
        Assert.Contains(imp, world.Creatures);
        Assert.Equal(199, imp.Dk2StateId);
        Assert.Equal(FightTargetKind.Door, imp.FightTargetKind);
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.TortureChamberTerrain, false)]
    [InlineData(Dk2TerrainFixture.TortureChamberTerrain, true)]
    [InlineData(Dk2TerrainFixture.PortalTerrain, false)]
    [InlineData(Dk2TerrainFixture.PortalTerrain, true)]
    public void ALevelPlacedKeeperWorkerChecksCombatBeforeItsOwnRoom(byte terrain, bool builtDoor)
    {
        CreatureDef worker = Dk2TerrainFixture.Goblin() with { CreatureId = 99,
            Flags = Dk2TerrainFixture.Goblin().Flags | CreatureFlags.IsWorker };
        (GameDatabase db, WorldState world) = Setup(flags: builtDoor ? (byte)0 : (byte)2, additional: worker);
        for (int y = 9; y <= 11; y++)
            for (int x = 8; x <= 10; x++) world.Tile(new TilePos(x, y)).TerrainId = terrain;
        world.Tile(DoorTile).OwnerPlayerId = Keeper;
        if (terrain == Dk2TerrainFixture.TortureChamberTerrain)
            world.SpawnObject(db, TortureRules.WheelObjectId, Keeper, world.TileIndex(Landing)).AutoPlaced = true;
        else Assert.True(DisposalRules.Disposes(world, db, worker, Landing, Keeper));
        CreatureState creature = world.SpawnCreature(db, 99, Keeper, Landing);
        creature.Flags |= LevelPlacedCreatureRules.PlacedAsDropped;
        StatePlacement.Place(creature, DropLandingRules.LandingState);
        if (terrain == Dk2TerrainFixture.TortureChamberTerrain)
            Assert.NotNull(TortureRules.FreeDeviceAt(world, db, Keeper, world.TileIndex(Landing), creature.Id));
        new GameSimulation(db, world, 7, [new AutoFurnitureSystem()]).Tick();
        Assert.Equal(builtDoor ? 199 : terrain == Dk2TerrainFixture.PortalTerrain ? 197 : 131, creature.Dk2StateId);
        Assert.Equal(builtDoor ? FightTargetKind.Door : FightTargetKind.None, creature.FightTargetKind);
    }

    [Fact]
    public void OrdinaryCreatureSelectionPrecedesTheDoor()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState creature = GettingUp(db, world);
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(8, 10));
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();
        Assert.Equal(enemy.Id, creature.FightTargetId);
        Assert.Equal(FightTargetKind.Creature, creature.FightTargetKind);
        Assert.False(creature.AttackAbnormalTargets);
        Assert.Equal(199, creature.Dk2StateId);
    }

    [Fact]
    public void NorthDoorWinsBeforeEastDoor()
    {
        (GameDatabase db, WorldState world) = Setup();
        DoorState north = PlaceDoor(world, db, new TilePos(9, 9), Rival);
        CreatureState creature = GettingUp(db, world);
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();
        Assert.Equal(north.Id, creature.AttackingId);
        Assert.Equal(north.Id, creature.FightTargetId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FightEligibilityAndGetUpActionHoldAreRespected(bool ineligible)
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState creature = GettingUp(db, world);
        if (ineligible) creature.Flags = (byte)(creature.Flags & ~(byte)CreatureFightFlags.MayLookForAFight);
        else creature.ActionUntilTick = 20;
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();
        Assert.False(creature.AttackAbnormalTargets);
        Assert.Equal(ineligible ? DropLandingRules.StandingState : DropLandingRules.GettingUpState, creature.Dk2StateId);
    }

    [Fact]
    public void ALevelPlacedWorkerAlsoChecksTheAdjacentDoor()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, Landing);
        creature.Flags |= LevelPlacedCreatureRules.PlacedAsDropped;
        StatePlacement.Place(creature, DropLandingRules.LandingState);
        new GameSimulation(db, world, 7, [new AutoFurnitureSystem()]).Tick();
        Assert.Equal(FightTargetKind.Door, creature.FightTargetKind);
        Assert.Equal(world.Doors[0].Id, creature.AttackingId);
    }

    [Fact]
    public void RemovalOfTheSelectedDoorClearsTheForcedTarget()
    {
        (GameDatabase db, WorldState world) = Setup();
        CreatureState creature = GettingUp(db, world);
        var sim = new GameSimulation(db, world, 7, [new CreatureStatusSystem(), new CombatSystem(), new DoorRemovalSystem()]);
        sim.Tick();
        Assert.True(creature.AttackAbnormalTargets);
        world.Doors[0].Health = 0;
        sim.Run(3);
        Assert.Empty(world.Doors);
        Assert.False(creature.AttackAbnormalTargets);
        Assert.Equal(CombatRules.NoTarget, creature.AttackingId);
    }

    [Fact]
    public void SavingDuringGetUpPreservesTheDoorCombatContinuation()
    {
        (GameDatabase db, WorldState world) = Setup();
        DoorState door = world.Doors[0];
        CreatureState creature = GettingUp(db, world);
        creature.ActionUntilTick = 3;
        static ISimulationSystem[] Systems() => [new CreatureStatusSystem(), new CombatSystem(),
            new CreatureMovementSystem(), new DoorRemovalSystem()];
        var sim = new GameSimulation(db, world, 7, Systems());
        sim.Tick();
        GameSimulation loaded = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, Systems());
        for (int i = 0; i < 60; i++)
        {
            sim.Tick();
            loaded.Tick();
            Assert.Equal(sim.StateHash(), loaded.StateHash());
        }
        Assert.True(door.Health < db.Doors[1].Health);
    }

    private static CreatureState GettingUp(GameDatabase db, WorldState world)
    {
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Landing);
        StatePlacement.Place(creature, DropLandingRules.GettingUpState);
        creature.ActionUntilTick = 0;
        return creature;
    }

    [Theory]
    [InlineData(0, 0, -2048)]
    [InlineData(512, 2048, 0)]
    [InlineData(1024, 0, 2048)]
    [InlineData(1536, -2048, 0)]
    public void GettingUpAppliesTheDefinitionStepAlongTheFacing(int heading, int dx, int dy)
    {
        (GameDatabase db, WorldState world, CreatureState creature) = StepSetup();
        creature.Heading = heading;
        int x = creature.PosX, y = creature.PosY;
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();
        Assert.Equal((x + dx, y + dy), (creature.PosX, creature.PosY));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheGetUpStepCannotEnterSolidTerrainOrCrossAFloorSlotBoundary(bool floorBoundary)
    {
        (GameDatabase db, WorldState world, CreatureState creature) = StepSetup(-4096);
        if (floorBoundary)
            for (int y = 8; y <= 10; y++)
                for (int x = 8; x <= 10; x++)
                    world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.TrainingRoomTerrain;
        else world.Tile(new TilePos(9, 9)).TerrainId = Dk2TerrainFixture.Rock;
        int startX = creature.PosX, startY = creature.PosY;
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();
        Assert.Equal((startX, startY), (creature.PosX, creature.PosY));
    }

    [Fact]
    public void DoorNeighboursAreMeasuredAfterTheGetUpStep()
    {
        (GameDatabase db, WorldState world, CreatureState creature) = StepSetup();
        DoorState door = PlaceDoor(world, db, new TilePos(10, 9), Rival);
        creature.PosY = (10 << 12) + 100;
        new GameSimulation(db, world, 7, [new CreatureStatusSystem()]).Tick();
        Assert.Equal(new TilePos(9, 9), creature.Tile);
        Assert.Equal(door.Id, creature.AttackingId);
        Assert.Equal(199, creature.Dk2StateId);
    }

    private static (GameDatabase, WorldState, CreatureState) StepSetup(int stepY = -2048)
    {
        CreatureDef definition = Dk2TerrainFixture.Database().CreatureById(Dk2TerrainFixture.GoblinCreatureId)
            with { CreatureId = 99, GetUpStepY = stepY };
        GameDatabase db = Dk2TerrainFixture.Database(definition);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < 19; y++)
            for (int x = 1; x < 19; x++)
            {
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(new TilePos(x, y)).OwnerPlayerId = Keeper;
            }
        CreatureState creature = world.SpawnCreature(db, 99, Keeper, Landing);
        StatePlacement.Place(creature, DropLandingRules.GettingUpState);
        return (db, world, creature);
    }

    private static (GameDatabase, WorldState) Setup(byte owner = Rival, byte flags = 0, TilePos? doorAt = null,
        CreatureDef? additional = null)
    {
        GameDatabase db = Dk2TerrainFixture.Database(additional is null ? [] : [additional]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < 19; y++)
            for (int x = 1; x < 19; x++)
            {
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(new TilePos(x, y)).OwnerPlayerId = Keeper;
            }
        PlaceDoor(world, db, doorAt ?? DoorTile, owner, flags);
        return (db, world);
    }

    private static DoorState PlaceDoor(WorldState world, GameDatabase db, TilePos at, byte owner, byte flags = 0)
    {
        world.Tile(at).OwnerPlayerId = owner;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = at.X, Y = at.Y, PlayerId = owner, Flags = flags, DoorId = 1, Unknown_0011 = new byte[3] }]);
        return world.DoorAt(at.X, at.Y)!;
    }
}
