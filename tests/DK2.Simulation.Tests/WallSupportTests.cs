// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class WallSupportTests
{
    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();
    private static readonly PlayerId Keeper = 3, Rival = 4;
    private static readonly TilePos Centre = new(8, 6);

    private static GameSimulation Simulation()
    {
        WorldState world = WorldState.FromMap(Db, Dk2TerrainFixture.Map(), [Keeper.Value, Rival.Value]);
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                Put(world, new TilePos(x, y), Dk2TerrainFixture.Rock, PlayerIds.Neutral);
        Put(world, Centre, Dk2TerrainFixture.ClaimedPath, Rival);
        world.Tile(Centre).Health = 1;
        return new GameSimulation(Db, world, 4242, [new TileWorkSystem()]);
    }

    private static void Put(WorldState world, TilePos at, byte terrain, PlayerId owner)
    {
        world.Tile(at) = new TileState { TerrainId = terrain, OwnerPlayerId = owner.Value,
            Health = Db.TerrainById(terrain).MaxHealth };
    }

    private static void Attack(GameSimulation sim, TilePos? at = null)
    {
        TilePos position = at ?? Centre;
        sim.World.QueueWork(position.X, position.Y, TileWorkKind.AttackTile, Keeper);
        sim.Tick();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void EnemyFloorCompletionReleasesUnsupportedOrthogonalWalls(int dx, int dy)
    {
        GameSimulation sim = Simulation();
        var wall = new TilePos(Centre.X + dx, Centre.Y + dy);
        Put(sim.World, wall, Dk2TerrainFixture.ReinforcedWall, Rival);
        sim.World.Tile(wall).Health = 7;
        sim.World.Tile(wall).BurnTicksRemaining = 5;
        sim.World.Tile(wall).KeptManaGain = 9;
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(wall).TerrainId);
        Assert.Equal(PlayerIds.Neutral, sim.World.Tile(wall).OwnerPlayerId);
        Assert.Equal(Db.TerrainById(Dk2TerrainFixture.Rock).StartingHealth, sim.World.Tile(wall).Health);
        Assert.Equal(0, sim.World.Tile(wall).BurnTicksRemaining);
        Assert.Equal(0, sim.World.Tile(wall).KeptManaGain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyTheWallsOwnOtherFloorSupportsIt(bool sameOwner)
    {
        GameSimulation sim = Simulation();
        var wall = new TilePos(7, 6);
        Put(sim.World, wall, Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(6, 6), Dk2TerrainFixture.ClaimedPath, sameOwner ? Rival : Keeper);
        Attack(sim);
        Assert.Equal(sameOwner ? Dk2TerrainFixture.ReinforcedWall : Dk2TerrainFixture.Rock,
            sim.World.Tile(wall).TerrainId);
    }

    [Fact]
    public void APartialEnemyFloorAttackDoesNotReleaseWalls()
    {
        GameSimulation sim = Simulation();
        sim.World.Tile(Centre).Health = Db.TerrainById(Dk2TerrainFixture.ClaimedPath).MaxHealth;
        Put(sim.World, new TilePos(7, 6), Dk2TerrainFixture.ReinforcedWall, Rival);
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(7, 6).TerrainId);
    }

    [Fact]
    public void AnOwnedGoldOrthogonalWallIsReleasedWithoutMiningYield()
    {
        GameSimulation sim = Simulation();
        Put(sim.World, new TilePos(7, 6), Dk2TerrainFixture.Gold, Rival);
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(7, 6).TerrainId);
        Assert.Equal(PlayerIds.Neutral, sim.World.Tile(7, 6).OwnerPlayerId);
        Assert.Equal(0, sim.World.Tile(7, 6).GoldHeld);
    }

    [Fact]
    public void ADiagonalSeesOrthogonalWallRemovalBeforeItsPairTest()
    {
        GameSimulation sim = Simulation();
        Put(sim.World, new TilePos(7, 5), Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(7, 4), Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(8, 5), Dk2TerrainFixture.ReinforcedWall, Keeper);
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(8, 5).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(7, 5).TerrainId);
    }

    [Theory]
    [InlineData(899, false, false)]
    [InlineData(900, true, false)]
    [InlineData(900, false, true)]
    public void APairMemberMustBeFullNonGoldAndNonNeutral(int health, bool gold, bool neutral)
    {
        GameSimulation sim = Simulation();
        Put(sim.World, new TilePos(7, 5), Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(7, 4), Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(6, 5), gold ? Dk2TerrainFixture.Gold : Dk2TerrainFixture.ReinforcedWall,
            neutral ? PlayerIds.Neutral : Keeper);
        sim.World.Tile(6, 5).Health = gold ? Db.TerrainById(Dk2TerrainFixture.Gold).MaxHealth : health;
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(7, 5).TerrainId);
    }

    [Fact]
    public void DestroyingAnOwnWallDoesNotRunHostileFloorCleanup()
    {
        GameSimulation sim = Simulation();
        Put(sim.World, Centre, Dk2TerrainFixture.ReinforcedWall, Keeper);
        sim.World.Tile(Centre).Health = 1;
        Put(sim.World, new TilePos(7, 6), Dk2TerrainFixture.ReinforcedWall, Rival);
        sim.World.QueueWork(Centre.X, Centre.Y, TileWorkKind.DigOwnWall, Keeper);
        sim.Tick();
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(7, 6).TerrainId);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    public void EnemyFloorCompletionReleasesUnsupportedDiagonalWalls(int dx, int dy)
    {
        GameSimulation sim = Simulation();
        var wall = new TilePos(Centre.X + dx, Centre.Y + dy);
        Put(sim.World, wall, Dk2TerrainFixture.ReinforcedWall, Rival);
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(wall).TerrainId);
    }

    [Theory]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, 899)]
    [InlineData(Dk2TerrainFixture.Gold, 660)]
    public void DamagedOrGoldDiagonalWallsAreLeftAlone(byte terrain, int health)
    {
        GameSimulation sim = Simulation();
        var wall = new TilePos(7, 5);
        Put(sim.World, wall, terrain, Rival);
        sim.World.Tile(wall).Health = health;
        Attack(sim);
        Assert.Equal(terrain, sim.World.Tile(wall).TerrainId);
        Assert.Equal(health, sim.World.Tile(wall).Health);
    }

    [Theory]
    [InlineData(0, -1, -1, 0)]
    [InlineData(0, -1, 1, 0)]
    [InlineData(-1, 0, 0, -1)]
    [InlineData(-1, 0, 0, 1)]
    public void AFullWallPairRetainsADiagonalEvenWithDifferentOwners(int ax, int ay, int bx, int by)
    {
        GameSimulation sim = Simulation();
        var wall = new TilePos(7, 5);
        Put(sim.World, wall, Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(wall.X + ax, wall.Y + ay), Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(wall.X + bx, wall.Y + by), Dk2TerrainFixture.ReinforcedWall, Keeper);
        // Keep pair members beside the captured floor from being released first.
        Put(sim.World, new TilePos(9, 5), Dk2TerrainFixture.ClaimedPath, Keeper);
        Put(sim.World, new TilePos(6, 6), Dk2TerrainFixture.ClaimedPath, Keeper);
        Attack(sim);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, sim.World.Tile(wall).TerrainId);
    }

    [Fact]
    public void ARoomTransferChecksEverySlabWithoutAnEffectsContext()
    {
        GameSimulation sim = Simulation();
        for (int x = 7; x <= 9; x++)
            Put(sim.World, new TilePos(x, 6), Dk2TerrainFixture.TreasuryTerrain, Rival);
        Put(sim.World, new TilePos(6, 6), Dk2TerrainFixture.ReinforcedWall, Rival);
        Put(sim.World, new TilePos(10, 6), Dk2TerrainFixture.ReinforcedWall, Rival);
        RoomInstance room = RoomIndex.RoomAt(sim.World, Db, (6 * sim.World.Width) + 8)!;
        RoomHealthRules.Apply(sim.World, Db, room, -1_000_000, Keeper);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(6, 6).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(10, 6).TerrainId);
    }

    [Fact]
    public void EnemyFloorCompletionAtTheMapEdgeIsBoundsSafe()
    {
        GameSimulation sim = Simulation();
        Put(sim.World, new TilePos(0, 0), Dk2TerrainFixture.ClaimedPath, Rival);
        sim.World.Tile(0, 0).Health = 1;
        Put(sim.World, new TilePos(1, 0), Dk2TerrainFixture.ReinforcedWall, Rival);
        Attack(sim, new TilePos(0, 0));
        Assert.Equal(Dk2TerrainFixture.Rock, sim.World.Tile(1, 0).TerrainId);
    }
}
