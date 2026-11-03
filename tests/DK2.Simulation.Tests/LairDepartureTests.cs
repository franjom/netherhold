// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class LairDepartureTests
{
    // 0046F422 -> 00471A70 -> 004E4C10: one random slab-chain point, not the centre.
    [Theory]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(42u)]
    public void OutsideDepartureUsesOneRoomPointDraw(uint seed)
    {
        var (sim, creature, attempt) = Setup(new TilePos(2, 2), seed);
        RoomInstance room = RoomIndex.Build(sim.World, sim.Database).Single();
        var expected = new Dk2Lcg(((Dk2Lcg)sim.Rng.Algorithm).Seed);
        int tile = SlabChain.Order(sim.World, room)[expected.NextInt(room.TileCount)];
        sim.Tick();
        Assert.True(attempt.Succeeded);
        Assert.Equal(CreatureState.CentreOf(tile % sim.World.Width) << 4, creature.Body.PathTargetPos.X);
        Assert.Equal(CreatureState.CentreOf(tile / sim.World.Width) << 4, creature.Body.PathTargetPos.Y);
        Assert.Equal(expected.Seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(7, creature.Dk2StateId);
        Assert.Equal(80, creature.UtilityNewState);
    }

    // 0046F418 pushes E8; 004D0F60 turns it into body path_flags 13.
    [Fact]
    public void OutsideDepartureUsesTightSnappingArrival()
    {
        var (sim, creature, attempt) = Setup(new TilePos(2, 2), 1);
        sim.Tick();
        Assert.True(attempt.Succeeded);
        Assert.Equal(MoveOptions.ExactSpot, creature.RouteOptions);
        Assert.Equal(13, creature.Body.PathFlags);
    }

    // 00471AAA-AD2: membership shortcut before the point draw, even off the centre.
    [Fact]
    public void InsideDepartureDoesNotRouteDrawOrEndTheExistingAction()
    {
        var (sim, creature, attempt) = Setup(new TilePos(6, 1), 1);
        uint seed = ((Dk2Lcg)sim.Rng.Algorithm).Seed;
        creature.UtilityNewState = 123;
        creature.Body.PathTargetPos = new(12345, 67890);
        creature.Body.PathFlags = 2;
        creature.ActionUntilTick = 100;
        sim.Tick();
        Assert.True(attempt.Succeeded);
        Assert.Equal(80, creature.Dk2StateId);
        Assert.Equal(123, creature.UtilityNewState);
        Assert.Equal(seed, ((Dk2Lcg)sim.Rng.Algorithm).Seed);
        Assert.Equal(new Fixed16Vector(12345, 67890), creature.Body.PathTargetPos);
        Assert.Equal(2, creature.Body.PathFlags);
        Assert.Equal(100, creature.ActionUntilTick);
    }

    private static (GameSimulation, CreatureState, Attempt) Setup(TilePos at, uint seed)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)3]);
        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 8; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = 3;
                if (x >= 6) RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Lair), 3);
            }
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 3, at);
        var attempt = new Attempt(creature);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [attempt]);
        return (sim, creature, attempt);
    }

    private sealed class Attempt(CreatureState creature) : ISimulationSystem
    {
        public string Name => "lair_departure_test";
        public bool Succeeded { get; private set; }
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            Succeeded = new LairSeeker(new GridPathfinder()).TrySetOutForALair(
                context, Name, creature, context.Database.CreatureById(creature.CreatureTypeId));
        }
    }
}
