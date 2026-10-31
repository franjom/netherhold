// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class ReinforcementCornerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void State22FillsAtReleaseUnlessItsDigMarkCancelledTheTask(bool cancelled)
    {
        var run = Setup(1, 1, false);
        var sim = new GameSimulation(run.Sim.Database, run.Sim.World, 1, Dk2TerrainFixture.DefaultSystems());
        CreatureState imp = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(6, 5));
        imp.TargetTileIndex = 5 * sim.World.Width + 5;
        imp.TargetWork = TileWorkKind.Reinforce;
        imp.Dk2StateId = 22;
        if (cancelled) sim.World.Tile(5, 5).TaggedBy = PlayerMask.None.With(3);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(cancelled ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.ReinforcedWall,
            sim.World.Tile(run.Fill).TerrainId);
    }

    [Fact]
    public void NeutralReinforcedCandidateHealsWithoutOwnershipChange()
    {
        var run = Setup(1, 1, false);
        run.Sim.World.Tile(run.Fill).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        run.Sim.Tick(InputFrame.Empty);
        Assert.Equal(900, run.Sim.World.Tile(run.Fill).Health);
        Assert.Equal(2, run.Sim.World.Tile(run.Fill).OwnerPlayerId);
    }

    [Fact]
    public void OwnerZeroFinishedDiagonalIsNotTheNeutralPlayer()
    {
        var run = Setup(1, 1, false);
        run.Sim.World.Tile(6, 6).OwnerPlayerId = 0;
        run.Sim.Tick(InputFrame.Empty);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, run.Sim.World.Tile(run.Fill).TerrainId);
    }

    [Fact]
    public void State22AcceptsFullNonOwnableTargetAsFinished()
    {
        var run = Setup(1, 1, false);
        run.Sim.World.Tile(5, 5).TerrainId = Dk2TerrainFixture.Rock;
        run.Sim.World.Tile(5, 5).Health = 660;
        var sim = new GameSimulation(run.Sim.Database, run.Sim.World, 1, Dk2TerrainFixture.DefaultSystems());
        CreatureState imp = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(6, 5));
        imp.TargetTileIndex = 5 * sim.World.Width + 5;
        imp.TargetWork = TileWorkKind.Reinforce;
        imp.Dk2StateId = 22;
        sim.Tick(InputFrame.Empty);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(run.Fill).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(5, 5).TerrainId);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(-1, -1)]
    public void FinishedDiagonalFillsEitherSharedNeutralWall(int dx, int dy)
    {
        foreach (bool swap in new[] { false, true })
        {
            var run = Setup(dx, dy, swap);
            run.Sim.Tick(InputFrame.Empty);
            Assert.Equal(Dk2TerrainFixture.ReinforcedWall, run.Sim.World.Tile(run.Fill).TerrainId);
            Assert.Equal(3, run.Sim.World.Tile(run.Fill).OwnerPlayerId);
            Assert.Equal(900, run.Sim.World.Tile(run.Fill).Health);
        }
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.Gold, 2, 100, false, false)]
    [InlineData(Dk2TerrainFixture.ImpenetrableRock, 2, 0, false, false)]
    [InlineData(Dk2TerrainFixture.Rock, 4, 165, false, false)]
    [InlineData(Dk2TerrainFixture.Rock, 0, 165, false, false)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 660, false, false)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 165, true, false)]
    [InlineData(Dk2TerrainFixture.Rock, 2, 165, false, true)]
    public void InvalidCornerDoesNotFill(byte terrain, byte owner, int health, bool blockedSide, bool damagedDiagonal)
    {
        var run = Setup(1, 1, false);
        ref TileState fill = ref run.Sim.World.Tile(run.Fill);
        fill.TerrainId = terrain;
        fill.OwnerPlayerId = owner;
        fill.Health = health;
        if (blockedSide) run.Sim.World.Tile(6, 5).TerrainId = Dk2TerrainFixture.Rock;
        if (damagedDiagonal) run.Sim.World.Tile(6, 6).Health--;
        run.Sim.Tick(InputFrame.Empty);
        Assert.Equal(terrain, run.Sim.World.Tile(run.Fill).TerrainId);
        Assert.Equal(owner, run.Sim.World.Tile(run.Fill).OwnerPlayerId);
        Assert.Equal(health, run.Sim.World.Tile(run.Fill).Health);
    }

    private sealed class FillSystem(TilePos at) : ISimulationSystem
    {
        public string Name => "Corner test";
        public void Execute(SimulationContext context) => ReinforcementRules.FillCorners(context, at, 3);
    }

    private static (GameSimulation Sim, TilePos Fill) Setup(int dx, int dy, bool swap)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)2, (byte)3, (byte)4]);
        Set(5, 5, Dk2TerrainFixture.ReinforcedWall, 3, 900);
        Set(5 + dx, 5 + dy, Dk2TerrainFixture.ReinforcedWall, 4, 900);
        var fill = swap ? new TilePos(5 + dx, 5) : new TilePos(5, 5 + dy);
        var open = swap ? new TilePos(5, 5 + dy) : new TilePos(5 + dx, 5);
        Set(fill.X, fill.Y, Dk2TerrainFixture.Rock, 2, 165);
        Set(open.X, open.Y, Dk2TerrainFixture.DirtPath, 2, 1);
        return (new GameSimulation(db, world, 1, [new FillSystem(new TilePos(5, 5))]), fill);

        void Set(int x, int y, byte terrain, byte owner, int health)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = terrain;
            tile.OwnerPlayerId = owner;
            tile.Health = health;
        }
    }
}
