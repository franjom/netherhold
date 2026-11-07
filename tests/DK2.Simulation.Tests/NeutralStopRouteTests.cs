// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class NeutralStopRouteTests
{
    private const byte WalkerType = 99;

    private static (GameDatabase Db, WorldState World, CreatureState Walker) Walker(bool zeroSpeed = false)
    {
        CreatureDef definition = Dk2TerrainFixture.Goblin() with
        {
            CreatureId = WalkerType, TurnRate = 1364,
        };
        if (zeroSpeed) definition = definition with { Speed = new Fp32(0) };
        GameDatabase db = Dk2TerrainFixture.Database(definition);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int x = 1; x <= 12; x++) world.Tile(x, 3).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState walker = world.SpawnCreature(db, WalkerType, PlayerIds.Neutral, new TilePos(5, 3));
        walker.Body.Pos = new Fixed16Vector(walker.PosX << 4, walker.PosY << 4);
        return (db, world, walker);
    }

    [Fact]
    public void StoppingANeutralLeavesOneUnfinishedPointWithWholeProgress()
    {
        var (_, _, walker) = Walker();
        walker.Body.Vel = new Fixed16Vector(3000, -1000);

        walker.StopMoving();

        Assert.Equal(1, walker.Body.PathTotalCount);
        Assert.Equal(1, walker.Body.PathBufferCount);
        Assert.False(walker.Body.PathFinished);
        Assert.Equal(walker.Body.Pos, walker.Body.Subpath[0]);
        Assert.Equal(walker.Body.Pos, walker.Body.PathTargetPos);
        Assert.Equal(Fixed16Vector.Zero, walker.Body.Vel);
        Assert.Equal(0, walker.Body.PathTotalDist);
        Assert.Equal(Fixed16.One, FollowPathRules.Progress(walker.Body));
    }

    [Fact]
    public void NeutralIdleReturnResumesBeforeItsSingleStationaryFacingStep()
    {
        var (db, world, walker) = Walker();
        walker.Heading = 1617;
        StatePlacement.Place(walker, 6);
        walker.UtilityNewState = CreatureStateMachine.Idle;
        Fixed16Vector start = walker.Body.Pos;
        RouteWindow.Load(walker.Body, [start], start, 0);
        var sim = new GameSimulation(db, world, 1,
            [new CreatureUpdateSystem(new TravelArrivalSystem(), new CreatureJobSystem(), new CreatureMovementSystem())]);

        sim.Tick();

        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
        Assert.Equal(1617, walker.Heading);
        Assert.Equal(1, walker.Body.PathTotalCount);
        Assert.False(walker.Body.PathFinished);
        sim.Tick();
        Assert.Equal(CreatureJobSystem.NeutralIdleState, walker.Dk2StateId);
        Assert.Equal(1958, walker.Heading);
        Assert.True(walker.Body.PathFinished);
        Assert.Equal(start, walker.Body.Pos);
        sim.Tick();
        Assert.Equal(1958, walker.Heading);
        Assert.Equal(start, walker.Body.Pos);
    }

    [Fact]
    public void NeutralImpKeepsTheExistingWorkerStopAdapter()
    {
        var (db, world, _) = Walker();
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.Neutral, new TilePos(5, 3));

        imp.StopMoving();

        Assert.Equal(0, imp.Body.PathTotalCount);
        Assert.False(imp.HasRoute);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStoppedNeutralOnlyRunsItsFacingStepWithNonzeroSpeed(bool zeroSpeed)
    {
        var (db, world, walker) = Walker(zeroSpeed);
        StatePlacement.Place(walker, CreatureJobSystem.NeutralIdleState);
        walker.Heading = 1617;
        walker.StopMoving();
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);

        sim.Tick();

        Assert.Equal(zeroSpeed ? 1617 : 1958, walker.Heading);
        Assert.Equal(!zeroSpeed, walker.Body.PathFinished);
    }

    [Theory]
    [InlineData(CreatureStateMachine.Idle, false)]
    [InlineData(CreatureJobSystem.NeutralIdleState, true)]
    public void NeutralSteeringUsesTheCurrentStatesMovementFlag(int state, bool moves)
    {
        var (db, world, walker) = Walker();
        StatePlacement.Place(walker, state);
        walker.Heading = 512;
        int beforeX = walker.PosX;
        Fixed16Vector target = walker.Body.Pos + new Fixed16Vector(Fixed16.One, 0);
        RouteWindow.Load(walker.Body, [target], target, 0);
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);

        sim.Tick();

        if (moves) Assert.True(walker.PosX > beforeX);
        else Assert.Equal(beforeX, walker.PosX);
    }
}
