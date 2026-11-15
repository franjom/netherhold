// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CombatPitCaptiveTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;
    private static readonly TilePos Floor = new(6, 4);
    private static readonly TilePos Outside = new(10, 4);

    private static (GameDatabase Database, WorldState World) Setup()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = Dk2TerrainFixture.Sellable(WorldState.FromMap(database,
            Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)1, Keeper, Rival]));
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = Keeper;
        }
        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            RoomRules.Place(database, ref world.Tile(x, y), database.Room(RoomTypeId.CombatPit), Keeper);
        return (database, world);
    }

    private static void Drop(GameSimulation simulation, CreatureState creature)
    {
        simulation.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, creature.Id)));
        Assert.True(creature.InHand);
        simulation.Tick(InputFrame.Of(new DropCommand(Keeper, Floor)));
        simulation.Tick();
    }

    private static void LandWorker(GameSimulation simulation, CreatureState creature)
    {
        // The input gate refuses workers over pit floors; exercise native76 directly.
        creature.PosX = creature.LastPosX = CreatureState.CentreOf(Floor.X);
        creature.PosY = creature.LastPosY = CreatureState.CentreOf(Floor.Y);
        creature.LandingPending = true;
        creature.LandingDropperPlayerId = Keeper;
        StatePlacement.Place(creature, 76);
        simulation.Tick();
    }

    [Theory]
    [InlineData(Keeper)]
    [InlineData(Rival)]
    public void APlacedFighterBindsToThePitsKeeperWithoutBeingFreedByThePrisonStep(byte owner)
    {
        var (database, world) = Setup();
        CreatureState fighter = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, owner, Floor);
        fighter.CombatPitFighter = true;
        StatePlacement.Place(fighter, CombatPitRules.PlacingState);
        fighter.PitStateEnteredTick = -1;
        var simulation = new GameSimulation(database, world, 42, [new CombatPitSystem(), new PrisonSystem()]);

        simulation.Tick();

        Assert.Equal(CombatPitRules.WaitingState, fighter.Dk2StateId);
        Assert.Equal(Keeper, fighter.PrisonOwnerPlayerId);
        Assert.True(fighter.CombatPitFighter);
        Assert.True(HandRules.HasPickupPermission(world, database, fighter, new PlayerId(Keeper)));
    }

    [Fact]
    public void ThePrisonStepDoesNotReleaseAnEnemyFighterOrCountItAsACellOccupant()
    {
        var (database, world) = Setup();
        CreatureState fighter = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Rival, Floor);
        fighter.PrisonOwnerPlayerId = Keeper;
        fighter.CombatPitFighter = true;
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        var simulation = new GameSimulation(database, world, 42, [new PrisonSystem()]);

        simulation.Tick();

        Assert.Equal(Keeper, fighter.PrisonOwnerPlayerId);
        Assert.Equal(CombatPitRules.WaitingState, fighter.Dk2StateId);
        Assert.Null(PrisonRules.PrisonerOn(world, Floor.Y * world.Width + Floor.X));
    }

    [Fact]
    public void AnOwnBoundPitFighterStillRegeneratesOnItsOwnLand()
    {
        var (database, world) = Setup();
        database = new GameDatabase(database.Terrain.Values, database.Rooms.Values,
            database.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.GoblinCreatureId
                ? c with { OwnLandHealthIncreasePerSecond = 67 } : c), database.Variables,
            objects: database.Objects.Values);
        CreatureState fighter = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, Floor);
        fighter.PrisonOwnerPlayerId = Keeper;
        fighter.CombatPitFighter = true;
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        fighter.Health = 100;
        var simulation = new GameSimulation(database, world, 42, [new CreatureStatusSystem()]);

        simulation.Tick();

        Assert.True(fighter.Health > 100);
    }

    [Theory]
    [InlineData(78)]
    [InlineData(255)]
    [InlineData(217)]
    public void ThePrisonStepPreservesTheBindingWhileHeldStunnedOrDyingOutsideACell(int state)
    {
        var (database, world) = Setup();
        CreatureState captive = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Rival, Outside);
        captive.PrisonOwnerPlayerId = Keeper;
        captive.InHand = state == 78;
        StatePlacement.Place(captive, state);
        var simulation = new GameSimulation(database, world, 42, [new PrisonSystem()]);

        simulation.Tick();

        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
        Assert.Equal(state, captive.Dk2StateId);
    }

    [Theory]
    [InlineData(Keeper)]
    [InlineData(Rival)]
    public void AKnockedOutFighterWithAKeeperBindingCanStillBeCarried(byte owner)
    {
        var (database, world) = Setup();
        CreatureState fighter = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, owner, Floor);
        fighter.PrisonOwnerPlayerId = Keeper;
        StatePlacement.Place(fighter, CreatureDeathRules.DyingState);

        Assert.True(CarryRules.IsCarryable(fighter));
    }

    [Fact]
    public void AWalkFromIdleResumingPrisonEscapeStillCountsAsAPrisonInmate()
    {
        var (database, world) = Setup();
        CreatureState captive = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Rival, Outside);
        captive.PrisonOwnerPlayerId = Keeper;
        StatePlacement.Place(captive, 6);
        captive.StateBeforeUtilityState = CreatureStateMachine.Idle;
        captive.UtilityNewState = JailBreakRules.EscapingState;

        Assert.True(PrisonRules.IsPrisonInmate(captive));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEnemyCaptiveDroppedOnTheFloorEnters137BeforeTheWorkerSplit(bool worker)
    {
        var (database, world) = Setup();
        CreatureState captive = world.SpawnCreature(database,
            worker ? Dk2TerrainFixture.ImpCreatureId : Dk2TerrainFixture.GoblinCreatureId, Rival, Outside);
        captive.PrisonOwnerPlayerId = Keeper;
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);

        if (worker) LandWorker(simulation, captive);
        else Drop(simulation, captive);

        Assert.Equal(CombatPitRules.ArrivingState, captive.Dk2StateId);
        Assert.Equal(Keeper, captive.PrisonOwnerPlayerId);
        Assert.True(captive.CombatPitFighter);
    }

    [Theory]
    [InlineData(Keeper, false)]
    [InlineData((byte)1, true)]
    public void OnlyAWorkerWhosePlayerNumberIsBelowThreeUsesTheOrdinaryPitDrop(byte owner, bool entersPit)
    {
        var (database, world) = Setup();
        if (owner == 1)
            for (int y = 2; y <= 6; y++)
            for (int x = 4; x <= 8; x++) world.Tile(x, y).OwnerPlayerId = PlayerIds.Neutral;
        CreatureState worker = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, owner, Outside);
        worker.PrisonOwnerPlayerId = Keeper;
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);

        LandWorker(simulation, worker);

        Assert.Equal(entersPit, worker.Dk2StateId == CombatPitRules.ArrivingState);
        Assert.Equal(entersPit, worker.CombatPitFighter);
    }

    [Theory]
    [InlineData(Keeper)]
    [InlineData(Rival)]
    public void AnOwnFighterRetainingAKeeperBindingCanStillBeDroppedIntoItsPortal(byte binding)
    {
        var (database, world) = Setup();
        var centre = new TilePos(10, 4);
        for (int y = 3; y <= 5; y++)
        for (int x = 9; x <= 11; x++)
            RoomRules.Place(database, ref world.Tile(x, y), database.Room(RoomTypeId.Portal), Keeper);
        CreatureState fighter = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, Floor);
        fighter.PrisonOwnerPlayerId = binding;
        fighter.CombatPitFighter = true;
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);
        simulation.Tick(InputFrame.Of(new TakeCreatureCommand(Keeper, fighter.Id)));
        Assert.True(fighter.InHand);
        Assert.Equal(binding, fighter.PrisonOwnerPlayerId);

        simulation.Tick(InputFrame.Of(new DropCommand(Keeper, centre)));
        simulation.Tick();

        Assert.False(fighter.InHand);
        Assert.Equal(LeavingRules.PortalDropState, fighter.Dk2StateId);
    }
}
