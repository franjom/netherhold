// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using Xunit;

namespace DK2.Simulation.Tests;

/// <summary>Job 24 (<c>0x0045A290</c>) and state 225 (<c>0x0045A3E0</c>): their world draws (ADR-0674).</summary>
public sealed class ExploreTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const uint Seed = 42;
    private const int TravelToPoint = 6;   // StateShadow.TravelToPoint

    /// <summary>With no neutral floor on the map every one of the arm's 20 tries is refused: 40 draws,
    /// width then height, and the job fails.</summary>
    [Fact]
    public void TheArmDrawsTwentyPointsWhenNoTileIsNeutral()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.ThiefCreatureId, Keeper, new TilePos(5, 5));
        bool armed = true;
        uint before = 0, after = 0;
        GameSimulation sim = Sim(db, world, ctx => { before = SeedOf(ctx); armed = ExploreRules.Arm(ctx, c); after = SeedOf(ctx); });

        sim.Tick();

        var expected = new Dk2Lcg(before);
        for (int i = 0; i < ExploreRules.ArmTries; i++) { expected.NextInt(world.Width); expected.NextInt(world.Height); }
        Assert.False(armed);
        Assert.Equal(0, c.ExploreStepsLeft);
        Assert.Equal(expected.Seed, after);
    }

    /// <summary>With the open floor neutral the arm stops at the first open draw: 10 steps, a walk
    /// resuming 225, and two draws per try up to that one.</summary>
    [Fact]
    public void TheArmTakesTheFirstNeutralFloorAndWalksResuming225()
    {
        (GameDatabase db, WorldState world) = Open();
        Own(world, PlayerIds.Neutral);
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.ThiefCreatureId, Keeper, new TilePos(5, 5));
        bool armed = false;
        uint before = 0, after = 0;
        GameSimulation sim = Sim(db, world, ctx => { before = SeedOf(ctx); armed = ExploreRules.Arm(ctx, c); after = SeedOf(ctx); });

        sim.Tick();

        var expected = new Dk2Lcg(before);
        for (int i = 0; i < ExploreRules.ArmTries; i++)
        {
            int x = expected.NextInt(world.Width), y = expected.NextInt(world.Height);
            if (world.Tile(x, y).OwnerPlayerId == PlayerIds.Neutral) break;
        }
        Assert.True(armed);
        Assert.Equal(ExploreRules.Steps, c.ExploreStepsLeft);
        Assert.Equal(TravelToPoint, c.Dk2StateId);
        Assert.Equal(ExploreRules.ExploringState, c.UtilityNewState);
        Assert.Equal(expected.Seed, after);
    }

    /// <summary>State 225 with steps left spends one and draws <c>random(5)</c> twice per try; with no
    /// unowned floor around, all five tries are refused (10 draws) and the creature goes idle. With
    /// none left it draws nothing.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 10)]
    public void AStepDrawsFiveNeighbourTriesOnlyWithStepsLeft(int steps, int draws)
    {
        (GameDatabase db, WorldState world) = Open();
        Own(world, PlayerIds.Neutral);
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.ThiefCreatureId, Keeper, new TilePos(5, 5));
        c.ExploreStepsLeft = steps;
        bool stepped = true;
        uint before = 0, after = 0;
        GameSimulation sim = Sim(db, world, ctx => { before = SeedOf(ctx); stepped = ExploreRules.Step(ctx, c); after = SeedOf(ctx); });

        sim.Tick();

        var expected = new Dk2Lcg(before);
        for (int i = 0; i < draws; i++) expected.NextInt(5);
        Assert.False(stepped);
        Assert.Equal(Math.Max(0, steps - 1), c.ExploreStepsLeft);
        Assert.Equal(expected.Seed, after);
    }

    /// <summary>A keeper's creature in 225 with unowned floor around walks on, resuming 225, one step
    /// spent; the job system runs the state (<see cref="ExploreRules.KeeperTurn"/>).</summary>
    [Fact]
    public void AnExplorerInState225WalksOnThroughTheJobSystem()
    {
        (GameDatabase db, WorldState world) = Open();
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.ThiefCreatureId, Keeper, new TilePos(5, 5));
        CreatureStateMachine.TrySetState(new StateHookContext(world, db), c, ExploreRules.ExploringState);
        c.ExploreStepsLeft = 2;
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(Seed)), [new CreatureJobSystem(), new CreatureMovementSystem()]);

        sim.Tick();

        Assert.Equal(1, c.ExploreStepsLeft);
        Assert.Equal(TravelToPoint, c.Dk2StateId);
        Assert.Equal(ExploreRules.ExploringState, c.UtilityNewState);
    }

    private static uint SeedOf(SimulationContext context) => ((Dk2Lcg)context.Rng.Algorithm).Seed;

    private static GameSimulation Sim(GameDatabase db, WorldState world, Action<SimulationContext> act) =>
        new(db, world, new SimRandom(new Dk2Lcg(Seed)), [new Probe(act)]);

    private static (GameDatabase, WorldState) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.DirtPath;
                tile.OwnerPlayerId = PlayerIds.Unowned;
            }
        return (db, world);
    }

    private static void Own(WorldState world, byte owner)
    {
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).OwnerPlayerId = owner;
    }

    private sealed class Probe(Action<SimulationContext> act) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => act(context);
    }
}
