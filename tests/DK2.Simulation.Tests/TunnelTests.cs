// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A hero worker digging its own way, states 186, 223 and 224 (tunnelling.md, ADR-0222).</summary>
public class TunnelTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Knight = Dk2TerrainFixture.KnightCreatureId;
    private const byte Worker = Dk2TerrainFixture.ImpCreatureId;

    [Fact]
    public void AHeroWorkerWalledOffFromTheHeart_DigsAStraightTunnelToIt()
    {
        (GameDatabase db, WorldState world) = Walled();
        CreatureState dwarf = Hero(db, world, Worker, new TilePos(3, 6));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        bool arrived = RunUntil(sim, () => dwarf.Dk2StateId == HeroJobRules.AtTheHeartState, 6000);

        Assert.True(arrived, "the worker never reached the heart");
        Assert.Contains(sink.Events, e => e.Kind == "tunnel_started" && e.Detail.Contains($"creature={dwarf.Id} "));
        for (int x = 4; x <= 9; x++)
            Assert.False(db.TerrainById(world.Tile(x, 6).TerrainId).IsSolid, $"({x},6) is still solid");
    }

    /// <summary>An impenetrable column across the line, reaching the border above: the planner follows
    /// the wall round it (<c>0x00485EF0</c> phase 1) on the hand <c>0x004868D0</c> finds open, below,
    /// and never digs the column.</summary>
    [Fact]
    public void AnImpenetrableColumnIsTunnelledRound_OnTheOpenHand_NeverThrough()
    {
        (GameDatabase db, WorldState world) = Walled();
        for (int y = 1; y <= 7; y++) world.Tile(7, y).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        CreatureState dwarf = Hero(db, world, Worker, new TilePos(3, 6));
        (GameSimulation sim, _) = Simulation(db, world);

        bool arrived = RunUntil(sim, () => dwarf.Dk2StateId == HeroJobRules.AtTheHeartState, 12000);

        Assert.True(arrived, "the worker never reached the heart");
        for (int y = 1; y <= 7; y++) Assert.Equal(Dk2TerrainFixture.ImpenetrableRock, world.Tile(7, y).TerrainId);
        Assert.False(db.TerrainById(world.Tile(7, 8).TerrainId).IsSolid, "the way round below was not dug");
    }

    [Fact]
    public void AWalledOffHeroThatIsNoWorker_FailsItsJobInsteadOfDigging()
    {
        (GameDatabase db, WorldState world) = Walled();
        CreatureState knight = Hero(db, world, Knight, new TilePos(3, 6));
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        byte wallTerrain = world.Tile(4, 6).TerrainId;
        sim.Run(2);
        Assert.Equal(HeroJobRules.WaitFailedState, knight.Dk2StateId);
        Assert.Equal(JobType.None, knight.CurrentJob);
        sim.Tick();
        Assert.Equal(ImpStates.Wandering, knight.Dk2StateId);
        sim.Run(5);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "tunnel_started");
        Assert.Equal(wallTerrain, world.Tile(4, 6).TerrainId);
    }

    /// <summary>A leader's failed route lends the errand to its worker follower, which tunnels
    /// resuming 224; the leader advances behind it in 223 and is released to idle at the end.</summary>
    [Fact]
    public void ALeaderLendsItsErrandToItsWorker_AndWalksBehindUntilItIsHandedBack()
    {
        (GameDatabase db, WorldState world) = Walled();
        world.Tile(2, 6).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState leader = Hero(db, world, Knight, new TilePos(2, 6));
        leader.PartyId = 1;
        leader.Flags = 0x02;
        CreatureState dwarf = world.SpawnCreature(db, Worker, PlayerIds.Good, new TilePos(3, 6));
        dwarf.PartyId = 1;
        dwarf.Flags = 0x04;
        HeroPartyRules.LinkFollowers(world, 1);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);

        var leaderStates = new HashSet<int>();
        var dwarfStates = new HashSet<int>();
        for (int t = 0; t < 8000 && !dwarfStates.Contains(TunnelRules.HandBackState); t++)
        {
            sim.Tick();
            leaderStates.Add(StateOf(leader));
            dwarfStates.Add(StateOf(dwarf));
        }

        Assert.Contains(sink.Events, e => e.Kind == "tunnel_started" && e.Detail.Contains($"creature={dwarf.Id} ") && e.Detail.Contains("resume=224"));
        Assert.Contains(TunnelRules.AdvancingState, leaderStates);
        Assert.Contains(TunnelRules.TunnellingState, dwarfStates);
        Assert.Contains(TunnelRules.HandBackState, dwarfStates);
        // Released to idle, the leader runs its job again, now through the tunnel.
        Assert.True(RunUntil(sim, () => leader.Dk2StateId == HeroJobRules.AtTheHeartState, 3000), "the leader never reached the heart");
    }

    /// <summary>A heart sealed in a ring of impenetrable rock: the wall-follow comes back to its hit
    /// point four times, the reach flag is cleared, and 186 sets 86, wandering (0x00485CD8, ADR-0400).</summary>
    [Fact]
    public void AWorkerThatCannotTunnelRoundASealedRingWandersIn86()
    {
        (GameDatabase db, WorldState world) = Walled();
        for (int y = 4; y <= 8; y++)
            for (int x = 9; x <= 13; x++)
                if (x is 9 or 13 || y is 4 or 8) world.Tile(x, y).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        CreatureState dwarf = Hero(db, world, Worker, new TilePos(3, 6));
        (GameSimulation sim, ListEventSink _) = Simulation(db, world);

        bool wandered = RunUntil(sim, () => StateOf(dwarf) == TunnelRules.WanderState, 12000);

        Assert.True(wandered, $"never wandered; state {dwarf.Dk2StateId}");
    }

    private static int StateOf(CreatureState c) =>
        c.Dk2StateId is StateShadowTravel.ToPoint or StateShadowTravel.ToRoom ? c.UtilityNewState : c.Dk2StateId;

    private static class StateShadowTravel
    {
        public const int ToPoint = 6;
        public const int ToRoom = 7;
    }

    private static bool RunUntil(GameSimulation sim, Func<bool> done, int limit)
    {
        for (int t = 0; t < limit; t++)
        {
            sim.Tick();
            if (done()) return true;
        }
        return false;
    }

    private static CreatureState Hero(GameDatabase db, WorldState world, byte type, TilePos at)
    {
        CreatureState hero = world.SpawnCreature(db, type, PlayerIds.Good, at);
        hero.GoodJob = JobType.KillPlayer;
        hero.GoodJobPlayerId = Keeper;
        return hero;
    }

    /// <summary>All rock inside the border but a pocket at (3,6) and a 3x3 heart at (10..12, 5..7).</summary>
    private static (GameDatabase, WorldState) Walled()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        world.Tile(3, 6).TerrainId = Dk2TerrainFixture.DirtPath;
        for (int y = 5; y <= 7; y++)
            for (int x = 10; x <= 12; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        return (db, world);
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world)
    {
        var sim = new GameSimulation(db, world, 7, [new HeroSystem(), new CreatureMovementSystem(), new TileWorkSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }
}
