// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A Combat Pit's spectators: job 26 and states 142, 143 and 144 (ADR-0227,
/// <c>combat-pit-spectators.md</c>).</summary>
public sealed class CombatPitSpectatorTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;
    private const byte Watcher = 52;
    private const byte SlowTurner = 53;
    private const int TravelToRoom = 7;
    private const int Engaging = 199;   // the fight state, category 22

    // A 5x5 pit at (4..8, 2..6): its floor is the 3x3 at (5..7, 3..5).
    private static readonly TilePos Rim = new(4, 4);
    private static readonly TilePos Outside = new(10, 4);

    private static (GameDatabase, WorldState) World()
    {
        CreatureDef watcher = Dk2TerrainFixture.Goblin() with { CreatureId = Watcher, Name = "Watching Goblin", HappyJobs = [new(JobType.CombatPitSpectate, 0, 0, 100)] };
        GameDatabase db = Dk2TerrainFixture.Database(watcher,
            watcher with { CreatureId = SlowTurner, Name = "Slow Goblin", TurnRate = 64 });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), Keeper);
        return (db, world);
    }

    private static (GameSimulation, ListEventSink) Simulation(GameDatabase db, WorldState world, ulong seed = 42)
    {
        var sim = new GameSimulation(db, world, seed,
            [new CombatPitSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static RoomInstance Pit(GameDatabase db, WorldState world) =>
        RoomIndex.RoomAt(world, db, (4 * world.Width) + 6)!;

    /// <summary>Two creatures in the fight state on the floor, which is all <c>0x004D7CF0</c> looks for.</summary>
    private static void AFight(GameDatabase db, WorldState world)
    {
        foreach ((TilePos at, byte owner) in new[] { (new TilePos(6, 4), Keeper), (new TilePos(6, 5), Rival) })
        {
            CreatureState fighter = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, owner, at);
            Assert.True(CreatureStateMachine.TrySetState(fighter, Engaging));
        }
    }

    private static void EndTheFight(WorldState world)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.Dk2StateId == Engaging) Assert.True(CreatureStateMachine.TrySetState(c, CreatureStateMachine.Idle));
    }

    private static bool OnTheFloor(GameDatabase db, WorldState world, CreatureState c) =>
        CombatPitRules.IsFloor(Pit(db, world), (c.Tile.Y * world.Width) + c.Tile.X);

    [Fact]
    public void TheJobWantsAFightInTheKeepersPit_WithNoneItIsRefused()
    {
        (GameDatabase db, WorldState world) = World();
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState watcher = world.SpawnCreature(db, Watcher, Keeper, Outside);

        for (int i = 0; i < 12; i++) sim.Tick();

        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_taken");
        Assert.Contains(sink.Events, e => e.Kind == "job_refused" && e.Detail.Contains("reason=no_fight_to_watch"));
        Assert.Equal(CreatureActivity.Idle, watcher.Activity);
    }

    /// <summary><c>0x004D8510</c> measures to each pit's <c>FirstSlab</c>, the head of its slab chain.
    /// Pit A, dragged and released at (3, 3), starts there; pit B, placed as the map places a room, at
    /// (11, 1). From (7, 4) A's head is nearer (17 to 25 tiles²), though its lowest tile (1, 1) is
    /// farther than B's.</summary>
    [Fact]
    public void ThePitFinderMeasuresToEachPitsFirstSlab_NotItsLowestTile()
    {
        CreatureDef watcherDef = Dk2TerrainFixture.Goblin() with { CreatureId = Watcher, Name = "Watching Goblin", HappyJobs = [new(JobType.CombatPitSpectate, 0, 0, 100)] };
        GameDatabase db = Dk2TerrainFixture.Database(watcherDef);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 10; y++)
        for (int x = 1; x <= 14; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        RoomDef pit = db.Room(RoomTypeId.CombatPit);
        foreach (TilePos t in SlabChain.Wave(1, 1, 3, 3))
        {
            ref TileState tile = ref world.Tile(t);
            byte terrain = tile.TerrainId, owner = tile.OwnerPlayerId;
            RoomRules.Place(db, ref tile, pit, Keeper);
            SlabChain.Changed(world, db, (t.Y * world.Width) + t.X, terrain, owner);
        }

        for (int y = 1; y <= 3; y++)
        for (int x = 11; x <= 13; x++)
            RoomRules.Place(db, ref world.Tile(x, y), pit, Keeper);

        foreach (TilePos floor in new[] { new TilePos(2, 2), new TilePos(12, 2) })
        foreach (byte owner in new[] { Keeper, Rival })
            Assert.True(CreatureStateMachine.TrySetState(world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, owner, floor), Engaging));

        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        for (int i = 0; i < 3; i++) sim.Tick();
        CreatureState watcher = world.SpawnCreature(db, Watcher, Keeper, new TilePos(7, 4));
        for (int i = 0; i < 300 && watcher.Dk2StateId != CombatPitSpectatorRules.ChoosingState; i++) sim.Tick();

        Assert.Equal(CombatPitSpectatorRules.ChoosingState, watcher.Dk2StateId);
        Assert.True(watcher.Tile.X <= 3, $"the watcher went to ({watcher.Tile.X},{watcher.Tile.Y}), not pit A");
    }

    /// <summary><c>0x004D8510</c> and <c>0x00471A70</c>: a walk into the pit resuming 142, to a rim
    /// tile (<c>0x004E4C10</c> refuses the floor).</summary>
    [Theory]
    [InlineData(1UL)] [InlineData(2UL)] [InlineData(3UL)] [InlineData(4UL)] [InlineData(5UL)]
    [InlineData(6UL)] [InlineData(7UL)] [InlineData(8UL)] [InlineData(9UL)] [InlineData(10UL)]
    public void WithAFightOnItWalksIntoThePit_ToTheRim_AndStartsIn142(ulong seed)
    {
        (GameDatabase db, WorldState world) = World();
        AFight(db, world);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world, seed);
        for (int i = 0; i < 3; i++) sim.Tick();
        CreatureState watcher = world.SpawnCreature(db, Watcher, Keeper, Outside);

        long taken = sim.World.CurrentTick.Value;
        sim.Tick();
        Assert.Contains(sink.Events, e => e.Kind == "job_taken" && e.Detail.Contains("job=CombatPitSpectate"));
        Assert.Equal(TravelToRoom, watcher.Dk2StateId);
        Assert.Equal(CombatPitSpectatorRules.ChoosingState, watcher.UtilityNewState);

        for (int i = 0; i < 200 && watcher.Dk2StateId == TravelToRoom; i++) sim.Tick();
        Assert.Equal(CombatPitSpectatorRules.ChoosingState, watcher.Dk2StateId);
        Assert.True(Pit(db, world).Contains((watcher.Tile.Y * world.Width) + watcher.Tile.X));
        Assert.False(OnTheFloor(db, world, watcher));
        Assert.Equal(taken, world.TileAtIndex(Pit(db, world).TileIndices[0]).PitFightSeenTick);   // 0x004D7CF0 stamped it
    }

    /// <summary>142 walks to a point in the middle third of a rim tile, 143 turns to the pit and
    /// cheers, 144 waits out the cheer (ours: a GUESSED length) and picks again while the fight is on.
    /// Every spot is on the rim.</summary>
    [Fact]
    public void WhileTheFightLastsItCyclesThroughASpotTheCheerAndBack_AlwaysOnTheRim_CheerLengthGuessed()
    {
        (GameDatabase db, WorldState world) = World();
        AFight(db, world);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState watcher = world.SpawnCreature(db, Watcher, Keeper, Rim);

        var seen = new HashSet<int>();
        for (int i = 0; i < 300; i++)
        {
            sim.Tick();
            seen.Add(watcher.Dk2StateId is 6 or 7 ? watcher.UtilityNewState + 1000 : watcher.Dk2StateId);
            Assert.False(OnTheFloor(db, world, watcher));
            if (i > 0) Assert.NotEqual(CreatureActivity.Idle, watcher.Activity);   // while the fight lasts, it never stops watching
        }

        Assert.Superset(new HashSet<int> { 142, 143, 144, 1143 }, seen);
        Assert.True(sink.Events.Count(e => e.Kind == "spectator_cheer") >= 3);
        Assert.Contains(sink.Events, e => e.Kind == "spectator_spot");
        Assert.Equal(JobType.CombatPitSpectate, watcher.CurrentJob);
        int sub = watcher.PosX & 0xFFF;
        Assert.InRange(sub, 0x555, 0xAA9);
    }

    /// <summary>143 turns toward the pit's middle before it cheers, waiting while it turns: a slow
    /// turner holds 143 for several turns, and its heading ends on the bearing to the middle tile.</summary>
    [Fact]
    public void BeforeItCheersItTurnsToFaceThePitsMiddle_WaitingWhileItTurns()
    {
        (GameDatabase db, WorldState world) = World();
        AFight(db, world);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState watcher = world.SpawnCreature(db, SlowTurner, Keeper, Rim);
        watcher.Heading = DK2.Simulation.Steering.Bearing.Of(-0x1000, 0);   // facing away, to the west

        int facing = 0;
        for (int i = 0; i < 300 && !sink.Events.Any(e => e.Kind == "spectator_cheer"); i++)
        {
            sim.Tick();
            if (watcher.Dk2StateId == CombatPitSpectatorRules.FacingState) facing++;
        }
        Assert.Contains(sink.Events, e => e.Kind == "spectator_cheer");
        Assert.True(facing >= 3, $"143 held for {facing} turns");
        int toTheMiddle = DK2.Simulation.Steering.Bearing.Of(CreatureState.CentreOf(6) - watcher.PosX, CreatureState.CentreOf(4) - watcher.PosY);
        Assert.Equal(0, DK2.Simulation.Steering.Bearing.Distance(watcher.Heading, toTheMiddle));
    }

    /// <summary>144 idles once no fight has been seen for more than four game seconds, and not before.</summary>
    [Fact]
    public void FourSecondsAfterTheLastFightTheSpectatorGoesIdle_NotBefore()
    {
        (GameDatabase db, WorldState world) = World();
        AFight(db, world);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState watcher = world.SpawnCreature(db, Watcher, Keeper, Rim);
        ref TileState stamp = ref world.TileAtIndex(Pit(db, world).TileIndices[0]);

        // Until a 144 has just looked and seen the fight, then the fight ends.
        for (int i = 0; i < 400 && !(watcher.Dk2StateId == CombatPitSpectatorRules.ChoosingState && stamp.PitFightSeenTick == sim.World.CurrentTick.Value - 1); i++)
            sim.Tick();
        Assert.Equal(sim.World.CurrentTick.Value - 1, stamp.PitFightSeenTick);
        EndTheFight(world);

        long quietUntil = stamp.PitFightSeenTick + 16;   // variable 19 x 4: four game seconds
        while (sim.World.CurrentTick.Value <= quietUntil)
        {
            sim.Tick();
            Assert.NotEqual(CreatureActivity.Idle, watcher.Activity);
        }

        for (int i = 0; i < 100 && watcher.Activity != CreatureActivity.Idle; i++) sim.Tick();
        Assert.Equal(CreatureActivity.Idle, watcher.Activity);
        Assert.Equal(JobType.None, watcher.CurrentJob);
    }

    /// <summary>144's test exactly: a spectator whose cheer is over, standing where it last looked,
    /// with no fight on, idles when the last fight was seen more than 16 turns ago and picks a new
    /// spot at 16 (<c>var19 × 4 &lt; now − stamp</c>).</summary>
    [Theory]
    [InlineData(16, CombatPitSpectatorRules.ChoosingState)]
    [InlineData(17, CreatureStateMachine.Idle)]
    public void AQuietPitSendsTheSpectatorIdle_OnlyAfterSixteenTurns(int ago, int expected)
    {
        (GameDatabase db, WorldState world) = World();
        (GameSimulation sim, _) = Simulation(db, world);
        for (int i = 0; i < 20; i++) sim.Tick();
        CreatureState watcher = world.SpawnCreature(db, Watcher, Keeper, Rim);
        Assert.True(CreatureStateMachine.TrySetState(watcher, CombatPitSpectatorRules.ChoosingState));
        Assert.True(CreatureStateMachine.TrySetState(watcher, CombatPitSpectatorRules.CheeringState));
        watcher.CurrentJob = JobType.CombatPitSpectate;
        watcher.PitStateEnteredTick = sim.World.CurrentTick.Value - 5;
        watcher.SpectatorPosX = watcher.PosX;
        watcher.SpectatorPosY = watcher.PosY;
        world.TileAtIndex(Pit(db, world).TileIndices[0]).PitFightSeenTick = sim.World.CurrentTick.Value - ago;

        sim.Tick();

        Assert.Equal(expected, watcher.Dk2StateId);
    }

    /// <summary>142 moves a spectator that shares its tile to a free rim tile next to it, whatever
    /// the roll.</summary>
    [Fact]
    public void TwoSpectatorsOnOneTile_EachPicksAFreeRimTileNextToIt()
    {
        (GameDatabase db, WorldState world) = World();
        AFight(db, world);
        (GameSimulation sim, ListEventSink sink) = Simulation(db, world);
        CreatureState a = world.SpawnCreature(db, Watcher, Keeper, Rim);
        CreatureState b = world.SpawnCreature(db, Watcher, Keeper, Rim);

        sim.Tick();   // job 26 on the rim with a fight on: 142 where they stand
        Assert.Equal(CombatPitSpectatorRules.ChoosingState, a.Dk2StateId);
        Assert.Equal(CombatPitSpectatorRules.ChoosingState, b.Dk2StateId);
        sim.Tick();

        foreach (CreatureState c in new[] { a, b })
        {
            string spot = sink.Events.First(e => e.Kind == "spectator_spot" && e.Detail.StartsWith($"creature={c.Id} ", StringComparison.Ordinal)).Detail;
            string[] xy = spot[(spot.IndexOf("to=(", StringComparison.Ordinal) + 4)..^1].Split(',');
            var at = new TilePos(int.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, Math.Abs(at.X - Rim.X) + Math.Abs(at.Y - Rim.Y));
            Assert.False(CombatPitRules.IsFloor(Pit(db, world), (at.Y * world.Width) + at.X));
            Assert.True(Pit(db, world).Contains((at.Y * world.Width) + at.X));
        }
    }

    /// <summary>DK2's gate: the Spectating category's entry mask <c>0x8002383B</c> lacks the Combat
    /// Pit's bit, so 138's rim arm (<c>SetState(142)</c>) is refused and 138 returns 0, idle.
    /// Idle may still take job 26, which enters 142 from state 1.</summary>
    [Fact]
    public void TheGateRefuses142FromAPitState_AndAdmitsItFromIdle()
    {
        (GameDatabase db, WorldState world) = World();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, Rim);
        Assert.True(CreatureStateMachine.TrySetState(goblin, CombatPitRules.PlacingState));
        Assert.Equal(StateTransition.Refused, CreatureStateMachine.Classify(goblin, CombatPitSpectatorRules.ChoosingState));
        Assert.True(CreatureStateMachine.TrySetState(goblin, CreatureStateMachine.Idle));
        Assert.Equal(StateTransition.Allowed, CreatureStateMachine.Classify(goblin, CombatPitSpectatorRules.ChoosingState));
    }
}
