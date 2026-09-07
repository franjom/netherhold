// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

/// <summary>
/// Where a worker stands to do a job (Q44, Q49).
///
/// The rule under test is two clauses — standable now, and standable when the
/// job finishes — and these tests exist to show that each clause is carrying a
/// case the other one would get wrong. Drop the first and a worker plans to
/// stand inside a rock; drop the second and it entombs itself filling a tile in.
/// </summary>
public class WorkStanceTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    private static (GameDatabase Database, WorldState World, IPathGrid Grid) Setup()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = Dk2TerrainFixture.World();
        var grid = new WorldPathGrid(
            database, world, database.CreatureById(Dk2TerrainFixture.ImpCreatureId));
        return (database, world, grid);
    }

    private static void Put(WorldState world, GameDatabase database, TilePos at, byte terrainId, byte owner = 0)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = terrainId;
        tile.OwnerPlayerId = owner;
        tile.Health = database.TerrainById(terrainId).StartingHealth;
    }

    // ---- floor: worked from the tile itself (Q44 CONFIRMED) --------------

    /// <summary>
    /// The observation this whole type exists for: <i>"imps claim tiles by
    /// jumping on them"</i>.
    /// </summary>
    [Fact]
    public void ClaimingIsDoneStandingOnTheTile()
    {
        (GameDatabase database, WorldState world, IPathGrid grid) = Setup();
        var at = new TilePos(5, 2);
        Put(world, database, at, Dk2TerrainFixture.DirtPath);

        Assert.True(WorkStance.IsOnTheTile(database, world, grid, at, TileWorkKind.Claim));
    }

    [Fact]
    public void RepairingAndAttackingFloorAreDoneStandingOnTheTile()
    {
        (GameDatabase database, WorldState world, IPathGrid grid) = Setup();

        var mine = new TilePos(5, 2);
        Put(world, database, mine, Dk2TerrainFixture.ClaimedPath, Keeper);
        Assert.True(WorkStance.IsOnTheTile(database, world, grid, mine, TileWorkKind.RepairTile));

        var theirs = new TilePos(6, 2);
        Put(world, database, theirs, Dk2TerrainFixture.ClaimedPath, Rival);
        Assert.True(WorkStance.IsOnTheTile(database, world, grid, theirs, TileWorkKind.AttackTile));
    }

    // ---- walls: worked from beside (Q27 CONFIRMED) -----------------------

    [Theory]
    [InlineData(Dk2TerrainFixture.Rock, TileWorkKind.Dig)]
    [InlineData(Dk2TerrainFixture.Rock, TileWorkKind.Reinforce)]
    [InlineData(Dk2TerrainFixture.Gold, TileWorkKind.MineGold)]
    [InlineData(Dk2TerrainFixture.ReinforcedWall, TileWorkKind.DigOwnWall)]
    public void SolidTerrainIsAlwaysWorkedFromBeside(byte terrainId, TileWorkKind kind)
    {
        (GameDatabase database, WorldState world, IPathGrid grid) = Setup();
        var at = new TilePos(5, 2);
        Put(world, database, at, terrainId, terrainId == Dk2TerrainFixture.ReinforcedWall ? Keeper : (byte)0);

        Assert.False(WorkStance.IsOnTheTile(database, world, grid, at, kind));
    }

    // ---- the second clause, which only fill-in needs ---------------------

    /// <summary>
    /// The mirror of the fill-in case, and the reason the second clause cannot
    /// simply be "the tile is walkable now". Digging rock ends in dirt path —
    /// walkable — and is still worked from beside, because of the first clause.
    /// </summary>
    [Fact]
    public void DiggingEndsWalkableAndIsStillWorkedFromBeside()
    {
        (GameDatabase database, WorldState world, IPathGrid grid) = Setup();
        var at = new TilePos(5, 2);
        Put(world, database, at, Dk2TerrainFixture.Rock);

        Assert.Equal(
            Dk2TerrainFixture.DirtPath,
            TileWorkRules.OutcomeTerrainId(database, world.Tile(at), TileWorkKind.Dig));
        Assert.False(WorkStance.IsOnTheTile(database, world, grid, at, TileWorkKind.Dig));
    }

    // ---- the stance is per worker ---------------------------------------

    /// <summary>
    /// Standability is asked of the grid, not of the terrain table, so a worker
    /// that cannot cross water does not plan to stand in it. Nothing in the
    /// shipped data makes water claimable, but the rule must not depend on that.
    /// </summary>
    [Fact]
    public void AWorkerThatCannotCrossWaterDoesNotStandInIt()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = Dk2TerrainFixture.World();
        var at = new TilePos(5, 2);
        Put(world, database, at, Dk2TerrainFixture.Water);

        CreatureDef swimmer = Dk2TerrainFixture.Imp() with
        {
            Flags = Dk2TerrainFixture.Imp().Flags | CreatureFlags.CanWalkOnWater,
        };
        CreatureDef lubber = Dk2TerrainFixture.Imp() with
        {
            Flags = Dk2TerrainFixture.Imp().Flags & ~CreatureFlags.CanWalkOnWater,
        };

        // Water's max-health type is water, so the job "completes" to something
        // walkable for one of them and not for the other. Same tile, same job.
        Assert.True(WorkStance.IsOnTheTile(
            database, world, new WorldPathGrid(database, world, swimmer), at, TileWorkKind.Claim));
        Assert.False(WorkStance.IsOnTheTile(
            database, world, new WorldPathGrid(database, world, lubber), at, TileWorkKind.Claim));
    }

    // ---- arriving --------------------------------------------------------

    [Fact]
    public void BeingInPositionMeansDistanceZeroForFloorAndOneForWall()
    {
        (GameDatabase database, WorldState world, IPathGrid grid) = Setup();

        var floor = new TilePos(5, 2);
        Put(world, database, floor, Dk2TerrainFixture.DirtPath);
        Assert.True(WorkStance.IsInPosition(database, world, grid, floor, floor, TileWorkKind.Claim));
        Assert.False(WorkStance.IsInPosition(
            database, world, grid, new TilePos(4, 2), floor, TileWorkKind.Claim));

        var wall = new TilePos(6, 6);
        Put(world, database, wall, Dk2TerrainFixture.Rock);
        Assert.True(WorkStance.IsInPosition(
            database, world, grid, new TilePos(6, 7), wall, TileWorkKind.Reinforce));
        Assert.False(WorkStance.IsInPosition(database, world, grid, wall, wall, TileWorkKind.Reinforce));
    }
}
