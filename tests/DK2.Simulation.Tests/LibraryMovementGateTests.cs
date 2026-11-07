// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class LibraryMovementGateTests
{
    [Theory]
    [InlineData(ResearchStep.ChoosingState, false)]
    [InlineData(ResearchStep.AtTheShelfState, false)]
    [InlineData(ResearchStep.ReadingLongState, false)]
    [InlineData(ResearchStep.ReadingShortState, true)]
    [InlineData(ResearchStep.DisturbedState, true)]
    [InlineData(ResearchStep.CelebratingState, true)]
    [InlineData(ResearchStep.ReturningState, true)]
    public void AKeepersResearcherSteersOnlyInAMovementEnabledState(int state, bool moves)
    {
        var (db, world, reader) = Reader();
        StatePlacement.Place(reader, state);
        var position = reader.Body.Pos;
        var velocity = reader.Body.Vel;
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);

        sim.Tick();

        if (moves) Assert.True(reader.Body.Pos.X > position.X);
        else
        {
            Assert.Equal(position, reader.Body.Pos);
            Assert.Equal(velocity, reader.Body.Vel);
            Assert.Equal(512, reader.Heading);
        }
        Assert.Equal(1, reader.Body.PathTotalCount);
        Assert.False(reader.Body.PathFinished);
    }

    [Fact]
    public void ReturningToTheChooserDoesNotStepAlongTheRetainedRoute()
    {
        var (db, world, reader) = Reader();
        new GameSimulation(db, world, 1, [new AutoFurnitureSystem()]).Tick();
        StatePlacement.Place(reader, ResearchStep.ReturningState);
        reader.StateEnteredTick = -2;
        var position = reader.Body.Pos;
        var velocity = reader.Body.Vel;
        var sim = new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(new ResearchTurn(), new CreatureMovementSystem())]);

        sim.Tick();

        Assert.Equal(ResearchStep.ChoosingState, reader.Dk2StateId);
        Assert.Equal(position, reader.Body.Pos);
        Assert.Equal(velocity, reader.Body.Vel);
        Assert.Equal(1, reader.Body.PathTotalCount);
        Assert.False(reader.Body.PathFinished);
    }

    private sealed class ResearchTurn : ICreatureUpdateStep
    {
        public string Name => "ResearchReturn";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature) =>
            ResearchStep.Turn(context, Name, creature);
    }

    private static (GameDatabase Db, WorldState World, CreatureState Reader) Reader()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 4; y++)
        for (int x = 3; x <= 7; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), PlayerIds.FirstKeeper);
        CreatureState reader = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId,
            PlayerIds.FirstKeeper, new TilePos(4, 3));
        world.Player(PlayerIds.FirstKeeper).ResearchTarget = 1;
        reader.CurrentJob = JobType.Research;
        reader.Heading = 512;
        reader.Body.Pos = new Fixed16Vector(reader.PosX << 4, reader.PosY << 4);
        var end = reader.Body.Pos + new Fixed16Vector(2 * Fixed16.One, 0);
        RouteWindow.Load(reader.Body, [end], end, 0);
        reader.Body.Vel = new Fixed16Vector(3000, -1000);
        return (db, world, reader);
    }
}
