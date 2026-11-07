// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class LibraryStopRouteTests
{
    [Fact]
    public void AStoppedLibraryReturnStillTakesItsStationaryFacingStep()
    {
        var (db, world, reader) = Reader();
        reader.Heading = 1050;
        reader.StopMoving();
        StatePlacement.Place(reader, ResearchStep.ReturningState);
        Assert.Equal(1, reader.Body.PathTotalCount);
        Assert.False(reader.Body.PathFinished);

        new GameSimulation(db, world, 1, [new CreatureMovementSystem()]).Tick();

        Assert.Equal(1391, reader.Heading);
        Assert.True(reader.Body.PathFinished);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheEntryWalkResumesAtWholeProgressWithEitherFinishedFlag(bool finished)
    {
        var (db, world, reader) = Walk(entry: true, whole: true, finished);
        var position = (reader.PosX, reader.PosY);

        new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new CreatureJobSystem())]).Tick();

        Assert.Equal((ResearchStep.ChoosingState, CreatureStateMachine.Idle), (reader.Dk2StateId, reader.UtilityNewState));
        Assert.Equal(position, (reader.PosX, reader.PosY));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheInternalWalkResumesAtWholeProgressWithEitherFinishedFlag(bool finished)
    {
        var (db, world, reader) = Walk(entry: false, whole: true, finished);
        var position = (reader.PosX, reader.PosY);

        new GameSimulation(db, world, 1, [new CreatureUpdateSystem(new ResearchTurn())]).Tick();

        Assert.Equal((ResearchStep.AtTheShelfState, CreatureStateMachine.Idle), (reader.Dk2StateId, reader.UtilityNewState));
        Assert.Equal(position, (reader.PosX, reader.PosY));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIncompleteWalkKeepsItsResumeState(bool entry)
    {
        var (db, world, reader) = Walk(entry, whole: false, finished: false);
        int state = reader.Dk2StateId, resume = reader.UtilityNewState;
        ICreatureUpdateStep step = entry ? new CreatureJobSystem() : new ResearchTurn();

        new GameSimulation(db, world, 1, [new CreatureUpdateSystem(step)]).Tick();

        Assert.Equal((state, resume), (reader.Dk2StateId, reader.UtilityNewState));
    }

    private static (GameDatabase, WorldState, CreatureState) Reader()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 4; y++)
        for (int x = 3; x <= 7; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Library), PlayerIds.FirstKeeper);
        CreatureState reader = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId,
            PlayerIds.FirstKeeper, new TilePos(4, 3));
        reader.CurrentJob = JobType.Research;
        StatePlacement.Place(reader, ResearchStep.ChoosingState);
        reader.HeldPlace = new RoomPlace(reader.Tile.Y * world.Width + reader.Tile.X, RoomCapacityRules.Units);
        reader.Body.Pos = new Fixed16Vector(reader.PosX << 4, reader.PosY << 4);
        world.Player(PlayerIds.FirstKeeper).ResearchTarget = 1;
        return (db, world, reader);
    }

    private static (GameDatabase, WorldState, CreatureState) Walk(bool entry, bool whole, bool finished)
    {
        var (db, world, reader) = Reader();
        StatePlacement.Place(reader, entry ? 7 : 6);
        reader.UtilityNewState = entry ? ResearchStep.ChoosingState : ResearchStep.AtTheShelfState;
        Fixed16Vector point = reader.Body.Pos + new Fixed16Vector(whole ? 0 : Fixed16.One, 0);
        RouteWindow.Load(reader.Body, [point], point, 0);
        reader.Body.PathFinished = finished;
        Assert.Equal(whole ? Fixed16.One : 0, FollowPathRules.Progress(reader.Body));
        return (db, world, reader);
    }

    private sealed class ResearchTurn : ICreatureUpdateStep
    {
        public string Name => "Library internal walk";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature) => ResearchStep.Turn(context, Name, creature);
    }
}
