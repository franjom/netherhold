// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0294: a job walk ends when the route's progress is exactly 1.0, near its point,
/// not on entering the tile; and an imp in selection or at work is not moved
/// (imp-task-objects-and-walks.md §3, §4).</summary>
public class ImpWalkEndTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    /// <summary>The keeper's claimed room at (1..3, 1..3) and a dirt path east along row 2.</summary>
    private static (GameSimulation Sim, CreatureState Imp) Start(int pathEndX, TilePos impAt)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int x = 4; x <= pathEndX; x++)
        {
            ref TileState path = ref world.Tile(x, 2);
            path.TerrainId = Dk2TerrainFixture.DirtPath;
            path.OwnerPlayerId = PlayerIds.Unowned;
            path.Health = database.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);
        return (new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems()), imp);
    }

    /// <summary>
    /// <b>Claim follows claim every 13 ticks.</b> DK2's Level 1 capture: 41 of 49 claims of a
    /// neighbouring tile by the same imp began 13 ticks after the one before, the walk landing on
    /// the centre on its third frame by the integrator's snap (path flag bit 2, which the claim's
    /// option word 0xE0 sets; imp-walk-arrival.md). Without the snap ours overshot and took 15.
    /// </summary>
    [Fact]
    public void ANeighbouringClaimBeginsThirteenTicksAfterTheLast()
    {
        (GameSimulation sim, CreatureState imp) = Start(pathEndX: 9, new TilePos(3, 2));
        var starts = new List<long>();
        int last = imp.Dk2StateId;
        for (int t = 0; t < 120; t++)
        {
            sim.Tick(InputFrame.Empty);
            if (imp.Dk2StateId == 15 && last != 15) starts.Add(sim.CurrentTick.Value);
            last = imp.Dk2StateId;
        }

        Assert.True(starts.Count >= 4, $"claims begun: {starts.Count}");
        Assert.All(starts.Zip(starts.Skip(1), (a, b) => b - a), gap => Assert.Equal(13, gap));
    }

    /// <summary>
    /// <b>A claim walk ends near the tile's centre.</b> The walk resumes only at progress 1.0: the
    /// point reached, or within a sixteenth of a tile; so the claimer stands by the centre, never
    /// at the tile's edge.
    /// </summary>
    [Fact]
    public void AClaimWalkEndsNearTheTilesCentre()
    {
        (GameSimulation sim, CreatureState imp) = Start(pathEndX: 4, new TilePos(2, 2));
        for (int t = 0; t < 40 && imp.Dk2StateId != 15; t++) sim.Tick(InputFrame.Empty);

        Assert.Equal(15, imp.Dk2StateId);
        long cx = 4 * 4096 + 2048, cy = 2 * 4096 + 2048;
        Assert.InRange(Math.Abs(imp.PosX - cx) + Math.Abs(imp.PosY - cy), 0, 4096 / 8);
    }

    /// <summary>
    /// <b>An imp in selection does not drift on a route it still holds.</b> DK2's creature update
    /// never moves an imp (type 1) outside states 6, 7 and 265, and a kick leaves the route in
    /// place (imp-task-objects-and-walks.md §3): 25 of 25 kicked walkers in the captures stood still.
    /// </summary>
    [Fact]
    public void AnImpInSelectionDoesNotMoveAlongARouteItStillHolds()
    {
        (GameSimulation full, CreatureState imp) = Start(pathEndX: 9, new TilePos(3, 2));
        WorldState world = full.World;
        Assert.True(new Navigation.WorldSteering(full.Database, world).TryRoute(imp, new TilePos(9, 2), Navigation.MoveOptions.Plain));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, full.Database), imp, ImpStates.TaskSelection));
        var sim = new GameSimulation(full.Database, world, 1, [new Systems.CreatureMovementSystem()]);
        long x = imp.PosX, y = imp.PosY;

        sim.Run(4);

        Assert.Equal((x, y), (imp.PosX, imp.PosY));
    }
}
