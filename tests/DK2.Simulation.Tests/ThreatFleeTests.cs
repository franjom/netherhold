// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;
using Xunit;

namespace DK2.Simulation.Tests;

/// <summary><c>Creature_Update</c>'s flight from its <c>ThreatThing</c> (<c>0x00489B42</c>) and state 221
/// (ADR-0678).</summary>
public sealed class ThreatFleeTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Me = new(8, 6);

    /// <summary>Afraid, with a live thing a tile away on both axes: one try of <c>random(4)</c> twice
    /// finds a point (the open floor holds every point), then idle and the walk to 221; the thing is
    /// cleared.</summary>
    [Fact]
    public void AnAfraidCreatureNearItsThreatWalksAwayResuming221()
    {
        (GameDatabase db, WorldState world, CreatureState me) = Open();
        ObjectState thing = world.SpawnObject(db, Navigation.WorldSteering.DungeonHeartObstacleObjectId, PlayerIds.Good, (7 * world.Width) + 9);
        Place(thing, 9, 7);
        ThreatFleeRules.Record(me, 10, ThreatSource.OfObject(thing));
        (uint before, uint after) = Run(db, world, me, fear: 60000);

        var expected = new Dk2Lcg(before);
        expected.NextInt(4);
        expected.NextInt(4);
        Assert.Equal(expected.Seed, after);
        Assert.Equal(6, me.Dk2StateId);   // StateShadow.TravelToPoint
        Assert.Equal(ThreatFleeRules.FledState, me.UtilityNewState);
        Assert.Equal(ThreatSource.None, me.ThreatThingKind);
    }

    /// <summary>Under the threshold nothing happens and the thing is kept; at 25 tiles squared or more it
    /// is cleared without a draw.</summary>
    [Theory]
    [InlineData(0, 9, 7, true)]
    [InlineData(60000, 14, 6, false)]
    public void NoFlightBelowTheThresholdOrBeyondTheReach(int fear, int tx, int ty, bool kept)
    {
        (GameDatabase db, WorldState world, CreatureState me) = Open();
        ObjectState thing = world.SpawnObject(db, Navigation.WorldSteering.DungeonHeartObstacleObjectId, PlayerIds.Good, (ty * world.Width) + tx);
        Place(thing, tx, ty);
        ThreatFleeRules.Record(me, 10, ThreatSource.OfObject(thing));
        (uint before, uint after) = Run(db, world, me, fear);

        Assert.Equal(before, after);
        Assert.Equal(kept ? ThreatSource.Object : ThreatSource.None, me.ThreatThingKind);
        Assert.Equal(CreatureStateMachine.Idle, me.Dk2StateId);
    }

    /// <summary>A live creature as the source (the fear accumulator's record) is cleared with no flight,
    /// however near: DK2's class-0 test (<c>0x00489C53</c>).</summary>
    [Fact]
    public void ACreatureSourceIsClearedWithoutAFlight()
    {
        (GameDatabase db, WorldState world, CreatureState me) = Open();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Good, new TilePos(9, 7));
        ThreatFleeRules.Record(me, 10, ThreatSource.OfCreature(enemy));
        (uint before, uint after) = Run(db, world, me, fear: 60000);

        Assert.Equal(before, after);
        Assert.Equal(ThreatSource.None, me.ThreatThingKind);
        Assert.Equal(CreatureStateMachine.Idle, me.Dk2StateId);
    }

    /// <summary>A threat names its source only when it is positive.</summary>
    [Fact]
    public void OnlyAPositiveThreatNamesItsSource()
    {
        var me = new CreatureState();
        ThreatFleeRules.Record(me, 0, new ThreatSource(ThreatSource.Trap, 5));
        Assert.Equal(ThreatSource.None, me.ThreatThingKind);
        ThreatFleeRules.Record(me, 1, new ThreatSource(ThreatSource.Trap, 5));
        Assert.Equal((ThreatSource.Trap, 5), (me.ThreatThingKind, me.ThreatThingId));
    }

    /// <summary>221's handler returns 0: a creature in it is idle the next turn.</summary>
    [Fact]
    public void State221IdlesTheCreature()
    {
        (GameDatabase db, WorldState world, CreatureState me) = Open();
        CreatureStateMachine.TrySetState(new StateHookContext(world, db), me, ThreatFleeRules.FledState);
        GameSimulation sim = new(db, world, new SimRandom(new Dk2Lcg(42)),
            [new Probe(ctx => ThreatFleeRules.Dispatch(ctx, "test", me))]);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, me.Dk2StateId);
    }

    /// <summary>A thing at a tile centre, as the Boulder keeps its own position.</summary>
    private static void Place(ObjectState thing, int x, int y)
    {
        thing.PosX = CreatureState.CentreOf(x);
        thing.PosY = CreatureState.CentreOf(y);
    }

    private static (uint Before, uint After) Run(GameDatabase db, WorldState world, CreatureState me, int fear)
    {
        uint before = 0, after = 0;
        new GameSimulation(db, world, new SimRandom(new Dk2Lcg(42)), [new Probe(ctx =>
        {
            me.CurrentFear = (ushort)fear;
            ctx.Navigation.Refresh();   // the map was dug open after the grid was built
            before = ((Dk2Lcg)ctx.Rng.Algorithm).Seed;
            ThreatFleeRules.Turn(ctx, new WorldSight(ctx.Navigation, ctx.World, ctx.Database), me, ctx.Database.CreatureById(me.CreatureTypeId));
            after = ((Dk2Lcg)ctx.Rng.Algorithm).Seed;
        })]).Tick();
        return (before, after);
    }

    private static (GameDatabase, WorldState, CreatureState) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Me);
        Dk2TerrainFixture.SetKnownIdentities(world);
        me.Flags |= (byte)CreatureFightFlags.Attackable;   // 0x0045A8B0's bit, recomputed by the update in play
        return (db, world, me);
    }

    private sealed class Probe(Action<SimulationContext> act) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => act(context);
    }
}
